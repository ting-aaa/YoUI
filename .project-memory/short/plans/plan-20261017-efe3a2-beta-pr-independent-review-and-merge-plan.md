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
    "Create PR and get fresh independent actual-diff review.",
    "Record premerge proof, verify checks and merge; record postmerge state without recursive PR."
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
    "docs/beta-review.md completed implementation acceptance"
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
  "updated_at": "2026-10-17T02:52:49Z",
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

Initial cleanhead2ad3a49c56ec9e59edda0590186ef4b2e1a70f42; origin/main e336d6e6ec8881764d5b3a8053e152f791ddb581 after successful fetch. Existing accepted Beta product90e86ad proof remains valid for that revision, with docs/memory closure only afterward. No PR/independent new review/merge evidence yet. Prior Beta implementation task is completed. This plan does not request tag/Release/NuGet publication or broaden platform/IME/accessibility1.0 claims.
