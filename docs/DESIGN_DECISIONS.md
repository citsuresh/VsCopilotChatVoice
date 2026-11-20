# Design decisions

Append-only dated log. Supersede a decision with a new entry rather than rewriting history.

## 2026-11-20 - Independent repository and process boundary

- **Decision:** Keep the VSIX, bundled reader/toolkit and eventual companion skill in this
  repository. Keep speech and reader execution outside `devenv.exe`; VoiceType remains a
  separate optional microphone backend.
- **Rationale:** Read-aloud must work without installing a transcription engine, while IDE
  integration stays small and speech lifetime remains outside the IDE.
- **Alternative:** Extend the VoiceType solution or copy its engine/models; rejected in favor
  of an independently installable product. No engine/model or external extension source copied.

## 2026-11-20 - Preserve the audited reader during migration

- **Decision:** Migrate the reader and executable regression harness without behavioral
  changes; retain the prototype namespace, labels, palette and existing preference location.
- **Rationale:** Preserve the tested navigation, highlighting, ownership and restart behavior
  before introducing production integration and theming.
- **Alternative:** Rebrand/refactor during the scaffold; deferred to keep migration reviewable.

## 2026-11-20 - Reading mode is immediately persistent

- **Decision:** Save Concise/Detailed selection immediately with atomic replacement and
  restore it before auto-play. Missing preference defaults to Detailed. Missing Concise
  content displays Detailed with a warning without overwriting a saved Concise choice.
- **Rationale:** The next reply/process restart must use the chosen mode. Switching the
  current reply maps by section ID and pauses rather than unexpectedly restarting speech.
  Preference failures remain visible; tests inject isolated paths.
- **Alternative:** Save only on exit or silently replace unavailable Concise; rejected because
  crashes or incomplete reply variants would lose intent.

## 2026-11-20 - Explicit Exp-only installation and load verification

- **Decision:** Leave build-time deployment disabled. Use the official installer with
  `/rootSuffix:Exp`, wait for the post-install `/RootSuffix Exp /UpdateConfiguration`,
  then verify the package's initialization message in a fresh launch activity log.
- **Rationale:** SDK deployment copied files but failed enablement with VSSDK1031. The
  installer plus post-install refresh succeeded and actual package initialization was observed.
  File presence, registry probes or installer exit status alone do not prove package loading.
- **Alternative:** Continue relying on SDK copying, manually patch registration or reset the
  experimental hive; not used. CopilotUsageMeter supplied a conceptual deployment precedent.
  No normal-instance installation until Exp testing passes and the user explicitly approves.

## 2026-11-20 - Approved toolbar and reader lifecycle (not implemented)

- **Decision:** Target `Mic | Speaker toggle | Settings | Customize AI` below the chat input.
  Speaker remembers automatic read-aloud: on shows/restores and allows new-reply speech;
  off hides, stops speech and suppresses automatic opening without discarding the latest reply.
  Minimize and X hide temporarily, retaining reply/position and Speaker state.
- **Rationale:** Distinguish disabling automatic speech from temporarily dismissing the reader.
  Settings must synchronize the toggle and reading mode and offer isolated voice/rate/volume,
  Preview and Reset without changing Windows settings.
- **Alternatives:** A separate automatic-opening button or X terminating the production
  reader; not selected. The current prototype still closes on X; theme/bridge/settings are pending.

## 2026-11-20 - Explicit companion delivery, not an assumed chat event

- **Decision:** Plan a local VS-instance-specific command bridge/toolkit and explicitly
  installed companion skill. Do not describe a skill as an automatic response subscription.
- **Rationale:** Toolbar insertion uses unsupported internal WPF surfaces unless a supported
  API is established; automatic response capture likewise needs a verified mechanism.
- **Alternative:** Assume a VSIX installs a skill or exposes a public Copilot response hook;
  unsupported and not implemented.