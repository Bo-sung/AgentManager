# Icon Resource Audit (LM-QA-2)

This document presents the visual asset audit for the **AgentManager** design surfaces. It checks whether all design icons used in the UI mockups have corresponding WPF geometry equivalents in `src/AgentManager/Theme/Icons.xaml`.

> [!NOTE]
> **File Check Notice**:
> The files `design/am-views.jsx` and `design/am-settings.jsx` listed in the task description do not exist in this workspace. The audit was successfully completed by analyzing all actual design files: `am-app.jsx`, `am-chat.jsx`, `am-sidebar.jsx`, `am-components.jsx`, and `am-data.jsx` (which contains the primary icon registry).

---

## 1. Audit Summary

* **Total Design Icons Identified**: 36
* **WPF Geometry Matches Found**: 36 (100% coverage)
* **Missing Icons**: 0
* **Extra WPF-only Icons**: 4 (Engine-specific brand icons)

All design icons are properly mapped to their WPF geometry resource keys in `src/AgentManager/Theme/Icons.xaml` using a 1-to-1 conversion from the prototype SVG definitions.

---

## 2. Icon Resource Mapping Table

Below is the complete audit registry verifying the mapping between design-side icon names and WPF resource keys:

| # | Design Icon Name | WPF Resource Match (`src/AgentManager/Theme/Icons.xaml` Key) | Path Geometry / Ellipse Details | Status |
|---|---|---|---|---|
| 1 | `plus` | `IconPlus` | `M12 5v14M5 12h14` | Match |
| 2 | `history` | `IconHistory` | `M3 12a9 9 0 1 0 3-6.7L3 8`, `M3 4v4h4`, `M12 8v4l3 2` | Match |
| 3 | `clock` | `IconClock` | Ellipse: Center=(12,12), R=8.5; Path: `M12 7.5V12l3 2` | Match |
| 4 | `settings` | `IconSettings` | Ellipse: Center=(12,12), R=3; Paths | Match |
| 5 | `search` | `IconSearch` | Ellipse: Center=(11,11), R=6.5; Path: `M20 20l-3.8-3.8` | Match |
| 6 | `grid` | `IconGrid` | 4 x RectangleGeometry (Rect="3.5,3.5,7,7" etc. R=1) | Match |
| 7 | `panel` | `IconPanel` | Rect="3.5,4.5,17,15" R=1.5; Path: `M15 4.5v15` | Match |
| 8 | `send` | `IconSend` | `M5 12h13M12 5l7 7-7 7` | Match |
| 9 | `plusbox` | `IconPlusBox` | Rect="4,4,16,16" R=3; Path: `M12 9v6M9 12h6` | Match |
| 10 | `mic` | `IconMic` | Rect="9,3,6,11" R=3; Path: `M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21` | Match |
| 11 | `chevdown` | `IconChevDown` | `M6 9l6 6 6-6` | Match |
| 12 | `chevup` | `IconChevUp` | `M6 15l6-6 6 6` | Match |
| 13 | `chevright` | `IconChevRight` | `M9 6l6 6-6 6` | Match |
| 14 | `branch` | `IconBranch` | 3 x Ellipse (R=2.2); Path: `M6 7.2v9.6M18 10.2c0 4-4 3.3-6 5.3` | Match |
| 15 | `file` | `IconFile` | Folded file path | Match |
| 16 | `folder` | `IconFolder` | Folder outline path | Match |
| 17 | `copy` | `IconCopy` | Rect="8.5,8.5,11,11" R=1.6; Paths | Match |
| 18 | `refresh` | `IconRefresh` | Circular arrow paths | Match |
| 19 | `thumbup` | `IconThumbUp` | Thumb up shape paths | Match |
| 20 | `thumbdown` | `IconThumbDown` | Thumb down shape paths | Match |
| 21 | `check` | `IconCheck` | `M5 12.5l4.5 4.5L19 7` | Match |
| 22 | `x` | `IconX` | `M6 6l12 12M18 6L6 18` | Match |
| 23 | `terminal` | `IconTerminal` | Rect="3.5,4.5,17,15" R=1.5; Path: `M7 9l3 3-3 3M12.5 15h4` | Match |
| 24 | `edit` | `IconEdit` | Pencil outlines | Match |
| 25 | `eye` | `IconEye` | Eye curves + Ellipse R=2.7 | Match |
| 26 | `ide` | `IconIde` | `M9 7l-4 5 4 5M15 7l4 5-4 5` | Match |
| 27 | `attach` | `IconAttach` | Paperclip path | Match |
| 28 | `stop` | `IconStop` | Rect="6.5,6.5,11,11" R=1.6 (Used with `Filled="True"`) | Match |
| 29 | `warn` | `IconWarn` | Triangle + alert details | Match |
| 30 | `alert` | `IconAlert` | Circle + alert details | Match |
| 31 | `spark` | `IconSpark` | 4-point star path | Match |
| 32 | `bolt` | `IconBolt` | Lightning bolt path | Match |
| 33 | `pin` | `IconPin` | Pushpin shapes | Match |
| 34 | `dots` | `IconDots` | 3 x Ellipse R=1.3 | Match |
| 35 | `calendar` | `IconCalendar` | Rect="3.5,5,17,15" R=1.5; Lines | Match |
| 36 | `layers` | `IconLayers` | 3 x Sheet paths | Match |

---

## 3. Missing Icon Resources & Suggested Fallbacks

There are **no missing design icons**. All 36 icons listed in the design specs are fully implemented.

However, if future additions are introduced, or if dynamic resolution needs fallbacks, the following suggested fallback mappings from the current set can be used:

* **Generic Action/Add**: `IconPlus`
* **Generic Cancel/Close**: `IconX`
* **Generic Status/Warning**: `IconAlert` or `IconWarn`
* **Generic Document**: `IconFile`
* **Generic Directory/Group**: `IconFolder`

---

## 4. Polished Additions in WPF

In addition to 1-to-1 matches with the design spec, the WPF application defines brand-specific icons for each of the supported AI engines. These icons are bound dynamically via Style triggers in `src/AgentManager/Theme/Icons.xaml` (`EngineIcon` and `EngineIconByDef`):

* `IconEngineClaude` (Claude Code - `cc`)
* `IconEngineOpenAi` (GPT/Codex - `gx`)
* `IconEngineGemini` (Antigravity CLI - `ag`)
* `IconEngineAgy` (Antigravity custom/future - `agy`)
