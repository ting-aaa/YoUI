# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Own a dedicated, authenticated Xvfb/Openbox session for CI acceptance.

Only run on a permitted Linux host. This does not work around denied sockets.
No dependency installation, remote dispatch, or display fallback occurs here.
"""
from __future__ import annotations

import argparse
from contextlib import ExitStack
import json
import os
from pathlib import Path
import re
import secrets
import signal
import subprocess
import sys
import tempfile
import time

import accept_x11 as acceptance

ROOT = Path(__file__).resolve().parents[1]
DISPLAY = ":97"


def write_report(output, report):
    acceptance.write_report(output, report)


class Session:
    def __init__(self, root, output, env, report):
        self.root, self.output, self.env, self.report = root, output, env, report
        self.processes = []
        self.readiness_deadline = None
        self.files = ExitStack()

    def start(self, name, argv):
        stream = self.files.enter_context((self.output / (name + ".log")).open("w"))
        process = subprocess.Popen(argv, cwd=self.root, env=self.env, stdout=stream,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        self.processes.append((name, process))
        self.report["processes"].append(dict(name=name, pid=process.pid, command=argv, exitCode=None))
        write_report(self.output, self.report)
        return process

    def healthy(self):
        for name, process in self.processes:
            if process.poll() is not None:
                raise ValueError(f"Owned {name} exited early with code {process.returncode}")

    def command(self, name, argv, timeout=5):
        if self.readiness_deadline is not None:
            timeout = min(timeout, self.readiness_deadline - time.monotonic())
            if timeout <= 0:
                raise ValueError("Readiness budget exhausted")
        record = acceptance.run_command(argv, root=self.root, output=self.output, name=name,
                                        env=self.env, timeout=timeout, report=self.report)
        return (self.output / record["log"]).read_text()

    def ready(self, name, probe, seconds=20):
        deadline = time.monotonic() + seconds
        last_error = "no successful probe"
        self.readiness_deadline = deadline
        while time.monotonic() < deadline:
            self.healthy()
            try:
                value = probe()
                self.healthy()
                self.report["readiness"][name] = value
                write_report(self.output, self.report)
                self.readiness_deadline = None
                return value
            except ValueError as exc:
                last_error = str(exc)
            time.sleep(.1)
        self.readiness_deadline = None
        raise ValueError(f"{name} readiness timed out: {last_error}")

    def close(self):
        errors = []
        for name, process in reversed(self.processes):
            try:
                error = acceptance.stop_process_tree(process)
                if error:
                    errors.append(f"{name}: {error}")
            except (Exception, KeyboardInterrupt) as exc:
                # Teardown is best-effort for every owned group, even if one
                # termination or reap operation fails. Any error revokes pass.
                errors.append(f"{name}: {type(exc).__name__}: {exc}")
            finally:
                next(record for record in self.report["processes"] if record["name"] == name)["exitCode"] = process.returncode
        try:
            self.files.close()
        except (Exception, KeyboardInterrupt) as exc:
            errors.append(f"log files: {type(exc).__name__}: {exc}")
        return errors


def window_id(text):
    match = re.fullmatch(r"_NET_SUPPORTING_WM_CHECK\(WINDOW\): window id # (0x[0-9a-fA-F]+)\s*", text)
    if not match or int(match[1], 16) == 0:
        raise ValueError("Window manager has not published a valid EWMH support window")
    return match[1]


def probe_wm(session, pid):
    xid = window_id(session.command("wm-root", ["xprop", "-root", "_NET_SUPPORTING_WM_CHECK"]))
    if window_id(session.command("wm-self", ["xprop", "-id", xid, "_NET_SUPPORTING_WM_CHECK"])) != xid:
        raise ValueError("Window manager support window does not point back to itself")
    name = session.command("wm-name", ["xprop", "-id", xid, "_NET_WM_NAME"])
    actual_pid = session.command("wm-pid", ["xprop", "-root", "_OPENBOX_PID"])
    if not re.fullmatch(r'_NET_WM_NAME\(UTF8_STRING\) = "Openbox"\s*', name):
        raise ValueError("EWMH window manager is not Openbox")
    if not re.fullmatch(rf"_OPENBOX_PID\(CARDINAL\) = {pid}\s*", actual_pid):
        raise ValueError("EWMH window manager is not the owned Openbox process")
    return dict(xid=xid, name="Openbox", pid=pid)


def source(session, expected):
    sha = session.command("source-sha", ["git", "rev-parse", "HEAD"]).strip()
    dirty = session.command("source-status", ["git", "status", "--porcelain=v1", "--untracked-files=normal"])
    if sha != expected or dirty.strip():
        raise ValueError("CI acceptance requires the exact expected commit and a clean checkout")
    return dict(sha=sha, dirty=False, statusPorcelain=[])


def validate_result(result, expected, configuration, output):
    if not isinstance(result, dict):
        raise ValueError("Native acceptance result must be an object")
    for field in ("adapter", "lifecycle"):
        if not isinstance(result.get(field), dict):
            raise ValueError(f"Native acceptance {field} must be an object")
    if not isinstance(result.get("checks"), list) or any(not isinstance(check, dict) for check in result["checks"]):
        raise ValueError("Native acceptance checks must be a list of objects")
    if not isinstance(result["adapter"].get("nativeWindowCapture"), dict):
        raise ValueError("Native acceptance capture must be an object")
    if result.get("status") != "passed" or result.get("configuration") != configuration:
        raise ValueError("Native acceptance did not pass the selected configuration")
    identity = dict(sha=expected, dirty=False, statusPorcelain=[])
    if result.get("source") != identity or result.get("sourceAfter") != identity:
        raise ValueError("Native acceptance source provenance differs from the clean expected commit")
    adapter = result.get("adapter") or {}
    if adapter.get("classification") != "software" or adapter.get("adapterKind") != "Cpu" or adapter.get("backend") != "Vulkan":
        raise ValueError("This CI job requires reported CPU/software Vulkan evidence")
    capture = adapter.get("nativeWindowCapture") or {}
    path = output / "sample/native-window.png"
    colors = capture.get("distinctColorsAtLeast")
    if capture.get("path") != "sample/native-window.png" or type(colors) is not int or colors < 8:
        raise ValueError("Native X11 presentation evidence is missing")
    evidence = acceptance.file_evidence(path, output)
    if any(capture.get(key) != value for key, value in evidence.items()):
        raise ValueError("Native X11 presentation artifact hash/size differs from the report")
    checks = result.get("checks") or []
    required = {"OS input focus", "ASCII typing", "captured pointer release outside",
                "OS focus loss cancels pointer", "native resize and rendering",
                "X11 remap resumes rendering", "second WM_DELETE_WINDOW closes",
                "WM minimize suspends rendering", "WM minimized frame count remains stable",
                "WM restore resumes rendering", "WM restored pointer targets activated client", "WM restored window accepts input"}
    passed = {check.get("name") for check in checks if check.get("status") == "passed"}
    if not required <= passed or any(check.get("status") != "passed" for check in checks):
        raise ValueError("Required OS-input/window checks did not pass")
    wm = adapter.get("windowManager")
    final = adapter.get("finalState")
    if (result.get("requireWindowManager") is not True or not isinstance(wm, dict)
            or wm.get("required") is not True or not isinstance(final, dict)
            or type(final.get("xid")) is not int or final["xid"] <= 0):
        raise ValueError("Required WM minimize/restore evidence is missing")
    expected = [dict(xid=final["xid"], wmState=state, mapState=mapped)
                for state, mapped in ((1, 2), (3, 0), (1, 2))]
    transitions = wm.get("transitions")
    if (transitions != expected or any(type(value) is not int
            for transition in transitions for value in transition.values())):
        raise ValueError("ICCCM WM_STATE transitions do not establish minimize/restore")
    if (result.get("lifecycle") or {}).get("reopened") is not True:
        raise ValueError("Native host reconstruction evidence is missing")


def run(args, root=ROOT):
    output = args.output.resolve()
    try:
        output.mkdir(parents=True, exist_ok=False)
    except FileExistsError:
        print(f"Refusing existing output directory: {output}", file=sys.stderr)
        return 2
    report = dict(schemaVersion=1, status="running", startedUtc=acceptance.utc_now(),
                  completedUtc=None, configuration=args.configuration, expectedSha=args.expected_sha,
                  source=None, commands=[], processes=[], readiness={}, cleanupErrors=[],
                  evidenceScope="Xvfb virtual X11 + Openbox + software Vulkan + synthetic XTest input only; no physical input, hardware GPU, Wayland, IME or accessibility acceptance.")
    write_report(output, report)
    session = None
    code = 1
    try:
        if sys.platform != "linux":
            raise ValueError("CI X11 session requires Linux")
        if not re.fullmatch(r"[0-9a-f]{40}", args.expected_sha):
            raise ValueError("Expected SHA must be a complete 40-character commit hash")
        icd = args.icd.resolve(strict=True)
        if not icd.is_file():
            raise ValueError("A readable software Vulkan ICD manifest is required")
        # Private temporary authentication/runtime files are never uploaded as evidence.
        with tempfile.TemporaryDirectory(prefix="youi-x11-ci-") as directory:
            private = Path(directory)
            authority = private / "Xauthority"
            authority.touch(mode=0o600)
            runtime = private / "runtime"
            runtime.mkdir(mode=0o700)
            env = {**os.environ, "DISPLAY": DISPLAY, "XAUTHORITY": str(authority),
                   "XDG_RUNTIME_DIR": str(runtime), "SDL_VIDEODRIVER": "x11",
                   "VK_DRIVER_FILES": str(icd), "VK_ICD_FILENAMES": str(icd),
                   "LIBGL_ALWAYS_SOFTWARE": "1", "YOUI_BACKEND": "vulkan",
                   "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "POWERSHELL_TELEMETRY_OPTOUT": "1"}
            env.pop("WAYLAND_DISPLAY", None)
            session = Session(root, output, env, report)
            try:
                report["source"] = source(session, args.expected_sha)
                report["icd"] = dict(absolutePath=str(icd), **acceptance.file_evidence(icd, icd.parent))
                # Cookie goes through stdin, not command arguments or artifact logs.
                auth = subprocess.run(["xauth", "-f", str(authority)],
                                      input=f"add {DISPLAY} . {secrets.token_hex(16)}\n",
                                      text=True, capture_output=True, env=env, timeout=5, check=False)
                if auth.returncode:
                    raise ValueError("Could not create private X11 authentication")
                session.start("xvfb", ["Xvfb", DISPLAY, "-screen", "0", "1280x960x24",
                                       "-nolisten", "tcp", "-auth", str(authority), "-noreset"])
                session.ready("display", lambda: session.command("display-info", ["xdpyinfo"]))
                wm = session.start("openbox", ["openbox", "--sm-disable", "--config-file", "/etc/xdg/openbox/rc.xml"])
                session.ready("windowManager", lambda: probe_wm(session, wm.pid))
                session.command("vulkan-info", ["vulkaninfo", "--summary"], timeout=30)
                session.healthy()
                native = argparse.Namespace(configuration=args.configuration, backend="vulkan",
                    output=output / "native", build_timeout_seconds=900, event_timeout_seconds=15,
                    window_timeout_seconds=120, xdotool="xdotool", require_wm=True)
                # In-process call preserves the acceptance runner's finally cleanup on SIGTERM.
                previous = dict(os.environ)
                try:
                    os.environ.clear()
                    os.environ.update(env)
                    native_code = acceptance.run_acceptance(native, root)
                finally:
                    os.environ.clear()
                    os.environ.update(previous)
                if native_code != 0:
                    raise ValueError(f"Native acceptance exited {native_code}; inspect native/result.json")
                session.healthy()
                result = acceptance.strict_json((native.output / "result.json").read_text())
                validate_result(result, args.expected_sha, args.configuration, native.output)
                if source(session, args.expected_sha) != report["source"]:
                    raise ValueError("Source changed during CI acceptance")
                report["nativeResult"] = acceptance.file_evidence(native.output / "result.json", output)
                report["status"], code = "passed", 0
            finally:
                report["cleanupErrors"] = session.close()
                if report["cleanupErrors"]:
                    report["status"], code = "failed", 1
    except (KeyboardInterrupt, OSError, ValueError, TypeError, subprocess.TimeoutExpired) as exc:
        report["status"], report["error"], code = "failed", str(exc) or "Interrupted", 1
    finally:
        report["completedUtc"] = acceptance.utc_now()
        write_report(output, report)
        print(f"{report['status'].upper()}: virtual X11 CI evidence: {output / 'result.json'}")
    return code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", required=True, choices=("Debug", "Release"))
    parser.add_argument("--expected-sha", required=True)
    parser.add_argument("--icd", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args(argv)
    def interrupted(_signum, _frame):
        raise KeyboardInterrupt("CI session interrupted")
    signal.signal(signal.SIGTERM, interrupted)
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
