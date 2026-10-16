<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-16T17:35:10Z",
  "derived_from": [],
  "event_id": "beta-current-architecture-20261017-v1",
  "id": "ARCH-20261016-18BA51",
  "kind": "architecture",
  "next_actions": [],
  "review_after": "",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "AGENTS.md",
    "docs/architecture.md at e336d6e",
    "docs/alpha-acceptance.md at e336d6e",
    "managed/YoUI/Assets/UiAsset.cs at e336d6e",
    "git rev-parse HEAD: e336d6e6ec8881764d5b3a8053e152f791ddb581"
  ],
  "status": "active",
  "summary": "Independent renderer, retained C# runtime and same-library Editor; host-owned loops/platform code; ordered batching, matched builds and revision-specific evidence remain mandatory.",
  "supersedes": [
    "ARCH-20260912-A547ED",
    "ARCH-20260912-064C54",
    "ARCH-20260912-8E49FD",
    "DEC-20260912-340228"
  ],
  "tags": [
    "beta"
  ],
  "task_id": "TASK-20261016-665876",
  "tier": "long",
  "title": "Current Alpha baseline and Beta architecture boundaries",
  "type_version": 1,
  "updated_at": "2026-10-16T17:35:10Z",
  "valid_as_of": "2026-10-17"
}
-->

# Current repaired Alpha baseline and Beta constraints

## Durable boundaries

Rust/wgpu renderer owns no C# controls, scene tree or application loop. The optional Rust scene consumer depends on renderer interfaces. C# retained UI and same-library Editor remain independent from platform Windows/Linux hosts. Preserve transparent order and merge only compatible adjacent operations. Coordinates are top-left X-right/Y-down; managed layout uses DIP, native rendering uses physical pixels. Existing C ABI remains version 0x00010000 with checked synchronous borrowed command/text submission. Sources: AGENTS.md; docs/architecture.md; native/youi-render/Cargo.toml.

## Beta baseline facts and scope

The task uses locally cached repaired Alpha e336d6e in C:/Users/HT/.codex/worktrees/beta-components/YoUI. At this baseline, managed/YoUI/Assets/UiAsset.cs has nine legacy NodeKind values; Composition.cs, Layout.cs and Binding.cs are absent. Existing original-workspace 1.0 additions are uncommitted and not current Beta acceptance. Windows DX12/Vulkan/native OpenGL have historical evidence; Linux X11 has an experimental host. Renderer explicit stop/dispose/recreate/reupload/rebind exists; transparent device recovery does not. Do not reuse September absent-feature statements as current facts.

## Verification requirements

Use uv for all Python. Build native before managed and match Debug/Release DLLs. Run scripts/verify.ps1 after rendering/input/layout changes and inspect screenshots for visible changes. Report logic, actual GPU, native-window, physical input/IME/accessibility and platform evidence separately. The published Alpha tag failed full local acceptance; repaired main evidence does not retroactively pass it. No current Beta tests have passed. Never infer acceptance or supported platforms from available API names.
