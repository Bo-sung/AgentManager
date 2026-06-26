# HistoryView 분석

> **파일 경로**
> - `src/AgentManager/Views/HistoryView.xaml`
> - `src/AgentManager/Views/HistoryView.xaml.cs`
> - `src/AgentManager/ViewModels/AppViewModel.History.cs`
> - `src/AgentManager/ViewModels/HistoryRowViewModel.cs`

---

## 1. 목적

`HistoryView`는 AgentManager의 **활동 내역(Activity History) 중앙 패널**이다. 현재 앱에 등록된 모든 세션(`_allSessions`)을 시작 시각 역순으로 나열하고, 상태·에이전트 종류·텍스트의 세 가지 필터를 실시간으로 조합해 원하는 세션을 빠르게 찾을 수 있게 한다.

주요 역할:
- 전체 세션 목록을 **가상화된 리스트(VirtualizingStackPanel)** 로 표시해 수백 개의 행을 부드럽게 스크롤
- 상태(running / waiting / done / error) · 에이전트(Claude Code / Codex / Antigravity) · 자유 텍스트 검색의 **3단 필터링**
- 행 클릭 시 해당 세션을 `ActiveSession`으로 설정하고 Session 뷰로 이동
- 헤더와 하단 바에서 세션 수·필터 결과 수를 **요약 텍스트**로 실시간 표시

---

## 2. 레이아웃 구조

```
UserControl (HistoryView)
└── DockPanel (LastChildFill)
    ├── [Dock=Top] 헤더 바 (Height=44)
    │   └── Grid (8열 정의)
    │       ├── Col 0: TextBlock "활동 내역" 제목
    │       ├── Col 1: TextBlock HistorySummaryText (세션/프로젝트 수)
    │       ├── Col 2: (스페이서 *)
    │       ├── Col 3 (W=120): ComboBox 상태 필터
    │       ├── Col 4 (W=8): 간격
    │       ├── Col 5 (W=140): ComboBox 에이전트 필터
    │       ├── Col 6 (W=8): 간격
    │       └── Col 7 (W=220): 검색창 Border
    │           └── Grid
    │               ├── IconView (IconSearch)
    │               └── TextBox (HistoryFilterText)
    ├── [Dock=Bottom] 하단 상태 바 (Height=36)
    │   └── DockPanel
    │       ├── TextBlock HistoryFilterSummaryText
    │       └── Button "새로고침" (RefreshHistoryCommand)
    └── ListView (HistoryRows, 나머지 전체)
        └── DataTemplate (행 아이템)
            └── Border (CornerRadius=6, Cursor=Hand)
                └── Grid (5열)
                    ├── Col 0 (W=20): LED 상태 점 (Outer Glow + Inner Dot)
                    ├── Col 1 (W=34): 엔진 뱃지 Border + IconView
                    ├── Col 2 (*): 제목 + [아카이브 뱃지] + Project/Branch
                    ├── Col 3 (SharedSizeGroup=TimeCol): 시작 시각 + 블록 수
                    └── Col 4 (SharedSizeGroup=TokensCol): 토큰 수 + 비용
```

### ListView 가상화 설정

| 속성 | 값 | 의미 |
|---|---|---|
| `VirtualizingPanel.IsVirtualizing` | `True` | 화면 밖 행은 UI 요소를 생성하지 않음 |
| `VirtualizingPanel.VirtualizationMode` | `Recycling` | 스크롤 시 컨테이너를 재사용(Recycle)해 GC 압력 최소화 |
| `ScrollViewer.CanContentScroll` | `True` | 픽셀 단위가 아닌 아이템 단위 스크롤 (가상화 필수 조건) |
| `ItemsPanel` | `VirtualizingStackPanel` | 위 설정들과 조합되는 패널 |

### ListViewItem 커스텀 템플릿

기본 `ListViewItem`의 선택·호버 하이라이트를 제거하고 `ContentPresenter`만 남겨, 행 배경·호버 효과를 데이터 아이템 내부 `Border.Style.Triggers`에서 직접 제어한다. `Focusable="False"`로 키보드 탐색도 비활성화되어 순수 마우스 클릭 UI로 동작한다.

---

## 3. ViewModel 바인딩

DataContext는 부모가 주입하는 `AppViewModel` 인스턴스이다.

### 3-1. 헤더 바 바인딩

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `HistorySummaryText` | `AppViewModel.History.cs:54` | `RebuildHistoryRows()`에서 갱신. 예: `"42 sessions · 3 projects"` |
| `HistoryStatusFilter` | `AppViewModel.History.cs:32` | `TwoWay`; `ComboBox.SelectedValue` (`Tag`: `"all"/"active"/"waiting"/"done"/"error"`). 변경 시 `ApplyHistoryFilters()` 즉시 호출 |
| `HistoryAgentFilter` | `AppViewModel.History.cs:31` | `TwoWay`; 에이전트 ComboBox (`Tag`: `"all"/"cc"/"gx"/"agy"`). 변경 시 `ApplyHistoryFilters()` 즉시 호출 |
| `HistoryFilterText` | `AppViewModel.History.cs:20` | `UpdateSourceTrigger=PropertyChanged`로 키 입력마다 필터 즉시 적용 |

### 3-2. 하단 바 바인딩

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `HistoryFilterSummaryText` | `AppViewModel.History.cs:57` | 현재 필터 결과 수. 검색 중이면 `"N개 표시(필터 적용 후)"` 형식, 아니면 `"N개 표시"` |
| `RefreshHistoryCommand` | `AppViewModel.cs:149` | `RebuildHistoryRows()` 재호출; 세션 목록을 소스에서 다시 구축 |

### 3-3. ListView 바인딩

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `HistoryRows` | `AppViewModel.cs:53` | `ObservableCollection<HistoryRowViewModel>`; `ApplyHistoryFilters()`가 채움 |

### 3-4. 행 아이템(`HistoryRowViewModel`) 바인딩

`HistoryRowViewModel`은 **불변(immutable) 스냅샷** 객체다. `INotifyPropertyChanged`를 구현하지 않으며, 세션 상태가 바뀌면 `RebuildHistoryRows()`를 통해 컬렉션이 통째로 재구성된다.

| 바인딩 경로 | 타입 | 설명 |
|---|---|---|
| `Status` | `string` | `"running"/"waiting"/"done"/"error"`; LED Glow·Dot `Fill` DataTrigger 기준값 |
| `StatusLabel` | `string` | 상태 툴팁 문자열 (예: `"실행 중"`) |
| `AgentName` | `string` | 엔진 뱃지 `ToolTip` |
| `IsArchived` | `bool` | 행 `Opacity=0.6` DataTrigger; 아카이브 뱃지 `Visibility` |
| `Title` | `string` | 세션 제목 (굵은 글씨); 빈 경우 `"(제목 없음)"` |
| `Project` | `string` | 프로젝트명; 빈 경우 `"(프로젝트 없음)"` |
| `Branch` | `string` | git 브랜치명; 빈 경우 `"(브랜치 없음)"` |
| `StartedText` | `string` | `"yyyy-MM-dd HH:mm"` 포맷 시작 시각 |
| `BlocksText` | `string` | `"N 블록"` 형식 트랜스크립트 블록 수 |
| `TokensText` | `string` | `"in/out"` 형식 토큰 수 (숫자 구분자 포함, 예: `"1,234/5,678"`) |
| `CostText` | `string` | `"$0.000"` 형식 비용; 0이면 `"-"`. `"-"`일 때 `Foreground`를 `Txt2`로 전환하는 `Trigger` 있음 |

#### CostText Trigger

```xml
<Trigger Property="Text" Value="-">
    <Setter Property="Foreground" Value="{DynamicResource Txt2}"/>
</Trigger>
```
비용이 없는 세션은 비용 TextBlock을 흐린 색(`Txt2`)으로 표시하고, 비용이 있으면 강조색(`Ok`, 녹색)으로 표시한다. 이 Trigger는 **값이 없음**을 의미하는 `"-"` 문자열을 센티넬 값으로 활용하는 패턴이다.

### 3-5. 커맨드 바인딩

| 커맨드 | 소스 파일 | 동작 |
|---|---|---|
| `OpenHistoryRowCommand` | `AppViewModel.NavCommands.cs:78` | `HistoryRowViewModel`을 받아 `OpenHistoryRow()` 호출 → `ActiveSession` 설정 후 Session 뷰로 이동 |
| `RefreshHistoryCommand` | `AppViewModel.cs:149` | `RebuildHistoryRows()` 재실행 |

`OpenHistoryRowCommand`는 행 `Border`에 첨부 속성(`controls:MouseClick.Command`)으로 바인딩된다. `RelativeSource AncestorType=UserControl`로 ViewModel을 탐색한다.

---

## 4. 사용된 커스텀 컨트롤

### `controls:MouseClick` (첨부 속성)

```csharp
// Controls/MouseClick.cs:9
// Usage: controls:MouseClick.Command="{Binding ...}"
//        controls:MouseClick.CommandParameter="{Binding}"
public static class MouseClick { ... }
```

- `MouseButtonEventHandler`를 `UIElement.MouseLeftButtonUp`에 등록하는 첨부 속성 쌍이다.
- `ListViewItem`이 `Focusable="False"`이고 커스텀 `ControlTemplate`이라 기본 `Button.Click` 이벤트를 사용할 수 없기 때문에 도입된 패턴이다.
- `Command`와 `CommandParameter` 두 속성을 제공하며, `ICommand.CanExecute`도 검사한다.

### `controls:IconView`

- 헤더 검색 아이콘(`IconSearch`)과 각 행의 엔진 뱃지 아이콘(`EngineIcon` StaticStyle)에 사용.
- `EngineIcon` 스타일은 DataContext(`HistoryRowViewModel.AgentId`)에 따라 브랜드 아이콘을 자동 선택한다.

### 스타일 / 리소스 키

| 키 | 종류 | 용도 |
|---|---|---|
| `ComposerInput` | Style | 테두리 없는 TextBox 스타일 (검색창 입력 필드) |
| `ChipButton` | Style | 작은 칩 형태 버튼 ("새로고침") |
| `BoolVis` | Converter | `bool → Visibility` 변환 (아카이브 뱃지) |
| `Lbl` | Style | 헤더 레이블 TextBlock 스타일 |
| `Mono` | FontFamily | 고정폭 폰트 (메타데이터 열) |

---

## 5. 애니메이션 & 트리거

### 5-1. LED 상태 점 (Outer Glow + Inner Dot)

행 맨 왼쪽의 상태 표시기는 두 개의 `Ellipse`로 구성된다.

| 요소 | 크기 | Opacity | 역할 |
|---|---|---|---|
| Outer Glow Ring | 12×12 | 0.15 | 번지는 발광 효과 |
| Inner Solid Dot | 6×6 | 1.0 | 실제 상태 색상 점 |

양쪽 모두 `Status` 값에 따라 `Fill`을 전환하는 `DataTrigger` 4개를 공유한다:

| Status | Fill 리소스 |
|---|---|
| `"running"` | `Accent` (파랑 계열) |
| `"waiting"` | `Warn` (노랑 계열) |
| `"done"` | `Ok` (녹색) |
| `"error"` | `Err` (빨강) |
| 기본값 | `Txt3` (회색) |

### 5-2. 행 호버 효과

`Border.Style.Triggers`의 `Trigger Property="IsMouseOver"`:

```
IsMouseOver=True → Background: Bg1 → Bg3, BorderBrush: LineSoft → Line
```

커스텀 `ListViewItem` 템플릿을 사용하기 때문에 기본 선택 하이라이트가 없고, 이 `Trigger`가 유일한 인터랙션 피드백이다.

### 5-3. 아카이브 행 투명도

```
DataTrigger: IsArchived=True → Border.Opacity = 0.6
```

아카이브된 세션은 목록에서 흐리게 처리되어 활성 세션과 시각적으로 구분된다.

### 5-4. 비용 텍스트 색상

```
Trigger: Text="-" → Foreground = Txt2 (흐린 색)
기본값             → Foreground = Ok  (녹색 강조)
```

애니메이션은 없으며, 정적 Trigger만 사용한다.

---

## 6. 코드 비하인드 vs. XAML 역할 분리

### 코드 비하인드 (`HistoryView.xaml.cs`)

```csharp
public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();
}
```

`OrchestratorView`와 동일하게 **생성자 한 줄**뿐이다. 필터링·갱신·행 열기 로직이 전부 ViewModel에 위임된다.

| 항목 | 위치 |
|---|---|
| 필터 적용 로직 | `AppViewModel.History.cs:ApplyHistoryFilters()` |
| 데이터 소스 구축 | `AppViewModel.History.cs:RebuildHistoryRows()` |
| 행 클릭 처리 | `AppViewModel.NavCommands.cs:OpenHistoryRowCommand` |
| 새로고침 | `AppViewModel.cs:RefreshHistoryCommand` |
| 행 클릭 이벤트 연결 | XAML `controls:MouseClick` 첨부 속성 |
| 상태별 색상 | XAML `DataTrigger` |
| 호버 효과 | XAML `Style.Triggers` |

MVVM 패턴이 엄격히 유지되며, View는 선언적 XAML만으로 구성된다.

---

## 7. 핵심 특징 (Key Highlights)

1. **`HistoryRowViewModel`은 불변 스냅샷 — 부분 업데이트 없음**
   `INotifyPropertyChanged`를 구현하지 않는 단순 레코드 스타일 객체다. 세션 상태가 변경되면 `RebuildHistoryRows()`가 `_historySource`를 통째로 재구성하고 `ApplyHistoryFilters()`가 `HistoryRows`를 재채운다. 이 단순성 덕분에 필터 파이프라인이 명확하지만, 수백 개의 행이 있는 경우 세션 하나의 변경으로도 전체 컬렉션이 교체된다. VirtualizingStackPanel이 이 비용을 일부 흡수한다.

2. **3단 필터가 모두 즉시 반응**
   - 텍스트 검색: `UpdateSourceTrigger=PropertyChanged`로 키 입력마다 즉시 적용
   - 상태 필터: `ComboBox SelectedValue TwoWay`로 선택 즉시 `ApplyHistoryFilters()` 호출
   - 에이전트 필터: 동일 패턴
   세 필터는 `AND` 조건으로 중첩 적용(`MatchesHistoryFilters`)되며, 필터 변경 시 `HistoryFilterSummaryText`도 자동 갱신된다.

3. **`controls:MouseClick` 첨부 속성 — ListViewItem 우회**
   `ListViewItem`의 기본 선택 동작(배경 변경, 키보드 포커스)을 완전히 제거하고 `ControlTemplate`을 `ContentPresenter` 하나로 교체했다. 그 결과 일반 `Button.Click`을 쓸 수 없으므로, `MouseLeftButtonUp`을 커맨드로 연결하는 전용 첨부 속성을 사용한다. 이 패턴은 ListView를 순수 스크롤 가능 목록으로만 사용하고 클릭 동작은 첨부 속성으로 완전히 분리한다.

4. **`SharedSizeGroup`으로 열 너비 통일**
   `Grid.IsSharedSizeScope="True"`가 ListView에 설정되어 있고, Col 3(`TimeCol`)과 Col 4(`TokensCol`)가 `SharedSizeGroup`을 사용한다. 모든 행의 시각 및 토큰 열이 가장 넓은 내용에 맞춰 자동으로 정렬되어, 데이터가 다른 행들 사이에서도 컬럼이 일렬로 정렬된다.

5. **두 가지 팩토리 메서드 (`FromSession` / `FromDto`)**
   `HistoryRowViewModel`은 메모리 내 `SessionViewModel`에서 만드는 `FromSession`과, 디스크에서 역직렬화한 `SessionDto`에서 만드는 `FromDto` 두 경로를 제공한다. 렌더링 코드는 단일 ViewModel 타입만 처리하면 되고, 데이터 출처(라이브 vs. 저장된 기록)는 팩토리 계층에서 추상화된다.

---

## 8. 유지보수 시 주의 사항

- **`HistoryRows` 전체 교체 비용**: `RebuildHistoryRows()`는 모든 세션을 반복하며 새 ViewModel을 생성한다. 세션이 수백 개 이상으로 늘어나면 세션 상태 변경마다(예: 타이머 틱) 전체 재구성이 성능 병목이 될 수 있다. 필요 시 `SessionViewModel` 변경 사항을 `HistoryRowViewModel`에 부분 갱신하거나, `RebuildHistoryRows()` 호출 조건을 강화해야 한다.
- **`controls:MouseClick` 첨부 속성 범위**: `RelativeSource AncestorType=UserControl`로 커맨드를 찾으므로, 이 DataTemplate을 다른 `UserControl`에 재사용하면 커맨드 바인딩 경로를 재검토해야 한다.
- **`Text="-"` 센티넬 Trigger**: `CostText`가 `"-"`이면 색상이 바뀐다. 다국어 환경에서 `"-"` 이외의 문자열을 빈 비용 표현으로 사용하면 Trigger가 발동하지 않아 녹색이 잘못 표시될 수 있다. `CostText` 결정 로직(`session.CostUsd > 0 ? ... : "-"`)과 이 Trigger는 항상 함께 변경해야 한다.
- **`SharedSizeGroup` 스코프**: `Grid.IsSharedSizeScope`가 `ListView`에 설정되어 있다. ListView 외부의 Grid와 크기를 공유하지 않으므로, 헤더에 동일 컬럼 정렬이 필요하다면 `SharedSizeGroup`을 상위 스코프로 올리거나 별도의 헤더 Grid를 추가해야 한다.
- **에이전트 필터 하드코딩**: `ComboBoxItem Tag="cc"/"gx"/"agy"` 값과 `MatchesHistoryFilters`의 `row.AgentId != HistoryAgentFilter` 비교가 암묵적으로 연결되어 있다. 새 에이전트 타입이 추가될 때 XAML ComboBox와 `MatchesHistoryFilters` 로직을 모두 업데이트해야 한다.
