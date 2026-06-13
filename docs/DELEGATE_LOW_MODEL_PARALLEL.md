# Low-Model Parallel Delegation Prompts

> Context: D1 Scheduling Core is complete and now treated as owned/stable. Do not touch `src/AgentManager.Core/Scheduling/*` or `src/AgentManager.Smoke/Program.cs` in any delegated task below unless the main session explicitly asks for a scheduler-core fix.

## LM-DOC-1 — UI Porting Gap Checklist

AgentManager repo: `J:\prj\AgentManager`.

You are doing a read-only design audit. Do not edit source code.

Goal:
Compare the updated design reference under `design/` with the current WPF implementation and produce a concise checklist of remaining UI porting gaps.

Read:
- `design/am-app.jsx`
- `design/am-views.jsx`
- `design/am-settings.jsx`
- `design/AgentManager.html`
- `src/AgentManager/MainWindow.xaml`
- `src/AgentManager/ViewModels/AppViewModel.cs`
- `src/AgentManager/Theme/Strings.Ko.xaml`
- `src/AgentManager/Theme/Strings.En.xaml`

Do not touch:
- `src/AgentManager.Core/Scheduling/*`
- `src/AgentManager.Smoke/Program.cs`
- Any `.cs` or `.xaml` source file

Output:
Create `docs/UI_PORTING_GAPS_LOW_MODEL.md` with:
1. Already implemented
2. Partially implemented
3. Not implemented
4. Risky/conflict-prone items that should stay with the main session

Keep it factual. Include file references and avoid broad refactor suggestions.

Validation:
Run no build unless you edited files. You should only create the doc.

## LM-QA-1 — Resource and i18n Key Audit

AgentManager repo: `J:\prj\AgentManager`.

You are auditing resource keys only. Prefer no source edits unless the fix is an obvious missing string key in both language dictionaries.

Goal:
Find WPF `StaticResource L.*` references that are missing from either `Strings.Ko.xaml` or `Strings.En.xaml`, and identify user-visible hardcoded English strings in recently added Orchestrator/History/Scheduled blocks.

Read:
- `src/AgentManager/MainWindow.xaml`
- `src/AgentManager/Theme/Strings.Ko.xaml`
- `src/AgentManager/Theme/Strings.En.xaml`

Allowed edits:
- Add missing `sys:String` keys to both `Strings.Ko.xaml` and `Strings.En.xaml`.
- If a hardcoded UI label is trivial, replace it with an existing or newly added string key.

Forbidden edits:
- Do not edit `AppViewModel.cs`.
- Do not edit any Scheduling or Smoke file.
- Do not reformat the dictionaries.

Output:
Report keys found, keys added, and remaining hardcoded labels.

Validation:
Run `dotnet build AgentManager.slnx`.

## LM-QA-2 — Icon Resource Audit

AgentManager repo: `J:\prj\AgentManager`.

This is a read-only audit. Do not edit code.

Goal:
Check whether all icons used by the updated design surfaces have WPF geometry equivalents, so the main session can avoid ad hoc SVG or text glyphs.

Read:
- `design/am-components.jsx`
- `design/am-views.jsx`
- `design/am-settings.jsx`
- `src/AgentManager/Theme/Icons.xaml`
- `src/AgentManager/Controls/IconView.cs`
- `src/AgentManager/MainWindow.xaml`

Output:
Create `docs/ICON_RESOURCE_AUDIT_LOW_MODEL.md` with:
1. Design icon names
2. Existing WPF resource match
3. Missing icon resources
4. Suggested existing fallback if any

Forbidden:
- Do not edit `Icons.xaml`.
- Do not edit source files.

## LM-DOC-2 — Manual QA Script for New Navigation

AgentManager repo: `J:\prj\AgentManager`.

You are writing a manual QA checklist. Do not edit app code.

Goal:
Create a short QA script for the newly added in-app navigation: Orchestrator, Activity History, Scheduled Tasks, and returning to Session by clicking a session.

Read:
- `src/AgentManager/MainWindow.xaml`
- `src/AgentManager/MainWindow.xaml.cs`
- `src/AgentManager/ViewModels/AppViewModel.cs`

Output:
Create `docs/UI_NAV_QA_SCRIPT.md` with:
1. Setup
2. Click path tests
3. Expected visual/result for each view
4. Regression checks: translation toggle, CLI HISTORY, project switching, review pane only on Session view

Forbidden:
- No source edits.
- No Scheduling/Smoke edits.

## LM-UI-1 — History View Visual Polish Only

Use this only after the main session says its current changes are stable.

AgentManager repo: `J:\prj\AgentManager`.

Goal:
Polish the in-app Activity History block visually without changing behavior.

Allowed file:
- `src/AgentManager/MainWindow.xaml`

Allowed area:
- Only the XAML block between comments `<!-- In-app activity history shell -->` and `<!-- Scheduled tasks shell -->`.

Forbidden:
- Do not edit C# files.
- Do not edit Scheduling/Smoke files.
- Do not change bindings, command names, or DataContext.
- Do not change Orchestrator, Scheduled, Session, Sidebar, Review pane blocks.

Tasks:
- Make rows closer to the design: subtle hover background, status-colored dot or icon, compact metadata alignment.
- Keep all colors via existing `StaticResource`.
- Keep text readable on narrow window widths.

Validation:
Run `dotnet build AgentManager.slnx`.

Report:
List exact visual changes and confirm no binding names changed.
