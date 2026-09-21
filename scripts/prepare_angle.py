# /// script
# requires-python = ">=3.11"
# dependencies = []
# ///
"""Prepare an isolated, experimental EGL build of YoUI's wgpu 26 renderer.

Uses a private copy of the locked wgpu-hal crate; never edits Cargo's registry,
the main lockfile or the default binaries. ANGLE DLLs must be supplied locally.
The generated patch and hashes are retained beside the build for review.
"""
import argparse
import difflib
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--angle-dir", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/angle-build")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise RuntimeError(f"Choose a new build directory: {output}")
    for dll in ["libEGL.dll", "libGLESv2.dll"]:
        if not (args.angle_dir / dll).is_file():
            raise FileNotFoundError(args.angle_dir / dll)
    metadata = json.loads(subprocess.check_output(["cargo", "metadata", "--locked", "--format-version=1"], cwd=ROOT))
    package = next(p for p in metadata["packages"] if p["name"] == "wgpu-hal" and p["version"] == "26.0.6")
    source = Path(package["manifest_path"]).parent
    output.mkdir(parents=True)
    for file in ["Cargo.toml", "Cargo.lock"]:
        shutil.copy2(ROOT / file, output / file)
    shutil.copytree(ROOT / "native", output / "native")
    vendor = output / "vendor/wgpu-hal"
    shutil.copytree(source, vendor)
    with (output / "Cargo.toml").open("a", encoding="utf-8") as stream:
        stream.write('\n[patch.crates-io]\nwgpu-hal = { path = "vendor/wgpu-hal" }\n')

    changes = []
    hashes = []

    def patch(relative, transform):
        path = vendor / relative
        old = path.read_text(encoding="utf-8")
        new = transform(old)
        assert old != new
        path.write_text(new, encoding="utf-8")
        changes.extend(difflib.unified_diff(old.splitlines(True), new.splitlines(True),
                                          f"a/{relative}", f"b/{relative}"))
        hashes.append(dict(file=relative, before=hashlib.sha256(old.encode()).hexdigest(),
                           after=hashlib.sha256(new.encode()).hexdigest()))

    def select_egl(text):
        assert text.count("#[cfg(not(any(windows, webgl)))]") == 3
        assert text.count("#[cfg(windows)]") == 3
        return text.replace("#[cfg(not(any(windows, webgl)))]", "#[cfg(not(webgl))]").replace("#[cfg(windows)]", "#[cfg(any())]")

    patch("src/gles/mod.rs", select_egl)

    def select_platform(text):
        marker = '} else if client_ext_str.contains("EGL_MESA_platform_surfaceless") {'
        assert text.count(marker) == 1
        # EGL extension tokens from ANGLE's platform_angle_{d3d,vulkan} specs.
        insertion = '''} else if cfg!(windows) && client_ext_str.contains("EGL_ANGLE_platform_angle") {
                let platform = match std::env::var("YOUI_ANGLE_PLATFORM").as_deref() {
                    Ok("d3d11") => 0x3208,
                    Ok("vulkan") => 0x3450,
                    _ => return Err(crate::InstanceError::new(String::from("set YOUI_ANGLE_PLATFORM to d3d11 or vulkan"))),
                };
                let egl = egl1_5.expect("ANGLE requires EGL 1.5");
                let display = unsafe {
                    egl.get_platform_display(EGL_PLATFORM_ANGLE_ANGLE,
                        khronos_egl::DEFAULT_DISPLAY,
                        &[0x3203, platform, 0x3209, 0x320A, khronos_egl::ATTRIB_NONE])
                }.map_err(|e| crate::InstanceError::with_source(String::from("ANGLE platform selection failed"), e))?;
                (display, None, WindowKind::Unknown)
            ''' + marker
        return text.replace(marker, insertion)

    patch("src/gles/egl.rs", select_platform)
    (output / "wgpu-hal-angle.patch").write_text("".join(changes), encoding="utf-8")
    provenance = dict(kind="experimental wgpu-hal 26.0.6 EGL build", patches=hashes, libraries=[])
    # Build only our local source and locked crates. The copied lockfile changes
    # solely for the local hal patch; the repository lockfile is untouched.
    subprocess.run(["cargo", "build", "--workspace", "--release", "--examples"], cwd=output, check=True)
    subprocess.run(["cargo", "build", "--workspace", "--release", "--locked"], cwd=output, check=True)
    build = subprocess.run(["cargo", "test", "-p", "youi-render", "--release", "--test", "gpu", "--no-run",
                            "--locked", "--message-format=json"], cwd=output, text=True, capture_output=True, check=True)
    provenance["gpuExecutable"] = next(json.loads(line)["executable"] for line in build.stdout.splitlines()
        if line.startswith("{") and json.loads(line).get("reason") == "compiler-artifact"
        and json.loads(line).get("target", {}).get("name") == "gpu" and json.loads(line).get("executable"))
    for app, path in [("editor", "tools/YoUI.Editor"), ("showcase", "samples/YoUI.Showcase")]:
        destination = output / app
        shutil.copytree(ROOT / path / "bin/Release/net9.0", destination)
        shutil.copy2(output / "target/release/youi_render.dll", destination / "youi_render.dll")
    for name in ["libEGL.dll", "libGLESv2.dll", "d3dcompiler_47.dll"]:
        library = args.angle_dir / name
        if not library.exists():
            continue
        provenance["libraries"].append(dict(source=str(library.resolve()), name=name,
            sha256=hashlib.sha256(library.read_bytes()).hexdigest()))
        for destination in [output / "editor", output / "showcase", output / "target/release/examples", output / "target/release/deps"]:
            shutil.copy2(library, destination / name)
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
