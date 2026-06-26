# ScheduledView 분석

> 소스 파일
> - `src/AgentManager/Views/ScheduledView.xaml`
> - `src/AgentManager/Views/ScheduledView.xaml.cs`
> - `src/AgentManager/ViewModels/AppViewModel.Scheduling.cs`
> - `src/AgentManager.Core/Scheduling/TimerScheduler.cs`
> - `src/AgentManager/ViewModels/ScheduledJobViewModel.cs` (보조)
> - `src/AgentManager.Core/Scheduling/ScheduleTrigger.cs` (보조)

---

## 목적

`ScheduledView`는 예약 작업(Scheduled Job) 목록을 표시하고, 새 예약을 생성하는 진입점을 제공하는 중앙 패널이다. 역할은 세 가지다:

1. **목록 표시** — 저장된 예약 작업을 카드 형태로 나열하고 다음 실행 시각을 실시간 표시
2. **생성 진입** — "New Schedule" 버튼으로 신규 예약 생성 오버레이(`ShowNewSchedule`)를 활성화
3. **런타임 위임** — 실제 스케줄링 평가는 `TimerScheduler`(Core 레이어)가 담당하고, 실행은 `AppViewModel.RunScheduledJob()`이 UI 스레드로 위임받아 처리

`DataContext`는 부모가 주입하는 `AppViewModel`이며, `ScheduledJobViewModel`은 `ScheduledJob`(코어 모델)의 읽기 전용 래퍼다.

---

## 레이아웃 구조

```
UserControl (ViewRoot, Background=Bg0)
└── DockPanel (LastChildFill=True)
    ├── Border [Dock=Top, Height=44]       ← 헤더
    │   └── DockPanel (LastChildFill=False)
    │       ├── IconView [Left]            ← 캘린더 아이콘 (Accent)
    │       ├── TextBlock [Left]           ← "예약 작업" 제목
    │       ├── TextBlock [Left]           ← "· N개" 카운트
    │       └── Button [Right]             ← "+ New Schedule" (AccentButton)
    └── ScrollViewer (VerticalScrollBarVisibility=Auto)   ← 본문
        └── StackPanel (Margin=16)
            └── ItemsControl (ScheduledJobs)
                └── DataTemplate (각 예약 카드)
                    └── Border (Bg2, CornerRadius=8, Padding=14)
                        └── Grid (3열)
                            ├── Column 0 (40px): IconView   ← 캘린더 아이콘
                            ├── Column 1 (*): StackPanel    ← 제목 + 메타 정보 행
                            │   ├── TextBlock               ← Title (SemiBold)
                            │   └── StackPanel (Horizontal)
                            │       ├── Border + TextBlock   ← EngineBadge 알약
                            │       ├── TextBlock           ← EngineName
                            │       ├── IconView + TextBlock ← CadenceText (Refresh 아이콘)
                            │       └── IconView + TextBlock ← TargetBranch (Branch 아이콘)
                            └── Column 2 (150px): StackPanel ← 다음 실행 시각
                                ├── TextBlock               ← "Next run" 레이블
                                └── TextBlock               ← NextRunLabel (Accent, SemiBold)
```

---

## ViewModel 바인딩 상세

### `DataContext` = `AppViewModel` (헤더 영역)

| XAML 요소 | 바인딩 속성 | 모드 | 역할 |
|---|---|---|---|
| 헤더 카운트 `Run` | `ScheduledJobs.Count` | OneWay | 저장된 예약 작업 수 표시 |
| "New Schedule" `Button` | `NewScheduleCommand` | OneWay | 클릭 시 `OpenNewSchedule()` → `ShowNewSchedule = true` |

### `DataContext` = `ScheduledJobViewModel` (카드 DataTemplate 내부)

| XAML 요소 | 바인딩 속성 | 역할 |
|---|---|---|
| 카드 제목 `TextBlock` | `Title` | 예약 작업 이름 (SemiBold, 12.5px) |
| 엔진 배지 `TextBlock` | `EngineBadge` | 엔진 단축 식별자 (예: "CC", "GX") — Mono 9.5px 알약 |
| 엔진 이름 `TextBlock` | `EngineName` | 엔진 전체 이름 (예: "Claude Code") |
| 주기 `TextBlock` | `CadenceText` | 사람이 읽는 주기 텍스트 (예: "Every day · 02:00") — Mono 10px |
| 대상 브랜치 `TextBlock` | `TargetBranch` | 실행 시 사용할 git 브랜치 — Mono 10px |
| 다음 실행 `TextBlock` | `NextRunLabel` | 다음 실행까지 남은 시간 (예: "in 3h", "due now", "on trigger") |

> **`NextRunLabel` 포맷 규칙** (`ScheduledJobViewModel.FormatNextRun`):
> - `NextRunUtc`가 null → `"on trigger"` (이벤트 트리거 또는 미계산)
> - 남은 시간 ≤ 0 → `"due now"`
> - 1일 이상 → `"in Nd"` (올림)
> - 1시간 이상 → `"in Nh"` (올림)
> - 1분 미만 포함 → `"in Nm"` (최소 1, 올림)

---

## 커스텀 컨트롤 사용

| 컨트롤 | 사용 위치 | 속성 |
|---|---|---|
| `controls:IconView` | 헤더 캘린더 아이콘 | `Icon=IconCalendar`, `Foreground=Accent`, 15×15 |
| `controls:IconView` | "New Schedule" 버튼 내 플러스 아이콘 | `Icon=IconPlus`, `Foreground=AccentText`, 13×13 |
| `controls:IconView` | 카드 좌측 캘린더 아이콘 | `Icon=IconCalendar`, `Foreground=Txt2`, 16×16 |
| `controls:IconView` | 주기 앞 새로고침 아이콘 | `Icon=IconRefresh`, `Foreground=Txt2`, 10×10 |
| `controls:IconView` | 브랜치 앞 브랜치 아이콘 | `Icon=IconBranch`, `Foreground=Txt2`, 10×10 |

코드-비하인드가 없고 Spinner·MouseClick 첨부 속성도 사용하지 않아, `SessionView`보다 커스텀 컨트롤 의존도가 낮다.

---

## 애니메이션 & 트리거

`ScheduledView.xaml`에는 DataTrigger, 속성 트리거, 애니메이션이 **없다**. 모든 시각 분기는 아래 두 가지 정적 스타일로만 처리된다:

- **`AccentButton`** (정적 리소스) — "New Schedule" 버튼의 배경·텍스트 색상
- **`DynamicResource`** — `Bg0`, `Bg1`, `Bg2`, `Line`, `LineBright`, `LineSoft`, `Txt0`, `Txt1`, `Txt2`, `Accent`, `AccentText` 등 테마 토큰으로 다크/라이트 전환에 자동 반응

> 카드 선택·호버 상태 강조나 `IsEnabled` 분기 트리거는 현재 구현되어 있지 않다.

---

## Code-Behind vs XAML 역할 분담

### XAML (`ScheduledView.xaml`)
- 전체 레이아웃, 데이터 바인딩, 카드 DataTemplate 정의
- 스크롤 컨테이너(ScrollViewer)와 ItemsControl 구성

### Code-Behind (`ScheduledView.xaml.cs`)
```csharp
public partial class ScheduledView : UserControl
{
    public ScheduledView() => InitializeComponent();
}
```
코드-비하인드는 **`InitializeComponent()` 호출 한 줄**이 전부다. 이벤트 핸들러, 스크롤 관리, P/Invoke 등 일체 없음. 뷰는 완전히 XAML 선언과 ViewModel 바인딩으로만 구동된다.

### `AppViewModel.Scheduling.cs` (스케줄링 로직 전담)

#### 새 예약 생성 오버레이 상태

| 속성 | 기본값 | 역할 |
|---|---|---|
| `ShowNewSchedule` | `false` | 오버레이 표시 여부 (MainWindow에서 바인딩) |
| `NewScheduleSelectedEngine` | 첫 번째 엔진 | 선택된 실행 엔진 |
| `NewScheduleTitle` | `""` | 작업 이름 |
| `NewSchedulePrompt` | `""` | 실행할 프롬프트 (비어 있으면 Title 사용) |
| `NewScheduleCadence` | `"Every day · 02:00"` | 주기 텍스트 |
| `NewScheduleTargetBranch` | `"agent/scheduled-task"` | 대상 브랜치 |
| `NewScheduleError` | `""` | 유효성 오류 메시지 |

#### `CreateSchedule()` 처리 흐름

```
입력 유효성 검증
  └─ cadence → TryParseCadenceToCron() 변환 시도
       ├─ "on push"로 시작 → Kind="Event" (cron 불필요)
       └─ 파싱 실패 → NewScheduleError 설정 후 중단

ScheduledJob 객체 생성 (Id = "job" + DateTime.Ticks)
  └─ ScheduleStore.Save() → ShowNewSchedule=false → LoadScheduledJobs()
```

#### `RunScheduledJob()` 처리 흐름

```
프로젝트 해결 (우선순위):
  1. ProjectId로 기존 프로젝트 조회
  2. ProjectPath로 경로 일치 검색
  3. ProjectPath 폴더 존재 시 임시 ProjectViewModel 생성 후 Projects에 추가
  4. 위 모두 실패 시 ActiveProject 또는 Projects[0] 사용

SessionViewModel 생성 (엔진·제목·브랜치·모델 설정)
  └─ Transcript에 WorkingBlock(실행 마커) 추가
  └─ _allSessions.Insert(0, session) → ActiveSession 설정
  └─ LoadScheduledJobs() + SaveState()
  └─ RunTurnAsync(session, prompt) 비동기 실행
```

---

## `TimerScheduler` 동작 원리

`AgentManager.Core.Scheduling.TimerScheduler`는 스케줄 평가를 담당하는 백그라운드 서비스다.

### 루프 구조

```
Start()
  └─ Task.Run(RunTimerAsync)   ← 반드시 백그라운드 스레드 (UI 스레드면 데드락)
       └─ PeriodicTimer(30초)
            └─ while (WaitForNextTickAsync)
                 └─ EvaluateJobs()
```

### `EvaluateJobs()` 처리

1. `lock(_lock)`으로 `_jobs` 스냅샷 복사
2. `job.Enabled == true && job.NextRunUtc <= UtcNow` 인 작업 필터링
3. 해당 작업의 `LastRunUtc`를 `UtcNow`로 갱신 후 `ScheduleStore.Save()`
4. `JobDue` 이벤트 발화 — `AppViewModel.Scheduler_JobDue`가 `Dispatcher.InvokeAsync`로 UI 스레드에 위임

### 스레드 안전성

| 메서드 | 보호 방식 |
|---|---|
| `Start()` | `lock(_lock)` — 중복 시작 방지 |
| `Stop()` | `lock(_lock)` 밖에서 `Cancel()` + `GetAwaiter().GetResult()` |
| `Reload()` | `lock(_lock)` — 파일에서 재적재 |
| `EvaluateJobs()` | 스냅샷 + `lock(_lock)` 내 업데이트 분리 |

> **데드락 방지 설계**: `Stop()`의 `GetAwaiter().GetResult()`는 `RunTimerAsync`가 UI SynchronizationContext에 묶이지 않아야 블록 없이 완료된다. 이 때문에 `Task.Run()` (백그라운드 스레드 풀)으로만 시작하며 UI 스레드에서 직접 `await`하지 않는다.

### `ScheduleTrigger.TryParseCadenceToCron()` — 사람 언어 → Cron 변환

| 입력 예시 | 변환 결과 |
|---|---|
| `"Every day · 02:00"` | `"0 2 * * *"` |
| `"daily 09:30"` | `"30 9 * * *"` |
| `"매일 06:00"` | `"0 6 * * *"` |
| `"Monday 10:00"` | `"0 10 * * 1"` |
| `"월요일 08:30"` | `"30 8 * * 1"` |
| `"0 2 * * *"` (이미 cron) | `"0 2 * * *"` (통과) |
| `"on push to main"` | `null` (이벤트 트리거, cron 불필요) |

한국어 요일명(월·화·수·목·금·토·일 / 월요일~일요일)과 영어 요일명 모두 지원. 구분자는 스페이스·`·`·쉼표·하이픈.

> **v1 미구현**: `Kind="Event"` 트리거는 `GetNextRunUtc()`가 `null`을 반환하므로 UI에 `"on trigger"` 표시되며 자동 실행되지 않는다.

---

## 핵심 특징 (Key Highlights)

1. **완전 수동 뷰 (Zero Code-Behind)**
   코드-비하인드가 `InitializeComponent()` 한 줄뿐이다. 모든 상태 관리와 로직이 `AppViewModel`에 집중되어 있어 뷰는 순수한 데이터 표현 계층으로 유지된다. `SessionView`의 복잡한 코드-비하인드(스크롤, P/Invoke, 키보드 처리)와 대조적이다.

2. **30초 폴링 기반 Cron 스케줄러**
   `PeriodicTimer(30초)` + 5필드 Cron 파서를 직접 구현해 외부 라이브러리(NCrontab 등) 없이 스케줄링을 처리한다. `CronExpressionEvaluator`는 표준 cron 문법(범위·스텝·리스트·와일드카드·요일 0=7=일요일)을 지원하며, 최대 5년 내 다음 실행 시각을 순방향 탐색으로 계산한다.

3. **사람 언어 → Cron 자동 변환 + 한국어 지원**
   `TryParseCadenceToCron()`이 `"Every day · 02:00"`, `"매일 06:00"`, `"월요일 08:30"` 등 자연어 형식을 5필드 cron으로 변환한다. 입력창에 cron 문법을 직접 입력해도 통과시키므로, 파워 유저와 일반 사용자 모두 지원한다.

4. **프로젝트 자동 해결 및 동적 생성**
   `RunScheduledJob()`은 프로젝트를 ID → 경로 → 임시 생성 → 폴백(ActiveProject/첫 번째 프로젝트) 순으로 해결한다. 프로젝트가 삭제되거나 이동된 경우에도 경로 폴더가 존재하면 임시 `ProjectViewModel`을 생성해 실행을 지속한다.

5. **불변 ViewModel 래퍼 (`ScheduledJobViewModel`)**
   `ScheduledJobViewModel`은 `INotifyPropertyChanged`를 구현하지 않는 단순 래퍼다. 예약 작업이 변경되면 `LoadScheduledJobs()`가 `ScheduledJobs` 컬렉션 전체를 재구성한다. 잦은 업데이트가 없는 정적 목록에 적합하며, 개별 속성 변경 알림의 복잡성을 피한다.

---

## 유지보수 고려사항

- **30초 폴링 정밀도 한계**: `PeriodicTimer(30초)` 기반이므로 실제 실행 시각은 설정 시각 기준 최대 30초 지연될 수 있다. 정밀한 1분 단위 cron이 필요하면 인터벌을 60초 이하로 줄여야 한다.

- **`NextRunLabel` 정적 스냅샷**: `ScheduledJobViewModel`은 `INotifyPropertyChanged`가 없으므로, `NextRunLabel`은 목록을 마지막으로 로드한 시점의 값이다. 뷰가 열려 있는 동안 자동으로 갱신되지 않는다. 실시간 카운트다운이 필요하면 VM에 타이머 갱신 로직을 추가해야 한다.

- **이벤트 트리거 미구현 (`Kind="Event"`)**: `"on push to ..."` 형식은 저장·표시는 되지만 실행되지 않는다. `TimerScheduler`의 `EvaluateJobs`는 `NextRunUtc`가 null인 항목을 건너뛰고, `GetNextRunUtc()`가 이벤트 타입에 대해 null을 반환한다. 구현 시 git hook 또는 파일시스템 감시자와 연동이 필요하다.

- **`NewScheduleCommand` 바인딩 미확인**: XAML에서 `{Binding NewScheduleCommand}`를 사용하지만 `AppViewModel.Scheduling.cs`에는 `OpenNewSchedule()` 메서드만 있고 ICommand 정의가 이 파일에 없다. 다른 AppViewModel 파셜에서 정의된 것으로 추정되며, 파셜 클래스 분산으로 인한 추적 어려움에 주의해야 한다.

- **Job ID 충돌 위험**: `Id = "job" + DateTime.Now.Ticks`는 단일 인스턴스 내 빠른 연속 생성(수백 나노초 내)에서 이론적 충돌 가능성이 있다. 실사용상 거의 발생하지 않지만 GUID로 대체하면 완전히 제거할 수 있다.

- **`ScheduleStore` 저수준 의존**: `TimerScheduler.EvaluateJobs()`가 잠금 내에서 직접 `ScheduleStore.Save()`를 호출한다. 파일 I/O가 느리거나 실패하면 잠금 보유 시간이 늘어나 `Reload()` 호출이 지연될 수 있다.
