# Voice input and reader implementation plan

Draft: 2026-11-20. Records the design discussion after toolbar checkpoint `ee55bb6`.
Updated: 2026-11-21. All three bounded PoCs are accepted with recorded limitations and pushed
through `64451c8`. Production decomposition approved 2026-11-21; Phase 1 is now under implementation
and validation. Later phase acceptance gates still apply. Implement one independently
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
has not started. Changes are manually reviewed and the user explicitly approved a local commit;
no additional push or main-instance deployment is approved.

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
