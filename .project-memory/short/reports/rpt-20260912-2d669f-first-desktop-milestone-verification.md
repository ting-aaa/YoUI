<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "medium",
  "created_at": "2026-09-12T06:15:48Z",
  "derived_from": [],
  "event_id": "youi-report-desktop-v1",
  "id": "RPT-20260912-2D669F",
  "kind": "report",
  "next_actions": [],
  "review_after": "2026-09-26",
  "schema_version": 1,
  "scope": [],
  "sensitivity": "internal",
  "sources": [
    "docs/verification.md",
    "artifacts/benchmark.json",
    "artifacts/window-smoke.json",
    "Main agent final checkpoint 2026-09-12"
  ],
  "status": "completed",
  "summary": "Runnable desktop foundation passed native/GPU/managed/window validation; broad design remains incomplete.",
  "supersedes": [],
  "tags": [],
  "task_id": "TASK-20260912-CC3A20",
  "tier": "short",
  "title": "First desktop milestone verification",
  "type_version": 1,
  "updated_at": "2026-09-12T06:15:48Z",
  "valid_as_of": "2026-09-12"
}
-->

# First desktop milestone verification

Full scripts/verify.ps1 -Configuration Release passed before clipboard fix: fmt/clippy, 9 Rust logic tests, 1 explicitly enabled real GPU pixel test, then managed tests, 7 Win32 checks, screenshots, benchmark and 2 independent examples. After managed clipboard changes, rebuilt and reran affected C# tests (14 pass), headless, smoke and benchmark. Final snapshot-label fix was rebuilt and headless rerun. Main agent reports visible mouse theme switch, Aurora paste giving 16667 items, Ctrl+Z restoring 100000, modal and Escape successful; real Chinese/Japanese IME not accepted. Latest Release CPU benchmark on RTX 5060 Laptop DX12 at 1280x820,30 warmup/240 samples: C# median/P95 .019/.0209ms; native prepare .027/.038ms; CPU submit .034/.051ms; 100000 items,6 realized rows,0 animation relayout,444 commands,1054 instances,4 draws,1 layer,101280 upload bytes/frame,53 managed allocation bytes/frame including harness. GPU time/FPS not measured. Workspace was not Git; no commit created. See docs/design-status.md for unimplemented model SDKs, platforms, complete render graph, editor and broader UI.
