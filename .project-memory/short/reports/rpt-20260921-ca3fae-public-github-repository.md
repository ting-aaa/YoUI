<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "confirmed",
  "created_at": "2026-09-21T15:04:47Z",
  "derived_from": [],
  "event_id": "publish-youi-github-public-20260921-v1",
  "id": "RPT-20260921-CA3FAE",
  "kind": "report",
  "next_actions": [
    "Continue the product roadmap in docs/design-status.md."
  ],
  "review_after": "2026-10-05",
  "schema_version": 1,
  "scope": [
    ".project-memory"
  ],
  "sensitivity": "public",
  "sources": [
    "gh repo edit ting-aaa/YoUI --visibility public --accept-visibility-change-consequences",
    "gh api repos/ting-aaa/YoUI --jq visibility check"
  ],
  "status": "completed",
  "summary": "The ting-aaa/YoUI GitHub repository was changed from private to public at the user request.",
  "supersedes": [
    "RPT-20260921-C32975"
  ],
  "tags": [
    "github",
    "publish",
    "visibility"
  ],
  "task_id": "TASK-20260912-CC3A20",
  "tier": "short",
  "title": "Public GitHub repository",
  "type_version": 1,
  "updated_at": "2026-09-21T15:04:47Z",
  "valid_as_of": "2026-09-21"
}
-->

# Public GitHub repository

## Outcome

Following the user's instruction, the YoUI GitHub repository was changed from private to public.

## Evidence

- Visibility change command: gh repo edit ting-aaa/YoUI --visibility public --accept-visibility-change-consequences.
- GitHub API reports full_name ting-aaa/YoUI, private false, visibility public, and default branch main.
- Repository URL: https://github.com/ting-aaa/YoUI.

## Scope and caveat

No product source was changed and no full GPU, native-window, or managed verification suite was rerun. All files already tracked in the repository, including .project-memory, are now publicly visible.
