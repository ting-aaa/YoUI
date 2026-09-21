# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=11,<13", "numpy>=2,<3"]
# ///
"""Run each real GPU backend in bounded, isolated child processes.

uv run scripts/verify_backends.py --backends dx12 vulkan gl
Native and managed Release builds must exist; verify-backends.ps1 builds them.
No renderer fallback is enabled. Results describe this machine only.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import time

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]


def run_case(name: str, command: list[str], directory: Path, env: dict, timeout: int) -> dict:
    start = time.perf_counter()
    log = directory / f"{name}.log"
    with log.open("w", encoding="utf-8") as stream:
        process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=stream,
                                   stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            code = process.wait(timeout=timeout)
            status = "passed" if code == 0 else "failed"
        except subprocess.TimeoutExpired:
            # All validation commands are direct executables, with no shell or
            # build-tool parent whose termination could leave a GPU test behind.
            process.kill()
            code = process.wait()
            status = "timeout"
    result = dict(name=name, status=status, exitCode=code,
                  seconds=round(time.perf_counter() - start, 3), log=str(log.relative_to(ROOT)))
    print(f"{directory.name}/{name}: {status} ({result['seconds']} s)", flush=True)
    return result


def compare(output: Path, baseline: str, names: list[str], baseline_directory: Path | None = None) -> list[dict]:
    comparisons = []
    for name in names:
        if name == baseline:
            continue
        for source in sorted((baseline_directory or output / baseline).glob("*.png")):
            candidate = output / name / source.name
            if not candidate.exists():
                continue
            with Image.open(source) as a, Image.open(candidate) as b:
                aa, bb = np.asarray(a.convert("RGBA")), np.asarray(b.convert("RGBA"))
                if aa.shape != bb.shape:
                    comparisons.append(dict(backend=name, image=source.name, status="size-mismatch"))
                    continue
                diff = np.abs(aa.astype(np.int16) - bb.astype(np.int16))
                maximum = diff.max(axis=2)
                fraction = float((maximum > 4).mean())
                mean = float(diff.mean())
                # Diagnostic tolerance accommodates edge AA/rounding, but
                # cannot replace the absolute color/clip/order reference test.
                passed = mean <= 0.25 and fraction <= 0.001
                comparisons.append(dict(backend=name, image=source.name,
                    status="passed" if passed else "different", maxChannelError=int(diff.max()),
                    meanChannelError=round(mean, 6), changedPixels=int((maximum > 0).sum()),
                    pixelsOver4=int((maximum > 4).sum()), fractionOver4=round(fraction, 8)))
                if source.name in ("editor.png", "showcase-dark.png"):
                    thumb_size = (720, 450 if source.name == "editor.png" else 461)
                    heat = Image.fromarray(np.minimum(maximum * 12, 255).astype(np.uint8)).convert("RGB")
                    sheet = Image.new("RGB", (2160, thumb_size[1] + 32), "#171c26")
                    draw = ImageDraw.Draw(sheet)
                    for index, (pic, label) in enumerate(((a, baseline), (b, name), (heat, "difference x12"))):
                        sheet.paste(pic.convert("RGB").resize(thumb_size), (index * 720, 32))
                        draw.text((index * 720 + 12, 10), label, fill="white")
                    sheet.save(output / f"compare-{name}-{source.stem}.png")
    return comparisons


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--backends", nargs="+", default=["dx12", "vulkan", "gl"], choices=["dx12", "vulkan", "gl", "angle-d3d11", "angle-vulkan"])
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/backends")
    parser.add_argument("--timeout", type=int, default=45)
    parser.add_argument("--angle-build", type=Path)
    parser.add_argument("--baseline", type=Path, help="Existing DX12 evidence directory for an ANGLE-only run")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Resolve the exact test executable from Cargo's build output, never a
    # potentially stale similarly named file left in target/.
    build = subprocess.run(["cargo", "test", "-p", "youi-render", "--release", "--test", "gpu",
                            "--locked", "--no-run", "--message-format=json"], cwd=ROOT,
                           text=True, capture_output=True, check=True)
    gpu = next(json.loads(line)["executable"] for line in build.stdout.splitlines()
               if line.startswith("{") and json.loads(line).get("reason") == "compiler-artifact"
               and json.loads(line).get("target", {}).get("name") == "gpu" and json.loads(line).get("executable"))
    editor = ROOT / "tools/YoUI.Editor/bin/Release/net9.0/YoUI.Editor.exe"
    showcase = ROOT / "samples/YoUI.Showcase/bin/Release/net9.0/YoUI.Showcase.exe"
    report = dict(os=platform.platform(), configuration="Release", timeoutSeconds=args.timeout,
                  tolerance=dict(meanChannelError=0.25, fractionPixelsOver4=0.001), backends=[])
    for backend in args.backends:
        directory = output / backend
        directory.mkdir(exist_ok=True)
        # Do not accept stale screenshots as current-run evidence.
        if any(directory.iterdir()):
            raise RuntimeError(f"Use a fresh output directory; evidence already exists: {directory}")
        native = ROOT / "target/release"
        case_editor, case_showcase, case_gpu = editor, showcase, gpu
        is_angle = backend.startswith("angle-")
        env = {**os.environ, "YOUI_BACKEND": "gl" if is_angle else backend, "YOUI_TRACE": "1", "YOUI_ARTIFACT_DIR": str(directory)}
        if is_angle:
            if args.angle_build is None:
                raise ValueError("ANGLE requires --angle-build from prepare_angle.py")
            angle_build = args.angle_build.resolve()
            native = angle_build / "target/release"
            case_editor = angle_build / "editor/YoUI.Editor.exe"
            case_showcase = angle_build / "showcase/YoUI.Showcase.exe"
            provenance = json.loads((angle_build / "provenance.json").read_text())
            case_gpu = provenance["gpuExecutable"]
            env["YOUI_ANGLE_PLATFORM"] = backend.removeprefix("angle-")
            report["angleBuild"] = str(angle_build)
        cases = [
            ("renderer", [str(native / "examples/render_scene.exe"), str(directory / "render-scene.png")]),
            ("gpu-reference", [case_gpu, "--ignored", "--nocapture"]),
            ("scene", [str(native / "examples/scene_graph.exe"), str(directory / "scene-graph.png")]),
            ("editor-headless", [str(case_editor), "--headless"]),
            ("editor-window", [str(case_editor), "--smoke"]),
            ("showcase-headless", [str(case_showcase), "--headless"]),
            ("showcase-window", [str(case_showcase), "--smoke"]),
            ("benchmark", [str(case_showcase), "--benchmark"]),
        ]
        results = []
        for name, command in cases:
            result = run_case(name, command, directory, env, args.timeout)
            log_text = (directory / f"{name}.log").read_text(encoding="utf-8", errors="replace")
            adapters = re.findall(r"^YoUI init: (.+ \((?:Dx12|Vulkan|Gl);.+\))$", log_text, re.MULTILINE)
            if adapters:
                result["adapter"] = adapters[0]
            if result["status"] == "passed":
                expected = "Gl" if is_angle or backend == "gl" else {"dx12": "Dx12", "vulkan": "Vulkan"}[backend]
                valid = bool(adapters) and all(f"({expected};" in a for a in adapters)
                if is_angle:
                    valid = valid and all("ANGLE" in a and env["YOUI_ANGLE_PLATFORM"].lower() in a.lower() for a in adapters)
                if not valid:
                    result["status"] = "adapter-identity-mismatch"
            results.append(result)
            if name == "renderer" and result["status"] != "passed":
                results.extend(dict(name=n, status="blocked-by-initialization") for n, _ in cases[1:])
                break
        report["backends"].append(dict(requested=backend, cases=results))
        (output / "results.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    report["comparisons"] = compare(output, "dx12", args.backends, args.baseline)
    (output / "results.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Evidence: {output}", flush=True)
    return 0 if all(c["status"] == "passed" for b in report["backends"] for c in b["cases"]) and all(c["status"] == "passed" for c in report["comparisons"]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
