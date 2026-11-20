# VsCopilotChatVoice contributor instructions

- Read README.md before making changes; it documents the current foundation and approved direction.
- Update README.md whenever repository changes are made, and review/update it again before
  every commit. Keep setup, project layout, build/test commands, behavior, limitations and
  pending features accurate. Distinguish planned capabilities from implemented ones.
- Keep speech and reader execution outside devenv.exe. The VSIX is the thin IDE integration;
  VoiceType remains a separate optional dictation backend, not a copied engine dependency.
- Preserve existing reader behavior during migration, including remembered Concise/Detailed
  choice and paused current-reply version switching. Tests must use isolated preference paths.
- Build with Visual Studio MSBuild and run the relevant regression checks documented in README.md.
  Documentation-only updates do not need a rebuild unless tested examples change.
- Keep automatic extension deployment disabled. Validate extension runtime changes in an
  experimental VS instance before any approved main-instance installation.
- Do not commit or push without explicit user approval. Do not include personal settings,
  reply content, speech recordings, model binaries, build artifacts or secrets.

## Project Guidelines

- Manual commit review before any commit.
- Build/test verification after every change, using the existing documentation-only exception above.
- Do not commit or push automatically - wait for explicit user confirmation first.
- This project has a WinForms/WPF UI. If you need to debug it in Visual Studio, use the
  `vs-debug` skill (also independently useful for debugging tasks not reached via UI
  navigation). If you need to navigate through the running application to reach a
  specific screen or feature - including as part of debugging it - use the
  `ui-navigation-orchestrator` skill, which composes `vs-debug` and `ui-interaction` for you.
- If the `vs-chat-history` skill is installed, use it to search earlier Visual Studio
  Copilot Chat conversations on this machine (for example "what did we decide about X",
  "which chat worked on Y"). It reads `session-store.db` only, and chat text is
  confidential: show short excerpts only. Ignore this note if the skill is not installed.

## Response Guidelines

- Keep replies concise and minimal: no filler, restating the question or unnecessary preamble.
  Give full detail for design rationale, build/error diagnosis, before destructive actions,
  when multiple approaches exist, and when generating `docs/*.md` content itself.

<!-- project-memory-management-graph: skill-version=28 -->
## Persistent Project Memory

Regenerate this entire section, including the version marker, on every graph-memory Bootstrap.
The Project Guidelines and Response Guidelines sections remain merge-safe/additive-only.

- Read `docs/CODE_SUMMARY.md` and `docs/DESIGN_DECISIONS.md`, when present, before exploring
  the codebase with search tools for a new task. If absent, use normal exploration.
- Read `docs/PROJECT_STATE.md` and `docs/ROADMAP.md`, when present, for prior-work or next-step questions.
- Read `docs/domain-lookup-patterns.md`, when present, before investigating domain conventions,
  naming schemes or business logic the structural graph cannot represent.
- Read `docs/FEATURE_CHECKLIST.md`, when present, when planning or adding a feature.
- Query registered graphs through the confirmed wrapper
  `C:\Users\sveluswa\.copilot\toolkits\GraphTools\tools\Invoke-GraphTools.ps1`, invoked as
  `& '<wrapper>' -Tool Query -- <args>`. Never read the graphs wholesale.

| Solution path relative to repo root | Slug | Graph file | Dependencies file |
| --- | --- | --- | --- |
| `VsCopilotChatVoice.slnx` | `vscopilotchatvoice` | `docs/full-graph.vscopilotchatvoice.json` | `docs/project-dependencies.vscopilotchatvoice.json` |

- Graph-first is the default for locating or relating C# symbols, definitions, callers,
  callees and type relationships, including conceptual/explanatory questions. Before using
  general text/symbol search or grep, query the registry graph via the wrapper above. Use
  `--graph '<absolute-graph-path>' --list-symbols --project '<project-name>'` to discover IDs,
  then `--graph '<absolute-graph-path>' --symbol '<id>'`, optionally with
  `--direction callers` or `--direction callees`.
- Query the current IDE solution's graph first only when the solution is known; otherwise
  treat it as unknown. Then check other registered graphs. Label results with their solution
  and state when more than one solution contains a result.
- If no registered graph exists, a file is missing, Query fails, or no graph answers the
  question, state that explicitly before falling back to normal search. Report full errors;
  a missing symbol may mean a stale graph, not absent source. Fallback is not a blocking error.
- Mention recurring graph/domain-lookup friction briefly at the end of the reply; do not
  proactively investigate or implement tooling changes for it.
- Update `docs/CODE_SUMMARY.md` only for structural project/component/class/responsibility
  or dependency changes. Preserve its pointer to `docs/KEY_FLOWS.md`.
- Add fully traced, confirmed multi-symbol flows directly to `docs/KEY_FLOWS.md` as short
  arrow-chains, not to CODE_SUMMARY. Keep domain details out; reference an existing applicable
  `domain-lookup-patterns.md` entry only when one already exists.
- Append dated decisions/rationale/alternatives to `docs/DESIGN_DECISIONS.md` for non-obvious
  architectural choices, rejected alternatives or reversals. Never delete prior entries.
- Overwrite `docs/PROJECT_STATE.md` at session end with current focus, tasks and changed files.
  Edit `docs/ROADMAP.md` deliberately only when priorities/plans change. Keep memory concise.
- Reusable scripts: before writing code for a repeatable mechanical step (filtering a log, applying the same edit to many files, extracting data from several files), check `docs/SCRIPTS.md` if it exists and run a script that fits, as its index line says; if the index line says it modifies or deletes files, ask the user before each run, and check its output before relying on it. If you wrote working code for such a step that is likely to be reused with different inputs, is more than a one-liner, can take its inputs as parameters, and contains no credentials, tokens, customer data or personal details, save it as `scripts/agent/<verb-noun>.ps1` with a header comment (purpose, parameters, whether it modifies anything), add one line to `docs/SCRIPTS.md` (table: script | purpose | how to call | read-only or modifies | date; create the file with the table header if it is missing), and say so in your reply. Before saving, check whether an existing script in `scripts/agent/` can take the new case as a parameter. If one can, extend that script with a backward-compatible parameter (its default keeps the current behaviour, and existing parameter names, order and output format stay unchanged), update its header comment, index line and date, and say so, rather than saving a near-copy. If the task's file rules forbid changing the existing script, do not save a near-copy; say in your reply which parameter the existing script needs. Every part that varies between uses (paths, patterns, comment markers, row counts, exclusions) must be a parameter. For C# projects, skip `obj` and `bin` folders by default, with a switch to include them. Before finishing a task, check once whether you wrote any script worth saving. If a script fails or is out of date, fix it in place and update its date, or do the step normally; never loop on a broken script. When a task runs a listed script, set its date in `docs/SCRIPTS.md` to today if the task's file rules allow editing that file; otherwise leave the date unchanged and say so in your reply. The user curates these files: never delete scripts or index lines on your own.
- Read-aloud (TTS), off by default: when the user says "enable tts" or "start tts", invoke the `read-aloud` skill and follow its ready-handoff timing rules until the user says "disable tts" or "stop tts". Push notifications remain a separate opt-in mode. These modes apply to the current chat only.
