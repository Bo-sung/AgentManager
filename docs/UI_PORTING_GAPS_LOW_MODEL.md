# UI Porting Gaps (Low Model Audit)

This document lists the gaps identified between the static HTML/JSX design reference and the WPF implementation (`src/AgentManager/...`).

> [!NOTE]
> As noted during the audit, the files `design/am-views.jsx` and `design/am-settings.jsx` mentioned in the task description do not exist in the repository. The core view and settings design logic are instead found in `design/am-app.jsx`, `design/am-chat.jsx`, and `design/am-sidebar.jsx`.

## 1. Already Implemented
The following UI elements and behaviors from the design reference (`design/am-app.jsx`, `design/am-chat.jsx`, `design/am-sidebar.jsx`, `design/am-components.jsx`, `design/AgentManager.html`) are fully implemented in the WPF application (`src/AgentManager/MainWindow.xaml`, `src/AgentManager/ViewModels/AppViewModel.cs`):
- **Core Window Layout & Shell**: Split-panel design mapping the Left Sidebar (280px), Main Chat workspace, and Right Review drawer (420px when open) ([MainWindow.xaml:L334-340](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L334-L340)).
- **Custom Titlebar & Menu Bar**: Brand styling (◆ AgentManager) and window window controls (Min, Max, Close) with File, Edit, View, Agents, Settings, and Help dropdown menus ([MainWindow.xaml:L227-331](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L227-L331)).
- **Sidebar Spawning Action**: "New Agent" button initiating session generation ([MainWindow.xaml:L344-351](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L344-L351)).
- **Active Session Grouping**: Displays running and waiting sessions highlighted with status indicators and branch info ([MainWindow.xaml:L400-473](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L400-L473)).
- **Tabbar & Crumbs**: Project crumbs, branch badge, "Open IDE" action button, and Review Pane toggle ([MainWindow.xaml:L757-828](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L757-L828)).
- **Status Strip**: Live metrics (elapsed run times, status labels, agent engine name, active model name, token statistics) ([MainWindow.xaml:L829-874](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L829-L874)).
- **Composer Controls**: TextBox that expands based on content, Attach image, Dictate action, Send/Stop action buttons, active branch/model pills ([MainWindow.xaml:L876-1208](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L876-L1208)).
- **Transcript Renderers**:
  - `UserBlock` for user prompts ([MainWindow.xaml:L33-50](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L33-L50)).
  - `AgentTextBlock` rendering markdown output ([MainWindow.xaml:L52-87](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L52-L87)).
  - `ToolBlock` representing expandable terminal, file-read, or edit commands ([MainWindow.xaml:L89-122](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L89-L122)).
  - `ErrorBlock` showing compiler/CLI failures ([MainWindow.xaml:L124-134](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L124-L134)).
  - `ApprovalBlock` staging critical confirmation prompts for destructive tools ([MainWindow.xaml:L173-218](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L173-L218)).
- **Review Drawer Layout**: Grid-based slide-out panel on the right ([MainWindow.xaml:L1237-1423](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L1237-L1423)).
- **Changed Files List**: Selected file change details (additions/deletions stats) and hunk-based line-by-line diff viewing ([MainWindow.xaml:L1259-1312](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L1259-L1312)).
- **Settings & Project Modals**: Complete settings and project management modals implemented as fullscreen grid overlays ([MainWindow.xaml:L1426-1700](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L1426-L1700)).

## 2. Partially Implemented
The following features are partially implemented or deviate in terms of visual styles/UX interactions:
- **Sidebar Footer & Settings**:
  - The design features a Settings gear button inside the sidebar footer (`am-sidebar.jsx:L90`). In WPF, Settings is accessed exclusively from the window menu bar or context menu, and opens a custom fullscreen modal overlay instead of in-sidebar.
- **Status Dot & Pulse Animation**:
  - JSX status dots animate with pulsating shadows (`am-sidebar.jsx:L193`) and display an active CSS-based loading spinner. WPF has simple solid dots and static text (pulsating glow is not implemented).
- **Session Row Live Activity**:
  - Running sessions in JSX render a `<Spark />` component (5 animated bars showing dynamic activity heights). WPF replaces this with a static string (`Activity` text) or a simple colored dot, lacking the activity sparkline animation.
- **New Agent Modal Options**:
  - The JSX modal includes custom dropdowns for model selections (`NewAgentModal:L102`) and branch names (`NewAgentModal:L117`) at spawn time. WPF's overlay only allows selecting the agent runtime and task title; model and branch options are deferred to the composer pane.
- **Diff Drawer Footer Actions**:
  - JSX uses dummy "Keep changes" and "Discard" triggers. WPF extends this into fully functional Git commands ("Merge ▸ main" to merge, "Commit only" to stage/commit without merging, "Discard" to run hard reset) and adds a `Diff Feedback Composer` to direct the agent's next turn.

## 3. Not Implemented
The following elements from the JSX designs are completely missing from the WPF project:
- **Time Indicators in Session Rows**:
  - The JSX sidebar renders the relative elapsed/start time (e.g., `relTime(s.startedAt)`) on the right of each session item (`am-sidebar.jsx:L18`). The WPF session list items (`MainWindow.xaml:L407-473`) completely omit any start or relative time label.
- **Inline FileChange / Review Cards in Chat Transcript**:
  - JSX renders a `FileChange` card block in the chat transcript stream displaying modified files, addition/deletion metrics, and a "Review" button that opens the drawer (`am-chat.jsx:L56-71`). WPF does not print file changes as transcript entries. The user must manually toggle the Diff Review panel or look at the right-hand panel to see what files were modified.

## 4. Risky / Conflict-Prone Items (Keep with Main Session)
The following complex architectural components should remain with the main session due to dependencies and stability concerns:
- **Top Window Chrome Integration**: Resizing and dragging behaviors managed via custom Window Chrome headers ([MainWindow.xaml:L14-16](file:///J:/prj/AgentManager/src/AgentManager/MainWindow.xaml#L14-L16)). Modifying this risks breaking window resizing/focus responsiveness.
- **Ollama Translation Pipeline**: Real-time prompt translation using the local Ollama instance (`AppViewModel.cs:L17`). Thread synchronization and dispatcher operations make this code sensitive to thread-access exceptions.
- **Git Worktree Isolation Engine**: Dynamically creates, monitors, and removes git worktrees in local directories ([AppViewModel.cs:L961-978](file:///J:/prj/AgentManager/src/AgentManager/ViewModels/AppViewModel.cs#L961-L978)). Modifications could cause locked processes, orphan worktrees, or staging repository corruption.
- **Virtualized Panel Scroll Mechanics**: Pixel-based UI virtualization and event handler overrides for automatic scrolling (`MainWindow.xaml:L1213-1232`). Modifying this risks layout deadlocks or jerky UI scrolling.
