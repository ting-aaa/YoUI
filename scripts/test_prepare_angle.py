# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Regression checks for ANGLE staging only; no Cargo, DLL or GPU execution."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("prepare_angle", Path(__file__).with_name("prepare_angle.py"))
prepare_angle = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare_angle)


class PrepareAngleTests(unittest.TestCase):
    def test_external_output_preserves_rust_pin_before_every_cargo_command(self):
        pin = (prepare_angle.ROOT / "rust-toolchain.toml").read_bytes()
        with tempfile.TemporaryDirectory(prefix="youi-angle-test-") as directory:
            base = Path(directory)
            root = base / "repo with spaces"
            output = base / "external output" / "angle build"
            angle = base / "angle dlls"
            source = base / "registry" / "wgpu-hal"
            (root / "native").mkdir(parents=True)
            (source / "src/gles").mkdir(parents=True)
            angle.mkdir()
            (root / "Cargo.toml").write_text('[workspace]\nmembers = []\n', encoding="utf-8")
            (root / "Cargo.lock").write_text('version = 4\n', encoding="utf-8")
            (root / "rust-toolchain.toml").write_bytes(pin)
            (root / "native/fixture.txt").write_text("native source fixture", encoding="utf-8")
            for app in ["tools/YoUI.Editor", "samples/YoUI.Showcase"]:
                managed = root / app / "bin/Release/net10.0"
                managed.mkdir(parents=True)
                (managed / "app.dll").write_bytes(b"managed fixture")
            for dll in ["libEGL.dll", "libGLESv2.dll"]:
                (angle / dll).write_bytes(b"ANGLE fixture; never loaded")
            (source / "src/gles/mod.rs").write_text(
                ("#[cfg(not(any(windows, webgl)))]\n#[cfg(windows)]\n") * 3, encoding="utf-8")
            (source / "src/gles/egl.rs").write_text(
                '} else if client_ext_str.contains("EGL_MESA_platform_surfaceless") {\n', encoding="utf-8")
            metadata = json.dumps({"packages": [{"name": "wgpu-hal", "version": "26.0.6",
                                                "manifest_path": str(source / "Cargo.toml")}]})
            commands = []

            def fake_run(command, *, cwd, check, **kwargs):
                self.assertEqual(cwd, output)
                self.assertTrue(check)
                # Check before each invocation, not merely after staging finishes.
                self.assertEqual((Path(cwd) / "rust-toolchain.toml").read_bytes(), pin)
                commands.append(command)
                for folder in ["target/release/examples", "target/release/deps"]:
                    (output / folder).mkdir(parents=True, exist_ok=True)
                (output / "target/release/youi_render.dll").write_bytes(b"native fixture")
                artifact = {"reason": "compiler-artifact", "target": {"name": "gpu"},
                            "executable": str(output / "target/release/deps/gpu.exe")}
                return subprocess.CompletedProcess(command, 0, stdout=json.dumps(artifact) + "\n")

            with patch.object(prepare_angle, "ROOT", root), \
                    patch.object(sys, "argv", ["prepare_angle.py", "--angle-dir", str(angle), "--output", str(output)]), \
                    patch.object(prepare_angle.subprocess, "check_output", return_value=metadata) as metadata_call, \
                    patch.object(prepare_angle.subprocess, "run", side_effect=fake_run), \
                    contextlib.redirect_stdout(io.StringIO()):
                prepare_angle.main()

            metadata_call.assert_called_once_with(
                ["cargo", "metadata", "--locked", "--format-version=1"], cwd=root)
            self.assertFalse(output.is_relative_to(root))
            self.assertFalse((output.parent / "rust-toolchain.toml").exists())
            self.assertEqual(commands, [
                ["cargo", "build", "--workspace", "--release", "--examples"],
                ["cargo", "build", "--workspace", "--release", "--locked"],
                ["cargo", "test", "-p", "youi-render", "--release", "--test", "gpu", "--no-run",
                 "--locked", "--message-format=json"],
            ])
            self.assertEqual((output / "rust-toolchain.toml").read_bytes(), pin)
            self.assertEqual((root / "rust-toolchain.toml").read_bytes(), pin)
            self.assertEqual((root / "Cargo.lock").read_text(encoding="utf-8"), 'version = 4\n')
            self.assertTrue((output / "provenance.json").is_file())


if __name__ == "__main__":
    unittest.main()
