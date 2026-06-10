# [Delegated task] AgentManager — diff coloring, markdown upgrade, release packaging

> Paste this whole document into the session. Working directory: `J:\prj\AgentManager`

## Context
WPF (.NET 10) multi-agent manager, MVVM. You did the previous UI batch here. Same rules apply.

**Files you may touch (ONLY these):**
- `src/AgentManager/MainWindow.xaml` (binding swaps only)
- `src/AgentManager/Controls/` (new view-layer controls allowed; `MarkdownViewer.cs` lives here)
- `src/AgentManager/Theme/Theme.xaml` (styles only)
- `scripts/` (new folder, build scripts)
- `.gitignore`, `docs/PROGRESS_KO.md` (status update at the end)

**HARD RULES** — never edit `src/AgentManager.Core/`, `src/AgentManager/ViewModels/`, `src/AgentManager/Persistence/`, `MainWindow.xaml.cs` (except: you may add zero code-behind; if a control needs events, handle them inside the control class). No `--` in XML comments. Build after every item:
```powershell
Get-Process AgentManager -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build J:\prj\AgentManager\AgentManager.slnx -c Debug   # 0 errors required
```
Commit per item, message ends with `Co-Authored-By: Codex <noreply@openai.com>`.
If an item fails to build twice: `git checkout -- .`, note it in `docs/POLISH_NOTES.md`, move on.

## Item 1 — Diff syntax coloring (Review pane)
Currently the Review pane shows the unified diff as plain text bound to `DiffText` (search `DiffText` in MainWindow.xaml — it's a read-only TextBox or TextBlock).
1. Create `src/AgentManager/Controls/DiffViewer.cs` — a view-layer control modeled on the existing `MarkdownViewer.cs` pattern (a Control/UserControl with a `Text` DependencyProperty that re-renders on change).
2. Render line-by-line with these rules (use existing theme brushes via `Application.Current.Resources`):
   - line starts with `+++` or `---` (file headers) → foreground `Txt2`
   - else starts with `+` → foreground `#FF7FE0B6`, background `#1A2FAE7A`
   - else starts with `-` → foreground `#FFEA97A1`, background `#17E05566`
   - starts with `@@` → foreground `Info` (`#FF5B9BFF`), background `Bg3`
   - otherwise → foreground `Txt1`
   - font: Consolas 11.5, line height ~1.6, no wrapping (horizontal scroll comes from the parent ScrollViewer)
3. Swap the diff display in MainWindow.xaml to `<controls:DiffViewer Text="{Binding DiffText}"/>` (the `controls:` xmlns already exists).

## Item 2 — Markdown upgrade: clickable links (+ best-effort tables)
Edit `src/AgentManager/Controls/MarkdownViewer.cs` only.
1. **Links (required):** `[text](url)` and bare `https://…` URLs render as `Hyperlink` inlines (accent color). On click open the URL:
   ```csharp
   Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
   ```
   wrapped in try/catch.
2. **Tables (best-effort):** consecutive lines matching `| a | b |` (with a `|---|` separator line) render as a bordered Grid: header row bold `Txt0` on `Bg3`, body rows `Txt1`, cell padding 6×3, borders `LineSoft`. If this gets complicated, a clean monospace fallback block is acceptable — but links are mandatory.
3. Do not regress existing rendering (heading/list/code fence/inline code/bold).

## Item 3 — Release packaging
1. Create `scripts/publish.ps1`:
   ```powershell
   $ErrorActionPreference = "Stop"
   $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
   dotnet publish "$root\src\AgentManager\AgentManager.csproj" -c Release -r win-x64 `
     --self-contained false -p:PublishSingleFile=true -o "$root\dist"
   Write-Host "Published to $root\dist\AgentManager.exe"
   ```
2. Add `dist/` to `.gitignore`.
3. Run the script, confirm `dist\AgentManager.exe` exists and **launches** (start it, wait 8s, check the process is alive, then kill it).
4. Note in the commit message the produced exe size.

## Done criteria
- [ ] 3 items committed (or noted as reverted)
- [ ] `dotnet build` 0 errors; app runs (`dotnet run --project src/AgentManager/AgentManager.csproj`)
- [ ] Append to `docs/PROGRESS_KO.md` ✅ table: `| **Diff 색상 + 마크다운 링크/테이블 + Release 패키징 (Codex)** | <hashes> |`
