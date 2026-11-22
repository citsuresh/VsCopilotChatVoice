# Feature Checklist

**Progress:** `[##########----------] 52%` — 29 of 56 checklist items complete.

User-curated implementation and acceptance checklist for Phases 1-7. Checked items require repository evidence;
manual/official or intentionally deferred items remain unchecked until their stated gate is met. Update the progress
bar and fraction whenever an item changes state.

## Phase 1 - Metadata foundation

- [x] Conservative native chat discovery and typed identity capture.
- [x] Current-user/session/peer/lifetime fencing and graceful shutdown.
- [x] Read-only metadata contract with explicit unsupported capability reporting.
- [x] Actual-DLL regression coverage and official isolated Exp verification.

## Phase 2 - Native reader and external speech

- [x] Shared speech-free reader model/navigation/preference extraction.
- [x] External speech worker with pause/resume/seek/cancel lifecycle.
- [x] Native VS Tool Window presentation with float-first VS-managed layout.
- [x] Prepared paired Detailed/Concise fixture and paused version switching.
- [x] Reader keyboard, focus, Follow, frame-lifecycle and cleanup regression coverage.
- [x] Exp checks for floating/docking, keyboard navigation, theme/DPI and two-instance behavior.
- [ ] Full native/live-reply acceptance; real reply reopen and live Concise/Detailed acceptance are deferred to Phase 6.

## Phase 3 - Settings

- [x] Scoped storage model: global, solution and temporary VS-window scopes.
- [x] Precedence and inheritance: window > solution > global > built-in.
- [x] Clear inherited/overridden state and global restore defaults.
- [x] Validation, explicit persistence errors and isolated preference-path tests.
- [x] Cross-instance store invalidation/update behavior.
- [x] Concise/Detailed remembered preference preservation and scoped override consumption.
- [x] Available voice discovery and exact selected-voice validation.
- [x] Rate and volume controls.
- [x] External Preview and Stop; no speech synthesis in devenv.exe.
- [x] No silent fallback when a selected voice is unavailable.
- [x] Audio policy: Off, configurable ducking and suppression.
- [x] Worker/VS audio-session exclusion and session restoration after completion, cancellation or failure.
- [x] Core Audio HRESULT validation and partial-mutation rollback.
- [x] Theme-aware reader presentation, theme/font overrides and High Contrast precedence.
- [x] Settings button wiring; Mic and Speaker remain disabled.
- [x] Actual-DLL regression routes and no-deploy MSBuild verification.
- [ ] Official isolated Exp installation and package-load verification for 0.5.7.
- [ ] Manual audible Preview and selected voice/rate/volume acceptance.
- [ ] Manual duck/suppress and prior-volume restoration acceptance.
- [ ] Manual theme, DPI, High Contrast, focus and accessibility acceptance.
- [ ] Final local commit review and Phase 3 commit.

## Phase 4a - External capture probe

- [ ] External net48 capture project; no microphone capture in devenv.exe.
- [ ] Synthetic capture lifecycle and bounded cleanup probe.
- [ ] WAV writing plus PCM RMS/peak statistics.
- [ ] Hardware probe gated by `VSCOPILOTCHATVOICE_CAPTURE_HW=1`.
- [ ] Maximum three-second hardware capture; print only duration, bytes and levels.
- [ ] Delete temporary recording after the probe.
- [ ] No model downloads, bundled Whisper binaries or bundled models.
- [ ] Defer transcription until a user-provided engine and model path are explicitly available.

## Phase 5 - Input gestures and safe insertion

- [x] Pure hold/toggle/Right-Ctrl gesture state model and cancellation rules.
- [x] Current-user recording ownership primitive and contention/abandonment checks.
- [ ] Production microphone capture and transcription wiring.
- [ ] Safe originating-chat draft insertion/send policy with authoritative native contract or approved fallback.
- [ ] No overwrite of changed drafts; explicit recovery for unreachable originating chats.

## Phase 6 - Reply delivery and playback policy

- [x] Speech-free captured reply catalog with stable identity, ordering, deduplication and capacity outcomes.
- [ ] Verified live reply delivery and automatic reader reopen behavior.
- [ ] Speaker remembered-state integration and automatic playback policy.
- [ ] Queue policies: finish current then latest, switch immediately or read all in order.
- [ ] Real-chat Concise/Detailed acceptance and reply-version correlation.

## Phase 7 - Hardening and release acceptance

- [ ] Full cross-phase regression sweep after all production behavior is wired.
- [ ] Official Exp installation/runtime validation for the final candidate.
- [ ] Accessibility, theme, DPI, focus, cancellation and multi-instance acceptance.
- [ ] Error/logging review with no silent fallback or success-shaped failure.
- [ ] Final documentation, manual review, local commit and release artifact review.
- [ ] Main-instance installation and push only after explicit user approval.
