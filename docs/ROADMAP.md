# Roadmap

Edited deliberately when priorities change; not automatically overwritten each session.
All capabilities below are planned unless checked. Implement and verify in Exp before any
explicitly approved normal-instance installation.

## Completed foundation

- [x] Independent three-project solution, reader bundling, executable regression harness and ignore rules.
- [x] Preserve reader navigation/highlighting and remembered Concise/Detailed selection.
- [x] Build/package verification and actual VS Experimental package initialization.

## Near term (priority order)

- [ ] Confirm the decomposition in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).
- [ ] Phase 1: instance/conversation contracts, local bridge/lifetime and capability probes.
- [ ] Phase 2: native floating/dockable reader presentation with external speech, preserving
  prototype regression behavior. VS manages layout; audio work stays outside the IDE.
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