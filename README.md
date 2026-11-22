# VsCopilotChatVoice

Independent voice input and synchronized read-aloud for Visual Studio Copilot Chat,
with a themed reader and companion skill. Not an official Microsoft or GitHub product.

## Current status: toolbar-shell preview under validation

Bounded PoC 1 uses Exp-verified external guarded-insertion and completed-reply observation
helpers; native atomic editing and a production reply feed remain unsupported. PoC 2
(native reader/external speech) is accepted with documented caveats. PoC 3 (0.3.2) is accepted
with a release-side adaptation: fake Ctrl capture, real two-process ownership and two live
Exp-instance handoff are proved. Physical Right Ctrl and early native shortcut/Esc down
remain unproved. These proofs do not activate the toolbar or implement a microphone.

Production decomposition approved 2026-11-21. Phase 1 metadata foundation (0.4.0) is implemented
and automated checks pass. The approved binding-ambiguity recovery fix and stronger negative
tests passed independent audit closure. Exp installation, native instance checks and the first
conversation's identity/status/epoch checks passed. Two distinct live chats, bidirectional instance
isolation and graceful task-instance shutdown now pass; Phase 1 metadata-only verification is complete.
No production reader migration, microphone, delivery or main-instance installation is enabled.
Phase 1 is committed and pushed as `9fca48c`. Phase 2 preparation has started: section 6 of
IMPLEMENTATION_PLAN.md now contains the approved source-grounded reader-migration breakdown.
It covers shared document/navigation/preference reuse, external pause/resume/seek, native presentation
and Exp acceptance. Part 1 extracts the existing speech-free document/navigation/preference types into
shared source compiled into Reader and VSIX. The standalone window and native sample preview are
unchanged outside the separate native-reader gate. The audited ownership increment is described below;
0.4.4 adds its gated prepared-fixture presentation; 0.4.5 corrects synchronous-notification reentrancy.
0.4.6 corrects accessible status/warning names; 0.4.7 closes Follow selection suppression; 0.4.8 fences
superseded native Click/same-version saves and restores focused reader-control Esc. Full native acceptance remains open.
The user approved/pushed part 1 as `da8fd78`, then requested continuation. Transport 0.4.2 adds
asynchronous bounded speech commands and actual external pause/resume acknowledgements; it is
verified and committed locally as `bdb1328`, not pushed. The independently audited owner increment
(0.4.3, checkpoint `556fdd9`) adds a single-reply playback controller and actual-DLL regression route.
The parent verified frozen 0.4.3 and 0.4.5 packages in Exp. Native 0.4.8 selection/persistence verification
is parked after two final-run failures; its package is preserved outside the repository, not installed.
Current 0.5.7 source retains the independently reviewed external speech-option boundary and captured-history
foundation, and now activates the Settings button through scoped speech/audio/appearance runtime settings.
The external worker remains the speech boundary; Mic and Speaker remain disabled. Settings is a dockable tool window
(`SettingsToolWindow` hosting `SettingsView`, floating by default, View > Other Windows > Copilot Voice Settings, also
opened by the chat-toolbar gear and a gear in the reader header). Like Tools > Options it has no Apply button: checkboxes,
combos and theme save on change and sliders save after a short debounce; Reset is the only footer button. Preview sits
beside voice selection and resolves inherited or engine-default voices. Its layout is titled sections
with a label column and a footer bar (status left, Reset right) styled by `VsThemedStyles`. Preview now keeps the worker
alive until it reports speech completion (the worker acknowledges options before it speaks, so tearing down on that
acknowledgement silenced Preview); the logic lives in `SpeechPreview`. The native reader preview
now follows the approved POC placement (header with Follow at right, version strip top-left, paragraph up/down rail beside
the text, centered Previous/Play/Next with Stop right-aligned beneath, then status) and MDL2 glyphs; a Close (X)
button sits at the far right of the header, and Concise/Detailed is a bordered segmented strip. Settings and the
reader no longer depend on Visual Studio's internal themed-dialog styles: the self-contained `VsThemedStyles`
(Button, IconButton, VersionChoice, RadioButton, CheckBox, ComboBox, ComboBoxItem, Slider) are driven only by
`EnvironmentColors` brushes, and disabled controls dim with opacity instead of painting the stock light plate.
Selected, hover and pressed states keep the normal fill and use an accent (`SystemHighlight`) border, because the
`ToolWindowButtonHoverActive`/`ToolWindowButtonDown` fills render as light plates in the real dark theme; the Follow
tint and the selected Concise/Detailed segment follow the same rule. The enabled chat-pane Settings gear uses normal
tool-window text, while the unconnected Mic/Speaker previews stay grey. Reader controls follow VS theme resources
dynamically and the reader re-applies its theme on `VSColorTheme.ThemeChanged` (a dark-to-light switch previously left
frozen dark brushes on the icon buttons); the Settings checkbox and slider thumb use the theme accent (`AccentMedium`).
Actual-DLL settings271, toolbar62, native626 and speech-options422 pass, including a real-worker Preview check
that the selected voice runs to completion. An audible, temporary real-voice run was heard by the user. Both windows
were reviewed visually outside VS in dark and light with stand-in theme brushes (temporary harness, removed). The
current 0.5.7 build is installed in Exp (installed DLL hash matches the build) and the user confirmed the dark-theme
control look there; light/pink-theme and Settings accent checks are in progress. The Settings view is built in the pane
constructor and its store is created lazily, because VS restores a persisted floating tool window before package
initialization finishes.
package-load verification, audible/audio, theme/DPI/High Contrast/accessibility and final local commit gates remain.


The in-memory captured-reply history foundation from plan section 3 is implemented and reviewed but unused,
while active Settings/native/source-reuse/production delivery gates remain parked. It enables no reply feed,
Speaker, queue policy or UI, and is not full Phase 6 acceptance.

In Visual Studio, use **File > Open > Project/Solution** and select
`VsCopilotChatVoice.slnx` (rather than **Open Folder**). No restart is required.
The solution contains:

| Project | Responsibility |
|---|---|
| `src\VsCopilotChatVoice.VisualStudio` | Thin VSIX (0.5.7 source, uninstalled) with Settings wired through scoped speech/audio/appearance runtime settings, Mic/Speaker disabled, preserved reader PoC, gated native paired-fixture view/frame, independent gesture/metadata gates and bundled external speech worker. |
| `src\VsCopilotChatVoice.Reader` | Independent x64 WPF reader with Windows speech, word highlighting, sentence/paragraph navigation, pause/resume, Stop, Follow and Concise/Detailed selection; also runs headless as `--speech-worker <pipe>` or read-only `--speech-capabilities`. |
| `src\Shared` | Speech-free reader model/pure block validation, speech framing/options, capability contracts/query and unused scoped-storage/captured-history foundations compiled into their consumers. Not a separate assembly. |
| `tests\VsCopilotChatVoice.Reader.Tests` | Executable reader, toolbar, bridge, speech-worker/capability/options, gesture, metadata-contract, shared-reader-model, actual-DLL playback/native-presentation, scoped-settings and captured-history regression routes; not a Test Explorer adapter project. |

The reader and tests were migrated from the audited session prototype, not from
VoiceType's dictation engine. The `ReadAlongTrial` namespace and sample labels are
intentionally retained during this foundation migration to preserve behavior.
Preliminary read-only VoiceType capture/runner/gesture/pill/clipboard comparison is recorded in
IMPLEMENTATION_PLAN.md section 6. Source permission/notices are unresolved before selective copying;
this is not runtime parity proof and no VoiceType source, capture or models are included.
The standalone reader still uses the prototype's pale-yellow palette. The gated native view uses
 dynamic owning VS/system resources; native reader controls and radio labels inherit the resolved
 Light/Dark/VS/High Contrast text and background brushes.
The 0.2.3 VSIX adds a conservative internal-WPF toolbar shell:
**Mic | Speaker toggle | Settings | Customize AI**, inside Chat below its message box,
not in the main IDE toolbar. All three new controls are disabled with explanatory tooltips
and accessible names; the unchecked Speaker is a preview, not a saved-state indicator.
The compact, flat icon buttons use toolbar styles and share a subtle rounded border whose
color follows VS theme resources; disabled glyphs use the system's gray-text resource.
The group is 24 DIPs high with no vertical group padding, 28-by-22-DIP buttons and unchanged
14-DIP glyphs, tightening the border to match the neighboring chat controls more closely.
Standalone Reader theming is unchanged. No dictation, automatic
speech, settings UI or delivery bridge is connected. Build and deterministic checks pass;
The user approved the 0.2.3 appearance in Exp; the full chat-recreation/theme/DPI/accessibility
matrix remains pending. Foundation 0.1.0 loading and initial
0.2.0 toolbar attachment were verified; the latter exposed dialog-style minimum-width expansion,
now covered by compact-dimension regression checks.
No main-instance deployment was performed.

### Native reader tool window PoC 2 (0.3.0 preview, Exp-verified)

**View > Other Windows > Copilot Voice Reader (Preview)** opens a floating tool window with Speak, Cancel,
a status line and a sample reply. Speak starts the bundled `VsCopilotChatVoice.Reader.exe --speech-worker <pipe>`
(on the first click) and the window highlights each word as the worker reports progress; Cancel stops speech
and ignores late worker messages. The IDE does not synthesize speech. It shows a prepared sample, not a
Copilot reply, and is not connected to the toolbar, the bridge or any microphone.
`VSCOPILOTCHATVOICE_SPEECH_SILENT=1` (set before launching VS) makes the worker synthesize to a null output for tests.
Verified in Exp on 2026-11-21: the command appears and opens the window floating; Speaking / "word N of 56" /
Finished / Cancelled status transitions; a second Speak after Cancel; audible speech with the highlight
following it (confirmed by the user); no error from this package in the activity log; the worker exits when
the Exp IDE closes; the floating window is restored on the next launch. Not yet tested: docking and
pinning, keyboard focus, live theme change, two instances, high DPI. See IMPLEMENTATION_PLAN.md section 6.
Installed only into Exp; no main-instance install.

### Ctrl gesture PoC 3 (0.3.2 preview; accepted with adaptation)

The gesture proof is preserved in 0.4.0; its optional gate remains independent of metadata IPC.

Only Debug + `/RootSuffix Exp` + launch-process `VSCOPILOTCHATVOICE_GESTURE_POC=1`
enables this log-only observer. Right Ctrl/Hold defaults; launch-only
`VSCOPILOTCHATVOICE_GESTURE_LEFT=1` selects Left Ctrl and
`VSCOPILOTCHATVOICE_GESTURE_TOGGLE=1` selects toggle for the proof. No setting is persisted.
The nearest supported chat panel contains the picker, native input and transcript. Preview
handlers use `handledEventsToo: true`, accept only active-window/inside-Chat keyboard focus,
and never change `Handled`. Logs contain counters and decisions/state/generation, no individual
keys, chat contents or audio. Toolbar controls remain disabled and reader playback independent.

The pure detector requests fake capture immediately on down, suppresses repeats/held inputs,
finalizes hold on release (deferred until ready if release wins startup), and confirms toggle
on solitary up. Other keys/buttons invalidate a tentative gesture without swallowing shortcuts;
Ctrl+A/C keeps an established toggle running. Esc/focus cancels all phases and rejects later
readiness/releases/results. Native Chat sometimes suppresses shortcut/Esc key-down: 0.3.2
also invalidates on other releases, rechecks still-held inputs on Ctrl up and cancels on Escape
release. This is a release-side fallback, not a promise of immediate native chord/Esc cancellation.

The fake adapter queues ready/stopped/result acknowledgements; it never records, transcribes,
inserts or sends. A current-user ACL-restricted `Local\` mutex acquires without waiting,
refuses a live owner, reports abandonment and has no persisted PID. Acquire/release/dispose
stay on one thread, with local recursive-acquire suppression. Release follows fake stop
acknowledgement; finalization is independently serialized. `Local\` covers the current Windows
session, not separate logon sessions.

Verified 2026-11-21: all six suites pass, including 209 full gesture assertions (196 without
VSIX fixtures), real two-process contention/race/abandonment, startup-release and late-result
races, and actual-DLL observer focus/Handled/teardown checks. Exp-only installer/configuration
refresh exited 0; installed 0.3.2 version/DLL matched the package. Live configurable Left Ctrl
Hold/repeat/release and focus/mouse cancellation passed; normal Ctrl+V/A/C read back exactly
with clipboard restoration and nothing sent. Patched Toggle kept running through Ctrl+A/C,
then stopped on a solitary tap; Escape release cancelled a fresh toggle. Two task Exp instances
cancelled/released before the other acquired and allowed later reacquisition, with no focus-loss
result/finalization. Both logs have zero package errors. Reader Speak/Cancel remained usable.

Decision: **adapt / accept this bounded proof; keep optional Ctrl gated**. Physical Right Ctrl
(the test keyboard lacks it), early native key-down cancellation, actual capture startup/hardware,
missing key-up, lock/sleep, cross-session and live-VS crash recovery remain unproved. Live two-VS
proof is focus-cancelled handoff; simultaneous Busy and abandonment are real child-process tests,
not live-VS crash claims. No recording, models/downloads, VoiceType copying, global hooks,
production settings or production delivery. See IMPLEMENTATION_PLAN section 6 for detailed evidence.

### Phase 1 metadata foundation (0.4.0 preview; verified in Exp)

Debug + exact `/RootSuffix Exp` + `VSCOPILOTCHATVOICE_METADATA_POC=1` enables a separate
metadata-only endpoint, `VsCopilotChatVoice.Metadata.<VS-PID>`. A fresh instance GUID is returned
by `hello`; a PID/solution path alone is not a lifetime identity. `list` and `status` use correlated
request IDs, strictly increasing per-connection sequences and the pinned instance GUID. A status
target includes native session GUID + client ID + observation epoch; stale/missing/ambiguous targets
are explicitly refused. Unknown trust/responding remains unknown, not ready. Chat/input/context
replacement and hide/unload/ended observations invalidate epochs; shutdown closes owned pending IPC.

Audit reproduced a binding `AmbiguousMatchException` permanently faulting the listener. The approved
fix contains it at the capture boundary, logs the failure and returns `TargetUnavailable`; the next
client can recover. Independent targeted closure also passed the request 64/65 capacity boundary
and independently wrong acknowledgement ID/sequence tests, with no outstanding targeted finding.

Current-user ACL and kernel client/server PID checks bind the selected endpoint; instance/request
GUIDs are correlation fences, not credentials. Limits: 65,536-byte strict UTF-8 frames, 32 targets,
64 requests per connection and a 15-second connection deadline. Duplicate request IDs are rejected
within a connection; reconnects start a fresh metadata-only handshake. This is not a mutating-command
deduplication guarantee. Bad schema/version/fields, capacity, timeouts and provider failures surface
explicitly; late provider callbacks cannot deliver into a new connection. No draft/reply text is in
the protocol or logs, and no edit, Send, reply-feed or speech command exists. Toolbar stays disabled.

`--contracts-probe [VSIX-DLL]` passes 131 assertions without the DLL, 136 with actual-DLL gate/schema
checks; real child processes prove independent lifetime/peer isolation. All six prior routes also
pass; an unchanged preference-save `File.Replace` I/O failure passed on immediate integration rerun.
The official 0.4.0 Exp installer/configuration refresh exited 0 and the installed DLL matched the build.
Two task Exp instances of the same solution returned distinct lifetime GUIDs and accepted pinned
handshake/list reconnections. Querying background Exp A preserved Exp B's foreground window;
before negative probes both package logs had zero errors; logs contain no chat contents.
No speech was started; restored sample reader windows were hidden.

Exp A's user-selected populated SDK conversation exposes session GUID/client ID/epoch, known trusted
and idle state, and explicit read-only/transcript flags. Repeated reads keep the epoch; exact status
returns `Ok`. Wrong epochs are refused. Returning to history retires the target; reopening the same
verified session creates a new epoch, refuses the old one and accepts the current one. A populated
background status query also preserves Exp B's foreground. Seven deliberately refused status probes
produced expected `InvalidDataException` log entries (five A, two B); no other package errors appeared.

Both user-prepared live SDK chats now expose distinct session GUIDs/epochs under their respective
instance lifetimes. Both exact statuses succeed; changing A's conversation refuses its old tuple,
and each instance refuses the other's current tuple. Both task Exp instances accepted graceful
`CloseMainWindow`, completed waited exit and disappeared; neither endpoint accepted a later client.
The hosting main VS stayed running and untouched. Exit codes were not retrieved for these closes.

Decision: accept this metadata-only foundation. Blank new-session and some history-only surfaces
were observed with no targets; their cause is not established and they remain unsupported until a
live binding is available. Do not infer readiness or fabricate identity for them. Live failure injection
for ambiguity/capacity/timeouts/pending-IPC shutdown remains untested; isolated automated fixtures
cover those cases. Phase 2 and production delivery remain separate gates. `--metadata-inspect <Exp-PID> [instance-guid]`
performs a read-only handshake/list, checks the kernel server PID and optional expected lifetime,
and prints bounded metadata JSON. No capture, speech or UI input is performed by this helper.
`--metadata-status <Exp-PID> <instance-guid> <session-guid> <client-id> <epoch-guid>` performs the
same bounded, peer/lifetime-checked handshake followed by an exact-target status request. It prints
the correlated response and exits 0 for `Ok`, 1 for refusal/failure. It never edits or sends.

### Phase 2 part 1 shared reader extraction (0.4.1; verified)

The approved first increment moves the original document/section/word/navigation/preference types
to `src\Shared\ReaderDocument.cs`, compiled into Reader and VSIX. Public names/signatures and
standalone assembly identity are preserved. `ReaderWindow`, System.Speech, CLI/window lifetime and
the original palette remain external; VSIX has no System.Speech or Reader assembly reference.
The native tool window still shows its original sample, with unchanged Speak/Cancel behavior.

`--reader-model-probe <VSIX-DLL>` passes 379 assertions against both built and installed DLLs:
type ownership/dependencies, document/sample parity, reordered/malformed versions, spoken step
prefixes, word/sentence/paragraph/text-pointer positions and isolated cross-consumer preferences.
All seven earlier routes pass with their unchanged counts. VS MSBuild and IDE builds pass with
only the existing toolbar warning. The independent targeted regression audit has no outstanding finding.

The official 0.4.1 Exp install/configuration refresh exited 0. Packaged/installed manifests, VSIX
DLL and bundled Reader matched the build; installed Reader self-tests pass 1,868 assertions.
A fresh task Exp loaded the package and metadata gate, served a bounded hello/list and logged zero
package errors. It closed gracefully; its endpoint disappeared while protected main VS remained.
No reader speech, chat insertion/Send or microphone was initiated. This validates extraction/loading,
not full Phase 2: external pause/resume/seek, migrated controls, frame/layout/focus/theme/DPI and
two-instance playback acceptance remain ahead. The new reader-preview gate is still only proposed.

### Phase 2 external transport increment (0.4.2; automated checks and audit closure pass)

The worker now supports real prompt-preserving pause/resume with generation/command-correlated
acknowledgements. New host entry points serialize asynchronous writes with a five-second deadline,
cancel pending state requests and support cancelled startup; legacy preview wrappers remain.
`--speech-worker-probe` passes 104 assertions, including raw worker silence while paused, same-prompt
resume, missing acknowledgements, cancelled/late startup and actual non-reading-pipe backpressure.
VS MSBuild/IDE builds and all eight routes pass. Both independently reproduced audit findings were
fixed with approval: a cancellation epoch rejects earlier queued starts (including idle first starts),
and legacy Cancel preserves its existing exception contract. Exact silent regressions and independent
closure pass. The unchanged isolated model preference File.Replace failed twice before a passing
rerun; no preference fix was made. The verified transport is local checkpoint `bdb1328`, not pushed.
The parent independently installed a fixed 0.4.2 package snapshot in Exp: installer/configuration
refresh exited 0; manifests/DLL/Reader matched; peer-checked metadata hello/list, installed Reader
self 1,868 and actual installed-worker pause silence/resume/clean shutdown passed. The unchanged
native preview reached Speaking/Finished twice. Its Cancel UI attempt raced silent completion and
did not prove native Cancel; no package defect is inferred. Task Exp/worker shutdown and endpoint
refusal passed, package activity errors were zero, and main VS stayed untouched. This is transport/
old-preview evidence, not migrated controller/view/frame acceptance. Installed Exp is 0.4.2; the
0.4.3 increment was still uninstalled at that transport checkpoint; the later frozen-owner proof is below.
Finish Phase 2 before Settings; a combined cross-phase
review follows all implementation phases.

### Phase 2 playback owner increment (0.4.3; unwired)

`ReaderPlaybackController` owns one supplied reply on an explicit WPF dispatcher. It separates
reply/version/operation lifetimes, segment/selected-word position, prompt-base offset and external
speech generation. Play reads the selected suffix; sequential completion advances exactly once
and finished Play restarts. Seek/sentence/paragraph navigation speaks only while already speaking.
Pause/Resume publishes final state only after backend acknowledgement and retains the same prompt.
Version switching cancels, maps reordered variants by section ID, selects the first word and stays
paused. Stop/hide retains position; disposal/startup/queued callbacks cannot revive playback.
Expected operational failures are explicit and require deliberate Play to retry. Unexpected
programmer exceptions fault the returned task rather than becoming success-completed Error results.
Coalesced worker events are bounded,
including progress/completion before asynchronous Speak returns; obsolete launches dispose only
their own returned resources. Later starts await retirement of the owner's previous session
asynchronously; ignored-cancellation obsolete launches do not block or own newer sessions.
A typed internal adapter owns the real external worker, with no
System.Speech or Reader assembly reference in VSIX.

`--reader-playback-probe <VSIX-DLL>` loads the actual controller and adapter from that DLL. Internal
typed fake sessions (BCL RealProxy) exercise ordering, races, dispatcher affinity without a test
SynchronizationContext, independent owners and exact cleanup; silent real workers exercise the
actual adapter, same-prompt pause/resume, suffix offsets and independent-process teardown. No new
project/package, settings schema or preference location is added. The owner accepts the already
loaded choice; the following view increment will use the existing shared preference model.
No visible reader, tool-window frame, native preview control, toolbar, focus/activation/scroll
behavior or deployment is changed. Native layout/theme/keyboard/Follow and 0.4.3 Exp gates remain
open; synthetic/controller/child-worker checks do not establish native acceptance. Independent
targeted review is obtained by the parent before further migration. Final VS MSBuild passes with
only the existing toolbar warning. After the approved completion-race correction the new route passed 220 assertions
first, then all eight preserved routes passed serially: speech 110, toolbar 62, bridge 209, gesture
209, contracts 136, model 379, Reader self 1,868 and integration 292/seven live mappings. All exit
codes were zero on that final run; no preference flake occurred. The known preference File.Replace
flake remains unchanged/unresolved, not fixed by this increment.
The exception-boundary fixtures distinguish expected operational failures from unexpected programmer
exceptions in launch/speak/pause/resume/cancel, including stale-launch fencing and exact cleanup.
Independent review passed the original 190-assertion route but reproduced a pending Pause/Resume
cancellation arriving before normal completion and incorrectly producing Error instead of advancing.
The approved correction carries an explicit internal state result: confirmed state versus the exact
prompt's natural completion. Completion advances independently of callback ordering; genuine cancelled
acknowledgements remain errors. Tests cover both orders, final/intermediate sections, duplicates and
requests racing completion. Independent closure passes playback220/speech110 and additional actual-DLL
host/adapter/controller protocol-injection checks. No significant coupled finding remains. Native
view/frame integration and its Exp acceptance are still required; this historical owner checkpoint is unwired.

The parent subsequently verified frozen 0.4.3 in Exp: official installer and UpdateConfiguration
exited 0; packaged/installed manifests and VSIX DLL/bundled Reader hashes matched. Installed actual-DLL
owner checks passed 220 and Reader self-tests 1,868; the actual installed worker passed 300 ms raw
pause silence, same-prompt resume and child shutdown. Fresh task Exp served pinned metadata hello/list
(no supported targets), restored the old preview and logged four package entries with zero errors.
No old-view speech, draft input or Send occurred. Graceful task close/wait removed its process/endpoint;
protected main VS remained responding. This accepts frozen-owner loading/external transport, not migrated
view/frame/keyboard behavior. The restored layout was not reset. Current source 0.4.8 is uninstalled.

### Phase 2 part 3 native presentation/frame increment (0.4.8; gated, synthetic checks)

Only **Debug + exact `/RootSuffix Exp` + `VSCOPILOTCHATVOICE_READER_PREVIEW=1` at launch** selects
the new single-reply view in the existing **Copilot Voice Reader (Preview)** tool window. Gate-off
retains the original Speak/Cancel view unchanged. The new five-section Detailed/Concise fixture is
explicitly labelled prepared content, not a Copilot reply/feed or text-bearing metadata endpoint.
No constructor, version selection, manual open or reopen starts speech. Synthesis remains external.

Native controls provide Play/Pause/Resume, Stop, labelled sentence/paragraph navigation, selected-word
seek, Follow and versions. The view loads the existing `%LOCALAPPDATA%\ChatVoice\reader-version.txt`
before playback. Explicit choice saves immediately, including clicking already-selected Detailed after
a missing-Concise fallback. Missing Concise retains saved intent with a notice; expected invalid-data,
I/O and access failures are visible without silently resetting the file. Tests inject isolated paths.
For later Exp proof, launch-only `VSCOPILOTCHATVOICE_READER_PREFERENCE_PATH=<isolated absolute path>`
is honored only under the same Debug/Exp/reader gate; no new preference schema or Settings UI exists.

Manual wheel/scrollbar browsing suspends Follow; explicit Follow/seek restores it. Scrolling only targets
the reader, never focuses/activates Chat or an editor. The wrapping document/toolbar and native Tab-able
controls use dynamic content, marker, highlight, selection, error, disabled and focus resources; system
High Contrast overrides the owning VS colors. Focused reader text/surface only: Space transport,
Left/Right sentence, Up/Down paragraph and Home/End bounds. Unmodified, unhandled, nonrepeat Esc also
stops/hides from owned reader buttons/radios. Dropdown/edit/scrollbar/slider focus keeps native Esc;
buttons/radios keep every other key and Tab, and outside-reader keys are untouched. No global handlers.

The same `ToolWindowPane` remains registered float-first; VS persists layout, with no forced geometry
or refloating. An owned `IVsWindowFrame2.Advise` sink implements Notify/Notify2/Notify3 close notification,
without replacing `VSFPROPID_ViewHelper`. The referenced compile-time SDK requires `OnClose(ref uint)`;
incoming save options are preserved. Explicit X requests asynchronous Stop while allowing VS's close
contract; Esc requests Stop/Hide without waiting for cancellation. Reply/position survive. Show/move/size/
dock notifications, WPF unload/reparent/visibility, tabbing and auto-hide are not disposal or automatic
Stop. Missing/failed close registration visibly disables Play. Actual pane shutdown disposes its owner.
Dispatcher-bound owner changes update controls without polling; queued drains, autonomous advancement,
cleanup and UI event tasks are observed through FileAndForget or the injected test reporter. Unexpected
failures remain faulted and visible/reported; intentionally aborted stale drains do not create false errors.

`--native-reader-probe <actual VSIX DLL>` has **611 synthetic assertions** using the actual view/controller;
an isolated run passed, but the final serial native gate is blocked as recorded below. It uses
and the existing internal typed playback fakes, not a Compile-linked owner. It covers gate/preference
refusal/failures, silent construction, state/progress/completion, reordered paused versions, exact word/
prefix/navigation offsets, Follow, focus/handled/repeat scope, dynamic resources/High Contrast/narrow
wrapping, startup/hide/close/shutdown races and synthetic frame subscriptions. The route resolves existing
SDK prerequisites and removes its isolated repo-local preference directory. All fourteen current routes are listed below.
Independent review reproduced a Medium synchronous-notification defect in 0.4.4: queued progress and
completion highlighted a Run whose RequestBringIntoView handler called FrameClosed. Close reached Stopped,
but the old completion then restored Speaking and launched a second worker. The permanent actual-DLL
test first failed against the unchanged original DLL with exactly those states/two launches.
The approved 0.4.5 correction revalidates prompt/version/operation after presentation and background-task
observers, including completion, automatic Seek restart, pending Pause/Resume submission and accepted
start Ready. Tightly coupled view continuations also retain newer explicit version preferences and avoid
navigation or frame hiding after synchronous close/disposal. No delay, notification suppression or duplicate
owner is added. New synthetic cases cover synchronous close/Stop/Dispose/version/seek at these boundaries,
valid same-prompt early completion and later deliberate Play; playback220/speech110 remain unchanged.
Independent 0.4.5 reentrancy closure passed (Stopped, one launch/exact cleanup; native301/playback220),
with no significant coupled issue. Parent official frozen-0.4.5 Exp installation matched manifests/hashes;
installed native301/playback220/Reader self1,868 passed. Its restored gated floating view had enabled Play
(frame advice available) and started the actual installed non-silent worker. Native X removed its floating
HWND and worker; the actual View > Other Windows command reopened a new floating HWND without a worker
or autoplay. Graceful task Exp close/wait left all workers absent and protected main VS intact. This proves
that bounded floating close/cleanup/silent-reopen case, not retained-position, docking or the full native matrix.

Parent native UIA exposed a new defect: status and warnings had constant generic Names with no usable
text/value pattern for their actual content. Permanent actual-WPF-peer tests first failed on unchanged
0.4.5: status Name was `Playback status`, not its displayed Stopped/section text; SelectionItemProvider.Select
checked Concise but left the model Detailed and saved no choice. Source 0.4.6 binds status/preference/frame
Names directly to displayed text, preserves IDs and polite live settings, and avoids word-progress Name
changes. Genuine non-click Checked selection applies/saves the version; a small native RadioButton subtype
keeps Click handling once-only, including already-selected Detailed fallback/save. Its inherited native
automation peer/role, keyboard/focus and styling remain; no mouse-focus workaround or custom provider exists.
Synthetic peer tests cover state changes, visible load/save/frame/operational/unexpected errors, cleared
warnings, selection during speech, disabled variants, native Click/selection deduplication and no auto speech.

Independent 0.4.6 closure found a Medium directly coupled defect: Refresh kept its suppression flag through
Follow, so a synchronous highlighted Run.RequestBringIntoView callback selecting Concise through its actual
WPF peer checked the radio without changing the model, pausing or saving. The permanent test first failed
against unchanged 0.4.6 with checked=True/modelConcise=False/Speaking/cancels=0. Source 0.4.7 removes
refresh-wide suppression: Checked matching the controller is model synchronization, not an explicit choice;
genuine peer changes and Click apply synchronously. Native Checked/Click deduplication covers only OnToggle,
not the Click handler's reentrant callbacks, retaining exactly-once Click and already-selected fallback save.
The view retires intent before controller shutdown notifications; final/late disabled peer selection cannot
submit work to a disposed owner. New tests cover peer/Click Follow reentry, nested newer choice/preference,
same-radio selection while an older Click is on the stack, Stop/close/disposal, expected save failure, silent
model synchronization and stale progress/completion. The audited owner/transport/standalone are unchanged.

The 0.4.7 Follow case passed independent closure, but a newer matching-model peer choice from native
Checked still lost to the older unconditional Click. Permanent original-DLL tests failed in both directions:
version1 -> version2/opposite mode/Paused/cancel1; owned Play/radio Esc also returned false with no hide.
Source 0.4.8 uses explicit originating-event model/native Checked markers, consumed before RadioButton
group callbacks, rather than mode equality or a toggle-wide guard. A request revision captured before
native OnClick rejects superseded Click and same-version save continuations. Genuine same-mode Checked
persists immediately; only native's own paired Checked waits for its once-only Click. Tests cover exact
two-peer and longer round trips on the original control, third nested native Click, later Click peers,
model Checked/Unchecked callbacks, same-version expected save-failure retention, fallback and disposal.
Scoped Esc tests cover every owned control/surface/text, exact hide/cleanup/position retention and stale
events, while preserving outside-view, dropdown/edit/scrollbar/slider, handled/repeat/modifiers and Tab.
Radio fixtures invoke actual WPF OnClick, not a bare synthetic Click that bypasses native intent tagging.

**Parked/unverified:** live radio/version proof, retained position across X/reopen, docking/pinning/tabbing/
auto-hide/restored layouts, keyboard-only focus, live theme/High Contrast/DPI/readability, no activation
theft, startup/close races and two-instance playback/cleanup. The toolkit's native select-item attempt was
refused **before** action because no selection container was available (`actionAttempted=false`); it did
not mutate a native version. A separate physical radio click was blocked by foreground interference,
not an established package defect. Source 0.4.8 independent narrow closure and live read-only accessibility
proof remain pending. No deployment, main-instance installation, Settings, Speaker, microphone, capture or
automatic reply delivery is enabled by this source correction.

**0.4.8 source verification blocked; stopped before installation:** prescribed no-deploy MSBuild/package
passes (only the existing toolbar warning on source rebuild). Native560 passed an isolated run, but both
final ten-route serial attempts failed native; the other nine routes passed at their documented counts.
One exact failure preserved the latest Detailed model/radio while save visibly reported `Unable to remove
the file to be replaced.` and the file remained Concise. Inspection confirms the unchanged shared Save
uses File.Replace; no underlying cause is established or preference fix/retry added. After an unchanged
native560 rerun passed, the final serial attempt failed `Nested peer round trip lost its newest checked
state/preference` (NativeReaderRegressionTests, saved-choice/radio assertion); that last failure's cause is
unconfirmed. Do not treat isolated success as reliable selection/persistence closure or all-ten acceptance.
Stop rather than weakening assertions or adding speculative synchronization/retries. Source 0.4.8 remains
uninstalled; independent closure is blocked on repeatable final native verification.

### Phase 3 part 1 scoped voice storage (0.5.0 source; unwired)

Independent settings foundation continues while the native 0.4.8 selection/persistence gate is
parked. Source 0.5.0 is not installed; installed Exp remains 0.4.5. No Settings/toolbar control,
automatic Speaker, worker option, microphone or Windows-wide preference is enabled or changed.

The immutable model resolves built-in < global < solution < temporary window values and reports
each value's origin. This increment covers remembered reading mode, desired Speaker preference,
voice name, rate (-10..10) and volume (0..100). False and zero are explicit overrides; null means
inherit, and an empty voice name explicitly selects the engine default. Voice-name validation is
structural only; actual installed-voice enumeration/availability and playback application are pending.
Appearance, mic/model resources, Settings UI and the rest of Phase 3 remain planned. Automatic reader
reopen after a hidden or unpinned window receives a new live reply, and Concise/Detailed acceptance against
real replies, are deferred to the later reply-delivery phase rather than blocking Phase 3.

The unused default store lives under `%LOCALAPPDATA%\VsCopilotChatVoice\Settings`, not a repository.
Strict versioned XML uses one global file and canonical solution-path SHA256 filenames. Selected-field
patches read the latest scope under an exclusive empty `.lock` file handle, flush an adjacent temporary
file and atomically replace/move it. Competing updates wait at most five seconds; crashes release handles,
not persisted PID ownership. Corrupt/unknown-version data, access/save/cleanup and watcher errors surface
explicitly. Window values are never persisted. These synchronous storage operations belong on a background
thread when a future UI calls them; watcher callbacks contain scope invalidation only and need asynchronous
UI dispatch. They never load settings, manipulate controls or start audio by themselves.

First explicit global access imports legacy Concise/Detailed once through the existing preference helper;
the legacy file remains untouched, and later standalone changes are not mirrored. Missing legacy defaults
to Detailed; invalid legacy aborts import. Existing global storage is authoritative thereafter. No current
native/standalone reader calls this new store. Tests inject only isolated temporary roots and legacy paths.

`--settings-probe [VSIX-DLL]` passes **126 assertions** on linked source, **253** with the actual DLL too,
including inheritance,
schema/I/O negatives, watcher invalidation, real two-process merged updates, lock deadlines and owner
release/crash recovery. VS MSBuild/package and IDE builds pass; all eleven routes passed serially on
this source. Native560 passed once without another native-source change; this does not close the parked
0.4.8 reliability issue or live native acceptance. A fresh independent settings run then failed during
the real two-child merged-update check: `ScopedSettingsStore.Save`/`File.Replace` reported
`Unable to remove the file to be replaced.` The output did not identify the linked/actual-DLL leg or
rate/volume child. The cause is unconfirmed; no retry, storage fix or common-cause claim is made.
The 0.5.0 persistence gate and frozen package are parked, not accepted or installable.
Its private `--settings-child` helper refuses non-test roots. No new framework/package is added.

### Phase 3 part 2 external voice capabilities (0.5.2 source; unwired, targeted review closed)

`VsCopilotChatVoice.Reader.exe --speech-capabilities` runs headlessly before creating a reader window
and returns one bounded versioned JSON report of actual System.Speech-accessible voices, their culture
and enabled state, the nullable default voice, rate (-10..10), volume (0..100) and unsupported pitch. This is
engine availability, not a promise that every voice displayed in Windows Settings is accessible.
The external process owns/disposes the synthesizer; it invokes no Speak, voice-selection or preference
write. Existing standalone and speech-worker modes remain unchanged.
The schema is `VsCopilotChatVoice.SpeechCapabilities`, version 1, engine `System.Speech`, with at most
64 voices, 256-code-unit names and canonical culture names up to 85 code units. Success exits 0;
extra arguments exit 2; discovery/serialization/output failure exits 1 with bounded stderr, not a window.

Shared speech-free contracts/parser and an unwired owned-child query expose remembered-voice availability
without silently substituting a missing/disabled/ambiguous selection. The query drains bounded strict-UTF8
stdout/stderr concurrently, handles cancellation/deadlines and cleans up only its exact child. Output limits
are 65,536/8,192 bytes; callers supply a timeout up to 30 seconds and cleanup has a separate two-second
wait budget. Direct CLI discovery has no independent watchdog. Synchronous native start/kill/dispose calls
are not preempted by the timer; do not claim an unconditional hard wall-clock bound. No Settings/toolbar/worker consumer,
Preview audio, speech-option application or Windows-wide setting is enabled.

No-deploy MSBuild/package and parent IDE build pass; `--speech-capabilities-probe <VSIX-DLL>` passes 152 assertions, including
the real external no-speech report (five enabled voices/default available), contract negatives, isolated
process backpressure, cancellation/timeout and cleanup. No voice identifiers are printed in proof summaries.
An earlier real-child query timed out after ten seconds; subsequent direct execution and unchanged/final
probes passed. Its cause is unconfirmed, not reliable cold-start proof. Independent 0.5.1 review confirmed
a queued-deadline defect: a 100 ms query on a starved worker pool remained incomplete
at 250 ms and failed after 3,554 ms with Win32Exception instead of TimeoutException. Its timeout starts
after queuing. Corrected 0.5.2 captures the API-entry deadline, uses dedicated orchestration/bounded drains
and fences launch against expiry. Independent baseline152 passes once; isolated actual-DLL checks with
the sole worker blocked 3.5 seconds complete deadline/cancellation in 127 ms, terminate the exact child
and reject expired entry with zero starts. The original finding is closed with no new significant finding.
Supplemental checks blocked by fixture/reflection setup did not execute and are not claimed as proof.
The final serial settings route failed File.Replace at `VerifyStore`; the other ten preserved routes passed
The native/storage reliability gates remain parked; 0.5.2 is not an install candidate.

### Independent captured-history data foundation (0.5.3 source; unused, bounded review passed)

The speech-free in-memory catalog binds immutable instance/session/client conversation identity and copies
caller-supplied completed Detailed/optional Concise blocks. It supplies no producer trust/completion proof,
transport, UI or automatic playback. Missing Concise remains missing. Identical stable-ID deliveries preserve
all state; conflicting reuse and capacity return explicit refusal outcomes. Distinct IDs with identical text
remain distinct. Future callers must handle every refusal; no current UI consumes this catalog.

Capacity is 64 replies and 1,000,000 retained UTF16 Text units across both variants, at most 128 blocks per
variant/100,000 units per block, reply/anchor IDs up to 128 units, client IDs 256 and optional metadata 1,024.
Limits refuse atomically without eviction. Snapshots/nested values are defensive and immutable; owned locking
serializes updates. Previous/Next/Latest are explicit selection only; arrivals never move existing selection,
and manual/new-arrival flags remain until Latest. Clear/disposal releases owned references; late operations
refuse. No reply text is persisted or logged; no WPF document is retained.

Shared pure block validation is reused by catalog and ReaderDocument. The production model requires readable
body text/zero non-step numbers; legacy IDs/numbers, prefix-only steps, error order, formatting, offsets and
standalone parsing remain unchanged. `--reply-history-probe [VSIX-DLL]` passes 651 assertions with actual DLL
(318 linked, 319 actual-DLL, 14 legacy). Build/package and parent IDE build pass; twelve other routes ran once:
settings failed its existing child File.Replace check, all eleven others passed unchanged. Independent probe651
passed once with no significant finding; HEAD comparison confirmed unchanged legacy validation/format/offsets.
The source is uninstalled and neither a full Phase 6 implementation nor an accepted aggregate.

### External speech-option boundary (0.5.4 source; no UI consumer, bounded review passed)

An immutable speech-free snapshot contains voice name (empty means the worker-initial engine default),
rate (-10..10) and volume (0..100), with no pitch. Empty voice changes only the identity; rate/volume remain
explicit. The uniquely named host `SpeakWithOptionsAsync` uses
distinct bounded `speak-options` framing; older workers refuse the unknown command rather than ignoring
settings. A strict System.Speech/version1 profile requires all three fields; invalid/unknown/duplicate/missing
fields, encoding, ranges and frame limits are refused without silent defaults.

Only the external worker selects an exact uniquely named enabled voice, retires the previous prompt before
mutation and verifies actual getters. Correlated applied-option evidence means configuration was applied,
not successful speech completion. Cancellation epochs, generation fences, five-second command deadlines,
serialized writes, actual pause/resume and prompt offsets remain preserved. Retirement waits up to five seconds
outside the callback lock; timeout closes the connection. Native calls themselves are not preemptible. Legacy Speak resets worker-initial
voice/rate/volume; read-only option queries prove defaults without unsolicited legacy frames. No global speech
setting, active Settings/toolbar/reader-backend consumer, UI Preview, microphone or automatic activation changes.

No-deploy MSBuild/package and parent IDE build pass. `--speech-options-probe [VSIX-DLL]` passes 422 assertions with actual DLL
and real null-output worker evidence, including defaults/options/errors/lifetimes. Thirteen preserved routes
ran once: settings failed its existing child File.Replace check; the other twelve passed unchanged. Independent
once-probe422/source review found no significant issue and left zero owned Readers/fixtures.
Source0.5.4 is uninstalled; native/storage/Exp/audible/fullPhase3 gates remain open.

### Phase 3 increment 1 persistence closure (0.5.6; tests and review passed)

The shared atomic publisher retains File.Replace/File.Move and retries only Win32 1175/32/33.
Retry sleeps are capped to the remaining one-second budget, and expiry is checked before another
attempt. Native filesystem calls and scheduling are not preemptible; this is a retry budget,
not an unconditional wall-clock deadline. A preference save plus cleanup failure remains a visible
IOException containing both messages and both original causes, preserving existing reader handlers.
Private implementation boundaries support deterministic deadline tests and real locked-file cleanup
tests without changing public APIs, preference tokens, paused switching or storage locations.

The independent baseline build/settings253/native560/integration298 checks passed once. After the
approved R1/R2 corrections, no-deploy VS MSBuild and IDE build passed; integration320 (seven live
progress mappings), settings253, native560, model379 and Reader self1868 passed once serially.
Integration adds 22 deterministic deadline/cleanup assertions. Independent closure rebuilt and ran
integration320/settings253/native560 once serially: all exited0, R1/R2 closed, no new in-scope findings.
Cleanup tests exercise the real private cleanup path with a locked file and injected original failure;
the full Save double-failure sequence is source-reviewed, not independently fault-injected.
### Phase 3 increment 2 Settings UI/runtime (0.5.7; automated checks pass, Exp/manual acceptance pending)

The Settings toolbar button is now functional while Mic and Speaker remain disabled. It opens one scoped WPF
editor for Global defaults, the current solution and the temporary VS window, with window > solution > global
precedence, explicit effective-origin status and Reset semantics. Speech voice, rate, volume, Speaker intent and
Concise/Detailed preference use the existing validated atomic store; audio policy (Off, configurable duck or
suppress), theme choice and reader font size are stored in the same schema and inherit by scope. Corrupt,
missing or unavailable voice data is reported; Preview refuses a missing/disabled selected voice and always uses
the external worker.

Native reader playback now uses the effective speech snapshot and applies other-application audio policy outside
devenv.exe. The policy excludes both VS and the external reader worker, records each session before mutation,
restores only unchanged sessions after completion/cancellation/failure, and surfaces restoration failures. Reader
appearance follows VS resources by default, supports Light/Dark/font-size overrides, and High Contrast retains
system-color precedence. This remains a preview: no Mic/Speaker activation or real-chat reply delivery is added.

The actual-DLL settings route passes 271 assertions, toolbar 62 and native reader 560 after the increment. These
are synthetic/isolated checks; Exp installation, audible Preview, audio restoration perception, live theme/DPI and
accessibility acceptance remain manual gates. Phase 4 remains paused until those Phase 3 gates and the local commit.

### PoC 1 bridge research and fallback proof (historical native increments)

The 0.2.12 PoC build adds an opt-in binding/state probe and separately gated insertion-capability refusal, not production
dictation/reply delivery. It is enabled only in Debug, with `/RootSuffix Exp` and the
launch-process environment variable `VSCOPILOTCHATVOICE_BRIDGE_POC=1`. All toolbar controls
remain disabled. By default the current-user-only pipe accepts only `inspect`; the read helper verifies
the server PID and bounds response size/time. See `docs/SCRIPTS.md`. Do not enable this in
normal VS or treat accessibility runtime IDs/chat titles as stable conversation identities.
Atomic native editing is unproved: the unsafe insertion prototype was withdrawn after review.
Native insertion, send and automatic reply capture remain unimplemented.
The solution and targeted 56-probe/IPC and 62-toolbar checks pass. The corrected 0.2.5 Exp-only
installer and configuration refresh both exited 0. The installed manifest and DLL hash match
the independently reviewed package. Foreground access recovered after the desktop obstruction
was removed, and a new disposable Exp chat was created without submitting anything.
Independent review found two diagnostic defects: rejected requests could terminate the listener,
and idle pipe reads ignored token cancellation on .NET Framework. The user approved both fixes:
reject per connection and dispose the active pipe when its deadline expires. Check cancellation
after reads/capture so timeout-induced EOF is not misreported as malformed input. The 0.2.5
fixes pass 56 probe/IPC assertions, including next-client recovery. Independent re-review
confirmed both resolved with no new high-confidence findings. Live Exp also recovered after a
malformed request and terminated an idle client after 15,006 ms; the next inspection succeeded
while that client remained open.

The disposable chat exposes `HarnessSessionViewModel` with `Session`, `Composer`, `ChatLog`
and `IsResponding` property metadata. The user explicitly approved workspace trust; native
**Trust once** removed the rendered warning without selecting permanent folder trust. No
prompt was entered/sent and no replies were captured.

The 0.2.6 probe follows only those three observed bindings by one level. It inventories
public properties, including inherited interface properties, and reads only the existing
primitive identity whitelist through public getters. It does not read draft/reply content
or recurse further. The expanded 78 probe/IPC assertions and unchanged 62 toolbar assertions
pass. The 0.2.6 Exp installer/refresh both exited 0, and the installed version/hash match.
Independent review found one medium-severity defect: name-only lookup of a hidden inherited
binding property can throw `AmbiguousMatchException` and terminate the listener. The reviewer
reproduced it against the current DLL; no other high-confidence findings were reported.
The user approved appropriate fixes autonomously. A new regression reproduced that exception
against 0.2.6. Version 0.2.7 resolves the nearest public declaration without falling back past a
private getter, and reports remaining ambiguity per connection. Independent re-review confirms
the finding resolved, reruns all 78/62 checks successfully, and reports no new in-scope findings.
The 0.2.7 Exp installer/refresh both exited 0; its installed DLL matches the reviewed package.
Live inspection reaches the disposable chat's real session/composer/log bindings. The session
uses `CopilotSessionId`, not a primitive ID: inspected SDK metadata exposes a public Guid plus
`ClientId.Id`. The 0.2.8 probe reads only these verified identity fields and inventories the
observed Composer.PromptInput/ActionButton paths, without stringifying arbitrary objects or
reading a draft/reply. Expanded 84 probe/IPC and unchanged 62 toolbar checks pass; exact package
version/hash are verified. Independent review reran both routes and reported no high-confidence
findings. The 0.2.8 Exp installer/refresh exited 0, and live SDK identity extraction succeeded.
A harmless generated prompt was entered through UIA, read back exactly, submitted once, and
returned exactly `POC_BRIDGE_OK`; the draft cleared. Native Guid/client identity survived trust,
title changes, submission, switching and history reopen, while another chat had a distinct Guid.
SDK metadata also confirms Prompt has a public setter and PropertyChanged notifications.
These are controlled capability proofs, not a voice-delivery bridge: the pipe remains read-only.
Atomic draft-revision/busy/target guards and native completed-reply delivery are still pending;
PoC 1 is incomplete. Stable checkpoint `50938af`
saves the reviewed 0.2.7 diagnostic and plan locally; nothing was pushed.

Checkpoint `1755792` saves the reviewed 0.2.8 typed-identity and live smoke evidence.
The next 0.2.9 increment passively observes exactly one verified native chat root: a fresh
observation epoch, monotonic Prompt-change notifications (without reading draft text), and
trust/responding streams. Unknown states stay null; replacement, hiding, unloading and ended
feeds invalidate the epoch. Read-only/transcript flags are reported separately. Disposal
removes subscriptions and ignores late callbacks. These are observations, not authorization
to insert/send; atomic command guards and reply delivery remain pending. Build and 138 bridge
probe/IPC plus 62 toolbar assertions pass. Review reproduced a 0.2.9 lifetime defect: closed
targets retained subscriptions until another inspection/disposal. The 0.2.10 fix automatically
tears down terminal observations, defers constructor-time cleanup until handles are assigned,
preserves the terminal reason and releases native/model references. A new close-without-inspection
regression failed on the unfixed DLL and now passes alongside synchronous/background terminal
callback checks. Independent 0.2.10 re-review passed 138/62 checks with no new in-scope findings;
an isolated background bridge-disposal test also verified subscription/reference release and
pipe-worker completion. Exp-only installation and configuration refresh exited 0, and the
installed manifest/package/DLL match version 0.2.10 (SHA256
`7246031262823FC2F9FE93BFDB5BAA07DED5CBA578EFDE26EFC1A32CD6AB0BDA`).
Live native observation verified Trust once false-to-true, content-free draft revisions across
A-B-A edits and clearing (11, 23, 35, 36), and responding true-to-false after one generated
submission. The exact reply `POC_BRIDGE_STATE_OK` appeared and the draft cleared with unchanged
native identity/epoch. History navigation returned NoNativeTarget; reopening only the generated
chat preserved its native identity/reply and created a fresh epoch with revision zero.
UIA entry required settled TextPattern read-back; no uncertain submission was retried. These
are controlled native-state/UIA proofs, not native guarded send or automatic reply delivery.
Installed SDK metadata confirms the root and prompt notify changes, but the composer does not.
`HasInputReplacementNotifications` reports that missing capability explicitly; complete native
input-replacement coverage is not established, so this is not a command guard.

Read-only installed SDK research now resolves the actual metadata dependencies. The public
composer input getter has no setter and directly reads a constructor-only field in SDK
18.13.0.0; this is version-specific lifetime evidence, not a shipped runtime guard. Backend
`SendAsync(ChatSendRequest, CancellationToken)` returns `Task<CopilotMessageResponse>` with
Content/Status/Error, while native log entries expose InteractionId, IsComplete and IsFromUser.
No such method/getter was invoked on a live session. Command acceptance, reply correlation,
native content extraction and the guarded protocol still need implementation/live validation.
The reusable `scripts\agent\Read-PublicAssemblyContract.ps1` reads exact public type metadata
in Windows PowerShell with explicit dependency folders and bounded member counts; its optional
constructor-only-property check reads metadata, never private field values. See `docs/SCRIPTS.md`.

### Guarded UIA insertion helper (experimental, not in the extension)

Because native atomic editing is unproven, `scripts\agent\Insert-ChatDraftUia.ps1` is the approved
fallback experiment (see `docs/SCRIPTS.md`). It wraps the AgentDebugToolkit paste-with-read-back
primitive and refuses unless the target is an Exp `devenv.exe`, the chat input is showing, no
trust/folder banner is shown and the Send button is disabled (empty, idle draft). It never clears
or sends, and re-checks the Send button right before typing. It is not atomic: on a verify mismatch
it reports `DraftChanged`, does not clear, and our text may remain inside the person's draft. If the
input exposes a writable ValuePattern the primitive replaces the whole draft (the method is reported),
so text typed after the final check would be lost. Verified so far: parse, stale-handle, control-character and main-instance refusals.
Live Exp results (signed-in chat, 2026-11-21): an Exp instance that reported "an operation is blocking
user input" dropped Ctrl chords and End and moved focus to Git Changes, so paste read-back failed; the
Send button was also an unreliable emptiness signal there. After restarting Exp the helper proved, with
TextPattern read-back and nothing sent: paste into an empty draft; `-Append -PlaceCaret` onto a one-line,
a two-line and a 14-line draft whose end was scrolled out of view (previous text preserved, marker at the
end). TextPattern text is the read-back signal. `-Append` without `-PlaceCaret` is passive (it does not switch window/tab or move focus itself; typed mode
sends only the text, `-Paste` sends Ctrl+V, and both let the toolkit re-activate Exp, so text could land in
Exp's current control if the person moves focus during the call); you must have Exp foreground, the input focused and the
caret at the end, else it refuses. `-PlaceCaret` sends one Ctrl+End after bringing Exp forward and refuses
if the input leaves the UI tree, the text changes or the caret is not at the end. `-Append -Paste` delivers
up to 20000 characters (one line, no control characters or double quote, not starting with a hyphen or ending with a backslash) with the
toolkit's `paste-text` verb (clipboard snapshot restored) at the caret, with the same guards and exact
read-back; an 11,091-character paste onto a 14-line draft was proved live and nothing was sent. When the
draft is not empty, `-Append -Paste` starts the pasted text with a line break so the new prompt begins on its
own line (`-NoLeadingNewline` opts out; typed `-Append` never adds one, since a typed Enter would send).
Additional live scenarios on Exp (unattended, nothing sent), all exact read-back or the intended refusal:
accents, CJK, emoji, shell/SendKeys special characters and padded spaces via paste; a 20,000-character
paste (20,001 refused); clipboard text, image and file-list contents restored after a paste (a text
clipboard regains synthesized `System.String`/`Text` formats); a clipboard locked by another process gives
`Failed` with the draft verified unchanged; a caret placed mid-draft or a selection gives `CaretNotAtEnd` (typed and paste); `-PlaceCaret` collapses a
selection with Ctrl+End and appends without replacing text; one Ctrl+Z removes a whole 2,200-character paste;
Exp behind another window gives `ExpNotForeground`; focus in the Exp code editor gives `ChatInputNotFocused`
(editor untouched), and `-PlaceCaret` from that state refocuses the chat input and appends; with the Chat tab
hidden behind another tab of its group (composer out of the UI tree) every mode refuses with
`ChatInputNotVisible` and no tab is switched; with the Chat tab closed, View > GitHub Copilot Chat (UIA
Invoke) brought the composer back with the old draft intact (default mode `DraftNotEmpty`, append works);
while a long reply was streaming (Send button replaced by Stop) the default, typed `-Append` and
`-Append -Paste` modes all gave `Refused` / `SendButtonNotFound` with nothing typed, and insertion worked
again once the reply finished. Not yet tested (need the person to set up the state): two chat inputs,
`#file`/`/command`/attachment chips, an open autocomplete popup, typing during the call and the
blocked-input Exp state. Still not atomic: text typed by the person during the few seconds of the call can
interleave (reported, never cleared).
The orchestrator's own Send script clears the input, so it is unsuitable here.

### Read-only completed-reply observation helper (experimental, not in the extension)

`scripts\agent\Read-ChatRepliesUia.ps1 -Hwnd <Exp hwnd> [-IncludeText]` makes one toolkit `inspect` call
and reads the chat transcript. It sends no key, click or paste and changes no focus, window, tab or draft.
It refuses a non-Exp `devenv.exe` (unless `-AllowMainInstance`), a missing transcript, more than one
transcript (`AmbiguousChat`) and a truncated tree. Observed on Exp (2026-11-21): the transcript is a
`ChatSessionList` (AutomationId `session`) of `ChatMessageItem` ListItems in chronological layout order.
The tree keeps only a window of recent items (a very long reply can sit far above the viewport, with
negative Y), so `MessageCount` and the earliest turns can change between calls. Items are classified by
structural markers, never by length or position alone: a footer contains `PART_LikeButton` (name "The
response cost N credits.", sometimes prefixed "The response used the <model> model."; a cost-only footer
seen on Exp can lack the Like button, so an item with no body or tool marker whose name matches that wording
is also a footer), a message body
contains `PART_FlowScrollViewer` (its full text is the item's UIA Name), a tool call or task group contains
`toolCallExpander` or `HeaderSite` (for example "Run command: ...", "Worked on 7 tasks", "Read 2 items"),
and a short item (at most 2 children, name up to 60 characters) is a header: `GitHub Copilot` starts an
assistant turn and any other header (author name, `Restore Checkpoint`) starts a user turn. Inside an open
assistant turn only `GitHub Copilot` or `Restore Checkpoint` counts as a header, so file chips such as
`README.md` stay `Unknown`. An assistant turn can hold several bodies and tool items before its footer;
`LastReply` is the last assistant turn (bodies joined with a line feed) with `FirstIndex`, `BodyCount`,
`ToolItems`, `Length`, `Sha256` and `LaterTurns` (turns after it, so a newer user prompt makes it non-zero). It
is `Complete` only when the footer is present, no later turn exists and the chat is idle. A turn with no body
yet (tool items only, or just started) is still selected, with `BodyCount: 0`.
Busy signals: idle means a visible SendButton and an offscreen CancelButton; while a reply streams (and
while an approval such as Run command / Allow / Deny is pending) Send is offscreen and Cancel is visible and
enabled; the helper reports `ChatBusy`, `SendButton` and `CancelButton`. The UI exposes no per-message
AutomationId, so reply identity is the first body's position plus length and SHA-256. Default output is
lengths and hashes only; `-IncludeText` adds the last reply text. Live checks on Exp: a finished 45,096-character
reply; a 22,252-character reply that followed a run-command approval (Allow clicked); and a later
three-body, two-tool-item turn reporting `Complete: true`. The main host is refused. Not yet observed: a
second chat, a cancelled reply, a Deny outcome, and very long transcripts (parked by user decision).

`scripts\agent\Wait-ChatReplyUia.ps1 -Hwnd <Exp hwnd> [-AfterSha256 <hash>] [-TimeoutSeconds 120] [-SettlePolls 2] [-IncludeText]`
polls the observer (read-only, one inspect per poll) until the last reply is `Complete`, differs from
`-AfterSha256` (the duplicate-delivery guard: pass the hash seen before sending) and the same hash was seen
on `-SettlePolls` consecutive polls (default 2, range 2-10). Statuses: `Completed`, `TimedOut`, and an immediate `Refused`/`Failed` from the
observer. A new reply with text identical to `-AfterSha256` is accepted only after the wait saw the chat busy
or a newer turn. `-PollSeconds` must be at most half of `-TimeoutSeconds`; a wait can overrun its timeout by
one observer call, and a hung inspect call is not interrupted. Verified on Exp against an idle finished chat
only: delivery after two polls, the duplicate guard timing out, a different hash delivering, bad input and a
stale handle refused, no text unless requested. It was also run across a live reply sent to Exp with the
current last-reply hash as `-AfterSha256`: it returned `Completed` after 24 seconds and 2 polls with a new
hash. A second live run with a 4321-character text-only reply also returned `Completed` after 3 polls with a
new hash; the send call outlasted most of the streaming, so mid-stream polling was not seen, and the visible
transcript window shrank from 6 to 3 messages with turn ordinals restarting (virtualization). Two further
live runs started the wait before sending (4321-char and 9573-char replies) and both delivered the correct
final reply (`Completed`, 3 polls). A side sampler during a third long reply (8869 chars) caught one transient
observation, about 16 seconds after the send, of a 76-character body with an 87-character footer, an idle
chat and `Complete` true, followed by the real reply on the next sample; the settle rule kept it from being
delivered, but a stream stalled across two polls could still slip through, so `-SettlePolls` can be raised
(checked with a mock observer only). An approval pause mid-wait is not yet observed. How these findings could
feed Phase 6 reply identity is a design note in
[the implementation plan](docs/IMPLEMENTATION_PLAN.md); it is not implemented in the extension.

### Atomic-insertion capability probe (0.2.12)

Set the additional **launch-process-only** gate `VSCOPILOTCHATVOICE_DRAFT_POC=1` in Debug Exp
to enable the framed `insert` request; without it the pipe remains inspect-only. No send request
exists. The helper in `docs/SCRIPTS.md` supplies explicit SessionId/ClientId, observation
generation/revision and a retained unique request ID, but every valid insertion request is
rejected with `NativeAtomicInsertionUnsupported` and `MutationAttempted=false`.
Review reproduced draft/trust/busy changes after the last copied snapshot; immutable input-slot
metadata and observer locks cannot fence native producers. The draft getter/setter transaction
was removed, rather than adding another racy check. No draft is read or changed by this request.
The response never echoes a draft/transcript. No request is retried automatically.
Retain the complete original request for reconciliation: replaying its exact ID and parameters
returns the saved refusal; changed parameters with that ID are rejected.
The non-evicting in-process ledger holds at most 128 requests; capacity exhaustion is explicit.
Payloads are bounded to 32 KiB and transcripts to 4,096 characters. Client writes and reads share
a 15-second deadline that disposes the pipe on expiry; retain the complete request if outcome is unknown.
Build and 209 actual-DLL bridge/IPC plus 62 toolbar assertions pass. The real non-reading-pipe
regression verifies backpressure and disposal within 369 ms for a 350 ms test deadline.
Independent re-review closed both findings with no new in-scope issues. Safe Exp tests verified
default-gate refusal/recovery, optional-gate refusal, explicit reconciliation and request-ID conflict
with no mutation or transcript echo. The target remained unavailable; these are capability-refusal
tests, not positive native insertion tests. A live PowerShell task-result leak was fixed and its
real-pipe output-shape regression independently passed. Task Exp is closed and host focus restored.
Current testing is complete; stop here as requested. PoC 1 is incomplete and atomic native editing
remains blocked. No microphone, send, reply delivery or later production phase is enabled.

Incremental packaging retained manifest version 0.2.4 after the source was bumped to 0.2.5.
An explicit no-deploy VSIX Rebuild corrected it. The actual ZIP now reports 0.2.5 and contains
the identical independently reviewed DLL (SHA256 `0810376689126D67D228C496ADFE80F1D76BB47701EF8E1E68DAF66599EB4516`).
The rebuild emitted two existing UI-thread analyzer warnings on toolbar refresh/package disposal;
no unrelated warning suppression or fixes were applied. Always verify the packaged manifest,
not just its source, before deploying an update.

Discovery checks visible WPF windows every two seconds with a 12,000-node budget. Placement
requires one visible Copilot tool picker and a horizontal StackPanel reached only through
single-child wrappers. Unsupported, ambiguous or over-budget layouts are explicitly logged
and left without an inserted toolbar. Recreated/hidden chats, reordered controls and disposal
are handled without deleting unrelated controls. Existing attachments revalidate the host,
single-child wrapper branch and competing toolbar IDs on every refresh; supported placement
recovers when an unsupported layout or competing toolbar is removed. Copilot's internal layout is not a public API;
future VS changes may make placement unavailable rather than justify unsafe layout edits.

No VoiceType source, Whisper binaries/models, personal settings or session replies
are included. The new implementation plan proposes selectively reusing VoiceType microphone
code in our own external host, not requiring the standalone VoiceType app. This is not yet
implemented; read-aloud uses the bundled reader and does not require a transcription model.

## Build and verify

Requires Windows x64, Visual Studio 2022/2026 with .NET desktop development and
Visual Studio extension development tooling, and the .NET Framework 4.8 targeting pack.
Use Visual Studio's MSBuild (Developer PowerShell), not `dotnet build`, for VSIX packaging:

```powershell
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' .\VsCopilotChatVoice.slnx /restore /m /v:m /nologo /p:Configuration=Debug /p:DeployExtension=false
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --speech-options-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --reply-history-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --speech-capabilities-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --settings-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --native-reader-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --reader-playback-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --speech-worker-probe
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --toolbar .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --bridge-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --gesture-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --contracts-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --reader-model-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\src\VsCopilotChatVoice.Reader\bin\Debug\net48\VsCopilotChatVoice.Reader.exe --self-test
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe
```

Run these fourteen routes serially after the final edit: options 422, history 651, capabilities 152, settings 271,
native 560, playback 220, speech 110,
toolbar 62, bridge 209, gesture 209, contracts 136, model 379, Reader self 1,868 and
integration 320/seven progress mappings. These counts describe automated checks, not native acceptance.
The 0.5.7 settings271 route and the toolbar/native routes pass with the actual rebuilt DLL. Official Exp
installation, audible/audio restoration, live theme/DPI/High Contrast/accessibility and final local commit
remain Phase 3 gates; real-chat reopen and Concise/Detailed acceptance remain Phase 6 work.

Final 0.4.6 no-deploy solution build/package and complete ten-route serial run passed; packaged version
and tested-DLL SHA256 match. The first sequence had one five-second actual-worker startup timeout in
playback RealAdapter. Inspection found no leftover worker; isolated playback and the entire serial rerun
passed unchanged. No timeout, owner or unrelated preference/layout fix was made.

Final 0.4.7 prescribed no-deploy solution build/package and all ten then-current routes passed serially,
including native412 and the nine preserved routes. The source rebuild emitted only the existing toolbar VSTHRD010 warning.
An initial existing fallback-save assertion failed; its diagnostic message was expanded and the unchanged
production rerun passed. No preference/layout fix or unconfirmed failure-cause claim is made. The exact
original 0.4.6 Follow-selection and intermediate shutdown-order regressions failed before their fixes;
the final native412 includes both permanent cases. These are synthetic checks, not a 0.4.7 installation.

`--gesture-probe [built-VSIX-DLL]` checks the pure detector with a fake capture owner,
startup/release races, cancellation and real two-process local mutex contention/abandonment.
Supplying the DLL also checks the actual observer's gates, chat-root selection, handled-events-too
input, unchanged Handled flags, focus refusal and teardown. No audio or models are used.
It passes 196 assertions without the DLL and 209 with it; the observer fixture uses an isolated
mutex name. Run the DLL route for the full proof. The internal `--gesture-owner-child` route
is only the harness's child-process handshake, not a recording tool.
Missing-key-down regressions cover both release orders, startup and established toggle,
Ctrl-up held-input revalidation, and Escape-up cancellation across all lifecycle stages.

The harness suppresses speech output, uses temporary preferences, and briefly opens
offscreen test windows. The separate `--toolbar <built-VSIX-DLL>` mode checks placement,
control states/accessibility, theme callbacks, compact sizing, flat icons/shared border,
duplicate suppression, reordering, chat recreation,
ambiguity, hidden windows and disposal. It does not launch speech or access user preferences.
Current documented counts: 62 toolbar assertions, 1,868 reader self-test assertions and 320 reader
integration assertions with seven live progress mappings. A successful build produces
`src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.vsix`.
Automatic deployment is disabled. Runtime extension testing must use an experimental
VS instance (`/RootSuffix Exp`) first, not the main development instance.
`scripts\agent\Test-VsixInstallation.ps1` independently verifies package/install identity,
version and binary matches without installation or UI access; see `docs\SCRIPTS.md`.

Clear inherited `DOTNET_ROOT` before invoking the installed VS MSBuild, as in the task's
Developer PowerShell environment. The existing preference-save flake remains unresolved;
one additional unchanged layout-sensitive integration failure was seen during PoC 3, followed
by passing reruns. Neither is fixed by this proof.

The isolated `--bridge-probe` route passes 209 assertions for opt-in gating, binding metadata,
no unrelated content getter reads, hidden windows and the actual listener with a test capture
callback. IPC checks cover malformed/early-disconnected/unterminated requests, idle/partial
request deadlines, oversized responses, next-client recovery and clean shutdown. They do not
read a draft; passive-observation checks cover ABA/wildcard/background notifications, unknown
and changed readiness, transient input replacement, ended feeds, partial construction,
background-owner cleanup, revision exhaustion and late callbacks. VS uses joinable-thread
switching for UI cleanup; the standalone observer harness supplies its WPF dispatcher.
They do not
prove live Copilot identity, insertion/send or automatic capture. The production deadline remains
15 seconds; tests inject a one-second deadline, not a production setting change.
Insertion-capability checks prove removal of the unfenced mutation surface, explicit unsupported
refusal, cancellation, request deduplication/conflicts/capacity and actual framed-pipe recovery/deadlines.
Run `& .\tests\Test-GuardedChatDraftTransport.ps1` for client parsing, completed transmission,
pre-write expiry and a real non-reading peer deadline. These are refusal proofs, not positive insertion proofs.

Independent review resolved the cached-placement finding after the approved fix. Its latest
low-severity test-only finding remains unapplied: the glyph-height assertion does not prove
unclipped rendering or detect a collapsed glyph. No production clipping defect was established;
passing synthetic checks do not replace the remaining native appearance/accessibility matrix.

### Experimental smoke-test status

On 2026-11-20, installation and package initialization were verified in an isolated
`/RootSuffix Exp` instance. A fresh launch with the solution open produced an activity-log
entry from `VsCopilotChatVoice`: **"Foundation preview loaded. Reader bundled; chat buttons
and delivery bridge are not connected yet."** The experimental IDE was responsive.
This verifies the 0.1.0 package shell only, not the new controls, speech delivery or theme integration.
An official 0.2.0 Exp update and waited refresh both exited 0; a fresh launch logged toolbar
attachment and exposed all three disabled controls beside Customize AI, including Speaker off.
The screenshot/layout check exposed oversized Mic/Settings buttons caused by dialog-style
minimums. The 0.2.2 preview adds compact flat styling and the requested shared group border;
the user confirmed the buttons look fine but the surrounding border is too tall. The 0.2.3
preview reduces its outer height to 24 DIPs without shrinking the glyphs. Official Exp-only
update and configuration refresh exited 0, the new package loaded, and the user approved
the resulting appearance. Deterministic toolbar checks do not substitute
for actual Copilot layout/appearance verification.

The initial SDK deployment copied files but failed enablement with **VSSDK1031: extension
could not be found**. Refreshing configuration after that failed deployment was insufficient.
The successful sequence was the official Exp-only installer (exit 0), a waited post-install
configuration refresh (exit 0), then a fresh Exp launch. File presence or installer success
alone is not proof of package loading. Normal builds still default to no deployment.
No experimental hive was reset and no main-instance extension was installed.

The reference project's deployment notes report the same SDK deployment/registration issue:
[CopilotUsageMeter](https://github.com/citsuresh/CopilotUsageMeter). Its documented reliable
route is `VSIXInstaller /rootSuffix:Exp`, followed by an experimental configuration refresh.
It is a conceptual reference only; no source is copied or modified there.

Close only the task's experimental instance before installation, and use the exact installed
VS instance ID (not the process ID). For first installation from PowerShell, wait for the
installer and refresh:

```powershell
$ide = 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE'
$instanceId = '<installed-VS-instance-ID>'
$vsix = (Resolve-Path '.\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.vsix').Path
$install = Start-Process "$ide\VSIXInstaller.exe" -ArgumentList @('/quiet', '/rootSuffix:Exp', "/instanceIds:$instanceId", "`"$vsix`"") -PassThru -Wait
if ($install.ExitCode -ne 0) { throw "Exp install failed: $($install.ExitCode)" }
$refresh = Start-Process "$ide\devenv.exe" -ArgumentList @('/RootSuffix', 'Exp', '/UpdateConfiguration') -PassThru -Wait
if ($refresh.ExitCode -ne 0) { throw "Exp configuration refresh failed: $($refresh.ExitCode)" }
$solution = (Resolve-Path '.\VsCopilotChatVoice.slnx').Path
$log = Join-Path $env:TEMP ("VsCopilotChatVoice-Exp-{0}.xml" -f [Guid]::NewGuid().ToString('N'))
Start-Process "$ide\devenv.exe" -ArgumentList @('/RootSuffix', 'Exp', '/Log', "`"$log`"", "`"$solution`"")
```

Never omit `/rootSuffix:Exp` or add `/shutdownprocesses`. Do not reset the hive, alter unrelated
extensions or install into the main instance. Capture package initialization in the activity log
after launch before declaring runtime success. `/Log` takes the following argument as its output
file: always supply a dedicated log path, never the solution path. Installer logs are generated
under `%TEMP%`. Subsequent updates must replace only this extension in the Exp hive, not change
the deployment target. Normal-instance installation remains deferred until Exp testing passes
and the user explicitly approves it.

After graceful Exp shutdown, `scripts\agent\Test-VsPackageActivity.ps1 -LogPath '<dedicated log>'`
verifies at least one exact package-source entry and zero errors without printing log contents.
Explicit `-MinimumEntries`, `-Source` and `-ExpectedErrors` support bounded proof expectations;
entry counts alone do not replace native behavior checks. See `docs/SCRIPTS.md`.

## Reader prototype usage

```powershell
# Replace 0xHANDLE with the verified live HWND of the intended VS window.
& .\src\VsCopilotChatVoice.Reader\bin\Debug\net48\VsCopilotChatVoice.Reader.exe `
  --status "$env:TEMP\vs-chat-voice-status.txt" --owner 0xHANDLE `
  --reply .\detailed.json --concise .\concise.json
```

Only the shown argument order is supported. Without `--reply`, the reader uses its
built-in sample; `--concise` requires `--reply`. This is one prepared reply per launch,
not an automatic Copilot response subscription. Opening starts speech in the saved
reading mode. Switching the current reply maps by section ID and waits for Play.

Each JSON file is a nonempty array of blocks, for example:

```json
[
  { "id": "intro", "kind": "paragraph", "text": "A prepared reply with **emphasis**." }
]
```

Supported kinds: `heading`, `paragraph`, `bullet`, `step` (positive `number` required).
Both versions require the same unique IDs; order may differ. No summarizer is built in.
Concise is disabled when absent. If saved Concise is unavailable, the reader explicitly
shows Detailed without overwriting the preference. The status file contains reply words;
choose a private local path and remove it when finished.

Reading mode remains at `%LOCALAPPDATA%\ChatVoice\reader-version.txt` for compatibility
with the existing prototype. It is the only migrated preference; no personal preference
file is distributed. Storage errors remain visible in the reader.

## Approved direction (not yet implemented)

The discussion is consolidated in [the draft implementation plan](docs/IMPLEMENTATION_PLAN.md).
All three bounded PoCs are complete and pushed through `64451c8`, with the caveats above.
The production decomposition was approved 2026-11-21. Section 6 decomposes Phase 1 into contracts,
target lifetime, metadata-only IPC and Exp acceptance. Its 0.4.0 metadata-only foundation is verified as
described above; full production functionality is not implemented. Working defaults not explicitly
settled remain labelled in the plan, and later phases require their own acceptance gates.
Phase 2's detailed breakdown is confirmed; shared reader extraction is the first increment;
it does not authorize microphone capture, Settings, Speaker activation or reply delivery/history.
The plan evolves with implementation/testing and approved enhancements. Start with bounded
production increments under their acceptance gates; do not reopen accepted PoCs
or treat their completion as automatic approval of the full migration.

- Activate the existing toolbar only as its backends become available; Settings remains
  independent of model/skill availability. Report missing capabilities with accessible badges.
- Native VS reader Tool Window, initially floating, with VS-managed docking/pinning/layout.
  Only presentation moves inside the IDE; speech synthesis, capture and transcription stay
  external. This supersedes the earlier entirely external production-reader UI direction.
- Hold-to-talk/toggle microphone modes, optional Chat-only Ctrl gesture, Esc cancellation,
  real audio-level Mic fill and VoiceType-style chat-local status pill. Preserve draft/target
  safety and copy completed text to the clipboard if its originating chat is unreachable.
- External Whisper engine/models, manually provisioned or selected through Browse. Neither
  extension nor companion skill downloads models. Missing resources lead to setup, not recording.
- Per-conversation captured-reply history, separate reply/within-reply navigation, queue policy,
  scoped keyboard support, and preserved Concise/Detailed behavior. Speaker Off stops/hides
  without discarding history; tool-window hide is distinct from changing automatic preference.
- Global settings, solution overrides and temporary VS-window overrides, including different
  voices for two agents in the same solution. User-local storage; no Windows-wide setting changes.
- Live VS theme plus readable appearance overrides and supported voice/rate/volume controls;
  pitch is conditional on actual backend support. Native tool-window chrome stays native.
- A local instance/conversation-specific reply/progress bridge. A skill is not an automatic
  response subscription; automatic capture requires a verified mechanism. Chat-scroll matching
  is an optional experiment and will be dropped if unreliable.

No automatic main-instance installation, commits or pushes.

## Contributing and documentation

- [History of Astronomy](docs\history-of-astronomy.md) is a standalone historical essay.
- Persistent project memory is in `docs\CODE_SUMMARY.md`, `KEY_FLOWS.md`,
  `DESIGN_DECISIONS.md`, `PROJECT_STATE.md` and `ROADMAP.md`. The contributor instructions
  register the per-solution graph/dependencies files. Open `docs\graph-viewer.html` and
  select or drag in the slug-named graph pair; its automatic loader uses legacy filenames.
- Initial graph Bootstrap analyzed all three projects: 119 nodes and 1,115 edges.
  The latest incremental border-height update analyzed two changed files: 154 nodes and
  1,417 edges in 4.74 seconds. Graph metadata identifies the commit at generation, not clean
  or committed worktree status.
  See `docs\PROJECT_STATE.md` for current validation results, including the intermittent
  preference-persistence harness failure observed before the first foundation commit.
	The latest diagnostic incremental update analyzed five changed C# files across two projects:
  410 nodes and 2,551 edges in 11.57 seconds. Metadata describes the analyzed working tree,
  not a new commit.
- Independent pre-commit review passed the solution build and both executable checks on
  their first audit runs. Its one viewer-wording finding was corrected in the upstream
  skill template and refreshed here; no application behavior changed for that correction.
- Update this README whenever repository changes are made, and review/update it again
  before each commit. Keep project layout, setup, commands, implemented behavior and
  remaining work aligned with the actual code. Do not describe planned features as shipped.
- Contributor instructions are in `.github\copilot-instructions.md`.
- Build and run the relevant existing tests for code changes. Documentation-only changes
  do not need a rebuild unless they change tested examples.
- Obtain explicit approval before committing or pushing; updating this README does not
  authorize a commit. Never include local settings, speech content, build outputs or secrets.
