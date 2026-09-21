<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "confirmed",
  "created_at": "2026-09-12T05:45:26Z",
  "derived_from": [],
  "event_id": "youi-project-bootstrap-20260912",
  "id": "PROJ-20260912-5E915B",
  "kind": "project",
  "next_actions": [],
  "review_after": "",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "User request 2026-09-12",
    "https://chatgpt.com/share/6aa4e629-ec40-83e8-b299-0e60c6978b22"
  ],
  "status": "active",
  "summary": "Independent native rendering engine with a C# retained UI framework; user allows C++ or Rust and requests efficient modern UI.",
  "supersedes": [],
  "tags": [],
  "task_id": "",
  "tier": "long",
  "title": "YoUI project scope",
  "type_version": 1,
  "updated_at": "2026-09-12T05:45:26Z",
  "valid_as_of": "2026-09-12"
}
-->

# YoUI project scope

The shared design describes an independent 2D engine below a UGUI-style retained component UI, unified ordering/clipping/composition, RectTransform layout, text/input, virtualization, optional Spine/Live2D adapters, and phased platform verification. The user additionally specifies C# for the UI framework and C++ or Rust for native foundations. Platform and third-party adapter support must be supported by runtime evidence, not inferred from interfaces. Initial audit saw only .project-memory in the workspace; there was no prior implementation.
