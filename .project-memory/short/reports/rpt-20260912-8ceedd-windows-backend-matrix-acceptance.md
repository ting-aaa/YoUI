<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-09-12T08:06:49Z",
  "derived_from": [],
  "event_id": "youi-backend-report-v1",
  "id": "RPT-20260912-8CEEDD",
  "kind": "report",
  "next_actions": [],
  "review_after": "2026-09-26",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "docs/render-backends.md",
    "artifacts/backends-final/results.json",
    "artifacts/backend-quality-gate.log",
    "artifacts/backends-angle/results.json",
    "Main agent visible Vulkan/OpenGL evidence and user ANGLE optional clarification 2026-09-12"
  ],
  "status": "completed",
  "summary": "24 backend runs and28 comparisons passed for DX12 Vulkan nativeGL; ANGLE optional experiments did not render successfully.",
  "supersedes": [],
  "tags": [],
  "task_id": "TASK-20260912-1DA605",
  "tier": "short",
  "title": "Windows backend matrix acceptance",
  "type_version": 1,
  "updated_at": "2026-09-12T08:06:49Z",
  "valid_as_of": "2026-09-12"
}
-->

# Windows backend matrix acceptance

Three backends each passed8 independent cases=24;14 images per comparison backend=28 comparisons pass. DX12 RTX5060Laptop driver32.0.15.7705; Vulkan same NVIDIA577.05; native WGL AMD860M GL4.6 driver26.8.1.260810. Vulkan14 outputs exactly match DX12. GL defaultEditor max2/255; all GL max5/255,maxmean.11799; tolerance mean<=.25 and fraction pixels anychannel>4<=.001. Reference PNGs identical across3. Everybackend Editor13 HWND checks and Showcase7 passed. Final Release full gate:Rust10 logic+1GPU+14managed+12Editor/export+13EditorWindow+7ShowcaseWindow,fmt/clippy. Fixed Vulkan missingHINSTANCE and DX12 corneralpha215 with fixed1pxAA; added corner/fullalpha regression. Main agent visibly inspected Vulkan/GL Editor and Preview; GL Sync ON->OFF; no apparent inversion/black/color/content failure. Comparisons use texture readback,not desktop screenshot pixel tests. CPU samples overlapped compilation and GL uses anotherGPU; no speed ranking/GPUtime/FPS claim. ANGLE isolated build succeeded but D3D11 AMD860M/ES3.0 fails requested compute limits; ANGLE Vulkan initialization fails unknowncause. User said ANGLE not necessary; experimental failures nonblocking and not passes. No commit/global memory/secrets.
