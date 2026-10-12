# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Bounded host-specific Logic/Gpu acceptance; never runs native windows.

uv run scripts/accept_platform.py --configuration Release --backend vulkan
The result describes this checkout, host and adapter only, including software GPUs.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
BACKENDS = {"dx12": "Dx12", "vulkan": "Vulkan", "gl": "Gl", "metal": "Metal"}
ADAPTER_KINDS = {"Cpu": "cpu", "IntegratedGpu": "integrated", "DiscreteGpu": "discrete",
                 "VirtualGpu": "virtual", "Other": "other"}
TIMINGS = ("managedBuildMs", "nativePrepareMs", "nativeSubmitMs")
COUNTERS = ("commands", "instances", "drawCalls", "layers", "uploadBytes", "atlasGlyphs",
            "textCacheHits", "layoutPasses")
RESOURCE_COUNTERS = ("atlasBytes", "outputBytes", "layerBytes", "instanceBufferBytes", "layerCount")
SUMMARIES = ("managedBuildMedianMs", "managedBuildP95Ms", "nativePrepareMedianMs",
             "nativePrepareP95Ms", "nativeSubmitMedianMs", "nativeSubmitP95Ms",
             "managedAllocatedBytesPerFrame")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def write_report(output: Path, report: dict) -> None:
    temporary = output / "result.json.tmp"
    temporary.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(output / "result.json")


def stop_process_tree(process: subprocess.Popen) -> str | None:
    """Kill the gate's children as well as PowerShell; report failed cleanup."""
    error = None
    try:
        if os.name == "nt":
            cleanup = subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                     capture_output=True, text=True, timeout=15, check=False)
            if cleanup.returncode != 0:
                error = f"taskkill exited {cleanup.returncode}: {cleanup.stdout} {cleanup.stderr}"
                process.kill()
        else:
            os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    except (OSError, subprocess.SubprocessError) as exc:
        error = str(exc)
        process.kill()
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        error = (error or "") + " process did not exit after tree termination"
    return error


def run_command(command: list[str], *, root: Path, output: Path, name: str,
                env: dict[str, str], timeout: int, report: dict) -> dict:
    result = dict(name=name, command=command, status="running", startedUtc=utc_now(),
                  completedUtc=None, exitCode=None, log=f"{name}.log", timeoutSeconds=timeout)
    report["commands"].append(result)
    write_report(output, report)
    start = time.monotonic()
    process = None
    try:
        with (output / result["log"]).open("w", encoding="utf-8") as stream:
            process = subprocess.Popen(command, cwd=root, env=env, stdout=stream,
                                       stderr=subprocess.STDOUT, start_new_session=os.name != "nt",
                                       creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
            try:
                result["exitCode"] = process.wait(timeout=timeout)
                result["status"] = "passed" if result["exitCode"] == 0 else "failed"
            except subprocess.TimeoutExpired:
                result["status"] = "timeout"
                result["cleanupError"] = stop_process_tree(process)
                result["exitCode"] = process.returncode
            except KeyboardInterrupt:
                result["status"] = "interrupted"
                result["cleanupError"] = stop_process_tree(process)
                result["exitCode"] = process.returncode
                raise
    except OSError as exc:
        result["status"] = "launch-failed"
        result["error"] = str(exc)
    finally:
        result["seconds"] = round(time.monotonic() - start, 3)
        result["completedUtc"] = utc_now()
        write_report(output, report)
    return result


def require_success(result: dict) -> None:
    if result["status"] != "passed":
        raise ValueError(f"{result['name']}: {result['status']}; inspect {result['log']}")


def file_evidence(path: Path, relative_to: Path) -> dict:
    with path.open("rb") as stream:
        checksum = hashlib.file_digest(stream, "sha256").hexdigest()
    return dict(path=str(path.relative_to(relative_to)), bytes=path.stat().st_size, sha256=checksum)


def binary_evidence(root: Path, configuration: str) -> list[dict]:
    native = {"Windows": "youi_render.dll", "Linux": "libyoui_render.so", "Darwin": "libyoui_render.dylib"}.get(platform.system())
    if native is None:
        raise ValueError("No host-native library naming contract for this host")
    managed = root / "samples/YoUI.Showcase/bin" / configuration / "net10.0"
    paths = [managed / "YoUI.Showcase.dll", managed / "YoUI.dll", managed / native,
             root / "target" / configuration.lower() / native]
    records = [file_evidence(path, root) for path in paths]
    if any(record["bytes"] == 0 for record in records):
        raise ValueError("A built binary is empty")
    if records[2]["sha256"] != records[3]["sha256"]:
        raise ValueError("Showcase's copied native library differs from the matching Cargo profile")
    return records


def number(value: object, label: str, *, integer: bool = False) -> None:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or (isinstance(value, float) and not math.isfinite(value)) or value < 0:
        raise ValueError(f"{label} must be a finite nonnegative number")
    if integer and not isinstance(value, int):
        raise ValueError(f"{label} must be an integer")


def strict_json(text: str) -> object:
    def reject_constant(value: str) -> None:
        raise ValueError(f"Non-finite JSON constant: {value}")

    def unique_keys(pairs: list[tuple[str, object]]) -> dict:
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"Duplicate JSON key: {key}")
            result[key] = value
        return result

    return json.loads(text, parse_constant=reject_constant, object_pairs_hook=unique_keys)


def validate_benchmark(path: Path, configuration: str, backend: str,
                       expected_os: str) -> dict:
    # Fresh output ownership, sample counts and the gate's own completion record
    # establish completion. Exit zero alone (or an old benchmark file) does not.
    benchmark = strict_json(path.read_text(encoding="utf-8-sig"))
    if not isinstance(benchmark, dict) or benchmark.get("schemaVersion") != 2 or type(benchmark.get("schemaVersion")) is not int:
        raise ValueError("benchmark schemaVersion must be 2")
    for field, expected in (("configuration", configuration), ("requestedBackend", backend),
                            ("backend", BACKENDS[backend]), ("os", expected_os), ("warmupFrames", 30), ("measuredFrames", 240)):
        if benchmark.get(field) != expected or isinstance(benchmark.get(field), bool):
            raise ValueError(f"benchmark {field} does not match {expected!r}")
    for field in ("warmupFrames", "measuredFrames"):
        number(benchmark.get(field), field, integer=True)
    # Workload identity is part of evidence, not an optional descriptive label.
    # Keep fixed scene inputs exact, while checking realized virtualization output
    # by its meaningful bounds rather than imposing a performance threshold.
    if benchmark.get("resolution") != "1280x820":
        raise ValueError("benchmark resolution must be 1280x820")
    for field in ("dataItems", "realizedRows", "processorCount"):
        number(benchmark.get(field), field, integer=True)
        if benchmark[field] < 1:
            raise ValueError(f"benchmark {field} must be positive")
    if benchmark["dataItems"] != 100000:
        raise ValueError("benchmark dataItems must be 100000")
    if benchmark["realizedRows"] > benchmark["dataItems"]:
        raise ValueError("benchmark realizedRows exceeds dataItems")
    number(benchmark.get("simulationStepSeconds"), "simulationStepSeconds")
    if benchmark["simulationStepSeconds"] != 1.0 / 60:
        raise ValueError("benchmark simulationStepSeconds must be 1/60")
    adapter = benchmark.get("adapter")
    if not isinstance(adapter, str) or not adapter.strip():
        raise ValueError("benchmark adapter identity is missing")
    identity = re.search(r"\((Dx12|Vulkan|Gl|Metal); (Cpu|IntegratedGpu|DiscreteGpu|VirtualGpu|Other); driver .+\)$", adapter)
    if not identity or identity[1] != BACKENDS[backend]:
        raise ValueError("benchmark adapter identity contradicts the requested backend")
    kind = ADAPTER_KINDS[identity[2]]
    if benchmark.get("adapterKind") != kind:
        raise ValueError("benchmark adapterKind contradicts the actual adapter identity")
    classification = "software" if kind == "cpu" else "hardware" if kind in ("integrated", "discrete") else "unclassified"
    for field in ("runtime", "os", "processArchitecture"):
        if not isinstance(benchmark.get(field), str) or not benchmark[field].strip():
            raise ValueError(f"benchmark {field} is missing")
    samples = benchmark.get("samples")
    if not isinstance(samples, list) or len(samples) != 240:
        raise ValueError("benchmark must contain all 240 measured samples")
    for index, sample in enumerate(samples):
        if not isinstance(sample, dict) or sample.get("frame") != index or type(sample.get("frame")) is not int:
            raise ValueError(f"benchmark samples must have consecutive frame indices: {index}")
        for field in TIMINGS:
            number(sample.get(field), f"samples[{index}].{field}")
        for field in COUNTERS:
            number(sample.get(field), f"samples[{index}].{field}", integer=True)
    for field in SUMMARIES:
        number(benchmark.get(field), field)
    for field in ("resourceUsageBefore", "resourceUsageAfter"):
        if not isinstance(benchmark.get(field), dict) or not benchmark[field]:
            raise ValueError(f"benchmark {field} is missing")
        for key in RESOURCE_COUNTERS:
            number(benchmark[field].get(key), f"{field}.{key}", integer=True)
    if benchmark.get("percentileMethod") != "sorted[floor((N - 1) * p)]":
        raise ValueError("benchmark percentile method is missing or unsupported")
    for field, prefix in zip(TIMINGS, ("managedBuild", "nativePrepare", "nativeSubmit")):
        ordered = sorted(sample[field] for sample in samples)
        for suffix, percentile in (("MedianMs", .5), ("P95Ms", .95)):
            if benchmark[prefix + suffix] != ordered[int((len(ordered) - 1) * percentile)]:
                raise ValueError(f"benchmark {prefix + suffix} contradicts its samples")
    number(benchmark.get("managedAllocatedBytes"), "managedAllocatedBytes", integer=True)
    if not math.isclose(benchmark["managedAllocatedBytesPerFrame"], benchmark["managedAllocatedBytes"] / len(samples), rel_tol=1e-12, abs_tol=1e-12):
        raise ValueError("benchmark allocation summary contradicts its total")
    for key in ("Commands", "Instances", "DrawCalls", "Layers", "UploadBytes"):
        number(benchmark.get(key), key, integer=True)
        if benchmark[key] != samples[-1][key[0].lower() + key[1:]]:
            raise ValueError(f"benchmark {key} contradicts its final sample")
    number(benchmark.get("layoutPassesDuringAnimation"), "layoutPassesDuringAnimation", integer=True)
    if benchmark["layoutPassesDuringAnimation"] != sum(sample["layoutPasses"] for sample in samples):
        raise ValueError("benchmark layout summary contradicts its samples")
    return dict(adapter=adapter, adapterKind=kind, backend=identity[1], classification=classification,
                benchmark="gpu-artifacts/benchmark.json", measuredFrames=len(samples),
                warmupFrames=benchmark["warmupFrames"])


def collect_verification(root: Path, output: Path, previous: set[Path], layer: str) -> list[dict]:
    records = []
    for source in sorted(set((root / "artifacts/verification").glob("*/result.json")) - previous):
        destination = output / f"{layer.lower()}-verification-{source.parent.name}.json"
        # Keep original bytes, including a malformed or failed gate's record.
        destination.write_bytes(source.read_bytes())
        item = dict(source=str(source), copy=destination.name)
        try:
            item["result"] = strict_json(destination.read_text(encoding="utf-8-sig"))
        except (ValueError, UnicodeError) as exc:
            item["error"] = str(exc)
        records.append(item)
    return records


def validate_verification(records: list[dict], layer: str, configuration: str, source: dict, command: dict) -> None:
    # Concurrent unrelated verifier runs are not accepted as this gate's proof.
    if len(records) != 1 or not isinstance(records[0].get("result"), dict):
        raise ValueError(f"{layer} did not produce exactly one fresh verification record")
    record = records[0]["result"]
    for key, expected in (("layer", layer), ("configuration", configuration), ("sha", source["sha"]),
                          ("dirty", source["dirty"]), ("status", "passed")):
        if record.get(key) != expected:
            raise ValueError(f"{layer} verification record {key} does not match {expected!r}")
    if type(record.get("dirty")) is not bool or not isinstance(record.get("platform"), str) or not record["platform"].strip():
        raise ValueError(f"{layer} verification has invalid dirty/platform metadata")
    if record.get("error") is not None:
        raise ValueError(f"{layer} verification record contains an error")
    dates = {}
    for field in ("startedUtc", "completedUtc"):
        value = record.get(field)
        if not isinstance(value, str) or not value:
            raise ValueError(f"{layer} verification record has no {field}")
        dates[field] = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if dates[field].tzinfo is None:
            raise ValueError(f"{layer} verification {field} lacks a timezone")
    if dates["completedUtc"] < dates["startedUtc"]:
        raise ValueError(f"{layer} verification completion precedes its start")
    # Allow filesystem/process timestamp granularity, not records from prior runs.
    if dates["startedUtc"] < datetime.fromisoformat(command["startedUtc"]) - timedelta(seconds=2) or dates["completedUtc"] > datetime.fromisoformat(command["completedUtc"]) + timedelta(seconds=2):
        raise ValueError(f"{layer} verification timestamps are outside this gate's execution")


def run_acceptance(args: argparse.Namespace, root: Path = ROOT) -> int:
    output = args.output.resolve() if args.output else root / "artifacts/platform-acceptance" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8])
    try:
        output.mkdir(parents=True, exist_ok=False)
    except FileExistsError:
        print(f"Refusing existing output directory (including empty directories): {output}", file=sys.stderr)
        return 2
    env = {**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "POWERSHELL_TELEMETRY_OPTOUT": "1",
           "YOUI_BACKEND": args.backend, "YOUI_TRACE": "1", "YOUI_ARTIFACT_DIR": str(output / "gpu-artifacts")}
    report = dict(schemaVersion=1, status="running", startedUtc=utc_now(), completedUtc=None,
                  configuration=args.configuration, requestedBackend=args.backend,
                  requestedLayers=args.layers, requireHardware=args.require_hardware,
                  host=dict(os=platform.system(), release=platform.release(), version=platform.version(),
                            machine=platform.machine(), python=sys.version),
                  environment={key: env[key] for key in ("DOTNET_CLI_TELEMETRY_OPTOUT", "POWERSHELL_TELEMETRY_OPTOUT",
                      "YOUI_BACKEND", "YOUI_TRACE", "YOUI_ARTIFACT_DIR", "YOUI_ANGLE_PLATFORM", "VK_ICD_FILENAMES",
                      "VK_DRIVER_FILES", "LIBGL_ALWAYS_SOFTWARE", "WGPU_BACKEND", "XDG_RUNTIME_DIR", "RUSTUP_TOOLCHAIN") if key in env},
                  source=None, sourceAfter=None, toolchainFiles=[], versions={}, commands=[], layers=[], adapter=None,
                  evidenceScope="This host and selected headless layers only; no native-window, OS input, IME, accessibility or other-platform acceptance.")
    write_report(output, report)
    exit_code = 1
    try:
        if env.get("RUSTUP_TOOLCHAIN"):
            raise ValueError("RUSTUP_TOOLCHAIN overrides the repository pin; unset it before acceptance")
        report["toolchainFiles"] = [file_evidence(root / name, root) for name in ("global.json", "rust-toolchain.toml")]
        probes = [("git-sha", ["git", "rev-parse", "HEAD"]),
                  ("git-dirty", ["git", "status", "--porcelain=v1", "--untracked-files=normal"]),
                  ("rustc", ["rustc", "--version"]), ("cargo", ["cargo", "--version"]),
                  ("dotnet", ["dotnet", "--info"]),
                  ("powershell", [args.powershell, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.ToString()"])]
        values = {}
        for name, command in probes:
            result = run_command(command, root=root, output=output, name=name, env=env,
                                 timeout=min(30, args.timeout_seconds), report=report)
            require_success(result)
            values[name] = (output / result["log"]).read_text(encoding="utf-8").rstrip("\r\n")
            if name == "git-dirty":
                if not re.fullmatch(r"[0-9a-fA-F]{40,64}", values["git-sha"]):
                    raise ValueError("git did not return a valid commit SHA")
                report["source"] = dict(sha=values["git-sha"], dirty=bool(values["git-dirty"]),
                                        statusPorcelain=values["git-dirty"].splitlines())
            elif name not in ("git-sha", "git-dirty"):
                if not values[name]:
                    raise ValueError(f"{name} version output is empty")
                report["versions"][name] = values[name]
        for layer in args.layers:
            case = dict(layer=layer, status="running", verificationRecords=[])
            report["layers"].append(case)
            previous = set((root / "artifacts/verification").glob("*/result.json"))
            try:
                command = [args.powershell, "-NoLogo", "-NoProfile", "-NonInteractive", "-File",
                           str(root / "scripts/verify.ps1"), "-Configuration", args.configuration, "-Layer", layer]
                result = run_command(command, root=root, output=output, name=f"gate-{layer.lower()}", env=env,
                                     timeout=args.timeout_seconds, report=report)
                case["status"] = result["status"]
            finally:
                case["verificationRecords"] = collect_verification(root, output, previous, layer)
                write_report(output, report)
            require_success(result)
            validate_verification(case["verificationRecords"], layer, args.configuration, report["source"], result)
            case["binaryEvidence"] = binary_evidence(root, args.configuration)
            if report["toolchainFiles"] != [file_evidence(root / name, root) for name in ("global.json", "rust-toolchain.toml")]:
                raise ValueError("Toolchain pin files changed during acceptance")
            if layer == "Gpu":
                report["adapter"] = validate_benchmark(output / "gpu-artifacts/benchmark.json", args.configuration,
                                                       args.backend, case["verificationRecords"][0]["result"]["platform"])
                if args.require_hardware and report["adapter"]["classification"] != "hardware":
                    raise ValueError(f"hardware adapter required; actual classification is {report['adapter']['classification']}")
            case["status"] = "passed"
            write_report(output, report)
        after = {}
        for name, command in probes[:2]:
            result = run_command(command, root=root, output=output, name=name + "-after", env=env,
                                 timeout=min(30, args.timeout_seconds), report=report)
            require_success(result)
            after[name] = (output / result["log"]).read_text(encoding="utf-8").rstrip("\r\n")
        report["sourceAfter"] = dict(sha=after["git-sha"], dirty=bool(after["git-dirty"]),
                                    statusPorcelain=after["git-dirty"].splitlines())
        if report["source"] != report["sourceAfter"]:
            raise ValueError("Git SHA or working-tree status changed during acceptance")
        report["status"] = "passed"
        exit_code = 0
    except KeyboardInterrupt:
        report["status"] = "interrupted"
        report["error"] = "Interrupted; retained available command logs and partial records."
        exit_code = 130
    except (OSError, ValueError, TypeError) as exc:
        report["status"] = "failed"
        report["error"] = str(exc)
    finally:
        if report["status"] != "passed" and report["layers"]:
            last = report["layers"][-1]
            if last["status"] in ("passed", "running"):
                last["status"] = report["status"]
        report["completedUtc"] = utc_now()
        report["artifacts"] = [file_evidence(path, output)
                               for path in sorted((output / "gpu-artifacts").glob("*")) if path.is_file()]
        write_report(output, report)
        adapter = report["adapter"]
        description = f"; {adapter['classification']} adapter: {adapter['adapter']}" if adapter else ""
        print(f"{report['status'].upper()}: {', '.join(args.layers)} {args.configuration} on {platform.system()}{description}")
        print(f"Evidence: {output / 'result.json'}", flush=True)
    return exit_code


def bounded_timeout(value: str) -> int:
    result = int(value)
    if not 1 <= result <= 86400:
        raise argparse.ArgumentTypeError("timeout must be between 1 and 86400 seconds")
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", required=True, choices=("Debug", "Release"))
    parser.add_argument("--backend", required=True, choices=tuple(BACKENDS))
    parser.add_argument("--layers", nargs="+", choices=("Logic", "Gpu"), default=["Logic", "Gpu"])
    parser.add_argument("--output", type=Path, help="New directory; existing directories are rejected")
    parser.add_argument("--timeout-seconds", type=bounded_timeout, default=900, help="Per-gate bound including builds; probes are bounded to at most 30 seconds")
    parser.add_argument("--powershell", default="pwsh", help="PowerShell 7 executable or full path")
    parser.add_argument("--require-hardware", action="store_true", help="Fail GPU acceptance for software or unclassified adapters")
    args = parser.parse_args(argv)
    if len(set(args.layers)) != len(args.layers):
        parser.error("duplicate layers are not allowed")
    if args.layers == ["Gpu", "Logic"]:
        parser.error("when selecting both layers, use --layers Logic Gpu")
    if args.require_hardware and "Gpu" not in args.layers:
        parser.error("--require-hardware requires the Gpu layer")
    return run_acceptance(args)


if __name__ == "__main__":
    raise SystemExit(main())
