<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-09-12T07:08:57Z",
  "derived_from": [],
  "event_id": "youi-editor-architecture-v1",
  "id": "ARCH-20260912-064C54",
  "kind": "architecture",
  "next_actions": [],
  "review_after": "",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "docs/editor.md",
    "managed/YoUI/AuthoringControls.cs",
    "managed/YoUI/Assets/UiAsset.cs",
    "managed/YoUI.Editor.Core"
  ],
  "status": "active",
  "summary": "Reusable YoUI authoring controls and runtime assets underpin a same-library Editor with JSON, standalone generated C#, and local exporter plugins.",
  "supersedes": [],
  "tags": [],
  "task_id": "",
  "tier": "long",
  "title": "Self-hosted Editor and runtime asset export architecture",
  "type_version": 1,
  "updated_at": "2026-09-12T07:08:57Z",
  "valid_as_of": "2026-09-12"
}
-->

# Self-hosted Editor and runtime asset export architecture

Reusable TreeView, ScrollView and PropertyGrid live in managed/YoUI. Version-1 assets and UiAssetRuntime/UiInstance also live in the runtime with no Editor dependency; enforce 4MiB,1000 nodes,24 depth,64-4096 artboard and strict IDs/types/numeric/color validation. Editor.Core supplies transactions,100-step undo/redo, hierarchy operations and atomic persistence. Editor shell uses same UI library and real nested runtime preview. IUiExporter/ExportCatalog support JSON and direct control-building C# plus trusted local DLL format plugins; paths checked and staged new output directories committed atomically. Plugin code is in-process, with no sandbox/hot unload/private dependency resolver/custom controls. Editing rebuilds preview tree; idle frames cached. See docs/editor.md for API and workflow.
