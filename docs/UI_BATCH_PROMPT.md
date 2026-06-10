# [Delegated task] AgentManager — UI batch binding pass

> Paste this whole document into a fresh Claude session. Self-contained.
> Working directory: `J:\prj\AgentManager`

---

## Context
`J:\prj\AgentManager` is a WPF (.NET 10) multi-agent manager. **MVVM separation is complete** — every feature below already has its ViewModel logic, commands, and persistence implemented and verified. **Only the View (XAML) bindings are missing.** Your job is to add XAML binding UI — *nothing else*.

- Design reference: `design/AgentManager.html` + `design/am-*.jsx` (dark `#0a0e12` + orange `#ff5a2c` accent). Follow the existing XAML's look.
- Main window: `src/AgentManager/MainWindow.xaml` (+ code-behind `MainWindow.xaml.cs`)
- Theme tokens/styles: `src/AgentManager/Theme/Theme.xaml` (brushes `Bg0`–`Bg5`, `Line*`, `Txt0`–`Txt3`, `Accent*`, `Warn`/`Ok`/`Err`; styles `Lbl`, `AccentButton`, `ChipButton`, `TogglePillButton`, `SendButton`)
- ViewModels: `src/AgentManager/ViewModels/AppViewModel.cs` (root DataContext), `SessionViewModel.cs`, `ProjectViewModel.cs`, `ArtifactViewModel.cs`, `Blocks.cs`
- Root window has `x:Name="Root"` → inside item templates, bind app-level commands via `{Binding DataContext.XxxCommand, ElementName=Root}` (many existing examples in the file).

## Build / run / verify loop (mandatory)
```powershell
# kill the running app before building (file locks)
Get-Process AgentManager -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build J:\prj\AgentManager\AgentManager.slnx -c Debug   # keep 0 warnings / 0 errors
dotnet run --project J:\prj\AgentManager\src\AgentManager\AgentManager.csproj
```
Build after **each** item. When all items are done, run the app and eyeball every change.

## ⚠ Constraints (important)
1. **Do NOT modify ViewModel/Core logic** — bindings only. If a property/command seems missing, you looked in the wrong place (see the table below; they all exist).
2. No `--` inside XML comments (causes MC3000 build error).
3. `BooleanToVisibilityConverter` is already registered as resource key `BoolVis`. For inverted visibility use a Style DataTrigger (existing examples in the file).
4. Adding event handlers to code-behind is allowed (see existing `SessionRow_Click` pattern), but they must only call existing VM methods/commands.
5. Adding new *styles* to Theme.xaml is allowed (e.g., a dark ContextMenu style). Logic, never.
6. Commit per item; end each commit message with `Co-Authored-By: Claude <noreply@anthropic.com>`.
7. App UI labels may stay Korean where specified below (the product UI is Korean-first).

## Work items (independent; any order)

### 1. Titlebar aggregates
- Where: titlebar right-side stats stack (search: `DONE</TextBlock>`)
- Bind: `AppViewModel.TotalTokensLabel` (string like `"1.2k / 3.4k"`), `TotalCostLabel` (`"$0.12"`)
- Shape: `Σ TKN {TotalTokensLabel}` · `COST {TotalCostLabel}` — Mono 10px, colors Txt2 / Ok

### 2. Per-session cost in the status strip
- Where: status strip next to `TKN {TokensLabel}` (search: `TokensLabel`)
- Bind: `SessionViewModel.CostLabel` (`"$0.0042"` or `"—"`)

### 3. Session context menu
- Where: sidebar session row Border (search: `SessionRow_Click`)
- A `ContextMenu` with: Rename / Archive toggle / Fork / Delete
- Commands (all on AppViewModel; parameter = the session VM):
  - `ArchiveSessionCommand` (CommandParameter={Binding})
  - `ForkSessionCommand` (CommandParameter={Binding})
  - `DeleteSessionCommand` (CommandParameter={Binding})
  - Rename: `RenameSessionCommand` takes a **string** (new title) and applies to the active session. A small overlay (copy the existing New-Agent overlay pattern) with a TextBox is fine.
- A dark ContextMenu style in Theme.xaml is welcome.

### 4. Archived group in the sidebar
- Where: below the Active/Project groups (search: `ProjectSessions`)
- Bind: `AppViewModel.ArchivedSessions` (ObservableCollection<SessionViewModel>)
- Shape: same ItemsControl pattern as the other groups + section header "ARCHIVED" + count, dimmed (Txt2).

### 5. Sandbox / approval toggles in the status strip
- Where: next to the translation toggle (search: `TranslationEnabled, Mode=TwoWay`)
- Approval toggle: `TogglePillButton` style, `IsChecked="{Binding RequireApproval, Mode=TwoWay}"`, Content="APPROVAL"
- Sandbox: small ComboBox (Mono 10px), `SelectedItem="{Binding Sandbox}"`, ItemsSource via XAML `ObjectDataProvider` over enum `AgentManager.Core.Agents.SandboxMode` (ReadOnly/WorkspaceWrite/DangerFullAccess)
- (Skip the New-Agent modal; changing these on the strip after creation is enough.)

### 6. Review pane: Commit button + diff feedback
- Where: Review pane footer (search: `Merge ▸ main`)
- Commit: `ChipButton`, Content="Commit only", `Command="{Binding DataContext.CommitReviewCommand, ElementName=Root}"`
- Diff feedback: a one-line TextBox + send button above the footer. Button: `Command="{Binding DataContext.SendDiffFeedbackCommand, ElementName=Root}"`, `CommandParameter="{Binding Text, ElementName=<theTextBox>}"`. Clearing the TextBox after send may be done in a code-behind Click handler.

### 7. Concurrency cap setting
- Where: settings panel (search: `SettingsOllamaModel`)
- Bind: `AppViewModel.MaxConcurrentSessions` (int; VM clamps to ≥1)
- Shape: numeric TextBox or -/+ buttons. Label: "동시 실행 한도"

### 8. Artifacts panel
- Where: inside the Review pane (a section under the Changes list, collapsible is fine)
- Bind: `SessionViewModel.Artifacts` (ObservableCollection<ArtifactViewModel>)
  - `Kind` ("tasklist" | "test" | "summary"), `Title`, `Content` (multiline), `IsError` (true on failed test), `UpdatedAt`
- Shape: ItemsControl; per item a header (Kind badge + Title + time) and body (Mono, wrapped, MaxHeight + scroll). Err-colored border when `IsError`.

### 9. Project MCP config path field
- Where: settings panel (add a small "PROJECT" section targeting the active project)
- Bind: `AppViewModel.ActiveProject.McpConfigPath` (string, TwoWay)
- Label: "MCP CONFIG (.mcp.json path, empty = unused)"
- Note: the setter does not auto-save; TwoWay binding alone is correct (state persists on the next session/state change). Do not add save logic.

## Done criteria
- [ ] All 9 items bound, one commit each
- [ ] `dotnet build` → 0 warnings / 0 errors
- [ ] App runs and shows: titlebar aggregates · right-click session menu works · archive toggle moves rows between groups · with APPROVAL toggled on, the next turn shows an approval block (the approval UI itself already exists) · Review pane shows Commit / feedback / Artifacts
- [ ] Update `docs/PROGRESS_KO.md`: mark the "UI 일괄 패스" item done with your commit hashes
