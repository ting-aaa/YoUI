<!-- PROJECT_MEMORY
{
  "blockers": [],
  "confidence": "high",
  "created_at": "2026-10-16T18:06:43Z",
  "derived_from": [],
  "event_id": "beta-pre-freeze-checkpoint-20261017-v1",
  "id": "RPT-20261016-F4A454",
  "kind": "report",
  "next_actions": [
    "Finish binding normalization and independent review, then freeze product source.",
    "Rerun complete Debug/Release Logic/Gpu/Window plus package consumers and inspect regenerated screenshots against frozen source."
  ],
  "review_after": "2026-10-31",
  "schema_version": 1,
  "scope": [
    "docs",
    "managed",
    "samples",
    "scripts",
    "tests"
  ],
  "sensitivity": "internal",
  "sources": [
    "docs/beta-development.md B01-B10 contract",
    "artifacts/beta-verification/bootstrap.log",
    "artifacts/beta-verification/editor-debug-content.log",
    "artifacts/verification/dfa133f9ad3b4605883ef11ddcdd5151/result.json",
    "artifacts/verification/20e4f8cafa144d3abc5c293906ed1626/result.json",
    "artifacts/beta-verification/beta-expanded.log",
    "artifacts/beta-headless.json",
    "artifacts/beta-verification/mouse-condition.txt",
    "artifacts/beta-packages/0.2.0-beta.1-Debug-191bf84f6df9413686f3ec2352d87122/manifest.json",
    "artifacts/beta-packages/0.2.0-beta.1-Release-cd220fe2a6a3410cae753d24accf0b35/manifest.json",
    "Root prefreeze checkpoint and beta_controls/beta_packaging memory_delta on 2026-10-17"
  ],
  "status": "active",
  "summary": "B01-B09 implemented; intermediate Logic/Window/GPU/package evidence passes on earlier dirty snapshots, while final B10 freeze and integrated verification remain pending.",
  "supersedes": [],
  "tags": [
    "beta"
  ],
  "task_id": "TASK-20261016-665876",
  "tier": "short",
  "title": "Beta implementation and intermediate verification checkpoint",
  "type_version": 1,
  "updated_at": "2026-10-16T18:06:43Z",
  "valid_as_of": "2026-10-17"
}
-->

# Beta implementation checkpoint before product freeze

## Objective and implementation

B01-B10 in docs/beta-development.md remains the complete Beta contract. In codex/beta-components at HEAD e336d6e6ec8881764d5b3a8053e152f791ddb581 with dirty Beta source, B01-B09 are implemented pending final integrated acceptance. Code includes composable ContentControl/Presenter and Button content, keyed nonvirtualized ItemsControl, CheckBox/RadioGroup/ComboBox/NumericUpDown/ProgressBar/TabControl/Separator/read-only DataGrid, external Popup roots and lifecycle, BoxLayout/Grid spans/Wrap, disposable bindings/commands/validation, v2 assets with v1 compatibility, Editor palette/properties/undo/save/reopen/C# export, Beta reference application, and three local NuGet packages with isolated consumers. The runtime-only consumer resolves YoUI alone, with no Editor dependency.

## Evidence currently accepted as intermediate

- Native Debug bootstrap succeeded; the original managed CS0103 ReorderChildren failure is preserved at artifacts/beta-verification/bootstrap.log and was resolved by layout integration. SDK10.0.401 resides at C:/Users/HT/DEV/YoUI/artifacts/alpha-audit-20261014/dotnet.
- Debug Logic gate dfa133f9ad3b4605883ef11ddcdd5151 passed on an earlier dirty snapshot: artifacts/verification/<id>/result.json and artifacts/beta-verification/Debug-Logic.log. It contains 198 managed and 23 editor tests, not the latest worker counts.
- Debug Window gate 20e4f8cafa144d3abc5c293906ed1626 passed on an earlier dirty snapshot. Separate native lifecycle, posted-message smoke and Beta native-message/mixed checks are recorded; this is not physical-input/IME acceptance. User explicitly agreed to leave the mouse idle during testing; artifacts/beta-verification/mouse-condition.txt records that condition.
- Latest standalone managed count reported by root is 203; editor-debug-content.log confirms 24 editor checks before a newly added fixture expected to increase the count to 25. Do not label the pending 25-check run passed.
- Actual NVIDIA RTX5060 Laptop DX12 output and 12 UiDocument.Dispatch routed checks are confirmed by artifacts/beta-verification/beta-expanded.log and artifacts/beta-headless.json. Earlier beta-verification/beta-headless.json has 11 checks; retain their separate paths/counts. Root inspected images, found/fixed focused selected text, numeric glyph clipping, light card and compact scrolling defects; regenerated final images still pending.
- Initial Debug package manifest artifacts/beta-packages/0.2.0-beta.1-Debug-191bf84f6df9413686f3ec2352d87122/manifest.json and Release .../0.2.0-beta.1-Release-cd220fe2a6a3410cae753d24accf0b35/manifest.json each report passed 23 runtime-only and 27 full checks with real NVIDIA RTX5060 Laptop DX12 and separate HWND submission. Debug sourceChangedDuringRun=true; Release was stable for its earlier dirty snapshot. Both precede latest fixes and must be rerun after freeze. Native hashes are Debug319733A8919374AD48F107A0C7CBC69538F60915ABDF37BFBE179C98931AEA20 and Release19EBC2861E585F9BB130843687E05C0F92AFB6141779951DAF2CC56B58AE9426. No packages were published.

## Review findings and repaired failures

Root and workers report nested Stack percent double-resolution fixed and independently reproduced, Tab selectedIndex=-1 validation corrected, ButtonContent declarative/editor/export gap closed, focused header backing and numeric glyph clipping repaired. Model normalization during binding is still finishing at this checkpoint. Numeric normalization and synchronous model correction need final regression evidence.

A Label constructor accessibility-name attempt duplicated button semantic names and broke an Alpha Single assertion; it was reverted. Initial package runs retained failures for an absent SDK path and a PowerShell Configuration argument construction bug; paths/argument handling were corrected and packages rerun. NativeProfile/config mismatch and missing native DLL rejection checks passed. These repaired attempts do not make original failed records pass.

## Boundaries and next actions

No final B10 complete Beta acceptance yet. Finish binding normalization, independent review and all product changes, freeze source, rerun Debug/Release Logic/Gpu/Window and packages against that source, regenerate and inspect images, then record exact SHA/dirty/snapshots and close the task only on complete B01-B10 acceptance. Keep platform/physical input/IME/accessibility/animation limitations, failed historical Alpha tag and original workspace uncommitted additions separate. Network fetch failed and freshest remote state remains unverified. Do not publish/tag as a side effect of local package validation.
