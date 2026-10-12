# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""CI supervisor fixtures only: no X display, GPU or native-window evidence."""
import argparse
import contextlib
import copy
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock, patch
import accept_x11_ci as ci

SHA = "a" * 40
IDENTITY = dict(sha=SHA, dirty=False, statusPorcelain=[])
CHECKS = ["OS input focus", "ASCII typing", "captured pointer release outside",
          "OS focus loss cancels pointer", "native resize and rendering",
          "X11 remap resumes rendering", "second WM_DELETE_WINDOW closes",
          "WM minimize suspends rendering", "WM minimized frame count remains stable",
          "WM restore resumes rendering", "WM restored window accepts input"]


def result(output):
    (output / "sample").mkdir(parents=True)
    # Not an actual image. The real runner validates PNG pixels; this fixture
    # tests only the supervisor's artifact hash requirement.
    path = output / "sample/native-window.png"
    path.write_bytes(b"fixture: not presentation evidence")
    return dict(status="passed", configuration="Release", source=IDENTITY, sourceAfter=IDENTITY, requireWindowManager=True,
                adapter=dict(classification="software", backend="Vulkan", adapterKind="Cpu",
                    finalState=dict(xid=12345), windowManager=dict(required=True, transitions=[
                        dict(xid=12345, wmState=s, mapState=m) for s, m in ((1, 2), (3, 0), (1, 2))]),
                    nativeWindowCapture=dict(distinctColorsAtLeast=8, **ci.acceptance.file_evidence(path, output))),
                checks=[dict(name=name, status="passed") for name in CHECKS], lifecycle=dict(reopened=True))


class SupervisorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="youi-ci-fixture-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.icd = self.root / "fixture.json"
        self.icd.write_text("{}")
        self.args = argparse.Namespace(output=self.root / "output", icd=self.icd,
                                       expected_sha=SHA, configuration="Release")
        self.session = Mock()
        self.session.command.return_value = "fixture"
        self.session.close.return_value = []
        self.session.start.return_value.pid = 123

    def run_fixture(self, mutate=None, native_code=0, top_level=None):
        def native(args, root):
            self.assertEqual(os.environ["DISPLAY"], ":97")
            self.assertEqual(os.environ["VK_DRIVER_FILES"], str(self.icd))
            self.assertEqual(os.environ["VK_ICD_FILENAMES"], str(self.icd))
            self.assertNotIn("WAYLAND_DISPLAY", os.environ)
            self.assertEqual(args.backend, "vulkan")
            self.assertTrue(args.require_wm)
            value = result(args.output)
            if mutate:
                mutate(value)
            if top_level is not None:
                value = top_level
            (args.output / "result.json").write_text(json.dumps(value))
            return native_code
        with patch.object(ci, "Session", return_value=self.session), \
             patch.object(ci, "source", return_value=IDENTITY), \
             patch.object(ci.subprocess, "run", return_value=Mock(returncode=0)), \
             patch.object(ci.acceptance, "run_acceptance", side_effect=native) as runner, \
             contextlib.redirect_stdout(io.StringIO()):
            code = ci.run(self.args, self.root)
        return code, json.loads((self.args.output / "result.json").read_text()), runner

    def test_fixture_success_requires_runner_contract_and_owned_cleanup(self):
        previous = dict(os.environ)
        code, report, runner = self.run_fixture()
        self.assertEqual(code, 0, report)
        self.assertEqual(report["status"], "passed")
        self.assertEqual(os.environ, previous)
        self.session.close.assert_called_once()
        self.assertEqual(self.session.ready.call_count, 2)
        self.assertEqual(self.session.start.call_count, 2)
        argv = self.session.start.call_args_list[0].args[1]
        self.assertIn("-auth", argv)
        self.assertIn("-nolisten", argv)
        self.assertNotIn("-ac", argv)
        self.assertFalse(Path(argv[argv.index("-auth") + 1]).exists())
        self.assertEqual(runner.call_count, 1)

    def test_setup_success_cannot_replace_missing_native_checks(self):
        code, report, _ = self.run_fixture(lambda r: r.update(checks=[]))
        self.assertEqual(code, 1)
        self.assertIn("Required OS-input", report["error"])

    def test_missing_or_stale_wm_evidence_cannot_pass(self):
        for mutation in (
            lambda r: r.pop("requireWindowManager"),
            lambda r: r["adapter"].pop("windowManager"),
            lambda r: r["adapter"]["windowManager"].update(required=False),
            lambda r: r["adapter"]["windowManager"].update(transitions=[]),
            lambda r: r["adapter"]["windowManager"]["transitions"][1].update(wmState=0),
            lambda r: r["adapter"]["windowManager"]["transitions"][0].update(wmState=True),
            lambda r: r["adapter"]["windowManager"]["transitions"][2].update(xid=999),
            lambda r: r["adapter"]["windowManager"]["transitions"].pop(),
            lambda r: r["checks"].pop(),
        ):
            with self.subTest(mutation=mutation):
                self.args.output = self.root / ("output-" + str(time.monotonic_ns()))
                code, report, _ = self.run_fixture(mutation)
                self.assertEqual(code, 1, report)
                self.assertEqual(report["status"], "failed")

    def test_nonzero_runner_exit_cannot_pass_with_passed_fixture_report(self):
        code, report, _ = self.run_fixture(native_code=1)
        self.assertEqual(code, 1)
        self.assertIn("exited 1", report["error"])

    def test_dead_server_blocks_runner_and_teardown_still_runs(self):
        self.session.healthy.side_effect = ValueError("owned server exited")
        code, report, runner = self.run_fixture()
        self.assertEqual(code, 1)
        runner.assert_not_called()
        self.session.close.assert_called_once()
        self.assertIn("owned server", report["error"])

    def test_wm_readiness_failure_blocks_native_runner(self):
        self.session.ready.side_effect = ["ready", ValueError("window manager not ready")]
        code, _, runner = self.run_fixture()
        self.assertEqual(code, 1)
        runner.assert_not_called()
        self.session.close.assert_called_once()

    def test_cleanup_failure_revokes_pass(self):
        self.session.close.return_value = ["fixture cleanup failure"]
        code, report, _ = self.run_fixture()
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")
        self.assertEqual(report["cleanupErrors"], ["fixture cleanup failure"])

    def test_interruption_restores_environment_and_cleans_servers(self):
        previous = dict(os.environ)
        def interrupt(_result):
            raise KeyboardInterrupt("fixture signal")
        code, report, _ = self.run_fixture(interrupt)
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")
        self.assertEqual(os.environ, previous)
        self.session.close.assert_called_once()

    def test_server_death_after_native_success_revokes_pass(self):
        self.session.healthy.side_effect = [None, ValueError("late server death")]
        code, report, runner = self.run_fixture()
        self.assertEqual(code, 1)
        self.assertEqual(runner.call_count, 1)
        self.assertIn("late server death", report["error"])

    def test_teardown_exception_after_success_returns_failure(self):
        self.session.close.side_effect = OSError("mock teardown filesystem failure")
        code, report, _ = self.run_fixture()
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")
        self.assertIn("mock teardown", report["error"])

    def test_late_interrupt_after_success_returns_failure(self):
        self.session.close.side_effect = KeyboardInterrupt("late interrupt")
        code, report, _ = self.run_fixture()
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")

    def test_malformed_top_level_has_terminal_failed_report(self):
        code, report, _ = self.run_fixture(top_level=[])
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")
        self.assertIn("must be an object", report["error"])

    def test_malformed_nested_types_have_terminal_failed_reports(self):
        for field, value in (("adapter", []), ("lifecycle", []), ("checks", {}), ("checks", [None])):
            with self.subTest(field=field, value=value):
                self.args.output = self.root / (field + str(len(str(value))))
                code, report, _ = self.run_fixture(lambda r: r.update({field: value}))
                self.assertEqual(code, 1)
                self.assertEqual(report["status"], "failed")

    def test_existing_directory_rejected_without_processes(self):
        self.args.output.mkdir()
        with patch.object(ci, "Session") as session, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(ci.run(self.args, self.root), 2)
        session.assert_not_called()

    def test_missing_icd_creates_failed_report_without_server(self):
        self.args.icd = self.root / "missing"
        with patch.object(ci, "Session") as session, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(ci.run(self.args, self.root), 1)
        session.assert_not_called()
        self.assertEqual(json.loads((self.args.output / "result.json").read_text())["status"], "failed")

    def test_provenance_and_presentation_fail_closed(self):
        output = self.root / "native"
        valid = result(output)
        for field, value in (("status", "running"), ("configuration", "Debug"),
                             ("source", dict(IDENTITY, dirty=True)),
                             ("sourceAfter", dict(IDENTITY, sha="b" * 40)),
                             ("adapter", {}), ("checks", []), ("lifecycle", {})):
            invalid = copy.deepcopy(valid)
            invalid[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                ci.validate_result(invalid, SHA, "Release", output)
        for key, value in (("classification", "hardware"), ("adapterKind", "DiscreteGpu"), ("backend", "Gl")):
            invalid = copy.deepcopy(valid)
            invalid["adapter"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                ci.validate_result(invalid, SHA, "Release", output)
        (output / "sample/native-window.png").write_bytes(b"altered")
        with self.assertRaisesRegex(ValueError, "hash/size"):
            ci.validate_result(valid, SHA, "Release", output)

    def test_source_requires_exact_sha_and_clean_tree(self):
        for responses in ((SHA, ""), ("b" * 40, ""), (SHA, " M file")):
            session = Mock()
            session.command.side_effect = responses
            if responses == (SHA, ""):
                self.assertEqual(ci.source(session, SHA), IDENTITY)
            else:
                with self.assertRaises(ValueError):
                    ci.source(session, SHA)


class ReadinessTests(unittest.TestCase):
    def test_wm_probe_validates_owned_pid_and_reciprocal_window(self):
        good = ['_NET_SUPPORTING_WM_CHECK(WINDOW): window id # 0x20\n'] * 2 + [
                '_NET_WM_NAME(UTF8_STRING) = "Openbox"\n', '_OPENBOX_PID(CARDINAL) = 123\n']
        for index, invalid in ((-1, None), (0, "no such atom"),
                               (1, '_NET_SUPPORTING_WM_CHECK(WINDOW): window id # 0x21\n'),
                               (2, '_NET_WM_NAME(UTF8_STRING) = "Other"\n'),
                               (3, '_OPENBOX_PID(CARDINAL) = 124\n')):
            fixture = good.copy()
            if index != -1:
                fixture[index] = invalid
            session = Mock()
            session.command.side_effect = fixture
            if index == -1:
                self.assertEqual(ci.probe_wm(session, 123)["pid"], 123)
            else:
                with self.subTest(index=index), self.assertRaises(ValueError):
                    ci.probe_wm(session, 123)

    def test_real_blocking_probe_times_out_and_reaps_child_without_display(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            report = dict(commands=[], processes=[], readiness={})
            session = ci.Session(folder, folder, dict(os.environ), report)
            started = time.monotonic()
            try:
                with self.assertRaisesRegex(ValueError, "readiness timed out"):
                    session.ready("fixture", lambda: session.command("probe", [sys.executable, "-c",
                                  "import time; time.sleep(3600)"], timeout=5), seconds=.15)
            finally:
                self.assertEqual(session.close(), [])
            self.assertLess(time.monotonic() - started, 2)
            record = report["commands"][0]
            self.assertEqual(record["status"], "timeout")
            self.assertEqual(record["exitCode"], -ci.signal.SIGKILL)
            self.assertLessEqual(record["timeoutSeconds"], .15)

    def test_teardown_attempts_all_owned_groups_after_one_raises(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            first, second = Mock(returncode=None), Mock(returncode=None)
            report = dict(commands=[], processes=[dict(name="first"), dict(name="second")], readiness={})
            session = ci.Session(folder, folder, {}, report)
            session.processes = [("first", first), ("second", second)]
            with patch.object(ci.acceptance, "stop_process_tree", side_effect=[OSError("kill failed"), None]) as stop:
                errors = session.close()
            self.assertEqual(stop.call_count, 2)
            self.assertEqual([call.args[0] for call in stop.call_args_list], [second, first])
            self.assertEqual(len(errors), 1)
            self.assertIn("second: OSError: kill failed", errors[0])

    def test_real_owned_process_is_killed_and_reaped_without_display(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            report = dict(commands=[], processes=[], readiness={})
            session = ci.Session(folder, folder, dict(os.environ), report)
            process = session.start("fixture", [sys.executable, "-c", "import time; time.sleep(3600)"])
            session.healthy()
            self.assertEqual(session.close(), [])
            with self.assertRaises(ProcessLookupError):
                os.kill(process.pid, 0)
            self.assertEqual(report["processes"][0]["exitCode"], -ci.signal.SIGKILL)


if __name__ == "__main__":
    unittest.main()
