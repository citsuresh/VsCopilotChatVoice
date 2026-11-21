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

All three bounded proofs are complete and pushed through `64451c8` (0.3.2). Current continuation
is the approved Phase 1 metadata foundation (0.4.0), not microphone or reader migration. PoC 1's
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
Local commit is explicitly approved; next is push approval or a separately reviewable Phase 2 reader migration increment;
do not skip directly to microphone capture or reader/delivery integration.

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
  PoC 2 (0.3.0 preview) proved floating window, external worker speech, progress/cancel and highlight in Exp;
  still to prove: docking/pinning, keyboard focus without activation stealing, live theme change, two instances,
  and migration of the real reader features (navigation, Concise/Detailed, pause/resume).
- [ ] Phase 3: global/solution/window settings, inheritance, supported speech controls and
  accessible theme/font/color overrides, including distinct voices for same-solution instances.
- [ ] Phase 4: external VoiceType-derived capture/transcription/pill, model discovery and
  atomic recording ownership. Engine/models are external; no model downloads.
- [ ] Phase 5: mouse/optional Ctrl modes, cancellation and safe originating-chat insert/send.
- [ ] Phase 6: verified reply delivery, remembered Speaker, queue policies and captured history.
  Companion policy is already authored/installed/committed in the separate skills repository;
  production protocol coordination remains separate and must not invent an automatic hook.
- [ ] Phase 7: optional precise chat-scroll experiment and final hardening. Drop scrolling if
  unreliable; it must not block playback. Complete the toolbar lifecycle/theme/DPI/accessibility
  matrix left pending after the user-approved 0.2.3 appearance.

## Release gates / later

- [ ] Verify a mechanism before offering automatic Copilot-response capture; explicit prepared
  delivery remains distinct from automatic capture.
- [ ] Exercise multiple VS instances, chat recreation, bridge failures, reader lifetime and backend absence.
- [ ] Complete Exp behavior/regression checks, installation/update/uninstall documentation and packaging.
- [ ] Install/test in normal VS only after Exp passes and the user explicitly approves.