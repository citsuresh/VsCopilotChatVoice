# Roadmap

Edited deliberately when priorities change; not automatically overwritten each session.
All capabilities below are planned unless checked. Implement and verify in Exp before any
explicitly approved normal-instance installation.

## Completed foundation

- [x] Independent three-project solution, reader bundling, executable regression harness and ignore rules.
- [x] Preserve reader navigation/highlighting and remembered Concise/Detailed selection.
- [x] Build/package verification and actual VS Experimental package initialization.
- [x] Bounded PoC 1 guarded external insertion/read-back and completed-reply observation, with parked cases.
- [x] Bounded PoC 2 native floating reader/external speech, accepted with documented UI caveats.
- [x] Bounded PoC 3 fake-capture Ctrl lifecycle and user-local ownership, accepted with release-side adaptation.

## Near term (priority order)

All three bounded proofs are complete and pushed through `64451c8` (0.3.2). Verified Phase 1
metadata foundation (0.4.0) is committed/pushed as `9fca48c`; current continuation is Phase 2
reader-migration preparation, not microphone or reply delivery. PoC 1's
external guarded UIA fallback/observer is accepted with parked edge cases; native
atomic insertion and a production reply feed remain unsupported. PoC 2 is accepted with caveats.
PoC 3's detector, user-local mutex and log-only native observer pass 209 checks and are installed
in Exp; normal Ctrl+V/A/C and Left Ctrl hold/repeat/focus/mouse cancellation passed live. Native
shortcut key-down suppression exposed a Toggle guard gap; 0.3.2 adds release-side and held-input
guards, proven against the old/new actual DLLs. Patched Toggle/shortcut/Escape-release passed
live; two Exp instances passed focus-cancelled ownership handoff and later reacquisition.
Physical Right Ctrl is unavailable on the test keyboard; early native shortcut/Escape down
is not established.
The authorized bounded proof is complete. Keep Ctrl gated pending actual capture integration
and broader native checks; defer/drop it if decisive edges are suppressed, without blocking
mouse delivery or reader playback. No next production phase is authorized by this result.
The approved Phase 1 breakdown in IMPLEMENTATION_PLAN section 6 covers typed identities,
originating-target lifetime, metadata-only IPC and native acceptance. Foundation code and all seven
regression routes pass, including 136 full / 131 pure contract assertions. The approved ambiguity
listener-recovery fix and capacity/acknowledgement negative tests passed independent audit closure.
Exp installation, distinct native instance lifetimes, pinned reconnection and background metadata
without foreground change passed. The first populated SDK chat passes identity, exact status,
retirement/reopen epoch changes, old-epoch refusal and next-client recovery. Blank/history views
without a supported live binding remain unavailable. Two distinct live SDK chats now pass own-target
status, old-conversation refusal and bidirectional cross-instance isolation; graceful task-instance
shutdown and endpoint disappearance are verified. Phase 1 metadata-only verification is complete.
The user approved its push and starting the next phase. Phase 2's source-grounded breakdown in
IMPLEMENTATION_PLAN section 6 is confirmed before runtime changes: shared reader
types, external playback/lifetime, native view/frame behavior and regression/Exp acceptance.
Do not skip directly to microphone capture, Settings or reply delivery.

User decision 2026-11-21: use guarded UIA typing with read-back (`Insert-ChatDraftUia.ps1`) as the
insertion fallback; positive live Exp proof is done (paste and `-Append -PlaceCaret` on fresh Exp).
Read-only completed-reply observation (`Read-ChatRepliesUia.ps1`) is live-proved on finished replies,
streaming (Send offscreen, Cancel visible), an approval item and multi-body tool turns.
Reply identity for Phase 6 is drafted as a design note in IMPLEMENTATION_PLAN.md section 6 (content-hash identity,
completion rule, duplicate handling, limits); it is design input only, not implemented in the extension.
Added `Wait-ChatReplyUia.ps1` (new-reply wait with duplicate guard); live-run on a short Exp reply
(`Completed`, 24 s) and three long Exp replies (`Completed`; waits started before the send delivered the right
final reply). One transient `Complete` observation of a short partial body was seen mid-stream; `-SettlePolls`
(default 2) was added to harden it. An approval pause mid-wait is not yet observed.
Parked (2026-11-21, user decision; revisit only on request): reply observer cases not yet observed - cancelled
reply, Deny outcome, second chat, very long transcript.

- [x] Confirm the decomposition in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) (2026-11-21).
- [x] Phase 1: metadata-only instance/conversation contracts, local bridge/lifetime and capability probes; live SDK bindings verified, blank/history-only surfaces remain unavailable.
- [ ] Phase 2: native floating/dockable reader presentation with external speech, preserving
  prototype regression behavior. VS manages layout; audio work stays outside the IDE.
	Detailed implementation breakdown approved 2026-11-21; part 1 shared-reader extraction verified:
  model 379 plus seven preserved routes, independent audit closure and Exp 0.4.1 package/load proof.
	Extraction checkpoint `da8fd78` is pushed. External transport 0.4.2 builds and passes all eight
	routes (speech 104); both approved queued-cancel/legacy-timeout fixes pass independent closure.
	Local transport checkpoint `bdb1328`/continuation approved, not push. Parent independently
  verified fixed-snapshot 0.4.2 transport/loading, actual installed-worker pause/resume and old
	preview completion/cleanup in Exp (native Cancel UI remains unproved). That checkpoint installed 0.4.2.
  The bounded 0.4.3 single-reply owner and typed real-worker adapter were implemented unwired;
	actual-DLL playback 220 and all eight preserved routes pass (speech110), with VS MSBuild/DeployExtension=false
  and only the existing toolbar warning. Independent review reproduced a completion/state race;
	the approved explicit terminal-outcome correction and independent actual-host/controller closure pass.
	Parent frozen 0.4.3 loading/transport proof now passes: official install/refresh, matching binaries,
  installed owner220/Reader1,868, raw worker pause/resume/shutdown, pinned metadata, zero package errors
	and graceful task-only cleanup. Subsequent frozen 0.4.5 install/hash and installed native301/playback220/
  Reader1,868 checks passed, as did bounded floating X/worker cleanup/reopen without autoplay. Task Exp
  and workers exited gracefully; protected main stayed intact. Installed Exp is 0.4.5, not full Phase 2 acceptance.
	Source 0.4.8 implements the gated paired-fixture native view/preferences/frame increment with 560
  synthetic actual-DLL presentation/gate/resource/keyboard/lifetime checks. Old ungated/standalone
	behavior is preserved; source 0.4.8 is uninstalled (0.4.6/0.4.7 were never installed). The approved notification-reentrancy correction
  first reproduced synchronous highlighted-Run close reviving playback against the original 0.4.4 DLL;
  prompt/version/operation and tightly coupled view continuation fences now pass synthetic close/Stop/
	Dispose/version/seek cases while preserving playback220/speech110; independent 0.4.5 closure passed.
  Native status/warning Names were verified generic, and actual WPF peer Select checked a version without
  applying/saving it. 0.4.6 binds displayed accessible text and supports non-click Checked plus once-only
	native Click/fallback saving. Independent 0.4.6 closure reproduced genuine peer selection dropped by
  refresh-wide suppression during highlighted-Run Follow (checked=True/modelConcise=False/Speaking/cancels=0).
  0.4.7 uses model-match synchronization filtering, native-toggle-only Click deduplication and early retirement
  of shutdown intent. Actual peer/Click nested choices, newest preferences, lifetime and late/model-sync
	negatives pass synthetically; the 0.4.7 Follow case closed independently. A newer peer during native Checked
  still lost to older Click, in both directions. 0.4.8 replaces equality/toggle-wide inference with explicit
  per-event origins and captured request revisions, preserving matching-mode/round-trip saves and rejecting
  superseded Click/same-version continuations. Owned Play/radio Esc refusal was reproduced and corrected;
  native560 covers nested peers/native Clicks, setter callbacks, same-version save failures, disposal and all
  scoped-control Esc/negative paths. Owner/transport/standalone are unchanged. Next: independent narrow closure,
	blocked first on repeatable final native verification: isolated560 passed, but both final serial runs failed
  saved-choice/radio assertions (one visible File.Replace save error; last failure cause unconfirmed). Stop
  source0.4.8 before installation; do not claim all-ten/source/native acceptance or add speculative retries.
  live read-only Name proof and
  full native Exp acceptance, including actual frame subscription/close retention and layout/focus/
  theme/DPI/Follow/two-instance cleanup. No full Phase 2 native acceptance is claimed.
	Native version selection is parked: toolkit refusal occurred before action (missing selection container),
  and a separate physical radio click was blocked by foreground interference; neither proves a native mutation.
  PoC 2 (0.3.0 preview) proved floating window, external worker speech, progress/cancel and highlight in Exp.
  The 2026-11-21 manual Exp check confirmed docking/pinning, keyboard navigation, live theme change, DPI and
  two-instance independence; arrow keys intentionally navigate sentences/paragraphs. Automatic reopen after a
  hidden or unpinned reader receives a new live reply, and Concise/Detailed acceptance against real replies,
  are deferred to Phase 6 because they require the reply-delivery path.
- [ ] Phase 3: global/solution/window settings, inheritance, supported speech controls and
  accessible theme/font/color overrides, including distinct voices for same-solution instances.
  The 0.5.7 Settings increment now wires the Settings button, scoped speech/audio/appearance persistence,
  external Preview, worker-aware audio restoration and native reader runtime settings. Automated actual-DLL
  settings271/toolbar62/native560 pass; Exp, audible, audio-restoration, theme/DPI/accessibility and final
  local commit gates remain.
  The 0.5.7 increment completes the automated Settings surface: scoped speech, Concise/Detailed, audio
  policy and appearance values; external Preview/Stop; worker-aware session exclusion/restoration; and native
  reader runtime application. Actual-DLL settings271, toolbar62, native560, playback220, integration320 and
  the remaining documented routes pass after the final compatibility and audit fixes. Mic and Speaker remain
  disabled. Remaining Phase3 gates are official isolated Exp installation, audible Preview, live duck/suppress
  restoration, theme/DPI/High Contrast/accessibility checks and local commit review. Phase4 remains paused
  until those gates are completed; real-chat reopen and live-reply Concise/Detailed acceptance remain Phase6.
- [ ] Phase 4: external VoiceType-derived capture/transcription/pill, model discovery and
  atomic recording ownership. Engine/models are external; no model downloads.
- [ ] Phase 5: mouse/optional Ctrl modes, cancellation and safe originating-chat insert/send.
- [ ] Phase 6: verified reply delivery, remembered Speaker, queue policies and captured history.
  Independent0.5.3 speech-free immutable in-memory captured-history catalog passes651 and bounded
  independent review, with legacy parity preserved. It enables no feed/Speaker/UI/audio, fabricates no Concise and
  does not bypass parked native/storage/source-permission/transport gates.
  Companion policy is already authored/installed/committed in the separate skills repository;
  production protocol coordination remains separate and must not invent an automatic hook.
- [ ] Phase 7: optional precise chat-scroll experiment and final hardening. Drop scrolling if
  unreliable; it must not block playback. Complete the toolbar lifecycle/theme/DPI/accessibility
  matrix left pending after the user-approved 0.2.3 appearance.

## Release gates / later

- [ ] Combined cross-phase review after all implementation phases (requested 2026-11-21), covering
  integration, lifecycle/ownership, compatibility, errors and regression/runtime evidence; retain per-phase checks.
- [ ] Verify a mechanism before offering automatic Copilot-response capture; explicit prepared
  delivery remains distinct from automatic capture.
- [ ] Exercise multiple VS instances, chat recreation, bridge failures, reader lifetime and backend absence.
- [ ] Complete Exp behavior/regression checks, installation/update/uninstall documentation and packaging.
- [ ] Install/test in normal VS only after Exp passes and the user explicitly approves.