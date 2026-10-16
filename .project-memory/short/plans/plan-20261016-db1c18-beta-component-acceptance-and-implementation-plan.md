<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-16T17:34:13Z",
  "derived_from": [],
  "event_id": "beta-components-plan-20261017-v1",
  "id": "PLAN-20261016-DB1C18",
  "kind": "plan",
  "next_actions": [
    "Create second clean freeze including implemented and independently confirmed RadioGroup Modal/Popup keyboard-scope repair.",
    "Rerun B10 Debug/Release All gates and stable paired package consumers on the second freeze; inspect all regenerated images.",
    "Record exact source/snapshot, failures, unrun scope and release boundary; complete task only after all B01-B10 acceptance is supported."
  ],
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
    "RPT-20261016-919434 first clean Beta All gate passes and subsequent keyboard-scope defect"
  ],
  "status": "active",
  "summary": "Implement common components and composable framework capability with runtime-document-editor-export parity, gallery workflows and revision-specific regression/package evidence.",
  "supersedes": [],
  "tags": [
    "beta"
  ],
  "task_id": "TASK-20261016-665876",
  "tier": "short",
  "title": "Beta component acceptance and implementation plan",
  "type_version": 1,
  "updated_at": "2026-10-16T18:27:01Z",
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

First clean product freeze0555ea504eaf7a95aa9e44350b367ef16c704fce passed DebugAll c8029b74b7114e9f9fede5f2705e6aa0 and ReleaseAll8539b609ca90422298f9205733dad1fa. A later audit found P2 RadioGroup Modal/Popup scope escape; original repro-modal-0555ea5.log is preserved. Repair is implemented,213 managed Debug checks pass, and independent repro-modal-repaired.log confirms modal/top-popup isolation. Create second clean freeze and rerun both complete All gates, stable Debug/Release package pair and fresh image inspection before B10 acceptance. RPT-20261016-919434 is current progress/failure/repair evidence; no narrowed scope or complete status. Prior packages are outdated; final pair has not run. SDK10.0.401 and mouse-idle agreement remain as recorded. Network freshness, historical Alpha failures, original uncommitted1.0 workspace and unverified platform/IME/animation/accessibility boundaries remain unchanged.
