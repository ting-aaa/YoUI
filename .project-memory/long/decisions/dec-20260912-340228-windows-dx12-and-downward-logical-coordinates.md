<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-09-12T06:16:12Z",
  "derived_from": [],
  "event_id": "youi-decisions-desktop-v1",
  "id": "DEC-20260912-340228",
  "kind": "decision",
  "next_actions": [],
  "review_after": "",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "docs/architecture.md",
    "docs/verification.md"
  ],
  "status": "active",
  "summary": "Verified backend is Windows DX12; top-left Y-down coordinates explicitly differ from shared design.",
  "supersedes": [],
  "tags": [],
  "task_id": "",
  "tier": "long",
  "title": "Windows DX12 and downward logical coordinates",
  "type_version": 1,
  "updated_at": "2026-09-12T06:16:12Z",
  "valid_as_of": "2026-09-12"
}
-->

# Windows DX12 and downward logical coordinates

Rust chosen from user-permitted native languages. Explicit DX12 Windows initialization succeeded after default multi-backend stalls and old OpenGL process errors; exact driver cause unproven. Logical Y-down coordinates align desktop layout/text/input while retaining RectTransform formulas. Managed units are DIP; native units physical pixels. Other backends are not verified.

Historical scope note: the preceding unverified-backend statement describes initial foundation acceptance only. RPT-20260912-8CEEDD now verifies this Windows machine with DX12, Vulkan and native WGL; ARCH-20260912-8E49FD describes explicit selection. Windows default remains DX12 and the coordinate decision remains unchanged. ANGLE is optional and unverified.
