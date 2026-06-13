# Manual QA Test Script: WPF Navigation & UI Controls (LM-DOC-2)

This document provides a manual QA test script to verify the new WPF navigation, sidebar features, session views, and related interactive controls in `AgentManager`.

---

## 1. Setup

### 1.1 Prerequisites
- **Operating System**: Windows (required for WPF execution).
- **Development Environment**: 
  - .NET 8.0 SDK or higher.
  - Visual Studio 2022 or JetBrains Rider (optional, for running/building).
- **Services (Optional but Recommended for Full Integration)**:
  - **Ollama**: Running locally at `http://localhost:11434` with the `exaone3.5:7.8b` model pulled (required for testing the Ollama Translation feature).
  - **VS Code**: Installed and available on `PATH` (required for testing the "Open IDE" hand-off functionality).

### 1.2 Preparation & Build
1. Open PowerShell or Command Prompt.
2. Navigate to the project root directory:
   ```powershell
   cd "J:\prj\AgentManager"
   ```
3. Build the application:
   ```powershell
   dotnet build AgentManager.slnx
   ```
4. Run the application:
   ```powershell
   dotnet run --project src/AgentManager
   ```

---

## 2. Click Path Tests

Perform the following sequences of clicks and key presses to verify correct navigational behavior:

### Test Path A: Basic Sidebar Navigation & Empty State
1. **Launch the Application**: Verify the app opens and initializes. If no sessions are saved from previous runs, it will default to an empty state.
2. **Observe Sidebar Main Items**: Locate the top of the sidebar. You will see:
   - **Orchestrator**
   - **Activity History**
   - **Scheduled Tasks**
3. **Verify Orchestrator Clickability**: Hover over **Orchestrator**.
   - *Expected*: The text is fully white (`Txt0`), indicating it represents the active main view. There is no hand cursor (`Cursor="Hand"`) or click handler because the main pane itself serves as the Orchestrator workspace.
4. **Verify Activity History Clickability**: Hover over **Activity History**.
   - *Expected*: The text is white, the cursor changes to a hand (`Cursor="Hand"`), and a tooltip says "Cross-session activity history".
   - *Action*: Click on **Activity History**.
   - *Expected*: A modal dialog window titled **Activity History** pops up.
   - *Action*: Click **Close** or press `Esc` to close the dialog.
5. **Verify Scheduled Tasks Interaction**: Hover over **Scheduled Tasks**.
   - *Expected*: The text is grayed out (`Txt3`), the cursor does *not* change to a hand, and a tooltip says "Planned (P2) - scheduled agent runs".
   - *Action*: Attempt to click on it.
   - *Expected*: No action occurs (non-interactive placeholder).

### Test Path B: Project Switching & CLI History Discovery
1. **Observe Projects Header**: In the sidebar, find the **PROJECTS** section.
2. **Add a Project**:
   - Click the `+` (Add project) button next to the **PROJECTS** label.
   - Enter a valid local git directory path (or browse using the **Browse** button).
   - Click **Add project**.
3. **Switch Projects**:
   - Left-click on the newly added project name in the sidebar list.
   - *Expected*:
     - The selected project row receives a left-aligned orange highlight bar.
     - The main pane project indicator changes to `PROJECT · [New Project Name]`.
     - The list of sessions in the **PROJECT · [Name]** section updates.
     - The **CLI HISTORY** section automatically rescans the new project directory for external CLI sessions.

### Test Path C: CLI History Import & Session Activation
1. **Select a Project with Ext. CLI History**: Switch to a project that has existing external Claude Code or Codex CLI history files.
2. **Trigger CLI Discovery Rescan**: Click the refresh/rescan icon next to **CLI HISTORY** in the sidebar.
   - *Expected*: A list of discoverable sessions appears with their respective engines (e.g., CC, GX) and timestamps.
3. **Import CLI Session**:
   - Hover over a CLI History item. A tooltip should say "Click to import as a session and continue with resume".
   - Left-click on the item.
   - *Expected*:
     - The CLI item disappears from the **CLI HISTORY** list.
     - The session is imported and active immediately, added to the top of **ACTIVE** sessions.
     - The main pane switches to show the imported session's chat view.
     - A green/red/orange status dot is shown next to it in the active list.
     - The transcript restores past CLI conversational blocks.

### Test Path D: Active Session Controls & Transcript Interaction
1. **Select an Active Session**: Click on any session under the **ACTIVE** list.
   - *Expected*: The session panel loads in the middle pane, displaying the title, branch name, and the "Open IDE", "Export", "Copy all", and "Diff review" toggle buttons.
2. **Toggle translation**: Click the translation button (`TR ON` / `TR OFF`) in the status strip.
   - *Expected*: The label toggles, and any future messages utilize this setting.
3. **Enter Prompt**: Type a prompt in the composer area and press `Enter` (or click the Send button).
   - *Expected*: A `YOU` block is added with the prompt.
4. **Expand/Collapse Thinking Blocks**:
   - During thinking phases, a `◌ thinking ▸` indicator appears.
   - Click the indicator to toggle it: `◌ thinking ▾` (expanded), showing reasoning.
5. **Expand/Collapse Tool Blocks**:
   - When a tool runs, a tool block with the tool name and elapsed time appears.
   - Click the tool row to toggle the details section (showing input parameters and results).
6. **Tool Approvals**:
   - If a tool requires approval, a `⚠ APPROVAL REQUIRED` block is rendered in yellow.
   - Click **Approve & run** to proceed, or **Reject** to abort. For Codex sessions, a chip for **Approve for session** is available.

---

## 3. Expected Results

### 3.1 Orchestrator View
| State | Component | Expected Visual / Behavioral Result |
| :--- | :--- | :--- |
| **No Session Active** | Middle Pane | Shows the localized empty state message: `No sessions · start with New Agent` or `세션이 없습니다 · 새 에이전트로 시작하세요`. |
| **No Session Active** | Review Pane | Shows a blank panel (if toggled open) since `ActiveSession` is null. The toggle button on the tabbar is hidden/collapsed. |
| **Session Active** | Tabbar | Displays Project Name, Current Branch, `Open IDE` button, `Export` button, `Copy all` button, and the `Diff Review` panel toggle button (icon of a sidebar panel). |
| **Session Active** | Status Strip | Displays `STATUS`, `AGENT`, `MODEL`, `TR ON`/`TR OFF` (Translation Toggle), `APPROVAL` (Approval Toggle), `Sandbox Mode` combo box, `COST`, and `TKN` count. |
| **Session Active** | Composer | Text input area at the bottom with attachment clips, dictation button, and send button. Shows hint: `ENTER to send · SHIFT+ENTER for newline`. |

### 3.2 Activity History View
- **Window Type**: Modal dialog popup (`Views.ActivityHistoryWindow`).
- **Header Section**:
  - Displays localized title `Activity History` / `활동 기록`.
  - Shows total metrics summary next to it (e.g. `X sessions in Y projects` / `X개 세션, Y개 프로젝트`).
  - Search box is aligned to the top-right to filter records dynamically.
- **Table Columns**:
  - **Status Dot**: Green (done), Red (error), Orange (waiting/running).
  - **Badge**: CC (Claude Code), GX (Codex), etc.
  - **Title & Path**: Displays session name, project name, and active branch. Shows an `Archived` tag if the session is archived.
  - **Time**: Shows the start time formatted as `MM-dd HH:mm`.
  - **Tokens**: Input/Output tokens ratio (e.g., `1.5k/3.4k`).
  - **Cost**: Cost in USD formatted to three decimal places (e.g., `$0.045`).
  - **Blocks**: Shows number of blocks in the transcript (e.g., `12 blocks`).
- **Footer Section**:
  - Left-hand side: Filter status (e.g. `Shown: X` or `Shown: X (filtered)`).
  - Right-hand side: `Refresh` and `Close` buttons.

### 3.3 Scheduled Tasks View
- **Visual Appearance**: Text foreground set to disabled color (`Txt3`), matching static grayed-out layout.
- **Behavior**: Clicking or hovering does not trigger hover states, hand cursors, or any action. It is a non-functional placeholder.

### 3.4 Session View (When Selected)
- **Selection Highlight**: Sidebar row is highlighted in light dark-gray background (`Bg3`) with a thick orange left border (`Accent`).
- **Transcript Area**: Displays conversation history chronologically. Individual block types:
  - **UserBlock**: Green-accented message bubble. Has a `SENT EN` toggle for translation audits.
  - **AgentTextBlock**: Contains agent replies parsed into Markdown, showing engine badge, model, `ORIGINAL` response toggle, and `Copy` button.
  - **ToolBlock**: Inner border displaying run time, tool name, and collapsible JSON request/response body.
  - **ErrorBlock**: Light red border displaying error title and error details.
  - **WorkingBlock**: Shows running indicators with checkmarks (e.g. `✓ git diff completed`).

---

## 4. Regression Checks

Ensure these functional integrations do not break during navigation updates:

### 4.1 Translation Toggle (TR ON / TR OFF)
- **Test Steps**:
  1. Open a session.
  2. Toggle the translation button.
  3. Ensure it toggles between `TR ON` and `TR OFF` (or `번역 켬` / `번역 끔`).
  4. Verify that the underlying `SessionViewModel.TranslationEnabled` property changes state.
  5. Check that incoming agent responses are translated when `TR ON` is active, and show the translation controls.

### 4.2 CLI History Discovery & Resume
- **Test Steps**:
  1. Initialize or run a Codex/Claude session directly in the command prompt outside `AgentManager` within a project directory.
  2. In `AgentManager`, select that project.
  3. Click **Rescan CLI history** (`Refresh` button) in the CLI HISTORY sidebar section.
  4. Verify the external session shows up in the CLI HISTORY list.
  5. Click to import it.
  6. Confirm that the session resumes directly in the project directory (i.e. `WorktreeAttempted = true` is set, ensuring it does *not* create a separate git worktree which would break resumes).

### 4.3 Project Switching
- **Test Steps**:
  1. Click through multiple projects in the **PROJECTS** list in quick succession.
  2. Check that the application updates the following elements concurrently:
     - Project highlight bar moves to the active project.
     - **Project Sessions** list updates to sessions associated only with the active project.
     - **CLI History** list is wiped and reloaded for the newly active project.
     - The working directory path and project name label on the main view update instantly.
  3. Verify that rapid clicks do not cause background thread crashes or race conditions in `LoadCliHistoryAsync`.

### 4.4 Review Pane Context-Awareness (Session View Only)
- **Test Steps**:
  1. When no active session is selected:
     - Verify the main tab bar is hidden.
     - Press `Ctrl+R` or click `View` -> `Toggle Review Pane`.
     - *Expected*: The review pane column width changes (between 420px and 0px), but the column itself contains only a blank background. No controls, titles, or empty labels are rendered because `ActiveSession` is null.
  2. Select an active session:
     - Press `Ctrl+R` to open the review pane.
     - *Expected*: The review pane appears on the right side, showing `DIFF REVIEW`, `CHANGES`, `ARTIFACTS`, feedback input box, and `Merge ▸ main` / `Commit only` / `Discard` buttons, fully populated with the active session's context.
     - Toggle it closed and ensure it collapses to 0px.
