# AgentManager 프론트엔드 구조 위키

> WPF(.NET 10) UI의 렌더링 구조 · 화면 영역 명칭 · 테마/리소스 시스템 · 컨벤션을 정리한 개발 위키.
> 코드: `src/AgentManager/`. 이 문서는 "어디를 뭐라고 부르고, 무엇이 무엇을 그리는가"의 단일 참조다.

---

## 1. 한눈에 보는 화면 지도

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Title Bar (타이틀바, 34px)                                                  │
│  [Brand]  [Menu Bar: Agents·View·Help]   ……drag……   [Status/Usage] [⊟▢✕] │
├───────────────┬──────────────────────────────────────────┬───────────────┤
│ Sidebar       │ Main Pane (메인 페인 / 콘솔)               │ Review Pane    │
│ (280px)       │  ─ 한 번에 하나의 View만 표시 ─            │ (0↔420, 접이식)│
│               │                                          │               │
│ New Agent     │  • Orchestrator (대시보드)                │ Changes List  │
│ Nav Rail      │  • Activity History                      │ Diff Viewer   │
│ Search        │  • Scheduled                             │ Review Actions│
│ Projects List │  • Settings                              │ Diff Feedback │
│ Sessions List │  • Session View ▼                        │               │
│  (Active/      │     Status Strip                         │               │
│   Project/     │     Native Workers Strip                 │               │
│   Archived)    │     Transcript                           │               │
│ CLI History   │     Composer                             │               │
│ Status Footer │                                          │               │
└───────────────┴──────────────────────────────────────────┴───────────────┘
        ▲ Overlays/Modals (New Agent · New Project · New Schedule · About) — 전체 위에 표시
```

레이아웃 골격(`MainWindow.xaml`):
- `Window` + `WindowChrome`(CaptionHeight 34, 커스텀 타이틀바) · 루트 `Grid x:Name="Root"`.
- 2행: `Row0=34`(Title Bar), `Row1=*`(Body).
- Body `Grid`(3열): `280`(Sidebar) · `*`(Main Pane) · `ReviewCol 0↔420`(Review Pane).
- Overlays는 `Grid.RowSpan="2"`로 전체를 덮는다.

---

## 2. 영역 명칭 사전 (이렇게 부른다)

| 영역 (한글) | Name (EN) | 코드 위치 | 설명 |
|---|---|---|---|
| 타이틀바 | **Title Bar** | MainWindow Row0 | 커스텀 창 크롬 |
| └ 브랜드 | Brand | `AgentManager` 워드마크 + accent 장식 바 | |
| └ 메뉴바 | **Menu Bar** | Agents·View·Help (`MenuButton_Click` → ContextMenu) | |
| └ 상태/사용량 | **Status Strip** (titlebar) | 실행/대기/완료 카운트 · 토큰 · COST · Quota | |
| └ 창 버튼 | Window Controls | Min/Max/Close | |
| 사이드바 | **Sidebar** (좌측 레일) | MainWindow Body 1열 | |
| └ 새 에이전트 | New Agent button | `NewAgentCommand` | |
| └ 내비 레일 | **Nav Rail** | Orchestrator/History/Scheduled (`ShowViewCommand`) | |
| └ 검색 | Search box | `Ctrl+F`, 세션 필터 | |
| └ 프로젝트 목록 | **Projects List** | `SelectProjectCommand` | |
| └ 세션 목록 | **Sessions List** | Active / Project / Archived 그룹 (`SelectSessionCommand`) | |
| └ CLI 히스토리 | **CLI History** | 외부 claude/codex 세션 가져오기 | |
| └ 상태 푸터 | Status Footer | local · 계정/엔진 표시 | |
| 메인 페인 | **Main Pane** / Console | MainWindow Body 2열 | View 호스트 |
| └ 오케스트레이터 | **Orchestrator** | `Views/OrchestratorView` | KPI + 카드 대시보드 |
| └ 활동 내역 | **Activity History** | `Views/HistoryView` | |
| └ 예약 | **Scheduled** | `Views/ScheduledView` | |
| └ 설정 | **Settings** | `Views/SettingsView` | 중앙 페인 설정 |
| └ 세션 뷰 | **Session View** | `Views/SessionView` | 활성 세션 콘솔 |
| 세션 뷰 ─ 상태 띠 | **Status Strip** (session) | 실행중/경과/모델/번역·승인 토글 | |
| 세션 뷰 ─ 네이티브 작업자 띠 | **Native Workers Strip** | subagent/bg-session 카드 | |
| 세션 뷰 ─ 트랜스크립트 | **Transcript** | 블록 DataTemplate 목록 | |
| 세션 뷰 ─ 컴포저 | **Composer** | 입력창 + @멘션·/슬래시·이미지·받아쓰기 | |
| 리뷰 페인 | **Review Pane** / Inspector | MainWindow Body 3열 (접이식) | |
| └ 변경 목록 | Changes List | worktree 변경 파일 | |
| └ 디프 뷰어 | **Diff Viewer** | `Controls/DiffViewer` | |
| └ 리뷰 액션 | Review Actions | Merge ▸ main / Commit only / Discard | |
| └ 디프 피드백 | Diff Feedback | diff 인라인 코멘트 후속 지시 | |
| 오버레이/모달 | **Overlays / Modals** | New Agent·New Project·New Schedule·About | fade+rise 진입 |

---

## 3. 렌더링 파이프라인 (무엇이 무엇을 그리는가)

```
App (Application)
 ├─ App.xaml  : MergedDictionaries = [Colors.<theme> + Theme.xaml + Icons.xaml + Strings.<lang>]
 │              + 전역 컨버터(BoolVis, SelBrush), SandboxModes
 ├─ App.OnStartup : 저장된 theme/language로 위 팔레트를 다시 머지(시작 색/언어 확정)
 └─ StartupUri → MainWindow
       └─ DataContext = AppViewModel   ← 창 전체의 루트 VM (partial, 파일 분할)
            ├─ Title Bar / Sidebar : AppViewModel에 직접 바인딩
            ├─ Main Pane : CurrentView(enum) → Is*View(bool) → Visibility(BoolVis)로
            │              5개 View UserControl 중 하나만 보임 (스택 방식)
            │     └ View들의 DataContext = AppViewModel (ElementName=Root)
            │        단, Session 페인은 ActiveSession(SessionViewModel)을 DataContext로 받음
            └─ Review Pane / Overlays : AppViewModel 바인딩
```

핵심 원리:
- **루트 VM 하나(`AppViewModel`)** 가 창 전체의 DataContext. 항목 단위는 `SessionViewModel`/`ProjectViewModel`/`HistoryRowViewModel`/`NativeWorkItemViewModel` 등.
- **View 전환** = 페인을 갈아끼우는 게 아니라, 5개 View를 같은 칸에 겹쳐 두고 `Visibility`로 하나만 노출. `CurrentView`(MainViewKind) 설정 → `IsOrchestratorView`/`IsSessionView`… 파생 bool → `BoolVis` 컨버터.
- **트랜스크립트**는 `MainWindow.Resources`의 블록별 `DataTemplate`(UserBlock/AgentTextBlock/ToolBlock/ErrorBlock/WorkingBlock/ThinkingBlock/ApprovalBlock)로 그려지며, `SessionView`의 가상화 리스트가 호스팅.
- **명령 바인딩**: 네이티브 `Button`/`MenuItem`은 `Command`, 클릭 가능한 `Border`(행·칩·스와치)는 `controls:MouseClick.Command` attached behavior. (코드비하인드 클릭 핸들러는 MVVM 점검에서 커맨드로 전환됨.)

---

## 4. MVVM 레이어

- **`AgentManager.Core`** — UI 비의존. 엔진 어댑터(cc/gx/agy), 정규화 이벤트, 번역기, 세션 실행, GitWorktree, 스케줄링, 관측(observation).
- **`AgentManager` (WPF)** — MVVM:
  - **ViewModels/** : `AppViewModel`(partial: `.Run/.Composer/.Settings/.Persistence/.History/.Scheduling/.Usage/.Dashboard/.Artifacts/.About/.NavCommands`), 항목 VM들, `EngineRegistry`, `Mvvm.cs`(ObservableObject/RelayCommand).
  - **Views/** : `SessionView·OrchestratorView·HistoryView·ScheduledView·SettingsView` (UserControl).
  - **Controls/** : 재사용 컨트롤·behavior·컨버터(아래 §6).
  - **Theme/** : 색 팔레트·스타일·문자열·팔레트 적용 로직(아래 §5).
- 규칙: **VM은 View 타입(`TextBox`/`MessageBox` 등)을 참조하지 않는다.** 다이얼로그는 `IDialogService`, 입력 포커스/캐럿 등은 View가 담당.

---

## 5. 테마 / 리소스 시스템

### 색 토큰
`Theme/Colors.<Theme>.xaml`에 **36개 색 토큰**(브러시 + `Bg0Color`)을 정의. 7개 테마 모두 **동일 키 집합**:
`Bg0..Bg5 · Line/LineSoft/LineBright · Txt0..Txt3 · Accent/AccentBright/AccentDim/AccentLine/AccentText/Run · Warn/Ok/Err/Info · Add/AddBg/Del/DelBg · Gx/GxLine/GxDim · Info(Line/Dim) · Agy(Line/Dim)`.

테마: **Dark · Light · Gray · Visual Studio · VS Code · Monokai · Nord** (`ThemePalette.All`).

### 라이브 전환의 핵심: DynamicResource
- 색 토큰은 XAML에서 **`{DynamicResource X}`** 로 참조한다(스타일·아이콘·문자열·컨버터는 `StaticResource`).
- 이유: WPF는 ResourceDictionary를 `Application.Resources`에 머지할 때 내부 브러시를 **frozen** 처리한다 → `StaticResource` + in-place 색 변경으로는 라이브 반영이 **불가능**. `DynamicResource`는 리소스 엔트리가 교체되면 소비자가 재해석하므로 라이브 전환이 동작.
- **`ThemePalette.Apply(theme)`** : `Colors.<theme>.xaml`을 읽어 `Application.Resources[key]`를 **덮어쓰기** → 모든 DynamicResource 소비자 즉시 갱신.
- **`AccentPalette.Apply(name)`** : accent 5키(Accent/AccentBright/AccentLine/AccentDim/Run)만 덮어씀. 테마 적용 직후 사용자 accent를 재적용.
- C# 컨트롤(`DiffViewer`/`MarkdownViewer`)은 `TryFindResource(key)`로 리소스 브러시를 조회 → 교체된 엔트리를 다음 렌더에 반영.

### 적용 흐름
- 시작: `App.OnStartup`이 저장 테마의 `Colors.<theme>.xaml`을 머지.
- 변경: Settings ▸ Appearance의 테마 칩 → `ThemeSelectCommand` → `SettingsTheme` 세터 → `ThemePalette.Apply` + `AccentPalette.Apply`(라이브 미리보기). **Save**로 영속, **Cancel**은 저장값으로 복원(`CloseSettings`).
- 영속: `state.Settings.Theme`(문자열). 기본 `dark`.

### 기타 리소스
- **타이포그래피**: 번들 **IBM Plex Sans/Mono**(Latin), 한글 등은 시스템 폰트로 폴백(`Sans`/`Mono` FontFamily).
- **i18n**: `Strings.Ko.xaml`/`Strings.En.xaml` (`L.*` 키, `App.L(...)`). 언어 전환은 재시작 적용.
- **밀도**: `DensityScale` → Body `LayoutTransform`(ScaleTransform).
- **모서리**: `RSm/RMd/RLg`(4/7/10).

---

## 6. 컨트롤 · behavior · 컨버터 (`Controls/`)

| 항목 | 종류 | 역할 |
|---|---|---|
| `IconView` | 컨트롤 | 벡터 Geometry 아이콘 렌더 (`IconX` 등 `Icons.xaml`) |
| `MarkdownViewer` | 컨트롤 | assistant 응답 마크다운 → FlowDocument |
| `DiffViewer` | 컨트롤 | Git diff 색상 렌더 |
| `StatusDot` | 컨트롤 | 상태 닷 + running **pulse** 헤일로 |
| `Spinner` | 컨트롤 | 회전 로더(원본 `.spin`) |
| `MouseClick` | attached behavior | `Border` 좌클릭 → `ICommand` |
| `BorderHover` | attached behavior | 호버 시 border-color 트랜지션(.14s) |
| `PasswordBoxAssistant` | attached behavior | API key PasswordBox 바인딩 |
| `GridLengthAnimation` | 애니메이션 | Review pane 폭 0↔420 슬라이드(.22s) |
| `SelectedBrushConverter`(SelBrush) | 컨버터 | 선택 id 일치 시 AccentLine, 아니면 Line |
| `BooleanToVisibilityConverter`(BoolVis) | 컨버터 | bool → Visibility |

---

## 7. 애니메이션 맵 (원본 디자인 재현)

| 효과 | 위치 | 구현 |
|---|---|---|
| **pulse** | 상태 닷(running) | `StatusDot` 헤일로 1→2.6 확대 + 페이드, 1.6s |
| **spin** | 세션 running pill | `Spinner` 0.7s 회전 |
| **blink** | WorkingBlock 점 | opacity 1↔0 discrete, 1s |
| **fade / rise** | 오버레이/모달 진입 | `OverlayFadeIn` / `OverlayRiseIn`(Theme.xaml) |
| **bar (equalizer)** | Orchestrator/Sidebar 카드 | Spark 스토리보드 |
| **border hover** | orch 카드 | `BorderHover` ColorAnimation |
| **pane slide** | Review pane | `GridLengthAnimation` |

---

## 8. 컨벤션 (수정 시 지킬 것)

- **색은 항상 `{DynamicResource ...}`**, 그 외 리소스(스타일/아이콘/문자열/컨버터)는 `{StaticResource ...}`. (새 색 추가 시 7개 `Colors.*.xaml` 전부에 같은 키로 추가.)
- 클릭 가능한 `Border`는 `controls:MouseClick.Command`로 커맨드 바인딩(코드비하인드 핸들러 지양). 네이티브 `Button`/`MenuItem`은 `Command`.
- ItemsControl 내부에서 루트 VM 커맨드는 `RelativeSource AncestorType=UserControl`의 `DataContext.<Cmd>`(뷰) 또는 `Source={x:Reference Root}`(MainWindow)로 바인딩.
- 새 문자열은 `Strings.Ko`/`Strings.En` **양쪽**에 추가(키 동일성 유지).
- 새 View 추가 시: `MainViewKind`에 항목 + `Is*View` 파생 prop + Main Pane에 Visibility 바인딩된 UserControl 추가.
- VM에서 View 타입 참조 금지(다이얼로그는 `IDialogService`).

---

## 9. 관련 문서

- [DESIGN_SPEC_KO.md](DESIGN_SPEC_KO.md) — 아키텍처·정규화 이벤트
- [FEATURES_KO.md](FEATURES_KO.md) — 기능 정의
- [PROGRESS_KO.md](PROGRESS_KO.md) — 진행 로그(단일 진실 소스)
- [README.md](../README.md) — 제품 개요·빌드
