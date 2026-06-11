# Delegation Brief — Codex #3: Activity History window + docs refresh

Repo: `J:\prj\AgentManager` (WPF / .NET 10, MVVM, hand-rolled, no frameworks).
Check `git log --oneline -5` and base on latest `main`.

## Hard fences (another session is working in parallel — Stage 2 approval integration)
**You must NOT edit these files/folders. Read them as much as you like:**
- `src/AgentManager.Core/**` (all of Core)
- `src/AgentManager/ViewModels/AppViewModel.cs`
- `src/AgentManager/ViewModels/EngineRegistry.cs`
- `src/AgentManager.Smoke/**`

Allowed edit surface: **new files** under `src/AgentManager/Views/` + the two small marked
insertions in `MainWindow.xaml` / `MainWindow.xaml.cs` described in Task A + `README.md`,
`docs/FEATURES_KO.md`, `docs/PROGRESS_KO.md`.

General rules (same as previous briefs):
- Style: design tokens from `Theme/Theme.xaml` (Bg0..Bg5, Line*, Txt0..3, Accent, Ok/Warn/Err, Mono/Sans),
  icons via `<controls:IconView Icon="{StaticResource Icon...}"/>` (see `Theme/Icons.xaml`).
- No XML comments containing double hyphens (breaks XAML build).
- `dotnet build AgentManager.slnx -c Debug` must pass: 0 errors, 0 new warnings.
- One commit per task with the task letter in the message.

---

## Task A — Activity History window (read-only, cross-session)

The sidebar nav item "Activity History" is currently a disabled placeholder. Implement it as a
**self-contained window that reads the persisted app state directly** — zero coupling to live
ViewModels (this is deliberate: the other session is editing AppViewModel).

### Data source
`AgentManager.Persistence.AppStateStore.Load()` (returns `AppStateDto?` — see
`src/AgentManager/Persistence/AppStateStore.cs`). It contains `Projects` and `Sessions`
(`SessionDto`: Id, AgentId(cc/gx), Title, Project, Branch, Status, Activity, TokensIn/Out,
CostUsd, IsArchived, StartedAt, Transcript list). Read-only — never write state from this window.

### New files
- `src/AgentManager/Views/ActivityHistoryWindow.xaml` + `.xaml.cs`

### Window spec
- Dark chrome consistent with MainWindow (WindowStyle=None is NOT required — a normal window with
  `Background={StaticResource Bg1}` and dark title via `Title="Activity History"` is fine; keep it simple).
- Size ~ 860x560, `WindowStartupLocation="CenterOwner"`.
- Top bar: title label ("ACTIVITY HISTORY" with Lbl style), total counts (N sessions · M projects),
  a filter TextBox (matches Title/Project/Status, OrdinalIgnoreCase, live as you type).
- Main area: a virtualized `ListView`/`ListBox` (rows, newest StartedAt first):
  - status dot (Ok=done, Err=error, Warn=waiting, Txt3=idle/other — copy the Ellipse style pattern
    from the sidebar session rows in MainWindow.xaml)
  - engine badge (CC/GX text in a small bordered box, Mono 10)
  - Title (Txt0, trimmed), Project name (Txt2, Mono 10.5), Branch (Txt3, Mono 10)
  - StartedAt as `MM-dd HH:mm` (Mono, Txt2), tokens `in/out` (Mono, Txt2), cost `$0.000` (Mono, Txt2)
  - archived rows at Opacity 0.6 with an "archived" tag
  - row count of transcript items as `N blocks` (Txt3)
- Footer: a Refresh ChipButton (re-runs `AppStateStore.Load()`), Close ChipButton.
- No editing, no deletion, no selection side effects. Double-click does nothing.

### MainWindow integration (the ONLY edits outside new files)
1. `MainWindow.xaml` — the sidebar nav StackPanel (search for `Activity History`): replace the
   disabled placeholder row with a clickable row (keep the same IconHistory + label look,
   `Foreground` Txt0, `Cursor=Hand`, `MouseLeftButtonUp="ActivityHistory_Click"`). Keep the
   "Scheduled Tasks" placeholder row untouched.
2. `MainWindow.xaml.cs` — add ONE new isolated handler method (do not modify other methods):
   ```csharp
   private void ActivityHistory_Click(object sender, MouseButtonEventArgs e)
   {
       new Views.ActivityHistoryWindow { Owner = this }.ShowDialog();
   }
   ```

## Task B — Docs refresh (docs only)

`README.md` is out of date vs the last two weeks of features. Update the 주요 기능 section to add
(KEEP the existing tone/format, Korean):
- 사이드바 PROJECTS 목록 (전 프로젝트 표시/전환, 프로젝트 우클릭 Rename/Remove)
- 세션 검색/필터
- CLI HISTORY (AgentManager 밖에서 돌린 claude/codex 세션 발견 → 클릭 한 번으로 가져와 resume,
  과거 대화 트랜스크립트 복원 포함) + 재스캔
- 멀티폴더 project (Settings의 EXTRA FOLDERS — claude --add-dir / codex writable_roots)
- 트랜스크립트 UI 가상화(대형 세션 성능) · 본문 텍스트 선택/복사 · 라이브 Review 갱신(실행 중 diff)
- 프로젝트 폴더 생성(Browse + 미존재 경로 자동 생성)
- 타이틀바 File/View/Help 메뉴
- Activity History 창 (Task A에서 구현한 내용)
Also update `docs/FEATURES_KO.md` if it has a feature matrix (add rows; do not restructure).
Finally append one row per task to `docs/PROGRESS_KO.md` with your commit hashes.

## Verification
- Build passes; launch `src/AgentManager/bin/Debug/net10.0-windows/AgentManager.exe`, click
  Activity History in the sidebar, confirm the window opens with real data and the filter works.
- If the app exe is locked when building, another session's instance may be running: kill the
  `AgentManager` process, build, and relaunch it when done.
