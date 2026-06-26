# SettingsView 분석

> **파일 경로**
> - `src/AgentManager/Views/SettingsView.xaml`
> - `src/AgentManager/Views/SettingsView.xaml.cs`
> - `src/AgentManager/ViewModels/AppViewModel.Settings.cs`
> - `src/AgentManager/Persistence/SettingsStore.cs`

---

## 1. 목적

`SettingsView`는 AgentManager의 **전역 설정 패널**로, 7개 섹션(Runtimes · Translation · Orchestration · Permissions · Appearance · Project · Skills)을 한 화면에 담는다. 모든 설정값은 `Settings*` 접두사의 미러(staging) 프로퍼티에 임시 저장되며, **저장(Save)** 시에만 실제 필드(`_claudePath` 등)에 반영된다. 테마와 강조색만 예외적으로 라이브 미리보기가 적용되고 취소(Cancel) 시 이전 값으로 복원된다.

설정 영속화는 `SettingsStore`가 `%LOCALAPPDATA%\AgentManager\settings.json`에 인덴트 JSON으로 기록한다. VS Code식 손편집 지원이 설계 의도이며, 세션 데이터가 담긴 `state.json`과 분리되어 있다.

---

## 2. 레이아웃 구조

```
UserControl (SettingsView, x:Name="SettingsRoot")
├── UserControl.Resources
│   └── DataTemplate DataType=ModelChecklistVm  ← "주로 쓰는 모델" 체크리스트 공용 템플릿
└── DockPanel (LastChildFill)
    ├── [Dock=Top]    헤더 바 (Height=44)
    │   ├── IconView (IconSettings) + TextBlock 제목
    │   └── Button ✕ (CancelSettingsCommand)
    ├── [Dock=Bottom] 푸터 바
    │   ├── Button "settings.json 열기" (OpenSettingsFileCommand)
    │   ├── TextBlock SettingsStatus
    │   ├── Button "취소" (CancelSettingsCommand)
    │   └── Button "저장" (SaveSettingsCommand, AccentButton)
    └── Grid (나머지 전체)
        ├── Col 0 (W=168): TOC StackPanel (섹션 링크 7개)
        └── Col 1 (*):     ScrollViewer (x:Name="SettingsScroll")
            └── StackPanel (MaxWidth=720)
                ├── SecRuntimes   (01) — Claude Code / Codex / Antigravity / Pi + 사용량 카드
                ├── SecTranslation (02) — UI 언어 · 번역 쌍 · Ollama 엔드포인트/모델
                ├── SecOrchestration (03) — 동시 실행 제한 · worktree 경로 · 워커 동작 규칙
                ├── SecPermissions  (04) — 승인 정책 (ask/safe/yolo) · 텔레메트리
                ├── SecAppearance   (05) — 테마 · 강조색 스와치 · UI 줌
                ├── SecProject      (06) — MCP 설정 경로 · 추가 폴더
                └── SecSkills       (07) — 스킬 내용(SKILL.md) · 엔진별 스킬 디렉터리
```

### TOC 동작

왼쪽 7개 `TextBlock`은 각각 `Tag` 속성에 대응 `StackPanel`의 `x:Name` 문자열을 보유한다. 코드 비하인드 `SettingsToc_Click`이 `FindName(tag)`으로 요소를 찾아 `BringIntoView()`를 호출하는 **코드 비하인드 유일 내비게이션 패턴**이다.

---

## 3. ViewModel 바인딩

DataContext는 `AppViewModel` 인스턴스(부모 주입).

### 3-1. 헤더 / 푸터 공통

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `CancelSettingsCommand` | `AppViewModel.NavCommands.cs` | 편집 취소; 테마/강조색 원복 후 이전 뷰로 이동 |
| `SaveSettingsCommand` | `AppViewModel.NavCommands.cs` | `SaveSettings()` 호출 → 모든 설정을 실제 필드에 반영 후 영속화 |
| `OpenSettingsFileCommand` | `AppViewModel.NavCommands.cs` | `settings.json`을 OS 기본 편집기로 열기 |
| `SettingsStatus` | `AppViewModel.Settings.cs:546` | 저장 결과 / 탐지 결과 / 로그인 상태 메시지. `Ok` 색상으로 표시 |

### 3-2. 섹션 01 — Runtimes

각 엔진 카드(cc · gx · agy · pi)는 동일한 패턴을 반복한다.

#### 엔진 공통 바인딩 패턴

| 바인딩 경로 (cc 예시) | 소스 파일 | 설명 |
|---|---|---|
| `SettingsEngineCc` | `Settings.cs:311` | `TwoWay`; 엔진 활성/비활성 `TogglePillButton` |
| `ClaudeDetectLabel` | `Settings.cs:503` | CLI 실행 파일 탐지 결과 텍스트. 저장 시 `RefreshDetectLabels()` 갱신 |
| `SettingsClaudePath` | `Settings.cs:25` | `UpdateSourceTrigger=PropertyChanged`; CLI 경로 입력란 |
| `DetectEnginePathCommand` + `CommandParameter="cc"` | `NavCommands.cs` | 자동 탐지 → 경로 입력란 자동 완성 |
| `CcModels` | `Settings.cs:185` | 모델 드롭다운 소스. 체크리스트 선택 시 `DropdownModelsFor("cc")` 재계산 |
| `SettingsModelCc` | `Settings.cs:301` | `TwoWay`; 기본 모델 선택 |
| `CcChecklist` | `Settings.cs:199` | `ModelChecklistVm` 인스턴스. `ContentControl.Content`에 연결 → `DataTemplate` 자동 적용 |
| `SettingsAuthCc` | `Settings.cs:372` | `"subscription"` 또는 `"api"`; 인증 모드 선택 토글 Border DataTrigger 기준값 |
| `AuthModeCommand` + `CommandParameter="cc:subscription"` | `NavCommands.cs` | 인증 모드 Border 클릭 → `SettingsAuthCc` 갱신 |
| `SignInCommand` + `CommandParameter="cc"` | `NavCommands.cs` | `cmd.exe /k <cli>`로 CLI 로그인 터미널 열기 |
| `CcAccount` | `Settings.cs:542` | 구독 인증 계정 이메일. 빈 문자열이면 안내 텍스트, 있으면 계정 표시 |
| `SettingsApiKeyCc` | `Settings.cs:374` | `PasswordBoxAssistant.BoundPassword TwoWay`; DPAPI 복호화된 일반 텍스트 |
| `SettingsAutoApiCc` | `Settings.cs:381` | `TwoWay`; 한도 소진 시 API 자동 전환 `TogglePillButton` |

> Antigravity(agy)와 Pi(pi) 카드는 구독/API 이중 인증 선택이 없고, agy는 `AgyAccount` 표시만, pi는 자체 `~/.pi` 관리를 안내한다.

#### Pi 전용 추가 바인딩

| 바인딩 경로 | 설명 |
|---|---|
| `PiModels` | `IsEditable="True"` 콤보박스; pi 카탈로그(`pi --list-models`) 또는 정적 목록 |
| `SettingsModelPi` | `UpdateSourceTrigger=PropertyChanged`; 직접 입력 허용 |
| `QueryPiModelsCommand` | 카탈로그 조회 트리거 |
| `PiModelsStatus` | 조회 상태 텍스트 |
| `PiConnectedProviders` | 연동 provider 목록 텍스트 |

#### 사용량 카드 바인딩

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `UsageRows` | `AppViewModel.Usage.cs:39` | `ObservableCollection<UsageRowVm>`; 엔진별 사용량 행 |
| `UsageStatusText` | `AppViewModel.Usage.cs` | 조회 중 / 데이터 없음 fallback 텍스트 |
| `UsageFallbackVisible` | `AppViewModel.Usage.cs:34` | `UsageRows.Count == 0`이면 `Visible`, 아니면 `Collapsed` (Visibility 직접 반환) |
| `CheckUsageCommand` | `NavCommands.cs` | 사용량 조회 실행 |

`UsageRowVm` 아이템 바인딩:
- `Name`, `Note`: 엔진명·주석
- `Bars`: `IEnumerable<UsageBarVm>` — `Label`, `FillStar`, `RestStar`, `Percent`, `Level`(`"ok"/"warn"/"crit"`)

### 3-3. 섹션 02 — Translation

| 바인딩 경로 | 설명 |
|---|---|
| `AvailableLanguages` | `IReadOnlyList<LanguageDef>` (`ko`/`en`); UI 언어 ComboBox 소스 |
| `SettingsLanguage` | `TwoWay SelectedValue`; `"ko"` 또는 `"en"` |
| `AvailableTranslationLanguages` | 11개 언어 목록; 번역 쌍 ComboBox 공용 소스 |
| `SettingsTranslateSource` | `TwoWay`; 번역 전 언어(사용자 입력 언어) |
| `SettingsTranslateTarget` | `TwoWay`; 번역 후 언어(엔진 전달 언어) |
| `SettingsOllamaEndpoint` | `UpdateSourceTrigger=PropertyChanged`; Ollama HTTP 엔드포인트 |
| `OllamaRunning` / `OllamaStopped` / `OllamaAbsent` | Ollama 상태별 bool; 버튼 및 표시기 Visibility DataTrigger 기준 |
| `OllamaStatusText` | 상태 설명 텍스트 |
| `RefreshOllamaStatusCommand` | Ollama 상태 재확인 |
| `StartOllamaCommand` | `ollama serve` 실행 (설치됐지만 꺼진 경우만 표시) |
| `OpenInstallGuideCommand` | 설치 안내 (미설치 경우만 표시) |
| `OllamaModels` | `IsEditable="True"` ComboBox 소스; 조회 결과 목록 |
| `SettingsOllamaModel` | 선택/직접 입력 번역 모델 |
| `OllamaModelsStatus` | 조회 상태 텍스트 |
| `QueryOllamaModelsCommand` | 설치된 모델 목록 조회 |
| `SettingsDefaultTranslationEnabled` | `TwoWay`; 신규 세션 기본 번역 활성화 토글 |

### 3-4. 섹션 03 — Orchestration

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `MaxConcurrentSessions` | `AppViewModel.cs:334` | `UpdateSourceTrigger=PropertyChanged`; 동시 세션 수 상한 (Width=120 입력) |
| `MaxConcurrentWorkers` | `AppViewModel.cs:342` | 동시 워커 수 상한 |
| `SettingsWarnNoWorktree` | `Settings.cs:118` | `TwoWay`; 비-git 폴더 경고 토글 |
| `SettingsWorktreeBase` | `Settings.cs:172` | worktree 생성 기본 경로 입력 |
| `SettingsAutoStart` | `Settings.cs:175` | `TwoWay`; 마지막 세션 자동 시작 토글 |
| `SettingsStreamLogs` | `Settings.cs:180` | `TwoWay`; 실시간 활동 로그 표시 토글 |
| `WorkerBehaviorPreamble` | `AppViewModel.cs:350` | `AcceptsReturn="True"` 멀티라인 텍스트박스; 워커 동작 지침 기본 템플릿 |

### 3-5. 섹션 04 — Permissions

| 바인딩 경로 | 설명 |
|---|---|
| `SettingsApprovalPolicy` | 현재 선택된 정책 (`"ask"/"safe"/"yolo"`); 각 Border DataTrigger 기준값 |
| `SettingsSegCommand` + `CommandParameter="policy:ask"` 등 | `NavCommands.cs:86`; `"policy:X"` 파라미터로 `SettingsApprovalPolicy` 설정 |
| `SettingsTelemetry` | `TwoWay`; 익명 텔레메트리 토글 |

### 3-6. 섹션 05 — Appearance

| 바인딩 경로 | 소스 파일 | 설명 |
|---|---|---|
| `AvailableThemes` | `Settings.cs:135` | `IReadOnlyList<Theme.ThemeDef>`; 테마 선택지 WrapPanel |
| `SettingsTheme` | `Settings.cs:120` | `ThemeSelectCommand` CommandParameter로 전달; 선택 즉시 라이브 적용 |
| `ThemeSelectCommand` | `NavCommands.cs:107` | `SettingsTheme = id` 실행 |
| `SelBrush` (MultiBinding) | `Converters` | 테마 카드 BorderBrush 결정: `Id == SettingsTheme`이면 강조색, 아니면 기본 |
| `SettingsAccent` | `Settings.cs:388` | 강조색 식별자(`"ember"` 등); 선택 즉시 라이브 적용 |
| `SettingsSegCommand` + `CommandParameter="accent:ember"` 등 | `NavCommands.cs` | `"accent:X"` 파라미터로 `SettingsAccent` 설정 |
| `SettingsAccentCustom` | `Settings.cs:396` | hex 입력; 유효 hex이면 `SettingsAccent`에 즉시 반영 |
| `ZoomPercentOptions` | `Settings.cs:423` | `int[]` 50~200 (10 단위); 줌 ComboBox 소스 |
| `BodyScalePercent` | `Settings.cs:420` | `TwoWay`; 본문 줌 비율(%) |
| `ModalScalePercent` | `Settings.cs:421` | `TwoWay`; 모달 줌 비율(%) |
| `ZoomResetAllCommand` | `NavCommands.cs:57` | 본문·모달 줌 모두 100%로 리셋 |

강조색 스와치 선택 표시: 각 `Border`는 `Tag`(색상 이름)를 보유하며, `StackPanel.Resources`에 `Swatch` 스타일이 8개의 `MultiDataTrigger`로 `Tag == SettingsAccent`일 때 `BorderBrush`를 `Txt0`으로 전환한다.

### 3-7. 섹션 06 — Project

| 바인딩 경로 | 설명 |
|---|---|
| `ActiveProject.Name` | `Mode=OneWay`; 섹션 설명 텍스트에 현재 프로젝트명 삽입 |
| `ActiveProject.McpConfigPath` | `Mode=TwoWay`; MCP 설정 경로 입력 |
| `ActiveProject.ExtraPaths` | `ItemsSource`; 추가 폴더 목록 |
| `RemoveExtraPathCommand` | `AppViewModel.cs:130`; 항목 삭제 |

`AddExtraPath_Click` 버튼은 코드 비하인드에서 `OpenFolderDialog`를 열어 선택 결과를 `vm.AddExtraPath(path)`로 전달한다.

### 3-8. 섹션 07 — Skills

| 바인딩 경로 | 설명 |
|---|---|
| `SettingsSkillContent` | `TwoWay`; `SKILL.md`에 기록될 스킬 내용 멀티라인 텍스트박스 |
| `SettingsSkillDirCc` / `Gx` / `Agy` / `Pi` | `TwoWay`; 각 엔진의 스킬 파일 배치 디렉터리 |
| `SkillInjectStatus` | Save 시 `ApplyAndInjectSkill()` 결과 요약 (`cc ✓   gx ✓ …`); 빈 문자열이면 `Collapsed` |

---

## 4. 사용된 커스텀 컨트롤

### `controls:PasswordBoxAssistant` (첨부 속성)

```csharp
// Controls/PasswordBoxAssistant.cs
public static class PasswordBoxAssistant
{
    // BoundPassword: PasswordBox.Password ↔ ViewModel string (TwoWay 가능)
    // Updating: 재귀 업데이트 방지 플래그
}
```

WPF `PasswordBox`는 보안상 `Password` 프로퍼티가 데이터 바인딩을 지원하지 않는다. `PasswordBoxAssistant`는 `PasswordChanged` 이벤트를 구독해 `BoundPassword` 첨부 프로퍼티와 동기화하는 브리지다. `SettingsApiKeyCc`, `SettingsApiKeyGx`에 사용.

### `controls:MouseClick` (첨부 속성)

Border(클릭 가능한 카드)에 `MouseLeftButtonUp` 이벤트를 `ICommand`에 연결한다. 인증 모드 토글 Border, 승인 정책 Border, 강조색 스와치 Border에 모두 적용된다.

### `controls:IconView`

엔진 브랜드 아이콘(`IconEngineClaude`, `IconEngineOpenAi`, `IconEngineAgy`, `IconEnginePi`)에 `Filled="True"` 옵션과 브랜드 색상 Foreground를 적용. 섹션 헤더의 `IconSettings`도 동일 컨트롤.

### `ModelChecklistVm` (DataTemplate 자동 적용)

`UserControl.Resources`에 `DataType="{x:Type vm:ModelChecklistVm}"`으로 선언된 DataTemplate이 각 엔진 카드의 `<ContentControl Content="{Binding CcChecklist}"/>`에 자동 적용된다. 접힘/펼침(`ToggleButton IsChecked=IsExpanded`), 필터(`TextBox Text=Filter`, pi에만 표시), 체크리스트(`ItemsControl ItemsSource=Choices`, `CheckBox IsChecked=IsChecked`)로 구성된 접이식 모델 선택기다.

### 스타일 / 리소스 키

| 키 | 용도 |
|---|---|
| `TogglePillButton` | 알약 형태 토글 버튼 (엔진 활성화, 번역 기본값, 자동 전환 등) |
| `ExpanderHeaderToggle` | 체크리스트 접기/펼치기 헤더 토글 버튼 |
| `ModelCheck` | 체크리스트 `CheckBox` 스타일 |
| `SettingsInput` | 설정 전용 `TextBox` 스타일 |
| `ApiKeyBox` | `PasswordBox` 스타일 |
| `Swatch` | 강조색 원형 스와치 `Border` 스타일 (26×26, 8개 `MultiDataTrigger`) |
| `SelBrush` | `MultiValueConverter`; `(id, selectedTheme)` → `BorderBrush` |
| `BoolVis` | `bool → Visibility` |
| `AccentButton` | 강조색 "저장" 버튼 |

---

## 5. 애니메이션 & 트리거

SettingsView에는 **시간 기반 애니메이션이 없다**. 모든 시각 피드백은 정적 DataTrigger/Trigger로 구현된다.

### 5-1. 인증 모드 토글 카드

```
DataTrigger: SettingsAuthCc == "subscription"
    → Border.Background = AccentDim, BorderBrush = AccentLine
DataTrigger: SettingsAuthCc == "api"
    → 해당 Border 동일 효과
```

구독/API 섹션 내용물 패널(`DockPanel` · `StackPanel`)도 같은 방식으로 가시성을 제어한다.

### 5-2. 승인 정책 카드

세 정책 Border(`ask`/`safe`/`yolo`) 각각이 `SettingsApprovalPolicy` 값과 비교하는 DataTrigger를 보유한다. `yolo`는 선택 시 BorderBrush가 `AccentLine` 대신 `Warn`으로 변경되어 위험성을 시각적으로 강조한다.

### 5-3. 강조색 스와치 선택 표시

`MultiDataTrigger`로 **두 조건**을 동시에 검사한다:
1. `SettingsAccent` 값이 이 스와치의 색상 이름과 같은가
2. `Border.Tag`가 동일 값인가

두 조건 모두 충족될 때만 `BorderBrush = Txt0`(흰 테두리)으로 선택 표시가 나타난다. 동일 스타일이 8개 색상 모두에 중복 정의되어 있다.

### 5-4. Ollama 상태 표시기

```
Ellipse.Fill:
    기본값      → Txt3 (회색)
    OllamaRunning=True  → Ok  (녹색)
    OllamaStopped=True  → Warn (노랑)
    OllamaAbsent=True   → Err  (빨강)
```

"Ollama 시작" 버튼과 "설치 안내" 버튼도 각각 `OllamaStopped` / `OllamaAbsent` DataTrigger로 가시성이 제어된다.

### 5-5. 계정 로그인 상태

구독 인증 섹션에서:
- `CcAccount == ""` → 안내 텍스트 표시, 계정 패널 숨김
- `CcAccount != ""` → 계정 이메일 표시, 안내 텍스트 숨김

빈 문자열을 기준값으로 쓰는 패턴은 `HistoryView`의 `CostText="-"` 센티넬과 동일한 방식이다.

### 5-6. SkillInjectStatus 가시성

```
DataTrigger: SkillInjectStatus == "" → Visibility=Collapsed
기본값                               → Visibility=Visible
```

Save 전에는 빈 문자열이므로 숨겨져 있다가, Save 후 결과 요약이 채워지면 나타난다.

---

## 6. 코드 비하인드 vs. XAML 역할 분리

### 코드 비하인드 (`SettingsView.xaml.cs`)

```csharp
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private AppViewModel? Vm => DataContext as AppViewModel;

    // TOC 클릭 → 스크롤 내 섹션으로 이동
    private void SettingsToc_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name && FindName(name) is FrameworkElement target)
            target.BringIntoView();
    }

    // 추가 폴더 → OS 폴더 선택 다이얼로그
    private void AddExtraPath_Click(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog → vm.AddExtraPath(path)
    }
}
```

생성자 외에 **정확히 두 개의 이벤트 핸들러**만 존재한다.

| 핸들러 | 이유 |
|---|---|
| `SettingsToc_Click` | `BringIntoView()`는 `UIElement` 메서드로, ViewModel이 View 요소에 직접 접근할 수 없어 코드 비하인드가 적합 |
| `AddExtraPath_Click` | `OpenFolderDialog`는 Win32 다이얼로그로 `Window` 참조(`Window.GetWindow(this)`)가 필요하며, XAML 커맨드만으로 처리할 수 없음 |

나머지 모든 인터랙션(설정 변경, 저장, 취소, 탐지, 조회 등)은 ViewModel 커맨드와 TwoWay 바인딩으로 처리된다.

---

## 7. 핵심 특징 (Portfolio Highlights)

1. **Settings* 이중 버퍼(Staging Mirror) 패턴**
   설정 UI는 `_claudePath` 같은 실제 필드를 직접 바인딩하지 않는다. `PullSettingsToEditor()`가 설정 열 때 실제 필드 → `Settings*` 미러 프로퍼티로 복사하고, `SaveSettings()`가 반대 방향으로 반영한다. 테마·강조색만 예외적으로 라이브 미리보기가 적용되며, `CloseSettings()`에서 `Theme.ThemePalette.Apply(_theme)`로 저장된 값을 복원한다. 이 패턴 덕분에 "취소"가 완벽하게 작동한다.

2. **`SettingsSegCommand`의 파라미터 라우팅**
   `"policy:ask"`, `"accent:ember"`처럼 `"섹션:값"` 형식의 문자열 파라미터 하나로 여러 설정을 동일 커맨드로 처리한다(`NavCommands.cs:86`). 이를 통해 Border·스와치처럼 Button이 아닌 요소들도 `controls:MouseClick`과 조합해 커맨드를 발행할 수 있다.

3. **`PasswordBoxAssistant` — 보안 입력의 MVVM 우회**
   WPF의 `PasswordBox`는 의도적으로 `Password` 프로퍼티의 데이터 바인딩을 막는다. `PasswordBoxAssistant`는 `PasswordChanged` 이벤트를 구독해 `BoundPassword` 첨부 프로퍼티와 동기화하는 브리지 역할을 한다. 입력된 API 키는 ViewModel에서 `Persistence.Dpapi.Encrypt`로 암호화해 저장하고, `PullSettingsToEditor`에서 `Dpapi.Decrypt`로 복호화해 UI에 채운다.

4. **`ModelChecklistVm` — DataType 자동 템플릿**
   4개 엔진 카드가 모두 `<ContentControl Content="{Binding CcChecklist}"/>`만 선언하면, `UserControl.Resources`에 등록된 `DataTemplate DataType=ModelChecklistVm`이 자동으로 적용된다. 접힘/펼침, 필터, 체크리스트 렌더링 로직이 단 하나의 템플릿으로 중복 없이 재사용된다. Pi만 `showFilter: true`로 생성돼 동적 카탈로그 검색이 가능하다.

5. **`SettingsStore` — VS Code식 분리 영속화**
   `SettingsStore`는 세션 데이터(`state.json`)와 완전히 분리된 `settings.json`에 인덴트 JSON을 원자적으로 기록한다(`JsonFile.WriteAtomic`). 구버전에서 올라오면 `state.json`의 `Settings` 노드를 1회 자동 마이그레이션한다. 푸터의 "settings.json 열기" 버튼으로 OS 기본 편집기에서 손편집도 지원한다.

---

## 8. 유지보수 시 주의 사항

- **`Settings*` 미러 누락**: 새 설정을 추가할 때 `PullSettingsToEditor()`(열기)와 `SaveSettings()`(저장) 양쪽에 대칭 코드를 추가해야 한다. 어느 한쪽을 누락하면 취소 시 이전 값 복원이 깨지거나 편집값이 실제 필드에 반영되지 않는다.
- **`SettingsSegCommand` 파라미터 규약**: `"섹션:값"` 형식 파싱 로직이 `NavCommands.cs:86`에 집중되어 있다. 새 정책·강조색 항목 추가 시 XAML `CommandParameter`와 `SettingsSegCommand` 핸들러를 함께 수정해야 한다.
- **강조색 스와치 `MultiDataTrigger` 중복**: 8개 색상 각각에 `MultiDataTrigger`가 `Swatch` 스타일 내에 중복 정의되어 있다. 새 강조색 추가 시 XAML에 `MultiDataTrigger` 한 블록, `Border` 한 행, `NavCommands.cs`의 처리 분기가 필요하다.
- **Ollama 상태 bool 3분할**: `OllamaRunning`, `OllamaStopped`, `OllamaAbsent`는 `OllamaState` 문자열에서 파생된 computed bool이다. `OllamaState` 변경 시 세 프로퍼티 모두 `OnChanged`가 발행되는 구조로, 하나라도 누락하면 DataTrigger가 업데이트되지 않는다.
- **`SkillInjectStatus` 빈 문자열 센티넬**: `DataTrigger: Value=""` → `Collapsed`로 가시성을 제어하는 방식이다. 오류가 아닌 빈 상태를 `""` 이외의 문자열로 표현하면 스킬 상태 텍스트가 의도치 않게 표시될 수 있다.
- **`MaxConcurrentSessions` · `MaxConcurrentWorkers` 입력 검증**: 현재 `UpdateSourceTrigger=PropertyChanged`로 키 입력마다 즉시 VM 프로퍼티에 반영된다. 비정수 입력에 대한 `ValidationRule`이 없어, 빈 문자열이나 음수를 입력한 채 저장하면 `int.Parse` 예외가 발생할 수 있다.
