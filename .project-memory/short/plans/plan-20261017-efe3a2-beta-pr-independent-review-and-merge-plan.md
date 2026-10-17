<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-17T02:52:49Z",
  "derived_from": [],
  "event_id": "beta-pr-review-merge-plan-20261017-v1",
  "id": "PLAN-20261017-EFE3A2",
  "kind": "plan",
  "next_actions": [
    "Freeze/push independently confirmed PR35 Grid repair and run fresh complete revision-specific gates/CI.",
    "Record final premerge review, verify final-head CI and input-test condition, merge and record actual GitHub state."
  ],
  "review_after": "2026-10-31",
  "schema_version": 1,
  "scope": [
    "docs",
    "managed",
    "tests"
  ],
  "sensitivity": "internal",
  "sources": [
    "User authorized PR+independent review+merge2026-10-17",
    "TASK-20261017-06D61A",
    "docs/beta-review.md completed implementation acceptance",
    "https://github.com/ting-aaa/YoUI/pull/35",
    "RPT-20261017-42017B Grid finding/repair and mixed-build evidence boundaries"
  ],
  "status": "active",
  "summary": "Publish Beta PR, independently review actual head, repair/reverify findings, commit review record, verify checks and merge with explicit evidence.",
  "supersedes": [],
  "tags": [
    "beta",
    "pull-request"
  ],
  "task_id": "TASK-20261017-06D61A",
  "tier": "short",
  "title": "Beta PR independent review and merge plan",
  "type_version": 1,
  "updated_at": "2026-10-17T03:02:25Z",
  "valid_as_of": "2026-10-17"
}
-->

# Beta PR review and merge plan

1. Commit workflow memory, push codex/beta-components and create/attach the Beta PR against main using the verified current remote baseline. Lead PR description with component/framework behavior and exact local Beta acceptance scope.
2. Delegate a new independent reviewer with a fresh bounded context pack. Review actual PR diff/head rather than treating previous implementation review as sufficient. Require actionable findings or explicit no-findings conclusion with evidence.
3. Fix actionable findings, add meaningful targeted regressions and re-review the repaired head. If renderer/input/layout changes, run scripts/verify.ps1 and inspect relevant images; preserve original/final product revision boundaries.
4. Record premerge independent review/check evidence in project memory and commit it within the PR. Confirm GitHub required checks and final head before merge. No guessed pending check success.
5. Merge the authorized PR, verify GitHub state/merge SHA and remote main, then close task/plan and record a local postmerge outcome/handoff if needed to avoid recursive PRs. Leave unrelated original1.0 workspace untouched.

## Current evidence

PR35 is created/attached at startup memory-only head1dfb407 after2ad3a49; origin/main e336d6e was freshly fetched. Grid reattachment P2 repair is independently confirmed: serial build0 warnings/errors,215 managed checks and original repro GRID PASS. Mixed-build failures remain invalid evidence; reviewer ran logic only. Freeze/push, fresh complete local gates/final CI and GitHub merge proof remain, so task/plan active. Fresh native idle answer pending and no cursor test started. Prior90e86ad acceptance stays revision-specific; no tag/Release/NuGet publication or expanded support claim.
