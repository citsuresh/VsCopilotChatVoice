# Roadmap

Edited deliberately when priorities change; not automatically overwritten each session.
All capabilities below are planned unless checked. Implement and verify in Exp before any
explicitly approved normal-instance installation.

## Completed foundation

- [x] Independent three-project solution, reader bundling, executable regression harness and ignore rules.
- [x] Preserve reader navigation/highlighting and remembered Concise/Detailed selection.
- [x] Build/package verification and actual VS Experimental package initialization.

## Near term (priority order)

- [ ] Add native-themed Mic, Speaker toggle and Settings immediately left of Customize AI.
  Scope internal WPF discovery, handle chat close/reopen/new sessions without duplicate controls,
  detach/dispose cleanly and explicitly indicate unavailable backend functionality.
- [ ] Add a VS-instance-specific local bridge/toolkit and persistent explicit reply/progress delivery.
  Keep speech/reader execution outside the IDE and retain the latest reply while automatic mode is off.
- [ ] Implement remembered Speaker on/off, production show/restore, temporary Minimize/X hide,
  off-stops-speech behavior and shutdown cleanup.
- [ ] Synchronize owning VS theme/live theme changes; check high contrast, keyboard access and DPI.
- [ ] Add settings for supported voice, rate, volume, Preview/Reset, automatic read-aloud and
  saved reading mode; synchronize settings and toolbar without changing Windows-wide settings.
- [ ] Discover/launch optional VoiceType and direct dictation into the intended chat.
- [ ] Implement the explicitly installed `vs-copilot-chat-voice` companion skill and document toolkit usage.

## Release gates / later

- [ ] Verify a mechanism before offering automatic Copilot-response capture; explicit prepared
  delivery remains distinct from automatic capture.
- [ ] Exercise multiple VS instances, chat recreation, bridge failures, reader lifetime and backend absence.
- [ ] Complete Exp behavior/regression checks, installation/update/uninstall documentation and packaging.
- [ ] Install/test in normal VS only after Exp passes and the user explicitly approves.