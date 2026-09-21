# Repository guidance

- Use `uv` for Python environments and scripts.
- Keep rendering independent of C# controls, scene trees and application loops.
- Keep platform window/input code in platform projects.
- Preserve transparent draw order; batch only compatible adjacent operations.
- Build native code before managed samples; Debug and Release must load matching libraries.
- Run `scripts/verify.ps1` after changes to rendering, input or layout.
- Inspect produced screenshots when visible rendering changes.
- Report real GPU, native-window and logical-test evidence separately.
- Do not claim unverified platforms, animation runtimes, IME behavior or accessibility support.
