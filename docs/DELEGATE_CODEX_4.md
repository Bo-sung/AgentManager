# Delegation Brief — Codex #4: UI language setting (KO / EN)

Repo: `J:\prj\AgentManager` (WPF / .NET 10). Base on latest `main` (`git log --oneline -3` first).
This is a **mechanical string-extraction sweep** — no behavior changes.

## Architecture (follow the theme pattern, already in the codebase)
The light/dark theme works by merging a palette dictionary before `Theme.xaml` at startup
(`App.xaml.cs OnStartup`, persisted in `AppSettingsDto.Theme`). Language works the same way:

1. New dictionaries `src/AgentManager/Theme/Strings.Ko.xaml` and `Strings.En.xaml`:
   ```xml
   <ResourceDictionary xmlns=... xmlns:sys="clr-namespace:System;assembly=System.Runtime">
       <sys:String x:Key="L.NewAgent">New Agent</sys:String>
       ...
   </ResourceDictionary>
   ```
   Key convention: `L.` + PascalCase English hint (e.g. `L.DiffReviewToggleTip`).
2. `App.xaml` merges `Strings.Ko.xaml` by default (after Icons.xaml). `App.xaml.cs OnStartup`
   reads `AppStateStore.Load()?.Settings.Language` ("ko"|"en", default "ko") and, when "en",
   swaps in `Strings.En.xaml` (same re-merge approach as the light theme — keep palette logic intact).
3. `AppSettingsDto.Language` (string, default "ko") + Settings UI toggle `ENGLISH UI`
   (TogglePillButton next to LIGHT THEME, tooltip "재시작 후 적용") + persistence wiring in
   AppViewModel (`SettingsEnglishUi` prop, OpenSettings/SaveSettings/SaveState/RestoreState —
   copy the `_theme` pattern exactly).

## Extraction scope
- `MainWindow.xaml`: every user-visible literal — labels, button contents, tooltips, placeholder
  texts, watermarks, menu headers, section headers (PROJECTS / CLI HISTORY / ARCHIVED / CHANGES /
  DIFF REVIEW...), settings panel labels, overlay titles. Replace with `{StaticResource L.Key}`
  (ToolTip="{StaticResource ...}" works on attributes).
- `Views/ActivityHistoryWindow.xaml`: same.
- Code-behind / ViewModels: user-visible strings built in C# (MessageBox texts, "승인 대기: {0}",
  WorkingBlock messages like the CLI-import marker, status strings "restored after restart",
  SettingsStatus, NewProjectError messages, About dialog). Add a tiny helper in App or a static
  class: `public static string L(string key, params object[] args)` that looks up
  `Application.Current.Resources[key]` (fallback: key itself) and `string.Format`s args.
  Replace literals with `L("L.WaitingApproval", toolName)` style. Use composite keys with
  `{0}` placeholders inside the string resources.
- Do NOT extract: engine/CLI identifiers (model ids, flag names), log/commit strings, docs,
  Smoke console output, mono badges like "CC"/"GX"/"TR", the brand name AgentManager.

## Translations
- `Strings.Ko.xaml`: today's Korean strings verbatim, **but function badges/buttons that are
  currently English get natural Korean** (user request): e.g. TR ON/OFF → `번역 ON`/`번역 OFF`,
  APPROVAL → `승인`, RUNNING → `실행 중`, Running/Awaiting input/Completed/Failed/Idle →
  `실행 중`/`입력 대기`/`완료`/`실패`/`대기`, Approve & run → `승인 후 실행`, Approve for session →
  `세션 동안 승인`, Reject → `거부`, Copy all → `전체 복사`, Export → `내보내기`, SENT EN → `전송본`,
  ORIGINAL → `원문`, Merge ▸ main → `메인에 병합`, Commit only → `커밋만`, Discard → `폐기`.
  Keep mono badges SHORT (they sit in pills). Section headers (PROJECTS, CLI HISTORY, CHANGES,
  DIFF REVIEW, ARCHIVED, TASK, NAME...) may stay English in Ko if a Korean label would break the
  uppercase-mono look — use judgment, consistency over literalism.
- `Strings.En.xaml`: natural English equivalents (concise UI English; keep technical words as-is).

## Function-button / VM-label checklist (must be covered; the user called these out)
- `SessionViewModel.TranslationLabel` ("TR ON"/"TR OFF") → resource-based with language
- status-strip APPROVAL toggle, sandbox toggle label text (the SandboxMode enum VALUES in the
  ComboBox stay technical — do not localize enum names)
- `SessionViewModel.StatusLabel` (Running/Awaiting input/Completed/Failed/Idle), RUNNING badge,
  `LastSignalLabel` ("last signal ... ago" / "waiting for first signal"), `CostLabel` ("plan")
- composer: Worktree pill prefix, model/effort tooltips, placeholder watermark, 보내기/중지 tooltips
- approval block: APPROVAL REQUIRED header, Approve & run / Approve for session / Reject
- review pane: DIFF REVIEW, CHANGES, Merge/Commit/Discard buttons, feedback box placeholder,
  ReviewStatus strings from AppViewModel ("Scanning changes...", "N changed file(s)", "No changes",
  "세션 worktree가 아직 없습니다" 등)
- sidebar: New Agent, PROJECTS/CLI HISTORY/ARCHIVED headers + tooltips, nav items
- transcript blocks: YOU label, thinking 라벨, stderr/error titles, CLI-import marker text

## Hard rules
- No edits under `src/AgentManager.Core/**` and `src/AgentManager.Smoke/**`.
- Do not change logic, bindings other than the string swap, or layout.
- No XML comments containing double hyphens. Build must stay 0 errors / 0 new warnings:
  `dotnet build AgentManager.slnx -c Debug`.
- The two string dictionaries must have IDENTICAL key sets (write a quick manual check or a
  tiny PowerShell diff of `x:Key=` lines before committing).
- Commits: (1) infra (dictionaries + App wiring + Settings toggle), (2) MainWindow sweep,
  (3) ActivityHistory + code-behind/VM sweep + PROGRESS_KO.md row. Korean or English messages.

## Verification
- Build, launch `src/AgentManager/bin/Debug/net10.0-windows/AgentManager.exe`, confirm UI is
  pixel-identical in Korean default; flip ENGLISH UI + restart and spot-check the sidebar,
  composer tooltips, settings panel, approval block, Activity History window.
- If the exe is locked during build, kill the running AgentManager process first and relaunch after.
