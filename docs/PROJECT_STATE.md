# Project state

Updated: 2026-11-22. Source/manifest 0.5.7 Settings UI/runtime increment is implemented and targeted actual-DLL routes pass. The 0.5.7 VSIX is installed in the isolated Exp hive and launched. The Settings dialog uses the Visual Studio SDK dialog surface, is modal like Tools > Options, is compact, follows dynamic theme resources, places Preview beside voice selection, and Preview resolves inherited or engine-default voices. The native reader preview now uses the approved POC control grouping/glyphs and reapplies theme brushes to recreated button/radio content. Package-load verification, audible/audio, theme/DPI/High Contrast/accessibility checks and final local commit remain before Phase 4.
This file is overwritten at the end of each working session.

## Latest session summary (read first)

- Current HEAD is 05e409b; working tree was clean before this increment. The earlier 556fdd9/two-local-commit state below is historical, not current branch evidence. This increment is still uncommitted.
- Independent baseline review ran build/settings253/native560/integration298 once; all exited0. It found R1 (a retry could start after expiry) and R2 (legacy cleanup could mask a save failure). The user explicitly approved both corrections and their tests.
- Implemented0.5.6: cap retry sleeps to remaining time and refuse another attempt after expiry; preserve both operational save/cleanup causes in a caller-compatible IOException. Private retry/cleanup boundaries support deterministic tests; no public API, preference path, token or paused-switch behavior changes.
- Once-serial validation passed: integration320/seven live mappings, settings253, native560, model379 and Reader self1868; VS MSBuild/DeployExtension=false and IDE build pass. Only existing toolbar VSTHRD010 warning. Independent closure separately rebuilt and ran integration320/settings253/native560 once; all exit0, R1/R2 closed, no new in-scope findings. These are isolated/synthetic checks, not new native Exp or full Phase3 acceptance. Full Save double-failure wiring is source-reviewed; tests exercise its real cleanup path with a locked file and injected original failure.
- Latest (2026-11-22, later): Settings is now a floating tool window (SettingsToolWindow/SettingsView) with apply-on-change (no Apply), a Settings gear in the reader header, theme-switch fix (reader re-applies on VSColorTheme.ThemeChanged, no frozen brushes) and accent-coloured Settings checkbox/slider (MainWindowActiveDefaultBorder, also used for the selected Concise/Detailed segment, button borders and text highlight; inherited checkboxes show the inherited value dimmed). Routes pass: native626, settings271, toolbar62, speech-options422; build deployed to Exp (hash matched). User is testing in Exp (dark/light/pink); local commit approved by the user, no push.
- Latest (2026-11-22): added `VsThemedStyles.cs` (self-contained EnvironmentColors-only styles; removed VsResourceKeys/ThemedDialog dependency), redesigned Settings (sections, label column, footer Apply/Reset), aligned the reader to the POC (Close button, segmented Concise/Detailed strip, icon buttons, Follow tint). Visual review outside VS (dark+light) approved by the user; temporary review harness removed. Build passes; native611, settings271, toolbar62 and speech-options422 pass. 0.5.7 VSIX (DLL hash C37CF7FA...16C8BC) installed in Exp 063a2394 and Exp launched with READER_PREVIEW=1. Later fixes: selected/hover/pressed states use an accent SystemHighlight border instead of light ToolWindowButtonHoverActive/Down fills (Follow, Concise/Detailed segment, combo items), and the enabled Settings toolbar gear uses ToolWindowText instead of GrayText. User confirmed the dark-theme control look is now correct in Exp. Still to verify: light theme in Exp, audible Preview, High Contrast/DPI. Open question: convert Settings from DialogWindow to a docked tool window (user noted Tools > Options is dockable); not started. No commit or push.
- Changed earlier this increment: Preview fix (SettingsWindow now uses new SpeechPreview, which waits for the worker's Completed/Faulted before teardown; previously the host was disposed at the options acknowledgement, cutting speech), NativeReaderView POC placement (header/Follow, version strip, paragraph rail, bottom transport + Stop) and themed button template (disabled Stop was light), NativeReaderRegressionTests (placement and disabled-Stop assertions, SettingsPreviewWaitsForSpeech, real worker), README and this state record. Build passed; native582, settings271, speech-options422 and toolbar62 passed; a temporary audible real-voice run was confirmed by the user and removed. Exp still has the pre-fix VSIX; no commit or push was made. No new reusable script was written.
- Approved continuation: one combined review after execution/UI increments2/3, with targeted tests after coherent edits. Wire Settings only; Mic/Speaker disabled. Audio policy applies to Preview and native reader. Global Reset restores defaults; Solution/Window clear overrides, with per-field inheritance. Complete automated/Exp testing before requesting remaining perceptual checks. Commit Phase3 only after acceptance/manual review, then Phase4a.

### Earlier checkpoint evidence (historical)

- 0.5.5: shared AtomicFileReplace.Publish (ReaderDocument.cs) retries Win32 1175/32/33 for 1s; used by ReplyVersionPreference.Save and ScopedSettingsStore.Save. New VerifyAtomicPublish in ReaderRegressionTests (integration 298). Evidence and limits are in DESIGN_DECISIONS 2026-11-21. After the fix: settings route 5/5 pass (253), native route 3/3 pass (560), integration 298 pass. Other routes were NOT rerun after 0.5.5 (user asked for targeted runs only). Not yet independently reviewed; a fresh reviewer should run settings and native once.
- Standalone stress showed %TEMP% fails 1.5-12% vs 0% in the repo bin folder. Real settings folder (%LOCALAPPDATA%\VsCopilotChatVoice) was NOT yet measured; process of the locker unidentified (needs admin tooling, e.g. Procmon, or a Defender exclusion experiment by the user).
- VoiceType source reuse permission granted by the user (DESIGN_DECISIONS). VoiceType repo is at C:\MyFiles\Git\VoiceType (rev 7c0b6f8, net8.0-windows, NAudio 2.1.0). A microphone (Realtek array) is present.
- Phase 4a plan subsection added to IMPLEMENTATION_PLAN (net48 external capture core, synthetic probe, gated hardware probe). Delegated agent f4a99ac2-6376-42b5-9443-b214d424173c stopped idle with NO files added or changed (an empty src\VsCopilotChatVoice.Capture directory was removed). Phase 4a has not started; do not reuse that agent, start a fresh one when approved.
- Docs not yet updated for 0.5.5: README (assertion counts, AtomicFileReplace), CODE_SUMMARY, KEY_FLOWS, ROADMAP, and the parked-blocker entries in IMPLEMENTATION_PLAN. Do that when the user confirms blocker closure.

## Remaining blockers (for the user)

1. Phase 3 manual gates: package-load verification for the isolated 0.5.7 Exp installation, audible Preview,
   live duck/suppress restoration, theme/DPI/High Contrast/accessibility checks and manual commit review. Automated Settings/runtime work is complete.
2. Real-chat integration: automatic reader reopen for a new live reply and live Concise/Detailed acceptance;
   these are deferred to Phase 6 and do not block Phase 3.
3. Phase 4: explicitly paused until Phase 3 manual gates and local commit are complete; no capture/model work here.
4. Main-instance installation and push remain separately unauthorized.

## Current focus and authorization

- Bounded PoCs1/2/3 and metadata-only Phase1 are complete/pushed. Shared extraction da8fd78 is pushed. Last verified HEAD556fdd9258dcbc203b5a00783bd586bdf56249f6; local transport bdb1328 and owner556fdd9 are two ahead/zero behind, not pushed.
- User authorizes autonomous remaining-phase work, relevant audit fixes and local commits when required. Park blockers and continue truly independent work; user will revisit blockers on return. Push/main-instance installation still require separate approval.
- Native0.4.8 and scoped-storage0.5.0 are implemented but reliable closure is PARKED. Do not commit/install an unaccepted aggregate, weaken assertions, retry-to-green or speculate about a common IO cause.
- External read-only capability0.5.2 scheduling correction is independently CLOSED. Phase 2 manual Exp checks for docking, keyboard navigation, themes, DPI and two-instance independence passed; live-reply reopen and Concise/Detailed checks remain deferred to Phase 6. Phase 3 automated work is complete pending manual gates; Phase 4/5/6/7 and the requested combined cross-phase review remain incomplete.
- Captured-history0.5.3 data foundation passes651 and fresh independent bounded review; unused, no feed/UI/Speaker/queue integration. Parent IDE build passes.

## Runtime boundaries

- Installed Exp0.4.5 has gated prepared-pair native reader/external speech; bounded floating X/worker cleanup/silent reopen passed. Manual Exp checks for docking, keyboard navigation, themes, DPI and two-instance independence passed. Automatic reopen after a new live reply and real-chat Concise/Detailed acceptance remain deferred to Phase 6.
- Settings is the only enabled toolbar action. Mic and Speaker remain disabled. No feed/history/insert/send,
  microphone, recording, transcription, models/downloads, VoiceType copying or global hooks is enabled.
- Metadata remains Debug + exact Exp + VSCOPILOTCHATVOICE_METADATA_POC=1, peer-checked hello/list/status with instance/session/client/epoch fences. No mutating/content/speech command.
- Standalone reader and ungated PoC behavior are preserved. All System.Speech stays outside devenv; VSIX bundles Reader as content, not an assembly reference.
- Scoped store, Settings UI and runtime snapshot now consume speech/audio/appearance values; speech remains external
  to devenv.exe. Capability CLI/query remains read-only. Captured-history is data-only with no feed or automatic playback.

## Latest verified increment: external capabilities0.5.2

- Shared SpeechCapabilities.cs and SpeechCapabilityQuery.cs link into Reader/VSIX/Tests; Reader-only SpeechCapabilityDiscovery.cs branches before window construction via --speech-capabilities. New --speech-capabilities-probe takes actual VSIX DLL.
- Schema1 explicit strictUTF8 JSON: actual engine/voice name/culture/enabled/nullable default metadata, rate -10..10, volume0..100, no pitch. Zero Speak/voice-selection/preference writes. Five enabled voices/default available were observed without publishing names.
- Bounds: stdout65536/stderr8192 bytes,64 voices, names256/cultures85 UTF16 units, caller timeout0<value<=30seconds, cleanup waits share2seconds. Direct CLI has no watchdog; native Start/Kill/dispose are nonpreemptible, not an unconditional wall-clock guarantee.
- Independent0.5.1 review reproduced queued deadline100ms completing3554ms with Win32Exception.0.5.2 captures API-entry deadline, dedicated orchestration/drains and pool-independent waits/prelaunch expiry fence.
- Implementer/retained reviewer baseline152 pass once. Exact-DLL sole-worker starvation3503ms: missing executable15ms Win32Exception, hanging-child timeout127ms, cancellation127ms, child gone, expired entry0ms/zero starts after late callbacks. Original finding CLOSED/high confidence; no significant coupled finding.
- Supplemental reflection/fixture checks never executed and were not retried. Earlier real-child ten-second timeout lacks elapsed/PID/substage evidence; later375ms direct execution/passing probes do not establish its cause or cold-start reliability.
- Prescribed no-deploy MSBuild/package and parent IDE build pass, only existing toolbar VSTHRD010. Final once-serial capability152 passes; settings fails before253 at child File.Replace/VerifyProcesses270. Ten other routes pass: native560/playback220/speech110/toolbar62/bridge209/gesture209/contracts136/model379/Reader1868/integration292(7). Native pass does not close its parked gate.
- Reviewer41ea5e3f-dac6-45fd-8dd6-1c4ca37dc4bb and implementere25a32f0-4958-4d48-a39d-bdad2205b9cb are idle; no capability result pending. Zero review children/fixtures remained.

## Parked reliability evidence

1. Native0.4.8 final runs: NativeReaderRegressionTests226 captured Detailed model/radio correct, saved Concise, visible Unable to remove the file to be replaced. Shared legacy Save uses File.Replace; cause unconfirmed. Line255 subsequently failed Nested peer round trip lost its newest checked state/preference without precise state/cause. Isolated560 passes are not closure. Stopped implementer7601685d-8414-4071-922a-ee7b7db011a7; no speculative restart.
2. Scoped0.5.0 parent253/all eleven passed once, then independent shell736 exited1 at VerifyProcesses270 real rate/volume child: File.InternalReplace -> ScopedSettingsStore.Save271 -> Update183 -> UpdateGlobal172 -> Child338. Error Unable to remove the file to be replaced. Linked/actual-DLL leg/child unprinted; DLL matched; no retry/code change, isolated roots removed. Reviewer a6cd0b45-d06d-4b79-aa8e-ecbabdee7f91 idle.
3. Subsequent0.5.1 settings failed at VerifyStore151;0.5.2 failed again in child/VerifyProcesses270. Same message is not proof of a common root cause. No atomic-primitive substitution, preference fix or blanket retry.

## Preserved uninstalled artifacts

All under session-state\files, outside repository. Do not delete or install these investigation/review artifacts. Helper's comparison directory was build output, NOT an installed extension.

| Artifact | Version | Package SHA256 | Packaged/tested DLL SHA256 |
| --- | --- | --- | --- |
| p2-048-blocked.vsix | 0.4.8 | 4CD33242AC8F093E27DE34AB107A122C5B26E2E433A1C1116FB5CD29D4E48B3A | 17893AA98925BD6BAAC6CA8CFDD5752352E6D4701DE50D16A65FB8BFA2FDE010 |
| p3-050-blocked.vsix | 0.5.0 | 922F0CF4F06D93C4E9CAC0CA75086AA625BA02D11A111C5293FD04CF9A3D1F72 | 02C72B03744796077D7323687A569870583780421780496D617224EC02CFE1AA |
| p3-051-review.vsix | 0.5.1 | 97BC18C9FFC631315F385F1A08F318351FE09C4108C4D0DFE9ABF06074FF214A | 662BEA99EEB8564BFBB3F2755AA669E8E6F05DD09DFEF7ED5197C02D664E96CD |
| p3-052-review.vsix | 0.5.2 | 306881A0B05CC65B1F4C28BBF8818C68DFBA3ADD378A34AE907DA8A2CA180E46 | 056BC328D55715405725A4927842C4EDE50EBD8C4FBC03701D8C43498BE2FAFB |
| p6-053-review.vsix | 0.5.3 | F97A41F10C1A047E4CE1011E52F8386D3841FEC2A32D80524801014C7D35D1BA | A5EBC1979A6E34C0411D559025976B65C6FD84AD8E71814F9576BE8326055289 |
| p3-054-review.vsix | 0.5.4 | A257332D6836403565CD4DAF27EBB81AAEE5F27C9FB9CB23AB4C16BBF05920DD | 55874C4D5435C223BD1D5BFD32527672C9EFE9AF368B4BC685DEEA11A4F9EB1E |

0.5.2 packaged/tested Reader SHA58ABE76C952FADE414ABCE76EB8B830550E3620D1FEB3B945E2D548AC3809772. No new staging/commit/push/install this continuation.

## Latest history data evidence

- CapturedReplyCatalog.cs/internal History types bind immutable instance/session/ordinal-client identity; caller DTO completion is only an assertion. Detailed required/Concise optional, immutable deep copies, exact ID+ordered variants+nullable metadata equality, explicit Added/Duplicate/ConflictingReplyId/CapacityExceeded. Capacity failures preserve all state; future UI must handle results.
- 64replies/1M raw Text units/128blocksvariant/100Kblock/128-unit reply-anchor IDs/256-unit client/1024 metadata. First arrival selects0; later arrivals retain selection and set new-arrival flag. Previous/Next enter manual even at bounds; Latest clears flags. Locking, snapshots, clear/dispose and no payload persistence/WPF retention.
- ReaderBlockValidation.cs extracts header/number/tokens/readability/section checks into pure helper, linked into Reader/VSIX/Tests; catalog onlyVSIX/Tests. Legacy parsing/permissive IDs-numbers/prefix-only steps/order/messages/format/offsets remain unchanged; production-only restrictions stronger.
- Implementer f0470cdc-dd0d-4e02-884c-a375ccbf582a and reviewer37ac4008-3930-4592-a680-670acb8b249d idle; history651 (318linked+319actual-DLL+14legacy) passes once independently, no significant finding/legacy difference, no review fixture/process. Parent IDE build passes. Twelve preserved routes ran once: settings fails existing child File.Replace270, eleven others pass. No native/feed/trust/Phase6 acceptance.
- Packaged/tested0.5.3 ReaderSHA9024836D10F883972B56BFDA5C6F849E93AC3622BD4B045C957E9838E2CDDE03. Frozen package comparison used bin, not installation; all source remains uncommitted.

## Latest independent item: speech options 0.5.4 (review CLOSED)

Implementer74a831e7-1e74-4e8e-9863-0a2bd2742067 and fresh reviewer97693598-59c7-4e80-88da-573b525f7f03 are idle. Reviewer ran the options probe once (422 = 186 linked + 187 actual-DLL + 49 shared real-worker checks), found no significant issue; binary/harness hashes matched, no owned Reader/fixture remained. Not wired to any consumer; no audible/Exp/native evidence is claimed.

## Next steps for the new chat

- Complete the remaining Phase 3 manual/official Exp gates and perform the local commit review; do not install into main VS or push.
- After Phase 3 commit, begin Phase 4a capture only: external net48 lifecycle probe, bounded WAV/PCM statistics and hardware probe gated by VSCOPILOTCHATVOICE_CAPTURE_HW=1; no transcription/model work.
- Phase 2 real-reply reopen and live Concise/Detailed acceptance remain deferred to Phase 6. Phase 5 input modes, Phase 6 delivery and Phase 7 hardening remain future work.
- Phase status: 1 done/pushed; 2 shared extraction done with native/live-reply caveats; 3 automated Settings work complete pending manual gates; 4 paused; 5/6/7 not started for production behavior.

- Plan: immutable voice/default/rate/volume snapshot, distinct configured-speech protocol + unique Host API, actual external voice availability/selection at new prompt boundaries, explicit correlated failures/applied evidence, no missing/ambiguous fallback or midprompt/Windows mutation/default bleed into legacy.
- Existing signatures/legacywire/generation/pause-resume/epoch/offset/lifetime behavior preserved; real silent worker checks, no audible output/voice identifiers in summaries. Current store/toolbar/reader backend not wired; native/persistence gates unchanged.
- Owns only needed new shared/protocol/host/Reader-worker/test/Compile/route/manifest. No catalog/helper/parked/native/owner/doc/script/skill edits. Parent reconciles docs/artifacts after report and requests fresh bounded independent review. No installation/commit/push.
- Implemented snapshot/unique SpeakWithOptionsAsync, strict speak-options/applied/query framing; worker retires old prompt and checks actual enabled unique voice/getters before current progress, legacy restores initial defaults. No current consumers. Final no-deploy build/options422 pass; settings child File.Replace270 fails unchanged, twelve other preserved routes pass once. Initial fixture fixed first-frame vs current-generation assumption; no production retries. Packaged/tested ReaderSHA361F7F452F805C00E4B1DFA42794D9E4A2250267A05E858CDE9871EE6F33E773; helper compared bin, not installed.

## Working tree

Uncommitted native/storage/capability/history source plus parent documentation/helper remain intentional. Last observed changes include README; docs CODE_SUMMARY/DESIGN_DECISIONS/IMPLEMENTATION_PLAN/KEY_FLOWS/PROJECT_STATE/ROADMAP/SCRIPTS; Reader entrypoint/csproj; VSIX owner/toolwindow/csproj/manifest; harness/playback/native/settings/capability/history tests/csproj; shared identity/storage/capability/query/catalog/ReaderBlockValidation and ReaderDocument; Reader discovery; NativeReaderPreview/NativeReaderView/ReaderFrameLifecycle; scripts\agent\Test-VsPackageActivity.ps1. Speech-option 0.5.4 is verified and reviewed. Never stage docs\history-of-astronomy.md, personal settings, reply/audio, models, proof logs/artifacts or secrets.

## Resume safely

- Read README, this state, IMPLEMENTATION_PLAN section6, DESIGN_DECISIONS, CODE_SUMMARY, ROADMAP and instructions. Historical detail lives in plan/decision docs; state above is authoritative.
- Graph-first wrapper remains registered. Graph lacks newer worker/native/settings/history symbols and has stale extracted-model ReaderPrototype locations. State gap then use current source; do not read graph wholesale/regenerate speculatively. VSIX history lookup found only unrelated cleanup queue, no history prior art. Feature/domain checklists absent at last check.
- No live VS/UI action this continuation. All task Exp were closed; main35204/skills33164 remain protected. Reidentify processes/HWNDs/installed directory before any future action. Never reuse retired Exp9776/worker40820/HWNDs/instance tuple. Close only owned Exp gracefully with CloseMainWindow/wait.
- Install only reviewed reliable candidate using VSIXInstaller /quiet /rootSuffix:Exp /instanceIds:063a2394, never /shutdownprocesses; then devenv /RootSuffix Exp /UpdateConfiguration and fresh Exp/dedicatedLog. No current aggregate candidate is accepted; no main/automatic deployment.
- Clear DOTNET_ROOT; build with `& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' .\VsCopilotChatVoice.slnx /restore /m /v:m /nologo /p:Configuration=Debug /p:DeployExtension=false`. Run all fourteen current README routes serial/isolated; record parked failures and continue others without retry-to-green. Do not build concurrently with preference/process probes.
- Before approved commit: README/manual staged review; UTF8Encoding($false) message file with required Co-authored-by trailer, git commit -F, no push. Avoid sweeping unaccepted gates into a checkpoint.
- VoiceType local7c0b6f8 comparison remains read-only, no tracked LICENSE/NOTICE/COPYING found; permission is parked before copying, no runtime parity claim. Hardware/model provisioning/manual native layout/theme/focus cases remain for user return.
- Normal TTS on, autoTTS/push off; ready-handoff script/Windows default voice. No cursor/screenshot/pending speech. Skills repo/installed skills untouched. No new reusable mechanical script worth saving; existing helper/SCRIPTS date2026-11-21.
