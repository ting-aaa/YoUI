<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-17T03:01:45Z",
  "derived_from": [],
  "event_id": "beta-pr35-grid-review-checkpoint-20261017-v1",
  "id": "RPT-20261017-42017B",
  "kind": "report",
  "next_actions": [
    "Freeze/push independently repaired-reviewed Grid source and run fresh full acceptance/CI.",
    "Commit final premerge review record, verify final-head CI and merge with GitHub evidence."
  ],
  "review_after": "2026-10-31",
  "schema_version": 1,
  "scope": [
    "docs/beta-components.md",
    "managed/YoUI/Layout.cs",
    "tests/YoUI.Tests/BetaFrameworkTests.cs"
  ],
  "sensitivity": "internal",
  "sources": [
    "https://github.com/ting-aaa/YoUI/pull/35 root created/attached receipt",
    "git rev-parse HEAD:1dfb4071b72120f289469d76df5ffcebeeff86a4",
    "artifacts/pr35-review/Program.cs",
    "artifacts/pr35-review/repro.log",
    "artifacts/pr35-review/review.md final independent repair review",
    "artifacts/pr35-review/repaired-build.log zero warnings/errors",
    "artifacts/pr35-review/repaired-logic.log215 passed",
    "artifacts/pr35-review/repaired-repro.log GRID PASS",
    "artifacts/pr35-review/grid-fix-tests.log invalid stale/mixed build output",
    "artifacts/pr35-review/logic.log invalid stale/mixed build output",
    "managed/YoUI/Layout.cs GetCell uncommitted repair",
    "tests/YoUI.Tests/BetaFrameworkTests.cs two reattachment regression groups",
    "Root checkpoint CI38996686797 at1dfb407 running and fresh idle question pending2026-10-17"
  ],
  "status": "active",
  "summary": "PR35 independent P2 Grid stale placement repaired/reviewed; serial build clean,215 managed checks and original repro passed. Final frozen-head full gates/CI and merge remain pending; mixed-build failures retained.",
  "supersedes": [],
  "tags": [
    "beta",
    "pull-request"
  ],
  "task_id": "TASK-20261017-06D61A",
  "tier": "short",
  "title": "PR35 Grid review finding and pending repaired verification",
  "type_version": 1,
  "updated_at": "2026-10-17T03:02:25Z",
  "valid_as_of": "2026-10-17"
}
-->

# PR35 independent review: Grid stale placement and repair pending verification

## PR and reviewed baseline

PR35 https://github.com/ting-aaa/YoUI/pull/35 was created, source branch pushed and attached to the chat. Actual branch head1dfb4071b72120f289469d76df5ffcebeeff86a4 contains workflow memory only after2ad3a49; tested original Beta product90e86ad evidence remains historical for that revision. Successful current fetch confirmed origin/main e336d6e; older network failures are preserved history. PR task/plan remain active; no merge has occurred.

## Independent P2 finding and repair

New reviewer pr35_independent_review independently reproduced a Grid lifecycle defect: child assigned column1 is removed; Columns shrink to1; readding child then Measure crashes because retained cell metadata indexes outside current tracks. Original reproduction Program.cs/repro.log in artifacts/pr35-review/ records ArgumentOutOfRangeException at old Layout.cs148.

Root repaired Grid.GetCell to retain valid stored placements while resetting invalid old placements to default(0,0,1,1). Two meaningful regression groups cover rows/columns/spans, hidden children, default placement, valid retention and atomic occupied-track rejection. docs/beta-components.md states the contract. Independent serial rebuild succeeded with0 warnings/errors;215 managed checks passed and rebuilt original isolated reproduction reports GRID PASS. No remaining actionable finding in the reviewed working delta. Layout SHA256A8F7C3CDC8634E585F6F1FB1A27FA2DF30BF730C4281D7C064812A1776F433A3 and test SHA256FC14AD1A5B8A0E7733D4F354EAFD37A5539103123504F539BE7D64DC38FA5ABF match reviewer evidence. All review processes stopped. Fresh full gates/CI and merge remain pending; reviewer ran logic only, no GPU/window/package or image rerun.

## Invalid attempts and current verification

Native Debug build passed before managed verification. Initial grid-fix-tests.log and logic.log overlapped shared build output and executed the old runtime, both crashing at old Layout.cs148 despite repaired source. They are invalid mixed/stale-build validation attempts, retained as failures; neither is evidence that repaired source passed or still fails. Root will not build concurrently with the reviewer's clean serial retest.

CI run38996686797 at startup head1dfb407 was running four logic jobs; LinuxDebug had passed when root reported status. The run will be obsolete once Grid fix is pushed. Do not promote partial/old-head CI to final PR checks. A fresh user mouse-idle question for native cursor tests is pending; no new native cursor test started. Previous Beta idle agreement describes previous runs, not this pending condition.

## Next actions and boundaries

Freeze/push independently repaired-reviewed source and run fresh required full local/CI gates with appropriate images and matched native libraries. Commit final premerge evidence within the PR, verify final-head required checks and merge only with actual GitHub proof. Prior Beta TASK/PLAN remain completed; this is a separate PR+review+merge task. No tag/Release/NuGet publication, broadened platforms/IME/accessibility or1.0 claim. Original dirty workspace remains untouched.
