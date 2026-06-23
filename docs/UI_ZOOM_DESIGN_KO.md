# UI 줌 (Ctrl+휠) — 설계 분석

> 상태: 분석/설계(확정 전) · 목표: 크롬/파이어폭스식 Ctrl+휠 확대·축소로 글씨 크기·UI 비율을 유동 조절.

## 1. 현재 스케일 구조
- 스케일 노브가 하나뿐: DensityScale(comfortable=1.0 / compact=0.92) — AppViewModel.Settings.cs:181.
- 적용: Body Grid(Row 1)의 LayoutTransform→ScaleTransform (MainWindow.xaml:404-407).
  - LayoutTransform이라 글씨+레이아웃이 함께 리플로우(흐림 없음). 크롬 줌과 동일 성질.
  - 타이틀바(Row 0, 커스텀 WindowChrome)는 스케일 제외.
- 영속: settings.json Density. 라이브 적용(OnChanged(nameof(DensityScale))).
→ 줌 메커니즘은 이미 존재. 이 스케일을 연속값으로 굴리면 됨.

## 2. 핵심 발견 / 결정 지점
### 2.1 모달이 스케일 밖에 있음 (주의)
오버레이(New Agent · WorkerAssign · NoIdleWorker · About · NewProject · NewSchedule)는 루트에서 Grid.RowSpan="2" 형제 → Body Grid 바깥. 현재 구조로 줌하면 본문만 커지고 모달은 안 커짐.
- (a) 권장: 본문+오버레이를 Row 1 내부 컨테이너로 묶고 그 컨테이너를 스케일(타이틀바만 제외).
- (b) 본문만 줌, 모달 고정(수용 가능하나 어색).

### 2.2 휠 핸들러 충돌 — 없음
트랜스크립트 TranscriptScroll_PreviewMouseWheel(중첩 스크롤 중계, SessionView.xaml.cs)가 있으나, Window 레벨 PreviewMouseWheel(터널링)이 먼저 발화. Ctrl 눌림이면 줌 처리 후 e.Handled=true → 스크롤 중계로 안 내려감.

### 2.3 "글씨 크기" 단독 vs 통합 줌
- 통합 줌(ScaleTransform)은 글씨+UI를 함께 스케일 = 크롬/파폭 Ctrl+휠과 동일 → 한 노브로 충족. 권장.
- 글씨만 따로: FontSize가 곳곳에 하드코딩 → 전역 베이스 폰트 리소스화 필요 = 별도 큰 작업. 후순위.

## 3. 구현 개요 (확정 시)
1. UiScale(double) 신설 — 기본 1.0, clamp(예: 0.5~2.0), step 0.1. ScaleTransform ScaleX/Y가 바인딩.
   - density: ① UiScale로 흡수/대체(compact→0.9 마이그레이션) 또는 ② 병존(effective = density × zoom). ①이 단순.
2. Window PreviewMouseWheel(code-behind): Keyboard.Modifiers.HasFlag(Control)이면 UiScale += Sign(e.Delta)*step, clamp, 반올림, e.Handled=true.
3. 단축키: Ctrl+0 100% 리셋(+선택 Ctrl++/Ctrl+-, 넘패드 포함). Window.InputBindings/ZoomCommand.
4. 영속: AppSettingsDto.UiScale + ApplySettings/BuildSettingsDto(density와 동형). 라이브. 휠 연사 대비 저장 디바운스.
5. (선택) Appearance UI: density 세그를 줌 컨트롤(슬라이더 또는 −/100%/+ 스테퍼)로 교체/병행. 줌 변경 시 "110%" 토스트.

## 4. 확정 필요 항목
| # | 결정 | 옵션 |
|---|------|------|
| 1 | 스케일 범위 | 모달 포함(권장, 2.1-a) / 본문만 |
| 2 | density 처리 | UiScale로 대체(권장) / 병존(density×zoom) |
| 3 | 글씨 단독 옵션 | 통합 줌만(권장) / 전역 폰트 스케일까지 |
| 4 | 범위·스텝 | 예 50~200%, 10% 단위 — 적정? |
| 5 | 설정 UI | 슬라이더 / 스테퍼 / 휠 전용+% 표시 |

## 4.1 확정된 결정 (2026-06-23)
1. **스케일 범위 = 둘 다 지원 + 설정에서 선택.** `ZoomScope`(Body | All) 설정 신설. All이면 본문+오버레이를 Row 1 내부 컨테이너로 묶어 스케일(2.1-a, 타이틀바 제외), Body면 본문만. 기본값 = **All(모달 포함)**(브라우저 동일 거동).
2. **density → UiScale로 대체.** 단일 연속값 UiScale로 통합. 기존 compact는 0.9로 마이그레이션, comfortable=1.0. density 노브 제거.
3. **통합 줌만.** ScaleTransform로 글씨+UI 동시 스케일. 전역 폰트 단독 스케일은 후순위(별도 작업).
4. **범위 50~200%, 스텝 10%.** clamp 0.5~2.0, step 0.1.
5. **설정 UI = 휠 + 단축키 + % 토스트.** Ctrl+휠 / Ctrl+0(리셋)·Ctrl++·Ctrl+−(넘패드 포함), 변경 시 "110%" 토스트. Appearance에는 `ZoomScope` 선택 + 리셋만(슬라이더/스테퍼 없이 최소).

> 남은 열린 항목 없음 → 다음은 구현(§3 기준, ZoomScope 설정 반영).

## 5.1 구현 메모 (2026-06-23, 비주얼 테스트 전)
- **UiScale**(double, clamp 0.5~2.0, step 0.1)가 density를 대체 — AppViewModel.Settings.cs. 기존 compact는 로드 시 0.9로 마이그레이션(Persistence ApplySettings). density 노브/세그 제거.
- **본문 스케일**: Body Grid LayoutTransform→ScaleTransform이 UiScale에 바인딩(MainWindow.xaml).
- **모달 스케일(ZoomScope=All)**: 컨테이너 재구성 대신 **각 모달 Border에 LayoutTransform→ScaleTransform(ModalScale) 바인딩**으로 처리. `ModalScale = ZoomScope=="all" ? UiScale : 1.0`. 6개 오버레이 모두 적용(백드롭은 풀사이즈 유지, 모달만 중앙 기준 스케일). 2.1-a의 행 재구성보다 단순·저위험.
- **휠/단축키**: MainWindow.xaml.cs PreviewMouseWheel(Ctrl+휠) + InputBindings(Ctrl+0/넘패드0 리셋, Ctrl + +/Add 인, Ctrl + -/Subtract 아웃) → ZoomIn/Out/ResetCommand.
- **영속**: AppSettingsDto.UiScale/ZoomScope(+레거시 Density 읽기 유지). 휠 연사 대비 600ms 디바운스 저장.
- **토스트**: 줌 변경 시 "110%" 1.1s 표시(루트 최상단 Border, IsHitTestVisible=false).
- **설정 UI**: Appearance density 세그 → ZoomScope 세그(전체/본문만) + 현재 % + 100% 리셋.
- 상태: 빌드 0/0, KO/EN 파리티 통과. **비주얼 테스트 대기(사용자 지시 전 앱 미실행).**

## 5. 리스크 / 메모
- LayoutTransform 스케일은 텍스트를 새 크기로 재렌더(선명). RenderTransform(흐림)은 미사용.
- 극단 배율에서 고정폭 컬럼(사이드바 280 등)·아이콘 크기 점검.
- 줌은 창 크기와 독립(레이아웃만 스케일). 접근성(저시력) 보너스.
