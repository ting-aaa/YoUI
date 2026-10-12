# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""No-display/fake tests of the X11 acceptance runner; never native-window evidence."""
from __future__ import annotations

import argparse
import contextlib
import copy
import ctypes
import importlib.util
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import zlib

SPEC = importlib.util.spec_from_file_location("accept_x11", Path(__file__).with_name("accept_x11.py"))
accept = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(accept)
SHA = "0123456789abcdef0123456789abcdef01234567"


def state():
    return dict(xid=12345, adapter="Fake GPU (Vulkan; Cpu; driver fixture)", sdlVersion="2.26.5",
                width=640, height=400, frames=1, clicks=0, text="", focus=None, hasInputFocus=False,
                suspended=False, closeRequests=0, closed=False, pointerCancels=0,
                threadGuard=True, duplicateGuard=True, eventCounts={},
                button=dict(x=24, y=110, width=160, height=44),
                textbox=dict(x=24, y=170, width=440, height=40))


def png(width=640, height=400):
    def chunk(kind, value):
        return struct.pack(">I", len(value)) + kind + value + struct.pack(">I", zlib.crc32(kind + value) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress((b"\0" + b"\0" * width * 3) * height)) + chunk(b"IEND", b""))


class FakeDriver:
    """A deliberately fake OS-input model to cover orchestration, never SDL/X11."""
    def __init__(self, *, cancel=True):
        self.state = state()
        self.x11 = self
        self.inputs, self.checks = [], []
        self.pointer, self.down, self.selected = None, False, False
        self.cancel = cancel
        self.finished = False
        self.wm = 1

    def increment(self, event):
        self.state["eventCounts"][event] = accept.count(self.state, event) + 1

    def wait(self, name, predicate, **_):
        if not self.state["suspended"] and not self.state["closed"]:
            self.state["frames"] += 1
        matched = False
        if name == "WM restored pointer targets activated client":
            # Exercise the observed stable-readiness interval without real time.
            with patch.object(accept.time, "monotonic", side_effect=[100., 100.05, 100.2]):
                for _ in range(3):
                    matched = predicate(self.state)
                    if matched:
                        break
        else:
            matched = predicate(self.state)
        if not matched:
            raise ValueError("Fake condition failed: " + name)
        self.checks.append(name)
        return copy.deepcopy(self.state)

    def input(self, *args):
        self.inputs.append(args)
        kind = args[0]
        if kind in ("windowfocus", "windowactivate"):
            self.state["hasInputFocus"] = True
        elif kind == "click":
            if args[1] in ("4", "5"):
                self.increment("Scroll")
            else:
                self.increment("PointerDown")
                self.increment("PointerUp")
                self.state["focus"] = self.pointer
                self.state["clicks"] += self.pointer == "button"
        elif kind == "mousedown":
            self.increment("PointerDown")
            self.down = True
            self.state["focus"] = self.pointer
        elif kind == "mouseup":
            if self.down:
                self.increment("PointerUp")
                self.state["clicks"] += self.pointer == "button"
            self.down = False
        elif kind == "mousemove":
            self.pointer = None
            self.increment("PointerMove")
        elif kind == "type":
            self.state["text"] = args[-1] if self.selected else self.state["text"] + args[-1]
            self.selected = False
            self.increment("TextInput")
        elif kind == "key":
            self.increment("KeyDown")
            if args[-1] == "ctrl+a":
                self.selected = True
            elif args[-3:] == ("End", "Left", "BackSpace"):
                text = self.state["text"]
                self.state["text"] = text[:-2] + text[-1:]
            elif args[-1] == "Tab":
                self.state["focus"] = "button"
            elif args[-1] == "shift+Tab":
                self.state["focus"] = "textbox"
        elif kind == "windowsize":
            self.state["width"], self.state["height"] = int(args[-2]), int(args[-1])
        elif kind == "windowunmap":
            self.state["suspended"] = True
        elif kind == "windowmap":
            self.state["suspended"] = False
            self.wm = 1
        elif kind == "windowminimize":
            self.wm = 3
            self.state["suspended"] = True
            self.state["hasInputFocus"] = False
        elif kind == "windowraise":
            pass
        else:
            raise AssertionError("Unhandled fake OS action: " + repr(args))

    def point(self, element):
        self.inputs.append(("point", element))
        self.pointer = element
        self.increment("PointerMove")

    def focus_away(self):
        self.inputs.append(("XSetInputFocus",))
        self.state["hasInputFocus"] = False
        if self.cancel:
            self.state["pointerCancels"] += int(self.down)
            self.down = False

    def input_snapshot(self, xid):
        rect = self.state["button"]
        return dict(xid=xid, wmState=self.wm, mapState=2, inputFocus=xid, focusRevert=2,
            focusSink=99, activeWindow=xid, clientOrigin=[10, 20], pointerPath=[1, 2, xid],
            pointerRoot=[10 + round(rect["x"] + rect["width"] / 2),
                         20 + round(rect["y"] + rect["height"] / 2)], pointerMask=0)

    def diagnose_input(self, label):
        self.inputs.append(("diagnostic", label))

    def request_close(self, xid):
        assert xid == self.state["xid"]
        self.inputs.append(("WM_DELETE_WINDOW",))
        self.state["closeRequests"] += 1
        self.state["closed"] = self.state["closeRequests"] == 2

    def outside_point(self, _):
        return 0, 0

    def pause(self, seconds):
        self.inputs.append(("pause", seconds))

    def finish(self):
        self.finished = True

    def capture(self):
        return dict(method="fixture only; no native image", distinctColorsAtLeast=20)

    def wait_wm_state(self, xid, expected):
        if self.wm != expected:
            raise ValueError("fixture WM state mismatch")
        return dict(xid=xid, wmState=self.wm, mapState=2 if self.wm == 1 else 0)


class Fixture:
    def __init__(self, root, failure=None):
        self.root, self.failure = root, failure
        self.commands, self.sessions = [], []
        for filename in ("global.json", "rust-toolchain.toml"):
            (root / filename).write_text("fixture; not toolchain evidence", encoding="utf-8")

    def run(self, command, *, root, output, name, env, timeout, report):
        self.commands.append((command, env, timeout))
        if name == self.failure:
            raise ValueError("fixture failure at " + name)
        text = "fixture version; not real tool evidence"
        if name.startswith("git-sha"):
            text = SHA
        elif name.startswith("git-dirty"):
            text = "?? tracked fixture"
        elif name == "build-managed":
            configuration = command[command.index("-c") + 1]
            folder = root / "samples/YoUI.Linux.Sample/bin" / configuration / "net10.0"
            native = root / "target" / configuration.lower()
            folder.mkdir(parents=True)
            native.mkdir(parents=True)
            for filename in ("YoUI.Linux.Sample.dll", "YoUI.Platform.Linux.dll", "YoUI.dll", "libyoui_render.so"):
                (folder / filename).write_bytes(b"fixture binary, never loaded")
            (native / "libyoui_render.so").write_bytes(b"fixture binary, never loaded")
        record = dict(name=name, command=command, log=name + ".log", status="passed")
        report["commands"].append(record)
        (output / record["log"]).write_text(text, encoding="utf-8")
        return record

    def session(self, args, root, output, env, report, x11):
        fixture = self

        class Session:
            def start(self):
                folder = output / "sample"
                folder.mkdir(exist_ok=False)
                final = state()
                final.update(closed=True, closeRequests=2)
                (folder / "state.json").write_text(json.dumps(final), encoding="utf-8")
                (folder / "frame.png").write_bytes(png())
                (folder / "lifecycle.json").write_text('{"reopened":true,"frames":1}', encoding="utf-8")

            def cleanup(self):
                self.cleaned = True

            cleaned = False

        result = Session()
        fixture.sessions.append(result)
        return result


class AcceptanceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="youi-x11-fake-")
        self.addCleanup(temporary.cleanup)
        self.base = Path(temporary.name)
        self.root = self.base / "repo with spaces"
        self.root.mkdir()
        self.args = argparse.Namespace(output=self.base / "fresh evidence", configuration="Release", backend="vulkan",
                                       build_timeout_seconds=20, window_timeout_seconds=10,
                                       event_timeout_seconds=2, xdotool="fixture xdotool")

    def fixture_run(self, fixture, cleanup_error=None):
        final = state()
        final.update(closed=True, closeRequests=2)
        result = dict(**accept.adapter_evidence(final["adapter"]), finalState=final)
        with patch.dict(os.environ, {"DISPLAY": ":fake"}), patch.object(accept.platform, "system", return_value="Linux"), \
             patch.object(accept, "X11Worker") as x11, patch.object(accept, "run_command", side_effect=fixture.run), \
             patch.object(accept, "WindowSession", side_effect=fixture.session), \
             patch.object(accept, "exercise_window", return_value=result) as exercise, contextlib.redirect_stdout(io.StringIO()):
            x11.return_value.library = "fixture libX11"
            x11.return_value.close.return_value = cleanup_error
            with patch.dict(os.environ):
                os.environ.pop("RUSTUP_TOOLCHAIN", None)
                code = accept.run_acceptance(self.args, self.root)
            x11.return_value.close.assert_called_once()
            if code == 0:
                self.assertIs(exercise.call_args.kwargs["require_wm"], getattr(self.args, "require_wm", False))
        return code, json.loads((self.args.output / "result.json").read_text(encoding="utf-8"))

    def test_requested_wm_mode_is_forwarded_and_recorded(self):
        self.args.require_wm = True
        code, report = self.fixture_run(Fixture(self.root))
        self.assertEqual(code, 0)
        self.assertIs(report["requireWindowManager"], True)

    def test_matching_profiles_native_before_managed_and_metadata(self):
        for configuration in ("Debug", "Release"):
            with self.subTest(configuration=configuration):
                self.args.configuration = configuration
                self.args.output = self.base / (configuration + " fresh")
                fixture = Fixture(self.root)
                code, result = self.fixture_run(fixture)
                self.assertEqual(code, 0, result)
                self.assertEqual(result["status"], "passed")
                native = next(command for command, _, _ in fixture.commands if command[:2] == ["cargo", "build"])
                managed = next(command for command, _, _ in fixture.commands if command[:2] == ["dotnet", "build"])
                commands = [command for command, _, _ in fixture.commands]
                self.assertLess(commands.index(native), commands.index(managed))
                self.assertEqual("--release" in native, configuration == "Release")
                self.assertIn("--locked", native)
                self.assertEqual(native[native.index("--target-dir") + 1], str(self.root / "target"))
                self.assertEqual(managed[managed.index("-c") + 1], configuration)
                self.assertEqual(result["source"]["sha"], SHA)
                self.assertEqual(result["source"], result["sourceAfter"])
                self.assertEqual(result["adapter"]["classification"], "software")
                self.assertTrue(result["lifecycle"]["reopened"])
                self.assertTrue(fixture.sessions[0].cleaned)
                self.assertEqual(len(result["binaryEvidence"]), 5)
                self.assertEqual({item["path"] for item in result["artifacts"]},
                                 {"sample/frame.png", "sample/state.json", "sample/lifecycle.json"})
                self.assertTrue(all(len(item["sha256"]) == 64 for item in result["artifacts"]))
                for _, env, timeout in fixture.commands:
                    self.assertEqual(env["YOUI_BACKEND"], "vulkan")
                    self.assertEqual(env["SDL_VIDEODRIVER"], "x11")
                    self.assertGreater(timeout, 0)

    def test_no_display_fails_without_native_tools_or_build(self):
        with patch.dict(os.environ), patch.object(accept, "X11Worker") as x11, \
             patch.object(accept.subprocess, "Popen") as popen, contextlib.redirect_stdout(io.StringIO()):
            os.environ.pop("DISPLAY", None)
            code = accept.run_acceptance(self.args, self.root)
        self.assertEqual(code, 1)
        x11.assert_not_called()
        popen.assert_not_called()
        result = json.loads((self.args.output / "result.json").read_text())
        self.assertIn("DISPLAY is required", result["error"])
        self.assertEqual(result["checks"], [])

    def test_unopenable_display_fails_before_build(self):
        with patch.dict(os.environ, {"DISPLAY": ":fixture"}), patch.object(accept, "X11Worker", side_effect=ValueError("Cannot open display")), \
             patch.object(accept.subprocess, "Popen") as popen, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(accept.run_acceptance(self.args, self.root), 1)
        popen.assert_not_called()

    def test_existing_even_empty_output_is_rejected(self):
        self.args.output.mkdir()
        with patch.object(accept, "X11Worker") as x11, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(accept.run_acceptance(self.args, self.root), 2)
        x11.assert_not_called()
        self.assertEqual(list(self.args.output.iterdir()), [])

    def test_build_failure_keeps_report_and_prevents_window(self):
        fixture = Fixture(self.root, failure="build-native")
        code, result = self.fixture_run(fixture)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "failed")
        self.assertIn("build-native", result["error"])
        self.assertEqual(fixture.sessions, [])
        self.assertFalse(any(command[:2] == ["dotnet", "build"] for command, _, _ in fixture.commands))

    def test_xlib_shutdown_failure_cannot_leave_an_acceptance_pass(self):
        code, result = self.fixture_run(Fixture(self.root), cleanup_error="fixture hung Xlib close")
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "failed")
        self.assertIn("Xlib cleanup failed", result["error"])

    def test_cli_requires_explicit_configuration_and_vulkan(self):
        for arguments in ([], ["--backend", "vulkan"], ["--configuration", "Release"],
                          ["--configuration", "Release", "--backend", "gl"]):
            with self.subTest(arguments=arguments), self.assertRaises(SystemExit) as exc, contextlib.redirect_stderr(io.StringIO()):
                accept.main(arguments)
            self.assertEqual(exc.exception.code, 2)

    def test_cli_wm_mode_defaults_off_and_is_explicit(self):
        for extra, expected in (([], False), (["--require-wm"], True)):
            with patch.object(accept, "run_acceptance", return_value=0) as run:
                self.assertEqual(accept.main(["--configuration", "Release", "--backend", "vulkan", *extra]), 0)
            self.assertIs(run.call_args.args[0].require_wm, expected)

    def test_actual_cli_no_display_creates_failed_evidence(self):
        environment = dict(os.environ)
        environment.pop("DISPLAY", None)
        result = subprocess.run([sys.executable, str(Path(accept.__file__)), "--configuration", "Debug", "--backend", "vulkan",
                                 "--output", str(self.args.output)], env=environment, capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 1, result.stderr)
        report = json.loads((self.args.output / "result.json").read_text())
        self.assertEqual(report["commands"], [])
        self.assertEqual(report["status"], "failed")


class DriverTests(unittest.TestCase):
    def test_wm_mode_requires_iconic_normal_transitions_and_restored_click(self):
        session = FakeDriver()
        result = accept.exercise_window(session, require_wm=True)
        self.assertEqual(result["windowManager"], dict(required=True, transitions=[
            dict(xid=12345, wmState=s, mapState=m) for s, m in ((1, 2), (3, 0), (1, 2))]))
        self.assertIn(("windowminimize", "12345"), session.inputs)
        self.assertEqual(result["finalState"]["clicks"], 3)
        self.assertIn("WM restored window accepts input", session.checks)

    def test_activation_and_target_preconditions_precede_one_real_click(self):
        session = FakeDriver()
        accept.exercise_window(session, require_wm=True)
        before = session.inputs.index(("diagnostic", "wm-restored-before-activation"))
        activation = session.inputs.index(("windowactivate", "--sync", "12345"))
        capture = session.inputs.index(("diagnostic", "wm-restored-before-click"))
        self.assertLess(before, activation)
        self.assertLess(activation, capture)
        self.assertEqual(session.inputs[capture + 1], ("click", "1"))
        self.assertNotIn(("windowfocus", "--sync", "12345"), session.inputs[before:])

    def test_occlusion_wrong_focus_coordinates_or_held_button_prevent_click(self):
        for changed in (dict(pointerPath=[1, 99]), dict(activeWindow=99), dict(inputFocus=99),
                        dict(pointerRoot=[0, 0]), dict(pointerMask=256), dict(wmState=3), dict(mapState=0)):
            with self.subTest(changed=changed):
                session = FakeDriver()
                observation = session.input_snapshot(12345)
                observation.update(changed)
                with patch.object(session, "input_snapshot", return_value=observation):
                    with self.assertRaisesRegex(ValueError, "targets activated client"):
                        accept.exercise_window(session, require_wm=True)
                self.assertEqual(session.state["clicks"], 2)
                self.assertEqual(session.inputs[-1], ("diagnostic", "wm-restored-input-failure"))

    def test_readiness_interval_restarts_after_target_loss(self):
        session = FakeDriver()
        original = session.wait
        observation = session.input_snapshot(12345)
        observations = [observation, {**observation, "activeWindow": 99}, observation, observation, observation]
        def wait(name, predicate, **kwargs):
            if name != "WM restored pointer targets activated client":
                return original(name, predicate, **kwargs)
            with patch.object(session, "input_snapshot", side_effect=observations), \
                    patch.object(accept.time, "monotonic", side_effect=[100., 100.2, 100.25, 100.4]):
                self.assertEqual([predicate(session.state) for _ in range(5)], [False, False, False, False, True])
            session.checks.append(name)
            return copy.deepcopy(session.state)
        with patch.object(session, "wait", side_effect=wait):
            accept.exercise_window(session, require_wm=True)
        self.assertTrue(session.finished)

    def test_restored_click_requires_down_up_and_focus_not_just_click_counter(self):
        for defect in ("PointerDown", "PointerUp", "focus"):
            session = FakeDriver()
            original = session.input
            def input_(*args):
                original(*args)
                if args == ("click", "1") and ("windowactivate", "--sync", "12345") in session.inputs:
                    if defect == "focus":
                        session.state["hasInputFocus"] = False
                    else:
                        session.state["eventCounts"][defect] -= 1
            with patch.object(session, "input", side_effect=input_):
                with self.assertRaisesRegex(ValueError, "WM restored window accepts input"):
                    accept.exercise_window(session, require_wm=True)
            self.assertFalse(session.finished)
            self.assertEqual(session.inputs[-1], ("diagnostic", "wm-restored-input-failure"))

    def test_no_wm_mode_never_queries_or_minimizes(self):
        session = FakeDriver()
        with patch.object(session, "wait_wm_state", side_effect=AssertionError("unexpected WM probe")):
            result = accept.exercise_window(session)
        self.assertEqual(result["windowManager"], dict(required=False, transitions=[]))
        self.assertFalse(any(item[0] == "windowminimize" for item in session.inputs))

    def test_wm_failure_does_not_become_passing_check(self):
        for failure_index in (0, 1, 2):
            session = FakeDriver()
            observations = [dict(xid=12345, wmState=s, mapState=m) for s, m in ((1, 2), (3, 0), (1, 2))]
            observations[failure_index] = ValueError("WM transition never observed")
            with patch.object(session, "wait_wm_state", side_effect=observations):
                with self.assertRaisesRegex(ValueError, "never observed"):
                    accept.exercise_window(session, require_wm=True)
            self.assertFalse(session.finished)
            self.assertNotIn("WM restored window accepts input", session.checks)

    def test_host_not_suspended_or_failed_restored_input_cannot_pass(self):
        for failure in ("windowminimize", "click"):
            session = FakeDriver()
            original = session.input
            def input_(*args):
                if args[0] == failure and (failure == "windowminimize" or session.wm == 1 and ("windowminimize", "12345") in session.inputs):
                    if failure == "windowminimize":
                        session.wm = 3
                    return
                original(*args)
            with patch.object(session, "input", side_effect=input_):
                with self.assertRaisesRegex(ValueError, "Fake condition failed"):
                    accept.exercise_window(session, require_wm=True)
            self.assertFalse(session.finished)

    def test_minimized_frame_progress_and_missing_restored_focus_fail(self):
        session = FakeDriver()
        def pause(_seconds):
            if session.wm == 3:
                session.state["frames"] += 1
        with patch.object(session, "pause", side_effect=pause):
            with self.assertRaisesRegex(ValueError, "WM minimized frame count"):
                accept.exercise_window(session, require_wm=True)
        session = FakeDriver()
        original = session.input
        def input_(*args):
            if args[0] == "windowactivate" and ("windowminimize", "12345") in session.inputs:
                return
            original(*args)
        with patch.object(session, "input", side_effect=input_):
            with self.assertRaisesRegex(ValueError, "WM restore resumes"):
                accept.exercise_window(session, require_wm=True)

    def test_fake_os_sequence_covers_contract_without_real_window_claim(self):
        session = FakeDriver()
        result = accept.exercise_window(session)
        self.assertTrue(session.finished)
        self.assertEqual(result["classification"], "software")
        self.assertEqual(result["finalState"]["text"], "word")
        self.assertEqual(result["finalState"]["clicks"], 2)
        self.assertEqual(result["finalState"]["pointerCancels"], 1)
        self.assertEqual(result["finalState"]["closeRequests"], 2)
        self.assertGreaterEqual(len(session.checks), 20)
        self.assertEqual(sum(item[0] == "WM_DELETE_WINDOW" for item in session.inputs), 2)
        self.assertIn(("windowunmap", "12345"), session.inputs)
        self.assertIn(("windowmap", "--sync", "12345"), session.inputs)
        self.assertIn(("XSetInputFocus",), session.inputs)
        self.assertFalse(any(item[0] == "windowclose" for item in session.inputs))

    def test_missing_pointer_cancel_cannot_pass(self):
        with self.assertRaisesRegex(ValueError, "focus loss cancels pointer"):
            accept.exercise_window(FakeDriver(cancel=False))

    def test_focus_away_error_cannot_record_focus_loss_as_passed(self):
        session = FakeDriver()
        with patch.object(session, "focus_away", side_effect=ValueError("Focus-loss sink: X11 error 8")):
            with self.assertRaisesRegex(ValueError, "X11 error 8"):
                accept.exercise_window(session)
        self.assertNotIn("OS focus loss cancels pointer", session.checks)
        self.assertFalse(session.finished)

    def test_missing_thread_guard_cannot_pass(self):
        session = FakeDriver()
        session.state["threadGuard"] = False
        with self.assertRaisesRegex(ValueError, "ownership guards"):
            accept.exercise_window(session)

    def test_client_message_abi_has_24_native_longs(self):
        self.assertEqual(ctypes.sizeof(accept.XEvent), 24 * ctypes.sizeof(ctypes.c_long))
        self.assertLessEqual(ctypes.sizeof(accept.ClientMessage), ctypes.sizeof(accept.XEvent))

    def test_close_is_wm_protocol_message_not_native_destruction(self):
        observed = []

        class Library:
            def XInternAtom(self, display, name, only_existing):
                return {b"WM_PROTOCOLS": 42, b"WM_DELETE_WINDOW": 43}[name]

            def XSendEvent(self, display, xid, propagate, mask, pointer):
                message = ctypes.cast(pointer, ctypes.POINTER(accept.XEvent)).contents.client
                observed.append((xid, propagate, mask, message.type, message.message_type,
                                 message.format, message.data.l[0], message.data.l[1]))
                return 1

            def XSync(self, *_):
                pass

        x11 = accept.XlibNative.__new__(accept.XlibNative)
        x11.display, x11.lib = 1, Library()
        x11.request_close(12345)
        self.assertEqual(observed, [(12345, 0, 0, 33, 42, 32, 43, 0)])

    def test_native_capture_decoder_requires_nonuniform_pixels_and_frees_image(self):
        class Library:
            def __init__(self, uniform):
                self.header = accept.XImageHeader(width=4, height=4, red_mask=0xFF0000,
                                                 green_mask=0xFF00, blue_mask=0xFF)
                self.uniform, self.destroyed = uniform, 0

            def XSync(self, *_):
                pass

            def XGetImage(self, *_):
                return ctypes.addressof(self.header)

            def XGetPixel(self, pointer, x, y):
                return 0 if self.uniform else ((x * 60) << 16) | ((y * 60) << 8) | 30

            def XDestroyImage(self, pointer):
                self.destroyed += 1

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "native.png"
            small = {**state(), "width": 4, "height": 4}
            for uniform in (False, True):
                x11 = accept.XlibNative.__new__(accept.XlibNative)
                x11.display, x11.lib = 1, Library(uniform)
                if uniform:
                    with self.assertRaisesRegex(ValueError, "fewer than eight"):
                        x11.capture_window(small, path)
                else:
                    result = x11.capture_window(small, path)
                    self.assertEqual(result["distinctColorsAtLeast"], 16)
                    self.assertEqual(accept.validate_frame(path, small)["width"], 4)
                self.assertEqual(x11.lib.destroyed, 1)


class FocusSinkTests(unittest.TestCase):
    """Pure Xlib mocks: no display, sockets, native input or hardware evidence."""
    class Library:
        def __init__(self, states=(0, 1, 2), *, query_ok=True, focus=True, error_at=None):
            self.states = iter(states)
            self.last_state = 0
            self.query_ok, self.focus, self.error_at = query_ok, focus, error_at
            self.calls, self.handler, self.pending = [], None, False

        def XSetErrorHandler(self, handler):
            previous, self.handler = self.handler, handler
            self.calls.append("handler" if handler else "restore")
            return previous

        def XSync(self, *_):
            if self.pending:
                self.pending = False
                event = accept.XErrorEvent(error_code=8, request_code=42, resourceid=99, serial=11)
                ctypes.cast(self.handler, accept.XErrorHandler)(1, ctypes.pointer(event))

        def XCreateSimpleWindow(self, *_):
            self.calls.append("create")
            self.pending = self.error_at == "create"
            return 99

        def XMapWindow(self, *_):
            self.calls.append("map")
            self.pending = self.error_at == "map"

        def XGetWindowAttributes(self, display, sink, attributes):
            self.last_state = next(self.states, self.last_state)
            ctypes.cast(attributes, ctypes.POINTER(accept.XWindowAttributes)).contents.map_state = self.last_state
            self.calls.append(("attributes", self.last_state))
            self.pending = self.error_at == "query"
            return int(self.query_ok)

        def XSetInputFocus(self, display, sink, revert, timestamp):
            self.calls.append("focus")
            if self.last_state != 2:
                raise AssertionError("Focus requested before IsViewable")
            self.pending = self.error_at == "focus"

        def XGetInputFocus(self, display, window, revert):
            self.calls.append("get-focus")
            ctypes.cast(window, ctypes.POINTER(ctypes.c_ulong)).contents.value = 99 if self.focus else 7
            self.pending = self.error_at == "get-focus"

        def XDestroyWindow(self, display, sink):
            self.calls.append("destroy")
            self.pending = self.error_at == "destroy"

        def XCloseDisplay(self, *_):
            self.calls.append("close")

    def native(self, **kwargs):
        native = accept.XlibNative.__new__(accept.XlibNative)
        native.lib, native.display, native.root, native.sink = self.Library(**kwargs), 1, 2, 0
        return native

    def clock(self):
        current = [0.0]
        def sleep(seconds):
            current[0] += seconds
        return patch.object(accept.time, "monotonic", side_effect=lambda: current[0]), patch.object(accept.time, "sleep", side_effect=sleep)

    def test_delayed_mapping_waits_for_viewable_and_checks_actual_focus(self):
        native = self.native()
        clock, sleep = self.clock()
        with clock, sleep:
            native.focus_away(.1)
        self.assertEqual(native.lib.calls, ["handler", "create", "map", ("attributes", 0),
                         ("attributes", 1), ("attributes", 2), "focus", "get-focus", "restore"])
        self.assertEqual(native.sink, 99)
        native.close()
        self.assertEqual(native.lib.calls[-2:], ["destroy", "close"])

    def test_unmapped_or_unviewable_times_out_without_setting_focus(self):
        for state in (0, 1):
            native = self.native(states=(state,))
            clock, sleep = self.clock()
            with clock, sleep, self.assertRaisesRegex(ValueError, "IsViewable"):
                native.focus_away(.03)
            self.assertNotIn("focus", native.lib.calls)
            self.assertEqual(native.lib.calls[-2:], ["destroy", "restore"])
            self.assertEqual(native.sink, 0)

    def test_focus_request_without_actual_focus_cannot_pass(self):
        native = self.native(states=(2,), focus=False)
        clock, sleep = self.clock()
        with clock, sleep, self.assertRaisesRegex(ValueError, "own input focus"):
            native.focus_away(.03)
        self.assertEqual(native.lib.calls.count("focus"), 1)
        self.assertEqual(native.sink, 0)

    def test_create_failure_does_not_map_focus_or_destroy_zero(self):
        native = self.native()
        with patch.object(native.lib, "XCreateSimpleWindow", return_value=0), \
             self.assertRaisesRegex(ValueError, "Could not create"):
            native.focus_away()
        self.assertEqual(native.lib.calls, ["handler", "restore"])
        self.assertEqual(native.sink, 0)

    def test_query_failure_destroys_sink_and_restores_handler(self):
        native = self.native(query_ok=False)
        with self.assertRaisesRegex(ValueError, "Cannot query"):
            native.focus_away()
        self.assertNotIn("focus", native.lib.calls)
        self.assertEqual(native.lib.calls[-2:], ["destroy", "restore"])
        self.assertEqual(native.sink, 0)

    def test_asynchronous_x_errors_fail_and_cleanup_instead_of_exiting(self):
        for operation in ("create", "map", "query", "focus", "get-focus"):
            with self.subTest(operation=operation):
                native = self.native(states=(2,), error_at=operation)
                with self.assertRaisesRegex(ValueError, "X11 error 8, request 42"):
                    native.focus_away()
                self.assertEqual(native.lib.calls[-2:], ["destroy", "restore"])
                self.assertEqual(native.sink, 0)
                self.assertIsNone(native.lib.handler)

    def test_cleanup_error_is_drained_under_handler_and_original_failure_kept(self):
        native = self.native(query_ok=False, error_at="destroy")
        with self.assertRaisesRegex(ValueError, "Cannot query"):
            native.focus_away()
        self.assertFalse(native.lib.pending)
        self.assertIsNone(native.lib.handler)

    def test_reused_sink_is_rechecked_before_focus(self):
        native = self.native(states=(1,))
        native.sink = 99
        clock, sleep = self.clock()
        with clock, sleep, self.assertRaisesRegex(ValueError, "IsViewable"):
            native.focus_away(.03)
        self.assertNotIn("create", native.lib.calls)
        self.assertNotIn("focus", native.lib.calls)


class WindowManagerTests(unittest.TestCase):
    class Library:
        def __init__(self, *, atom=42, actual=42, form=32, count=2, remaining=0,
                     state=3, mapped=0, status=0, query=1, data=True):
            self.atom, self.actual, self.form, self.count = atom, actual, form, count
            self.remaining, self.mapped, self.status, self.query = remaining, mapped, status, query
            self.data = (ctypes.c_ulong * 2)(state, 777)
            self.has_data, self.freed = data, 0

        def XInternAtom(self, _display, name, existing):
            assert name == b"WM_STATE" and existing == 1
            return self.atom

        def XGetWindowProperty(self, display, xid, atom, offset, length, delete, requested,
                               actual, form, count, remaining, data):
            assert (xid, atom, offset, length, delete, requested) == (12345, 42, 0, 2, 0, 42)
            for pointer, kind, value in ((actual, ctypes.c_ulong, self.actual),
                    (form, ctypes.c_int, self.form), (count, ctypes.c_ulong, self.count),
                    (remaining, ctypes.c_ulong, self.remaining)):
                ctypes.cast(pointer, ctypes.POINTER(kind))[0] = value
            if self.has_data:
                ctypes.cast(data, ctypes.POINTER(ctypes.c_void_p))[0] = ctypes.addressof(self.data)
            return self.status

        def XGetWindowAttributes(self, display, xid, attributes):
            ctypes.cast(attributes, ctypes.POINTER(accept.XWindowAttributes)).contents.map_state = self.mapped
            return self.query

        def XFree(self, _data):
            self.freed += 1

    def native(self, **kwargs):
        native = accept.XlibNative.__new__(accept.XlibNative)
        native.display, native.lib = 1, self.Library(**kwargs)
        return native

    def test_icccm_lp64_property_read_and_free(self):
        native = self.native()
        self.assertEqual(native.wm_state(12345), dict(xid=12345, wmState=3, mapState=0))
        self.assertEqual(native.lib.freed, 1)

    def test_malformed_or_failed_property_is_rejected_and_freed(self):
        for kwargs in (dict(actual=43), dict(form=8), dict(count=1), dict(remaining=4),
                       dict(state=2), dict(status=1), dict(data=False)):
            with self.subTest(kwargs=kwargs):
                native = self.native(**kwargs)
                with self.assertRaises(ValueError):
                    native.wm_state(12345)
                self.assertEqual(native.lib.freed, 0 if kwargs.get("data") is False else 1)

    def test_missing_wm_property_is_not_iconic_even_when_unmapped(self):
        for kwargs in (dict(atom=0), dict(actual=0, data=False)):
            native = self.native(**kwargs)
            self.assertIsNone(native.wm_state(12345)["wmState"])
            with self.assertRaisesRegex(ValueError, "Timed out"):
                native.wait_wm_state(12345, 3, 0)

    def test_query_failure_cannot_pass(self):
        with self.assertRaisesRegex(ValueError, "map state"):
            self.native(query=0).wm_state(12345)

    def test_delayed_transition_requires_both_wm_state_and_map_state(self):
        native = self.native()
        records = [dict(xid=12345, wmState=3, mapState=2), dict(xid=12345, wmState=1, mapState=0),
                   dict(xid=12345, wmState=3, mapState=0)]
        with patch.object(native, "wm_state", side_effect=records), patch.object(accept.time, "sleep"):
            self.assertEqual(native.wait_wm_state(12345, 3, 1), records[-1])
        for state, mapped, expected in ((1, 0, 3), (3, 2, 3), (3, 2, 1), (1, 1, 1)):
            native = self.native(state=state, mapped=mapped)
            with self.assertRaisesRegex(ValueError, "Timed out"):
                native.wait_wm_state(12345, expected, 0)

    def test_worker_result_type_and_exact_client_state_are_required(self):
        worker = accept.X11Worker.__new__(accept.X11Worker)
        worker.event_timeout, worker.deadline = 1, time.monotonic() + 2
        for result in (None, {}, dict(xid=42, wmState=1, mapState=2),
                       dict(xid=12345, wmState=True, mapState=2), dict(xid=12345, wmState=1, mapState=0)):
            with patch.object(worker, "request", return_value=result), self.assertRaises(ValueError):
                worker.wait_wm_state(12345, 1)

    def test_rpc_routes_wm_probe_to_native_observer(self):
        requests = [dict(id=1, operation="open", arguments=dict(display=":fixture")),
                    dict(id=2, operation="wait-wm-state", arguments=dict(xid=12345, expected=3, timeout=.2)),
                    dict(id=3, operation="close", arguments={})]
        stream = io.StringIO()
        with patch.object(accept, "XlibNative") as native, \
             patch.object(sys, "stdin", io.StringIO("\n".join(map(json.dumps, requests)))), \
             contextlib.redirect_stdout(stream):
            native.return_value.library = "fake Xlib, no display"
            native.return_value.wait_wm_state.return_value = dict(xid=12345, wmState=3, mapState=0)
            native.return_value.close.return_value = None
            self.assertEqual(accept.xlib_worker_main(), 0)
        native.return_value.wait_wm_state.assert_called_once_with(12345, 3, .2)
        replies = list(map(json.loads, stream.getvalue().splitlines()))
        self.assertEqual(replies[1]["result"], dict(xid=12345, wmState=3, mapState=0))


class InputSnapshotTests(unittest.TestCase):
    class Library:
        def __init__(self):
            self.data = (ctypes.c_ulong * 1)(12345)
            self.freed = 0
            self.path, self.queried = {1: 2, 2: 12345, 12345: 0}, []
            self.actual, self.form, self.count, self.remaining = 33, 32, 1, 0
            self.same_screen, self.translated = 1, 1

        @staticmethod
        def put(pointer, kind, value):
            ctypes.cast(pointer, ctypes.POINTER(kind))[0] = value

        def XGetInputFocus(self, display, focus, revert):
            self.put(focus, ctypes.c_ulong, 12345)
            self.put(revert, ctypes.c_int, 2)

        def XInternAtom(self, display, name, existing):
            assert name == b"_NET_ACTIVE_WINDOW" and existing == 1
            return 44

        def XGetWindowProperty(self, display, xid, atom, offset, length, delete, requested,
                               actual, form, count, remaining, data):
            assert (xid, atom, offset, length, delete, requested) == (1, 44, 0, 1, 0, 33)
            for ptr, typ, val in ((actual, ctypes.c_ulong, self.actual), (form, ctypes.c_int, self.form),
                    (count, ctypes.c_ulong, self.count), (remaining, ctypes.c_ulong, self.remaining)):
                self.put(ptr, typ, val)
            self.put(data, ctypes.c_void_p, ctypes.addressof(self.data))
            return 0

        def XTranslateCoordinates(self, display, src, dst, x, y, left, top, child):
            assert (src, dst) == (12345, 1)
            self.put(left, ctypes.c_int, 10)
            self.put(top, ctypes.c_int, 20)
            return self.translated

        def XQueryPointer(self, display, current, root, child, rx, ry, wx, wy, mask):
            self.queried.append(current)
            for ptr, typ, val in ((root, ctypes.c_ulong, 1), (child, ctypes.c_ulong, self.path[current]),
                    (rx, ctypes.c_int, 114), (ry, ctypes.c_int, 152), (mask, ctypes.c_uint, 0)):
                self.put(ptr, typ, val)
            return self.same_screen

        def XFree(self, data):
            self.freed += 1

    def native(self):
        native = accept.XlibNative.__new__(accept.XlibNative)
        native.display, native.root, native.sink, native.lib = 7, 1, 99, self.Library()
        native.wm_state = lambda xid: dict(xid=xid, wmState=1, mapState=2)
        return native

    def test_observes_topmost_path_through_wm_frame_and_lp64_active_property(self):
        native = self.native()
        result = native.input_snapshot(12345)
        self.assertEqual(native.lib.queried, [1, 2, 12345])
        self.assertEqual(result["pointerPath"], [1, 2, 12345])
        self.assertEqual(result["focusSink"], 99)
        self.assertTrue(accept.pointer_ready(result, state(), "button"))
        self.assertEqual(native.lib.freed, 1)

    def test_occluding_sibling_is_not_mistaken_for_client(self):
        native = self.native()
        native.lib.path = {1: 99, 99: 0}
        result = native.input_snapshot(12345)
        self.assertFalse(accept.pointer_ready(result, state(), "button"))
        self.assertEqual(result["pointerPath"], [1, 99])

    def test_malformed_property_is_freed_and_cannot_pass(self):
        for key, value in (("actual", 42), ("form", 8), ("count", 2), ("remaining", 4)):
            native = self.native()
            setattr(native.lib, key, value)
            with self.assertRaisesRegex(ValueError, "Malformed"):
                native.input_snapshot(12345)
            self.assertEqual(native.lib.freed, 1)

    def test_pointer_query_failure_cycle_and_depth_are_bounded(self):
        for mode in ("screen", "translate", "cycle", "depth"):
            native = self.native()
            if mode == "screen": native.lib.same_screen = 0
            if mode == "translate": native.lib.translated = 0
            if mode == "cycle": native.lib.path = {1: 2, 2: 1}
            if mode == "depth": native.lib.path = {i: i + 1 for i in range(1, 34)}
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                native.input_snapshot(12345)
            self.assertLessEqual(len(native.lib.queried), 32)

    def test_worker_routes_snapshot_and_root_capture(self):
        observation = FakeDriver().input_snapshot(12345)
        requests = [dict(id=1, operation="open", arguments=dict(display=":fixture")),
                    dict(id=2, operation="input-snapshot", arguments=dict(xid=12345)),
                    dict(id=3, operation="capture-root", arguments=dict(path="fixture.png")),
                    dict(id=4, operation="close", arguments={})]
        output = io.StringIO()
        with patch.object(accept, "XlibNative") as native, \
                patch.object(sys, "stdin", io.StringIO("\n".join(map(json.dumps, requests)))), \
                contextlib.redirect_stdout(output):
            native.return_value.library = "fixture"
            native.return_value.input_snapshot.return_value = observation
            native.return_value.capture_root.return_value = dict(method="fixture")
            native.return_value.close.return_value = None
            self.assertEqual(accept.xlib_worker_main(), 0)
        native.return_value.input_snapshot.assert_called_once_with(12345)
        native.return_value.capture_root.assert_called_once_with(Path("fixture.png"))
        self.assertEqual(list(map(json.loads, output.getvalue().splitlines()))[1]["result"], observation)

    def test_worker_rejects_bad_identity_types_and_path(self):
        worker = accept.X11Worker.__new__(accept.X11Worker)
        for changed in (dict(xid=99), dict(inputFocus=True), dict(pointerRoot=[0, False]),
                        dict(pointerPath=[]), dict(pointerPath=[1, 1]), dict(pointerMask=-1)):
            value = FakeDriver().input_snapshot(12345)
            value.update(changed)
            with patch.object(worker, "request", return_value=value), self.assertRaises(ValueError):
                worker.input_snapshot(12345)

    def test_diagnostics_keep_latest_state_and_capture_errors(self):
        with tempfile.TemporaryDirectory() as temporary:
            session = accept.WindowSession.__new__(accept.WindowSession)
            session.report, session.state, session.output = {}, state(), Path(temporary)
            latest = {**state(), "hasInputFocus": True}
            with patch.object(session, "read", return_value=latest), patch.object(session, "x11", create=True) as worker:
                worker.input_snapshot.side_effect = ValueError("snapshot failed")
                worker.capture_root.side_effect = ValueError("capture failed")
                session.diagnose_input("fixture")
            record = session.report["inputDiagnostics"][0]
            self.assertEqual(record["state"], latest)
            self.assertEqual(record["errors"], ["snapshot failed", "capture failed"])
            self.assertTrue((session.output / "result.json").is_file())

    def test_failure_diagnostic_does_not_replace_original_input_failure(self):
        session = FakeDriver()
        original = session.input
        def input_(*args):
            if args[0] == "windowactivate":
                raise ValueError("original activation failed")
            original(*args)
        def diagnose(label):
            if label.endswith("failure"):
                raise OSError("diagnostic persistence failed")
        with patch.object(session, "input", side_effect=input_), patch.object(session, "diagnose_input", side_effect=diagnose):
            with self.assertRaisesRegex(ValueError, "original activation failed"):
                accept.exercise_window(session, require_wm=True)


class WorkerTimeoutTests(unittest.TestCase):
    # This child deliberately never opens Xlib or a display. It blocks forever
    # where the real worker could be stuck inside an uninterruptible Xlib call.
    BLOCKING = """
import json, sys, time
for line in sys.stdin:
    request = json.loads(line)
    if request['operation'] == 'open':
        print(json.dumps(dict(id=request['id'], ok=True, result=dict(library='fake Xlib'))), flush=True)
    else:
        time.sleep(3600)
"""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="youi-xlib-worker-")
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.report = dict(status="running", commands=[])

    def create(self, source):
        return accept.X11Worker(":never-opened", root=self.folder, output=self.folder, env=dict(os.environ),
                                report=self.report, event_timeout=.5, window_timeout=5,
                                worker_command=[sys.executable, "-u", "-c", source])

    def assert_reaped(self):
        record = self.report["commands"][0]
        self.assertEqual(record["exitCode"], -accept.signal.SIGKILL)
        self.assertIsNone(record["cleanupError"])
        with self.assertRaises(ProcessLookupError):
            os.kill(record["pid"], 0)

    def test_indefinitely_blocked_open_is_killed_and_reaped_without_display(self):
        started = time.monotonic()
        with self.assertRaisesRegex(ValueError, "Xlib open: timeout"), patch.object(accept.os, "killpg", wraps=os.killpg) as kill:
            self.create("import sys,time; sys.stdin.readline(); time.sleep(3600)")
        self.assertLess(time.monotonic() - started, 3)
        kill.assert_called_once_with(self.report["commands"][0]["pid"], accept.signal.SIGKILL)
        self.assertEqual(self.report["xlibOperations"][0]["status"], "timeout")
        self.assert_reaped()

    def test_blocked_worker_cannot_produce_a_top_level_acceptance_pass(self):
        original = accept.X11Worker

        def factory(display, **kwargs):
            return original(display, **kwargs, worker_command=[sys.executable, "-u", "-c",
                            "import sys,time; sys.stdin.readline(); time.sleep(3600)"])

        args = argparse.Namespace(output=self.folder / "fresh", configuration="Debug", backend="vulkan",
                                  event_timeout_seconds=.2, window_timeout_seconds=5,
                                  build_timeout_seconds=5, xdotool="unused")
        with patch.dict(os.environ, {"DISPLAY": ":never-opened"}), patch.object(accept, "X11Worker", side_effect=factory), \
             patch.object(accept, "run_command") as command, contextlib.redirect_stdout(io.StringIO()):
            os.environ.pop("RUSTUP_TOOLCHAIN", None)
            code = accept.run_acceptance(args, self.folder)
        command.assert_not_called()
        result = json.loads((args.output / "result.json").read_text())
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["checks"], [])
        self.assertEqual(result["commands"][0]["status"], "timeout")
        self.assertEqual(result["commands"][0]["exitCode"], -accept.signal.SIGKILL)

    def test_indefinitely_blocked_capture_uses_remaining_window_budget(self):
        worker = self.create(self.BLOCKING)
        worker.bind_deadline(time.monotonic() + .08)
        started = time.monotonic()
        try:
            with self.assertRaisesRegex(ValueError, "Xlib capture-window: timeout"):
                worker.capture_window(state(), self.folder / "never-produced.png")
        finally:
            worker.close()
        self.assertLess(time.monotonic() - started, 2)
        self.assertLessEqual(self.report["xlibOperations"][-1]["timeoutSeconds"], .08)
        self.assertEqual(self.report["xlibOperations"][-1]["status"], "timeout")
        self.assertFalse((self.folder / "never-produced.png").exists())
        self.assert_reaped()

    def test_input_snapshot_and_root_capture_use_shared_remaining_budget(self):
        for operation in ("input-snapshot", "capture-root"):
            worker = self.create(self.BLOCKING)
            worker.bind_deadline(time.monotonic() + .08)
            try:
                with self.assertRaisesRegex(ValueError, "Xlib " + operation + ": timeout"):
                    if operation == "input-snapshot":
                        worker.input_snapshot(12345)
                    else:
                        worker.capture_root(self.folder / "missing.png")
            finally:
                worker.close()
            self.assertLessEqual(self.report["xlibOperations"][-1]["timeoutSeconds"], .08)
            self.assertEqual(worker.process.returncode, -accept.signal.SIGKILL)

    def test_indefinitely_blocked_close_returns_failure_and_reaps_worker(self):
        worker = self.create(self.BLOCKING)
        worker.bind_deadline(time.monotonic() + .08)
        started = time.monotonic()
        error = worker.close()
        self.assertLess(time.monotonic() - started, 2)
        self.assertIn("Xlib close: timeout", error)
        self.assertEqual(self.report["commands"][0]["status"], "failed")
        self.assertEqual(self.report["xlibOperations"][-1]["status"], "timeout")
        self.assert_reaped()

    def test_indefinitely_blocked_wm_probe_is_killed_with_remaining_budget(self):
        worker = self.create(self.BLOCKING)
        worker.bind_deadline(time.monotonic() + .08)
        try:
            with self.assertRaisesRegex(ValueError, "Xlib wait-wm-state: timeout"):
                worker.wait_wm_state(12345, 3)
        finally:
            worker.close()
        self.assertLessEqual(self.report["xlibOperations"][-1]["timeoutSeconds"], .08)
        self.assert_reaped()

    def test_normal_pipe_rpc_worker_is_reaped_cleanly(self):
        source = """
import json, sys
for line in sys.stdin:
    request = json.loads(line)
    result = dict(library='fake Xlib') if request['operation'] == 'open' else None
    print(json.dumps(dict(id=request['id'], ok=True, result=result)), flush=True)
    if request['operation'] == 'close':
        break
"""
        worker = self.create(source)
        self.assertIsNone(worker.close())
        self.assertEqual(self.report["commands"][0]["status"], "passed")
        self.assertEqual(self.report["commands"][0]["exitCode"], 0)
        self.assertTrue(worker.process.stdin.closed)
        self.assertTrue(worker.process.stdout.closed)


class ValidationTests(unittest.TestCase):
    def test_state_accepts_initial_null_focus_and_rejects_invalid_numbers(self):
        self.assertIsNone(accept.validate_state(state())["focus"])
        for key, value in (("frames", True), ("xid", 0), ("pointerCancels", -1), ("threadGuard", 1)):
            invalid = state()
            invalid[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                accept.validate_state(invalid)

    def test_nonfinite_and_duplicate_json_are_rejected(self):
        for text in ('{"frames":NaN}', '{"frames":1,"frames":2}'):
            with self.assertRaises(ValueError):
                accept.strict_json(text)

    def test_adapter_classification_is_reported_device_type(self):
        for kind, classification in (("Cpu", "software"), ("DiscreteGpu", "hardware"), ("VirtualGpu", "unclassified")):
            self.assertEqual(accept.adapter_evidence(f"fixture (Vulkan; {kind}; driver fake)")["classification"], classification)
        with self.assertRaises(ValueError):
            accept.adapter_evidence("fixture (Gl; Cpu; driver fake)")

    def test_png_dimensions_checksums_and_lifecycle(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "frame.png"
            path.write_bytes(png())
            self.assertEqual(accept.validate_frame(path, state())["width"], 640)
            for content in (png()[:-10], png(100, 100), png()[:40] + b"bad checksum" + png()[52:]):
                path.write_bytes(content)
                with self.assertRaises(ValueError):
                    accept.validate_frame(path, state())
            path.write_text('{"reopened":true,"frames":1}')
            self.assertTrue(accept.validate_lifecycle(path)["reopened"])
            path.write_text('{"reopened":true,"frames":0}')
            with self.assertRaises(ValueError):
                accept.validate_lifecycle(path)

    def test_timeout_terminates_process_group_and_keeps_log(self):
        class Process:
            pid, returncode = 987654, None
            waits = 0

            def wait(self, timeout):
                self.waits += 1
                if self.waits == 1:
                    raise subprocess.TimeoutExpired(["fixture"], timeout)
                self.returncode = -9
                return -9

        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            report = dict(commands=[])
            with patch.object(accept.subprocess, "Popen", return_value=Process()) as popen, \
                 patch.object(accept.os, "killpg") as kill, self.assertRaisesRegex(ValueError, "timeout"):
                accept.run_command(["fixture"], root=folder, output=folder, name="timeout-fixture",
                                   env={}, timeout=.01, report=report)
            self.assertTrue(popen.call_args.kwargs["start_new_session"])
            kill.assert_called_once_with(987654, accept.signal.SIGKILL)
            self.assertEqual(report["commands"][0]["status"], "timeout")
            self.assertTrue((folder / "timeout-fixture.log").exists())


if __name__ == "__main__":
    unittest.main()
