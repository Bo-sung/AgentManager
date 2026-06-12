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
- `Strings.Ko.xaml` keeps the current Korean/English mix exactly as it is today
  (today's UI is the source of truth — copy verbatim).
- `Strings.En.xaml`: natural English equivalents (concise UI English; keep technical words as-is).

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
