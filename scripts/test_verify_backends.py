# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=11,<13", "numpy>=2,<3"]
# ///
"""CPU-only comparison and driver regressions; never GPU evidence."""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image

import verify_backends as driver


# Synthetic executable outputs follow the source programs' public file contract,
# independently of the driver's required-image manifest.
PROGRAM_IMAGES = {
    "renderer": ("render-scene.png",),
    "gpu-reference": ("gpu-reference.png", "gpu-textured-mesh.png", "offscreen-trim.png"),
    "scene": ("scene-graph.png",),
    "editor-headless": ("editor.png", "editor-selected.png", "editor-anchor.png",
                        "editor-properties.png", "editor-preview.png", "editor-compact.png"),
    "editor-window": ("editor-window-smoke.png",),
    "showcase-headless": ("showcase-dark.png", "showcase-light.png", "showcase-modal.png", "showcase-compact.png"),
    "showcase-window": ("window-smoke.png",),
    "benchmark": ("benchmark-frame.png",),
}


class BackendDriverTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="youi-backends-")
        self.addCleanup(self.scratch.cleanup)
        self.output = Path(self.scratch.name)
        for backend in ("dx12", "vulkan"):
            (self.output / backend).mkdir()

    def image(self, backend, name="fixture.png", color=(10, 20, 30, 255), size=(3, 4)):
        Image.new("RGBA", size, color).save(self.output / backend / name)

    def complete_images(self, backend):
        for images in PROGRAM_IMAGES.values():
            for image in images:
                self.image(backend, image)

    def matrix(self, backends=("dx12",), omit=(), only=None):
        events = []
        output = self.output / "matrix"

        def sync(stage):
            events.append(("sync", stage))
            return dict(stage=stage, sourceBinaryHash="fixture hash", consumers=[])

        def run(name, command, directory, env, timeout):
            events.append(("run", name, command))
            adapter = {"dx12": "Dx12", "vulkan": "Vulkan", "gl": "Gl"}[env["YOUI_BACKEND"]]
            (directory / f"{name}.log").write_text(f"YoUI init: Fixture ({adapter}; hardware)\n")
            for image in PROGRAM_IMAGES[name]:
                if image not in omit and (only is None or image in only):
                    Image.new("RGBA", (2, 2)).save(directory / image)
            return dict(name=name, status="passed")

        with patch("sys.argv", ["verify_backends.py", "--backends", *backends, "--output", str(output)]), \
                patch.object(driver, "build_gpu_executable", return_value="fixture-gpu.exe"), \
                patch.object(driver, "sync_native", side_effect=sync), \
                patch.object(driver, "run_case", side_effect=run):
            code = driver.main()
        return code, json.loads((output / "results.json").read_text()), events

    def test_equal_images_pass(self):
        self.image("dx12")
        self.image("vulkan")
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"], expected_images=("fixture.png",))
        self.assertEqual([row["status"] for row in result], ["passed"])
        self.assertEqual(result[0]["changedPixels"], 0)

    def test_missing_candidate_fails_instead_of_skipping(self):
        self.image("dx12")
        self.assertEqual(driver.compare(self.output, "dx12", ["dx12", "vulkan"], expected_images=("fixture.png",)),
                         [dict(backend="vulkan", image="fixture.png", status="missing-candidate-image")])

    def test_missing_entire_baseline_fails(self):
        self.image("vulkan")
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"], expected_images=("fixture.png",))
        self.assertEqual(result[0]["status"], "missing-baseline")

    def test_candidate_only_image_reports_missing_baseline_image(self):
        self.image("dx12")
        self.image("vulkan")
        self.image("vulkan", "extra.png")
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"], expected_images=("fixture.png",))
        self.assertIn(dict(backend="dx12", image="extra.png", status="missing-baseline-image"), result)

    def test_size_and_pixel_failures_stay_strict(self):
        self.image("dx12")
        self.image("vulkan", size=(4, 4))
        self.assertEqual(driver.compare(self.output, "dx12", ["vulkan"], expected_images=("fixture.png",))[0]["status"], "size-mismatch")
        self.image("vulkan", color=(255, 0, 0, 255))
        self.assertEqual(driver.compare(self.output, "dx12", ["vulkan"], expected_images=("fixture.png",))[0]["status"], "different")

    def test_comparison_rejects_same_required_image_missing_from_every_backend(self):
        self.complete_images("dx12")
        self.complete_images("vulkan")
        for backend in ("dx12", "vulkan"):
            (self.output / backend / "benchmark-frame.png").unlink()
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"])
        self.assertIn(dict(backend="dx12", image="benchmark-frame.png", status="missing-baseline-image"), result)
        self.assertIn(dict(backend="vulkan", image="benchmark-frame.png", status="missing-candidate-image"), result)

    def test_single_backend_partial_baseline_is_rejected(self):
        self.image("dx12", "render-scene.png")
        result = driver.compare(self.output, "dx12", ["dx12"])
        self.assertEqual(len(result), 17)
        self.assertTrue(all(row["status"] == "missing-baseline-image" for row in result))

    def test_external_angle_baseline_can_include_gallery_extras(self):
        self.complete_images("dx12")
        (self.output / "angle-vulkan").mkdir()
        self.complete_images("angle-vulkan")
        self.image("dx12", "gallery-controls-dark.png")
        result = driver.compare(self.output, "dx12", ["angle-vulkan"], self.output / "dx12")
        self.assertEqual(len(result), 18)
        self.assertTrue(all(row["status"] == "passed" for row in result))

    def test_gpu_resolution_binds_repo_target_even_with_environment_override(self):
        artifact = '{"reason":"compiler-artifact","target":{"name":"gpu"},"executable":"exact-gpu.exe"}'
        with patch.object(driver.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, artifact)) as run:
            self.assertEqual(driver.build_gpu_executable(), "exact-gpu.exe")
        command = run.call_args.args[0]
        self.assertEqual(command[command.index("--target-dir") + 1], str(driver.ROOT / "target"))
        self.assertIn("--locked", command)
        self.assertIn("--release", command)

    def test_failed_gpu_build_propagates(self):
        with patch.object(driver.subprocess, "run", side_effect=subprocess.CalledProcessError(23, ["cargo"])):
            with self.assertRaises(subprocess.CalledProcessError):
                driver.build_gpu_executable()

    def test_failed_native_sync_propagates(self):
        with patch.object(driver.subprocess, "run", side_effect=subprocess.CalledProcessError(23, ["pwsh"])):
            with self.assertRaises(subprocess.CalledProcessError):
                driver.sync_native("fixture")

    def test_matrix_serializes_gpu_and_snapshots_before_managed_consumption(self):
        code, report, events = self.matrix()
        self.assertEqual(code, 0)
        self.assertEqual(report["status"], "passed")
        self.assertEqual(report["backends"][0]["status"], "passed")
        self.assertEqual(report["backends"][0]["missingImages"], [])
        self.assertEqual(events[0], ("sync", "backend-matrix-after-cargo-build"))
        for index, event in enumerate(events):
            if event[0] == "run" and event[1] == "gpu-reference":
                self.assertIn("--test-threads=1", event[2])
            if event[0] == "run" and event[1].startswith(("editor-", "showcase-", "benchmark")):
                self.assertEqual(events[index - 1], ("sync", f"before-dx12-{event[1]}"))
        self.assertEqual(events[-1], ("sync", "backend-dx12-complete"))

    def test_matrix_rejects_common_missing_image_even_when_processes_exit_zero(self):
        code, report, _ = self.matrix(("dx12", "vulkan"), omit=("benchmark-frame.png",))
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")
        for backend in report["backends"]:
            self.assertEqual(backend["status"], "failed")
            self.assertEqual(backend["missingImages"], ["benchmark-frame.png"])
            benchmark = next(case for case in backend["cases"] if case["name"] == "benchmark")
            self.assertEqual(benchmark["status"], "missing-images")
            self.assertEqual(benchmark["missingImages"], ["benchmark-frame.png"])

    def test_single_backend_one_image_cannot_pass_the_matrix(self):
        code, report, _ = self.matrix(only=("render-scene.png",))
        self.assertEqual(code, 1)
        self.assertEqual(report["status"], "failed")
        backend = report["backends"][0]
        self.assertEqual(backend["status"], "failed")
        self.assertEqual(len(backend["missingImages"]), 17)
        self.assertEqual(backend["cases"][0]["status"], "passed")
        self.assertTrue(all(case["status"] == "missing-images" for case in backend["cases"][1:]))


if __name__ == "__main__":
    unittest.main()
