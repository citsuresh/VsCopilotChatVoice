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

## 2026-11-21 - PoC 2: native tool window with an external speech worker over a named pipe

- **Decision:** The reader presentation is a native VS tool window (`ToolWindowPane`, floating-first). Speech
  runs in the bundled Reader exe started as `--speech-worker <pipe>`; the package owns a current-user-only
  `NamedPipeServerStream` and exchanges JSON lines (`speak {gen,text}`, `cancel {gen}`, `shutdown` out;
  `progress {gen,offset}`, `completed`, `cancelled`, `error` back). The window highlights words from the
  worker's offsets, so devenv never synthesizes speech or opens an audio device.
- **Safety rules:** one active generation; the host drops any message not matching it, and `Cancel()`
  deactivates immediately so late progress cannot revive speech; the control re-checks its own generation on the
  UI thread after the marshal. EOF, an invalid or oversize line (262,144 bytes), a host-bound message type from the
  worker, an unrequested `cancelled` or a pipe client PID different from the started worker fault the host and tear
  it down. Writes are bounded (5 s). `Dispose` does the shutdown handshake and kill on a background task so the UI
  thread never waits.
- **Rejected:** In-process `SpeechSynthesizer` (the IDE would own audio and its failures); stdout/stdin pipes
  (no PID/identity check, mixes with diagnostics); reusing the full reader window as the presentation (not
  dockable, own theme); a lazily auto-started worker per reply (kept lazy per first Speak, reused after).
- **Gotchas found:** `AutomationProperties.Name` on the status TextBlock hides its displayed text from UIA, so it
  was removed and the status is read as the control's text. An intermittent failure (1 in 7 runs) of the existing
  integration check "Selecting Detailed did not replace Concise on disk" (preference-save test, unrelated to this
  change) appeared once; two later runs passed. Not investigated here.
- **Result:** Proceed with caveats; see IMPLEMENTATION_PLAN.md section 6 for what was and was not tested live.

## 2026-11-21 - PoC 3: pure gestures, explicit fake lifecycle and thread-owned arbitration

- **Decision:** Keep Ctrl decisions in a pure, serialized state machine with generation-tagged backend
  acknowledgements. Request on down; defer hold finalization until ready if release wins startup.
  A first toggle tap is tentative until up; later Ctrl combinations invalidate only a tentative stop
  tap, not the established toggle. Esc/focus cancel all phases and invalidate late results/releases.
- **Boundary:** This proof has no microphone, recording, transcription or delivery. The VSIX observer
  uses Debug + exact Exp suffix + launch opt-in; preview handlers observe already-handled events
  without changing `Handled`. It shares conservative toolbar picker discovery and accepts only
  active-window Chat keyboard focus. Log only counters and decision/state/generation, never key
  contents, draft/reply text or audio. Launch Left/Toggle flags are test knobs, not saved settings.
- **Ownership:** Use a current-user ACL-restricted named `Local\` mutex with `WaitOne(0)`, not a PID
  file or live-owner takeover. Report abandonment separately as a crashed previous owner. Windows
  mutex ownership is thread-affine, so fake acquire/stop/release/dispose stay on the UI thread;
  future external capture must marshal stop acknowledgement to that owning thread. Prevent same-
  thread recursive acquisition from creating two local owners. Hold until capture-stop acknowledgement,
  not until a potentially long transcription finishes; serialize finalization in the state machine.
- **Alternatives rejected:** A timing threshold before requesting capture, global keyboard hooks,
  arbitrary foreground input, silent abandoned-owner recovery and treating synthetic WPF events as
  physical-key proof. An intentionally queued dispatcher callback models fake backend readiness,
  not an artificial gesture activation delay; its narrow analyzer suppression documents that distinction.
- **Evidence/decision:** 165 full gesture assertions include actual-DLL fixtures and real two-process
  contention/crash recovery. Installed observer attached in Exp and normal Ctrl+V/A/C preserved exact
  scratch text; reader Speak/Cancel remained usable. Physical Right Ctrl/repeat/Esc/focus/toggle and
  two VS instances remain open because the toolkit cannot send side-specific held modifiers.
  **Adapt:** retain the optional gate pending manual coexistence proof; mouse/reader are independent.

## 2026-11-21 - PoC 3 release-side guards for native suppressed key-down

- **Evidence:** Physical Left Ctrl Toggle starts and second solitary tap stops in Exp. Native Chat
  sometimes exposes other key-up without its key-down, including the requested shortcut/Esc exercise.
  The old detector mistook a tentative stop chord for a solitary Ctrl tap. Exact actual-DLL reflection
  reproduced 0.3.1 `Stopping / FinalizeCapture`; the same sequence on 0.3.2 stays `Listening / None`.
- **Decision:** Any other release invalidates a tentative gesture. Sample other held inputs again on
  Ctrl release to guard the opposite release ordering. Escape release cancels if its down was not
  observed. Preserve immediate capture request, normal routing, established toggle, generation guards
  and the no-hook boundary. Cancel-on-release is a fallback, not a claim of immediate native down delivery.
- **Validation:** All six suites pass; gesture 209 full / 196 pure checks include both release orders,
  startup and active toggle plus Escape release in every lifecycle stage. Official Exp install/refresh
  exited 0 with matching 0.3.2 DLL; observer reattached. Patched physical Toggle/shortcut/Esc checks
  remain pending. Keep the optional gate; do not promote to production or invent an early-down capability.

## 2026-11-21 - PoC 3 final bounded result: accept with native release-side adaptation

- **Evidence:** Patched Left Ctrl Toggle remains Listening through Ctrl+A/C (StopTapInvalidated),
  finalizes once on a later solitary tap and cancels on EscapeRelease. Two live task Exp instances
  cancel/release before the other acquires; both allow a later start. No focus-loss finalization/result
  or overlapping active owner appeared; both dedicated logs have zero package errors. Live-VS proof
  is safe focus handoff, not crash/Busy proof; real two-process tests separately prove Busy/abandonment.
- **Decision:** Accept the authorized fake-capture PoC, with an explicit release-side native adaptation.
  Keep Ctrl optional/gated, default Right Ctrl unchanged, configurable Left Ctrl used for live testing.
  Direct native shortcut/Esc down, physical Right Ctrl, microphone latency/hardware, cross-session,
  missing decisive key-up and live-VS crash recovery remain unproved. No production phase is authorized.
- **Fallback:** If a later native surface suppresses both decisive edges, defer/drop Ctrl rather than
  install global hooks or claim immediate cancellation. Mouse delivery and external reader playback
  stay independent. This supersedes the earlier pending physical/two-instance result, not its history.

## 2026-11-21 - Phase 1 approved: metadata before production delivery

- **Scope:** Start the confirmed production decomposition with contracts/lifetime/metadata IPC only.
  Native session GUID plus client ID identifies a conversation; an observation epoch and fresh instance
  GUID fence lifetimes. PIDs/HWNDs/solution paths/titles alone do not substitute for these identities.
- **Transport:** A current-user-only per-PID discovery pipe performs a fresh instance handshake;
  kernel peer PID checks bind its endpoints. GUIDs are correlation, not secrets/authentication tokens.
  Request ID/sequence replay guards are per connection, bounded to 64 requests; reconnects are fresh
  read-only queries. Mutating retry/dedup semantics must be designed when actual delivery is integrated.
- **Reuse:** Extract exact existing native capture rules and share safe-handle pipe identity/security
  and bounded framing. Preserve default speech framing behavior; metadata requests select strict UTF-8
  and a smaller bound. Do not replace passive counters with an invented native atomic-edit authority.
- **Evidence:** Reproduced framework JSON reader accepting a second trailing root; explicit root-end
  validation now rejects it. Seven routes pass, including 54 contracts assertions. Native acceptance
  and independent audit remain open; no Phase 2, microphone, delivery or main installation is implied.
- **Audit checkpoint:** Independent review found binding ambiguity escaping the new listener's
  per-request capture handler; parent reproduced permanent fault and retry timeout on the binary.
  Correction and stronger capacity/acknowledgement negative tests await user approval. No deployment
  before resolution; this evidence does not revise the accepted PoC results.

## 2026-11-21 - Phase 1 approved recovery fix; native conversation gate stays open

- **Decision:** Contain binding ambiguity narrowly at the metadata capture boundary, log it and
  return `TargetUnavailable` rather than fault the listener. Preserve explicit capacity/correlation
  refusals; do not add a broad catch or silently assume a valid target.
- **Evidence:** User-approved recovery, request 64/65 boundary and independently wrong acknowledgement
  ID/sequence tests pass. Contracts now have 131 full / 126 pure assertions; independent targeted
  audit closed all three items. Other suites passed, with an unchanged preference-save I/O failure
  passing on immediate integration rerun.
- **Native result so far:** Exp-only 0.4.0 installation/refresh exited 0 with matching DLL. Two same-
  solution instances have distinct lifetime GUIDs, pinned reconnections and a background query that
  preserved the other instance's foreground. Both package logs have zero errors. No speech or Send.
- **Acceptance boundary:** The fresh blank session currently returns no native target; its cause
  remains unconfirmed. Require populated-chat, two-chat and recreation/epoch evidence before accepting
  Phase 1. Do not substitute a title or successful instance handshake for conversation identity,
  auto-send a probe to make a target appear, or proceed to Phase 2 on synthetic tests alone.

## 2026-11-21 - Phase 1 exact native status and observed-epoch lifetime proof

- **Decision:** Add only a read-only test-executable status inspector, reusing typed exchange,
  bounded framing, kernel peer identity and pinned handshake. No production/VSIX runtime change,
  auto-send or diagnostic content channel is needed for native exact-target refusal checks.
- **Evidence:** Five isolated inspector assertions bring contracts to 136 full / 131 pure. All seven
  routes and VS MSBuild/IDE build pass; the installed VSIX DLL remains unchanged. Exp A's populated
  SDK target retains its identity/epoch across reads, refuses wrong epochs, retires on history
  navigation and acquires a new epoch on reopening the same verified session. The old epoch remains
  refused; the current one succeeds. Background populated status preserves the other instance's focus.
- **Limits:** Four deliberate UnknownTarget probes log expected InvalidDataException failures.
  Exp B's selected history view and fresh blank views expose no native target; their cause remains
  unconfirmed. Refusing A's tuple in B is not proof of a second positive binding. Keep Phase 1 open
  until distinct live conversation origin/isolation and task-instance shutdown are verified.

## 2026-11-21 - Phase 1 final result: accept metadata-only live-binding foundation

- **Evidence:** Two same-solution Exp instances expose distinct live SDK session/epoch tuples under
  distinct instance lifetimes. Exact status succeeds, A's old conversation is refused after switching,
  and each instance refuses the other's tuple. Earlier history/reopen and background-focus proofs
  remain valid. Seven expected UnknownTarget failures are logged, with no other package errors.
- **Cleanup:** Both task instances accepted CloseMainWindow, completed waited exit and disappeared;
  their endpoints no longer accept clients. Exit codes were not retrieved. Main VS stayed running;
  no forced/main-instance shutdown, deployment, speech, recording or bridge Send was performed.
- **Decision/limits:** Accept the approved metadata-only phase; all seven regression routes and
  targeted runtime audit closure pass. Blank/history-only surfaces without a supported live binding
  remain unavailable; do not fabricate IDs or readiness. Native ambiguity/capacity/timeout/pending-
  IPC failure injection is not claimed. Reader migration/delivery stay in separately gated phases.
- **Commit boundary:** Source/project/doc changes are reviewed and uncommitted. Local commit and
  push require explicit approval; no personal settings, reply/audio contents or proof logs belong in it.
- **Approval:** The user subsequently approved the local Phase 1 commit only; no push approval.

## 2026-11-21 - Phase 1 pushed; Phase 2 reader reuse breakdown prepared

- **Evidence:** The user approved pushing and starting the next phase. Remote main was verified
  exactly at `9fca48c`; the working tree was clean before Phase 2 documentation preparation.
- **Proposed boundary (awaiting confirmation):** Extract the existing document/navigation/preference
  types through Compile links, keeping the speech-owning ReaderWindow and standalone behavior outside
  VS. Reuse the saved reading choice, map reordered variants by section ID and keep switches paused.
  Add actual external pause/resume acknowledgement and asynchronous bounded transport rather than
  blocking the IDE or simulating pause through cancellation. Preserve separate playback/UI lifetimes.
- **Presentation/acceptance:** An Exp-only opt-in migrated single-reply view uses prepared paired
  content and isolated proof preferences, never a fabricated reply feed. Native frame close/hide,
  docking/auto-hide and reparenting need separate runtime proof; generic visibility loss is not owner
  destruction. Manual open does not start speech or change future Speaker state. VS persists layout.
- **Deferred:** Scoped settings/voice/appearance overrides, automatic Speaker/history/queues, reply
  delivery, microphone/VoiceType/model work and main installation. No new runtime change, commit or
  push is implied by preparing the breakdown; implementation follows its confirmation gate.

## 2026-11-21 - Phase 2 confirmed; preserve shared reader implementation first

- **Decision:** The user confirmed the Phase 2 breakdown. Start with a literal extraction of
  existing speech-free document/navigation/preference types through Reader/VSIX Compile links;
  keep ReaderWindow, System.Speech, CLI/window lifetime and palette unchanged in the Reader.
- **Rationale:** Reuse tested step-prefix/word offsets, reordered section matching and atomic
  choice persistence without two diverging implementations or a speech-bearing VSIX dependency.
  Shared WPF document types are presentation data, not microphone/synthesis execution.
- **Validation boundary:** Actual-DLL model parity checks and the seven existing regression routes
  precede external playback/native integration. No pause/resume or native UI migration is claimed
  by extraction. Existing nullable behavior is retained under #nullable disable in shared source.
- **Result:** Model 379 and all seven preserved routes pass; VS MSBuild/IDE build and independent
  targeted regression audit pass. Exp 0.4.1 manifests/DLL/bundled Reader match, installed model and
  self-tests pass, package/metadata loading has zero package errors, and graceful task-only shutdown
  removes the endpoint. This accepts extraction, not full native reader/layout/playback migration.
- **Checkpoint:** The user explicitly approved a local reviewed extraction commit only; no new push.

## 2026-11-21 - Extraction pushed; bounded external state acknowledgements next

- **Approval:** The user explicitly pushed `da8fd78` and requested continuing the confirmed Phase 2.
- **Implementation boundary:** Keep native preview/probe synchronous wrappers for compatibility,
  but new playback uses asynchronous serialized/bounded pipe writes and genuine external pause/resume.
  Correlate state acknowledgements with speech generation and a separate command ID; never publish
  paused/resumed from command submission alone or let stale state errors alter current speech.
- **Validation:** Include raw silent-worker observation to prove progress actually stops rather than
  relying on the host's paused-event filter. Full native reader/controller integration remains later;
	0.4.2 VS MSBuild/IDE builds and all eight routes pass (speech 93); native loading remains pending.
- **Audit gate:** Independent silent reproducers found queued replacement Speak can outrun Cancel,
  and legacy Cancel's new TimeoutException escapes its UI handler. Obtain explicit fix approval,
  add those exact regression cases and re-audit before native playback wiring/deployment. Passing
  the current suites is not acceptance of these uncovered interleavings or exception contracts.

## 2026-11-21 - Approved transport audit fixes and final combined review

- **Decision:** The user approved both reproduced fixes. Increment a cancellation epoch even when
  idle and check the start's captured epoch under the mutation lock; reject earlier queued starts
  without allocating speech generations, but permit later deliberate Play. Preserve legacy Cancel's
  InvalidOperationException contract while keeping asynchronous timeout information explicit.
- **Evidence:** Exact queued replacement/idle-start and actual non-reading-pipe timeout tests pass;
  independent narrow closure closes both findings with no significant new directly coupled issue.
  All eight routes pass, speech 104. Unchanged isolated model preference File.Replace failed twice
  before a passing rerun; it was not fixed. No 0.4.2 Exp or native-reader acceptance is claimed.
- **Approval/review:** The user approved a local transport commit and continuation, not push. Finish
  the confirmed Phase 2 reader before later phases. The requested combined review follows all
  implementation phases and supplements, rather than replaces, per-phase checks and blocker closure.

## 2026-11-21 - Bounded unwired single-reply playback owner before native view

- **Decision:** Add an internal VSIX controller and typed external-worker/session adapter, not the
  visible view or frame. Keep one supplied reply, separate reply/version/operation/prompt-generation
  fences and explicit owning-dispatcher access, including continuations without SynchronizationContext.
  The owner accepts the already loaded Concise/Detailed choice; preference load/save and controls
  remain the following view's responsibility through the existing shared model.
- **Rationale:** Isolate exactly-once sequential completion, suffix/step offsets, acknowledged true
  pause/resume, section-ID mapping and Stop/hide/startup races before native layout/focus migration.
  Retired sessions are cancelled/disposed by exact local identity; a launch ignoring cancellation
  may clean only its own returned resource. Normal completed segments reuse the same session.
  A later start awaits the owner's existing-session retirement asynchronously, without a UI wait
  or dependence on an obsolete launch that ignored cancellation.
- **Bounds:** Coalesce progress monotonically with sticky completion, at most 16 generation slots
  before Speak returns and one queued dispatcher callback per prompt; overflow is an explicit error.
  No synchronous UI waits, automatic error retries or controller activation/scroll/focus effects.
- **Alternatives:** Duplicate Compile-linked controller tests, BCL-only untyped production delegates,
  an over-general playback framework and new test packages/projects were rejected. Internal typed
  interfaces plus framework RealProxy test injection exercise the actual VSIX DLL without public APIs.
- **Acceptance boundary:** 0.4.3 is uninstalled and the native PoC/ReaderWindow are unchanged. The parent
  independently verified a fixed 0.4.2 transport package in Exp; that is not 0.4.3 controller/view/frame
  acceptance. The new executable route covers actual adapter/child workers, not native layout or UI.
  Parent-owned independent targeted review and the next native-view/Exp gates remain required.
- **Automated evidence:** Final no-deploy VS MSBuild passes (existing toolbar warning only).
  Actual-DLL playback 148 passed before eight serial routes: speech 104, toolbar 62, bridge 209,
  gesture 209, contracts 136, model 379, self 1,868 and integration 292/seven mappings. No flake
  occurred on that final run; the pre-existing preference issue is unchanged, not resolved.

## 2026-11-21 - Pre-audit operational exception boundary correction

- **Decision:** Narrow controller startup/state/cancellation catches to expected IO, invalid/disposed
  state, cancellation and timeout failures, with access/Win32 failures at startup. Unexpected
  programmer exceptions fault operation tasks rather than being converted to successful Error results.
- **Evidence:** New fake cases reproduce the former swallowed launch programmer exception and cover
  launch/speak/pause/resume/cancel plus stale launch/resource fences. Final no-deploy VS MSBuild and
  all nine serial routes pass: playback 190; eight prior route counts remain unchanged. Independent
  review and unwired native-view/Exp gates remain pending; no deployment or preference fix occurred.

## 2026-11-21 - Explicit natural-completion outcome for pending state changes

- **Decision:** Internal Pause/Resume tasks return true for confirmed state and false when their exact
  prompt completed naturally. Track the last completed generation for a request racing completion;
  keep explicit cancellation, stale-generation refusal, timeouts and faults distinct.
- **Evidence:** Independent review and primary reproduction found cancellation-first Pause/Resume
  callbacks could discard legitimate completion and leave section0 in Error. Both completion/outcome
  orders, intermediate/final sections, duplicates and real-host exact-generation cases now pass:
  playback220, speech110 and all seven preserved routes; no-deploy MSBuild/IDE builds pass.
- **Alternative:** Ignore every OperationCanceledException, delay the handler or rely on dispatcher
  scheduling order; rejected because these hide genuine failures or retain the race. No wire-schema
  change or new exception framework is needed; independent targeted closure remains required.
- **Workflow approval:** The user approved relevant audit corrections and commits when required.
  Continue all remaining phases autonomously; park blockers and continue independent items without
  treating parked gates as passed. Push/main-instance installation still require separate approval.
- **Closure:** Independent playback220/speech110 and actual-DLL host/adapter/controller injections
  close the finding, with genuine cancellation, duplicate completion and Stop/Dispose cleanup intact.
  No significant coupled issue remains; natural race frequency and native presentation remain unproved.

## 2026-11-21 - Gated native prepared-reply presentation and owned frame advice

- **Decision:** Wire the audited single-reply owner only into a new internal native view selected by
  Debug + exact /RootSuffix Exp + launch VSCOPILOTCHATVOICE_READER_PREVIEW=1. Preserve the old ungated
  Speak/Cancel control and standalone ReaderWindow. The only supplied production content is an explicitly
  labelled five-section paired fixture; no Copilot feed or text-bearing metadata is implied.
- **Reuse:** DocumentBuilder, ReadingNavigation and ReplyVersionPreference retain their shared behavior
  and existing preference location. Load before playback; missing Concise keeps saved intent and reports
  fallback. Explicit radio Click, including an already-selected Detailed fallback, saves immediately.
  Expected I/O/access/invalid-data failures remain visible; tests/native proof can inject isolated paths,
  with any runtime path override restricted to the same Debug/Exp gate. No new schema/settings exists.
- **Presentation:** Dispatcher-bound Changed notification plus observed task-only background work avoid
  polling and duplicated playback ownership. FileAndForget/injected reporting preserves unexpected task
  faults, including autonomous advance/queued drain failures. Intentionally aborted stale dispatcher
  drains are expected lifetime cancellation, not false UI errors. Shared word/step offsets, paused
  reordered-version selection, Follow suspension/restoration and native keyboard scope are reused.
- **Frame:** Keep float-first registration and VS-managed layout. IVsWindowFrame2.Advise adds an owned
  Notify/Notify2/Notify3 sink rather than replacing VSFPROPID_ViewHelper. The referenced compile-time SDK
  requires OnClose(ref uint); preserve incoming options. Explicit close requests asynchronous Stop;
  Esc requests Stop/Hide. Other frame notifications and WPF unload/visibility/reparent/auto-hide are not
  disposal or Stop. Missing/failed advice disables Play rather than claiming close safety. Pane teardown
  retires the exact subscription/view/controller/base pane without synchronously waiting for cancellation.
- **Appearance/focus:** Use dynamic owning VS/system resources for content/markers/highlight/selection,
  warnings/disabled/focus; opaque system High Contrast overrides theme. Native controls and text wrap.
  Manual wheel/scrollbar browsing suspends Follow; explicit Follow/seek restores it without focusing
  Chat/editor. Reader-text-only Space/arrows/bounds/Esc do not intercept native controls, Tab, modified,
  already-handled or repeated keys; no global keyboard handlers or geometry persistence was added.
- **Rejected:** Instantiate/reference the synthesis-owning ReaderWindow in VS, duplicate/Compile-link
  the owner, stop/dispose on generic WPF visibility changes, blindly displace frame helpers, block the UI
  for cancellation, fabricate Concise/reply feeds, or add Settings/Speaker/microphone in this increment.
- **Evidence/limits:** Actual-DLL native route179 reuses playback typed fakes and isolated repo-local
  preferences for synthetic presentation/resource/keyboard/frame/startup/hide/shutdown checks. Parent
  frozen 0.4.3 Exp loading/transport (owner220/self1,868/raw paused worker/clean task exit) is verified,
  but current 0.4.4 remains uninstalled. Parent independent targeted review and all native frame/layout/
  keyboard/theme/DPI/two-instance gates remain open; synthetic success never passes native acceptance.

## 2026-11-21 - Fence synchronous presentation/task-observer reentrancy (0.4.5)

- **Evidence:** Independent review found a Medium introduced defect: one mailbox contained progress and
  completion; highlighting synchronously raised Run.RequestBringIntoView, whose handler called FrameClosed.
  Drain continued after Stop and started the next section. The permanent test first failed against the
  unchanged 0.4.4 actual DLL with during=Stopped, after=Speaking, launches=2. User approved related fixes.
- **Decision:** Treat Changed/BackgroundWork observers as reentrancy boundaries. Revalidate current prompt,
  operation and version before old completion/automatic Seek restart/state-command submission/accepted
  Ready continues. Capture invalidation operation before callbacks and check again after background-task
  observers before advancing; only the current lifetime may perform follow-on work.
- **Coupled view boundary:** Synchronous version notification also reproduced an older save overwriting a
  newer explicit preference. Retain newer version intent, skip preference writes after disposal, and fence
  Follow/navigation and explicit frame hiding after synchronous close/disposal. Do not reinterpret a simple
  Stop as an unrelated version change; a still-current explicit version choice remains persistable.
- **Alternatives rejected:** Delay notifications/completion, suppress BringIntoView, block/retry on the UI,
  ignore every cancellation, or duplicate the controller. Reentrant same-prompt Pause must still allow valid
  early completion, and later explicit Play must remain available.
- **Synthetic evidence:** Actual native route301 covers exact highlighted-Run close and Stop/Dispose/version/
  seek reentrancy across progress/completion, seek restart, Starting/accepted Ready, pending Pause/Resume,
  background-task observers and view continuations. Playback220/speech110 remain unchanged. No native VS
  launch/deployment or parent helper/state changes occurred; independent narrow closure and every migrated
  native runtime gate remain pending. Manifest 0.4.5 identifies this runtime correction, not native acceptance.

## 2026-11-21 - Native accessible text and supported non-click radio selection (0.4.6)

- **Prior closure/native boundary:** Independent 0.4.5 reentrancy closure passed (Stopped, one launch/exact
  cleanup; native301/playback220), with no significant coupled issue. Parent frozen official Exp installation
  matched binaries and installed checks; restored gated Play/frame advice, actual non-silent worker, floating
  X/worker cleanup, actual menu reopen without worker/autoplay and graceful task/worker exit are verified.
  This is bounded close/reopen proof, not full layout/focus/theme/version or retained-position acceptance.
- **Reproduced defect:** Native UIA could not obtain actual status/warning content: fixed generic Names
  overrode it, with no usable text/value pattern. Before changes, actual WPF peer tests on unchanged 0.4.5
  returned Playback status instead of displayed Stopped/section text. The actual radio selection provider
  also changed IsChecked without applying/saving Concise. Both observations are permanent regressions.
- **Decision:** Bind status/preference/frame accessible Names one-way to displayed Text; preserve IDs and
  polite live settings. Stable per-word status text does not change Name or cause per-word live-property
  updates. Handle non-click Checked version selection through the existing owner/preference path. A small
  private RadioButton subtype marks native OnClick, avoiding Checked/Click double execution while retaining
  explicit already-selected Detailed fallback/save. Inherit native automation peer/role/style/focus/keyboard;
  do not add a custom provider, polling, owner, or speculative mouse-focus workaround.
- **Synthetic evidence:** Actual-DLL native348 includes existing reentrancy checks plus held peer Name/state/
  section changes, visible load/save/frame/operational/unexpected errors, cleared warning Names, quiet word
  progress, actual SelectionItemProvider.Select and native OnClick, disabled variant refusal, paused section
  mapping/exact worker retirement and no automatic speech. Source/manifest is 0.4.6, uninstalled; new narrow
  independent closure and live read-only accessibility proof remain pending.
- **Parked native input:** Toolkit select-item refused before action because its selection container was
  missing (actionAttempted=false); it never mutated a native version. Foreground interference separately
  blocked a physical radio click. Do not call either a native version proof or an established package defect.
  Preserve parent comparison/state/helper/index and keep later Settings/capture/main installation out of scope.

## 2026-11-21 - Preserve genuine accessible choice through synchronous refresh (0.4.7)

- **Original evidence:** Independent 0.4.6 closure found a Medium directly coupled defect. While Detailed
  spoke, highlighted Run.RequestBringIntoView invoked the actual Concise SelectionItemProvider.Select;
  Refresh still held its broad flag, so genuine Checked was suppressed with no Click fallback. A permanent
  test first failed on the unchanged actual 0.4.6 DLL: checked=True/modelConcise=False/Speaking/cancels=0.
  Ordinary native348/playback220 passed; 0.4.6 was never installed. This is reproducible source behavior,
  separate from the parent's blocked live input/toolkit selection-container cases.
- **Decision:** Suppress model synchronization by the desired choice already matching controller mode,
  not by a refresh-wide boolean. Genuine peer changes and native Click run synchronously from Follow;
  no dropping, queuing, delay or global guard. Deduplicate only native OnToggle's Checked from later Click,
  leaving the Click handler's nested same-radio peer intent available. Preserve exactly-once native Click,
  already-selected Detailed fallback/save and existing owner/view version/operation/lifetime fences.
- **Coupled shutdown:** Permanent actual-peer test reproduced a still-enabled choice during controller
  disposal notification after its model was already Disposed. Retire view intent before controller.Dispose;
  final Refresh disables controls, peer Select refuses, and late native Click/Checked submit nothing/save
  nothing. This changes view ordering only; the audited owner and its task-failure semantics are unchanged.
- **Synthetic evidence:** Native412 preserves native348 plus 64 actual peer/native Click synchronous Follow,
  nested newer/same-radio preference, Stop/close/disposal, expected save-failure, no-save model synchronization
  and stale-event/shutdown/late negatives. Native role/style/focus/keyboard and accessible Names stay intact.
  Source/manifest is 0.4.7, uninstalled; new independent narrow closure and live accessibility/input gates
  remain pending. No synthesis, owner duplication, schema, mouse-focus workaround, deployment, VS UI or
  parent state/helper/index/comparison change is introduced.

## 2026-11-21 - Explicit selection origins/revisions and owned-control Escape (0.4.8)

- **Original proof:** Independent 0.4.7 Follow closure passed, but a native Checked callback's actual peer
  request matching the original model was ignored; older unconditional Click then overwrote it. Permanent
  original-DLL tests reproduced both directions (version1 -> version2/opposite mode/Paused/cancel1) and
  owned Play/radio Esc refusal (handledFalse/hide0/Speaking/cancel0). No 0.4.7 installation occurred.
- **Decision:** Model equality does not establish origin: genuine same-mode Checked may request immediate
  persistence. The private VersionChoice marks only model setter/native's originating Checked event and
  consumes its marker before base RadioButton group callbacks. Nested peer Checked on the original radio
  within stacked OnToggle remains genuine. Capture a monotonic selection request before native OnClick;
  reject superseded Click and same-version save continuations, preserving newest intent/failure notices.
  Only native's own paired Checked waits for its once-only Click. Inherit native automation/focus/style;
  do not add custom providers, broad suppression, delays, retries or preference/schema/owner changes.
- **Keyboard contract:** Section 3 approves focused-reader Esc stop/hide, not text-only Escape. Accept owned
  buttons/radios/surface/text only for unmodified/unhandled/nonrepeat Esc, retaining reply/position. Keep
  all other native button keys/Tab, dropdown/edit/scrollbar/slider and outside-view handling untouched.
  No speculative word-click focus intervention is introduced.
- **Synthetic evidence:** Native560 includes both-direction exact two-peer/longer round trips, a third
  nested native click on either radio, later Click peers, nested model Checked/Unchecked, immediate matching-
  mode persistence, same-version locked-file expected save-failure retention, fallback/disposal and every
  owned-control Esc/cleanup/retained-position/late-event path. Radio fixtures call actual WPF OnClick;
  bare synthetic Click cannot bypass revision tagging. Source/manifest 0.4.8 is uninstalled; independent
  scoped closure and native input/accessibility/layout acceptance remain pending. Existing warning-name
  intermittency is unconfirmed, not evidence for an IO blame, blanket retry or unrelated preference fix.
- **Verification stop:** Isolated native560 passes, but both final serial native attempts failed; all nine
  other routes retain their passing counts. One recorded latest model/radio agreed while File.Replace save
  reported `Unable to remove the file to be replaced.`; underlying cause is unconfirmed. The last serial
  round-trip saved-choice/radio assertion also failed without a precise state snapshot, so its cause is not
  assigned. Stop before installation rather than relax assertions, add retries or speculate about binding/
  preferences. Source0.4.8 all-ten/reliable selection closure is blocked, not complete.

## 2026-11-21 - Independent scoped voice storage while native acceptance is parked (0.5.0)

- **Boundary:** Follow the user's instruction to park blockers and continue independent work. Preserve
  uninstalled native0.4.8 as a frozen session artifact; do not interpret subsequent passing native560
  as closure of its two earlier failures. Implement storage/model only, without Settings/toolbar activation,
  voice enumeration/application, microphone, reply delivery or main installation.
- **Model:** Immutable nullable overrides distinguish inheritance from explicit false/zero and empty
  engine-default voice. Resolve built-in < global < solution < window and retain each value's origin.
  Initial fields are reading mode, desired Speaker, voice name, rate and volume; other Phase3 fields/UI
  remain planned. Desired values are not active playback/Speaker state.
- **Persistence:** App-local strict versioned XML, one global file and SHA256-normalized solution-path
  files, avoid a dependency or duplicating metadata JSON parsing. Unknown/corrupt data refuses both read
  and update. Selected-field patches reread under an exclusive empty lock-file handle; adjacent flushed
  temporary files replace/move atomically. Kernel handle lifetime provides cross-process/session crash
  release without persisted PID or live-owner takeover; five-second contention failures remain explicit.
- **Migration/notifications:** First explicit global access imports only existing Concise/Detailed through
  its unchanged helper; existing global data then wins. Do not rewrite/mirror the legacy file or persist
  temporary window values. Owned filesystem notifications expose invalidation/error only, never automatic
  playback, recording or control changes. Future UI performs storage off-thread and dispatches asynchronously.
- **Alternatives rejected:** Whole-document/all-scope replacement risks unrelated lost updates; PID lock
  files leave stale owners; silently resetting invalid data loses intent. Actual voice support remains an
  external-worker capability gate, not inferred from a valid string or every Windows Settings voice.
- **Evidence:** No-deploy VS MSBuild/package and IDE builds pass; linked126/combined actual-DLL253 settings
  assertions and all ten preserved routes pass in one serial run. Real children prove merged field/revision
  updates, refusal of live-owner takeover, release/crash recovery and empty owner metadata. Schema/access/
  atomic-save/watcher failures and untouched legacy/window isolation pass. Independent settings review
  remains pending; no native or full Phase3 acceptance, installation, commit or push is claimed.

## 2026-11-21 - Park scoped persistence after fresh independent verification failure

- **Evidence:** The parent 0.5.0 serial run passed settings253 and ten preserved routes. A fresh
  independent settings run exited1 during the real two-child rate/volume merged-update check;
  ScopedSettingsStore.Save/File.Replace reported `Unable to remove the file to be replaced.`
  The child and linked/actual-DLL leg were not printed. DLL identity matched; no retry was run,
  no source was changed and isolated roots were removed.
- **Decision:** Park reliable persistence acceptance and retain the verified 0.5.0 package outside
  the repository alongside blocked0.4.8. Continue independent external speech capability work
  without activating the store, Settings or audio. Earlier passes do not close either reliability gate.
- **Rationale/alternatives:** No underlying cause is established. Similar native/preference error text
  does not establish a common cause. Do not replace atomic primitives, add repeat-to-green retries,
  weaken assertions or blame the environment without evidence. Revisit retained failures with the user.

## 2026-11-21 - Read-only external voice capabilities and queued-deadline correction

- **Boundary:** Add Reader-only headless metadata discovery, not Settings activation or speech Preview.
  System.Speech remains external; strict speech-free contracts/query are source-linked into consumers.
  Report actual engine names/cultures/enabled/default identity and supported rate/volume/no pitch,
  rather than assuming all Windows Settings voices are available or substituting missing selections.
- **Process contract:** One bounded schema1 UTF8 report, concurrent bounded stdout/stderr drains,
  nonzero discovery/output errors and exact owned-child cancellation/cleanup. Caller timeout is at most
  30 seconds; cleanup waits share two seconds. Direct CLI has no watchdog; synchronous native calls
  are not preempted. This is not an unconditional native wall-clock guarantee.
- **Evidence:** Implementer/independent capability136 pass; final settings route fails unchanged
  File.Replace at VerifyStore151, while ten other routes pass. An earlier ten-second real-child timeout
  lacks elapsed/PID/substage evidence; later375ms direct execution/passing probes do not explain it.
- **Authorized correction:** Independent actual-DLL starvation reproduces a100ms query completing
  after3554ms with Win32Exception instead of timeout. Start deadline/cancellation ownership before
  scheduling and prevent expired late launch; do not assume pool-based timers can run when the pool
  is exhausted. Add isolated permanent deadline/cancellation starvation coverage and preserve exact
  cleanup. Target0.5.2 needs retained-reviewer closure;0.5.1 is preserved uninstalled, not accepted.

## 2026-11-21 - Close pool-independent capability scheduling correction (0.5.2)

- **Decision:** Capture the absolute deadline at API entry and use narrowly owned dedicated
  orchestration/drains with pool-independent waits and prelaunch expiry checks. Pool-based callbacks
  cannot guarantee timely retirement under starvation. Preserve native-call nonpreemption and explicit
  owned cleanup/errors; do not pretend a timeout can preempt synchronous Start/Kill/dispose.
- **Evidence:** Build/package and parent IDE build pass. Implementer/retained-reviewer capability152
  pass once. With a sole worker held3503ms, exact-DLL deadline/cancellation complete127ms, owned child
  exits, and expired entry completes0ms with zero starts after late callbacks. Missing executable fails
  legitimately15ms before expiry. Original finding closed/high confidence; no significant coupled finding.
- **Limits:** Supplemental fixture/reflection checks never executed; cold-start ten-second timeout
  cause remains unknown. Final settings concurrent child fails unchanged File.Replace; ten other routes
  pass. No native/storage/fullPhase3 acceptance or installation follows this narrow closure.

## 2026-11-21 - Advance history data independently of parked integrations

- **Decision:** Follow the user's park-and-continue instruction with a bounded speech-free in-memory
  captured-history model. Active Settings/appearance depends on native/persistence reliability;
  selective capture reuse needs permission/hardware; production insertion and reply feeds remain
  unsupported. Implement no dependent activation or fake completion claim to bypass those gates.
- **Scope:** Immutable completed caller-supplied variants, instance/session/client identity, stable-ID
  dedup/conflict refusal, explicit bounded capacity and navigation/manual-arrival state. No persisted
  text, WPF document retention, fabricated Concise, queue policy, Speaker/audio, toolbar or delivery.
- **Alternative:** Halt all work or activate unsupported integrations; neither follows the approved
  independent-work boundary. Foundation tests/review do not establish native feed or Phase6 acceptance.

## 2026-11-21 - Immutable captured-history model and shared legacy validation (0.5.3)

- **Decision:** Bind a locked catalog to immutable instance/session/ordinal-client identity and copy
  completed caller DTOs into immutable variants/snapshots. Completion remains caller-asserted, not SDK
  authority. Exact ordered payload plus nullable metadata deduplicates a reused ID; conflicts/capacity
  return distinct outcomes without mutation/eviction. Future callers must handle refusals visibly.
- **Reuse:** Extract pure ReaderBlockValidation for catalog and ReaderDocument, preserving legacy
  signatures, permissive IDs/numbers, prefix-only steps, error order, formatting and spoken offsets.
  Stronger production bounds/readable-body checks remain confined to the new model.
- **Bounds/lifecycle:**64 replies/1M raw Text units,128 blocks per variant/100K per block,128-unit IDs,
  256-unit clients/1024-unit metadata. Separate navigation/manual-arrival flags clear only on Latest;
  no arrival forces current selection, queue/audio or UI. Owned clear/dispose releases payload references.
- **Evidence:** Implementer/independent history651 pass once and parent IDE build passes. HEAD review
  confirms legacy parity, no significant finding. Settings child File.Replace remains failed/parked;
  eleven other preserved routes pass. Source0.5.3 is uninstalled, data-only, not fullPhase6 acceptance.

## 2026-11-21 - External immutable speech-option boundary (0.5.4, review pending)

- **Decision:** Use a distinct configured-speech command and immutable voice/rate/volume snapshot;
  older workers must refuse rather than silently ignore options. Keep selection/property mutation in
  the external worker at replacement boundaries, after previous-prompt retirement. Default means the
  worker-initial engine identity/rate/volume, restored for subsequent legacy Speak.
- **Evidence contract:** Generation-correlated applied getters confirm configuration, not successful
  speech completion. Preserve bounded command writes/acknowledgements, cancellation epochs, actual
  pause/resume, offsets and legacy frame sequence; explicit read-only queries prove legacy defaults.
  Missing/disabled/ambiguous choices fail without substitution. No Settings/backend/UI consumer is wired.
- **Verification:** No-deploy build/options422 pass with real null-output worker. Twelve preserved
  routes pass, existing settings child File.Replace fails without retry/fix. Independent boundary review
  is pending; source is uninstalled, with no Windows-wide setting change or fullPhase3/native acceptance.

## 2026-11-21 - External option review closure and precise default semantics

- **Evidence:** Independent once-probe422/source review passes with no significant issue; binary and
  harness copies match, zero owned Readers/fixtures remain. Parent IDE build passes. Null-output real
  getters/default/reset/missing-name/lifetime proof is distinct from synthetic unavailable/ambiguous,
  hostile acknowledgement and ordering cases.
- **Clarification:** Empty voice chooses the worker-initial identity only; configured rate/volume are
  explicit. Legacy Speak restores all initial values. Matched command0 evidence proves applied getters,
  not synthesis-start/natural-completion success. Positive-command read-only queries cannot acknowledge
  configured startup. Retirement waits5000ms outside callback locks; native calls are not preempted.
- **Limits:** No consumer, actual voice uninstall/disable, audible/nativeExp, persistence or fullPhase3
  acceptance follows this bounded closure. Keep source0.5.4 uninstalled and blockers parked.

## 2026-11-21 - Atomic-save lock cause found; bounded retry (0.5.5)

- **Evidence:** A standalone File.Replace stress in %TEMP% failed with Win32 1175 in about 1.5-3% of
  back-to-back saves and 6-12% at 0.1-2s spacing, always recovering on retry (median 1ms, max 21ms).
  MoveFileEx replace-existing failed at a similar rate (Win32 5), so swapping primitives does not help.
  The same stress inside the repo bin folder had 0 failures in 3,000 saves plus 200 spaced saves. Restart
  Manager reported no locker. Defender real-time, Defender for Endpoint and Search Indexer are active; the
  location dependence fits scanner/indexer handles, but the process was not identified (no admin tooling).
- **Decision:** New shared AtomicFileReplace.Publish retries only IOException Win32 1175/32/33 for a 1s
  budget (2ms doubling to 100ms), leaving both files unchanged between attempts; other errors and budget
  expiry surface unchanged. Both ReplyVersionPreference.Save and ScopedSettingsStore.Save use it. A
  persistent lock can block the calling thread up to about 1s before the existing visible save warning.
- **Result:** Deterministic integration checks (transient lock clears, persistent lock bounded and
  non-destructive, non-transient error not retried) pass; integration is 298. Settings route passed 5 of
  5 and native route 3 of 3 afterwards, versus repeated earlier failures. That is strong, not conclusive,
  evidence for the 10% temp-folder rate; native Exp acceptance remains separate.

## 2026-11-21 - VoiceType source reuse permitted

- **Decision:** The user explicitly permits selective copying of the local VoiceType source (revision
  7c0b6f8, no tracked LICENSE/NOTICE) into this repository for the Phase 4 external capture host. This
  closes the permission gate; it does not widen scope. Copy only what the plan lists (capture, amplitude,
  WAV session writing, bounded process ownership); never the tray, personal settings, history, hotkey hooks,
  clipboard helper, models or binaries. Record provenance in the copied files and README.

## 2026-11-21 - Defer live-chat reader acceptance to delivery integration

- **Decision:** Do not block Phase 3 on automatic reader reopen after a hidden/unpinned window receives a
  new reply or on Concise/Detailed acceptance against real replies. Both require the verified reply-delivery
  path and belong to Phase 6/integration acceptance; Phase 3 uses prepared fixtures for settings and
  persistence validation.
- **Rationale:** The current reader preview has no real reply producer, so testing those behaviors now would
  prove only a proxy. Keeping the boundary explicit prevents Phase 3 settings work from claiming live-chat
  behavior while preserving the requirement for later delivery validation.

## 2026-11-21 - Approved persistence deadline and diagnostic closure (0.5.6)

- **Decision:** After independent0.5.5 baseline build/settings253/native560/integration298 passed once,
  the user explicitly approved R1/R2. Cap lock-retry sleep to remaining time and refuse another attempt
  after expiry; retain both operational preference save/cleanup failures as an IOException with visible
  messages and AggregateException inner causes, compatible with existing reader failure handlers.
- **Rationale:** A pre-sleep expiry check alone admits late successful retries; a finally cleanup failure
  can otherwise replace the original save diagnostic. Private retry/cleanup boundaries permit controlled
  clock and real locked-file tests without a public test API or changed caller signatures.
- **Alternatives:** Do not replace atomic primitives, silently swallow cleanup, expose a top-level aggregate
  outside existing IOException filters, or rely solely on scheduling-sensitive sleep tests. Native calls
  remain nonpreemptible; a retry budget is not an unconditional whole-call deadline. Locker identity remains
  unproven despite the earlier location-dependent stress evidence.
- **Executed evidence:** No-deploy VS MSBuild/IDE builds pass; integration320/seven mappings (22 added
  deadline/cleanup assertions), settings253/native560/model379/Reader1868 pass once serially. Independent
  correction closure remains pending; no new Exp installation, audible proof, commit or push.

## 2026-11-21 - Phase 3 completion before capture; combined execution/UI review

- **Decision:** Finish persistence increment1, execution increment2 and combined Settings/reader-appearance
  UI increment3, then acceptance/documentation/manual commit review in increment4. The user approved one
  combined2/3 audit, while targeted tests still run after coherent edits. Only Settings enables; Mic/Speaker
  remain disabled. Audio policy applies to both Preview and native reader, independently of skill TTS.
- **Reset:** Global Restore defaults resets built-ins; Solution/Window Clear overrides inherits the lower
  scope. Individual fields offer inheritance. Never erase another scope or change Windows settings.
- **Sequencing:** Complete automated/official isolated Exp checks before asking for remaining perceptual
  user checks. Phase3 acceptance and commit precede any Phase4a capture. Real-chat reopen/variant acceptance
  stay Phase6 requirements, not Phase3 blockers. No push/main-instance installation is authorized.

## 2026-11-21 - Persistence correction independent closure (0.5.6)

- **Evidence:** Independent no-deploy build and once-serial integration320/seven mappings,
  settings253/native560 pass with exit0; diff whitespace check passes. R1/R2 are closed, with no new
  in-scope finding. Private tests exercise actual production methods; the original failure in the real
  locked-file cleanup test is injected, while full Save double-failure wiring is source-reviewed.
- **Boundary:** No native Exp, audible, full Phase3 or broad persistence-reliability acceptance follows.
  Locker identity remains unproven; no installation, capture, commit or push occurred during review.
