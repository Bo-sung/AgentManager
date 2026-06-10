# [위임 작업] AgentManager UI 일괄 바인딩 패스

> 이 문서를 그대로 다른 Claude 세션에 붙여넣으면 됩니다. 자체완결형.

---

## 컨텍스트
`J:\prj\AgentManager` 는 WPF(.NET 10) 멀티 에이전트 매니저다. **MVVM 분리 완료** — 아래 모든 기능의 ViewModel 로직·커맨드·영속성은 이미 구현·검증되어 있고, **View(XAML) 바인딩만 없다**. 네 작업은 XAML에 바인딩 UI를 붙이는 것 **뿐**이다.

- UI 디자인 레퍼런스: `design/AgentManager.html` + `design/am-*.jsx` (다크 `#0a0e12` + 오렌지 `#ff5a2c`). 기존 XAML 스타일을 따라가면 됨.
- 메인 화면: `src/AgentManager/MainWindow.xaml` (+ code-behind `MainWindow.xaml.cs`)
- 테마 토큰/스타일: `src/AgentManager/Theme/Theme.xaml` (Bg0~5, Line*, Txt0~3, Accent*, Warn/Ok/Err, `Lbl`/`AccentButton`/`ChipButton`/`TogglePillButton`/`SendButton` 스타일 존재)
- VM: `src/AgentManager/ViewModels/AppViewModel.cs`(루트 DataContext), `SessionViewModel.cs`, `ProjectViewModel.cs`, `ArtifactViewModel.cs`, `Blocks.cs`
- 루트 윈도우 이름: `x:Name="Root"` → 세션 템플릿 안에서 앱 커맨드는 `{Binding DataContext.XxxCommand, ElementName=Root}` 패턴 사용(기존 코드에 예시 다수)

## 빌드/실행/검증 (필수 루프)
```powershell
# 실행 중인 앱 종료 후 빌드 (파일 잠금 방지)
Get-Process AgentManager -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build J:\prj\AgentManager\AgentManager.slnx -c Debug   # 경고 0/오류 0 유지
dotnet run --project J:\prj\AgentManager\src\AgentManager\AgentManager.csproj
```
각 항목 끝날 때마다 빌드 확인. 전부 끝나면 앱 실행해서 육안 확인.

## ⚠ 제약 (중요)
1. **ViewModel/Core 로직 수정 금지** — 바인딩만. 속성/커맨드가 없다고 느껴지면 잘못 찾은 것(아래 표 참조).
2. XML 주석에 `--` 금지 (MC3000 빌드 에러).
3. `BooleanToVisibilityConverter`는 리소스 키 `BoolVis`로 이미 등록됨. 역변환 필요하면 Style DataTrigger 사용(기존 예시 있음).
4. 인라인 이벤트 핸들러가 필요하면 code-behind에 추가해도 됨(기존 `SessionRow_Click` 등 패턴 참조). 단 로직은 VM 메서드 호출만.
5. 커밋: 항목 단위로, 메시지 끝에 `Co-Authored-By: Claude <noreply@anthropic.com>`.

## 작업 항목 (각각 독립, 순서 무관)

### 1. 타이틀바 집계 표시
- 위치: 타이틀바 우측 RUNNING/WAITING/DONE 스택(검색: `DONE</TextBlock>`) 옆
- 바인딩: `AppViewModel.TotalTokensLabel`(문자열 "1.2k / 3.4k"), `TotalCostLabel`("$0.12")
- 형태: `Σ TKN {TotalTokensLabel}` · `COST {TotalCostLabel}` — Mono 10px, Txt2/Ok 색

### 2. 상태 strip 세션 비용
- 위치: 상태 strip의 `TKN {TokensLabel}` 옆 (검색: `TokensLabel`)
- 바인딩: `SessionViewModel.CostLabel` ("$0.0042" 또는 "—")

### 3. 세션 컨텍스트 메뉴
- 위치: 사이드바 세션 행 Border (검색: `SessionRow_Click`)
- `ContextMenu`로: 이름변경 / 보관 토글 / Fork / 삭제
- 커맨드(전부 AppViewModel, parameter=세션 VM):
  - `ArchiveSessionCommand` (CommandParameter={Binding})
  - `ForkSessionCommand` (CommandParameter={Binding})
  - `DeleteSessionCommand` (CommandParameter={Binding})
  - 이름변경: `RenameSessionCommand`는 string 파라미터(새 제목) — 간단한 입력은 활성 세션 대상. 인라인 TextBox 팝업이 번거로우면 컨텍스트 메뉴에서 "이름변경" 클릭 시 작은 오버레이(기존 NewAgent 오버레이 패턴 참조)로.
- ContextMenu 다크 스타일은 Theme.xaml에 추가해도 됨(스타일 추가는 허용).

### 4. 아카이브 그룹
- 위치: 사이드바, Active/Project 그룹 아래 (검색: `ProjectSessions`)
- 바인딩: `AppViewModel.ArchivedSessions` (ObservableCollection<SessionViewModel>)
- 형태: 기존 그룹과 동일한 ItemsControl + 섹션 헤더 "ARCHIVED" + 카운트. 흐리게(Txt2).

### 5. 샌드박스/승인 토글 (새 에이전트 모달 + 상태 strip)
- 새 에이전트 모달(검색: `NEW WORKER`)에 추가:
  - 샌드박스 선택: `SessionViewModel.Sandbox`는 enum `AgentManager.Core.Agents.SandboxMode` (ReadOnly/WorkspaceWrite/DangerFullAccess). **단, 모달은 세션 생성 전이므로** 생성 후 strip에서 바꾸는 것으로 충분 — 모달은 생략 가능.
- 상태 strip(검색: `TranslationEnabled, Mode=TwoWay`) 옆에:
  - 승인 토글: `TogglePillButton` 스타일, `IsChecked="{Binding RequireApproval, Mode=TwoWay}"`, Content="APPROVAL"
  - 샌드박스: ComboBox (ItemsSource는 XAML `ObjectDataProvider`로 enum 값, SelectedItem="{Binding Sandbox}") — 작게(Mono 10px)

### 6. Review pane Commit 버튼 + Diff 피드백
- 위치: Review pane 푸터 (검색: `Merge ▸ main`)
- Commit: `ChipButton`, Content="Commit only", `Command="{Binding DataContext.CommitReviewCommand, ElementName=Root}"`
- Diff 피드백: 푸터 위에 한 줄 TextBox+버튼. 버튼 `Command="{Binding DataContext.SendDiffFeedbackCommand, ElementName=Root}"`, `CommandParameter`= TextBox의 Text (CommandParameter에 ElementName 바인딩). 전송 후 TextBox 비우기는 code-behind Click에서 해도 됨.

### 7. cap 설정
- 위치: 설정 패널 (검색: `SettingsOllamaModel` 근처)
- 바인딩: `AppViewModel.MaxConcurrentSessions` (int, 최소 1 클램프는 VM이 함)
- 형태: 숫자 TextBox 또는 -/+ 버튼

### 8. Artifacts 패널
- 위치: Review pane 안 (Changes 목록 아래 섹션 또는 접이식)
- 바인딩: `SessionViewModel.Artifacts` (ObservableCollection<ArtifactViewModel>)
  - `Kind`("tasklist"/"test"/"summary"), `Title`, `Content`(여러 줄), `IsError`(test 실패 시 true), `UpdatedAt`
- 형태: ItemsControl, 항목별 헤더(Kind 뱃지+Title+시간) + 본문(Mono, wrap, MaxHeight+스크롤). IsError면 Err 색 테두리.

### 9. 프로젝트 MCP 경로 필드
- 위치: 설정 패널 또는 프로젝트 행 컨텍스트 (설정 패널 추천 — "프로젝트" 섹션 만들어 활성 프로젝트 대상)
- 바인딩: `AppViewModel.ActiveProject.McpConfigPath` (string, TwoWay)
- 라벨: "MCP CONFIG (.mcp.json 경로, 비우면 미사용)" — 변경 후 저장은 자동(VM이 SaveState)... ❗주의: McpConfigPath setter는 SaveState를 자동 호출하지 않음. **LostFocus에서 code-behind로 `_vm.SaveStateExternally()` 같은 게 없으므로**, 그냥 TwoWay 바인딩만 해두면 됨(다음 세션 생성/상태 변화 때 저장됨). 로직 추가 금지 원칙 유지.

## 완료 기준
- [ ] 9개 항목 바인딩 완료, 각 항목 커밋
- [ ] `dotnet build` 경고 0 / 오류 0
- [ ] 앱 실행 후: 타이틀바 집계 보임 · 세션 우클릭 메뉴 동작 · 아카이브 토글 시 그룹 이동 · APPROVAL 토글 ON 후 새 턴에서 승인 블록 뜸(승인 UI 자체는 이미 구현됨) · Review pane에 Commit/피드백/Artifacts 보임
- [ ] `docs/PROGRESS_KO.md`의 "UI 일괄 패스" 항목에 완료 표시 + 커밋 해시 기입
