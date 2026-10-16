<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-16T18:25:40Z",
  "derived_from": [
    "RPT-20261016-F4A454"
  ],
  "event_id": "beta-first-clean-freeze-and-scope-failure-20261017-v1",
  "id": "RPT-20261016-919434",
  "kind": "report",
  "next_actions": [
    "Preserve original RadioGroup scope reproduction and independently confirmed repaired result.",
    "Create second clean freeze and rerun Debug/Release All plus stable local package pair and inspect fresh images."
  ],
  "review_after": "2026-10-31",
  "schema_version": 1,
  "scope": [
    "docs",
    "managed",
    "scripts",
    "tests"
  ],
  "sensitivity": "internal",
  "sources": [
    "artifacts/verification/c8029b74b7114e9f9fede5f2705e6aa0/result.json",
    "artifacts/verification/8539b609ca90422298f9205733dad1fa/result.json",
    "artifacts/beta-final/0555ea504eaf7a95aa9e44350b367ef16c704fce/Debug-All.log",
    "artifacts/beta-final/0555ea504eaf7a95aa9e44350b367ef16c704fce/Release-All.log",
    "artifacts/beta-review-20261017/repro-modal-0555ea5.log",
    "artifacts/beta-review-20261017/repro-modal-repaired.log",
    "artifacts/beta-verification/orchestration-final.log",
    "artifacts/beta-verification/mouse-condition.txt",
    "Root second-freeze checkpoint and beta_controls followup memory_delta on 2026-10-17"
  ],
  "status": "active",
  "summary": "Clean0555ea5 Debug/Release All passed; later RadioGroup scope leak is repaired and independently reproduced, while second freeze All reruns and stable package pair remain pending.",
  "supersedes": [
    "RPT-20261016-F4A454"
  ],
  "tags": [
    "beta"
  ],
  "task_id": "TASK-20261016-665876",
  "tier": "short",
  "title": "Clean Beta All gates passed with new keyboard scope failure pending",
  "type_version": 1,
  "updated_at": "2026-10-16T18:27:01Z",
  "valid_as_of": "2026-10-17"
}
-->

# First clean Beta freeze passed All gates; new keyboard-scope defect remains

## Objective and tested revision

B01-B10 remains the complete Beta contract in docs/beta-development.md. Product commit 0555ea504eaf7a95aa9e44350b367ef16c704fce was clean for both full All gate runs. They are valid evidence for that exact revision and environment, but a subsequent independently reproduced P2 defect prevents final Beta acceptance. Task and plan must remain active; do not convert passing prior gates into a claim that the known defect is absent.

## Confirmed verification

- Debug scripts/verify.ps1 All passed with dirty=false and SHA0555ea504eaf7a95aa9e44350b367ef16c704fce. Result: artifacts/verification/c8029b74b7114e9f9fede5f2705e6aa0/result.json. Full log: artifacts/beta-final/0555ea504eaf7a95aa9e44350b367ef16c704fce/Debug-All.log.
- Release All likewise passed with dirty=false on the same SHA. Result: artifacts/verification/8539b609ca90422298f9205733dad1fa/result.json. Full log: same beta-final/SHA/Release-All.log.
- Each includes 211 managed checks, 25 editor checks, 12 Beta routed UiDocument checks, 13 Beta native-message/mixed checks and 48 HWND lifecycle checks. Logic, real GPU and native-window results remain distinct even though the All orchestration ran all layers.
- Actual GPU is NVIDIA GeForce RTX5060 Laptop DX12 DiscreteGpu, driver32.0.15.7705; host DPI125%. Debug fresh screenshots were inspected by root and were legible. Native cursor tests used the user's explicit mouse-idle agreement, recorded in artifacts/beta-verification/mouse-condition.txt. Posted/injected messages and controlled OS cursor motion do not establish physical keyboard/IME/accessibility acceptance.
- Fresh PowerShell orchestration harness passed exit0; artifacts/beta-verification/orchestration-final.log verifies argument guards and failure propagation through46 All commands. Its mock SHA012345... is orchestration-only and must never be mistaken for real GPU evidence.

## New failure after passing gates

Independent requirement audit reproduced RadioGroup arrow navigation leaking outside Modal/Popup keyboard scope at 0555ea5. artifacts/beta-review-20261017/repro-modal-0555ea5.log ends modal-radio-navigation focusModal=True selectedBackground=True modalChecked=False: focus stays in the modal but the background group member becomes selected. This is a P2 behavioral defect uncovered by a missing scenario, not a failure of the already-completed gate executions. Controls worker has now added a shared document KeyboardNavigationRoot/IsInKeyboardNavigationScope used by Tab and RadioGroup.Move, excluding inactive, foreign, detached and lower-overlay candidates and rechecking after callbacks. Two new modal/nested-popup tests exist, and current213 managed Debug checks passed. Independent repaired reproduction artifacts/beta-review-20261017/repro-modal-repaired.log confirms modalChecked=True with selectedBackground=False, top popup focus/selection preserved, and no lower/background selection even after explicitly focusing a lower overlay. Repair is implemented and independently review-confirmed. Complete new revision All gates and stable package pair remain pending. Programmatic RadioButton.Value intentionally keeps global group selection behavior.

## Other repaired controls findings

RadioButton.Group transfer now rejects nested assignments from old/new SelectionChanged callbacks; failures leave membership consistent and clear the guard for retries. VirtualList.ItemCount commits count/clamped selection/offset before notification, emits effective selection changes and attempts remaining observers even after callback errors. Added tests cover reentry, normalization/shrink/zero/invalid counts, throwing observers and corrections. Worker reports these changes are included in the211 managed checks. Earlier model-normalization binding, nested Stack percent, selected tab validation and ButtonContent declaration/export fixes are incorporated into the first freeze.

## Packaging and remaining exit work

B09 initial Debug/Release local packages passed23 runtime-only and27 full consumer checks, actual GPU and separate HWND submission, as preserved in RPT-20261016-F4A454. They are older snapshots. Final stable paired packages have not yet run and must be rebuilt after the keyboard-scope fix. No tag/publication occurred.

Required next actions: create a second clean product freeze containing the implemented and independently confirmed scope repair, rerun both full Debug/Release All gates and a stable matching package pair, inspect fresh screenshots, then reconcile exact SHA/dirty/snapshot evidence against every B01-B10 row. Preserve old passing gate records, original failure reproduction and repaired reproduction. Remote fetch freshness, original dirty1.0 workspace, historical failed Alpha tag and unverified platform/IME/animation/accessibility boundaries remain unchanged.
