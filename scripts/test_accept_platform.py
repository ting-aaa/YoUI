# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Fake subprocess orchestration/JSON checks; these are not GPU/platform evidence."""
from __future__ import annotations

import argparse
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("accept_platform", Path(__file__).with_name("accept_platform.py"))
accept = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(accept)
SHA = "0123456789abcdef0123456789abcdef01234567"
OS = 'Test OS "quoted" \\ host'


def benchmark(configuration="Release", backend="vulkan", kind="Cpu"):
    sample = dict(frame=0, managedBuildMs=1.25, nativePrepareMs=.5, nativeSubmitMs=.25,
                  **{field: 1 for field in accept.COUNTERS})
    samples = [{**sample, "frame": index} for index in range(240)]
    result = dict(schemaVersion=2, configuration=configuration, requestedBackend=backend,
                  backend=accept.BACKENDS[backend], adapter=f'Fake "GPU" \\ unit ({accept.BACKENDS[backend]}; {kind}; driver fake 1)',
                  adapterKind=accept.ADAPTER_KINDS[kind], warmupFrames=30, measuredFrames=240,
                  runtime=".NET fixture", os=OS, processArchitecture="X64", samples=samples,
                  resolution="1280x820", dataItems=100000, realizedRows=6, simulationStepSeconds=1.0 / 60, processorCount=4,
                  managedBuildMedianMs=1.25, managedBuildP95Ms=1.25, nativePrepareMedianMs=.5,
                  nativePrepareP95Ms=.5, nativeSubmitMedianMs=.25, nativeSubmitP95Ms=.25,
                  managedAllocatedBytes=24000, managedAllocatedBytesPerFrame=100,
                  layoutPassesDuringAnimation=240, percentileMethod="sorted[floor((N - 1) * p)]",
                  resourceUsageBefore={field: 1 for field in accept.RESOURCE_COUNTERS},
                  resourceUsageAfter={field: 2 for field in accept.RESOURCE_COUNTERS})
    result.update({field: 1 for field in ("Commands", "Instances", "DrawCalls", "Layers", "UploadBytes")})
    return result


class Fixture:
    def __init__(self, root, *, failure=None, timeout=None, interrupt=None, omit_record=False,
                 record_change=None, report_change=None, omit_benchmark=False, kind="Cpu"):
        self.root = root
        self.commands = []
        self.failure = failure
        self.timeout = timeout
        self.interrupt = interrupt
        self.omit_record = omit_record
        self.record_change = record_change
        self.report_change = report_change
        self.omit_benchmark = omit_benchmark
        self.kind = kind
        self.killed = []
        for name in ("global.json", "rust-toolchain.toml"):
            (root / name).write_text("fake toolchain pin; not real build evidence", encoding="utf-8")

    def popen(self, command, **options):
        self.commands.append((command, options))
        if "-File" in command:
            layer = command[command.index("-Layer") + 1]
            config = command[command.index("-Configuration") + 1]
            code = 23 if layer == self.failure else 0
            native = {"Windows": "youi_render.dll", "Linux": "libyoui_render.so", "Darwin": "libyoui_render.dylib"}[accept.platform.system()]
            managed = self.root / "samples/YoUI.Showcase/bin" / config / "net10.0"
            cargo = self.root / "target" / config.lower()
            managed.mkdir(parents=True, exist_ok=True)
            cargo.mkdir(parents=True, exist_ok=True)
            for path in (managed / "YoUI.Showcase.dll", managed / "YoUI.dll", managed / native, cargo / native):
                path.write_bytes(b"fixture binary; never executed")
            now = accept.utc_now()
            record = dict(sha=SHA, dirty=True, configuration=config, layer=layer, platform=OS,
                          startedUtc=now, completedUtc=now, status="passed" if code == 0 else "failed", error=None)
            if self.record_change:
                self.record_change(record)
            if not self.omit_record:
                directory = self.root / "artifacts/verification" / f"fresh-{layer}"
                directory.mkdir(parents=True)
                (directory / "result.json").write_text(json.dumps(record), encoding="utf-8")
            if layer == "Gpu" and not self.omit_benchmark:
                directory = Path(options["env"]["YOUI_ARTIFACT_DIR"])
                directory.mkdir(parents=True)
                report = benchmark(config, options["env"]["YOUI_BACKEND"], self.kind)
                if self.report_change:
                    self.report_change(report)
                (directory / "benchmark.json").write_text(json.dumps(report), encoding="utf-8")
            output = f"Fake {layer} output; never GPU evidence."
        elif command[:3] == ["git", "rev-parse", "HEAD"]:
            layer, code, output = "git-sha", 0, SHA
        elif command[:2] == ["git", "status"]:
            layer, code, output = "git-dirty", 0, '?? path with spaces and "quotes"'
        else:
            layer, code, output = command[0], 0, "test version 1.0"
        options["stdout"].write(output + "\n")
        options["stdout"].flush()
        fixture = self

        class Process:
            pid = 987654
            returncode = None
            waits = 0

            def wait(self, timeout):
                self.waits += 1
                if self.waits == 1 and layer == fixture.timeout:
                    raise subprocess.TimeoutExpired(command, timeout)
                if self.waits == 1 and layer == fixture.interrupt:
                    raise KeyboardInterrupt
                self.returncode = code
                return code

            def kill(self):
                fixture.killed.append(self.pid)
                self.returncode = -9

        return Process()


class AcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="youi-acceptance-")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / "repo with spaces"
        (self.root / "scripts").mkdir(parents=True)
        self.output = self.base / "fresh output"
        self.args = argparse.Namespace(output=self.output, configuration="Release", backend="vulkan",
                                       layers=["Logic", "Gpu"], timeout_seconds=15,
                                       powershell="custom pwsh", require_hardware=False)

    def run_fixture(self, fixture):
        with patch.object(accept.subprocess, "Popen", side_effect=fixture.popen), contextlib.redirect_stdout(io.StringIO()):
            code = accept.run_acceptance(self.args, self.root)
        return code, json.loads((self.output / "result.json").read_text(encoding="utf-8"))

    def test_explicit_profiles_backends_paths_and_fresh_evidence(self):
        for config in ("Debug", "Release"):
            for backend in accept.BACKENDS:
                with self.subTest(config=config, backend=backend):
                    self.output = self.base / f"fresh {config} {backend}"
                    self.args.output = self.output
                    self.args.configuration = config
                    self.args.backend = backend
                    # Each run gets an independent fake repository to avoid fixture record collisions.
                    self.root = self.base / f"repo {config} {backend}"
                    (self.root / "scripts").mkdir(parents=True)
                    fixture = Fixture(self.root)
                    code, result = self.run_fixture(fixture)
                    self.assertEqual(code, 0, result)
                    self.assertEqual(result["status"], "passed")
                    self.assertEqual(result["source"]["sha"], SHA)
                    self.assertTrue(result["source"]["dirty"])
                    self.assertEqual(result["adapter"]["classification"], "software")
                    self.assertEqual(result["adapter"]["backend"], accept.BACKENDS[backend])
                    gates = [cmd for cmd, _ in fixture.commands if "-File" in cmd]
                    self.assertEqual([cmd[-1] for cmd in gates], ["Logic", "Gpu"])
                    self.assertTrue(all(cmd[-3] == config for cmd in gates))
                    self.assertTrue(all(options["cwd"] == self.root for _, options in fixture.commands))
                    for _, options in fixture.commands:
                        self.assertEqual(options["env"]["YOUI_BACKEND"], backend)
                        self.assertEqual(options["env"]["YOUI_ARTIFACT_DIR"], str(self.output / "gpu-artifacts"))
                        self.assertEqual(options["env"]["DOTNET_CLI_TELEMETRY_OPTOUT"], "1")
                        self.assertEqual(options["env"]["POWERSHELL_TELEMETRY_OPTOUT"], "1")
                    self.assertIn('"GPU" \\ unit', result["adapter"]["adapter"])
                    self.assertEqual(len(result["artifacts"][0]["sha256"]), 64)
                    self.assertEqual(len(list(self.output.glob("*-verification-*.json"))), 2)

    def test_existing_even_empty_output_is_rejected_without_subprocess(self):
        self.output.mkdir()
        with patch.object(accept.subprocess, "Popen") as popen, contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(accept.run_acceptance(self.args, self.root), 2)
        popen.assert_not_called()
        self.assertEqual(list(self.output.iterdir()), [])

    def test_failed_gate_keeps_records_and_stops_later_layers(self):
        fixture = Fixture(self.root, failure="Logic")
        code, result = self.run_fixture(fixture)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["commands"][-1]["exitCode"], 23)
        self.assertEqual(result["layers"][0]["verificationRecords"][0]["result"]["status"], "failed")
        self.assertFalse(any(cmd[-1] == "Gpu" for cmd, _ in fixture.commands))

    def test_exit_zero_without_new_verification_is_failure(self):
        old = self.root / "artifacts/verification/old"
        old.mkdir(parents=True)
        (old / "result.json").write_text('{"status":"passed"}')
        code, result = self.run_fixture(Fixture(self.root, omit_record=True))
        self.assertEqual(code, 1)
        self.assertIn("exactly one fresh", result["error"])
        self.assertEqual(result["layers"][0]["status"], "failed")

    def test_contradictory_and_stale_gate_record_is_failure(self):
        changes = {"sha": "0" * 40, "dirty": False, "configuration": "Debug", "layer": "Window",
                   "status": "failed", "error": "unexpected", "platform": "",
                   "startedUtc": "2000-01-01T00:00:00+00:00"}
        for field, value in changes.items():
            with self.subTest(field=field):
                self.root = self.base / field
                self.root.mkdir()
                self.args.output = self.output = self.base / f"output {field}"
                code, result = self.run_fixture(Fixture(self.root, record_change=lambda r: r.update({field: value})))
                self.assertEqual(code, 1)
                self.assertEqual(result["layers"][0]["status"], "failed")

    def test_gpu_without_fresh_benchmark_is_failure(self):
        self.args.layers = ["Gpu"]
        (self.root / "artifacts").mkdir()
        (self.root / "artifacts/benchmark.json").write_text(json.dumps(benchmark()))
        code, result = self.run_fixture(Fixture(self.root, omit_benchmark=True))
        self.assertEqual(code, 1)
        self.assertIsNone(result["adapter"])

    def test_truncated_benchmark_kept_and_marked_failed(self):
        self.args.layers = ["Gpu"]
        code, result = self.run_fixture(Fixture(self.root, report_change=lambda r: r["samples"].pop()))
        self.assertEqual(code, 1)
        self.assertIn("all 240", result["error"])
        self.assertTrue((self.output / "gpu-artifacts/benchmark.json").is_file())
        self.assertEqual(result["layers"][0]["status"], "failed")

    def test_hardware_required_preserves_actual_software_identity(self):
        self.args.layers = ["Gpu"]
        self.args.require_hardware = True
        code, result = self.run_fixture(Fixture(self.root))
        self.assertEqual(code, 1)
        self.assertEqual(result["adapter"]["classification"], "software")
        self.assertIn("hardware adapter required", result["error"])

    def test_discrete_adapter_can_satisfy_explicit_hardware_requirement(self):
        self.args.layers = ["Gpu"]
        self.args.require_hardware = True
        code, result = self.run_fixture(Fixture(self.root, kind="DiscreteGpu"))
        self.assertEqual(code, 0)
        self.assertEqual(result["adapter"]["classification"], "hardware")

    def test_logic_only_never_claims_adapter_or_gpu_acceptance(self):
        self.args.layers = ["Logic"]
        code, result = self.run_fixture(Fixture(self.root))
        self.assertEqual(code, 0)
        self.assertIsNone(result["adapter"])
        self.assertEqual([item["layer"] for item in result["layers"]], ["Logic"])

    def test_timeout_stops_process_tree_and_preserves_partial_evidence(self):
        with patch.object(accept, "stop_process_tree", side_effect=lambda process: process.kill()) as stop:
            code, result = self.run_fixture(Fixture(self.root, timeout="Logic"))
        self.assertEqual(code, 1)
        stop.assert_called_once()
        self.assertEqual(result["commands"][-1]["status"], "timeout")
        self.assertEqual(result["layers"][0]["status"], "timeout")
        self.assertTrue(result["layers"][0]["verificationRecords"])

    def test_interrupt_stops_process_tree_and_keeps_interrupted_status(self):
        with patch.object(accept, "stop_process_tree", side_effect=lambda process: process.kill()) as stop:
            code, result = self.run_fixture(Fixture(self.root, interrupt="Logic"))
        self.assertEqual(code, 130)
        stop.assert_called_once()
        self.assertEqual(result["status"], "interrupted")
        self.assertEqual(result["commands"][-1]["status"], "interrupted")

    def test_launch_failure_is_persisted(self):
        Fixture(self.root)  # Create only toolchain-pin fixtures.
        with patch.object(accept.subprocess, "Popen", side_effect=FileNotFoundError("missing git")), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(accept.run_acceptance(self.args, self.root), 1)
        result = json.loads((self.output / "result.json").read_text())
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["commands"][0]["status"], "launch-failed")

    def test_changed_source_sha_is_not_accepted(self):
        fixture = Fixture(self.root)
        original = fixture.popen
        calls = 0

        def popen(command, **options):
            nonlocal calls
            process = original(command, **options)
            if command[:3] == ["git", "rev-parse", "HEAD"]:
                calls += 1
                if calls == 2:
                    options["stdout"].seek(0)
                    options["stdout"].truncate()
                    options["stdout"].write("f" * 40 + "\n")
            return process

        fixture.popen = popen
        code, result = self.run_fixture(fixture)
        self.assertEqual(code, 1)
        self.assertEqual(result["sourceAfter"]["sha"], "f" * 40)
        self.assertIn("changed during acceptance", result["error"])

    def test_binary_hash_mismatch_cannot_pass(self):
        fixture = Fixture(self.root)
        original = fixture.popen

        def popen(command, **options):
            process = original(command, **options)
            if "-File" in command:
                next((self.root / "target/release").glob("*youi_render*")).write_bytes(b"different binary")
            return process

        fixture.popen = popen
        code, result = self.run_fixture(fixture)
        self.assertEqual(code, 1)
        self.assertIn("copied native library differs", result["error"])

    def test_process_tree_cleanup_uses_posix_group(self):
        class Process:
            pid = 123
            def wait(self, timeout):
                self.timeout = timeout
        process = Process()
        with patch.object(accept.os, "name", "posix"), patch.object(accept.os, "killpg", create=True) as kill:
            self.assertIsNone(accept.stop_process_tree(process))
            kill.assert_called_once_with(123, accept.signal.SIGKILL)
        self.assertEqual(process.timeout, 15)

    def test_process_tree_cleanup_uses_windows_taskkill(self):
        class Process:
            pid = 123
            def wait(self, timeout):
                self.timeout = timeout
        process = Process()
        with patch.object(accept.os, "name", "nt"), patch.object(accept.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
            self.assertIsNone(accept.stop_process_tree(process))
            self.assertEqual(run.call_args.args[0], ["taskkill", "/PID", "123", "/T", "/F"])
            self.assertEqual(run.call_args.kwargs["timeout"], 15)

    def test_rust_toolchain_environment_override_is_rejected(self):
        Fixture(self.root)
        with patch.dict(accept.os.environ, {"RUSTUP_TOOLCHAIN": "nightly"}), patch.object(accept.subprocess, "Popen") as popen, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(accept.run_acceptance(self.args, self.root), 1)
        popen.assert_not_called()
        result = json.loads((self.output / "result.json").read_text())
        self.assertEqual(result["status"], "failed")
        self.assertIn("RUSTUP_TOOLCHAIN", result["error"])

    def test_invalid_cli_is_rejected_before_any_process(self):
        for extras in (("--layers", "Window"), ("--layers", "Gpu", "Gpu"), ("--layers", "Gpu", "Logic"), ("--timeout-seconds", "0"),
                       ("--timeout-seconds", "86401"), ("--layers", "Logic", "--require-hardware"), ("--unknown",)):
            with self.subTest(extras=extras), patch.object(accept.subprocess, "Popen") as popen, contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as exception:
                    accept.main(["--configuration", "Release", "--backend", "vulkan", *extras])
                self.assertEqual(exception.exception.code, 2)
                popen.assert_not_called()


class BenchmarkValidationTests(unittest.TestCase):
    def validate(self, report):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "benchmark.json"
            path.write_text(json.dumps(report), encoding="utf-8")
            return accept.validate_benchmark(path, "Release", "vulkan", OS)

    def test_strict_fields_missing_and_contradictory(self):
        for field, value in (("schemaVersion", 1), ("configuration", "Debug"), ("requestedBackend", "gl"),
                             ("backend", "Gl"), ("adapter", ""), ("adapterKind", "discrete"), ("runtime", ""),
                             ("os", "wrong OS"), ("processArchitecture", ""), ("warmupFrames", 29),
                             ("measuredFrames", 239), ("resourceUsageBefore", {}), ("resourceUsageAfter", None)):
            with self.subTest(field=field):
                report = benchmark()
                report[field] = value
                with self.assertRaises(ValueError):
                    self.validate(report)
                del report[field]
                with self.assertRaises(ValueError):
                    self.validate(report)

    def test_workload_metadata_missing_wrong_and_invalid_values_fail(self):
        invalid = {
            "resolution": ("1x1", "", 1280820, True, None),
            "dataItems": (0, -1, 99999, 100000.0, True, "100000", None, float("nan"), float("inf")),
            "realizedRows": (0, -1, 100001, 1.5, 6.0, True, "6", None, float("nan"), float("inf")),
            "simulationStepSeconds": (0, -1, 1.0 / 30, True, "1/60", None, float("nan"), float("inf")),
            "processorCount": (0, -1, 1.5, 4.0, True, "4", None, float("nan"), float("inf")),
        }
        for field, values in invalid.items():
            report = benchmark()
            del report[field]
            with self.subTest(field=field, missing=True), self.assertRaises(ValueError):
                self.validate(report)
            for value in values:
                report = benchmark()
                report[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    self.validate(report)

    def test_workload_bounds_do_not_invent_a_virtualization_or_cpu_target(self):
        for rows in (1, 6, 100000):
            report = benchmark()
            report.update(realizedRows=rows, processorCount=1)
            self.assertEqual(self.validate(report)["classification"], "software")

    def test_samples_require_all_finite_nonnegative_values_and_integer_counts(self):
        for field in (*accept.TIMINGS, *accept.COUNTERS):
            for value in (None, -1, float("nan"), float("inf"), True, "1"):
                with self.subTest(field=field, value=value):
                    report = benchmark()
                    report["samples"][3][field] = value
                    with self.assertRaises(ValueError):
                        self.validate(report)
        for field in accept.COUNTERS:
            report = benchmark()
            report["samples"][3][field] = 1.5
            with self.assertRaises(ValueError):
                self.validate(report)

    def test_duplicate_missing_and_reordered_samples_fail(self):
        for transform in (lambda s: s.pop(), lambda s: s.append(copy.copy(s[-1])),
                          lambda s: s.__setitem__(3, copy.copy(s[2])), lambda s: s.reverse()):
            report = benchmark()
            transform(report["samples"])
            with self.assertRaises(ValueError):
                self.validate(report)

    def test_summary_must_match_samples_and_allocation(self):
        for field in (*accept.SUMMARIES, "Commands", "managedAllocatedBytes", "layoutPassesDuringAnimation", "percentileMethod"):
            with self.subTest(field=field):
                report = benchmark()
                report[field] = "wrong" if field == "percentileMethod" else report[field] + 1
                with self.assertRaises(ValueError):
                    self.validate(report)

    def test_resource_counter_contract_is_not_an_arbitrary_object(self):
        for field in accept.RESOURCE_COUNTERS:
            report = benchmark()
            del report["resourceUsageBefore"][field]
            with self.assertRaises(ValueError):
                self.validate(report)

    def test_integer_metadata_rejects_floats_and_booleans(self):
        for field in ("schemaVersion", "warmupFrames", "measuredFrames"):
            for value in (float(benchmark()[field]), True):
                report = benchmark()
                report[field] = value
                with self.assertRaises(ValueError):
                    self.validate(report)
        report = benchmark()
        report["samples"][0]["frame"] = 0.0
        with self.assertRaises(ValueError):
            self.validate(report)

    def test_virtual_and_other_do_not_count_as_hardware(self):
        for kind in ("VirtualGpu", "Other"):
            self.assertEqual(self.validate(benchmark(kind=kind))["classification"], "unclassified")


if __name__ == "__main__":
    unittest.main()
