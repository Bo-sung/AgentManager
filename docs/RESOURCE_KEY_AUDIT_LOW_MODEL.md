# Resource and i18n Key Audit Report (LM-QA-1)

This report covers the key audit performed on the `AgentManager` WPF application, inspecting XAML files (`MainWindow.xaml`, `ActivityHistoryWindow.xaml`) and localization dictionary resource files (`Strings.En.xaml`, `Strings.Ko.xaml`).

## 1. Keys Found in Dictionary Files
* Both `Strings.En.xaml` and `Strings.Ko.xaml` define exactly **250 identical localization keys** aligned line-by-line.
* Main UI XAML files (`MainWindow.xaml` and `ActivityHistoryWindow.xaml`) reference a total of **98 unique `StaticResource L.*` keys**.
* Every referenced static resource key (e.g. `L.Orchestrator`, `L.ActivityHistory`, `L.ScheduledTasks`, etc.) is fully defined in both the English and Korean dictionary files.
* **No missing keys** were found between the XAML files and the dictionary resource files.

## 2. Keys Added
* **0 keys added**. No missing keys were detected in either `Strings.En.xaml` or `Strings.Ko.xaml`, as they already had matching keys for all WPF `StaticResource L.*` references.

## 3. Hardcoded English Strings Replaced
* **1 trivial hardcoded UI label was replaced** with resource keys:
  * **File**: `src/AgentManager/MainWindow.xaml` (Line 547)
  * **Original**: `<Run Text="PROJECT · "/>`
  * **Replacement**: `<Run Text="{StaticResource L.Project}"/><Run Text=" · "/>`
  * **Rationale**: Replaces the hardcoded "PROJECT" with the existing `L.Project` localization resource and keeps the separator dot.

## 4. Remaining Hardcoded Labels (Appropriate to Keep)
* **Application Title / Branding**:
  * `"AgentManager"` (MainWindow title)
  * `<Run Text="Agent"/>` and `<Run Text="Manager"/>` (Logo/Header text)
* **Initials Placeholder**:
  * `"JK"` (Footer user avatar initials)
* **Symbols and Separators**:
  * Window control buttons: `"—"` (Minimize), `"▢"` (Maximize), `"✕"` (Close)
  * Dropdown arrows / folder structures: `" ▸"`, `" ▾"`, `"  ›  "`, `" · "`, `" / "`

## 5. Verification
* The `AgentManager.slnx` solution was built using `dotnet build` and compiled successfully with **0 warnings and 0 errors**.
