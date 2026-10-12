# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Bounded native X11/Vulkan window acceptance using actual OS input.

uv run scripts/accept_x11.py --configuration Release --backend vulkan
Requires an already available DISPLAY. It does not start or configure a display.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.util
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import signal
import struct
import subprocess
import sys
import time
import uuid
import zlib

ROOT = Path(__file__).resolve().parents[1]
EVENTS = {"PointerMove", "PointerDown", "PointerUp", "PointerCancel", "Scroll",
          "KeyDown", "TextInput", "Composition", "Focus", "Blur"}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def write_report(output: Path, report: dict) -> None:
    temporary = output / "result.json.tmp"
    temporary.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(output / "result.json")


def strict_json(text: str) -> object:
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ValueError(f"Duplicate JSON key: {key}")
            value[key] = item
        return value

    def nonfinite(value):
        raise ValueError(f"Non-finite JSON constant: {value}")

    return json.loads(text, object_pairs_hook=unique, parse_constant=nonfinite)


def file_evidence(path: Path, relative_to: Path) -> dict:
    with path.open("rb") as stream:
        checksum = hashlib.file_digest(stream, "sha256").hexdigest()
    return dict(path=str(path.relative_to(relative_to)), bytes=path.stat().st_size, sha256=checksum)


def stop_process_tree(process: subprocess.Popen) -> str | None:
    """Every subprocess is a session leader; terminate its whole process group."""
    error = None
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    except OSError as exc:
        error = str(exc)
        process.kill()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        error = (error or "") + " process did not exit after group termination"
    return error


def run_command(command: list[str], *, root: Path, output: Path, name: str,
                env: dict[str, str], timeout: float, report: dict) -> dict:
    record = dict(name=name, command=command, status="running", startedUtc=utc_now(),
                  completedUtc=None, exitCode=None, log=name + ".log", timeoutSeconds=timeout)
    report["commands"].append(record)
    write_report(output, report)
    start = time.monotonic()
    try:
        with (output / record["log"]).open("w", encoding="utf-8") as stream:
            process = subprocess.Popen(command, cwd=root, env=env, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            try:
                record["exitCode"] = process.wait(timeout=timeout)
                record["status"] = "passed" if record["exitCode"] == 0 else "failed"
            except (subprocess.TimeoutExpired, KeyboardInterrupt) as exc:
                record["status"] = "interrupted" if isinstance(exc, KeyboardInterrupt) else "timeout"
                record["cleanupError"] = stop_process_tree(process)
                record["exitCode"] = process.returncode
                if isinstance(exc, KeyboardInterrupt):
                    raise
    except OSError as exc:
        record["status"] = "launch-failed"
        record["error"] = str(exc)
    finally:
        record["seconds"] = round(time.monotonic() - start, 3)
        record["completedUtc"] = utc_now()
        write_report(output, report)
    if record["status"] != "passed":
        raise ValueError(f"{name}: {record['status']}; inspect {record['log']}")
    return record


def nonnegative_integer(value: object, field: str) -> None:
    if type(value) is not int or value < 0:
        raise ValueError(f"state {field} must be a nonnegative integer")


def validate_state(state: object) -> dict:
    if not isinstance(state, dict):
        raise ValueError("state must be an object")
    for key in ("xid", "width", "height", "frames", "clicks", "closeRequests", "pointerCancels"):
        nonnegative_integer(state.get(key), key)
    if not state["xid"] or not state["width"] or not state["height"]:
        raise ValueError("state XID and dimensions must be positive")
    for key in ("hasInputFocus", "suspended", "closed", "threadGuard", "duplicateGuard"):
        if type(state.get(key)) is not bool:
            raise ValueError(f"state {key} must be a boolean")
    for key in ("adapter", "sdlVersion", "text"):
        if not isinstance(state.get(key), str):
            raise ValueError(f"state {key} must be a string")
    if not state["adapter"].strip() or not state["sdlVersion"].strip():
        raise ValueError("state adapter/SDL identity is empty")
    if state.get("focus") is not None and not isinstance(state["focus"], str):
        raise ValueError("state focus must be a string or null")
    if not isinstance(state.get("eventCounts"), dict):
        raise ValueError("state eventCounts must be an object")
    for key, value in state["eventCounts"].items():
        if key not in EVENTS:
            raise ValueError(f"Unrecognized eventCounts key: {key}")
        nonnegative_integer(value, "eventCounts." + key)
    for name in ("button", "textbox"):
        rect = state.get(name)
        if not isinstance(rect, dict):
            raise ValueError(f"state {name} rectangle is missing")
        for key in ("x", "y", "width", "height"):
            value = rect.get(key)
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ValueError(f"state {name}.{key} must be finite")
        if rect["width"] <= 0 or rect["height"] <= 0:
            raise ValueError(f"state {name} rectangle must have positive dimensions")
    return state


def adapter_evidence(identity: str) -> dict:
    match = re.search(r"\((Vulkan); (Cpu|IntegratedGpu|DiscreteGpu|VirtualGpu|Other); driver .+\)$", identity)
    if not match:
        raise ValueError("Adapter identity does not confirm the requested Vulkan backend")
    kind = match[2]
    classification = "software" if kind == "Cpu" else "hardware" if kind in ("IntegratedGpu", "DiscreteGpu") else "unclassified"
    return dict(adapter=identity, backend=match[1], adapterKind=kind, classification=classification,
                classificationBasis="reported adapter device type; not independent hardware certification")


class ClientData(ctypes.Union):
    _fields_ = [("b", ctypes.c_char * 20), ("s", ctypes.c_short * 10), ("l", ctypes.c_long * 5)]


class ClientMessage(ctypes.Structure):
    _fields_ = [("type", ctypes.c_int), ("serial", ctypes.c_ulong), ("send_event", ctypes.c_int),
                ("display", ctypes.c_void_p), ("window", ctypes.c_ulong), ("message_type", ctypes.c_ulong),
                ("format", ctypes.c_int), ("data", ClientData)]


class XEvent(ctypes.Union):
    _fields_ = [("client", ClientMessage), ("pad", ctypes.c_long * 24)]


class XImageHeader(ctypes.Structure):
    # The fields preceding XImage's function pointers are public Xlib ABI.
    _fields_ = [("width", ctypes.c_int), ("height", ctypes.c_int), ("xoffset", ctypes.c_int),
                ("format", ctypes.c_int), ("data", ctypes.c_void_p), ("byte_order", ctypes.c_int),
                ("bitmap_unit", ctypes.c_int), ("bitmap_bit_order", ctypes.c_int), ("bitmap_pad", ctypes.c_int),
                ("depth", ctypes.c_int), ("bytes_per_line", ctypes.c_int), ("bits_per_pixel", ctypes.c_int),
                ("red_mask", ctypes.c_ulong), ("green_mask", ctypes.c_ulong), ("blue_mask", ctypes.c_ulong)]


def save_rgb_png(path: Path, width: int, height: int, pixels: bytes) -> None:
    if len(pixels) != width * height * 3:
        raise ValueError("RGB pixel buffer does not match its dimensions")

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    rows = b"".join(b"\0" + pixels[y * width * 3:(y + 1) * width * 3] for y in range(height))
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


class X11:
    """Minimal Xlib calls; input itself is sent through xdotool's XTest path."""
    def __init__(self, display: str):
        self.library = ctypes.util.find_library("X11")
        if not self.library:
            raise ValueError("libX11 is required for WM_DELETE_WINDOW and OS focus-loss checks")
        self.lib = ctypes.CDLL(self.library)
        pointer, window = ctypes.c_void_p, ctypes.c_ulong
        signatures = {
            "XOpenDisplay": (pointer, [ctypes.c_char_p]),
            "XDefaultRootWindow": (window, [pointer]),
            "XDefaultScreen": (ctypes.c_int, [pointer]),
            "XDisplayWidth": (ctypes.c_int, [pointer, ctypes.c_int]),
            "XDisplayHeight": (ctypes.c_int, [pointer, ctypes.c_int]),
            "XCreateSimpleWindow": (window, [pointer, window, ctypes.c_int, ctypes.c_int, ctypes.c_uint,
                                             ctypes.c_uint, ctypes.c_uint, ctypes.c_ulong, ctypes.c_ulong]),
            "XMapWindow": (ctypes.c_int, [pointer, window]),
            "XSetInputFocus": (ctypes.c_int, [pointer, window, ctypes.c_int, ctypes.c_ulong]),
            "XInternAtom": (ctypes.c_ulong, [pointer, ctypes.c_char_p, ctypes.c_int]),
            "XSendEvent": (ctypes.c_int, [pointer, window, ctypes.c_int, ctypes.c_long, ctypes.POINTER(XEvent)]),
            "XTranslateCoordinates": (ctypes.c_int, [pointer, window, window, ctypes.c_int, ctypes.c_int,
                ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(window)]),
            "XSync": (ctypes.c_int, [pointer, ctypes.c_int]),
            "XDestroyWindow": (ctypes.c_int, [pointer, window]),
            "XGetImage": (pointer, [pointer, window, ctypes.c_int, ctypes.c_int, ctypes.c_uint,
                                    ctypes.c_uint, ctypes.c_ulong, ctypes.c_int]),
            "XGetPixel": (ctypes.c_ulong, [pointer, ctypes.c_int, ctypes.c_int]),
            "XDestroyImage": (ctypes.c_int, [pointer]),
            "XCloseDisplay": (ctypes.c_int, [pointer]),
        }
        for name, (result, arguments) in signatures.items():
            method = getattr(self.lib, name)
            method.restype, method.argtypes = result, arguments
        self.display = self.lib.XOpenDisplay(display.encode())
        self.sink = 0
        if not self.display:
            raise ValueError(f"Cannot open DISPLAY={display!r}; no native-window checks were run")
        self.root = self.lib.XDefaultRootWindow(self.display)

    def focus_away(self) -> None:
        if not self.sink:
            self.sink = self.lib.XCreateSimpleWindow(self.display, self.root, 0, 0, 8, 8, 0, 0, 0)
            if not self.sink:
                raise ValueError("Could not create an X11 focus-loss test window")
            self.lib.XMapWindow(self.display, self.sink)
            self.lib.XSync(self.display, 0)
        self.lib.XSetInputFocus(self.display, self.sink, 2, 0)  # RevertToParent, CurrentTime
        self.lib.XSync(self.display, 0)

    def request_close(self, xid: int) -> None:
        event = XEvent()
        event.client.type = 33  # ClientMessage, not XDestroyWindow.
        event.client.send_event = 1
        event.client.display = self.display
        event.client.window = xid
        event.client.message_type = self.lib.XInternAtom(self.display, b"WM_PROTOCOLS", 0)
        event.client.format = 32
        event.client.data.l[0] = self.lib.XInternAtom(self.display, b"WM_DELETE_WINDOW", 0)
        event.client.data.l[1] = 0
        if not self.lib.XSendEvent(self.display, xid, 0, 0, ctypes.byref(event)):
            raise ValueError("XSendEvent(WM_DELETE_WINDOW) failed")
        self.lib.XSync(self.display, 0)

    def outside_point(self, state: dict) -> tuple[int, int]:
        left, top, child = ctypes.c_int(), ctypes.c_int(), ctypes.c_ulong()
        if not self.lib.XTranslateCoordinates(self.display, state["xid"], self.root, 0, 0,
                                             ctypes.byref(left), ctypes.byref(top), ctypes.byref(child)):
            raise ValueError("Cannot locate the X11 client on its root window")
        screen = self.lib.XDefaultScreen(self.display)
        width = self.lib.XDisplayWidth(self.display, screen)
        height = self.lib.XDisplayHeight(self.display, screen)
        candidates = [(left.value - 4, top.value + state["height"] // 2),
                      (left.value + state["width"] + 4, top.value + state["height"] // 2),
                      (left.value + state["width"] // 2, top.value - 4),
                      (left.value + state["width"] // 2, top.value + state["height"] + 4)]
        for x, y in candidates:
            if 0 <= x < width and 0 <= y < height:
                return x, y
        raise ValueError("DISPLAY has no pointer-accessible space outside the sample; use a larger display")

    def capture_window(self, state: dict, path: Path) -> dict:
        width, height = state["width"], state["height"]
        if not 1 <= width <= 8192 or not 1 <= height <= 8192:
            raise ValueError("Native window dimensions exceed the capture bound")
        self.lib.XSync(self.display, 0)
        native = self.lib.XGetImage(self.display, state["xid"], 0, 0, width, height, ctypes.c_ulong(-1).value, 2)
        if not native:
            raise ValueError("XGetImage could not capture the mapped native window")
        try:
            header = ctypes.cast(native, ctypes.POINTER(XImageHeader)).contents
            masks = (header.red_mask, header.green_mask, header.blue_mask)
            if (header.width, header.height) != (width, height) or not all(masks):
                raise ValueError("Native-window capture requires a matching TrueColor X11 image")
            shifts = [(mask & -mask).bit_length() - 1 for mask in masks]
            maxima = [mask >> shift for mask, shift in zip(masks, shifts)]
            pixels = bytearray()
            colors = set()
            for y in range(height):
                for x in range(width):
                    pixel = self.lib.XGetPixel(native, x, y)
                    rgb = tuple(((pixel & mask) >> shift) * 255 // maximum
                                for mask, shift, maximum in zip(masks, shifts, maxima))
                    pixels.extend(rgb)
                    if len(colors) < 256:
                        colors.add(rgb)
            save_rgb_png(path, width, height, pixels)
            if len(colors) < 8:
                raise ValueError("Native X11 window contains fewer than eight distinct colors; surface rendering was not established")
            return dict(width=width, height=height, distinctColorsAtLeast=len(colors), method="XGetImage of mapped X11 client",
                        validation="nonuniform native pixels; manual visual inspection remains separate")
        finally:
            self.lib.XDestroyImage(native)

    def close(self) -> None:
        if self.display:
            if self.sink:
                self.lib.XDestroyWindow(self.display, self.sink)
            self.lib.XCloseDisplay(self.display)
            self.display = None


class WindowSession:
    def __init__(self, args, root: Path, output: Path, env: dict, report: dict, x11: X11):
        self.args, self.root, self.output = args, root, output
        self.env, self.report, self.x11 = env, report, x11
        self.process = None
        self.stream = None
        self.state = None
        self.pointer_down = False
        self.deadline = time.monotonic() + args.window_timeout_seconds
        self.record = None

    def remaining(self) -> float:
        value = self.deadline - time.monotonic()
        if value <= 0:
            raise ValueError("Native-window acceptance exceeded its total runtime bound")
        return value

    def start(self) -> None:
        sample = self.root / "samples/YoUI.Linux.Sample/bin" / self.args.configuration / "net10.0/YoUI.Linux.Sample.dll"
        command = ["dotnet", str(sample), "--acceptance", str(self.output / "sample")]
        self.record = dict(name="native-window", command=command, status="running", log="native-window.log",
                           startedUtc=utc_now(), completedUtc=None, exitCode=None,
                           timeoutSeconds=self.args.window_timeout_seconds)
        self.report["commands"].append(self.record)
        write_report(self.output, self.report)
        self.stream = (self.output / self.record["log"]).open("w", encoding="utf-8")
        self.process = subprocess.Popen(command, cwd=self.root, env=self.env, stdout=self.stream,
                                        stderr=subprocess.STDOUT, start_new_session=True)

    def read(self) -> dict | None:
        path = self.output / "sample/state.json"
        try:
            value = strict_json(path.read_text(encoding="utf-8-sig"))
        except FileNotFoundError:
            return None
        self.state = validate_state(value)
        return self.state

    def wait(self, name: str, predicate, *, allow_exit: bool = False) -> dict:
        deadline = time.monotonic() + min(self.args.event_timeout_seconds, self.remaining())
        while True:
            state = self.read()
            if self.process.poll() is not None and not allow_exit:
                raise ValueError(f"Sample exited {self.process.returncode} before {name}; inspect native-window.log")
            if state is not None and predicate(state):
                self.report["checks"].append(dict(name=name, status="passed", completedUtc=utc_now(), state=state))
                write_report(self.output, self.report)
                return state
            if time.monotonic() >= deadline:
                self.report["checks"].append(dict(name=name, status="timeout", state=state))
                raise ValueError(f"Timed out waiting for {name}; last state is retained in result.json")
            time.sleep(.025)

    def input(self, *arguments: str) -> None:
        if arguments[0] == "mousedown":
            self.pointer_down = True  # Also release if the command times out after pressing.
        run_command([self.args.xdotool, *map(str, arguments)], root=self.root, output=self.output,
                    name=f"input-{len(self.report['commands']):03d}", env=self.env,
                    timeout=min(self.args.event_timeout_seconds, self.remaining()), report=self.report)
        if arguments[0] == "mouseup":
            self.pointer_down = False

    def point(self, element: str) -> None:
        rect = self.state[element]
        self.input("mousemove", "--sync", "--window", str(self.state["xid"]),
                   str(round(rect["x"] + rect["width"] / 2)), str(round(rect["y"] + rect["height"] / 2)))

    def pause(self, seconds: float) -> None:
        time.sleep(min(seconds, self.remaining()))

    def capture(self) -> dict:
        self.remaining()
        path = self.output / "sample/native-window.png"
        result = self.x11.capture_window(self.state, path)
        self.remaining()
        return dict(**result, **file_evidence(path, self.output))

    def finish(self) -> None:
        try:
            code = self.process.wait(timeout=min(self.args.event_timeout_seconds, self.remaining()))
        except subprocess.TimeoutExpired as exc:
            raise ValueError("Sample did not exit after its accepted close request") from exc
        self.record.update(exitCode=code, status="passed" if code == 0 else "failed", completedUtc=utc_now())
        if code != 0:
            raise ValueError(f"Sample exited {code}; inspect native-window.log")

    def cleanup(self) -> None:
        if self.pointer_down:
            # Release only the button this runner pressed, including after a failed check.
            try:
                run_command([self.args.xdotool, "mouseup", "1"], root=self.root, output=self.output,
                            name="cleanup-mouseup", env=self.env, timeout=5, report=self.report)
            except (OSError, ValueError):
                pass
        if self.process is not None:
            if self.process.poll() is None:
                self.record["cleanupError"] = stop_process_tree(self.process)
            if self.record["status"] == "running":
                self.record.update(status="failed", exitCode=self.process.returncode, completedUtc=utc_now())
        if self.stream:
            self.stream.close()


def count(state: dict, event: str) -> int:
    return state["eventCounts"].get(event, 0)


def exercise_window(session: WindowSession) -> dict:
    initial = session.wait("initial rendered native window", lambda s: s["frames"] >= 1 and not s["closed"])
    adapter = adapter_evidence(initial["adapter"])
    if initial["text"] != "" or initial["clicks"] != 0 or initial["closeRequests"] != 0:
        raise ValueError("Sample did not start with fresh acceptance state")
    if not initial["threadGuard"] or not initial["duplicateGuard"]:
        raise ValueError("Sample did not verify creating-thread and single-host ownership guards")
    xid = str(initial["xid"])
    session.input("windowfocus", "--sync", xid)
    session.wait("OS input focus", lambda s: s["hasInputFocus"])
    session.point("button")
    for wanted in (1, 2):
        session.input("click", "1")
        session.wait(f"button click {wanted}", lambda s, wanted=wanted: s["clicks"] == wanted)
    session.point("textbox")
    session.input("click", "1")
    session.wait("textbox pointer focus", lambda s: s["focus"] == "textbox")
    text_events = count(session.state, "TextInput")
    session.input("type", "--delay", "35", "alpha beta")
    session.wait("ASCII typing", lambda s: s["text"] == "alpha beta" and count(s, "TextInput") > text_events)
    session.input("key", "ctrl+a")
    session.input("type", "--delay", "35", "world")
    session.wait("Ctrl+A replacement", lambda s: s["text"] == "world")
    session.input("key", "--delay", "50", "End", "Left", "BackSpace")
    session.wait("navigation and backspace", lambda s: s["text"] == "word")
    session.input("key", "Tab")
    session.wait("Tab traversal", lambda s: s["focus"] == "button")
    session.input("key", "shift+Tab")
    session.wait("Shift+Tab traversal", lambda s: s["focus"] == "textbox")
    session.point("button")
    scrolls = count(session.state, "Scroll")
    session.input("click", "4")
    session.input("click", "5")
    session.wait("OS wheel events", lambda s: count(s, "Scroll") >= scrolls + 2)
    clicks, downs, ups = session.state["clicks"], count(session.state, "PointerDown"), count(session.state, "PointerUp")
    session.input("mousedown", "1")
    session.wait("drag pointer down", lambda s: count(s, "PointerDown") > downs)
    x, y = session.x11.outside_point(session.state)
    session.input("mousemove", "--sync", str(x), str(y))
    session.input("mouseup", "1")
    session.wait("captured pointer release outside", lambda s: count(s, "PointerUp") > ups and s["clicks"] == clicks)
    session.point("button")
    downs, cancels = count(session.state, "PointerDown"), session.state["pointerCancels"]
    session.input("mousedown", "1")
    session.wait("focus-loss drag pointer down", lambda s: count(s, "PointerDown") > downs)
    session.x11.focus_away()
    session.wait("OS focus loss cancels pointer", lambda s: not s["hasInputFocus"] and s["pointerCancels"] > cancels)
    session.input("mouseup", "1")
    session.input("windowfocus", "--sync", xid)
    session.wait("focus restored without click", lambda s: s["hasInputFocus"] and s["clicks"] == clicks)
    width, height, frames = session.state["width"] + 80, session.state["height"] + 40, session.state["frames"]
    session.input("windowsize", "--sync", xid, str(width), str(height))
    session.wait("native resize and rendering", lambda s: s["width"] == width and s["height"] == height and s["frames"] > frames)
    session.input("windowunmap", xid)
    hidden = session.wait("X11 unmap suspends rendering", lambda s: s["suspended"])
    session.pause(.25)
    session.wait("unmapped frame count remains stable", lambda s: s["suspended"] and s["frames"] == hidden["frames"])
    session.input("windowmap", "--sync", xid)
    session.input("windowfocus", "--sync", xid)
    session.wait("X11 remap resumes rendering", lambda s: not s["suspended"] and s["frames"] > hidden["frames"])
    session.input("windowraise", xid)
    capture = session.capture()
    session.x11.request_close(int(xid))
    veto = session.wait("first WM_DELETE_WINDOW is vetoed", lambda s: s["closeRequests"] == 1 and not s["closed"])
    session.point("textbox")  # Idle hosts redraw only on invalidation; move hover to request a frame.
    session.wait("rendering continues after close veto", lambda s: s["frames"] > veto["frames"] and not s["closed"])
    session.x11.request_close(int(xid))
    final = session.wait("second WM_DELETE_WINDOW closes", lambda s: s["closeRequests"] == 2 and s["closed"], allow_exit=True)
    session.finish()
    if final["adapter"] != initial["adapter"] or final["xid"] != initial["xid"]:
        raise ValueError("Native window/adapter identity changed during acceptance")
    return dict(**adapter, sdlVersion=initial["sdlVersion"], nativeWindowCapture=capture, finalState=final)


def validate_lifecycle(path: Path) -> dict:
    value = strict_json(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict) or value.get("reopened") is not True:
        raise ValueError("Sample did not report disposal followed by successful host reconstruction")
    nonnegative_integer(value.get("frames"), "lifecycle.frames")
    if value["frames"] < 1:
        raise ValueError("Reconstructed native host did not render a frame")
    return value


def binary_evidence(root: Path, configuration: str) -> list[dict]:
    folder = root / "samples/YoUI.Linux.Sample/bin" / configuration / "net10.0"
    paths = [folder / name for name in ("YoUI.Linux.Sample.dll", "YoUI.Platform.Linux.dll", "YoUI.dll", "libyoui_render.so")]
    paths.append(root / "target" / configuration.lower() / "libyoui_render.so")
    records = [file_evidence(path, root) for path in paths]
    if any(record["bytes"] == 0 for record in records):
        raise ValueError("A built native or managed binary is empty")
    if records[-1]["sha256"] != records[-2]["sha256"]:
        raise ValueError("Sample native copy does not match the selected Cargo profile")
    return records


def validate_frame(path: Path, final: dict) -> dict:
    content = path.read_bytes()
    if len(content) < 33 or content[:8] != b"\x89PNG\r\n\x1a\n" or content[12:16] != b"IHDR":
        raise ValueError("Sample frame.png is missing or has an invalid PNG header")
    width, height = int.from_bytes(content[16:20], "big"), int.from_bytes(content[20:24], "big")
    if (width, height) != (final["width"], final["height"]):
        raise ValueError("Final frame dimensions do not match the resized native client")
    offset, image_data, ended = 8, False, False
    while offset + 12 <= len(content):
        length = int.from_bytes(content[offset:offset + 4], "big")
        end = offset + 12 + length
        if end > len(content):
            raise ValueError("Final PNG has a truncated chunk")
        kind, data = content[offset + 4:offset + 8], content[offset + 8:end - 4]
        checksum = int.from_bytes(content[end - 4:end], "big")
        if zlib.crc32(kind + data) & 0xFFFFFFFF != checksum:
            raise ValueError("Final PNG chunk checksum is invalid")
        if offset == 8 and (kind != b"IHDR" or length != 13):
            raise ValueError("Final PNG must begin with a 13-byte IHDR")
        image_data |= kind == b"IDAT" and length > 0
        offset = end
        if kind == b"IEND":
            ended = length == 0 and offset == len(content)
            break
    if not image_data or not ended:
        raise ValueError("Final PNG lacks complete IDAT/IEND chunks")
    return dict(width=width, height=height, validation="PNG structure/checksums/dimensions and SHA-256; visual inspection is separate")


def run_acceptance(args: argparse.Namespace, root: Path = ROOT) -> int:
    output = args.output.resolve() if args.output else root / "artifacts/x11-acceptance" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8])
    try:
        output.mkdir(parents=True, exist_ok=False)
    except FileExistsError:
        print(f"Refusing existing output directory (including empty directories): {output}", file=sys.stderr)
        return 2
    env = {**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "YOUI_BACKEND": args.backend,
           "SDL_VIDEODRIVER": "x11", "YOUI_TRACE": "1"}
    report = dict(schemaVersion=1, status="running", startedUtc=utc_now(), completedUtc=None,
                  configuration=args.configuration, requestedBackend=args.backend, source=None, sourceAfter=None,
                  host=dict(os=platform.system(), release=platform.release(), machine=platform.machine(), python=sys.version),
                  environment={key: env[key] for key in ("DISPLAY", "SDL_VIDEODRIVER", "YOUI_BACKEND", "YOUI_TRACE",
                      "VK_ICD_FILENAMES", "VK_DRIVER_FILES", "LIBGL_ALWAYS_SOFTWARE", "XDG_RUNTIME_DIR") if key in env},
                  versions={}, commands=[], checks=[], adapter=None, binaryEvidence=[], artifacts=[],
                  evidenceScope="This Linux X11/Vulkan host and its reported adapter only. Synthetic OS input via XTest and Xlib; no injected UiDocument or SDL event queue. Unmap/remap is hidden-window coverage, not window-manager minimize coverage. No IME, accessibility, Wayland, other-platform, or hardware-performance claim.")
    write_report(output, report)
    x11 = session = None
    code = 1
    try:
        if platform.system() != "Linux":
            raise ValueError("Native X11 acceptance requires Linux")
        if args.configuration not in ("Debug", "Release") or args.backend != "vulkan":
            raise ValueError("Explicit Debug or Release configuration and vulkan backend are required")
        if not env.get("DISPLAY", "").strip():
            raise ValueError("DISPLAY is required; no native-window checks were run")
        if env.get("RUSTUP_TOOLCHAIN"):
            raise ValueError("RUSTUP_TOOLCHAIN overrides the repository pin; unset it before acceptance")
        x11 = X11(env["DISPLAY"])
        report["versions"]["xlib"] = x11.library
        pins = [file_evidence(root / name, root) for name in ("global.json", "rust-toolchain.toml")]
        report["toolchainFiles"] = pins

        def command(name, argv, timeout=None):
            result = run_command(argv, root=root, output=output, name=name, env=env,
                                 timeout=timeout or min(30, args.build_timeout_seconds), report=report)
            return (output / result["log"]).read_text(encoding="utf-8").rstrip("\r\n")

        def source(suffix=""):
            sha = command("git-sha" + suffix, ["git", "rev-parse", "HEAD"])
            dirty = command("git-dirty" + suffix, ["git", "status", "--porcelain=v1", "--untracked-files=normal"])
            if not re.fullmatch(r"[0-9a-fA-F]{40,64}", sha):
                raise ValueError("git did not return a valid commit SHA")
            return dict(sha=sha, dirty=bool(dirty), statusPorcelain=dirty.splitlines())

        report["source"] = source()
        for name, argv in (("rustc", ["rustc", "--version"]), ("cargo", ["cargo", "--version"]),
                           ("dotnet", ["dotnet", "--info"]), ("xdotool", [args.xdotool, "--version"])):
            version = command(name, argv)
            if not version.strip():
                raise ValueError(f"{name} version output is empty")
            report["versions"][name] = version
        native = ["cargo", "build", "--workspace", "--locked", "--target-dir", str(root / "target")]
        if args.configuration == "Release":
            native.append("--release")
        command("build-native", native, args.build_timeout_seconds)
        command("build-managed", ["dotnet", "build", "samples/YoUI.Linux.Sample/YoUI.Linux.Sample.csproj",
                                  "-c", args.configuration, "-m:1", "--disable-build-servers"], args.build_timeout_seconds)
        report["binaryEvidence"] = binary_evidence(root, args.configuration)
        session = WindowSession(args, root, output, env, report, x11)
        session.start()
        report["adapter"] = exercise_window(session)
        report["frame"] = validate_frame(output / "sample/frame.png", report["adapter"]["finalState"])
        report["lifecycle"] = validate_lifecycle(output / "sample/lifecycle.json")
        report["sourceAfter"] = source("-after")
        if report["source"] != report["sourceAfter"]:
            raise ValueError("Git SHA or working-tree status changed during acceptance")
        if pins != [file_evidence(root / name, root) for name in ("global.json", "rust-toolchain.toml")]:
            raise ValueError("Toolchain pins changed during acceptance")
        if report["binaryEvidence"] != binary_evidence(root, args.configuration):
            raise ValueError("Native or managed binaries changed during acceptance")
        report["status"], code = "passed", 0
    except KeyboardInterrupt:
        report["status"], report["error"], code = "interrupted", "Interrupted; partial logs retained.", 130
    except (OSError, ValueError, TypeError) as exc:
        report["status"], report["error"] = "failed", str(exc)
    finally:
        if session:
            session.cleanup()
        if x11:
            x11.close()
        report["completedUtc"] = utc_now()
        report["artifacts"] = [file_evidence(path, output) for path in sorted((output / "sample").glob("*")) if path.is_file()]
        write_report(output, report)
        print(f"{report['status'].upper()}: native X11 {args.configuration} Vulkan on {platform.system()}")
        print(f"Evidence: {output / 'result.json'}", flush=True)
    return code


def bounded_timeout(value: str) -> int:
    number = int(value)
    if not 1 <= number <= 86400:
        raise argparse.ArgumentTypeError("timeout must be between 1 and 86400 seconds")
    return number


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", required=True, choices=("Debug", "Release"))
    parser.add_argument("--backend", required=True, choices=("vulkan",))
    parser.add_argument("--output", type=Path, help="New directory; existing directories are rejected")
    parser.add_argument("--build-timeout-seconds", type=bounded_timeout, default=900)
    parser.add_argument("--window-timeout-seconds", type=bounded_timeout, default=120)
    parser.add_argument("--event-timeout-seconds", type=bounded_timeout, default=15)
    parser.add_argument("--xdotool", default="xdotool", help="xdotool executable or full path")
    return run_acceptance(parser.parse_args(argv))


if __name__ == "__main__":
    raise SystemExit(main())
