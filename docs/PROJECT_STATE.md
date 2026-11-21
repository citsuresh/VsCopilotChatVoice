# Project state

Updated: 2026-11-21. Transport checks/audit closure pass; local commit and continuation approved, not push.

## Current focus

All three bounded PoCs and metadata-only Phase 1 are complete/pushed. Extraction da8fd78 is pushed; remote main was verified at da8fd781c0181b108120eb3c57373877129a62b3. The user approved a local transport commit and continuation, not push. This snapshot records that approved checkpoint; consult git log/status for its resulting commit.

Part 1's shared model/navigation/preference types remain unchanged. Transport 0.4.2 adds real external pause/resume, correlated state and bounded async commands/cancellable startup. Both independently reproduced audit findings were fixed with user approval and independently closed. All eight routes pass (speech104). Standalone ReaderWindow/CLI/palette, native sample controls and metadata protocol are unchanged. Next: native playback ownership/lifetime, presentation and full Phase 2 acceptance before Settings.

## Runtime

- PoC 1 external guarded insertion/read-back and completed-reply observation remain accepted with parked cases. Native atomic editing and production reply delivery remain unsupported.
- PoC 2 remains the native prepared-sample Speak/Cancel view with an external worker, not the migrated real reader. PoC 3 remains optional/gated fake Ctrl capture with nonblocking current-user/session ownership and accepted native release-side adaptation.
- Phase 1 retains Debug + exact Exp + VSCOPILOTCHATVOICE_METADATA_POC=1, kernel-peer-checked hello/list/status, fresh instance and native session/client/epoch fences. Limits: 65,536-byte frames, 32 targets, 64 requests/connection, 15-second deadline. No content, edit, send or speech command in metadata IPC.
- Toolbar stays disabled. No microphone, recording, transcription, VoiceType copying, models/downloads, global hooks, production Settings, Speaker/reply history or integrated delivery. Keep mouse/reader playback independent.
- Shared model types preserve public names/signatures and standalone assembly identity. VSIX acquires no System.Speech or Reader executable assembly reference; the new reader-preview gate is only proposed, not implemented.

## Baseline evidence

- Phase 1 VS MSBuild/IDE build and all seven routes passed: contracts 136 (131 pure), gesture 209, speech 51, toolbar 62, bridge 209, self-test 1,868, integration 292/seven mappings. Only existing toolbar VSTHRD010 remains. Independent targeted audit closed the approved ambiguity/capacity/correlation items. Known unchanged preference/layout flakes were not fixed.
- Official 0.4.0 Exp install/configuration refresh exited 0; installed/build DLL SHA256 matched 7D19774A76A06B3DEDBCFB461D61154568FB9DF56CE503D1D3579CB69C8570CA. Two same-solution instances returned distinct lifetimes and live SDK session/epoch tuples; own status, old-target refusal, history retirement/reopen and bidirectional isolation passed. Background queries preserved foreground.
- Seven controlled UnknownTarget probes yielded expected InvalidDataException entries (five A, two B), with no other package errors or contents. Both task Exp instances exited after waited CloseMainWindow; endpoints disappeared. Exit codes were unavailable, not claimed 0. Protected main remained running; no speech or Send initiated.
- Native blank/history-only surfaces without a supported live SDK binding remain unavailable, cause unconfirmed. Native ambiguity/capacity/timeouts/pending-IPC failure injection remains fixture proof, not live injection. PoC limits and detailed evidence remain in IMPLEMENTATION_PLAN/DESIGN_DECISIONS.
- Phase 2 part 1 VS MSBuild and IDE build pass with only existing toolbar VSTHRD010. New --reader-model-probe passes 379 against built and installed VSIX DLLs; all seven prior routes passed unchanged on first run after extraction. Installed Reader self-test also passes 1,868. Independent targeted regression audit reports no outstanding finding.
- Official 0.4.1 Exp installer/configuration refresh exited 0. Packaged/installed manifests are 0.4.1. VSIX DLL SHA256 82D73110D63D0234A37B0BDE43F563D345B54AA30024862410734087ACD48154 and bundled Reader SHA256 482E9E11C749FEEAF77E43B1DFDD3370B6C56FCD69DDC6CE0B58394E366957AF match build/installation. Fresh task Exp loaded the package/metadata gate, served hello/list and logged zero package errors. It accepted CloseMainWindow, completed waited exit and its endpoint became unavailable; main35204 remained present. No exit-code claim for closure, reader speech, draft/Send or microphone.
- This is extraction/loading proof, not migrated UI/layout/playback acceptance. Native PoC still lacks real document/navigation/version controls and does not expose the new external pause/resume APIs. Preserve paused section-aware switching, segment/prompt offsets and isolated preference invariants; do not fix unrelated flakes.
- Transport VS MSBuild and IDE build pass; all eight final routes pass: speech104, toolbar62, bridge209, gesture209, contracts136, model379, Reader self1,868, integration292/seven mappings. Raw worker proof observes actual paused silence/same-prompt resume without host filtering; fixtures cover missing acknowledgements, actual five-second backpressure and cancelled/late startup. Only existing toolbar VSTHRD010; narrow VSTHRD003 suppression documents the UI-independent bounded acknowledgement task.
- Both approved audit findings are closed: cancellation epochs reject earlier queued replacements and idle first starts while preserving later deliberate Play; legacy Cancel wraps timeout in the existing InvalidOperationException contract. Exact silent regressions and independent closure pass (104 assertions). Unchanged model preference File.Replace failed twice before passing on a later rerun; no preference implementation/test changes.

## This continuation

Extraction da8fd78 was explicitly pushed and tracking verified. Twelve files currently changed for the transport/audit checkpoint: README; CODE_SUMMARY, DESIGN_DECISIONS, IMPLEMENTATION_PLAN, KEY_FLOWS, PROJECT_STATE, ROADMAP; Shared\SpeechWorkerHost.cs and SpeechWorkerProtocol.cs; Reader\SpeechWorker.cs; VSIX manifest0.4.2; SpeechWorkerRegressionTests.cs. No native control/controller or metadata changes. No reusable script or settings/content/model artifact authored.

Local transport commit/continuation approved, not push. Exclude docs\history-of-astronomy.md, personal settings, reply/audio, proof logs, models, artifacts and secrets. No 0.4.2 Exp deployment/launch occurred. Main35204 remains protected/untouched; skills repo/installed skills unchanged, no update/fetch.

All prior/task Exp are closed. The 0.4.1 task was PID26460; never reuse it or prior handles/identity tuples. Re-identify before future live actions. Logs stay outside the repo: session-state\files\p2-041-extraction-exp.xml plus retained p1-040-metadata-exp-a.xml/b.xml. No UI input was needed for this load proof; no screenshot or active UI cursor session.

All eight routes ran after the final transport source/test change; final edits record results, closure and approval. No redundant rebuild for documentation-only edits. Speech104 passed in both primary verification and independent closure. No reusable script authored; no UI input or settings/content artifact created. The requested combined cross-phase review is tracked in the plan/roadmap after all implementation phases; per-phase checks remain required.

## Next gate

Commit only the approved reviewed transport checkpoint with its required trailer; do not push. Continue confirmed Phase 2 native playback ownership/lifetime, then presentation/full Exp acceptance. Do not skip to Phase 3 Settings/Phase 6 delivery or main installation. Later commits/pushes still require separate approval; the combined review is a final cross-phase gate, not deferred blocker handling.

Reader proof preferences must be isolated. Required native frame/focus/playback failures need an architecture decision; synthetic tests cannot establish dock/auto-hide/close semantics. Request manual docking/pinning/theme/second-instance help only when necessary.

## Resume safely

Read README, this state, IMPLEMENTATION_PLAN section 6, DESIGN_DECISIONS, CODE_SUMMARY, ROADMAP and .github\copilot-instructions.md. Preserve this extraction. Graph-first queries traced original ReaderWindow.SwitchVersion/Seek; graph now also has stale ReaderPrototype model locations and lacks newer native reader/worker symbols. Use current source after stating that gap; graph was not regenerated/read wholesale. Feature/domain checklists absent; SCRIPTS index reviewed, no new reusable script authored.

Protect main/unrelated/skills VS; re-identify PIDs/command lines/HWNDs before live action. Only task-owned Exp closes, gracefully via CloseMainWindow/wait. Install only VSIXInstaller /quiet /rootSuffix:Exp /instanceIds:063a2394, never /shutdownprocesses; then devenv /RootSuffix Exp /UpdateConfiguration and Exp launch with dedicated /Log. Keep automatic deployment disabled and audio external.

After source/runtime changes: clear DOTNET_ROOT, use C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe .\VsCopilotChatVoice.slnx /restore /m /v:m /nologo /p:Configuration=Debug /p:DeployExtension=false and run all eight README routes. Keep preference tests serial/isolated. Recheck README/manual staged review before an approved commit; UTF8Encoding($false) message file with required Co-authored-by trailer. No automatic push.
