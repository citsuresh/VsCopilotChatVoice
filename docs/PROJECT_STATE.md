# Project state

Updated: 2026-11-21. Phase 1 metadata-only verification complete; local commit approved, not pushed.

## Current focus

All three bounded PoCs are complete and accepted with documented caveats. Commits d1ad9a2 and 64451c8 were explicitly approved and pushed. The pushed checkpoint is 0.3.2; the verified local Phase 1 foundation is 0.4.0.

Production decomposition approved 2026-11-21. Phase 1 implements typed identities/metadata, originating-target lifetime and metadata-only IPC. Required regression, targeted runtime audit closure and native gates now pass; accept this metadata-only foundation with the limitations below. Phase 2 has not started.

## Runtime

- PoC 1: external guarded UIA insertion/read-back and completed-reply observation. Native atomic editing and a production reply feed remain unsupported; parked cases stay parked.
- PoC 2: native floating sample reader with external speech over a current-user pipe. Docking/focus/theme/DPI and full reader-feature migration remain production acceptance work.
- PoC 3: pure configurable Ctrl Hold/Toggle with fake capture, startup/cancellation/late-result guards, current-user/session nonblocking recording mutex and gated log-only observer. Patched Left Ctrl and two live Exp handoff passed; native release-side adaptation is accepted. Right Ctrl remains the default; optional Ctrl stays gated.
- Phase 1: Debug + exact Exp + VSCOPILOTCHATVOICE_METADATA_POC=1 creates a current-user per-PID metadata endpoint with a fresh instance GUID, kernel peer checks, hello/list/status and per-target native composite identity/observation epochs. No contents/commands. Strict 65,536-byte framing, 32 targets, 64 requests per connection, 15-second deadline and per-connection order/replay guards. Reuses exact native capture and shares pipe identity/security/framing with speech. Late provider results cannot cross cancelled connections.
- No microphone, recording, transcription, models/downloads, VoiceType copying, global hooks, production settings or integrated delivery. Toolbar controls remain disabled; mouse/reader playback remain independent.

## Baseline evidence

- Phase 1 VS MSBuild and IDE build passed, DeployExtension=false. Only the existing toolbar VSTHRD010 warning remains; new UI-thread warnings were corrected.
- All seven routes pass: contracts 136 (131 pure), gesture 209, speech 51, toolbar 62, bridge 209, self-test 1,868, integration 292/seven mappings. All passed first attempt after the test-only status helper. Trailing-JSON roots, ambiguity recovery, request 64/65 capacity and independently wrong acknowledgement ID/sequence are covered. An earlier unchanged preference-save File.Replace I/O failure passed on immediate rerun; no unrelated flake fix.
- User approved the independently reproduced ambiguity listener fault and stronger negative tests. Narrow capture containment now reports TargetUnavailable and preserves recovery. Independent targeted audit ran contracts 131 and closed all three items with no outstanding targeted finding.
- Official 0.4.0 Exp installation/configuration refresh exited 0. Installed DLL matched build SHA256 7D19774A76A06B3DEDBCFB461D61154568FB9DF56CE503D1D3579CB69C8570CA. Both live task instances loaded the metadata-only gate and returned distinct lifetime GUIDs for the same solution; pinned handshake/list reconnections passed.
- Querying populated background Exp A while Exp B's settled main window was foreground preserved that foreground. Logs have no contents. Seven deliberate UnknownTarget probes produced expected InvalidDataException entries (five A, two B), with no other package errors. Restored sample readers were hidden; no speech or bridge Send was started.
- Exp A's user-selected populated SDK chat exposes session GUID/client ID/epoch and known trusted/idle/read-only/transcript state. Exact status succeeds; wrong epochs refuse and next clients recover. Back/history retires the target; reopening the same verified session produces a fresh epoch, refuses the old one and accepts the new one.
- Both user-prepared live SDK conversations have distinct session/epoch tuples and instance lifetimes. Each own status succeeds; A's old conversation and both cross-instance tuples are refused. Both task Exp instances accepted CloseMainWindow, completed waited exit and disappeared; their endpoints became unavailable. Exit codes were not retrieved. Hosting main VS remained running.
- Exp package/DLL hash matched the 0.3.2 build. Left Ctrl Hold/repeats, normal shortcuts, focus/mouse cancellation, patched Toggle/Escape release and two-Exp cancellation-before-acquisition passed with zero package errors. Real child processes separately prove Busy/race/abandonment. See IMPLEMENTATION_PLAN for detailed timing and limits.
- Initial independent audit covers 0.3.1 only, not 0.3.2. Final source/manual review and old/new DLL reproduction are recorded separately. Known preference-save and earlier layout-sensitive integration flakes were not fixed here.

## This continuation

New source: Shared\ChatBridgeContracts.cs, ChatMetadataServer.cs, PipePeerIdentity.cs; VSIX\ChatMetadataBridge.cs; tests\ContractRegressionTests.cs. Related updates: ChatBridgePoc capture extraction, VoicePackage, shared speech framing/host, project Compile links, manifest 0.4.0 and test dispatch. The test-only --metadata-status helper adds typed, bounded exact-target inspection and five regression assertions; production DLL is unchanged. README, IMPLEMENTATION_PLAN, CODE_SUMMARY, KEY_FLOWS, DESIGN_DECISIONS, ROADMAP and this state reflect the scope. No reusable script was authored.

The user explicitly approved a local Phase 1 commit after verification/manual review. No additional push is approved; previous push approval covered the two PoC commits only. Exclude docs\history-of-astronomy.md, settings, chat/audio, logs, models, artifacts and secrets. Installation/UI actions were Exp-only. Main VS PID35204 remains protected and untouched. Skills repo/installed skills untouched; no update/fetch under the earlier explicit constraint.

Task Exp A/B are closed; do not reuse their PIDs/HWNDs or lifetime/target tuples. Hosting/protected main VS was reidentified as PID35204 and remains running. Re-identify all processes before any future live action; no main/skills VS input, shutdown or installation was performed.

Proof logs remain outside the repo under the CopilotCli session-state\files folder: p1-040-metadata-exp-a.xml and p1-040-metadata-exp-b.xml. Temporary UI output files were removed. No screenshot or reusable custom script was authored; UI cursor indicator is cleared.

## Next gate

Source/shared helper/project/test changes have been directly reviewed; independent runtime audit findings are closed. Local commit is explicitly approved; this state snapshot belongs to that verified increment. See git log/status for its resulting commit. No additional push is approved. Next gate: explicit push approval or a separately reviewable Phase 2 decomposition. Preserve the deliberate astronomy exclusion and all content/settings/artifact exclusions.

Phase 1 is metadata-only, not production delivery/reader migration. Blank/history-only surfaces were observed without native targets, cause unconfirmed; remain unavailable until a supported live binding exists. Native ambiguity/capacity/timeouts/pending-IPC shutdown remain fixture proof, not live injection claims. Next production work is a separately reviewable Phase 2 reader migration increment after the current commit/review gate; do not implement later phases automatically.

PoC limits remain: physical Right Ctrl, early native shortcut/Esc down, missing key-up, lock/sleep, real capture latency/hardware/transcription, cross-logon-session arbitration and live-VS crash recovery are unproved. Keep Ctrl gated and retain mouse/reader fallback.

## Resume safely

Read README, this state, IMPLEMENTATION_PLAN section 6, DESIGN_DECISIONS, CODE_SUMMARY, ROADMAP and .github\copilot-instructions.md. Preserve this documentation work. Graph-first query located ChatTargetObservation.Snapshot's caller; current source confirms the composite native identity and lifetime boundary. The graph is still stale for newer PoC components; it was not regenerated or read wholesale. Feature checklist is absent.

Re-identify PIDs/command lines/HWNDs before live actions; protect unrelated/main/skills VS. Only task-owned Exp may close, gracefully via CloseMainWindow and waited exit. Install only with VSIXInstaller /quiet /rootSuffix:Exp /instanceIds:063a2394, never /shutdownprocesses; then devenv /RootSuffix Exp /UpdateConfiguration and Exp launch with dedicated /Log. Keep automatic deployment disabled; no main install.

After runtime changes: clear DOTNET_ROOT, use C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe .\VsCopilotChatVoice.slnx /restore /m /v:m /nologo /p:Configuration=Debug /p:DeployExtension=false and run all seven README suites. Commit only after approval/manual review, with UTF8Encoding($false) message file and required Co-authored-by trailer. No push without approval.
