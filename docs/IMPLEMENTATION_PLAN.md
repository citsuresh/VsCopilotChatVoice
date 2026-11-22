# Voice input and reader implementation plan

Draft: 2026-11-20. Records the design discussion after toolbar checkpoint `ee55bb6`.
Updated: 2026-11-21. All three bounded PoCs are accepted with recorded limitations and pushed
through `64451c8`; verified metadata-only Phase 1 is committed/pushed as `9fca48c`.
Production decomposition approved 2026-11-21; Phase 2 preparation has started with the detailed
breakdown below approved. Later phase acceptance gates still apply. Implement one independently
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
	 existing engine/model for one end-to-end recording when available. For the currently authorized
   bounded PoC 3, use fake capture only: no recording, microphone, model download or source copying.
   Observe actual startup
   behavior rather than claiming immediate readiness. Do not use global hooks as a shortcut.
   If Ctrl cannot coexist safely, defer this optional gesture and retain mouse-mode delivery.

For each PoC record tested behavior, unsupported cases and a proceed/adapt/drop decision in
this plan or existing project-state docs. Required bridge/window failures require a reported
architecture decision before migration; optional Ctrl/scroll failures do not block mouse input
or reader playback. No independent new markdown tracking files are needed.

The following PoC 1 native-probe increments are historical. The accepted bounded fallback is
the external guarded UIA insertion/read-back and completed-reply observer described in README;
native atomic editing and production automatic delivery remain unsupported. Later "stop" notes
apply to their historical increments, not the currently authorized fake-capture PoC 3.

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

The 0.2.8 typed-identity increment is saved as local checkpoint `1755792`; no push.
The next independently reviewable slice is passive target/state observation before mutation.
Version 0.2.9 observes one exact native root, Prompt notifications without content reads,
trust/responding streams and read-only/transcript flags. Observation epochs are invalidated
by binding replacement, hiding/unloading and terminal feeds; unknown values remain unavailable.
Build and 138 bridge probe/IPC + 62 toolbar assertions pass, including ABA changes and late
callbacks. Independent 0.2.10 re-review and live SDK state validation pass. This does not yet prove
atomic guarded insert/send, complete draft-change coverage in native VS or completed-reply delivery.
Installed SDK metadata confirms the composer lacks PropertyChanged. Report this capability
gap explicitly rather than assume input-replacement notifications; evaluate exact input lifetime
and per-command reference validation before enabling mutation. The root and prompt do notify.
Review reproduced closed-target subscription retention in 0.2.9. The approved autonomous
0.2.10 fix queues teardown on terminal invalidation, handles constructor callbacks, releases
native/model references and preserves primitive terminal diagnostics. The close-without-another-
inspection regression failed before the fix and passes afterward. Independent re-review found
no new in-scope findings and verified actual background bridge disposal. Exp installation/refresh
exited 0 with matching 0.2.10 manifest/DLL. Native trust transitions, A-B-A draft revisions,
busy-to-idle after one exact generated prompt/reply, cleared draft and unchanged chat identity
passed. Leaving history invalidated the target; reopening preserved chat/reply identity with a
fresh observation epoch. No Send retry occurred. Proceed with bounded native command/reply
contract discovery; do not waive the input-replacement gap or infer native command acceptance
from UIA smoke. PoC 1 is incomplete; the pipe remains inspect-only and other phases are not started.

Further read-only contract research resolves the earlier blank property-type lookup: public
metadata requires the installed Copilot contracts plus PublicAssemblies/PrivateAssemblies
dependency folders. In SDK 18.13.0.0, the nonvirtual public PromptInput getter has no setter,
and its direct field-read body references a constructor-only instance field of the same type.
This can inform a fail-closed, per-loaded-SDK stable-input capability check; it is not currently
implemented in the bridge and does not waive root/composer reference or draft-revision checks.
The backend exposes SendAsync(ChatSendRequest, CancellationToken) returning
Task<CopilotMessageResponse>; response Content/Status/Error alone do not establish interaction
identity or acceptance semantics. Native ChatLog.Entries contains ChatContentPartViewModel
with typed InteractionId, IsComplete, IsFromUser and content-part Id. These are metadata only:
no live content getter/feed or backend send was used. Verify the native composer-command path,
acceptance, completed-entry correlation and content behavior before enabling the guarded protocol.
Avoid whole-assembly inventory: unrelated .NET 10 exports require different dependencies;
the reusable helper intentionally reads exact requested types and reports failures explicitly.

PoC 2 result (2026-11-21, 0.3.0, Exp only; the user authorized the next scope): **proceed with caveats.**
Built: a native floating tool window (View > Other Windows > Copilot Voice Reader (Preview)) with Speak/Cancel,
a status line and a sample reply in a themed `FlowDocumentScrollViewer`, driven by an external headless
speech worker (`VsCopilotChatVoice.Reader.exe --speech-worker <pipe>`) over a current-user-only named pipe
(JSON lines; `speak`/`cancel`/`shutdown` out, `progress`/`completed`/`cancelled`/`error` back). devenv hosts
presentation only; synthesis stays in the worker. The host keeps one active generation and drops everything
else, so a late `progress`/`completed` cannot revive cancelled speech; the control re-checks its own generation
on the UI thread. Unrequested `cancelled`, wrong-direction messages, EOF, bad or oversize lines and a worker PID
that is not the pipe client fault the host and tear it down; teardown runs off the UI thread.
Tested: 51 `--speech-worker-probe` assertions (protocol, word model, fake worker, malformed/oversize lines, PID
mismatch, a real silent worker with progress/cancel/replace, kill-worker fault, dispose kills worker). Live in Exp:
the menu command is registered and opens the window floating; the status showed Speaking, "Speaking: word N of
56" and Finished; Cancel gave Cancelled and stayed Cancelled for 4 s while the worker went on; a second Speak
after Cancel ran to word 56 of 56; the user heard the speech and saw the highlight follow it; the Exp activity
log shows the package and window loading with no error from this package; the worker process is gone after
the Exp IDE closes (checked twice); the floating window came back on the next launch (layout restoration for
the floating case).
Not yet tested: docking and pinning by the user, user-activated keyboard focus and absence of activation
stealing, live theme switching (brushes are `EnvironmentColors` resource references, not observed changing),
two IDE instances at once, high DPI, and worker crash recovery in the live window (probed only). Unsupported
or not attempted: speech from a worker the package did not start, and an automatic reply feed (PoC 1 supplies
the identity side). Decision: proceed to the production reader phase on this design; adapt by adding the
docking/focus/theme checks to that phase's acceptance gates. Does not start PoC 3 or any later phase.

PoC 3 result (2026-11-21, 0.3.2, Exp only): **adapt / proceed with the bounded design, keeping optional Ctrl gated.**

- Implemented: pure serialized `CtrlGestureDetector`, Right Ctrl/Hold defaults and configurable Left
  Ctrl/Toggle; immediate capture request on down, repeat/held-input suppression, tentative key/mouse
  cancellation, solitary-up toggle confirmation/stop, release-before-ready deferral and generation-fenced
  ready/stop/result. Esc/focus cancellation prevents later release/results from finalizing or sending.
- Implemented: ACL-restricted current-user `Local\` recording mutex, zero-wait acquire, explicit Busy
  and AcquiredAfterAbandonment, no persisted PID/live-owner takeover, same-thread lifetime and recursive
  local-acquire suppression. Release follows capture-stop acknowledgement; finalization stays serialized.
- Implemented: Debug + exact Exp suffix + `VSCOPILOTCHATVOICE_GESTURE_POC=1` log-only observer on the
  nearest supported chat panel via the toolbar's verified picker. Preview down/up use handledEventsToo;
  no handler changes Handled. Only active-window/inside-chat keyboard focus is accepted. Mouse, focus,
  visibility, unload, binding replacement and deactivation invalidate tentative/active work as appropriate.
  Counters/decisions/state/generation only are logged; no individual keys, chat contents or audio.
- Native adaptation: Chat sometimes suppresses ordinary shortcut/Esc key-down while delivering key-up.
  The 0.3.1 Toggle proof exposed a stop-tap guard gap. 0.3.2 invalidates tentative gestures on other key
  releases, samples still-held inputs at Ctrl release for the opposite release ordering, and cancels on
  Escape release. Actual old/new DLL reproduction changed Stopping/FinalizeCapture to Listening/None.
  No global hook, Handled change, activation delay or promise of early native down cancellation was added.
- Automated: 196 pure/fake/real-process assertions; 209 including actual-DLL WPF observer fixtures.
  Both shortcut release orders, startup races, Esc release at all stages, focus/late callbacks and
  unchanged Handled/focused-scope/teardown are covered. Two real child processes racing elect exactly
  one owner, refuse a live owner without waiting, acquire after release and report a crashed owner as
  abandonment. Prior suites pass: speech51, toolbar62, bridge209, self1,868, integration292/seven mappings.
  An earlier unchanged integration layout-sensitive failure passed reruns; the known preference-save
  flake remains unresolved. Neither is claimed fixed.
- Deployment: official Exp-only 0.3.1 and 0.3.2 installer/configuration routes exited0. Current packaged,
  built and installed 0.3.2 DLL match SHA256 8CE4EB2C72E7F487851C3C00A555E457369EB7CD8BC56F89CB362C6D38594E83.
  Both live task Exp observers attached with zero package errors. No main-instance install/deployment.
- Live Hold (0.3.1, unchanged hold/repeat logic): configurable Left Ctrl because the keyboard has no
  Right Ctrl. 2.67-second/68-repeat and 6.07-second/173-repeat holds each requested once and finalized
  once on release; 241 repeat-ignore decisions across runs. Active focus loss and mouse cancellation
  produced no finalization/result, and a later release was ignored. User confirmed Ctrl+A/C/V work;
  independent toolkit Ctrl+V/A/C scratch tests read back exactly, restored clipboard and sent nothing.
- Live Toggle (patched0.3.2): first tap confirmed Listening; Ctrl+A/C each StopTapInvalidated and retained
  Listening; next solitary tap finalized once. A fresh toggle cancelled on EscapeRelease without
  finalization/result; ordinary mouse and reader Speak/Cancel remained available.
- Live two instances (same solution, patched0.3.2): first owner cancelled on focus loss at09:09:04.346UTC
  before second acquired at09:09:04.988; second cancelled at09:09:06.518 before first reacquired at
  09:09:07.116. Final focus loss cancelled that owner. No overlapping active owners, no late result or
  finalization on focus loss and zero package errors. This proves focus-cancelled ownership handoff,
  not simultaneous live-VS Busy/abandonment; those cases are independently real-process-tested above.
- Unsupported/unmeasured: physical Right Ctrl, early native shortcut/Esc down delivery, actual microphone
  startup latency, capture/transcription/hardware/model failures, cross-logon-session arbitration, live-VS
  crash recovery, missing native key-up, lock/sleep and the broader input/layout/SDK matrix. Fake readiness
  is not microphone latency. No recording, model/download, VoiceType copying, settings UI or insert/send.
- Decision: accept the bounded PoC with the release-side adaptation; keep the optional gesture gated until
  production capture integration and wider native checks establish safe behavior. If a future Chat surface
  suppresses both decisive edges, defer/drop Ctrl and retain mouse delivery/reader playback. This completes
  the authorized fake-capture proof, not production phases or a promise of immediate chord cancellation.

### Production phases

Historical native-probe gate: 0.2.12 withdrew 0.2.11's unfenced native insertion after
independent review reproduced edits and trust/busy changes after final snapshot capture.
The additional Debug/Exp gate accepts only explicit unsupported-atomic-edit refusals; no native
draft getter/setter transaction remains. Client writes/reads share an actual disposal deadline.
Build, 209 bridge/IPC and 62 toolbar assertions plus real client backpressure/output-shape tests
pass. Independent re-review closed both findings and the live task-result leak. Safe Exp tests
verified default refusal/recovery and optional refusal/reconciliation/conflicts without mutation.
No native target was present, so no positive insertion proof is claimed. Native insertion requires
an authoritative atomic API, not copied snapshots or observer locks. Native insertion remains unsupported;
the later accepted PoC 1 proof uses the separately documented guarded external fallback. This historical
gate is not authorization to implement production send, reply feed, microphone or another phase.

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

### Phase 1 implementation decomposition (approved 2026-11-21)

Purpose: turn the reusable proof boundaries into instance/conversation contracts and lifetime
management before migrating reader features. Approval covers starting Phase 1, not automatic
deployment, commits/pushes or skipping later acceptance gates. Retain the existing preview gates.

1. **Typed identities and metadata contracts.** Define a versioned envelope with a fresh
   VS-instance lifetime ID, request correlation ID, native conversation identity and target epoch.
   The existing probe reads `CopilotSessionId.Guid` and `CopilotClientId.Id`; carry both, rather
   than substituting chat titles, solution paths, HWNDs, PIDs or UIA runtime IDs. Keep observation,
   speech and capture generations distinct. Validate required fields, bounds and supported versions;
   reject unknown/invalid identities explicitly. No draft/reply text in this increment.
2. **Originating-target lifetime.** Adapt the conservative discovery and passive observation
   boundaries rather than reimplementing their reflection/notification rules. Register each supported
   chat binding under its verified identity and epoch; refuse ambiguity and stale epochs. Preserve
   unknown trust/responding state rather than assuming ready. Chat/context/input replacement,
   hide/unload, ended notifications and package shutdown invalidate the binding and pending work.
   Marshal WPF operations/cleanup through the existing UI-thread scheduling pattern. A notification
   counter remains advisory metadata, not an authoritative atomic-edit permission.
3. **Bounded, metadata-only IPC.** Add per-instance discovery, handshake and capability/status
   requests with correlated acknowledgements. Reuse proven current-user ACL, bounded framing,
   real read/write deadline and lifetime-disposal patterns. Verify live peer identity for the selected
   endpoint; a PID alone is not a lifetime identity. Malformed/version-mismatched, cancelled,
   disconnected, duplicate and out-of-order requests must not affect another instance or survive
   target/instance disposal. Advertise only actual capabilities: native atomic editing and a production
   reply feed remain unsupported. Do not add insertion, Send, reply capture or speech commands here.
4. **Regression and native acceptance.** Extend the existing executable harness with an isolated
   contract/lifetime route; retain all six documented baseline suites. Test identical solution paths
   in different instance lifetimes, multiple conversation bindings, changed/reused identities, unknown
   state, malformed/oversize messages, timeout/recovery, cancellation and late callbacks. Exercise real
   two-process endpoint isolation. Build with VS MSBuild, bump the manifest for runtime changes and
   validate only in Exp: two chats, two instances, recreation, correct-origin metadata, no focus steal
   and no draft/reply content in logs. Obtain the independent phase regression audit and resolve its
   findings under the approval gate; no main-instance installation, automatic commit or push.

Reuse boundaries confirmed from current source: `ChatBridgePoc.CaptureOnUiThread` reads the
native composite identity and calls `ChatTargetObservation.Snapshot`; the observation manages
binding generations, draft notifications and lifetime cleanup. Shared speech framing already
exists, but its speech-command envelope is not the production chat contract. Extract only a genuinely
shared mechanism when implementation needs it; preserve the existing proof routes and speech behavior.

**Exit gate:** origin/lifetime and metadata IPC pass their regression/native checks without enabling
Mic, changing reader behavior or claiming an insertion/reply authority. Guarded external UIA delivery
remains a separately proved fallback; mapping it to production identities must be validated when
integrated, not assumed from titles/runtime IDs. Only then proceed to Phase 2's real reader migration.
Recording, VoiceType reuse, models, settings, delivery/history and broader UI work stay in later phases.

Phase 1 checkpoint (0.4.0, 2026-11-21): typed metadata contracts, per-target passive observation
epochs, per-process discovery/fresh instance handshake and metadata-only list/status are implemented.
The native adapter reuses the extracted `ChatBridgePoc.CaptureTarget` verification; unknown readiness
is retained. Shared `PipePeerIdentity` and bounded framing serve metadata and existing speech routes.
Frames use strict UTF-8 and reject unknown/duplicate root fields and trailing JSON roots (the framework
reader's trailing-root acceptance was reproduced and fixed). Limits are 65,536 bytes, 32 targets,
64 requests per connection and a 15-second whole-connection deadline. Replay/order fences are scoped
to a connection; a reconnect is fresh read-only metadata, not a production mutation retry protocol.
Provider cancellation is bounded even when a test provider ignores cancellation; its late result
cannot cross connections. No input, content, send, speech or automatic reply operation is exposed.
VS MSBuild and seven routes pass: contracts 136 (131 without DLL), gesture 209, speech 51, toolbar 62,
bridge 209, self 1,868, integration 292. Only the existing toolbar analyzer warning remains.
An unchanged preference-save `File.Replace` I/O failure passed on immediate integration rerun;
it was not fixed here. Independent audit reproduced capture's `AmbiguousMatchException` escaping
per-request containment and permanently faulting the listener; parent confirmed EOF and retry timeout.
The user approved narrow capture-boundary containment and regression strengthening. The failure now
logs and returns `TargetUnavailable`, and next-client recovery passes. Request 64 succeeds, request
65 refuses without capture and closes the connection, and a new client recovers. Independently wrong
acknowledgement request IDs and sequences are rejected. Independent targeted closure ran the full
131-assertion route and closed all three items with no outstanding targeted finding.

Native checkpoint: official 0.4.0 Exp installation/configuration refresh exited 0; the installed DLL
matched the built SHA256 `7D19774A76A06B3DEDBCFB461D61154568FB9DF56CE503D1D3579CB69C8570CA`.
Two task Exp instances opened the same solution, returned distinct instance GUIDs and accepted
fresh handshake/list connections pinned to their respective lifetimes. An Exp A query while Exp B's
settled main window was foreground left that same window foreground. Both package logs have zero
errors before negative status probes, and only counts/decisions, not draft/reply contents. Restored sample readers were hidden;
no speech, input insertion or Send was initiated.

The user-selected populated SDK conversation in Exp A now returns a real session GUID/client ID/epoch,
trusted=true, responding=false and explicit read-only/transcript=false flags. Repeated list/status
queries preserve the epoch and exact status succeeds. Returning to history removes the target and
refuses its old tuple. Reopening the uniquely selected same conversation (identity verified by GUID,
not title) creates a new epoch; the old epoch stays refused and the current one succeeds. A populated
background status query preserves Exp B's foreground. Seven controlled UnknownTarget probes (five
in A, two in B) logged expected InvalidDataException errors; no other package errors appeared.

A test-executable-only --metadata-status route adds five isolated assertions and reuses typed
ExchangeAsync, strict framing, kernel server-PID and pinned-lifetime checks under one 15-second
deadline. VS MSBuild/IDE build and all seven suites pass on the first rerun after this helper;
the installed VSIX DLL hash is unchanged. No new extension deployment/version bump was needed.

Final native proof: both user-prepared live SDK conversations return distinct session GUIDs/epochs
under their own instance GUIDs, with exact status Ok and known trusted/idle/read-only/transcript state.
A's conversation switch changes session/epoch and refuses its old tuple. Both A-to-B and B-to-A
foreign tuples are UnknownTarget while own tuples succeed. Both task instances accepted
CloseMainWindow, completed waited exit and were confirmed absent; their named endpoints then timed
out on bounded connection attempts. Exit codes were not retrieved. Hosting main VS stayed running
and untouched; no forced shutdown or main installation.

Decision: **proceed / accept Phase 1's metadata-only foundation**. The blank New session and some
history-only surfaces exposed no target, cause unconfirmed; retain explicit unavailable behavior
until a supported live SDK binding exists, without inventing identity/readiness. Carry that limitation
into delivery integration rather than auto-send a probe to make a session appear.
Native ambiguity/capacity/timeouts and pending-IPC shutdown are still automated/child-process proof,
not live-VS failure-injection claims. No bridge Send is used to create a session.
Phase 1's regression, targeted audit closure and required native origin/lifetime gates pass. Phase 2
runtime has not started. The user approved the local commit and subsequently the push; `9fca48c`
is verified on remote main. No main-instance deployment is approved.

### Phase 2 implementation decomposition (approved 2026-11-21)

Purpose: migrate the actual single-reply reading experience into the native tool window, with
all synthesis still in the external worker. The user requested starting the next phase after
pushing Phase 1. This breakdown is the confirmation boundary before changing production reader
code; the user confirmed it before implementation. It is not an additional PoC or permission to implement later phases. Implement/review the
parts in order, preserving the standalone reader and the existing ungated sample preview.

**Current-source evidence.** `ReaderPrototype.cs` contains the reusable `WordAnchor`, `Segment`,
`ReadingPosition`, `ReadingNavigation`, `ReplyBlock`, `DocumentBuilder` and `ReplyVersionPreference`
alongside the speech-owning `ReaderWindow`. Version switching cancels the old prompt, maps by
section ID (including differently ordered variants), selects that section's first word and remains
paused. Seek uses segment-relative offsets; live progress adds `promptOffset`. Stop retains the
selected position, while Play after completion restarts at the beginning. The native PoC control
instead renders one plain sample with `ReplyText`, always follows its highlight, and offers only
Speak/Cancel. The current worker protocol has speak/cancel/shutdown but no pause/resume or state
acknowledgements. Its host writes can wait synchronously; do not run those waits on the IDE UI
thread in the migrated reader. Graph queries confirm the original switch/seek flow, but the graph
does not list the newer native reader/worker symbols; current source is authoritative for those.

1. **Shared document/navigation/preference extraction.** Move only the reusable types above into
   shared source compiled by Reader and VSIX, following existing Compile-link patterns. Keep names,
   public signatures, parsing/formatting, sentence detection, spoken step prefixes and section matching
   unchanged. Keep `ReaderWindow`, its System.Speech owner, CLI/window hooks, animations and standalone
   palette in the Reader project. No VSIX reference to System.Speech or the speech-owning executable
   assembly. Reuse the existing preference path/load/save semantics, not a second saved choice or a
   new settings schema. Test reorder/malformed/missing variants, word/prefix offsets and source-file
   immutability before connecting native playback. Characterize existing behavior rather than silently
   fixing unrelated prototype defects or the known preference flake.
2. **External playback and lifecycle.** Extend the speech-only protocol/host/worker narrowly with
   generation-correlated pause/resume commands and state acknowledgements. Pause preserves the actual
   prompt; do not simulate it by cancel/re-speak or claim paused before the worker confirms. Keep
   speak/cancel/shutdown/progress/completed behavior and bounds compatible with prior routes. Serialize
   outbound operations off the UI thread, bound writes/reads and close the actual owned pipe on failure
   or cancellation. The native playback owner tracks reply/version lifetime, segment/selected position,
   prompt-relative base offset and speech generation separately. Completed segments advance exactly
   once; selected-word seeking and navigation cancel the old generation and submit the selected suffix
   only when currently playing. Stop/hide/disposal invalidate queued UI callbacks and retain position;
   finished Play restarts. Version switch cancels, maps by section ID and remains paused until Play.
   A cancelled/hidden start cannot attach a late worker or begin speaking after launch completes. Reject
   stale progress/completion/state acknowledgements at both pipe and UI boundaries; explicit worker
   failure updates status and allows a deliberate retry, never an automatic audio restart.
3. **Native presentation and frame lifetime.** Add the migrated view behind a Debug + exact Exp +
	  launch-only reader-preview gate (`VSCOPILOTCHATVOICE_READER_PREVIEW=1`), retaining the old
   prepared-sample PoC when off until acceptance. Use an explicitly labelled prepared Detailed/Concise
   fixture, not an assumed Copilot reply source or a new text-bearing metadata endpoint. Provide accessible
   Play/Pause/Resume, Stop, distinct sentence/paragraph controls, word seeking, Follow and version choices.
   Load the saved choice before playback; absent Concise shows Detailed with a notice without rewriting
   saved Concise. Explicit choice saves immediately, with visible load/save failures. Tests and native
   proof inject isolated preference paths (constructor/test configuration; any launch override remains
   Debug/Exp-only). All WPF access and cleanup marshal to the UI thread. Manual wheel/scrollbar browsing
   suspends Follow; explicit Follow/seek restores it, without stealing Chat/editor focus.
   Bind content, markers, highlights and accessible states to owning VS/system resources; custom
   appearance/font controls and scoped settings remain Phase 3. Use focused-reader-only keyboard handling
   for transport/navigation and Tab; respect version/control key handling and avoid double invocation.
   Esc and tool-window X stop/hide but retain the reply/position; opening manually never starts speech.
   Distinguish explicit close/hide from docking, tabbing, auto-hide and temporary WPF unload/reparenting;
   do not dispose or change a future Speaker preference merely because a docked view changes visibility.
   VS owns floating-first and later layout persistence; no custom geometry or forced refloating.
4. **Regression, review and Exp acceptance.** Extend the existing executable harness rather than adding
   a framework/project. Add a native-reader route against the actual DLL with injected fake playback,
   dispatcher/lifetime checks and isolated preferences. Extend silent real-worker tests for genuine
   pause/resume (progress stops, same prompt continues), segment/seek offsets, cancellation and independent
   hosts; deterministic fake-peer tests cover late/out-of-order acknowledgements, startup/hide/disposal
   races and stalled writes. Preserve all seven current routes (contracts 136, gesture 209, speech 51,
   toolbar 62, bridge 209, self-test 1,868, integration 292); update counts only from executed results.
   Build with VS MSBuild and DeployExtension=false, bump the manifest for runtime changes, verify the
   packaged/installed version and DLL, then use the official Exp-only installation route. Independent
   regression audit is required; surface its findings and obtain approval before applying audit fixes.
   Native gates: floating first use, manual docking/pinning/tabbing and restored layout, keyboard-only
   transport/focus scope, paused section-aware version switch and reopen, seek/highlight/Follow, theme/
   High Contrast/readability at narrow or high-DPI sizes, no activation theft, close/hide/startup races,
   external-worker cleanup and two same-solution instances with isolated state/preferences/workers.
   Record untested cases honestly; request manual docking/theme/second-instance help only if automation
   cannot prove the case. Do not claim native frame semantics from offscreen WPF tests alone.

**Exit gate:** the migrated single-reply view preserves the prototype's document/navigation/version
and preference behavior, native layout/focus/lifetime gates pass and audio remains external. Required
frame/focus/playback failures need a reported adapt/drop decision, not a workaround affecting the IDE.
Phase 1 metadata stays content-free and disabled toolbar controls stay disabled. No microphone, capture,
VoiceType copying, models, scoped Settings/voice controls, Speaker activation, automatic opening/delivery,
reply history/queue policy or chat-scroll synchronization in this increment. No main-instance install,
automatic deployment, new commit or push without their separate explicit approvals.

Preparation checkpoint: push of `9fca48c` is verified against remote main. No Phase 2 runtime/test/project
file was changed, no Exp launched and no build was needed for this documentation-only decomposition.
CODE_SUMMARY/KEY_FLOWS remain unchanged because no structure or runtime flow has changed.

Part 1 implementation checkpoint (0.4.1): the approved extraction moves the existing seven public types
unchanged to Shared\ReaderDocument.cs, linked into Reader/VSIX without a System.Speech/Reader assembly
reference in VSIX. ReaderWindow/CLI remain in the Reader assembly. --reader-model-probe adds actual-DLL
model/navigation/preference checks; manifest 0.4.1 reflects the expanded assembly. VS MSBuild and IDE
build pass; model 379 and all seven prior routes pass unchanged on the first run. Independent targeted
regression audit reports no outstanding finding. Native sample view, speech protocol and metadata runtime
are unchanged. Official Exp install/configuration refresh exited 0, packaged/installed manifests are
0.4.1 and VSIX DLL/bundled Reader match build hashes. Model 379 against the installed DLL and installed
Reader self 1,868 pass. A fresh task Exp logs package/metadata initialization, serves hello/list and
has zero package errors; graceful close/wait removes the endpoint while protected main stays running.
No speech, draft/Send or microphone started. This is an extraction/load checkpoint, not full Phase 2
layout/focus/playback acceptance. Keep the next parts and their required native gates open. The user
approved the reviewed local extraction checkpoint; no additional push is approved.

Subsequent user approval pushed extraction checkpoint `da8fd78` (verified on remote main) and
continued the next increment. External transport changes are under validation as 0.4.2: asynchronous
bounded commands, cancellable launch/connect and generation/command-correlated real pause/resume.
Legacy preview/probe wrappers remain; migrated native playback ownership/presentation are not yet
connected. VS MSBuild/IDE builds and all eight routes pass: speech 104, toolbar 62, bridge 209,
gesture 209, contracts 136, model 379, self-test 1,868 and integration 292/seven mappings.
The extended route proves raw real-worker pause silence/same-prompt resume, actual five-second
backpressure/state deadlines and cancelled/late startup. Initial regression failures were corrected
by preserving explicit fault refusal and distinguishing actual backpressure from scheduling latency.
Independent targeted audit independently reproduced two introduced regressions: queued replacement
Speak can reactivate after Cancel captures the old generation (High), and legacy Cancel now exposes
TimeoutException outside the existing UI filter (Medium). The user approved both fixes. Cancellation
epochs reject earlier queued replacements/first starts while allowing later deliberate Play; the
legacy Cancel wrapper preserves InvalidOperationException with the timeout as its inner cause.
Exact silent regressions and independent narrow closure pass (104 speech assertions). The unchanged
isolated model File.Replace failed twice, then passed; no preference code/test was changed.
Installed Exp remains 0.4.1; 0.4.2 native loading/full playback acceptance are not claimed. The user
approved a local transport checkpoint and continuation, not a push or main-instance installation.

User request 2026-11-21: after completing all implementation phases, perform a combined cross-phase
review of integration, lifecycle/ownership, compatibility, error paths and regression/runtime evidence.
This supplements existing per-phase independent checks; do not postpone a known blocker until that
final review or mark later phases complete without their own implementation and acceptance gates.

Bounded continuation (0.4.3): transport checkpoint `bdb1328` is committed locally, not pushed. The
parent independently installed a fixed 0.4.2 snapshot in Exp: version/binary matches, peer-checked
metadata hello/list, installed Reader self 1,868, installed-worker genuine pause silence/resume and
clean shutdown, unchanged preview Speaking/Finished twice, zero package activity errors and task-only
VS/worker/endpoint cleanup passed. Native Cancel UI raced silent completion and is not positively
proved. This accepts transport/loading evidence only; it does not accept migrated view/frame behavior.

The next approved owner increment adds `ReaderPlaybackController` and internal typed
`ReaderSpeechBackend`/session adapter, with an actual-VSIX-DLL `--reader-playback-probe` executable
route. It implements distinct reply/version/operation/generation fences, dispatcher-bound model/state
even without a test SynchronizationContext, suffix/step offsets, exactly-once sequential completion,
backend-acknowledged same-prompt pause/resume, paused section-ID version mapping, deliberate error
retry and exact-resource Stop/hide/disposal/startup cancellation. Events coalesce with bounded early
generation slots and one queued dispatcher callback; obsolete launches cannot attach/dispose a newer
session. Tests use internal typed RealProxy fakes and the actual external-worker adapter without new
packages/projects or public APIs. Choice injection uses the existing shared model; visible preference
load/save/controls follow later, with no new schema or preference location.

The native PoC control and standalone ReaderWindow are untouched. Owner automation is not migrated
presentation or native Exp acceptance: part 3 (gated view/frame/keyboard/Follow/theme/preferences) and
the full part 4 native matrix remain open. 0.4.3 is uninstalled; no deployment/UI automation, commit,
staging or push is part of this owner increment. Parent obtains independent targeted review before
continuing. Leave Phase 2's exit gate unchecked until migrated view/native gates pass.

Final owner automation: VS MSBuild with DeployExtension=false passes with the existing toolbar
warning only. After the last source change, actual-DLL playback 190 passed first, followed serially
by speech 104, toolbar 62, bridge 209, gesture 209, contracts 136, model 379, Reader self 1,868 and
integration 292/seven live mappings; all exit codes zero. No preference flake occurred on that run;
the unchanged known File.Replace flake remains unresolved. No 0.4.3 native runtime acceptance or
independent review closure is claimed by these results.
The pre-audit contributor-contract correction narrows startup/state/cancellation exception handlers
to expected operational failures. Unexpected programmer exceptions propagate through faulted tasks,
with precise fake launch/speak/pause/resume/cancel and stale-launch cases; resource/fencing semantics
remain unchanged. No unrestricted production Exception handler or generalized exception framework
is added.

Independent owner review passed playback190 but reproduced one Medium race in both Pause and Resume:
the host cancels a pending state acknowledgement before raising normal completion; cancellation-first
dispatcher ordering produced Error at section0 and discarded legitimate completion. Primary reproduced
both actual-DLL cases. The user approved relevant audit fixes and commits when required, with no push
or main-instance installation approval. Blocked items may be parked while independent work continues;
parked gates are not passed and dependent safety checks must not be bypassed.

The correction uses an internal Task<bool> state outcome: true confirms state, false identifies natural
completion of the exact prompt. Matching requests racing already processed completion also return false;
unrelated stale/cancelled generations still refuse and genuine cancelled acknowledgements still fail.
The controller leaves Pausing/Resuming until matching completion advances exactly once. Both callback
orders, Pause/Resume, final/intermediate sections, duplicates and real-host terminal/refusal behavior
pass. Final VS MSBuild/IDE build and all nine serial routes pass: playback220, speech110, toolbar62,
bridge209, gesture209, contracts136, model379, self1,868 and integration292/seven mappings. No flake.
Independent narrow closure passes playback220/speech110 and extra actual-DLL host/session/controller
protocol injections for pending and completed-before-request Pause/Resume, terminal outcome before
completion, duplicate/final completion, genuine cancellation and Stop/Dispose fencing. No significant
coupled issue remains. This is deterministic ordering/ownership proof, not natural race frequency;
no migrated view or 0.4.3 native acceptance is claimed. Proceed to the gated native-view increment.

Parent frozen-owner checkpoint (0.4.3): official Exp installer/UpdateConfiguration exited 0;
packaged/installed manifests and VSIX DLL/bundled Reader hashes matched the frozen package.
Installed actual-DLL owner220, Reader self1,868 and the actual installed-worker raw 300 ms pause
silence/same-prompt resume/child shutdown passed. Fresh task Exp served pinned metadata hello/list
with no supported targets, restored the old preview without speech/input/Send and logged four package
entries with zero errors. Graceful close/wait removed the task and endpoint while protected main VS
remained responding. Layout was restored, not reset. This proves frozen-owner loading/transport only;
source 0.4.4 is uninstalled and migrated native acceptance remains separate.

Part 3 source checkpoint (0.4.4): the existing float-first ToolWindowPane selects NativeReaderView only
under Debug + exact /RootSuffix Exp + VSCOPILOTCHATVOICE_READER_PREVIEW=1. Gate off preserves the old
PoC control; standalone ReaderWindow/synthesis/CLI are unchanged. A labelled five-section paired fixture,
shared document/navigation/preference types and the audited owner provide accessible native transport,
sentence/paragraph navigation, word seeking, Follow and paused section-ID version switching. Choice is
loaded before any speech; missing Concise does not rewrite saved intent. Explicit radio Click saves
immediately, including already-selected Detailed, with expected load/save/access/invalid-data failures
visible. The existing preference path remains; an isolated launch override is honored only under the
same Debug/Exp gate. Dynamic owning VS/system content/highlight/selection/error/disabled/focus resources
switch to opaque system High Contrast rendering; text and controls wrap. Manual wheel/scrollbar gestures
suspend Follow, explicit seek/Follow restores it, and focused-text-only keyboard handling respects native
control focus, handled/repeat/modifiers and Tab. No Chat/editor scrolling or activation is introduced.

Owner Changed notifications run on its explicit dispatcher; queued drains/autonomous operations and
UI tasks are observed through FileAndForget or injected test reporting, without polling. Unexpected
programmer faults remain task failures and visible/reported; intentionally aborted stale drains do not
produce false errors. An owned IVsWindowFrame2.Advise sink implements Notify/Notify2/Notify3 without
replacing VSFPROPID_ViewHelper. The project's referenced SDK requires OnClose(ref uint), not the earlier
out preflight spelling; incoming save flags are unchanged. Explicit close requests asynchronous Stop
while permitting VS's close contract; Esc requests Stop/Hide without waiting. Show/move/size/dock,
visibility/unload/reparent/tab/auto-hide do not dispose or stop. Missing/failed registration disables Play.
Shutdown disposes the exact owned controller/sink and base pane even if sink retirement fails.

The actual-DLL --native-reader-probe adds 179 synthetic assertions in the existing executable project,
reusing the internal typed playback fake seam and isolated repo-local preference paths. It covers gates,
preferences/failures, silent construction, progress/completion/pending-state presentation, reordered
versions, word/prefix offsets, Follow, keyboard scope, theme/High Contrast/narrow wrapping and synthetic
frame/startup/hide/shutdown races. Playback220 and all eight earlier routes remain required: speech110,
toolbar62, bridge209, gesture209, contracts136, model379, Reader self1,868, integration292/seven mappings.
Run all ten serially after the final edit with VS MSBuild/DeployExtension=false. No new package/project,
Compile-linked controller, schema, microphone, Speaker, reply feed or auto playback was added.

Part 4 remains open: parent independent targeted review and Exp native close subscription delivery/
retention, floating/docking/pinning/tabbing/auto-hide/layout, keyboard focus without activation theft,
paused versions/seek/Follow, live themes/High Contrast/DPI/narrow readability, external-worker cleanup
and two-instance isolation. Synthetic sinks are not proof of native frame semantics. No 0.4.4 deployment,
VS launch, main-instance changes, staging, commit/push or later Settings/capture phase is part of this
source task. Keep the Phase 2 exit gate unchecked; park any native blocker honestly.

Approved part 3 reentrancy correction (0.4.5): independent review found a Medium introduced defect when
Drain published progress synchronously through Changed -> Refresh -> highlighted Run.RequestBringIntoView.
A one-shot handler closing the view retired the prompt, but the same Drain still consumed queued completion
and launched the next section. Before any production correction, the permanent actual-DLL test was built
against the unchanged 0.4.4 DLL and failed with during=Stopped, after=Speaking, launches=2.

The root correction strengthens current-prompt checks with operation/version identity and revalidates after
Notify before completion, automatic Seek restart, backend Pause/Resume submission and accepted-start Ready.
Completion captures its invalidation epoch before callbacks and revalidates after BackgroundWork observers
before advancing. No delay, broad framework, duplicated owner or suppression of legitimate notifications/
completion was introduced. A same-prompt reentrant Pause still processes sticky early completion; later
deliberate Play remains allowed. Tightly coupled view continuation tests also reproduced an older version
save overwriting a newer explicit choice after reentry; view guards now preserve that version intent, skip
shutdown preference writes and avoid stale Follow/navigation or retired-frame hiding after close/disposal.

Native actual-DLL checks now pass 301 synthetic assertions, including synchronous Run close/Stop/Dispose/
version/seek, seek restart, Starting/accepted Ready, pending Pause/Resume, background-task completion
notifications and relevant view continuations. Preserved actual-DLL playback220 and speech110 pass;
all ten serial routes remain required after the final source edit. Manifest is 0.4.5; source is uninstalled.
Independent narrow review closure and all native Exp gates remain pending. These are deterministic synthetic
reentrancy proofs, not native close delivery, runtime race frequency, layout/theme/focus or Phase 2 acceptance.
No parent-owned project-state/scripts/helpers, Phase 4 comparison content, deployment, VS UI, staging or
commit/push is changed by this correction.

Subsequent 0.4.5 result: independent reentrancy closure passed with Stopped/one launch/exact cleanup,
native301/playback220 and no significant coupled issue. Parent official frozen-0.4.5 Exp installation
matched manifests/hashes; installed native301/playback220/Reader self1,868 passed. The restored gated
floating view had enabled Play (frame advice available) and started its actual installed non-silent worker.
Native X removed the floating HWND and worker; the actual View > Other Windows command reopened a new
floating HWND without a worker/autoplay. Graceful task Exp close/wait removed all workers and preserved
protected main VS. Accept that bounded floating close/cleanup/reopen case only; retained position and the
remaining docking/theme/focus/keyboard/version/two-instance matrix are not established by it.

Approved accessibility correction (0.4.6): native UIA found generic fixed status/warning Names and no
usable text/value pattern exposing their actual phase/section/error. Permanent actual WPF peer tests
were first compiled against the unchanged 0.4.5 DLL: status GetName returned Playback status rather than
displayed Stopped/section text, and SelectionItemProvider.Select set Concise IsChecked=true while the
model stayed Detailed and no preference was saved. This confirms the synthetic non-click selection
path; it is not a native toolkit selection proof. That tool refused before action for a missing selection
container (actionAttempted=false). A physical radio click was separately blocked by foreground interference;
native version/input proof is parked, not diagnosed as a package defect.

Status/preference/frame Names now bind directly to displayed Text with their stable IDs and polite live
settings. Word-only progress leaves status Name unchanged. Native radio non-click Checked selection
applies/saves through the existing view/owner; a small private RadioButton subtype distinguishes native
OnClick so Checked plus Click cannot duplicate work, while already-selected Detailed Click still saves
fallback intent. It inherits the native peer/selection role/style/keyboard/focus; no custom provider, polling
or speculative mouse-focus change is introduced. The owner, speech transport and standalone reader are
unchanged. Actual-DLL native checks pass 348 synthetic assertions, covering peer Names for state/section,
visible load/save/frame/operational/unexpected errors, cleared warnings, quiet word progress, native radio
Select/OnClick, paused section mapping/worker retirement, disabled versions and no automatic speech.

Manifest/source is 0.4.6, uninstalled. Prescribed no-deploy VS MSBuild/package and all ten routes passed
serially after the final source edit; native348/playback220/speech110 and prior counts match. Packaged DLL
matches the tested DLL. An initial five-second RealAdapter worker-start timeout left no worker; isolated
playback and the complete serial rerun passed without source/timeout changes. New independent
narrow closure and parent live read-only accessibility proof remain pending. Keep full Phase 2 unchecked
and native version/input/layout/theme/focus cases parked where blocked. No deployment/VS UI/staging/
commit/push, parent state/helpers/index or Phase 4 comparison is changed by this source correction.

Approved directly coupled accessible-choice correction (0.4.7): independent 0.4.6 closure reproduced
Detailed speech -> highlighted Run.RequestBringIntoView -> actual Concise SelectionItemProvider.Select.
Refresh's global flag remained set through Follow and suppressed genuine Checked with no subsequent Click.
The permanent actual-WPF-peer test first failed on unchanged 0.4.6 (DLL SHA256
5143FCE1BBBD3F03FED48D26D11D6A3FA302037B5BE61B06F54E0FADA0F76056) with
checked=True, modelConcise=False, Speaking, cancels=0. No 0.4.6 installation occurred.

Remove refresh-wide suppression. Checked already matching controller mode is model synchronization;
otherwise a live non-native-toggle selection applies/saves synchronously, even within Follow callbacks.
Native Click is never emitted by model synchronization. Limit native Checked/Click deduplication to
VersionChoice.OnToggle, not the full Click call stack, so a later same-radio peer choice during an older
Click is not swallowed. Preserve already-selected Detailed fallback/save and all existing operation/version/
lifetime continuation fences. A tightly coupled permanent test also reproduced selection against an already
disposed controller during final Refresh; mark view intent retired before owner shutdown notifications.
Final/late disabled peer selection refuses, and native late Click/Checked do not submit work/save.

Actual-DLL native412 retains native348 plus 64 synchronous peer/Click Follow, nested newer/same-radio choice,
preference/save-failure, stopped/closed/disposed lifetime, model-sync/no-save and stale-event assertions.
No owner/transport/standalone changes, delayed/deferred intent, broad guard, mouse-focus workaround or
new settings/schema are introduced. Source/manifest 0.4.7 remains uninstalled; independent narrow closure
and parent live read-only accessibility/input proof remain pending. Prescribed no-deploy MSBuild/package
and all ten final serial routes passed: native412/playback220/speech110/toolbar62/bridge209/gesture209/
contracts136/model379/Reader self1,868/integration292/seven mappings. Only the pre-existing toolbar warning
appeared on source rebuild. One initial existing fallback-save assertion passed on rerun with diagnostic-only
test text changed, not production preference/layout code; its cause remains unconfirmed. Preserve parent
state/helper/index and the following Phase 4 comparison; no UI/deployment/staging/
commit/push or later implementation phase is part of this correction.

Final bounded selection/keyboard correction (0.4.8): independent 0.4.7 Follow selection closure passed,
but native Concise.Checked -> actual peer Detailed.Select still lost to older unconditional Concise.Click;
reverse direction failed too. Before production changes, permanent actual-DLL probes on unchanged 0.4.7
(SHA256 6EBB28CEBF4D61D9CF57FE7BA2FCBB670183C837807D8F420638F660E4B2084B) reproduced
version1 -> version2/opposite mode/Paused/cancel1 in both directions. Owned Play/radio Esc returned false,
hide0/Speaking/cancel0, contradicting section 3's focused-reader stop/hide contract. No 0.4.7 install occurred.

Use explicit tightly scoped setter/native event origins, not model equality: VersionChoice consumes each
origin in OnChecked before RadioButton group callbacks, ignoring only native's own paired Checked and the
model write's own Checked. Nested peer events on that same control while OnToggle remains stacked are
genuine, including matching-model persistence. Capture a monotonic request revision before native OnClick;
commit Click only when still current and fence same-version save continuations against newer requests.
No delay, drop of current intent, custom automation provider, broad toggle/refresh suppression, preference
schema, owner/transport change or speculative word-click focus change is added.

Native560 retains prior presentation/lifetime evidence plus both-direction exact two-peer sequences,
longer round trips, third nested native clicks on either radio, later Click peers, nested model Checked/
Unchecked callbacks, genuine same-mode saves, same-version expected save-failure retention, fallback and
disposal. Radio fixtures use actual WPF OnClick, not bare RaiseEvent bypassing native intent tagging.
Unmodified/unhandled/nonrepeat Esc accepts the owned reader surface/text/buttons/radios, stops/hides once
and retains reply/position. All other button keys/Tab and dropdown/edit/scrollbar/slider/outside-view/native
handled-repeat-modifier paths remain untouched; permanent actual-DLL tests include exact cleanup and late
events after every owned-control Esc. These are synthetic controls/frame tests, not native input proof.

Source/manifest 0.4.8 is uninstalled; scoped independent closure and all parked live/manual gates remain
separate. Run prescribed no-deploy MSBuild/package and all ten final routes serially, retaining playback220/
speech110/toolbar62/bridge209/gesture209/contracts136/model379/Reader self1,868/integration292/seven mappings.
Preserve parent state/SCRIPTS/helpers/comparison. No UI/deployment/staging/commit/push, agents or next-phase
implementation occurs here. Parent's intermittent cleared-warning assertion remains cause-unconfirmed;
no blanket retries, silent success, binding workaround or preference/layout edits are introduced.

0.4.8 stop checkpoint: prescribed no-deploy build/package passes; isolated native560 passes. Both final
serial attempts passed playback220/speech110/toolbar62/bridge209/gesture209/contracts136/model379/
Reader self1,868/integration292/seven mappings, but failed native. First precise new-sequence snapshot:
Detailed model/radio correct, saved Concise, visible save error `Unable to remove the file to be replaced.`
The unchanged shared Save's File.Replace is inspected; underlying cause remains unconfirmed. Unchanged
isolated native560 passed again, then the last serial attempt failed its nested peer round-trip saved-choice/
radio assertion; last failure cause is unconfirmed. Stop before further speculative loops, assertion weakening,
binding workarounds or preference edits. All-ten source closure is not passed; 0.4.8 remains uninstalled.

### Phase 3 independent foundation (0.5.0; storage/model only)

**Current continuation (2026-11-21, 0.5.7):** Independent0.5.5 baseline build/settings253/native560/
integration298 passed once. The approved R1/R2 persistence corrections remain closed. The 0.5.7
Settings increment wires the Settings button, scoped speech/audio/appearance persistence, external Preview,
worker-aware audio restoration and native reader runtime appearance. Actual-DLL settings271, toolbar62 and
native560 pass after the increment; the independent audit fixes scope persistence, worker exclusion and
partial-mutation rollback. Exp installation, audible/audio/theme/accessibility acceptance and final commit
remain pending. Keep Mic/Speaker disabled; Phase4 starts only after those Phase3 gates and the local commit.

User-approved adaptation: park native0.4.8's two final verification failures and continue independent
work. Its verified frozen package remains outside the repository and is not installed. Current0.5.0
source retains that native gate unchanged and adds no active UI/audio consumer.

Part1 implements immutable reading-mode/desired-Speaker/voice/rate/volume overrides with explicit
inheritance origins (built-in < global < solution < temporary window), structural validation and
selected-field patches. App-local versioned XML uses global/canonical-solution-hash files, bounded
exclusive empty file-handle locks, latest-snapshot merging, flushed adjacent temporaries and atomic
replacement/move. Window values never persist. First explicit global access imports legacy mode once
through the original helper without rewriting it; corrupt/invalid/access/save/cleanup/watcher failures
surface, not reset/fallback. Watchers invalidate exact relevant scopes without reading or applying values.

Build/package with DeployExtension=false and IDE build pass. --settings-probe passes126 linked/253
combined actual-DLL assertions, including real two-child merged updates/revisions, live-owner contention,
release/crash recovery, five/eight-second lock bounds, strict schema/DTD/size/access negatives, atomic-save
retention/temporary cleanup and watcher lifecycle. All ten preserved routes pass once on0.5.0 source:
native560/playback220/speech110/toolbar62/bridge209/gesture209/contracts136/model379/Reader1868/
integration292/seven mappings. Native560's one pass does not close the earlier native reliability issue.
Fresh independent settings verification then failed at `SettingsRegressionTests.VerifyProcesses`,
the real two-child rate/volume update: `ScopedSettingsStore.Save`/`File.Replace` reported
`Unable to remove the file to be replaced.` The output did not identify the child or linked/actual-DLL
leg. The supplied DLL hash matched; the reviewer ran once without retry and removed isolated roots.
Park the 0.5.0 persistence gate and retain its verified frozen package outside the repository. Its cause
is unconfirmed, including whether it shares any cause with the native preference failures. No speculative
atomic-primitive replacement, retries, acceptance, installation or checkpoint commit is authorized by
the earlier single passing run. Reliable independent closure remains required.

Next independent parts: external-worker actual voice/capability/options contract; additional appearance,
mic/model/user-wide resource fields; scoped Settings UI and safe owning-window synchronization. Voice
availability and supported pitch are not inferred; changing desired defaults cannot activate another
window's Speaker/audio. Storage stays off the UI thread and observer dispatch is asynchronous. No current
reader, toolbar or worker calls the store. Full Phase3/native Exp gates remain unchecked.

**Phase boundary clarification (2026-11-21):** The reader's automatic reopen when a hidden or unpinned
window receives a new reply, and acceptance of Concise/Detailed behavior against live Copilot replies,
require the real reply-delivery path. They are Phase 6/integration acceptance items, not Phase 3 blockers.
Phase 3 may validate settings and persistence with prepared fixtures, but must not claim those live-chat
behaviors until the delivery path exists.

#### Part 2 external read-only capabilities (0.5.2; targeted review closed, unwired)

The Reader-only `--speech-capabilities` branch runs before window construction and owns/disposes
System.Speech without Speak, voice selection or preference changes. One strict bounded schema1 JSON
report identifies engine/installed name+canonical culture+enabled/default metadata, rate -10..10,
volume0..100 and unsupported pitch. Names remain private to local callers, not proof summaries.
Internal DTO/parser and owned-child query are source-linked into Reader/VSIX/Tests; synthesis-bearing
discovery is Reader-only. Remembered-name availability is explicit, without silent substitution.
No current Settings/Speaker/backend consumer, Preview audio or voice-option application is activated.

Limits: 65,536-byte stdout, 8,192-byte stderr, 64 voices, 256-code-unit names, 85-code-unit cultures;
caller timeout above zero/up to30 seconds, two-second shared cleanup wait budget. Direct CLI discovery
has no independent watchdog and synchronous native Start/Kill/dispose calls are not preemptible by
the timer. Do not claim an unconditional whole-call wall-clock guarantee.

No-deploy MSBuild/package and capability136 pass; independent baseline136 also passes. The final
serial settings route failed File.Replace at VerifyStore151; ten other preserved routes pass unchanged.
Native/storage gates remain parked. Earlier first real-child query timed out at its configured ten
seconds; exact elapsed/PID/exit/substage were not recorded. Later direct execution375ms and unchanged
probes passed; cause and reliable cold-start behavior remain unconfirmed.

Independent review reproduced one Medium, high-confidence scheduling defect against the exact DLL:
sole worker occupied3.5s, a100ms query remained incomplete250ms then failed3554ms with Win32Exception,
not TimeoutException. Timeout/cancellation accounting starts inside queued Task.Run; queued work can
outlive the deadline and launch afterward. Authorized narrowly scoped correction: establish deadline/
cancel ownership at API entry, reject expired/retired launch, complete promptly without depending on
a starved pool, preserve exact child cleanup/errors and add permanent isolated actual-DLL starvation
regressions. Target0.5.2; no parked-code edits or installation. Reuse the retained reviewer for narrow
closure after final once-serial verification, without repeating cold/IO failures to green.

Correction0.5.2 captures the API-entry deadline and uses dedicated orchestration/drain threads,
pool-independent deadline/cancel waits and a prelaunch expiry fence. Native calls remain nonpreemptible.
No-deploy MSBuild/package and parent IDE build pass. Final capability152 and retained-reviewer
baseline152 pass once. Independent isolated sole-worker starvation3503ms yields nonexistent-path
Win32Exception15ms (within100ms budget), hanging-child timeout127ms, cancellation127ms, exact child
exit and expired-entry0ms/zero starts even after callbacks release. Original finding CLOSED/high
confidence; no new significant thread/drain/cleanup finding. Supplemental checks blocked by fixture
scheduling/reflection did not execute and were not retried. Zero review children/fixtures remained.
Final settings failed again in its real child at VerifyProcesses270; all ten other preserved routes
passed unchanged. Keep native/storage/cold-start/fullPhase3 gates separate and source uninstalled.

#### Part 3 external speech-option boundary (next independent item)

Implement only the planned voice/rate/volume execution boundary, not Settings/toolbar activation.
Use a speech-free immutable snapshot (empty/default voice, rate -10..10, volume0..100, no pitch)
and a distinct bounded configured-speech command so older workers refuse rather than ignore options.
Apply each explicit snapshot in the external worker at replacement-prompt boundaries; no mid-prompt
changes, Windows-wide setting mutation, missing/disabled/ambiguous voice substitution or default bleed
into legacy Speak. Preserve existing host signatures, framing, generation/command fences, prompt offsets,
true pause/resume, cancellation epochs and legacy CLI/wire behavior.

Validate structural inputs before enqueue, confirm actual installed/enabled unique selection in the
worker and surface generation-correlated errors explicitly. Add a uniquely named typed host entrypoint
without introducing reflection-overload ambiguity. Use minimal correlated applied-option evidence for
silent real-worker tests; queued writes are not a claim that speech/configuration succeeded.
Verify actual voice/rate/volume/default restoration, exact scalar limits, invalid/unavailable selections,
replacement/paused/state/completion/cancellation races and legacy behavior using null output, no names in
proof summaries. No speech-bearing code in VSIX/tests, no store consumer, UI Preview, audio playback
activation, capture/models/source copying or installation. Preserve thirteen existing routes and review
the new boundary independently; native/storage/Exp/fullPhase3 gates remain parked.

Implemented0.5.4 with immutable SpeechOptionsSnapshot, unique Host.SpeakWithOptionsAsync and distinct
speak-options/applied/query framing. Worker retires the previous prompt before mutation, verifies actual
enabled unique voice and getters, reports correlated applied evidence and restores initial voice/rate/volume
for legacy Speak. Ack confirms properties, not successful speech completion. No store/UI/backend consumer.
No-deploy build/package and options422 pass; thirteen preserved routes ran once, settings child File.Replace
failed unchanged, other twelve passed. Initial raw fixture's first-frame assumption was corrected to
current-generation metadata ordering, preserving real generation fences. Independent review is pending;
source uninstalled and fullPhase3/native/storage gates remain open.

Independent once-probe422/source review now passes without significant finding; binary/harness copies
matched and no owned Readers/fixtures remain. Parent IDE build passes. Split186linked/187actual-DLL/
49shared worker-dependency checks. Applied command0 must match pending/current generation and getters;
query positive-command acknowledgements cannot complete a configured start. Empty voice selects initial
identity only; requested rate/volume remain explicit. Legacy restores all three initialization values.
Retirement waits5000ms outside the callback lock; timeout closes the worker, while ordinary option refusal
signals current generation and allows deliberate recovery. Native calls are not preemptible. No actual
voice uninstall/disable, audible, nativeExp, storage or fullPhase3 acceptance is claimed.

### Phase 4 source comparison preparation (read-only; implementation pending)

Local VoiceType revision `7c0b6f8` was inspected without running it, reading personal configuration,
recording, provisioning models or copying source. This is a preliminary source comparison, not a
runtime parity audit or Phase 4 acceptance.

| Area | Observed VoiceType approach | Boundary for this repository |
| --- | --- | --- |
| Capture/WAV/amplitude | NAudio 16 kHz, 16-bit mono capture with PCM RMS and WAV writing. | Potential selective reuse in an external host only; readiness, stop/finalization, bounded data and cancellation still need tests and hardware proof. |
| Transcription/process | External runner, sequential output drains, stdout-derived text and model fallback. | Require bounded cancellable process ownership, concurrent output drains, an explicit successful transcript contract and refusal of missing/invalid selected models. Do not copy silent fallback or binary/model packaging. |
| Gesture ownership | Global hotkey hooks and shortcut suppression. | Retain the chat-scoped detector and nonblocking current-user/session mutex; no global hooks or suppression. Native release-side limits remain explicit. |
| Pill placement/appearance | Compact overlay positions at primary-screen bottom center; invalid color parsing silently falls back. | Anchor the external non-focus-stealing pill to the originating chat and validate placement/DPI; hide when misplaced. Report invalid settings and preserve High Contrast, rather than adopting silent defaults or fixed primary-screen geometry. |
| Clipboard | Text-only backup/restore, swallowed backup/restore failures and blocking retry sleeps. | Do not reuse this helper for guarded insertion/recovery. Preserve verified target/read-back rules, truthful clipboard failure reporting and explicit completed-transcript recovery. |

No tracked LICENSE/NOTICE/COPYING was found during the bounded provenance check. Establish source
permission and required notices before any copying; absence of a license is not permission. Park this
source-reuse gate for user review while continuing independent reader/settings work. Do not edit
VoiceType, the skills repository or installed skills. No capture/transcription/pill is implemented by
this comparison.

**Update 2026-11-21:** the user explicitly granted permission to selectively copy VoiceType source for this
phase (see DESIGN_DECISIONS). The gate is closed; the scope limits above still apply.

### Phase 4a external capture core (0.5.5 baseline; unwired, bounded)

Prove the capture lifecycle independent of any UI, VSIX wiring, model or transcription.

- New external net48 project `src\VsCopilotChatVoice.Capture` (net48 avoids requiring a separate .NET runtime
  on user machines; VoiceType itself is net8.0-windows, so copied code is adapted, not referenced). NAudio 2.1.0
  is the only package; no models, engines or downloads beyond that package.
- Pure `CaptureSession` state machine over an `IAudioSource`: Idle, Starting, Recording, Stopping, Completed,
  Cancelled, Failed. Handle stop before the source is ready, cancel at every state, duplicate Start/Stop, late
  data or completion after cancel (ignored), source failure, and explicit duration and byte ceilings. Cancel never
  yields a result. Cancellation must reach the source and writer, not only the state flag.
- Copied/adapted from VoiceType only: 16 kHz 16-bit mono capture, PCM RMS level meter, WAV session writing.
  Writer targets a caller-supplied isolated path, bounds size, finalizes the header on stop, and deletes the file
  on cancel or failure. Keep provenance comments naming the source revision (7c0b6f8).
- `--capture-probe` in the test exe uses a synthetic source only (no microphone) and covers the lifecycle,
  bounds, WAV correctness (header/length/RMS), cancel cleanup and no leaked files/handles/processes.
- A separate `--capture-hardware-probe`, gated by env `VSCOPILOTCHATVOICE_CAPTURE_HW=1`, records at most 3 seconds
  from the default microphone into an isolated temp file, prints only duration, byte count and peak/RMS level,
  then deletes the file. It is run only with the user's go-ahead; audio and transcripts are never stored or logged.
- Not in this increment: transcription, model/engine discovery, pill UI, gestures, VSIX bundling or wiring, insert/send.

### Independent Phase 6 captured-history data foundation (0.5.3; unused, bounded review passed)

Under the user's park-and-continue instruction, active Settings/appearance depends on the parked
native/persistence gates; VoiceType reuse depends on permission/hardware; production insert/send
and reply feeds remain unsupported. Advance only the independent speech-free in-memory history
model from section3, not those integrations or full Phase6 acceptance.

- Bind one catalog to immutable instance-lifetime/native-session/client conversation identity,
  not a PID, solution path or arbitrary foreground window. Transport epoch/trust/completion proof
  remains the future verified producer's responsibility; this model creates no capture authority.
- Store caller-supplied completed Detailed and optional Concise blocks as defensive immutable
  values. Reuse existing readability/section-pair validation without retaining WPF documents,
  changing standalone parsing or fabricating Concise. Reject incomplete/malformed/wrong-origin items.
- Stable supplied reply IDs deduplicate only identical payloads; conflicting reuse refuses atomically.
  Distinct IDs with identical text remain distinct. Arrival order stays explicit; no persistent text.
- Bound history at64 replies,1,000,000 retained UTF16 text units,128 blocks per variant and100,000
  text units per block. Capacity rejects without eviction/partial mutation; never silently drop.
- Previous/Next/Latest are explicit catalog selection only. Arrivals do not force an older selection
  to jump. Retain a manual-browsing/new-arrival indicator until explicit Latest. Do not activate
  Speaker, auto-play, queue policies, toolbar/view or delivery; those need separate integration.
- Verify linked/actual-DLL immutable snapshots, identity/dedup/conflicts/pairs/capacity boundaries,
  independent same-solution instance models, navigation and clear/disposal. Add one existing-harness
  route, preserve all twelve current routes, then independent bounded review. No new test framework,
  models, recording, source copying, installation or push. This is an unused data foundation only.

Implemented0.5.3 with CapturedReplyCatalog/immutable values plus an internal ReaderBlockValidation
helper preserving old Reader behavior. Catalog/helper link into VSIX/Tests; only helper links into Reader.
Production-only IDs/body/non-step-number restrictions do not change legacy parsing/permissiveness.
Capture validates caller origin/completion, copies/validates both variants, matches section IDs, then
returns Added/Duplicate/ConflictingReplyId/CapacityExceeded atomically. Metadata is nullable/max1024;
IDs/anchors max128, client IDs max256 through the existing BridgeProtocol check. Text budget counts
raw Text.Length across both variants, including markup/whitespace, excluding separately bounded metadata.
Arrivals never move existing selection; Previous/Next mark manual even at bounds; only Latest clears
manual/new-arrival flags. Future consumers must surface enum refusals; none currently exists.

Build/package and parent IDE build pass. History651 (318linked/319actual-DLL/14legacy) and retained
eleven unaffected routes pass; settings fails its existing child File.Replace/VerifyProcesses270, without
retry/fix. Independent once-baseline651/source comparison found no significant issue or legacy order/
message/format/offset change. No review fixture/process was created. This is bounded data review only,
not epoch/trust/native completion, feed, queue policy, Speaker, Exp/audio or fullPhase6 acceptance.

**Reply identity design note (PoC 1 findings, 2026-11-21; design input only, not implemented in the
extension).** `scripts\agent\Read-ChatRepliesUia.ps1` and `Wait-ChatReplyUia.ps1` are read-only UIA
helpers proved on Exp; Phase 6 may reuse their findings, not their code path as a production feed.

- Observed identity inputs: the transcript exposes no stable reply id. A reply is the last assistant turn
  (headed `GitHub Copilot`) up to its completion footer; it can hold several bodies and tool items. Identity
  is therefore content-derived: SHA-256 of the joined body text, plus the conversation identity from Phase 1
  and the turn ordinal seen in that tree window.
- Completion rule: footer present (`PART_LikeButton`, or the cost/model footer text when the Like button is
  absent), Send visible with Cancel offscreen, no later turn, and the same hash on two consecutive polls.
  Never speak a partial stream (one live poll showed a short partial body as Complete while idle, so settle
  across several polls and do not trust a single observation); a pending approval keeps the chat busy, so it blocks delivery rather than
  completing it.
- Duplicates: an identical-text reply (for example `Done`) matches the previous hash, so a hash alone cannot
  prove novelty. Accept it only when the chat was seen busy or a newer turn appeared since the baseline.
  Deduplicate deliveries on (conversation, hash, turn ordinal), not hash alone.
- Limits to carry into Phase 6: the tree is virtualized, so turn ordinals and `MessageCount` shift between
  calls and old replies cannot be enumerated (matches "do not imply access to uncaptured old messages");
  cancelled replies, Deny outcomes, a second chat and very long transcripts are not yet observed; one Exp
  build only; UIA polling is an observation fallback, not the "verified explicit bridge/feed" this phase
  requires. Report honest capability status when only this fallback is available.
- Concise/Detailed variants are not derivable from the transcript; they still have to be supplied by the
  explicit delivery path. The observer never fabricates them.

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
explicit user approval. Bounded PoC 1/2 proofs are accepted with their documented limitations;
PoC 3's fake-capture proof is accepted with release-side adaptation. Physical Right Ctrl and early
native down delivery remain unproved. This is not approval for
production migration, actual microphone work or proceeding past an unresolved required capability.
