# SessionView 분석

> 소스 파일
> - `src/AgentManager/Views/SessionView.xaml`
> - `src/AgentManager/Views/SessionView.xaml.cs`
> - `src/AgentManager/ViewModels/SessionViewModel.cs`
> - `src/AgentManager/ViewModels/Blocks.cs`

---

## 목적

`SessionView`는 AgentManager의 **활성 세션(대화)** 전체를 표시하는 메인 패널이다. 하나의 `UserControl`로 구성되며, 다음 세 가지 영역을 담당한다:

1. **트랜스크립트 영역** — 사용자·어시스턴트·도구 블록이 시간 순으로 렌더링되는 대화 피드
2. **컴포저 영역** — 메시지 입력, 파일 첨부, 모델/추론 강도·샌드박스 설정이 집약된 하단 입력창
3. **헤더 영역** — 탭 바(세션 제목·브랜치·액션 버튼)와 상태 스트립(실행 상태·비용·토큰)

`DataContext`는 `SessionViewModel`(활성 세션)이며, 창 수준의 명령(`SendCommand`, `StopCommand` 등)은 `Window.DataContext`인 `AppViewModel`을 `RelativeSource=AncestorType=Window`로 참조한다.

---

## 레이아웃 구조

```
UserControl (SessionRoot)
└── Grid
    ├── TextBlock          ← 빈 상태 오버레이 (HasActive=False일 때만 표시)
    └── DockPanel          ← 메인 컨텐츠 (HasActive=True일 때만 표시)
        ├── Border [Dock=Top]           ← 탭 바 (44px)
        ├── Border [Dock=Top]           ← 상태 스트립 (38px)
        ├── Border [Dock=Top]           ← 네이티브 워커/서브에이전트 (조건부)
        ├── Border [Dock=Bottom]        ← 컴포저 영역
        │   └── StackPanel
        │       ├── Border              ← 보고 수신함 (조건부)
        │       ├── Border              ← 실행 중 배너 (IsRunning)
        │       ├── ItemsControl        ← 빠른 응답 칩 (QuickReplies)
        │       └── Border              ← 컴포저 박스 (에이전트 브랜드 테두리)
        │           └── DockPanel
        │               ├── ItemsControl [Dock=Bottom]  ← 대기 첨부파일
        │               ├── Border [Dock=Bottom]        ← 하단 툴바
        │               └── Grid                        ← TextBox + 플레이스홀더 + 제안 Popup
        └── ItemsControl (TranscriptList) ← 트랜스크립트 (나머지 공간 전체)
```

### DockPanel 배치 우선 순위
`DockPanel.Dock` 속성에 따라 Top/Bottom 항목이 먼저 공간을 차지하고, 나머지(`LastChildFill` 기본값 `True`)는 `TranscriptList`가 모두 사용한다.

---

## ViewModel 바인딩 상세

### `DataContext` = `SessionViewModel` (활성 세션)

| 바인딩 대상 (XAML 요소) | 속성 | 모드 | 역할 |
|---|---|---|---|
| TabBar `TextBlock` | `Project` | OneWay | 탭 바 좌측 프로젝트 이름 |
| TabBar `TextBlock` | `Title` | OneWay | 탭 바 중앙 세션 제목 (SemiBold, CharacterEllipsis) |
| Branch `TextBlock` | `Branch` | OneWay | 탭 바 우측 브랜치 배지 |
| 상태 스트립 `TextBlock` | `StatusLabel` | OneWay | 현재 상태 한국어 레이블 ("실행 중", "유휴" 등) |
| 실행 배지 `Border` | `IsRunning` | OneWay (BoolVis) | 실행 중 배지 전체 가시성 |
| 실행 배지 테두리 | `IsQuiet` | DataTrigger | `True`이면 `Warn` 색상 — 45초간 출력 없음 경고 |
| 실행 배지 `TextBlock` | `RunningElapsedLabel` | OneWay | 경과 시간 (`m:ss` / `h:mm:ss`) |
| 실행 배지 `TextBlock` | `LastSignalLabel` | OneWay | 마지막 신호 경과 시간 |
| 상태 스트립 Agent `Run` | `AgentName` | OneWay | 에이전트 이름 (Claude Code / Codex 등) |
| 상태 스트립 Model `Run` | `Model` | OneWay | 현재 모델 식별자 |
| 승인 `ToggleButton` | `RequireApproval` | **TwoWay** | 도구 실행 전 사용자 승인 요구 ON/OFF |
| 샌드박스 `ComboBox` | `Sandbox` | **TwoWay** | ReadOnly / WorkspaceWrite / DangerFullAccess 선택 |
| 토큰 `Run` | `TokensLabel` | OneWay | "In / Out" 형식 토큰 사용량 |
| 비용 `Run` | `CostLabel` | OneWay | USD 비용 or "plan" or "—" |
| 네이티브 워커 `Border` | `HasNativeWorkItems` | OneWay (BoolVis) | 서브에이전트 패널 가시성 |
| `ItemsControl` | `NativeWorkItems` | OneWay | 서브에이전트 카드 목록 |
| 빠른 응답 `ItemsControl` | `QuickReplies` | OneWay | 어시스턴트 A/B·1/2 선택지 칩 |
| 빠른 응답 `ItemsControl` | `QuickReplies.Count` | DataTrigger (Collapsed when 0) | 항목 없을 때 패널 숨김 |
| 실행 중 배너 `Border` | `IsRunning` | OneWay (BoolVis) | 실행 배너 가시성 |
| 실행 중 배너 `TextBlock` | `BusyLine` | OneWay | 현재 활동 설명 (조용할 때 경고 포함) |
| 컴포저 테두리 | `AgentId` | DataTrigger | `cc`/`gx`/`agy`별 브랜드 색상 테두리 |
| 엔진 배지 `TextBlock` | `Badge` | OneWay | 엔진 단축 식별자 (예: "CC", "GX") |
| 엔진 배지 색상 | `AgentId` | DataTrigger | 브랜드 색상 적용 |
| 브랜치 알약 `Run` | `BranchTail` | OneWay | 브랜치 마지막 세그먼트 (예: `foo-bar`) |
| 브랜치 알약 `ToolTip` | `Branch` | OneWay | 전체 브랜치 이름 툴팁 |
| 모델 메뉴 `TextBlock` | `Model` | OneWay | 컴포저 하단 모델 이름 표시 |
| 모델 팝업 `ItemsControl` | `AvailableModels` | OneWay | 엔진이 지원하는 모델 목록 |
| 추론 강도 `Grid` | `HasEffort` | OneWay (BoolVis) | `agy`(Antigravity)는 숨김 |
| 추론 강도 `TextBlock` | `ReasoningEffort` | OneWay | 현재 강도 레이블 ("default", "medium" 등) |
| 추론 강도 팝업 `ItemsControl` | `EffortOptions` | OneWay | 엔진별 강도 옵션 목록 |
| 번역 `ToggleButton` | `TranslationEnabled` | **TwoWay** | 번역 ON/OFF |
| 번역 `ToggleButton` `Content` | `TranslationLabel` | OneWay | "번역 ON" / "번역 OFF" 텍스트 |
| 컴포저 `TextBox` | `Draft` | **TwoWay** (PropertyChanged) | 작성 중인 메시지 내용 |
| 플레이스홀더 `TextBlock` | `Draft` | DataTrigger (Visible when "") | 빈 입력창 힌트 |
| 플레이스홀더 `Run` | `Cli` | OneWay | 힌트에 CLI 이름 삽입 |
| 하단 힌트 `Run` | `Cli` / `Branch` | OneWay | "Enter로 전송 · CLI · 브랜치" 텍스트 |
| 대기 첨부파일 `ItemsControl` | `PendingAttachments` | OneWay | 첨부 대기 이미지·문서 칩 |
| 트랜스크립트 `ItemsControl` | `Transcript` | OneWay | 트랜스크립트 블록 목록 |

### `Window.DataContext` = `AppViewModel` (RelativeSource AncestorType=Window)

| 바인딩 대상 | 속성 | 역할 |
|---|---|---|
| 빈 상태 / 메인 패널 | `HasActive` | 세션 없을 때 빈 상태 표시, 세션 있을 때 메인 패널 표시 |
| 리뷰 토글 버튼 | `ToggleReviewCommand` | 우측 리뷰 패널 열기/닫기 |
| 리뷰 버튼 스타일 | `IsReviewOpen` | `True`이면 `Accent` 색상으로 활성 표시 |
| IDE 열기 버튼 | `OpenIdeCommand` | 현재 프로젝트를 IDE에서 열기 |
| 보고 수신함 | `HasReadyReportsActive` | 위임 워커 보고 수신함 가시성 |
| 병합 버튼 | `CanMergeReports` | 병합 가능 여부 |
| 병합 버튼 | `MergeReportsCommand` | 보고서 일괄 붙여넣기 명령 |
| 수신함 카운트 | `ReadyReportsActiveCount` | "N READY" 표시 |
| Ollama 경고 아이콘 | `OllamaDown` | Ollama 서버 다운 시 경고 아이콘 노출 |
| Ollama 경고 클릭 | `ShowSettingsCommand` | 설정 화면으로 이동 |
| 제안 팝업 | `IsComposerSuggestionOpen` | `@`/`/` 트리거 제안 팝업 열림 여부 |
| 제안 팝업 헤더 | `ComposerSuggestionHeader` | 팝업 상단 범주 텍스트 |
| 제안 목록 | `ComposerSuggestions` | `@` 세션 / `/` 명령 제안 항목 |
| 제안 선택 | `SelectedComposerSuggestion` | 현재 선택된 제안 항목 |
| 전송 버튼 | `SendCommand` | 메시지 전송 |
| 중지 버튼 | `StopCommand` | 실행 중단 |
| 빠른 응답 클릭 | `QuickReplyCommand` | 클릭된 선택지 자동 전송 |

### `NativeWorkItemViewModel` (NativeWorkItems 각 항목)

| 속성 | 역할 |
|---|---|
| `Title` | 서브에이전트 이름 |
| `Status` | 상태 텍스트 (Mono 폰트) |
| `Source` | 출처 식별자 |
| `Detail` | 상세 설명 |
| `IsRunning` | 실행 중 (Accent 색상 테두리 + 점) |
| `IsCompleted` | 완료 (Ok 색상) |
| `IsFailed` | 실패 (Err 색상) |

---

## 커스텀 컨트롤 사용

| 컨트롤 | 사용 위치 | 역할 |
|---|---|---|
| `controls:IconView` | 탭 바, 컴포저 툴바, 버튼 내부 전반 | SVG 아이콘 렌더러. `Icon`(정적 리소스), `Foreground`, `Filled`, `Width`, `Height` 속성 사용 |
| `controls:Spinner` | 상태 스트립 실행 중 배지 | 애니메이션 회전 스피너 — CSS/타이머 없이 WPF 네이티브 구현 추정 |
| `controls:MouseClick.Command` | Ollama 경고 아이콘 | `Button`이 아닌 `IconView`에 클릭 명령을 연결하는 첨부 속성 |

---

## 애니메이션 & 트리거

### 데이터 트리거 (DataTrigger)

| 조건 | 효과 |
|---|---|
| `HasActive = False` | 빈 상태 TextBlock 표시, 메인 DockPanel 숨김 |
| `IsRunning = True` | 실행 중 배지 표시, 실행 배너 표시, 중지 버튼 표시 |
| `IsQuiet = True` | 실행 배지·배너 테두리를 `AccentLine` → `Warn`으로 전환 |
| `AgentId = "cc"` | 컴포저 테두리 `CcBrandLine`, 엔진 배지 배경 `CcBrandDim`, 텍스트 `CcBrand` |
| `AgentId = "gx"` | 컴포저 테두리 `GxBrandLine`, 배경 `GxBrandDim`, 텍스트 `GxBrand` |
| `AgentId = "agy"` | 컴포저 테두리 `AgyGradient`, 배경 `AgyBrandDim`, 텍스트 `AgyBrand` |
| `AgentId = "pi"` | 텍스트 `PiBrand` (테두리는 기본값 유지) |
| `IsRunning = True` (NativeWorkItem) | 카드 테두리 `AccentLine`, 상태 점 `Accent` |
| `IsCompleted = True` | 카드 테두리 `Ok`, 상태 점 `Ok` |
| `IsFailed = True` | 카드 테두리 `Err`, 상태 점 `Err` |
| `QuickReplies.Count = 0` | 빠른 응답 패널 `Collapsed` |
| `Draft = ""` | 컴포저 플레이스홀더 표시 |
| 제안 유형 `Session` | 아이콘 "⚡", 색상 `Accent` |
| 제안 유형 `Command` | 아이콘 "⚙️", 색상 `Info` |

### 속성 트리거 (Trigger)

| 조건 | 효과 |
|---|---|
| 리뷰 버튼 `IsMouseOver = True` | `Foreground` → `Txt0`, `BorderBrush` → `LineBright` |
| 모델/추론 메뉴 아이템 `IsMouseOver = True` | 배경 → `Bg4` |
| 제안 목록 `IsMouseOver = True` | 배경 → `Bg3` |
| 제안 목록 `IsSelected = True` | 배경 → `AccentDim`, 우측 3px `AccentLine` 테두리 |
| 중지 버튼 `IsMouseOver = True` | `Opacity` → 0.85 |

### Popup 애니메이션

| 팝업 | 트리거 | 애니메이션 |
|---|---|---|
| 모델 선택 (`ModelMenuBtn`) | `ToggleButton.IsChecked` (TwoWay) | `PopupAnimation="Fade"` + `DropShadowEffect(BlurRadius=20)` |
| 추론 강도 (`EffortMenuBtn`) | `ToggleButton.IsChecked` (TwoWay) | `PopupAnimation="Fade"` + `DropShadowEffect(BlurRadius=20)` |
| 컴포저 제안 (`ComposerSuggestionPopup`) | `AppViewModel.IsComposerSuggestionOpen` | `PopupAnimation="Fade"` + `DropShadowEffect(BlurRadius=15)` |

---

## Code-Behind vs XAML 역할 분담

### XAML이 처리하는 것
- 전체 레이아웃 구조와 시각 계층
- 데이터 바인딩 및 트리거를 통한 상태 반영
- 브랜드 색상 분기 (에이전트 ID별 스타일 트리거)

### Code-Behind(`SessionView.xaml.cs`)가 처리하는 것

#### 트랜스크립트 자동 스크롤
- `DataContextChanged` → 이전 세션의 `CollectionChanged` 구독 해제 후 신규 세션 구독
- `Transcript_Changed`: 사용자가 하단 80px 이내에 있을 때만 `ScrollToEnd()` — 읽는 중 강제 스크롤 방지
- `TranscriptScroll_PreviewMouseWheel`: 내부 스크롤러(마크다운 FlowDocument, 읽기전용 TextBox 등)가 끝에 닿으면 바깥 `TranscriptList` 스크롤러에 휠 이벤트를 전달

#### 트랜스크립트 내보내기
- `ExportTranscript_Click`: `SaveFileDialog` → `TranscriptExporter.ToMarkdown()` → 파일 저장
- `CopyTranscript_Click`: 동일 변환 후 클립보드 복사

#### 컴포저 입력 처리
- `Composer_PreviewKeyDown`:
  - 제안 팝업 열려 있을 때: `↑/↓` 선택 이동, `Enter/Tab` 적용, `Escape` 닫기
  - `Enter` (Shift 없음): `SendCommand.Execute()` — `PreviewKeyDown`을 사용하는 이유는 `AcceptsReturn` TextBox가 `KeyDown`을 먼저 가로채기 때문
  - `Ctrl+V` + 클립보드에 이미지: `PasteClipboardImage()` 호출
- `ComposerBox_TextChanged`: `AppViewModel.UpdateComposerSuggestion(text, caretIndex)` 호출 → `@`/`/` 제안 트리거
- `ApplySuggestionToComposer`: VM에서 `Draft` 업데이트 후 TextBox 캐럿 위치 복원

#### 받아쓰기 (Dictate)
- `Dictate_Click`: `keybd_event` P/Invoke로 `Win+H` 시뮬레이션 → OS 내장 STT(음성 받아쓰기) 실행

#### 파일 첨부
- `AttachImage_Click`: `OpenFileDialog(Multiselect)` → `PendingAttachments.Add()` (이미지 여부 자동 판별)
- `RemovePendingImage_Click`: `DataContext`에서 `PendingAttachment` 제거
- `PasteClipboardImage()`: `Clipboard.GetImage()` → `ImageAttachmentStore.SavePng()` → `PendingAttachments.Add()`

#### 팝업 닫기
- `ModelOption_Click`: 세션 `Model` 갱신 후 `ModelMenuBtn.IsChecked = false`로 팝업 닫기
- `EffortOption_Click`: 세션 `ReasoningEffort` 갱신 후 `EffortMenuBtn.IsChecked = false`

---

## `Blocks.cs` — 트랜스크립트 아이템 타입

트랜스크립트는 `ObservableCollection<TranscriptItem>`이며, 각 타입은 XAML `DataTemplate`으로 렌더링된다.

| 클래스 | 역할 | 주요 속성 |
|---|---|---|
| `UserBlock` | 사용자 메시지 | `Text`, `SentText`(번역 후 전송본), `ShowSent`(원문/번역 토글), `DisplayText` |
| `AgentTextBlock` | 어시스턴트 마크다운 응답 | `Text`, `OriginalText`(번역 전 원문), `ShowOriginal`, `ModelUsed`, `IsRetranslating` |
| `ToolBlock` | 도구 사용 카드 (READ/EDIT/RUN) | `Kind`, `Name`, `Stat`, `Body`, `IsOpen`(접기/펼치기), `CommandText` |
| `ErrorBlock` | 오류 메시지 | `Title`, `Body` |
| `ThinkingBlock` | Claude 추론(thinking) 블록 | `Text`, `IsOpen`(기본 접힘) |
| `ApprovalBlock` | 도구 실행 승인 요청 | `ToolName`, `InputSummary`, `State`(pending/allowed/denied/expired), `IsPending`, `SupportsSessionApproval` |
| `WorkingBlock` | 처리 중 임시 텍스트 | `Text` (업데이트 가능) |
| `DelegationBlock` | 워커 위임 인라인 카드 | `Delegation` (`WorkerDelegationViewModel` 참조 — 상태 라이브 업데이트) |

### 번역 토글 패턴 (`UserBlock` / `AgentTextBlock` / `ToolBlock`)
세 클래스 모두 동일한 패턴: `원본 Text` ↔ `번역본 Text`를 `ShowOriginal`/`ShowSent` 불리언으로 전환하며, `DisplayText`가 현재 표시 내용을 반환한다.

---

## 핵심 특징 (Key Highlights)

1. **이중 DataContext 구조**
   세션 데이터(`SessionViewModel`)는 `DataContext`로, 앱 수준 명령(`AppViewModel`)은 `RelativeSource AncestorType=Window`로 분리 접근한다. 이를 통해 세션이 교체되어도 앱 레벨 버튼들은 바인딩을 유지한다.

2. **가상화 트랜스크립트**
   `TranscriptList`는 `VirtualizingStackPanel` + `VirtualizationMode=Recycling` + `ScrollUnit=Pixel`을 사용해 수백 개의 블록이 있어도 화면에 보이는 항목만 생성한다. `ScrollViewer`가 `ItemsControl.Template` 내부에 배치된 이유도 이 가상화를 작동시키기 위함이다.

3. **에이전트 브랜드 색상 시스템**
   `AgentId`(`cc`/`gx`/`agy`/`pi`) 하나로 컴포저 테두리·배경·텍스트·엔진 아이콘 색상이 모두 전환된다. 신규 에이전트 추가 시 동적 리소스 키(`XxxBrand`, `XxxBrandDim`, `XxxBrandLine`)만 정의하면 된다.

4. **스마트 자동 스크롤**
   새 메시지가 추가될 때 사용자가 이미 하단 80px 이내에 있을 때만 자동 스크롤한다. 위로 스크롤해 읽는 중에는 강제로 끌려 내려가지 않는다.

5. **중첩 휠 스크롤 처리**
   마크다운 FlowDocument, 코드 블록 읽기 전용 TextBox 등 내부 스크롤러가 끝에 닿으면 `PreviewMouseWheel`에서 이를 감지하고 바깥 트랜스크립트 스크롤러에 휠을 위임한다.

---

## 유지보수 고려사항

- **새 에이전트 추가 시**: `AgentId` 분기 DataTrigger가 XAML 전반에 분산되어 있으므로(`cc`/`gx`/`agy`/`pi` 총 4곳 이상), 신규 ID 추가 시 컴포저 테두리, 엔진 배지, 모델 텍스트, 추론 강도 텍스트의 트리거를 모두 추가해야 한다.
- **`PART_TranscriptScroll` 이름 의존**: `ScrollViewer`를 코드-비하인드에서 `Template.FindName()`으로 찾는다. 템플릿 이름을 바꾸면 자동 스크롤이 무음 실패한다.
- **`AppViewModel` 캐스팅 패턴**: `Vm` 프로퍼티가 `Window.GetWindow(this)?.DataContext as AppViewModel`로 매번 캐스팅한다. `Window`가 없는 디자인 타임이나 테스트 환경에서 null이 된다.
- **`SandboxModes` 정적 리소스**: `ComboBox`가 `StaticResource SandboxModes`를 소스로 사용한다. 이 리소스가 App.xaml 등에 없으면 런타임 오류가 발생한다.
- **`QuickReplies` 단방향 생명 주기**: 어시스턴트 턴 완료 시 채워지고 새 턴 시작 시 지워진다. 지속되지 않으므로 재시작 후 이전 선택지는 복원되지 않는다.
- **Win+H 받아쓰기 P/Invoke**: `keybd_event`는 UAC 권한 상승 없이 동작하나, 일부 기업 정책(그룹 정책으로 Win+H 차단)에서 무음 실패할 수 있다.
