# Project state

Updated: 2026-11-21. This file is overwritten, not appended, at the end of each working session.

## Current focus

Latest increment: independently reviewed 0.2.10 passive native observation and automatic
terminal teardown are installed and live-validated in Exp. PoC 1 remains incomplete: native
guarded insertion/send and completed-reply delivery are the next required capabilities.
The following progress notes retain earlier checkpoints; the latest 0.2.10 results supersede
their pending-review/install statuses. No microphone or later production phase has started.

Foundation and approved toolbar preview are committed locally through `ee55bb6`; no push.
The user ended the design discussion and requested an implementation plan. The new draft
`docs/IMPLEMENTATION_PLAN.md` captures microphone modes/gestures/recovery, a native reader
Tool Window with external speech, scoped settings, reply policies/history and experimental
chat synchronization. Confirm later phase gates before production migration. The user
authorized PoC 1: a gated, read-only diagnostic bridge has been added; no microphone/reader
migration has started.
The user wants the plan to evolve during implementation/testing. Proposed Phase 1 PoCs cover
exact chat bridge/identity, native tool-window/external speech, and safe Ctrl/capture lifecycle;
the first is the highest-risk required capability. Optional gesture/scroll failures can defer
those features without blocking the rest. The first PoC is partially validated; others have
not been executed. Foreground access recovered after the desktop obstruction was removed.
A new disposable Exp chat was created. A rendered workspace-trust warning initially blocked
input/send testing; the user explicitly approved trust, and native Trust once removed it.
The 0.2.6 read-only diagnostic now inspects the three observed binding paths by one level.
Independent review reproduced one new medium-severity inherited-property lookup defect;
the user approved appropriate fixes autonomously. The 0.2.7 fix passes added regressions and
independent re-review; it is hash-verified in Exp. Live bindings expose a typed CopilotSessionId,
PromptInput and ChatEvents. Native identity now survives submission, switching and reopen in the
live proof. Do not claim an atomic guarded native input/send or reply-delivery bridge yet.
- Stable local checkpoint `50938af` saves the independently reviewed 0.2.7 diagnostic, 78/62
  passing checks, helper scripts, graph and agreed implementation plan. User authorized stable
  checkpoint commits; no push performed. Worktree was clean immediately afterward.
- The 0.2.8 increment extracts only verified CopilotSessionId.Guid/ClientId.Id and inventories
  Composer.PromptInput/ActionButton; no draft/reply read or mutation. Synthetic shape fixtures
  verify no arbitrary stringification, client-ID bounds and no draft/unrelated getter calls.
  Build and 84 probe/IPC + 62 toolbar checks pass. Actual package/DLL SHA256:
  `76611A1E5A14125E0688EBFC12083269EB4AD468BB038C47AF5C9BDB1C4D74BC`.
  Independent review and Exp-only update are underway; tracked shell `bridge-poc-028-install`.
  Read its result, rediscover the new Exp, verify native typed IDs and prompt-input metadata.
- Latest 0.2.8 review: no high-confidence correctness/privacy/boundedness findings; independently
  passed 84 probe/IPC + 62 toolbar assertions and verified package/hash. Exp installation/refresh
  exited 0; tracked shell completed. Current transient Exp PID 3048 / HWND 0xB11380 (reverify).
- Native typed identity extracted from a fresh disposable chat; remained identical after Trust
  once, user-title change, one generated prompt submission, switching and history reopen. A
  second fresh chat had a distinct Guid with the same client namespace. Asynchronous creation
  required actual input/binding read-back rather than assuming an Invoke success was settled.
- Controlled UIA smoke: exact harmless draft was read back through TextPattern, native target ID
  checked before/after insertion and before Send, one enabled visible Send invoked, user prompt
  appeared, draft cleared, and assistant returned exactly POC_BRIDGE_OK in the same chat. The
  reply persisted after history reopen. No existing user conversation, repository code, tools,
  recordings or models were used. This is not a native voice/pipe insertion/delivery API.
- Foreground entry initially failed before typing; supported verified foreground fallback
  recovered, with an empty-draft check before retry. A helper parameter name conflicted with a
  script-local Session variable during trust read-back; recovered by renaming the variable,
  not repeating the successful trust action. No submission was retried.
- PromptTextInputViewModel.Prompt has public string getter/setter and INotifyPropertyChanged,
  verified from installed SDK metadata. Next bounded step: native UI-thread guarded command
  prototype with target generation, monotonic draft revision and responding/trust readiness;
  then completed-reply delivery and negative/lifetime matrix. The diagnostic pipe is still
  inspect-only; PoC 1 remains incomplete. No new permission is needed for approved PoC work.
- Local checkpoint `1755792` saves reviewed 0.2.8 source and live smoke evidence; installed
  hash was reverified before commit. Worktree was clean and main-host foreground restored.
- Current 0.2.9 slice adds passive `ChatTargetObservation`: observation epoch, content-free
  Prompt-change counter, trust/responding streams and lifecycle invalidation. Unknown state
  stays null; late callbacks cannot restore invalid targets. Build and 126 probe/IPC + 62
  toolbar assertions pass. The standalone harness exposed a VS-only JIT dependency in disposal;
	an injected UI scheduler preserves the SDK-free observer/IPC routes while native cleanup
  uses VS joinable-thread switching. Background disposal, transient input replacement and
  revision exhaustion pass. Only the two pre-existing analyzer warnings remain. Independent
  review and live 0.2.9 SDK validation are pending. No command or draft/reply content API added.
- Installed public SDK metadata confirms root/prompt PropertyChanged, but not composer
  PropertyChanged. Initial composer-interface requirement was incompatible with native VS;
  corrected to report HasInputReplacementNotifications=false without disabling other passive
  observations. This gap is not waived for mutation. Input lifetime and atomic guards remain open.
- User-authorized read-only reference check of CopilotUsageMeter confirmed UI-thread discovery,
  active-chat polling and teardown patterns. Its read-only usage-context polling does not prove
  safe prompt mutation; private identity/stringification logic was not copied or modified.
- Independent review reproduced one medium 0.2.9 defect: target closure retained all five
  subscriptions and native objects until a later inspect/disposal. The added closure regression
  failed on the unfixed DLL. Version 0.2.10 automatically queues terminal teardown, defers it
  during construction, clears native/model/scheduler references and preserves terminal reasons.
  Build and 138 probe/IPC + 62 toolbar checks pass, including synchronous constructor completion
	and background terminal cleanup without client activity. Independent re-review found no new
  in-scope findings and reran 138/62 successfully. Its actual background ChatBridgePoc.Dispose
  test verified observer-slot release, UI subscription/reference cleanup, OwnerDisposed/null
  readiness, completed pipe worker and zero reported errors.
- Official 0.2.10 Exp installer/configuration refresh exited 0. Manifest/package/installed DLL
  match SHA256 `7246031262823FC2F9FE93BFDB5BAA07DED5CBA578EFDE26EFC1A32CD6AB0BDA`.
  Transient task Exp PID 36168 / HWND 0x341460; reverify before reuse. Initial Sessions screen
  returned NoNativeTarget/null state. A new disposable chat exposed trusted/responding false,
  read-only/transcript false and HasInputReplacementNotifications=false. Trust once changed
  trusted false-to-true without changing native Guid or observation epoch.
- Native A-B-A draft edits produced monotonic revisions 11, 23, 35, then 36 after verified
  clearing. Each generated draft was read back through TextPattern. Initial foreground entry
  needed bounded recovery; a later immediate mismatch settled correctly on fresh read-back,
  so it was not retyped. No RDP cause was inferred and no uncertain Send was retried.
- One exact generated prompt returned POC_BRIDGE_STATE_OK. Its visible user/assistant entries
  and empty draft were verified; responding changed true-to-false, revision remained 110 and
  native Guid/client plus epoch stayed unchanged. Native history navigation returned
  NoNativeTarget/null observation. A failed guessed history-label prefix caused no action;
  filtered inspection recovered the observed Active session label. Reopening only that generated
  chat preserved native identity/reply with a fresh epoch and revision zero. This is UIA smoke,
  not automatic native reply delivery or command acceptance proof.
- Original main host PID 29572 / HWND 0x311C0 was restored and verified foreground after the
  live batch; the green UI cursor session was stopped. No normal-instance installation occurred.
- Latest registered graph refresh analyzed three changed files: 348 nodes, 2,243 edges,
  14.1 seconds. Generated dependencies were renamed immediately; commit metadata records
	existing checkpoint 1755792 rather than a fabricated future commit. Only Exp was updated.
This changes earlier planned architecture, not current code: the reader is still standalone
and all three toolbar buttons remain disabled. Full Exp lifecycle/theme/DPI/accessibility
checks remain pending. Approved lifecycle fix 1 passed independent re-review; test-only glyph
fix 2 remains unapplied. Only the task's Exp instance is an installation target.

## Verified behavior

- Diagnostic solution build and targeted checks pass: 56 probe/IPC assertions and 62 unchanged
  toolbar assertions. Both PowerShell helpers parse. The new probe route is not a native
  Test Explorer test; the existing executable harness remains the validation mechanism.
- Official installer logs confirm 0.2.4 installed only under the Exp hive. Installer process
  exit code was not reliably captured; do not claim an observed installer exit 0. A subsequent
  waited Exp configuration refresh exited 0. Installed DLL SHA256 matches the tested build.
- Fresh explicitly enabled Exp accepted a diagnostic pipe read through server-PID verification:
  1,561 WPF nodes, zero matching conversation controls while Chat displayed the Sessions list.
  This proves live pipe/UI-thread observation, not a stable conversation identity. No insertion,
  submission, skill activation or automatic reply capture has been performed.
- Main host PID 29572 was not closed/installed. Initial failed input attempts restored the
  cursor indicator; a disposable chat was subsequently created after foreground access recovered.
  Independent diagnostic audit found two medium-severity listener defects;
  parent verification reproduced both and the user approved both fixes. The 0.2.5 source now
  rejects per connection and disposes timed-out pipe operations; 56 actual probe/IPC assertions
	and 62 unchanged toolbar assertions pass. Independent re-review confirms both resolved with
	no new high-confidence findings. Both fixes are now verified in live fixed-build Exp.
  The user reports the desktop obstruction removed and authorizes continuing Exp testing;
  retry foreground access once and return focus to the main host before final handoff.
- Foreground access subsequently succeeded. Task Exp exited normally for update. The first
  update/refresh both exited 0, but the auditor identified the stale 0.2.4 packaged manifest.
  Explicit no-deploy Rebuild corrected the actual VSIX to 0.2.5 with the same audited DLL hash;
  56 probe/IPC and 62 toolbar assertions passed again. Rebuild emitted two existing VSTHRD010
  warnings on toolbar refresh/package disposal; no unrelated edits made.
- Corrected-package installation and configuration refresh both exited 0; the installed Exp
  manifest is 0.2.5 and DLL SHA256 matches the audited package. Tracked installation shell
  `bridge-poc-025-verified-install` completed; no installer remains pending.
- Live Exp rejected a malformed request and remained available. Its production 15-second
  deadline terminated an idle client after 15,006 ms; another inspection succeeded while
  the idle client remained open. No test input was sent to Chat.
- A new disposable chat exposes `HarnessSessionViewModel` with public `Session`, `Composer`,
  `ChatLog` and `IsResponding` metadata. No stable identity is exposed by the shallow whitelist.
  The trust-warning UIA element has rendered bounds (795 by 53 pixels), not just a hidden label:
  "Chat is disabled because this workspace is not trusted." Trust was not changed. No prompt
  was entered/submitted and no reply was captured. A scoped screenshot could not be viewed
  by this session provider; it was deleted, and no visual claim relied on it.
- The user then explicitly approved workspace trust and continued testing. Native Trust once
  removed the warning; no permanent folder-trust action was used. No prompt has been entered/sent.
- The 0.2.6 probe adds one-level Session/Composer/ChatLog binding metadata and public inherited
  interface identities. New checks cover explicit-interface IDs, private getter exclusion,
  missing bindings and no deeper/content reads. Build and 64 probe/IPC + 62 toolbar assertions
  pass. Actual ZIP version 0.2.6 and DLL SHA256
  `565E5D1A4E6C217542EF7B070477E1AA975B821D66EC544B95BFBB72FFBA3152` match.
	Two existing VSTHRD010 warnings remain unchanged. Independent re-review reran both prebuilt
  routes successfully and reproduced AmbiguousMatchException when a derived Session property
  hides an inherited property with a different return type. Name-only lookup at lines 192-197
  throws; the exception escapes listener handlers and permanently faults the worker. Proposed:
  deterministic inherited-property resolution, per-connection containment and regression.
  No fix applied; explicit user approval is required. No other high-confidence findings.
- The user then authorized appropriate fixes without routine approval stops. The new hiding
  regression failed against the actual 0.2.6 DLL, reproducing AmbiguousMatchException. Fixed
  0.2.7 chooses nearest declared public bindings, refuses private shadowing getters without
  falling back, and contains ambiguity per connection. All 78 probe/IPC and 62 toolbar assertions
  pass, including injected reflection-capture failure and next-client recovery. Build succeeds;
  the two existing analyzer warnings remain. Actual 0.2.7 package/DLL SHA256:
  `B481058BF7C7289530A370B52A740264876E2ADAD38822711B0CE9D6C5232901`.
  Independent re-review and Exp-only installation/refresh/launch are underway; tracked shell
  `bridge-poc-027-install`. Read that result, rediscover its new PID/HWND, then inspect bindings.
- The 0.2.7 re-review confirms the collision resolved with no new high-confidence in-scope
  findings. Independently reran 78 probe/IPC + 62 toolbar assertions and verified package/hash.
  Installation/refresh exited 0; `bridge-poc-027-install` completed. Installed DLL matches.
  Transient current Exp: PID 42720 / HWND 0x4C1036. The prior startup modal disappeared naturally.
- A disposable chat was created and verified by read-back after asynchronous creation settled.
  Live session binding is CopilotSdkSession with SessionId: CopilotSessionId, ChatEvents and
  SessionCommands; Composer exposes PromptInput and ActionButton, and ChatLog exposes Entries.
  No draft/reply contents were read or changed. SDK metadata shows CopilotSessionId is a value
  type exposing Guid and ClientId; CopilotClientId exposes public string Id. The two installed
  SDK assembly copies match by SHA256. OS module enumeration omitted managed assemblies;
  constrained installed-file discovery recovered the metadata without guessing a live ID.
- User permits stable local checkpoint commits and recovery attempts; if an approved task is
  genuinely blocked, record exact evidence and move to another approved task, not unapproved
  production phases. Skills-repository agent interaction is permitted when needed; not needed yet.
- Required VoiceType source-copy permission is granted for the microphone phase. No source or
  models have been copied, no model download is authorized, and later production phases remain
  planned rather than started. No normal-VS install, commit or push occurred.
- Exp-only 0.2.6 installer/refresh exited 0; tracked `bridge-poc-026-install` completed.
  Installed manifest/hash match. Initial live probe returned 1,621 nodes with no selected
  conversation controls on the Sessions list. A small untitled modal window was discovered;
  it was not dismissed or otherwise acted upon before the audit approval gate.
- Transient targets, reverify before reuse: task Exp PID 42428 / HWND 0x9C12B6; its untitled
  modal HWND 0x3C0FAE; main host PID 29572 / HWND 0x311C0. Old Exp PID 32656 closed normally.
  Before trust approval, main-host foreground was restored through the verified fallback and
	confirmed; the cursor indicator was cleared. Main-host foreground was restored and verified
  again at the audit-approval handoff, with the cursor indicator cleared.
- Diagnostic incremental graph refresh analyzed four changed files across two projects:
  186 nodes, 1,567 edges. Dependencies were renamed to the registered slug; no commit metadata
  was fabricated and no commit/push was performed. A subsequent incremental refresh after the
  IPC/nested-binding changes analyzed two files across two projects: 224 nodes, 1,755 edges,
  21.33 seconds including the registered dependency rename. No stale unqualified output remains.
  Latest approved-fix update: two changed files, 237 nodes, 1,813 edges, 15.1 seconds including
  the registered dependency rename. Commit metadata reflects the pre-checkpoint HEAD.
  Latest typed-ID update: two files, 260 nodes, 1,876 edges, 7.9 seconds including dependency
  rename. Graph commit metadata references checkpoint 50938af, not a fabricated later commit.
- Full solution/VSIX build and rebuild passed during foundation validation.
- Grouped-toolbar solution build passed. Its isolated actual-VSIX harness passes 62 assertions;
  reader checks also pass 1,868 self-tests and 292 integration assertions/seven mappings.
  Initial toolbar lifecycle testing exposed retained controls after hiding; corrected discovery
  now revalidates visibility and uniqueness before retaining an attachment, with passing checks.
- Compact sizing, borderless icons and the themed group border have deterministic checks;
	the 0.2.3 group measures exactly 24 DIPs high with unchanged 14-DIP glyphs. An initial
  auto-sized attempt exceeded the target due to DPI rounding; explicit outer height corrected
	it and all 62 toolbar checks pass. Official 0.2.3 Exp update/configuration refresh exited 0;
  the package loaded and the user approved the tighter-border appearance.
- Latest independent review passed the solution build and all 62 toolbar assertions. It found
  a low-severity test-observation gap: the glyph-height predicate permits collapsed/zero-height
  content and does not prove unclipped rendering or unchanged glyph identity. Proposed test-only
  fix 2 was not approved/applied; no production clipping defect was established.
- Audit fix 1 revalidates supported host/wrapper placement and competing toolbar IDs before
  retaining a settled attachment. The added regression first failed against the unfixed DLL
  on an attached host becoming vertical, then all 58 toolbar assertions passed after the fix.
  Tests also cover a competing group introduced after attachment, wrappers acquiring unrelated
  children, owned-control preservation and recovery. No reader production code changed.
- Official 0.2.0 Exp update/refresh exited 0; a fresh launch logged attachment and showed
  three disabled controls with Speaker off. Live geometry confirmed dialog-style minimums
  enlarged Mic/Settings; sizing and shared-group styling address that observed presentation.
- Reader checks previously passed: 1,868 self-test assertions and 292 integration assertions,
  including seven live progress mappings and isolated child-process restart preferences.
- Pre-commit validation: solution build and 1,868 self-test assertions passed. The first
  unchanged integration-harness run failed at `VerifyPreferences` with "Selecting Detailed
  did not replace Concise on disk"; an immediate separate rerun passed all 292 assertions
  and seven mappings. Treat this as an unresolved intermittent result, not a clean first pass.
- Graph Bootstrap completed for `VsCopilotChatVoice.slnx`: 119 nodes, 1,115 edges in 7.49 s;
  all three projects analyzed. Slug `vscopilotchatvoice`; dependencies renamed after success.
  Graph commit metadata references HEAD at generation, not a guarantee of worktree cleanliness.
- Prior incremental graph update includes the grouping/style change: three changed files,
  154 nodes, 1,352 edges in 5.15 s; generated dependencies renamed to the registered slug.
- Approved lifecycle-fix graph refresh analyzed two changed files: 154 nodes, 1,415 edges
  in 11.28 s; generated dependencies renamed to the registered slug.
- Border-height graph refresh analyzed two changed files: 154 nodes, 1,417 edges in 4.74 s;
  generated dependencies renamed to the registered slug.
- Independent final regression audit completed: solution build, 1,868 self-test assertions
  and 292 integration assertions/seven mappings passed on the auditor's first executions.
  Its only finding was misleading graph-viewer search wording. The wording-only correction
  was reviewed and committed upstream in the skills repository (`40ec7da`); this repository's
  viewer copy is refreshed from that source. No reader or VSIX runtime defect was established.
- Fresh Exp activity log contains source `VsCopilotChatVoice` and the foundation-loaded
  message. The experimental IDE was responsive. Normal VS has no matching installed extension.
- SDK deployment failed with VSSDK1031; the official Exp installer and waited configuration
  refresh exited 0. Stalled read-only registry probes were stopped; no hive reset performed.

## Open tasks / limitations

- PoC 1: resolve native command parameters/acceptance, exact input-replacement lifetime and
  atomic target/draft/readiness guards; prove completed-reply delivery and negative/two-instance
  matrix before declaring completion. The diagnostic pipe is inspect-only.
- 0.2.3 Mic/Speaker/Settings controls are intentionally disabled; earlier placement/lifecycle
	and grouped-style checks pass, but fresh Exp chat close/reopen and theme/DPI/accessibility matrix still
  need verification. Unsupported/ambiguous layouts report unavailable instead of unsafe insertion.
- No persistent reply feed or IPC/toolkit/instance routing. Companion policy source was
	authored, independently reviewed, installed with matching source hashes, and committed as
  `a654406` in the separate skills repository; no push. It does not supply the missing
  production bridge or automatic Speaker state.
- Reader is still the separate prototype: pale-yellow theme, one prepared reply per launch,
  X closes rather than production hide behavior; no production show/hide/minimize protocol.
- No voice/rate/volume settings window, automatic-read-aloud preference or VS theme sync.
- Planned selective VoiceType source reuse supersedes optional-app discovery/launch; see the
  draft plan for external host/model boundaries, gestures, ownership, cancellation and targeting.
- Native reader presentation, external speech separation, scoped settings and captured-reply
  history are planned only. Auto-send On, five-minute review stop, latest-on-enable and
  finish-current-then-latest are explicitly provisional plan defaults, not separately confirmed.
- Automatic Copilot-response capture has no verified mechanism; do not imply otherwise.
- Intermittent preference-persistence harness failure needs investigation; no reader/test
	fix made during documentation Bootstrap. Passing audit checks do not resolve that observation.
- Foundation commit completed after review; the user authorized the reviewed toolbar checkpoint
  commit with the documented remaining validation/test limitation. No push authorized. Repository-local commit
  identity and origin username were configured as requested. Main-instance installation
  still requires completed Exp testing plus explicit approval.

## Recently changed files

- `ChatTargetObservation.cs`, `ChatBridgePoc.cs`, `BridgePocRegressionTests.cs` and VSIX manifest:
  0.2.10 passive native-state observation, automatic terminal cleanup and 138 actual-DLL checks.
- README, implementation plan, memory/flow docs and registered graph pair: reviewed behavior,
  native trust/revision/busy/history evidence and unresolved guarded-send/reply boundaries.
- `docs/IMPLEMENTATION_PLAN.md`: consolidated design scope, seven phases, acceptance gates,
  defaults, negative scenarios and optional experiment. Documentation-only; no new runtime tests.
- `README.md`, `ROADMAP.md`, `DESIGN_DECISIONS.md` and contributor instructions: align planned
  native-tool-window/source-reuse boundaries and settings scope without describing them as shipped.
- `src\VsCopilotChatVoice.VisualStudio\ChatToolbarIntegration.cs`, `VoicePackage.cs` and
	`source.extension.vsixmanifest`: new controller, package lifetime/theming/logging and 0.2.3 identity.
- `tests\VsCopilotChatVoice.Reader.Tests\ToolbarRegressionTests.cs`, `ReaderRegressionTests.cs`:
  optional isolated toolbar mode; default reader test behavior unchanged.
- `README.md` and memory docs: current preview behavior, exact validation and remaining gates.

## Resume safely

Read `README.md`, `CODE_SUMMARY.md`, `DESIGN_DECISIONS.md` and `ROADMAP.md` before implementing.
Use the graph registry in `.github\copilot-instructions.md` for C# symbol lookups.
Re-identify VS windows/processes after restart; do not persist or reuse transient HWNDs/PIDs.