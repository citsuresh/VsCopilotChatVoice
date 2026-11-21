# Project state

Updated: 2026-11-21. Session-end snapshot; detailed evidence/history is in IMPLEMENTATION_PLAN and DESIGN_DECISIONS.

## Current focus

Bounded PoC 3 completed as extension 0.3.2: **adapt / accept the fake-capture proof; keep optional Ctrl gated**. Native Chat sometimes suppresses shortcut/Esc key-down, so release-side invalidation and Escape-release cancellation were required. Patched Left Ctrl Hold/Toggle and two live Exp ownership handoff passed. No microphone, recording, transcription, models/downloads, VoiceType copying, global hooks, production settings or delivery. Toolbar controls remain disabled; mouse and reader playback remain independent.

Prior accepted bounded proofs: PoC 1 external guarded insertion/read-back and completed-reply observation (native atomic editing/production feed unsupported); PoC 2 native floating reader/external speech with docking/focus/theme/DPI caveats. PoC 2 pushed through 0f1cd11; d1ad9a2 is the preceding local state note. This snapshot belongs to the requested PoC 3 local checkpoint. No push or main-instance installation is authorized.

## Implemented

- Shared CtrlGestureDetector: serialized pure Right Ctrl/Hold default, configurable Left/Toggle, immediate down request, repeat/held-input rejection, tentative key/mouse cancellation, solitary toggle confirmation/stop, release-before-ready deferral and generation-fenced ready/stop/result. Esc/focus cancels and rejects late work.
- Shared RecordingOwnership: current-user ACL-restricted Local mutex, WaitOne(0), explicit Busy/AcquiredAfterAbandonment, no PID file/live takeover, same-thread acquire/release/dispose and local recursive-acquire suppression. Release follows stop acknowledgement; finalization is separately serialized. Local means the same Windows session, not separate logon sessions.
- ChatGesturePoc: Debug + exact /RootSuffix Exp + VSCOPILOTCHATVOICE_GESTURE_POC=1; nearest supported chat root via the existing toolbar picker. Preview down/up use handledEventsToo, never change Handled, and accept only active-window/inside-chat keyboard focus. Logs only counters/decisions/state/generation. Fake queued ready/stopped/result; no content, insertion or send. Focus/visibility/unload/binding/deactivation guards and package teardown are wired.
- Native adaptation in 0.3.2: other key releases invalidate tentative gestures; Ctrl up samples still-held inputs for the opposite release ordering; Escape up cancels when its down was suppressed. No delay/hook/swallowing. This is not proof of immediate native chord/Esc-down cancellation.
- GestureRegressionTests: --gesture-probe [VSIX-DLL], fake capture owner, GUID-isolated ownership names, real child contention/race/abandonment and actual-DLL WPF observer fixtures. Shared Compile links and test dispatch are wired.

## Verified

- Requested VS MSBuild Debug solution with DOTNET_ROOT cleared and DeployExtension=false passed. One existing toolbar VSTHRD010 warning remains. All six suites passed after the final runtime change and again at the final commit-review checkpoint: gesture 209 (196 without DLL), speech 51, toolbar 62, bridge 209, self-test 1,868, integration 292/seven mappings.
- Initial independent read-only 0.3.1 audit found no significant issue and ran its then-current152 pure/process assertions. That agent context expired; no independent review of0.3.2 is claimed. Parent reviewed final source/wiring/tests and reproduced old/new actual-DLL behavior: the suppressed-down shortcut changed Stopping/FinalizeCapture to Listening/None. Regressions cover both release orders and Escape-up/cancellation races.
- Official Exp-only0.3.1/0.3.2 installation and configuration refresh exited0. Current manifest0.3.2 and built/packaged/installed DLL match SHA2568CE4EB2C72E7F487851C3C00A555E457369EB7CD8BC56F89CB362C6D38594E83.
- Live Left Ctrl Hold:2.67s/68 repeats and6.07s/173 repeats each requested once and finalized once on release;241 RepeatIgnored decisions across runs. Focus/mouse cancellations and later release produced no result/finalization. User confirmed Ctrl+A/C/V; toolkit scratch Ctrl+V/A/C read back exactly, restored clipboard, cleared draft and sent nothing. Reader Speak/Cancel remained usable.
- Patched live Toggle: first tap confirmed Listening; Ctrl+A/C produced StopTapInvalidated, retaining Listening; later solitary tap finalized once. Fresh toggle cancelled on EscapeRelease without result/finalization. Early physical Esc-down was not proved.
- Two live Exp instances: first Cancelled at09:09:04.346UTC before second Acquired at09:09:04.988; second Cancelled at09:09:06.518 before first reacquired at09:09:07.116. Final focus loss cancelled. No overlap/result/finalization on these cancellations; zero package errors in both dedicated logs. This proves focus-cancelled handoff/reacquisition, not simultaneous live-VS Busy/crash recovery. Real child processes separately prove zero-wait Busy, race election, release/retry and abandonment.
- Earlier unchanged integration layout-sensitive failure passed reruns; known preference-save flake remains unresolved. Neither was modified or claimed fixed.

## Session end / remaining boundaries

Both final task Exp processes were already absent when reidentified for cleanup; no final close command was needed or issued. No Reader.exe worker remained. Earlier task Exp restarts used waited CloseMainWindow, never force shutdown. Protected main/skills VS, skills repo and installed skills were not driven/closed/modified by this task. All proof logs stay outside the repository. UI input used the verified managed agentdebug-ui and red cursor wrapper; no blue dot.

Checkpoint policy: local commit only after final verification/review; no push without explicit user approval. Exclude docs\history-of-astronomy.md (deliberately untracked or unsaved), personal settings, chat/audio, logs, models, artifacts and secrets. No reusable repository script was authored. No further manual PoC 3 gesture check is pending.

Unsupported/unmeasured: physical Right Ctrl (available keyboard lacks it), early native shortcut/Esc-down delivery, missing decisive key-up, lock/sleep, actual microphone startup latency/hardware/transcription, cross-logon-session ownership and live-VS crash recovery. Keep Ctrl gated; defer/drop it if a future surface suppresses both decisive edges, preserving mouse/reader behavior. Production phases remain unapproved.

## Resume safely

Read README, this state, IMPLEMENTATION_PLAN section 6, DESIGN_DECISIONS, CODE_SUMMARY, ROADMAP and .github\copilot-instructions.md. Preserve existing work. Registered graph query for ChatToolbarIntegration.TryFindHost returned symbol not found; graph is stale for PoC 2/3, not refreshed/read wholesale. Fall back to source without inferring an absent capability from stale graph metadata.

Re-identify command lines/PIDs/HWNDs before every live action/restart. Only a task-owned Exp may close, gracefully with CloseMainWindow and waited exit. Installation route: VSIXInstaller /quiet /rootSuffix:Exp /instanceIds:063a2394 (never omit Exp or use /shutdownprocesses), then devenv /RootSuffix Exp /UpdateConfiguration, then launch Exp with a dedicated /Log path. Keep automatic deployment disabled; no main-instance installation.

Build with DOTNET_ROOT cleared and C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe .\VsCopilotChatVoice.slnx /restore /m /v:m /nologo /p:Configuration=Debug /p:DeployExtension=false. Run all six README suites after runtime changes; documentation-only exception applies. Commit only explicitly approved files after manual review. Message via [IO.File]::WriteAllText with UTF8Encoding($false), git commit -F, trailer Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>. No push without approval.