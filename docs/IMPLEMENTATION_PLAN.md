# Voice input and reader implementation plan

Draft: 2026-11-20. Records the design discussion after toolbar checkpoint `ee55bb6`.
The user authorized PoC 1 (exact chat bridge); the remaining production phases are planned,
not implemented. Confirm later phase gates before proceeding. Implement one independently
reviewable phase at a time, keeping it within approved scope.
This is a living plan: update it from implementation/testing evidence and user-approved
enhancements as work proceeds. Do not treat every design suggestion as approved scope.

## 1. Architecture and distribution

- Keep the approved `Mic | Speaker | Settings | Customize AI` chat-toolbar layout and sizing.
- Host the production reader presentation in a native VS Tool Window: floating on first use,
  then dockable, tabbed, pinnable and auto-hideable. VS owns layout/position/size persistence.
  Do not force floating on later openings or implement competing geometry persistence.
- Keep speech synthesis outside `devenv.exe`, reusing the existing net48 Reader executable
  as the speech-worker starting point. Preserve the standalone prototype CLI and regression
  behavior while extracting the production UI/playback boundary. Never instantiate its
  current speech-owning ReaderWindow inside VS.
- Add an external Windows .NET voice host for selected VoiceType capture/transcription code
  and pill presentation. Validate source provenance, notices, dependencies and deployment
  runtime before copying. The existing VoiceType source targets .NET 8; choose a supported,
  available deployment runtime rather than assuming that source target is the final package.
- Bundle our hosts/UI with the VSIX. Do not copy VoiceType's tray, personal configuration,
  history, global input injection or unrelated features.
- Keep Whisper executable/DLLs and models outside the VSIX, conventionally under
  `%USERPROFILE%\.copilot\toolkits\VsCopilotChatVoice\whisper\<version>` and `models`.
  Browse supports existing installations elsewhere. Neither extension nor skill downloads
  models; there is no Download button. Initially document manual engine provisioning too.
- Keep personal settings in app-owned LocalAppData storage, not tracked solution/repo files.
  Do not migrate unrelated VoiceType settings or change Windows-wide speech settings.
- The extension does not require the companion skill for Mic or Settings. The skill alone
  creates no buttons and cannot record. Automatic speech requires a verified reply-delivery
  source; skill installation is not an implicit Copilot event hook.

## 2. Microphone behavior

### Mouse and keyboard gestures

- Default mode: **Hold to talk**. Mouse down records; release over Mic finalizes, inserts
  and sends when auto-send is enabled. Release outside Mic finalizes and inserts only.
  Capture the mouse so release is observed; evaluate the actual release location.
- **Click to toggle**: first click records and leaves Mic depressed; second click finalizes.
  Auto-send follows the setting. Do not leave a depressed button after failure/cancellation.
- **Esc**, while the owning VS instance has focus, cancels recording or transcription.
  A subsequent release and any late worker results do nothing. Never insert/send cancelled text.
- Optional Chat-only keyboard gesture: Right Ctrl alone by default; configurable Left Ctrl.
  Request capture on key-down with no artificial 300 ms delay. Capture can briefly occur
  during Ctrl combinations; explain this when enabling the shortcut. No continuous idle audio.
- Hold mode ends on Ctrl release; toggle mode confirms a solitary Ctrl tap on key-up.
  Other keys/buttons invalidate the tentative gesture without swallowing normal shortcuts.
  Ctrl+C while an established toggle recording is running must not stop/cancel that recording.
  Ignore key auto-repeat and handle modifiers already held; never activate outside Chat.
- Keyboard recording uses the same mode/auto-send setting. There is no keyboard equivalent
  of release-outside; use auto-send Off to review. Focus loss never sends.

### State, targeting and feedback

- Explicit states: idle, starting, listening, transcribing, committing, cancelled/error.
  Serialize transitions and identify every recording/request; disable overlapping operations.
- Only one recording owner across this user's VS instances. Acquire ownership atomically;
  reject a competing start with a pill message without depressing its button. No takeover.
  Release after capture stops; serialize that instance's finalization separately. Crashes must
  not leave stale ownership. Never use a persisted PID/handle as authoritative ownership.
- Disable Mic when its chat is already responding. Validate backend/model/device before capture.
  Block recording until our active read-aloud is stopped; do not automatically resume it later.
- Use actual audio amplitude for smooth inner Mic fill with a fixed outline and a separate
  recording-state highlight. Feed Mic and VoiceType-style wave pill from the same capture.
- Keep the pill compact, non-focus-stealing and positioned over the originating chat textbox,
  preferably lower-center above its controls, without obscuring existing draft text. Verify
  anchoring across scrolling, DPI, moving/resizing, minimizing and multiple monitors. Hide a
  misplaced pill rather than drawing over another window; the toolbar remains informative.
- Show Starting mic until capture is actually ready, then Listening, Transcribing and useful
  gesture/cancellation hints. Do not promise zero microphone initialization latency.
- Insert only a completed, successful transcript into the verified originating conversation.
  Snapshot the target and draft revision; never overwrite a draft or use arbitrary foreground
  paste. An existing unchanged draft gets appended text, but the combined draft is never auto-sent.
- If the draft changes or Copilot starts responding during finalization, do not auto-send or
  overwrite the draft. Keep completed text for explicit recovery and explain through the pill.
- If the originating chat is unreachable, copy only the completed, uncancelled transcript to
  the clipboard and show confirmation. On clipboard failure retain it for recovery and report
  the failure. Do not claim success or automatically redirect insertion to another chat.
- Recovery must be reachable through the originating instance's UI (for example a Copy action
  in its status/details surface); do not rely on making a click-through pill interactive.
- Verify insertion before Send. If send acceptance is uncertain, report it and never retry
  automatically. Deduplicate commands/results to prevent duplicate insertion/submission.
- Never put partial transcripts, diagnostic output or error/status messages into the draft.
- Error and clipboard-fallback pills last at least **10 seconds**; cancellation acknowledgements
  **3 seconds**. Active status persists until completion/cancellation. Persistent setup failures
  retain an accessible warning badge on the affected toolbar button, within its existing bounds.
- Distinguish silence, audio with no recognized text, timeout, invalid/missing model, device
  loss and worker failure. Badges/tooltips explain the problem and lead to relevant settings.
- Engine/model selection, Refresh and Browse are supported. Validate compatibility, remember
  the chosen model, and report a deleted/unloadable selection; never silently change models.

## 3. Speaker, reader and reply history

- Speaker remembers automatic-read preference; Off stops playback, clears pending speech,
  hides the reader and retains captured replies/preferences. On restores the reader and permits
  subsequent automatic playback. Actual playback controls stay local to the originating instance.
- Keep each completed assistant reply as a distinct history item, with ordered blocks/sections,
  a stable conversation/reply identity, and supplied Concise/Detailed variants. Reject malformed
  pairs and deduplicate deliveries. Never fabricate a Concise version or speak partial streams.
- Maintain history for captured replies in that conversation during the session; do not imply
  access to uncaptured old messages or add persistent reply-text history in this implementation.
- New-reply policy setting: finish current then latest; switch immediately to latest; or read
  every reply in order. Never append arriving text to a prompt already being spoken. Bound any
  queue/history resource usage explicitly and report capacity limits; never silently drop items
  in read-every-reply mode. Speaker Off and cancellation invalidate pending callbacks.
- Add labelled Previous reply / Next reply, a `Reply N of M` picker with previews, and Latest.
  These are distinct from current within-reply arrows. Switching replies stops playback; Play
  reads the selected item. Manual older-reply browsing suspends automatic jumps/interruptions;
  arrivals show an indicator. Latest returns to live mode under the chosen queue policy.
- Preserve sentence/paragraph navigation, word seeking/highlighting, Follow and section-aware
  Concise/Detailed switching. A version switch stays paused until Play, preserving prior behavior.
- Reader keyboard scope is its focused tool window only: arrows match existing navigation,
  Space plays/pauses, Alt+Left/Right selects replies, Home/End seeks current-reply bounds,
  Tab navigates controls. Esc hides/stops. Respect dropdown/edit-control key handling and avoid
  double invocation. Showing a reader for a new reply must not steal typing focus.
- Tool-window X hides/stops rather than destroys reply history or changes Speaker preference;
  later automatic delivery may reopen it while Speaker is On. Auto-hide/docking itself must
  not be interpreted as a Speaker preference change. VS/host shutdown releases resources.

## 4. Settings, appearance and scope

- One Settings UI with **Apply changes to**: global defaults, this solution, this VS window
  (temporary). Precedence: window > solution > global; indicate inherited values and offer
  Use inherited value. Same solution path shares overrides across instances; window overrides
  distinguish simultaneous agents in the same solution and disappear when that instance exits.
- User-wide engine discovery/paths are resources rather than per-window installations. Voice,
  rate/volume, appearance, reading policy, mic mode/auto-send and selected model may be scoped.
- Store durable settings locally with schema/version validation, serialized atomic scoped
  updates and cross-instance notifications; preserve other windows' changes. Surface invalid
  data/access/save failures. Do not silently reset corrupted preferences or overwrite scopes.
- Migrate the existing remembered Concise/Detailed choice without changing it unexpectedly.
  VS persists tool-window layout separately; do not promise window-layout isolation by voice scope.
- Offer actually available speech voices with language, speed, volume, Preview/Stop and Reset.
  Pitch is conditional on verified engine/voice support; a decorative ineffective slider is not
  acceptable. Do not claim every Windows Settings voice is accessible to System.Speech.
- Theme defaults to the owning VS instance, updates live, and permits Light/Dark choices plus
  user font/font-size, text-color (including yellow), background and highlight overrides.
  Add A-/A+ to the reader. Include readable contrast/focus/disabled/error states and live preview.
- High Contrast takes precedence with system colors and opaque content. Native floating VS
  chrome stays native: opacity customization is limited to supported content/background rendering,
  not a promise of the old transparent standalone window. Large text wraps/scrolls without clipping.
- Apply settings safely: volume promptly, voice/rate at a suitable playback boundary; record
  with a settings snapshot, applying model/mode changes to the next session. Updating defaults
  must not start another window's recording/speech or change its active Speaker control state.

## 5. Working defaults and explicitly experimental scope

The following fill previously unanswered choices for this draft; they are not shipped behavior:

- Auto-send **On** initially; all review, existing-draft, target and race-condition guards win.
- At **five minutes**, stop recording, finalize and insert for review without auto-send. Tell
  the user why capture stopped. This safety threshold is subject to hands-on testing.
- Speaker On reads the latest completed captured reply when available, then follows new replies.
- New-reply default: **finish current, then read latest**. Preserve skipped items in history.
- Experimental chat-scroll synchronization is Off by default. Try mapping conversation, reply
  and block identity to exact chat elements; explicit reader navigation may scroll without
  stealing focus. Automatic speech advance does not scroll Chat. Virtualized/repeated/collapsed
  content must not cause approximate matches. Drop this feature if reliable targeting fails;
  it is not a dependency for history, playback or release.
- No extra sentence/paragraph pause controls, named agent presets, global hotkeys, team-shared
  settings, model downloader or unrelated VoiceType features in this plan.

## 6. Implementation phases and acceptance gates

### Bounded proofs of concept before the full migration

Run these as Phase 1 work, starting with the chat bridge. Use disposable test content and
existing provisioned resources; no model downloads or normal-instance installation. Keep code
reusable where sound, but do not migrate the whole reader/VoiceType app just to prove a hook.

1. **Exact chat bridge and reply identity.** In Exp, target one known conversation for guarded
   insertion/send and receive a completed reply through a verified delivery mechanism. Exercise
   two chats/instances, changed drafts, busy state, chat recreation and duplicate delivery.
   A prepared explicit bridge message proves transport, not automatic Copilot capture; record
   the distinction. Pass only with correct-origin behavior and safe refusal on ambiguity.
   Native scroll anchors are optional and must not hold up the required bridge decision.
2. **Native tool window with external speech.** Use one prepared reply to prove floating-first,
   docking/pinning/layout restoration, user-activated keyboard focus without activation stealing,
   theme updates and external speech progress/cancellation with in-process word highlighting.
   Pass only if the IDE does not own speech synthesis and late worker events cannot revive it.
3. **Ctrl-only gesture and capture lifecycle.** In the actual Chat textbox/panel, prove solitary
   Right Ctrl down/up, normal Ctrl shortcuts, repeat suppression, startup/release races, Esc
   during capture/transcription, focus loss and competing starts from two instances. Use an
   existing engine/model for one end-to-end recording when available. Observe actual startup
   behavior rather than claiming immediate readiness. Do not use global hooks as a shortcut.
   If Ctrl cannot coexist safely, defer this optional gesture and retain mouse-mode delivery.

For each PoC record tested behavior, unsupported cases and a proceed/adapt/drop decision in
this plan or existing project-state docs. Required bridge/window failures require a reported
architecture decision before migration; optional Ctrl/scroll failures do not block mouse input
or reader playback. No independent new markdown tracking files are needed.

PoC 1 progress: the current UIA surface exposes input/messages but no verified stable
conversation ID. A Debug/Exp/environment-gated, current-user-only read-only diagnostic pipe
has been added to inspect real binding metadata before enabling any mutation. Build, 56
probe/IPC assertions and the existing 62 toolbar assertions pass. Live binding identity,
guarded insertion/send and completed-reply delivery are not yet proved.
The corrected 0.2.5 Exp installer and configuration refresh both exited 0; installed version
and audited DLL hash are verified. Foreground access recovered after the desktop obstruction
was removed. A disposable chat was created, preserving existing conversations.
Do not interpret these results as completed PoC 1 or authorize other phases automatically.
Independent review identified two verified medium-severity IPC findings. The user approved
both: reject malformed/disconnected requests locally, and explicitly dispose timed-out pipe
operations. The actual-listener tests now pass 56 assertions, including recovery after idle,
partial, malformed and oversized exchanges and clean shutdown. A captured timeout/EOF race
is corrected by checking cancellation after read/capture. Independent re-review confirms both
resolved with no new high-confidence findings. Both fixes also passed in live Exp: a rejected
request left the listener available; an idle client was terminated after 15,006 ms, and the
next inspection succeeded while the stalled client remained open.
An explicit no-deploy Rebuild corrected a stale packaged version; actual ZIP version and DLL
hash are verified. There is no approval to advance to other PoCs/phases yet.
The disposable chat's binding is `HarnessSessionViewModel`; observed public metadata includes
`Session: IBackendSession`, `Composer: PromptComposerViewModel`, `ChatLog: ChatLogViewModel`
and `IsResponding: Boolean`. The shallow identity whitelist returns no IDs; inspect these
specific binding paths before proposing guarded mutation, rather than guessing a target ID.
A rendered workspace-trust warning initially blocked input/send testing. The user explicitly
approved trust; native Trust once removed the warning. No permanent trust option was selected,
and no prompt entry, submission or reply capture occurred.
The 0.2.6 diagnostic adds one-level metadata inspection only for Session, Composer and ChatLog,
including public inherited-interface identities, with no private/content getter reads or deeper
recursion. Expanded 64 probe/IPC and unchanged 62 toolbar assertions pass; actual package version
and DLL hash are verified. Exp-only 0.2.6 installation/refresh exited 0, with matching installed
hash and a working initial probe on the Sessions list. Independent review found an inherited
binding-property collision: name-only GetProperty can throw AmbiguousMatchException, which
escapes current handlers and faults the listener. Reproduced against the current DLL; live
occurrence has not been established. The user approved appropriate fixes autonomously. An added
regression reproduced the fault against 0.2.6. The 0.2.7 fix resolves nearest declared public
properties, preserves private-getter refusal and contains ambiguity per connection. Expanded
78 probe/IPC + 62 toolbar assertions pass; independent re-review confirms the collision resolved
without new in-scope findings. Exp-only installation/refresh exited 0 and installed hash matches.
Live bindings reveal typed CopilotSessionId (public Guid and ClientId.Id), Composer.PromptInput,
Session.ChatEvents and SessionCommands. These observed contracts are the next bounded inspection
targets, not verified production APIs. Stable identity and guarded input/send/delivery still
require actual evidence. The user also permits copying required VoiceType source when the
microphone phase is reached; no source/models have been copied and no later phase was started.
Reviewed 0.2.7 work is saved in local checkpoint `50938af` with user approval; no push.
The 0.2.8 read-only increment extracts only inspected Guid/client-ID fields and inventories
the two observed composer paths; 84 probe/IPC + 62 toolbar checks pass and independent review
reports no high-confidence findings. Exp installation/refresh exited 0. Live extraction proves
Guid/client identity remains stable through trust, renamed title, submission, switch and reopen;
another conversation has a different Guid. A uniquely targeted, generated UIA prompt was read
back exactly, sent once and answered exactly POC_BRIDGE_OK; cleared draft confirms acceptance.
No existing chats were used, no repository code was submitted, and no voice/pipe mutation API
was enabled. Prompt has a public setter and INotifyPropertyChanged in inspected SDK metadata.
Proceed/adapt: use native UI-thread target/draft-revision/state guards rather than foreground
paste for production. Next PoC work is the guarded command prototype and completed-reply
delivery/lifetime matrix. UIA smoke success does not prove those contracts or complete PoC 1.

### Production phases

1. **Contracts, lifetime and capability probes.** Establish live VS/conversation identity,
   request generations and a current-user-only local bridge with validated messages, correlated
   acknowledgements, process ownership and bounded payloads. Validate native floating tool-window
   creation/no-focus-steal, exact chat input/send targeting and Ctrl key-down/up feasibility in
   Exp before committing to unsupported hooks. Probe completed-reply delivery separately; no
   assumed public Copilot subscription. Stop and report a blocked required capability.
2. **Native reader plus external speech.** Separate the current speech-owning reader UI,
   preserve reusable document/navigation behavior and prototype regression routes, and connect
   a native tool window to the external speech worker. Verify progress/highlighting, seek,
   pause/cancel and stale-event rejection. Verify floating-first and VS layout restoration.
3. **Scoped settings and reader appearance.** Implement scope resolution, storage/migration,
   cross-instance changes, speech controls and themed accessible reader content. Make Settings
   functional independently of Mic/model/skill availability; test two instances of one solution
   with different temporary voices and concurrent preference writes.
4. **External microphone, pill and model discovery.** Selectively reuse VoiceType capture,
   waveform, WAV/transcriber/process lifecycle; add amplitude feedback, engine/model validation,
   notification durations and atomic recording ownership. Verify on real hardware with a
   manually provisioned engine/model. Wire cancellation into owned backend work, not just UI.
5. **Mic gestures and guarded commit.** Connect mouse modes, optional Ctrl-only gesture,
   Esc, audio stop/read-aloud interaction and exact-origin insert/send. Test release-outside,
   startup/release races, draft/busy/focus changes, clipboard recovery and competing instances.
   Never terminate another instance's shared engine to cancel one request.
6. **Production reply delivery, Speaker and history.** Wire the verified explicit bridge/feed,
   stable identities, automatic toggle behavior, three queue policies, history and reply picker.
   A skill-driven explicit delivery path is not a universal automatic capture guarantee. Any
   companion-skill source/installation update is coordinated separately after the real protocol
   exists; do not silently edit the other repository or installed skills. Show honest capability
   status when no reply source is connected, and test missing skill/VSIX/worker independently.
7. **Optional chat-scroll experiment and release hardening.** Attempt precise reply/block
   scrolling; remove/defer it if unreliable. Complete multi-instance, hardware, lifecycle,
   keyboard/accessibility, theme/high-contrast/DPI, deployment and failure-matrix checks.

For every meaningful phase: build only affected projects initially, run the smallest existing
regression route covering it, then have an independent regression audit. Surface findings and
obtain approval for audit fixes; do not silently apply them. Escalate to full suites where shared
behavior changes and at integration milestones. Add harness routes/tests as needed, not a new
framework by default. New-runtime tests need native validation rather than net48-only proxies.
Run the relevant final suite once after the phase's last edit; do not claim manual runtime gates
from synthetic tests alone. Update README and affected design/flow documentation with each part.

## 7. Required negative-scenario matrix

- Missing/invalid/incompatible/unreadable engine or model; empty folder with/without an external
  selection; model/device removed while active; insufficient memory/disk; corrupted settings.
- Permission/device failure, silence, clipping, short press, startup-stop race, release outside
  and out/back, repeated key/click events, simultaneous starts, Ctrl shortcuts, Esc while held
  and during transcription/commit, and results arriving after cancellation.
- Chat recreation/close/switch, draft edits, busy-state changes, insertion verification failure,
  uncertain submission, clipboard contention/failure, incomplete/duplicate/out-of-order replies.
- Sleep/lock/minimize/focus/capture loss; graceful shutdown and worker/backend crash/restart;
  stale recording ownership; multiple VS instances and two instances of the same solution.
- Speech queue changes, manual older-reply browsing, missing Concise, unavailable voice, audio
  output failure and late progress callbacks after seeking, cancellation or reply replacement.
- Temp audio is removed after completion/cancellation and safely scoped crash leftovers cleaned
  on restart. Never log transcripts/audio or ship personal settings, captured replies or models.
  Report cleanup failures without masking the original failure; retain recovery text only in
  the originating session, and clear it explicitly on cancellation/shutdown.
- Preserve the baseline standalone reader and 62-check toolbar route. Existing intermittent
  preference-persistence failure and unapplied glyph-assertion finding remain tracked separately;
  do not call them resolved or silently include their fixes in this work.

## Completion and approval

The deliverable is working behavior verified in Exp, not just builds or disabled controls.
Keep automatic deployment disabled. No main-instance installation, commit or push without
explicit user approval. Only PoC 1 is underway; its authorization is not approval to implement
the entire migration or proceed past an unresolved capability/audit gate.
