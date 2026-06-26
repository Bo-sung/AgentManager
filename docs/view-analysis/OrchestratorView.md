# OrchestratorView 분석

> **파일 경로**
> - `src/AgentManager/Views/OrchestratorView.xaml`
> - `src/AgentManager/Views/OrchestratorView.xaml.cs`
> - `src/AgentManager/ViewModels/AppViewModel.Dashboard.cs`
> - `src/AgentManager/ViewModels/AppViewModel.WorkerTasks.cs`

---

## 1. 목적

`OrchestratorView`는 AgentManager 애플리케이션의 **오케스트레이터 대시보드 중앙 패널**이다. 현재 프로젝트에 속한 모든 에이전트 세션의 상태를 한눈에 보여주며, 워커 백로그(backlog) 관리·태스크 할당·큐 실행 등 멀티 에이전트 워크플로를 조작하는 모든 UI를 담는다.

주요 역할:
- KPI 카드로 전체 세션 상태(`running / waiting / done / error`)와 총 토큰 처리량을 집계 표시
- **워커 백로그**: `worker-prompt` 스킬이 파일 스풀(spool)에 써놓은 태스크를 인제스트하여 검토·할당할 수 있는 목록
- **워커 큐**: 각 워커 세션별로 순서가 있는 태스크 큐와 실행 컨트롤
- **라이브 워커 / 최근 완료 세션**: 공유 카드 템플릿(`OrchCardTemplate`)으로 동일한 UI를 재사용

---

## 2. 레이아웃 구조

```
UserControl (OrchestratorView)
└── Grid
    ├── Grid.Resources
    │   ├── Storyboard "SparkStoryboard"   ← 이퀄라이저 애니메이션 정의
    │   └── DataTemplate "OrchCardTemplate" ← 세션 카드 (live + recent 공유)
    └── DockPanel (LastChildFill)
        ├── [DockPanel.Dock=Top] 헤더 바 (높이 44)
        │   ├── IconView (IconGrid)
        │   ├── TextBlock "Orchestrator" 제목
        │   ├── TextBlock 부제목
        │   └── Button "새 에이전트" (AccentButton, NewAgentCommand)
        └── ScrollViewer (나머지 전체, 세로 스크롤)
            └── StackPanel (Margin=16)
                ├── UniformGrid (5열 1행, H=78) ← KPI 카드 5개
                ├── StackPanel (HasWorkerTasks 조건부) ← 워커 백로그 + 큐 섹션
                │   ├── 섹션 헤더 (DockPanel) + BacklogTasks ItemsControl
                │   ├── (HasWorkerQueues 조건부) 워커 큐 섹션 헤더
                │   ├── WorkerQueues ItemsControl
                │   └── Popup (ShowAssignPicker) ← 워커 선택 팝업
                ├── 섹션 헤더 "라이브 워커"
                ├── TextBlock (빈 상태 메시지, ActiveSessions.Count==0)
                ├── ItemsControl (ActiveSessions, WrapPanel)
                ├── 섹션 헤더 "최근 완료"
                ├── TextBlock (빈 상태 메시지, ProjectSessions.Count==0)
                └── ItemsControl (ProjectSessions, WrapPanel)
```

### OrchCardTemplate 내부 구조 (DataTemplate)

```
Border (Width=236, CornerRadius=8)
└── DockPanel
    ├── [Dock=Top] 카드 헤더
    │   ├── IconView (EngineIcon)
    │   ├── TextBlock StatusLabel + StatusDot
    │   └── StackPanel: AgentName / Model
    ├── [Dock=Bottom] 카드 푸터
    │   ├── IconView (IconBolt) + TokensLabel
    │   ├── Button "세션 열기" (SelectSessionCommand)
    │   └── Button "Diff" (OpenSessionReviewCommand)
    └── StackPanel (카드 바디)
        ├── TextBlock Title
        ├── IconBranch + BranchTail + RunningElapsedLabel
        ├── 이퀄라이저 바 (IsRunning 조건부, 5개 Border + Activity 텍스트)
        └── Diff 바 (HasDiff && !IsRunning 조건부)
            ├── 프로그레스 바 (추가/삭제 비율)
            └── TextBlock (+N −N · N files)
```

---

## 3. ViewModel 바인딩

DataContext는 부모가 주입하는 `AppViewModel` 인스턴스이다.

### 3-1. KPI 카드 바인딩

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `RunningCount` | `AppViewModel.Dashboard.cs:20` | `Status == "running"` 세션 수 |
| `WaitingCount` | `AppViewModel.Dashboard.cs:21` | `Status == "waiting"` 세션 수 |
| `DoneCount` | `AppViewModel.Dashboard.cs:22` | `Status == "done"` 세션 수 |
| `FailedCount` | `AppViewModel.Dashboard.cs:23` | `Status == "error"` 세션 수 |
| `FleetThroughputLabel` | `AppViewModel.Dashboard.cs:59` | 전체 세션의 `TokensIn + TokensOut` 합산 (`"1.2k tok"` 형식) |

### 3-2. 워커 백로그 섹션 바인딩

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `HasWorkerTasks` | `AppViewModel.WorkerTasks.cs:85` | `HasBacklog \|\| HasWorkerQueues`; 섹션 전체 `Visibility` 제어 |
| `BacklogCount` | `AppViewModel.WorkerTasks.cs:86` | `BacklogTasks.Count`; 헤더 카운터 칩 |
| `BacklogTasks` | `AppViewModel.WorkerTasks.cs:79` | `ObservableCollection<WorkerTaskViewModel>`; 미할당 태스크 목록 |
| `HasWorkerQueues` | `AppViewModel.WorkerTasks.cs:84` | 워커 큐 서브섹션 `Visibility` 제어 |
| `WorkerQueues` | `AppViewModel.WorkerTasks.cs:81` | `ObservableCollection<WorkerQueueViewModel>`; 워커별 큐 목록 |
| `ShowAssignPicker` | `AppViewModel.WorkerTasks.cs:113` | `TwoWay`; Popup `IsOpen` 제어 |
| `WorkerPool` | `AppViewModel.DelegationUi.cs:62` | 할당 팝업 내 선택 가능한 워커 세션 목록 |

#### BacklogTasks 아이템 (`WorkerTaskViewModel`) 바인딩

| 바인딩 경로 | 소스 | 설명 |
|---|---|---|
| `Title` | `WorkerTaskViewModel.Title` | 태스크 제목 |
| `PromptPreview` | `WorkerTaskViewModel.PromptPreview` | 프롬프트 첫 200자 미리보기 |
| `EngineLabel` | `WorkerTaskViewModel.EngineLabel` | 엔진 이름 대문자 표시 (예: `"CLAUDE"`) |
| `HasEngine` | `WorkerTaskViewModel.HasEngine` | 엔진 레이블 Border `Visibility` |

#### WorkerQueues 아이템 (`WorkerQueueViewModel`) 바인딩

| 바인딩 경로 | 소스 | 설명 |
|---|---|---|
| `WorkerName` | `WorkerQueueViewModel.WorkerName` | 워커 세션 이름 |
| `CountLabel` | `WorkerQueueViewModel.CountLabel` | 대기 중 태스크 수 |
| `EngineLabel` | `WorkerQueueViewModel.EngineLabel` | 엔진 이름 |
| `HasEngine` | `WorkerQueueViewModel.HasEngine` | 엔진 레이블 가시성 |
| `HasFinished` | `WorkerQueueViewModel.HasFinished` | "완료 정리" 버튼 가시성 |
| `Tasks` | `WorkerQueueViewModel.Tasks` | 이 워커에 할당된 `WorkerTaskViewModel` 컬렉션 |

#### WorkerQueues › Tasks 아이템 (`WorkerTaskViewModel`) 바인딩

| 바인딩 경로 | 설명 |
|---|---|
| `Title` | 태스크 제목 |
| `StatusLabel` | 상태 텍스트 (`L.TaskAssigned` 등 다국어 키로 변환) |

### 3-3. 세션 카드 (`OrchCardTemplate`) 바인딩

카드 템플릿은 `ActiveSessions`와 `ProjectSessions` 양쪽에서 공용으로 사용한다. 각 아이템은 `SessionViewModel`이다.

| 바인딩 경로 | 설명 |
|---|---|
| `IsLive` | `False`이면 카드 `Opacity`를 0.72로 낮춤 (흐리게) |
| `StatusLabel` | 상태 텍스트 (예: `"running"`, `"done"`) |
| `Status` | DataTrigger로 `Foreground` 색상 결정 (`Accent/Warn/Ok/Err`) |
| `AgentName` | 에이전트 이름 (볼드) |
| `Model` | 사용 중인 모델명 (작은 글씨) |
| `TokensLabel` | 토큰 소비량 레이블 |
| `Title` | 세션 타이틀 (굵게 처리) |
| `BranchTail` | git 브랜치 이름 끝 부분 |
| `RunningElapsedLabel` | 실행 경과 시간 (running 중에만 표시) |
| `IsRunning` | 이퀄라이저 바 + Activity 텍스트 `Visibility` 제어; Storyboard 트리거 |
| `Activity` | 현재 실행 중인 활동 문자열 |
| `HasDiff` | Diff 섹션 가시성 조건 (HasDiff=True AND IsRunning=False) |
| `DiffAddedStar` | Grid `ColumnDefinition Width`; 추가 라인 비율 (`*` 단위) |
| `DiffRemovedStar` | Grid `ColumnDefinition Width`; 삭제 라인 비율 |
| `DiffRemainderStar` | Grid `ColumnDefinition Width`; 나머지 비율 |
| `DiffAdded` | 추가된 라인 수 텍스트 |
| `DiffRemoved` | 삭제된 라인 수 텍스트 |
| `DiffFiles` | 변경된 파일 수 텍스트 |
| `FilesSuffix` | 파일 단위 접미사 (예: `"files"`) |

### 3-4. 커맨드 바인딩

| 커맨드 | 소스 파일 | 동작 |
|---|---|---|
| `NewAgentCommand` | `AppViewModel.cs:90` | `ShowNewAgent = true`로 신규 에이전트 패널 표시 |
| `SelectSessionCommand` | `AppViewModel.cs:95` | 선택한 세션을 `ActiveSession`으로 설정 후 `Session` 뷰로 이동 |
| `OpenSessionReviewCommand` | `AppViewModel.NavCommands.cs:79` | 세션 Diff 리뷰 뷰로 이동 |
| `AssignTaskCommand` | `AppViewModel.WorkerTasks.cs:121` | `PendingAssign` 설정 후 `ShowAssignPicker = true`로 팝업 열기 |
| `AssignToWorkerCommand` | `AppViewModel.WorkerTasks.cs:126` | 팝업에서 선택한 워커에 태스크 할당 (`_taskStore.Assign`) |
| `UnassignTaskCommand` | `AppViewModel.WorkerTasks.cs:135` | 태스크를 백로그로 복귀 (`_taskStore.Unassign`) |
| `DeleteTaskCommand` | `AppViewModel.WorkerTasks.cs:136` | 태스크 삭제 (`_taskStore.Delete`) |
| `RunTaskCommand` | `AppViewModel.WorkerTasks.cs:141` | 단일 태스크 즉시 실행; `CanRun`(Assigned or Failed)일 때만 활성화 |
| `RunQueueCommand` | `AppViewModel.WorkerTasks.cs:143` | 워커 큐 전체를 순차 실행; `CanRunQueue`일 때만 활성화 |
| `MoveTaskUpCommand` | `AppViewModel.WorkerTasks.cs:137` | 큐 내 태스크를 위로 이동 |
| `MoveTaskDownCommand` | `AppViewModel.WorkerTasks.cs:138` | 큐 내 태스크를 아래로 이동 |
| `ClearFinishedCommand` | `AppViewModel.WorkerTasks.cs:139` | 완료/실패 태스크를 큐에서 제거 |

---

## 4. 사용된 커스텀 컨트롤

| 컨트롤 | 용도 |
|---|---|
| `controls:IconView` | 아이콘 렌더링 컨트롤. `Icon`, `Width`, `Height`, `Foreground` 속성을 받아 벡터 아이콘을 표시. `EngineIcon` StaticStyle 포함 |
| `controls:StatusDot` | `Status` 속성에 따라 색상이 바뀌는 작은 상태 점. 카드 헤더 우측에 위치 |
| `controls:BorderHover` (첨부 속성) | `Border`에 첨부 가능한 호버 브러시. 마우스 오버 시 `BorderBrush`를 `AccentLine`으로 전환 |

### 스타일 / 리소스 키

| 키 | 종류 | 용도 |
|---|---|---|
| `ChipButton` | Style | 작은 칩 형태의 버튼 |
| `AccentButton` | Style | 강조색 배경 버튼 ("새 에이전트") |
| `MenuButton` | Style | 아이콘 전용 소형 버튼 |
| `CodeChip` | Style | 모노스페이스 숫자 칩 TextBlock |
| `HudTicks` | Style | KPI 카드 배경 장식 틱 마크 |
| `BoolVis` | Converter | `bool → Visibility` 변환기 |
| `Mono` | FontFamily | 고정폭 폰트 |

---

## 5. 애니메이션 & 트리거

### 5-1. Spark 이퀄라이저 애니메이션 (`SparkStoryboard`)

- **정의 위치**: `Grid.Resources` 내 `Storyboard x:Key="SparkStoryboard"`
- **구조**: 5개의 `DoubleAnimationUsingKeyFrames`가 `Bar1`~`Bar5`의 `ScaleTransform.ScaleY`를 각각 애니메이션
- **파라미터**:
  - 각 바의 `ScaleY`: `0.22 → 1.0 → 0.22` (이징: `QuadraticEase EaseInOut`)
  - 총 Duration: `1.1초`, `RepeatBehavior="Forever"`
  - `BeginTime` 스태거: 0ms → 130ms → 260ms → 390ms → 520ms (시각적으로 파도 효과)
  - `RenderTransformOrigin="0.5,1"` — 바의 하단을 고정점으로 스케일 (아래에서 위로 솟는 이퀄라이저)
- **트리거**: `OrchCardTemplate` 내 `DataTemplate.Triggers`에서 `IsRunning=True`일 때 `BeginStoryboard`, `False`로 돌아오면 `StopStoryboard`

> **주의**: 이 Storyboard는 `DataTemplate.Triggers`에서만 작동한다. `Style.Triggers`로 이동하면 `TargetName(Bar1~5)` 참조가 해소되지 않아 런타임 오류가 발생한다. (`OrchestratorView.xaml:201` 주석 참조)

### 5-2. DataTrigger — 카드 투명도

- `IsLive=False` → `Border.Opacity = 0.72` (완료/중단된 세션 카드를 흐리게)

### 5-3. DataTrigger — 상태별 색상

- `StatusLabel` TextBlock: `Status` 값에 따라 `Foreground`를 `Accent / Warn / Ok / Err` 동적 리소스로 전환

### 5-4. DataTrigger — Diff 섹션 표시

- `HasDiff=True` → `Visibility=Visible`
- `IsRunning=True` → `Visibility=Collapsed` (실행 중에는 diff 숨김, 이퀄라이저로 대체)

### 5-5. DataTrigger — 빈 상태 메시지

- `ActiveSessions.Count=0` → "라이브 워커 없음" 텍스트 표시
- `ProjectSessions.Count=0` → "최근 세션 없음" 텍스트 표시

---

## 6. 코드 비하인드 vs. XAML 역할 분리

### 코드 비하인드 (`OrchestratorView.xaml.cs`)

```csharp
public partial class OrchestratorView : UserControl
{
    public OrchestratorView() => InitializeComponent();
}
```

코드 비하인드는 **생성자 한 줄**뿐이다. DataContext 주입, 이벤트 핸들러, 애니메이션 제어가 전혀 없다.

| 항목 | 위치 |
|---|---|
| 모든 상태·로직 | `AppViewModel` (partial 클래스) |
| 애니메이션 제어 | XAML `DataTemplate.Triggers` + `Storyboard` |
| Visibility 제어 | XAML `DataTrigger` + `BoolVis` Converter |
| 커맨드 실행 | `RelayCommand` (ViewModel 계층) |
| 팝업 표시 제어 | `ShowAssignPicker` 프로퍼티 바인딩 (`TwoWay`) |

MVVM 패턴이 엄격하게 지켜져 있으며, View는 순수하게 선언적 XAML만으로 구성된다.

---

## 7. 핵심 특징 (Key Highlights)

1. **공유 카드 템플릿 (`OrchCardTemplate`)의 이중 사용**
   라이브 워커와 최근 완료 세션이 동일한 `DataTemplate`을 재사용한다. `IsLive` 바인딩 하나로 투명도만 달리하여 시각적 구분을 유지하면서 코드 중복을 제거했다.

2. **DataTemplate.Triggers에서만 작동하는 이퀄라이저 애니메이션**
   `TargetName`으로 DataTemplate 내부 요소(Bar1~5)를 직접 참조해야 하므로, Storyboard를 `Style.Triggers`가 아닌 `DataTemplate.Triggers`에 배치해야 한다. 이 제약이 XAML 주석으로 명시되어 있어 유지보수자에게 중요한 정보다.

3. **파일 스풀 기반 태스크 인제스트**
   `worker-prompt` 스킬이 `AGENTMANAGER_TASK_SPOOL` 환경 변수가 가리키는 디렉터리에 JSON 파일을 쓰면, `FileSystemWatcher`가 감지하여 150ms 디바운스 후 UI 스레드에서 `BacklogTasks`에 추가한다. 이 비동기 인제스트 파이프라인 덕분에 외부 CLI 툴과 UI 간 결합도가 없다.

4. **워커 선택 팝업의 커맨드 바인딩 경로**
   Popup 내부의 `AssignToWorkerCommand` 바인딩이 일반적인 `RelativeSource AncestorType=UserControl`이 아닌 `ElementName=AssignPickerRoot`를 사용한다. `Popup`은 시각 트리에서 분리되어 있어 `RelativeSource`로 `UserControl`을 탐색할 수 없기 때문이다.

5. **`WorkerTaskStore`가 단일 진실 출처(Single Source of Truth)**
   태스크 상태를 `WorkerTaskStore`(Core 계층)가 소유하고, ViewModel은 `Changed` 이벤트를 받아 `RebuildTaskViews()`로 View 전용 컬렉션을 재구성한다. ViewModel이 도메인 상태를 직접 보유하지 않아 도메인 로직과 표현 로직이 명확히 분리된다.

---

## 8. 유지보수 시 주의 사항

- **Storyboard TargetName 범위**: `SparkStoryboard`는 반드시 `DataTemplate.Triggers`에서 시작해야 한다. 이를 `Style.Triggers`로 옮기면 `Bar1`~`Bar5` 참조가 깨진다.
- **Popup 커맨드 바인딩**: `AssignPickerRoot`라는 `x:Name`은 Popup 내부의 커맨드 바인딩 앵커 역할을 한다. 이름 변경 시 `AssignToWorkerCommand` 바인딩이 silently 끊긴다.
- **`DiffAddedStar` / `DiffRemovedStar` / `DiffRemainderStar`**: `Grid.ColumnDefinition Width`에 직접 `*` 단위 값을 바인딩하는 방식이다. 이 값이 음수나 `NaN`이 되면 레이아웃 예외가 발생할 수 있으므로 `SessionViewModel`에서 반드시 0 이상의 값을 보장해야 한다.
- **`HasWorkerTasks` 가시성 계층**: `BacklogTasks`와 `WorkerQueues` 양쪽이 모두 비어야 전체 섹션이 숨겨진다. `RebuildTaskViews()` 호출 후 `OnChanged`를 빠뜨리면 UI가 빈 섹션을 표시한 채로 남는다.
- **`OrchCardTemplate`의 `DataContext.SelectSessionCommand` 바인딩**: `ItemsControl` 내부에서 `RelativeSource AncestorType=UserControl`로 커맨드를 참조하므로, 카드 템플릿을 다른 `UserControl`로 이동하면 바인딩 경로를 재검토해야 한다.
