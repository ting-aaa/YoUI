# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=11,<13", "numpy>=2,<3"]
# ///
"""CPU-only comparison and driver regressions; never GPU evidence."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image

import verify_backends as driver


class BackendDriverTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="youi-backends-")
        self.addCleanup(self.scratch.cleanup)
        self.output = Path(self.scratch.name)
        for backend in ("dx12", "vulkan"):
            (self.output / backend).mkdir()

    def image(self, backend, name="fixture.png", color=(10, 20, 30, 255), size=(3, 4)):
        Image.new("RGBA", size, color).save(self.output / backend / name)

    def test_equal_images_pass(self):
        self.image("dx12")
        self.image("vulkan")
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"])
        self.assertEqual([row["status"] for row in result], ["passed"])
        self.assertEqual(result[0]["changedPixels"], 0)

    def test_missing_candidate_fails_instead_of_skipping(self):
        self.image("dx12")
        self.assertEqual(driver.compare(self.output, "dx12", ["dx12", "vulkan"]),
                         [dict(backend="vulkan", image="fixture.png", status="missing-candidate-image")])

    def test_missing_entire_baseline_fails(self):
        self.image("vulkan")
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"])
        self.assertEqual(result[0]["status"], "missing-baseline")

    def test_candidate_only_image_reports_missing_baseline_image(self):
        self.image("dx12")
        self.image("vulkan")
        self.image("vulkan", "extra.png")
        result = driver.compare(self.output, "dx12", ["dx12", "vulkan"])
        self.assertIn(dict(backend="dx12", image="extra.png", status="missing-baseline-image"), result)

    def test_size_and_pixel_failures_stay_strict(self):
        self.image("dx12")
        self.image("vulkan", size=(4, 4))
        self.assertEqual(driver.compare(self.output, "dx12", ["vulkan"])[0]["status"], "size-mismatch")
        self.image("vulkan", color=(255, 0, 0, 255))
        self.assertEqual(driver.compare(self.output, "dx12", ["vulkan"])[0]["status"], "different")

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
        events = []

        def sync(stage):
            events.append(("sync", stage))
            return dict(stage=stage, sourceBinaryHash="fixture hash", consumers=[])

        def run(name, command, directory, env, timeout):
            events.append(("run", name, command))
            (directory / f"{name}.log").write_text("YoUI init: Fixture (Dx12; hardware)\n")
            # A synthetic comparison image, never renderer output.
            Image.new("RGBA", (2, 2)).save(directory / "fixture.png")
            return dict(name=name, status="passed")

        with patch("sys.argv", ["verify_backends.py", "--backends", "dx12", "--output", str(self.output / "matrix")]), \
                patch.object(driver, "build_gpu_executable", return_value="fixture-gpu.exe"), \
                patch.object(driver, "sync_native", side_effect=sync), \
                patch.object(driver, "run_case", side_effect=run):
            self.assertEqual(driver.main(), 0)
        self.assertEqual(events[0], ("sync", "backend-matrix-after-cargo-build"))
        for index, event in enumerate(events):
            if event[0] == "run" and event[1] == "gpu-reference":
                self.assertIn("--test-threads=1", event[2])
            if event[0] == "run" and event[1].startswith(("editor-", "showcase-", "benchmark")):
                self.assertEqual(events[index - 1], ("sync", f"before-dx12-{event[1]}"))
        self.assertEqual(events[-1], ("sync", "backend-dx12-complete"))


if __name__ == "__main__":
    unittest.main()
