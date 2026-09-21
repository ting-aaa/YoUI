<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-09-12T06:16:11Z",
  "derived_from": [],
  "event_id": "youi-architecture-desktop-v1",
  "id": "ARCH-20260912-A547ED",
  "kind": "architecture",
  "next_actions": [],
  "review_after": "",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "docs/architecture.md",
    "native/youi-render/Cargo.toml"
  ],
  "status": "active",
  "summary": "Rust wgpu renderer, optional Rust scene tree, batched C ABI, retained C# UI and Win32 host.",
  "supersedes": [],
  "tags": [],
  "task_id": "",
  "tier": "long",
  "title": "Desktop foundation architecture",
  "type_version": 1,
  "updated_at": "2026-09-12T06:16:11Z",
  "valid_as_of": "2026-09-12"
}
-->

# Desktop foundation architecture

youi-render uses wgpu 26.0.1 and cosmic-text 0.14.2 independently of UI. youi-scene adds generational IDs/shared meshes/affine transforms. C# submits 80-byte commands plus UTF-8 buffer synchronously; ABI 0x00010000. Ordered adjacent batching, text, RGBA images, triangles, nested rectangular clipping, isolated opacity layers, cached text, reused buffers and fixed-height virtualization are implemented. Win32 hosts messages/DPI/clipboard. General render graph, model SDKs, device recovery and other platform hosts are absent.
