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
    "git rev-parse HEAD: e336d6e6ec8881764d5b3a8053e152f791ddb581",
    "docs/beta-review.md final B01-B10 proof at90e86ad",
    "RPT-20261016-0EE9B5 final local Beta acceptance"
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
  "updated_at": "2026-10-16T18:45:06Z",
  "valid_as_of": "2026-10-17"
}
-->

# Current repaired Alpha baseline and Beta constraints

## Durable boundaries

Rust/wgpu renderer owns no C# controls, scene tree or application loop. The optional Rust scene consumer depends on renderer interfaces. C# retained UI and same-library Editor remain independent from platform Windows/Linux hosts. Preserve transparent order and merge only compatible adjacent operations. Coordinates are top-left X-right/Y-down; managed layout uses DIP, native rendering uses physical pixels. Existing C ABI remains version 0x00010000 with checked synchronous borrowed command/text submission. Sources: AGENTS.md; docs/architecture.md; native/youi-render/Cargo.toml.

## Beta baseline facts and scope

Historical task-start baselinee336d6e had nine legacy NodeKind values and no Composition.cs/Layout.cs/Binding.cs. Accepted final Beta90e86ad adds common/composable controls, keyed items, Box/Grid/Wrap, scoped popups, disposable bindings/commands/validation and v2 definitions with v1 compatibility and runtime/editor/export parity. Original workspace1.0 additions stay distinct. Current Beta proof is WindowsDX12; Vulkan/OpenGL history and experimental Linux host are not current Beta platform acceptance. Explicit host stop/dispose/recreate/reupload/rebind exists; transparent hardware recovery is not proven. Historical absent-feature claims do not describe accepted Beta.

## Verification requirements

Use uv for all Python. Build native before managed and match Debug/Release DLLs. Run scripts/verify.ps1 after rendering/input/layout changes and inspect screenshots for visible changes. Report logic, actual GPU, native-window, physical input/IME/accessibility and platform evidence separately. Final local Beta90e86ad acceptance is RPT-20261016-0EE9B5/docs/beta-review.md; it does not retroactively pass failed Alpha tag7a42be9 or prove other platforms/1.0. Native matching is stage-local because Cargo can relink between stages; each consumer must match its current stage source/profile. Never infer acceptance from API names or a preexisting binary.
