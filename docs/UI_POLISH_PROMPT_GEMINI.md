# [Delegated task] AgentManager — UI polish pass (5 small items)

> Paste this whole document into the session. Working directory: `J:\prj\AgentManager`
> Follow it **literally**. Do not improvise beyond what each item specifies.

## Context (read once)
WPF (.NET 10) app. MVVM. All business logic lives in ViewModels/Core and is **off-limits**. You will only touch these files:
- `src/AgentManager/MainWindow.xaml`
- `src/AgentManager/MainWindow.xaml.cs` (code-behind; small additions allowed)
- `src/AgentManager/Theme/Theme.xaml` (styles only)

**HARD RULES**
1. NEVER edit anything under `src/AgentManager.Core/`, `src/AgentManager/ViewModels/`, `src/AgentManager/Persistence/`, `src/AgentManager.Smoke/`.
2. Code-behind may READ the view model (`_vm`, e.g. `_vm.ActiveSession.Transcript`) and call existing commands — never add new state to it.
3. No `--` inside XML comments (build error MC3000).
4. After EVERY item: kill app, build, expect **0 errors**:
   ```powershell
   Get-Process AgentManager -ErrorAction SilentlyContinue | Stop-Process -Force
   dotnet build J:\prj\AgentManager\AgentManager.slnx -c Debug
   ```
5. Commit after each successful item: `git add -A && git commit -m "UI polish: <item>"` (append a line `Co-Authored-By: Gemini <noreply@google.com>`).
6. **If an item fails to build twice in a row: revert it (`git checkout -- .`), write the item number + error in `docs/POLISH_NOTES.md`, and move to the next item.** Do not get stuck.

Existing reference points (search these strings in MainWindow.xaml to orient): `x:Name="Root"`, `SessionRow_Click`, `Merge ▸ main`, `TranslationEnabled, Mode=TwoWay`. The root DataContext is `AppViewModel` (`_vm` in code-behind). The transcript items are in `_vm.ActiveSession.Transcript` (types: `UserBlock.Text`, `AgentTextBlock.Text`, `ToolBlock.Name/Body`, `ErrorBlock.Title/Body`, `WorkingBlock.Text`, `ApprovalBlock.ToolName/State`).

---

## Item 1 — Keyboard shortcuts
In `MainWindow.xaml`, directly under the `<WindowChrome.WindowChrome>...</WindowChrome.WindowChrome>` block, add:
```xml
<Window.InputBindings>
    <KeyBinding Modifiers="Ctrl" Key="N" Command="{Binding NewAgentCommand}"/>
    <KeyBinding Modifiers="Ctrl" Key="R" Command="{Binding ToggleReviewCommand}"/>
</Window.InputBindings>
```
In `MainWindow.xaml.cs`, override key handling for Escape — add inside the class:
```csharp
protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
{
    base.OnPreviewKeyDown(e);
    if (e.Key == System.Windows.Input.Key.Escape)
    {
        if (_vm.ShowNewAgent) { _vm.ShowNewAgent = false; e.Handled = true; }
        else if (_vm.ShowNewProject) { _vm.ShowNewProject = false; e.Handled = true; }
    }
}
```

## Item 2 — Copy button on agent messages
In the `AgentTextBlock` DataTemplate (search `vm:AgentTextBlock`), inside the horizontal StackPanel that holds the agent name (it contains `AgentName`), append:
```xml
<Button Content="⧉" FontSize="11" Margin="9,0,0,0" Padding="4,0"
        Background="Transparent" BorderThickness="0" Cursor="Hand"
        Foreground="{StaticResource Txt3}" ToolTip="복사"
        Click="CopyAgentText_Click"/>
```
In code-behind add:
```csharp
private void CopyAgentText_Click(object sender, RoutedEventArgs e)
{
    if ((sender as FrameworkElement)?.DataContext is ViewModels.AgentTextBlock b)
        try { Clipboard.SetText(b.Text); } catch { }
}
```
(If the namespace prefix differs, match the file's existing `using AgentManager.ViewModels;` and use `AgentTextBlock` directly.)

## Item 3 — Remember window size/position
Code-behind only. Own tiny JSON file — do NOT touch AppStateStore.
Add to `MainWindow.xaml.cs`:
```csharp
private static readonly string WindowStatePath =
    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentManager", "window.json");

private void RestoreWindowPlacement()
{
    try
    {
        if (!System.IO.File.Exists(WindowStatePath)) return;
        var parts = System.IO.File.ReadAllText(WindowStatePath).Split(',');
        if (parts.Length == 4
            && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var t)
            && double.TryParse(parts[2], out var w) && double.TryParse(parts[3], out var h)
            && w > 200 && h > 200)
        { Left = l; Top = t; Width = w; Height = h; }
    }
    catch { }
}

private void SaveWindowPlacement()
{
    try
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowStatePath)!);
        System.IO.File.WriteAllText(WindowStatePath,
            string.Join(",", Left, Top, Width, Height));
    }
    catch { }
}
```
Call `RestoreWindowPlacement();` at the end of the constructor (after `InitializeComponent()` and DataContext setup), and add `Closing += (_, _) => SaveWindowPlacement();` right after it.

## Item 4 — Export transcript to Markdown
In `MainWindow.xaml`, in the tabbar area (search `Merge ▸ main` is the review pane — instead search for the tabbar's right-side buttons near `ToggleReviewCommand`), add next to the review-toggle button:
```xml
<Button Style="{StaticResource ChipButton}" Content="⤓ Export" Margin="0,0,8,0"
        ToolTip="트랜스크립트를 Markdown으로 내보내기" Click="ExportTranscript_Click"/>
```
Code-behind:
```csharp
private void ExportTranscript_Click(object sender, RoutedEventArgs e)
{
    var s = _vm.ActiveSession;
    if (s is null) return;
    var dlg = new Microsoft.Win32.SaveFileDialog
    {
        FileName = "session-" + s.Title + ".md",
        Filter = "Markdown (*.md)|*.md"
    };
    if (dlg.ShowDialog() != true) return;
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# " + s.Title).AppendLine();
    foreach (var item in s.Transcript)
    {
        switch (item)
        {
            case ViewModels.UserBlock u: sb.AppendLine("## 🧑 User").AppendLine(u.Text).AppendLine(); break;
            case ViewModels.AgentTextBlock a: sb.AppendLine("## 🤖 " + s.AgentName).AppendLine(a.Text).AppendLine(); break;
            case ViewModels.ToolBlock t: sb.AppendLine("### 🔧 " + t.Name).AppendLine("```").AppendLine(t.Body).AppendLine("```").AppendLine(); break;
            case ViewModels.ErrorBlock err: sb.AppendLine("### ❌ " + err.Title).AppendLine(err.Body).AppendLine(); break;
            case ViewModels.ApprovalBlock p: sb.AppendLine("### ⚠ Approval: " + p.ToolName + " → " + p.State).AppendLine(); break;
            case ViewModels.WorkingBlock w: sb.AppendLine("> " + w.Text).AppendLine(); break;
        }
    }
    try { System.IO.File.WriteAllText(dlg.FileName, sb.ToString()); } catch { }
}
```
(Adjust the `ViewModels.` prefix to match the file's existing usings, same as Item 2.)

## Item 5 — Artifacts empty state + button tooltips
1. Find the Artifacts panel ItemsControl (search `Artifacts`). Directly after it add:
```xml
<TextBlock Text="아직 아티팩트 없음" FontFamily="{StaticResource Mono}" FontSize="10"
           Foreground="{StaticResource Txt3}" Margin="2,4">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding Artifacts.Count}" Value="0">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```
2. Add `ToolTip` attributes to any icon-only buttons in the titlebar window controls (`MinBtn`/`MaxBtn`/`CloseBtn` → "최소화"/"최대화"/"닫기") and the composer send button ("보내기 (Enter)") and stop button if missing.

---

## Done criteria
- [ ] Items 1–5 each committed (or noted as reverted in `docs/POLISH_NOTES.md`)
- [ ] Final `dotnet build` → 0 errors
- [ ] Run the app once: `dotnet run --project J:\prj\AgentManager\src\AgentManager\AgentManager.csproj` and confirm it opens
- [ ] Append one line to `docs/PROGRESS_KO.md` under "## ✅ 완료": `| **UI 폴리시 패스 (Gemini)** — 단축키/복사/창상태/내보내기/빈상태·툴팁 | <your commit hashes> |`
