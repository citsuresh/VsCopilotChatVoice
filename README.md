# VsCopilotChatVoice

Independent voice input and synchronized read-aloud for Visual Studio Copilot Chat,
with a themed reader and companion skill. Not an official Microsoft or GitHub product.

## Current status: toolbar-shell preview under validation

In Visual Studio, use **File > Open > Project/Solution** and select
`VsCopilotChatVoice.slnx` (rather than **Open Folder**). No restart is required.
The solution contains:

| Project | Responsibility |
|---|---|
| `src\VsCopilotChatVoice.VisualStudio` | Thin VSIX with a disabled chat-toolbar preview; bundles the reader under `Reader` but does not launch it yet. |
| `src\VsCopilotChatVoice.Reader` | Independent x64 WPF reader with Windows speech, word highlighting, sentence/paragraph navigation, pause/resume, Stop, Follow and Concise/Detailed selection. |
| `tests\VsCopilotChatVoice.Reader.Tests` | Executable reader regression harness plus an isolated toolbar mode against the built VSIX; not a Test Explorer test adapter project. |

The reader and tests were migrated from the audited session prototype, not from
VoiceType's dictation engine. The `ReadAlongTrial` namespace and sample labels are
intentionally retained during this foundation migration to preserve behavior.
The reader still uses the prototype's pale-yellow palette; VS theme synchronization
is planned, not implemented. The 0.2.3 VSIX adds a conservative internal-WPF toolbar shell:
**Mic | Speaker toggle | Settings | Customize AI**, inside Chat below its message box,
not in the main IDE toolbar. All three new controls are disabled with explanatory tooltips
and accessible names; the unchecked Speaker is a preview, not a saved-state indicator.
The compact, flat icon buttons use toolbar styles and share a subtle rounded border whose
color follows VS theme resources; disabled glyphs use the system's gray-text resource.
The group is 24 DIPs high with no vertical group padding, 28-by-22-DIP buttons and unchanged
14-DIP glyphs, tightening the border to match the neighboring chat controls more closely.
Reader theming is unchanged. No dictation, automatic
speech, settings UI or delivery bridge is connected. Build and deterministic checks pass;
The user approved the 0.2.3 appearance in Exp; the full chat-recreation/theme/DPI/accessibility
matrix remains pending. Foundation 0.1.0 loading and initial
0.2.0 toolbar attachment were verified; the latter exposed dialog-style minimum-width expansion,
now covered by compact-dimension regression checks.
No main-instance deployment was performed.

### Chat-bridge PoC in progress

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
Exp behind another window gives `ExpNotForeground`. Not yet tested (need the person to set up the state or
approve tab/focus changes inside Exp): input not focused, chat tab hidden, chat busy, two chat inputs,
`#file`/`/command`/attachment chips, an open autocomplete popup, typing during the call and the
blocked-input Exp state. Still not atomic: text typed by the person during the few seconds of the call can
interleave (reported, never cleared).
The orchestrator's own Send script clears the input, so it is unsuitable here.

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
msbuild .\VsCopilotChatVoice.slnx /restore /m /p:Configuration=Debug /p:DeployExtension=false
& .\src\VsCopilotChatVoice.Reader\bin\Debug\net48\VsCopilotChatVoice.Reader.exe --self-test
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --toolbar .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
& .\tests\VsCopilotChatVoice.Reader.Tests\bin\Debug\net48\VsCopilotChatVoice.Reader.Tests.exe --bridge-probe .\src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.dll
```

The harness suppresses speech output, uses temporary preferences, and briefly opens
offscreen test windows. The separate `--toolbar <built-VSIX-DLL>` mode checks placement,
control states/accessibility, theme callbacks, compact sizing, flat icons/shared border,
duplicate suppression, reordering, chat recreation,
ambiguity, hidden windows and disposal. It does not launch speech or access user preferences.
Current checks pass: 62 toolbar assertions, 1,868 reader self-test assertions and 292 reader
integration assertions with seven live progress mappings. A successful build produces
`src\VsCopilotChatVoice.VisualStudio\bin\Debug\net48\VsCopilotChatVoice.VisualStudio.vsix`.
Automatic deployment is disabled. Runtime extension testing must use an experimental
VS instance (`/RootSuffix Exp`) first, not the main development instance.

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
Its phase breakdown needs confirmation before production code changes. Working defaults that
were not explicitly settled are labelled in the plan; none of these features are shipped.
The user has authorized PoC 1 only; its read-only diagnostics and separately gated capability-refusal build are described above.
The plan evolves with implementation/testing and approved enhancements. Start with bounded
Exp proofs for exact chat routing/delivery, a native reader with external speech, and safe
Ctrl-only capture before committing to the full migration; optional hooks may be dropped.

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
