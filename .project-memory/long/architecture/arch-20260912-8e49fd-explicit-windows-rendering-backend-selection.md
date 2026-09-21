<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-09-12T08:06:49Z",
  "derived_from": [],
  "event_id": "youi-backend-architecture-v1",
  "id": "ARCH-20260912-8E49FD",
  "kind": "architecture",
  "next_actions": [],
  "review_after": "",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "docs/render-backends.md",
    "docs/architecture.md"
  ],
  "status": "active",
  "summary": "YOUI_BACKEND strictly selects DX12 Vulkan or native OpenGL; adapter identity verified without cross-API fallback.",
  "supersedes": [],
  "tags": [],
  "task_id": "",
  "tier": "long",
  "title": "Explicit Windows rendering backend selection",
  "type_version": 1,
  "updated_at": "2026-09-12T08:06:49Z",
  "valid_as_of": "2026-09-12"
}
-->

# Explicit Windows rendering backend selection

YOUI_BACKEND accepts dx12/vulkan/gl/opengl/metal/default, rejects angle and multi-backend lists; Windows default remains DX12. Metal accepted as API identifier is not Windows/macOS runtime verification. Native GL means WGL. Adapter type/driver reported. Editor and Showcase accept YOUI_ARTIFACT_DIR. Vulkan HWND surface now includes HINSTANCE read from window; fixed physical1px SDF AA eliminates opaque rectangle corner fading. verify-backends.ps1 and uv verify_backends.py isolate each GPU process with45s timeout and fresh output paths. Existing renderer/UI separation remains. Optional ANGLE copies locked wgpu-hal26.0.6 into isolated experiment and uses EGL; no production DLL/global Cargo cache/main lock mutation, not a supported backend.
