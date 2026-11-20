# Project state

Updated: 2026-11-20. This file is overwritten, not appended, at the end of each working session.

## Current focus

The three-project solution foundation is complete. Official Exp-only installation,
post-install configuration refresh and actual package initialization are verified.
The next implementation step is scoped chat toolbar insertion, not further speculative
registry modification. Project-memory Bootstrap is now set up for this repository.

## Verified behavior

- Full solution/VSIX build and rebuild passed during foundation validation.
- Reader checks previously passed: 1,868 self-test assertions and 292 integration assertions,
  including seven live progress mappings and isolated child-process restart preferences.
- Pre-commit validation: solution build and 1,868 self-test assertions passed. The first
  unchanged integration-harness run failed at `VerifyPreferences` with "Selecting Detailed
  did not replace Concise on disk"; an immediate separate rerun passed all 292 assertions
  and seven mappings. Treat this as an unresolved intermittent result, not a clean first pass.
- Graph Bootstrap completed for `VsCopilotChatVoice.slnx`: 119 nodes, 1,115 edges in 7.49 s;
  all three projects analyzed. Slug `vscopilotchatvoice`; dependencies renamed after success.
  Graph commit metadata references HEAD at generation, not a guarantee of worktree cleanliness.
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

- No Mic, Speaker or Settings controls in chat yet. Handle chat recreation without duplicates
  and cleanly detach any future integration; test everything in Exp first.
- No persistent reply feed, IPC/toolkit/instance routing or companion skill implementation.
- Reader is still the separate prototype: pale-yellow theme, one prepared reply per launch,
  X closes rather than production hide behavior; no production show/hide/minimize protocol.
- No voice/rate/volume settings window, automatic-read-aloud preference or VS theme sync.
- VoiceType discovery/launch and correctly targeted dictation are pending.
- Automatic Copilot-response capture has no verified mechanism; do not imply otherwise.
- Intermittent preference-persistence harness failure needs investigation; no reader/test
	fix made during documentation Bootstrap. Passing audit checks do not resolve that observation.
- Foundation commit authorized after review; no push authorized. Repository-local commit
  identity and origin username were configured as requested. Main-instance installation
  still requires completed Exp testing plus explicit approval.

## Recently changed files

- Solution and ignore rules: `VsCopilotChatVoice.slnx`, `.gitignore`.
- `src\VsCopilotChatVoice.VisualStudio\`: package, project and VSIX manifest.
- `src\VsCopilotChatVoice.Reader\`: migrated reader source and project.
- `tests\VsCopilotChatVoice.Reader.Tests\`: migrated executable harness and project.
- `README.md`, `.github\copilot-instructions.md`: setup/status, approved behavior and contributor rules.
- `docs\`: architecture, flows, decisions, state, roadmap and per-solution graph/viewer artifacts.

## Resume safely

Read `README.md`, `CODE_SUMMARY.md`, `DESIGN_DECISIONS.md` and `ROADMAP.md` before implementing.
Use the graph registry in `.github\copilot-instructions.md` for C# symbol lookups.
Re-identify VS windows/processes after restart; do not persist or reuse transient HWNDs/PIDs.