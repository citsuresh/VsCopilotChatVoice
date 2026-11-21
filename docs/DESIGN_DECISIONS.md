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

## 2026-11-20 - Disabled, conservative toolbar shell before backend activation

- **Decision:** Add Mic, an unchecked Speaker toggle and Settings immediately before the
  internal Copilot tool picker only when bounded discovery finds a unique visible picker and
  a horizontal StackPanel through single-child wrappers. Keep all three disabled with explicit
  unavailable-state accessibility/tooltips and dynamic VS resource references. Dispose only
  owned controls/timer subscriptions; rediscover after visibility, layout or lifetime changes.
- **Rationale:** Establish the approved in-chat location without implying working dictation,
  saved automatic state or settings. An unsupported layout must be reported, not restructured.
- **Alternatives:** Main IDE toolbar, arbitrary Grid surgery, clickable no-op controls or a
  second Speaker preference; not used. Keep speech and settings work in later phases.
- **Validation:** Reuse the existing executable harness with optional reflection-based toolbar
  checks against the actual VSIX assembly; no new test framework/project or reader behavior
  change. Synthetic WPF results do not establish native Copilot runtime/theme compatibility.

## 2026-11-20 - Grouped native toolbar icons, not dialog buttons

- **Decision:** At the user's request, use compact flat toolbar button/toggle styles with a
  single subtle, rounded, VS-resource-bound border around Mic, Speaker and Settings. Set
  explicit zero minimum sizes so theme defaults cannot expand individual 28-DIP icon buttons.
- **Rationale:** Live Exp inspection and the screenshot showed dialog-style minimums expanding
  Mic/Settings to 112 physical pixels while Speaker occupied 42 at the observed DPI. Grouping
  communicates ownership without three separate dialog-button rectangles.
- **Alternative:** Keep dialog styles or add a new custom control template; rejected in favor
  of existing WPF toolbar resources, dynamic VS border color and system disabled-text color.

## 2026-11-20 - Companion policy source belongs in the skills repository

- **Decision:** Maintain `vs-copilot-chat-voice` in Visual-Studio-Copilot-Skills, separate from
  the application. This supersedes the initial repository-boundary entry's co-location of
  the eventual companion skill; VSIX/reader/toolkit implementation remains in this repository.
- **Rationale:** Reuse the skills repository's discovery, generated index, reviewed update and
  explicit installation workflow without treating application installation as skill activation.
- **Alternative:** Bundle/copy skill source or speech helpers into the VSIX; not used.

## 2026-11-20 - Tighten the group border without shrinking glyphs

- **Decision:** Reduce the group outer height from its original 34-DIP nominal size to an
  explicit 24 DIPs, remove vertical group padding, and use 28-by-22-DIP buttons while keeping
  14-DIP glyphs, horizontal spacing, border weight and corner radius unchanged.
- **Rationale:** The user approved the flat icons but observed that their shared border was
  taller than neighboring control borders. Explicit outer height also prevents DPI-rounded
  border thickness from growing an auto-sized group beyond the measured target.
- **Alternative:** Shrink icon glyphs or merely remove vertical padding; neither addresses
  the requested border alignment while preserving the approved icon appearance as directly.

## 2026-11-20 - Native reader presentation with external audio execution (planned)

- **Decision:** Replace the planned standalone production overlay with a native VS Tool Window,
  floating on first use and thereafter following VS-managed docking/pinning/layout. Keep speech,
  microphone capture and transcription outside devenv.exe. Selectively reuse VoiceType capture,
  transcription and pill code in an extension-owned host; manually provision external Whisper
  engine/models or Browse existing paths, with no model downloads through extension or skill.
- **Rationale:** Native tool-window behavior meets the user's positioning/docking request without
  reimplementing the VS window manager. Audio remains isolated; mic functionality does not require
  the standalone VoiceType app or companion skill. Preserve existing reader CLI/tests during reuse.
- **Supersedes:** The initial entirely external production-reader UI and optional VoiceType-app
  discovery direction, not the current prototype implementation. See IMPLEMENTATION_PLAN.md;
  its decomposition must be confirmed before production code changes.

## 2026-11-20 - Explicit recording gestures, cancellation and recovery (planned)

- **Decision:** Hold-to-talk defaults to release-on-Mic auto-send eligibility and release-outside
  insert-only review; toggle mode follows the auto-send setting. Optional Chat-scoped solitary
  Right Ctrl requests capture immediately, invalidating tentative Ctrl-combination gestures.
  Esc cancels recording/transcription and invalidates late callbacks. Never overwrite changed
  drafts, auto-send existing combined drafts, or route to arbitrary foreground controls.
- **Rationale:** Preserve fast dictation with an intentional review gesture and safe shortcut
  handling. Single-owner capture across instances prevents conflicting depressed mic states.
- **Recovery:** Completed text for an unreachable originating chat is copied to the clipboard
  with truthful pill confirmation; failed copying retains explicit recovery. Incomplete/cancelled
  text never enters the draft. Error/fallback pills last at least 10 seconds, acknowledgements
  3 seconds, active status until resolved; persistent setup failures retain accessible badges.

## 2026-11-20 - Scoped personal settings and separate reply navigation (planned)

- **Decision:** Global defaults < solution overrides < temporary VS-window overrides, selected
  at the top of Settings. Durable personal settings are app-local rather than repo files;
  same-solution instances can use distinct temporary voices. Native VS persists layout separately.
- **Rationale:** Support simultaneous agents without forcing team-shared preferences or a complex
  profile system. Inheritance is explicit; updates must not overwrite other scopes or trigger audio.
- **Reader:** Capture replies separately with paired versions, labelled reply navigation distinct
  from sentence/paragraph arrows, and configurable new-reply queue handling. Theme-based colors
  allow personal overrides; High Contrast wins. Exact chat-scroll matching is optional/experimental
  and will be dropped if unreliable. Unsettled working defaults are identified in the draft plan.

## 2026-11-21 - Bounded read-only chat contract discovery before mutation

- **Decision:** Keep the Debug/Exp/launch-gated probe read-only and follow only observed binding
  paths. Resolve public properties from the nearest declaring type, preserve private-getter
  refusal, and report reflection ambiguity per connection without terminating the listener.
- **Rationale:** Live Copilot bindings expose typed session identity and observable chat events;
  accessibility titles/runtime IDs do not prove identity. The inherited-property regression
  first failed on the unfixed DLL, then passed alongside actual IPC recovery after the fix.
- **Alternatives:** Do not guess IDs, stringify arbitrary objects, read unrelated content, or
  enable insertion/send before identity, draft and state contracts are verified. Unsupported
  contracts remain explicit capability gaps rather than success-shaped fallbacks.

## 2026-11-21 - Passive native state observations before command guards

- **Decision:** Count Prompt notifications without reading draft content; associate them with a
  fresh observation epoch and one exact native root. Subscribe to observed trust/responding
  streams, leaving unavailable values null. Replacement, hiding, unloading or ended feeds
  invalidate observations. Keep IPC inspect-only until native behavior and atomic guards pass.
- **Rationale:** Text equality cannot detect an ABA edit, and absent readiness cannot safely
  imply trusted/idle. Subscription lifetime and late callbacks need separate regression checks.
- **Alternatives:** Foreground paste, guessed readiness, and treating observation as permission
  to send remain rejected. Isolate VS-only cleanup scheduling from standalone IPC test execution.
- **Native limitation:** Public installed metadata shows the composer does not notify property
  changes. Expose missing input-replacement notifications explicitly; do not treat synthetic
  notification fixtures as proof of native coverage or enable mutation on that assumption.

## 2026-11-21 - Terminal observation cleanup independent of client activity

- **Decision:** Terminal invalidation automatically queues UI teardown, deferred until constructor
  subscriptions have returned their handles. Preserve primitive status/revision/capability values
  but clear native/model references and subscriptions. Owner disposal shares the same cleanup.
- **Rationale:** Independent review and a failing regression showed chat closure otherwise retained
  all five subscriptions until another inspect. Background terminal events and synchronous
  subscription completion also require cleanup without reviving an invalid epoch.
- **Alternative:** Do not rely on future polling or owner shutdown to release a closed conversation.

## 2026-11-21 - Version-specific immutable-input evidence, not notification inference

- **Decision:** Treat the installed SDK's setter-free, nonvirtual PromptInput getter and direct
  constructor-only field metadata as evidence for a future fail-closed stable-input check.
  Keep reporting missing composer notifications; no runtime guard is enabled by this research.
- **Rationale:** Getter-only alone does not prove an immutable binding. The exact getter/field
  shape is verified without reading private values; changed SDK shapes must be unavailable
  rather than assumed equivalent. Root/composer replacement and draft/state guards still apply.
- **Alternatives:** Do not assume native lifetime from synthetic fixtures, blindly construct a
  backend send request, infer completed-reply identity from response Content, or silently skip
  metadata dependency failures. Use exact public-type reads instead of whole-assembly inventory.

## 2026-11-21 - Separate guarded draft insertion from native send

Superseded before deployment by the fail-closed decision below; these snapshot guards were not atomic.

- **Decision:** Add an additional process-local Debug/Exp gate for native UI-thread draft insertion.
  Revalidate exact session/client/epoch/revision/input, readiness and supported immutable input
  lifetime before the setter; preserve existing drafts with an append and verify read-back.
  Do not expose send while its acceptance and reply correlation remain unverified.
- **Rationale:** Native insertion can be tested independently without changing the approved
  disabled toolbar or trusting foreground input. A setter/transport failure may occur after
  mutation; store uncertain outcomes and deduplicate the complete request rather than retry.
- **Bounds:** The 128-entry ledger never silently evicts identities. Report capacity/refusal
  explicitly; keep original request parameters for reconciliation. Responses/logs contain no draft.
- **Alternatives:** No foreground paste, draft overwrite, combined-draft auto-send, blind retries,
  or success-shaped acknowledgement after failed read-back. Keep normal/ungated VS unchanged.

## 2026-11-21 - Refuse native insertion without authoritative atomic editing

- **Decision:** Remove the native draft read/write transaction and its callback surface. Every
  valid insertion request returns `Rejected / NativeAtomicInsertionUnsupported`, with no mutation.
  Keep bounded framing, explicit capability reporting and non-evicting request reconciliation.
- **Evidence:** Independent review reproduced trust/busy changes and concurrent draft replacement
  after the last copied snapshot. Neither constructor-only input metadata nor observer/ledger locks
  fence native producers; post-write uncertainty cannot restore an overwritten draft.
- **Transport:** Start one deadline before the first asynchronous write and include response reads.
  Dispose the actual pipe on expiry, preserve the complete request and never retry automatically.
  A real non-reading-peer test verifies backpressure and elapsed disposal, not just parsing.
- **Boundary:** Test safe refusal only, save the verified increment and stop as requested. Native
  insertion remains blocked; no send, reply delivery, microphone or production migration is enabled.

## 2026-11-21 - Guarded external UIA insertion as the PoC 1 fallback

- **Decision:** The user chose guarded UIA typing with read-back while native atomic editing stays
  unproven. `scripts\agent\Insert-ChatDraftUia.ps1` reuses the AgentDebugToolkit `type --paste --verify`
  primitive (clipboard snapshot/restore, history exclusion, read-back) that the agent-orchestrator's
  Send script already relies on, but adds the draft guard that script lacks: it refuses unless the
  Send button exists and is disabled (empty draft, not busy), refuses non-Exp targets and trust/folder
  banners, never clears, never clicks Send and reports a mismatch as `DraftChanged`.
- **Rejected:** Reusing `Send-CopilotPrompt.ps1` unchanged (it clears the input, destroying a draft, and
  sends); backend `SendAsync` (bypasses the draft and changes product behavior).
- **Limit:** Not atomic. The Send button is re-checked right before typing, but text typed after that is
  detected only afterwards: a clipboard paste keeps it and adds ours (reported, never cleared), while a
  writable ValuePattern would replace the whole draft (method reported). It also needs foreground focus and the clipboard. It stays an external experiment and
  is not a production insertion path until a live Exp proof and user approval exist.
- **Live result and reversal (same day):** In a signed-in Exp chat that reported "an operation is
  blocking user input", the guards passed but paste read-back never landed, Ctrl chords and End were
  dropped and focus moved to Git Changes. The Send button proved unreliable as an emptiness signal, so
  TextPattern text is the read-back signal. A first `-Append` design (foreground switch, SetFocus,
  Ctrl+End) was withdrawn after two runs failed there, and `-Append` became passive (user prepares
  focus and caret). A probe showed the toolkit's Ctrl chords work in a normal window, and the main VS
  chat accepts Ctrl+End, so the fault was that Exp instance. After restarting Exp, paste into an empty
  draft worked, and `-Append -PlaceCaret` (opt-in; one Ctrl+End after focusing, refusing if the input
  leaves the tree, text changes or the caret is not at the end) appended correctly to 1-, 2- and 14-line
  drafts with the end scrolled out of view. Nothing was sent. Added `-Append -Paste` for long text: it
  uses the toolkit's `paste-text` verb (current caret, no click, clipboard restored) after the same
  guards, polls the read-back for up to 5 s, and was proved live with 11,091 characters. A review also
  led to: UIA read errors map to `Failed`, text starting with a hyphen is refused (CLI option parsing)
  and plain `-Append` is passive only unless the person changes focus during the call. By user request,
  `-Append -Paste` onto a non-empty draft now prepends a line break (read-back accepts LF, CRLF or CR);
  `-NoLeadingNewline` opts out. The break is part of the pasted text, never a typed Enter, because Enter
  would send the message. Proved live on Exp. Still not
  atomic and still an external
  experiment; a native editor-buffer edit remains an unresearched alternative.
