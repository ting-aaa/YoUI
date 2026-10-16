<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-16T17:34:13Z",
  "derived_from": [],
  "event_id": "beta-components-plan-20261017-v1",
  "id": "PLAN-20261016-DB1C18",
  "kind": "plan",
  "next_actions": [],
  "review_after": "2026-10-31",
  "schema_version": 1,
  "scope": [
    "docs",
    "managed",
    "samples",
    "tests"
  ],
  "sensitivity": "internal",
  "sources": [
    "User roadmap clarification 2026-10-17: Alpha rendering and few controls, Beta expands controls and framework capability",
    "docs/alpha-acceptance.md milestone boundary",
    "docs/beta-development.md B01-B10 completion contract",
    "artifacts/beta-verification/bootstrap.log native dev build success, historical managed CS0103 ReorderChildren failure",
    "RPT-20261016-0EE9B5 final B01-B10 local Beta acceptance",
    "docs/beta-review.md independent46-clause final proof"
  ],
  "status": "completed",
  "summary": "All B01-B10 plan requirements completed and accepted at clean90e86ad with final Debug/Release All, stable packages, image QA and independent clause proof.",
  "supersedes": [],
  "tags": [
    "beta"
  ],
  "task_id": "TASK-20261016-665876",
  "tier": "short",
  "title": "Beta component acceptance and implementation plan",
  "type_version": 1,
  "updated_at": "2026-10-16T18:45:06Z",
  "valid_as_of": "2026-10-17"
}
-->

# Beta implementation plan

## Scope and exit contract

Confirmed roadmap: Alpha rendering plus a few controls; Beta expands component count and framework capability; complete UI belongs to 1.0. Deliver common widgets, composable content/items, Grid/Wrap layout, overlay focus, binding/commands/validation, document/runtime/editor/export parity, gallery/reference scenarios and regression/package evidence. Full editor redesign, complete animation support and all-platform acceptance are not Beta requirements.

## Ordered work

1. Audit e336d6e repaired Alpha APIs and current tests. Publish a bounded Beta acceptance matrix, known exclusions and baseline commands.
2. In parallel, implement common runtime controls/composition and layout/binding foundations. Preserve existing entry points and registered component contracts; adapt isolated dirty-workspace examples only after checking against repaired Alpha.
3. Integrate registered document metadata, validation, authoring, export and gallery/reference app. Verify serializable controls produce equivalent runtime and exported behavior.
4. Run native before managed with matching Debug/Release libraries. Run scripts/verify.ps1 for any rendering/input/layout changes, inspect produced screenshots, and record logical, real GPU and native-window evidence separately. Package consumer validation establishes reusable public APIs.
5. Independently review and fix findings. Record final SHA/dirty state, configurations, output paths, exclusions and exact remaining work. Do not release a tag or claim complete Beta until agreed acceptance passes.

## Current state

All B01-B10 is complete at accepted clean product90e86add65c6ffd24f73dca7a30874176d887474. Final DebugAll1a5285693a6444aeae3a80cdaeb33036 and ReleaseAllb56c2ee269ec469e89f670c7cb59038f passed, together with stable same-source Debug/Release packages and actual final-image inspection. docs/beta-review.md proves46 explicit clauses with0 unproven; no missing/incomplete deliverable or actionable P1/P2 remains. RPT-20261016-0EE9B5 records exact proof. Original0555ea5 passes, later scope failure, repaired reproduction and earlier failures stay preserved. No required Beta action remains. Future public release, extra platform acceptance and1.0 are separate optional work. Root docs/memory-only closure does not claim a product retest; remote freshness and original uncommitted1.0 workspace remain caveats.
