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

    def increment(self, event):
        self.state["eventCounts"][event] = accept.count(self.state, event) + 1

    def wait(self, name, predicate, **_):
        if not self.state["suspended"] and not self.state["closed"]:
            self.state["frames"] += 1
        if not predicate(self.state):
            raise ValueError("Fake condition failed: " + name)
        self.checks.append(name)
        return copy.deepcopy(self.state)

    def input(self, *args):
        self.inputs.append(args)
        kind = args[0]
        if kind == "windowfocus":
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

    def fixture_run(self, fixture):
        final = state()
        final.update(closed=True, closeRequests=2)
        result = dict(**accept.adapter_evidence(final["adapter"]), finalState=final)
        with patch.dict(os.environ, {"DISPLAY": ":fake"}), patch.object(accept.platform, "system", return_value="Linux"), \
             patch.object(accept, "X11") as x11, patch.object(accept, "run_command", side_effect=fixture.run), \
             patch.object(accept, "WindowSession", side_effect=fixture.session), \
             patch.object(accept, "exercise_window", return_value=result), contextlib.redirect_stdout(io.StringIO()):
            x11.return_value.library = "fixture libX11"
            with patch.dict(os.environ):
                os.environ.pop("RUSTUP_TOOLCHAIN", None)
                code = accept.run_acceptance(self.args, self.root)
            x11.return_value.close.assert_called_once()
        return code, json.loads((self.args.output / "result.json").read_text(encoding="utf-8"))

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
        with patch.dict(os.environ), patch.object(accept, "X11") as x11, \
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
        with patch.dict(os.environ, {"DISPLAY": ":fixture"}), patch.object(accept, "X11", side_effect=ValueError("Cannot open display")), \
             patch.object(accept.subprocess, "Popen") as popen, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(accept.run_acceptance(self.args, self.root), 1)
        popen.assert_not_called()

    def test_existing_even_empty_output_is_rejected(self):
        self.args.output.mkdir()
        with patch.object(accept, "X11") as x11, contextlib.redirect_stderr(io.StringIO()):
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

    def test_cli_requires_explicit_configuration_and_vulkan(self):
        for arguments in ([], ["--backend", "vulkan"], ["--configuration", "Release"],
                          ["--configuration", "Release", "--backend", "gl"]):
            with self.subTest(arguments=arguments), self.assertRaises(SystemExit) as exc, contextlib.redirect_stderr(io.StringIO()):
                accept.main(arguments)
            self.assertEqual(exc.exception.code, 2)

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

        x11 = accept.X11.__new__(accept.X11)
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
                x11 = accept.X11.__new__(accept.X11)
                x11.display, x11.lib = 1, Library(uniform)
                if uniform:
                    with self.assertRaisesRegex(ValueError, "fewer than eight"):
                        x11.capture_window(small, path)
                else:
                    result = x11.capture_window(small, path)
                    self.assertEqual(result["distinctColorsAtLeast"], 16)
                    self.assertEqual(accept.validate_frame(path, small)["width"], 4)
                self.assertEqual(x11.lib.destroyed, 1)


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
