# Delegation Brief #3 — Small Remaining Features (Gemini session)

You are working in `J:\prj\AgentManager` (WPF / .NET 10, MVVM, no frameworks).
Run `git pull` / check `git log --oneline -5` first; base your work on the latest `main`.

## Hard rules (same as previous briefs)
1. **Never edit anything under `src/AgentManager.Core/`.** App-layer only.
2. Do not rename/move existing members. Additive changes only unless a spec below says otherwise.
3. Follow existing code style exactly: hand-rolled `ObservableObject`/`RelayCommand`, design tokens from `Theme/Theme.xaml`, icons via `<controls:IconView Icon="{StaticResource Icon...}"/>` from `Theme/Icons.xaml`.
4. **Do not touch the central transcript area** of `MainWindow.xaml` (the `ItemsControl`/`ScrollViewer` showing `Transcript`), `Controls/MarkdownViewer.cs`, or `MainWindow.xaml.cs` scroll/wheel handlers — another change is in flight there. If you need a click handler, add a new isolated method only.
5. No XML comments containing `--` (breaks XAML build). Build must pass with 0 warnings introduced: `dotnet build AgentManager.slnx -c Debug`.
6. One git commit per task (A, B, C), Korean or English message, mention the task letter.

## Files you will mainly touch
- `src/AgentManager/ViewModels/AppViewModel.cs` (commands, filtering)
- `src/AgentManager/ViewModels/ProjectViewModel.cs`
- `src/AgentManager/MainWindow.xaml` (sidebar region only: PROJECTS list, session groups, CLI HISTORY header)
- `src/AgentManager/Persistence/AppStateStore.cs` only if a DTO field is genuinely needed (Project name is already persisted)

---

## Task A — Project context menu: Rename / Remove

Sidebar PROJECTS rows (ItemsControl bound to `Projects`, row template has `MouseLeftButtonUp="ProjectRow_Click"`). Add a `ContextMenu` to the row Border, modeled exactly on the session row context menu right below it in the same file (it uses `DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}"` and commands via `Source={x:Reference Root}`).

1. `ProjectViewModel.Name`: change `get;` to a `Set(ref ...)` notifying property (keep ctor assignment).
2. `AppViewModel`:
   - `RenameProjectCommand` (param: string new name; renames `ActiveProject`-independent: the row's project — pass the project via a small wrapper or rename the row's `DataContext` project; follow the session-rename pattern `RenameSessionCommand` which renames the bound session). After rename: `OnChanged(nameof(Project))` if it is the active one, then `SaveState()`.
   - `RemoveProjectCommand` (param: ProjectViewModel). Behavior:
     - Never deletes anything on disk. App-state only.
     - If the project has sessions (`_allSessions.Any(s => s.ProjectId == p.Id)`), show `MessageBox.Show(..., MessageBoxButton.YesNo)` warning "세션 N개가 함께 제거됩니다" and abort on No.
     - Stop any running sessions of that project (reuse the cancellation pattern in `DeleteSessionAsync` — do NOT remove worktrees here, just `cts.Cancel()`).
     - Remove its sessions from `_allSessions`, remove the project from `Projects`.
     - If it was `ActiveProject`, set `ActiveProject = Projects.FirstOrDefault()`.
     - `RefreshProjectSessions(); RefreshCounts(); RefreshProjectCounts(); SaveState();`
3. Context menu items: `Rename` (inline TextBox + Apply button, copy the session RENAME panel pattern), `Remove project`.

## Task B — Session search / filter

1. `AppViewModel`: `string SessionFilter` property (notifying). In `RefreshProjectSessions`, when the filter is non-empty, only include sessions whose `Title`, `Branch`, or `Project` contains the filter (OrdinalIgnoreCase). Call `RefreshProjectSessions(selectFirstIfMissing: false)` from the setter so the active session is not stolen while typing.
2. Sidebar UI: a search TextBox between the "New Agent" button and the nav StackPanel. Style: reuse `ComposerInput` style with `Background="{StaticResource Bg1}"`, `BorderBrush="{StaticResource Line}"`, `BorderThickness="1"`, `Padding="8"`, Margin `12,0,12,8`, `AcceptsReturn="False"`, placeholder behavior optional (a `search` icon `IconSearch` to the left is a nice touch; a simple TextBox is acceptable).
3. Empty filter = current behavior unchanged. Filter applies to all three groups (Active/Project/Archived). CLI HISTORY is NOT filtered.

## Task C — CLI HISTORY rescan button

1. `AppViewModel`: `RefreshCliHistoryCommand = new RelayCommand(_ => _ = LoadCliHistoryAsync(ActiveProject))` (method already exists).
2. Sidebar CLI HISTORY header DockPanel: add a small refresh icon button on the right, modeled on the Review pane refresh button (`MenuButton` style, `IconRefresh`, Width 24, Height 20 or similar to fit the 10px label row; keep it subtle: `Foreground Txt3`).
3. Tooltip: "CLI 기록 다시 스캔".

## Verification before each commit
- `dotnet build J:\prj\AgentManager\AgentManager.slnx -c Debug` → 0 errors / 0 new warnings.
- Launch `src/AgentManager/bin/Debug/net10.0-windows/AgentManager.exe` once and confirm the sidebar renders and your feature works by hand.
- Update `docs/PROGRESS_KO.md` (one row per task, with the commit hash) in your final commit.
