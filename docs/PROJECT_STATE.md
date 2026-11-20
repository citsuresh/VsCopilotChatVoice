# Project state

Updated: 2026-11-20. This file is overwritten, not appended, at the end of each working session.

## Current focus

Foundation committed locally as `f06b047`; no push. The approved toolbar-shell phase is now
implemented with disabled controls and deterministic checks. Initial 0.2.0 placement in Exp
was verified; the user requested matching flat icons and a shared border after seeing it.
The user approved the final 0.2.3 appearance after the group was tightened to 24 DIPs, and
explicitly authorized this toolbar checkpoint commit. Full Exp lifecycle/theme/DPI/accessibility
checks remain pending. Independent review identified a cached
attachment revalidation defect; the user approved fix 1, now implemented with regression
checks. Independent re-review confirmed that fix resolved with no new findings. Only the task's Exp
instance is an installation target; no normal-instance installation is authorized.

## Verified behavior

- Full solution/VSIX build and rebuild passed during foundation validation.
- Grouped-toolbar solution build passed. Its isolated actual-VSIX harness passes 62 assertions;
  reader checks also pass 1,868 self-tests and 292 integration assertions/seven mappings.
  Initial toolbar lifecycle testing exposed retained controls after hiding; corrected discovery
  now revalidates visibility and uniqueness before retaining an attachment, with passing checks.
- Compact sizing, borderless icons and the themed group border have deterministic checks;
	the 0.2.3 group measures exactly 24 DIPs high with unchanged 14-DIP glyphs. An initial
  auto-sized attempt exceeded the target due to DPI rounding; explicit outer height corrected
	it and all 62 toolbar checks pass. Official 0.2.3 Exp update/configuration refresh exited 0;
  the package loaded and the user approved the tighter-border appearance.
- Latest independent review passed the solution build and all 62 toolbar assertions. It found
  a low-severity test-observation gap: the glyph-height predicate permits collapsed/zero-height
  content and does not prove unclipped rendering or unchanged glyph identity. Proposed test-only
  fix 2 was not approved/applied; no production clipping defect was established.
- Audit fix 1 revalidates supported host/wrapper placement and competing toolbar IDs before
  retaining a settled attachment. The added regression first failed against the unfixed DLL
  on an attached host becoming vertical, then all 58 toolbar assertions passed after the fix.
  Tests also cover a competing group introduced after attachment, wrappers acquiring unrelated
  children, owned-control preservation and recovery. No reader production code changed.
- Official 0.2.0 Exp update/refresh exited 0; a fresh launch logged attachment and showed
  three disabled controls with Speaker off. Live geometry confirmed dialog-style minimums
  enlarged Mic/Settings; sizing and shared-group styling address that observed presentation.
- Reader checks previously passed: 1,868 self-test assertions and 292 integration assertions,
  including seven live progress mappings and isolated child-process restart preferences.
- Pre-commit validation: solution build and 1,868 self-test assertions passed. The first
  unchanged integration-harness run failed at `VerifyPreferences` with "Selecting Detailed
  did not replace Concise on disk"; an immediate separate rerun passed all 292 assertions
  and seven mappings. Treat this as an unresolved intermittent result, not a clean first pass.
- Graph Bootstrap completed for `VsCopilotChatVoice.slnx`: 119 nodes, 1,115 edges in 7.49 s;
  all three projects analyzed. Slug `vscopilotchatvoice`; dependencies renamed after success.
  Graph commit metadata references HEAD at generation, not a guarantee of worktree cleanliness.
- Prior incremental graph update includes the grouping/style change: three changed files,
  154 nodes, 1,352 edges in 5.15 s; generated dependencies renamed to the registered slug.
- Approved lifecycle-fix graph refresh analyzed two changed files: 154 nodes, 1,415 edges
  in 11.28 s; generated dependencies renamed to the registered slug.
- Border-height graph refresh analyzed two changed files: 154 nodes, 1,417 edges in 4.74 s;
  generated dependencies renamed to the registered slug.
- Independent final regression audit completed: solution build, 1,868 self-test assertions
  and 292 integration assertions/seven mappings passed on the auditor's first executions.
  Its only finding was misleading graph-viewer search wording. The wording-only correction
  was reviewed and committed upstream in the skills repository (`40ec7da`); this repository's
  viewer copy is refreshed from that source. No reader or VSIX runtime defect was established.
- Fresh Exp activity log contains source `VsCopilotChatVoice` and the foundation-loaded
  message. The experimental IDE was responsive. Normal VS has no matching installed extension.
- SDK deployment failed with VSSDK1031; the official Exp installer and waited configuration
  refresh exited 0. Stalled read-only registry probes were stopped; no hive reset performed.

## Open tasks / limitations

- 0.2.3 Mic/Speaker/Settings controls are intentionally disabled; earlier placement/lifecycle
	and grouped-style checks pass, but fresh Exp chat close/reopen and theme/DPI/accessibility matrix still
  need verification. Unsupported/ambiguous layouts report unavailable instead of unsafe insertion.
- No persistent reply feed or IPC/toolkit/instance routing. Companion policy source was
	authored, independently reviewed, installed with matching source hashes, and committed as
  `a654406` in the separate skills repository; no push. It does not supply the missing
  production bridge or automatic Speaker state.
- Reader is still the separate prototype: pale-yellow theme, one prepared reply per launch,
  X closes rather than production hide behavior; no production show/hide/minimize protocol.
- No voice/rate/volume settings window, automatic-read-aloud preference or VS theme sync.
- VoiceType discovery/launch and correctly targeted dictation are pending.
- Automatic Copilot-response capture has no verified mechanism; do not imply otherwise.
- Intermittent preference-persistence harness failure needs investigation; no reader/test
	fix made during documentation Bootstrap. Passing audit checks do not resolve that observation.
- Foundation commit completed after review; the user authorized the reviewed toolbar checkpoint
  commit with the documented remaining validation/test limitation. No push authorized. Repository-local commit
  identity and origin username were configured as requested. Main-instance installation
  still requires completed Exp testing plus explicit approval.

## Recently changed files

- `src\VsCopilotChatVoice.VisualStudio\ChatToolbarIntegration.cs`, `VoicePackage.cs` and
	`source.extension.vsixmanifest`: new controller, package lifetime/theming/logging and 0.2.3 identity.
- `tests\VsCopilotChatVoice.Reader.Tests\ToolbarRegressionTests.cs`, `ReaderRegressionTests.cs`:
  optional isolated toolbar mode; default reader test behavior unchanged.
- `README.md` and memory docs: current preview behavior, exact validation and remaining gates.

## Resume safely

Read `README.md`, `CODE_SUMMARY.md`, `DESIGN_DECISIONS.md` and `ROADMAP.md` before implementing.
Use the graph registry in `.github\copilot-instructions.md` for C# symbol lookups.
Re-identify VS windows/processes after restart; do not persist or reuse transient HWNDs/PIDs.