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
    "Finish binding model normalization and independent review; freeze implemented B01-B09 product source.",
    "Rerun B10 Debug/Release Logic/Gpu/Window gates and package consumers on frozen source; inspect all regenerated images.",
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
    "RPT-20261016-F4A454 Beta implementation and intermediate verification checkpoint"
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
  "updated_at": "2026-10-16T18:05:35Z",
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

Branch codex/beta-components remains at repaired Alpha e336d6e with dirty B01-B09 implementation. The complete B01-B10 contract in docs/beta-development.md remains unchanged. Intermediate evidence and repaired failures are recorded in RPT-20261016-F4A454; all prefreeze passes are tied to earlier snapshots. Model-normalization binding correction and independent review finish before final source freeze. Final B10 requires Debug/Release Logic/Gpu/Window, package reruns and screenshot inspection, so task and plan stay active. The resolved bootstrap failure is preserved at artifacts/beta-verification/bootstrap.log, and earlier editor24 evidence is artifacts/beta-verification/editor-debug-content.log. Use isolated SDK10.0.401 at C:/Users/HT/DEV/YoUI/artifacts/alpha-audit-20261014/dotnet. User mouse-idle agreement is recorded in artifacts/beta-verification/mouse-condition.txt. Network fetch failed and freshest remote state is unverified. Historical Alpha/September evidence stays revision-scoped; local packaging does not publish or tag.
