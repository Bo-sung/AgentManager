# Changelog

AgentManager 버전별 변경 사항. (최신순) · 버전은 `vX.Y.Z` 태그와 1:1 대응.

## 1.21.15
턴 무응답(무활동) 타임아웃을 설정에서 조정 가능. (기능)
- v1.21.14의 무활동 워치독(10분 하드코딩)을 **설정 → Orchestration의 "턴 무응답 타임아웃(분)"** 드롭다운으로 노출(0·3·5·10·15·30·60분, 기본 10). **0 = 무제한**(워치독 끔).
- `settings.json`의 **`TurnTimeoutMinutes`**로 영속·손편집 가능(유효 0~120). `SessionOptions.TurnInactivityTimeout`(TurnPlanner 경유)으로 턴에 전달되어 `AgentSession`이 그 값으로 stall 감지 — 0이면 워치독 비활성.
- (검증) 빌드 green(경고 0/오류 0) · 스모크 OK.

## 1.21.14
버그 수정 — 완료 이벤트 없이 매달리는 턴까지 무한 "대기" 해소(특히 Claude Code 워커). (버그 패치)
- **배경**: v1.21.13의 grace 강제종료는 **완료 이벤트(TurnCompleted)를 본 뒤에만** 예약된다. 그래서 엔진이 **완료 이벤트를 아예 안 내보내고 매달리면** grace가 안 걸려 여전히 무한 대기(수동 중단까지 30분+ 관측)였다.
- **수정 1 — 무활동 워치독**: 엔진이 **아무 출력도 없이 10분**이 지나면 turn을 stall로 보고 강제 종료 + 완료 합성(보고 캡처). 완료 이벤트 유무와 무관하게 **모든 고착을 최대 10분으로 상한**. 매 출력마다 타이머 리셋 → 조용하지만 정상적인 긴 도구 호출(느린 빌드/테스트)은 안 끊음.
- **수정 2 — grace 단축**: 완료 이벤트를 본 뒤 자식이 안 나가는 흔한 케이스의 강제종료 유예를 30초 → 6초로. 정상 세션은 result 후 ~1-2초에 종료(유예 취소)라 무영향.
- 함께: 완료-후 고착 → 6초, 완료 이벤트 없는 stall → 10분 상한 → **무한 대기·수동 중단 불필요**. (v1.21.12의 중단-시 보고 보존 + v1.21.13 grace-kill과 합쳐져 절전·워커 시나리오 정리.)
- (검증) 빌드 green(경고 0/오류 0) · 스모크 OK. 경험적으로 cc는 stdin 조기 종료 시 정상 exit(단순 6s·도구 9s) 확인. *(실제 고착은 재현이 어려워 라이브 검증은 로직 기반.)*

## 1.21.13
버그 수정 — 완료된 턴이 무한 "대기(running)"로 고착되던 문제(실행 중 PC 절전→복귀 등). (버그 패치)
- **증상**: 대화(턴)가 실제로 완료됐는데도 세션이 계속 "실행 중/대기"로 남아 완료를 감지 못 함 → 사용자가 수동으로 중단해야 했다. 특히 실행 중 PC가 절전에 들어갔다 복귀하면 재현.
- **원인**: keep-open 엔진(claude)은 턴 완료 후 **stdin EOF를 받아야 종료**하는데, 복귀 후 자식이 죽은 소켓에 매달려 안 나가면 stdout `ReadLineAsync`가 **영원히 블록** → 턴이 끝나지 않음(턴 자체 타임아웃도 없었음).
- **수정**: `TurnCompleted`를 본 뒤 자식이 **유예 30초** 내 종료 안 하면 강제 종료 → 읽기 루프가 끝나고 턴이 깔끔히 완료 처리(status→done, 보고 캡처). 자식이 스스로 정상 종료하면 유예 타이머를 취소하므로 **정상 턴은 무영향**. codex app-server(즉시 kill)·사용자 중단 경로도 무영향.
- v1.21.12(중단 시에도 보고 보존)와 합쳐져 **절전 시나리오의 무한 대기 + 보고 유실이 함께 해소**된다.
- (검증) 빌드 green(경고 0/오류 0) · 스모크 OK.

## 1.21.12
버그 수정 — 중지/에러로 끝난 워커 턴의 완성된 보고가 유실되던 문제. (버그 패치)
- **증상**: 자동 러너(백로그 배정→실행)로 워커가 태스크를 수행하고 "## Report"를 다 만들었어도, 그 턴이 `done`이 아니라 **중지(idle)·에러(error)**로 끝나면 보고가 캡처되지 않고 태스크가 Failed로만 남아, **origin(위임한 메인/컨트롤타워) 세션의 "보고 수신함"에 아무것도 안 떴다**.
- **원인**: 보고 캡처 조건이 `worker.Status == "done" && produced` — 중지/에러 턴은 `done`이 아니라 조건에서 탈락 → 완성된 보고가 버려짐.
- **수정**: 워커가 최종 응답을 만들었으면(`produced`) **종료 상태와 무관하게 보고를 캡처**하고, 상태는 Done/Failed 분류에만 사용(둘 다 `IsFinished`라 보고가 있으면 수신함에 노출).
- **범위 주의**: 이 수정은 **중지/에러 턴의 보고 유실**을 잡는다. 깔끔히 `done`으로 끝난 태스크의 보고가 origin 수신함에 안 뜨는 경우는 **origin 세션 id 링크(죽은/교체된 컨트롤타워 세션)** 문제로 별도 조사 대상.
- (검증) 빌드 green(경고 0/오류 0) · 스모크 OK.

## 1.21.11
버그 수정 — 모델 관리에서 추가한 모델이 설정 "기본 모델" 드롭다운에 즉시 안 뜨던 문제. (버그 패치)
- **증상**: 모델 관리(설정 서브페이지)에서 엔진 모델을 추가/삭제해도 설정 카드의 **"기본 모델" 드롭다운**(및 선택 보정)이 갱신되지 않았다. 특히 **cc·gx(codex)**는 `agy models`/`pi --list-models` 같은 CLI 목록 조회가 없어 모델을 오직 모델 관리로만 넣기 때문에 가장 두드러졌다.
- **원인**: 모델 목록 갱신 경로가 둘로 갈렸는데, 체크리스트/조회 경로(`RefreshEngineModels`)는 `OnChanged(CcModels/GxModels/AgyModels/PiModels)` + 선택 보정 + New Agent + 세션 컴포저를 전부 갱신한 반면, **모델 관리 경로(`AfterModelManagerChange`)는 `NewAgentModels`만** 알리고 설정 엔진 드롭다운은 빼먹었다.
- **수정**: `AfterModelManagerChange`가 빌트인(cc/gx/agy/pi)은 `RefreshEngineModels`에 위임하도록 통합 → 추가/삭제/기본지정이 **설정 드롭다운·New Agent·컴포저에 즉시 반영**. 커스텀 엔진은 전용 설정 드롭다운이 없어 기존대로 피커/컴포저만 갱신.
- (검증) 빌드 green(경고 0/오류 0) · 스모크 OK.

## 1.21.10
커스텀 엔진 아이콘/색상. (기능)
- 커스텀 엔진 매니페스트(`engines/<id>.json`)에 **`icon`**(내장 글리프 이름 circle·square·hexagon·triangle·diamond·spark·bolt·bubble **또는** SVG path 데이터 `"M…"`) + **`color`**(hex `"#RRGGBB"`)를 넣으면 New Agent 피커·세션 탭·히스토리·오케스트레이터·설정 카드 등 **모든 표시 지점에 브랜드 아이콘/색**으로 렌더된다. 그전엔 커스텀 엔진이 아이콘 슬롯이 비어 badge 글자만 보였다.
- **구현**: `EngineVisual` + `EngineGeometryConverter`가 `EngineIcon`/`EngineIconByDef` 스타일의 **기본 Setter**로 엔진 id→아이콘/색을 공급(표시 지점 무수정, 중앙 처리). 빌트인(cc/gx/agy/pi)은 기존 브랜드 DataTrigger가 그대로 우선하고, 커스텀만 매니페스트 값을 쓴다. 아이콘 미지정 커스텀·pi-worker도 기본 글리프(circle)라 빈칸이 없다. 색은 기존 `EngineBrushConverter`를 확장(뱃지·강조색도 매니페스트 `color` 반영). *(컨버터 인스턴스는 병합 딕셔너리 forward-reference 회피 위해 Icons.xaml 내부에 정의.)*
- **입력**: 설정 → 엔진 추가 폼에 **"아이콘"·"색상"** 입력 추가 + 파일 직접 편집. 커스텀 엔진 설정 카드에도 아이콘 표시. `EngineConfig`/`EngineDef`에 `Icon`/`Color` 필드 추가.
- (검증) 빌드 green(경고 0/오류 0) · 스모크 OK(신규 `engine-cfg: custom icon/color persisted`).

## 1.21.9
사용량(rate-limit %) 체크 기능 제거. (정리)
- **왜**: 엔진마다 사용량 확인 방식이 다르고 **헤드리스로 정확히 조회할 공식 경로가 없다**. 재점검 결과 cc `/usage`는 TUI 전용이라 `claude -p "/usage"`가 슬래시 명령이 아니라 **일반 프롬프트로 모델에 전달**돼 엉뚱한 응답 + **클릭당 실토큰 소모**(관측 $0.23)에 사용량 %는 못 얻었다(`claude usage` 서브커맨드도 없음). gx는 app-server usedPercent가 실측이나 매 체크가 실턴 소모, agy/ACP/커스텀은 사용량 API 자체가 없음.
- **제거**: 푸터·설정의 **"지금 체크"** 버튼 + 엔진별 사용량 % 카드, 능동 조회(`CheckUsageAsync`/`ProbeUsageAsync`)와 `CheckUsageCommand` 배선. **코드는 보존**(XAML 주석 + C# `#if false`) — 향후 공식 사용량 API가 생기면 복원하기 쉽게.
- **유지**: 실행 중 passive `rate_limit_event` 캡처 + `MarkRateLimited`(리셋시각 기반 **소진 감지 / auto-API-fallback**)는 표시 기능과 별개라 그대로 동작(무료·낭비 없음). **cost 표시**도 무관하게 유지 — AM은 cost를 계산하지 않고 각 엔진 자가보고값(cc `total_cost_usd` 등)을 그대로 표시할 뿐.
- (검증) 빌드 green(경고 0 / 오류 0) · 스모크 전항목 OK.

## 1.21.8
커스텀 엔진 모델 자동 조회. (기능)
- **`modelsQuery`**: 커스텀 엔진 매니페스트(`engines/<id>.json`)에 모델 목록을 **한 줄에 하나씩 출력하는 명령의 args**를 넣을 수 있다(예: opencode `["models"]`). 설정 → Runtimes의 커스텀 엔진 카드에 **"설치 모델 조회"** 버튼이 생겨(modelsQuery 설정 시), `launch.exe + modelsQuery`를 실행하고 stdout을 파싱(`EngineRegistry.ParseModelLines`: 줄당 첫 토큰, trim·dedupe)해 기존 `UpdateModelsFromQuery`(survivor의 effort/preferred 보존)로 `engines/<id>.json`에 반영한다. 빌트인 pi/agy의 모델 조회와 동일 UX. **엔진 추가 폼**에도 "모델 조회 명령(args)" 입력이 생김.
- (검증) 빌드·스모크 green(`model-query parse asserts OK`) · **GUI 실측**: opencode 엔진 `modelsQuery:["models"]` → "설치 모델 조회" 클릭 → 1개 → **117개 모델 자동 populate** + "117개 조회됨".

## 1.21.7
ACP(Agent Client Protocol) 어댑터 — opencode/hermes 통합. (기능)
- **`adapterKind:"acp"`**: Zed의 **Agent Client Protocol**(개행 구분 JSON-RPC 2.0 over stdio)을 말하는 커스텀 엔진 어댑터. ACP 서버를 노출하는 어떤 CLI든 하나의 어댑터로 붙는다 — 실측 통합: **opencode**(`opencode acp` v1.17.18)·**hermes**(`hermes-acp` v0.18.2, 둘 다 protocolVersion 1).
- **동작**: `CodexAppServerAdapter`와 같은 stateful handshake를 `EngineWriteback`으로 진행 — initialize → session/new(→ sessionId) → session/prompt → `session/update` 스트림. 매핑: `agent_message_chunk`→어시스턴트 텍스트, `agent_thought_chunk`→thinking, `tool_call`/`tool_call_update`→도구 시작/결과, prompt 응답 `{stopReason,usage}`→턴 완료(토큰 usage). `session/request_permission`→권한 승인 UI(옵션 kind로 allow/reject optionId 선택). fs 능력은 false로 광고(에이전트 자체 도구 사용=tool_call 가시성), 미지원 에이전트 요청은 JSON-RPC 에러로 응답(무한 대기 방지). 첫 실행은 커스텀 엔진 trust 프롬프트 통과.
- **추가 방법**: 설정 → Runtimes → ＋ 커스텀 엔진 추가에서 adapterKind `acp` + 실행 경로 + args(opencode=`acp`, hermes=비움). 매니페스트·상세: `docs/ACP_INTEGRATION_KO.md`.
- **한계/후속**: v1은 에이전트 기본 모델 사용(모델 선택 후속) · fs=false(에이전트 자체 도구) · 텍스트 프롬프트만 · **zcode**는 Electron+자체 protocol이라 stdio ACP 모드 미발견 → 미통합.
- (검증) 빌드·스모크 green(신규 `acp adapter asserts OK` — 실제 캡처한 opencode/hermes 트래픽으로 핸드셰이크·이벤트 매핑·권한 응답 검증) · **live GUI E2E**: `opencode acp`로 세션 생성→trust 승인→ACP 핸드셰이크 connected→thinking+어시스턴트 응답 스트림→완료(토큰 usage).

## 1.21.6
핫픽스 — 모델 0개 커스텀 엔진 실행 크래시. (버그 패치)
- **크래시(`IndexOutOfRangeException`)**: 모델이 하나도 없는(`models[]` 비어있는) 커스텀 엔진을 New Agent(또는 워커 피커)에서 선택해 **Launch하면 즉시 크래시**했다. `CreateSession`의 모델 폴백이 `engine.Models[0]`을(워커 setter는 `value.Models[0]`을) 무가드 접근한 탓. `DefaultModelFor`가 이미 `FirstOrDefault() ?? ""`로 안전하므로 두 경로 모두 그걸로 교체 — **0모델 엔진은 유효**(컴포저가 모델을 자유 입력하므로, 빈 모델이면 `--model` 없이 엔진 기본으로 실행). (v1.21.5의 커스텀 엔진 추가 폼이 모델 없이도 엔진을 만들 수 있게 되면서 드러난 잠복 버그.)
- (검증) 빌드·스모크 green · 잔여 무가드 `Models[0]` 없음 · **GUI 실측**: 0모델 one-shot 커스텀 엔진 생성 → New Agent → Launch → 세션 정상 생성(크래시 없음), 이어서 A3 trust 프롬프트 렌더 + 거부 시 프로세스 미생성·idle 복귀까지 확인.

## 1.21.5
커스텀 엔진 마감 + 보안 하드닝 (병렬 설계→구현 워크플로). (기능+보안 패치)
- **커스텀 엔진 추가 폼**(A2): 설정 → Runtimes에 인라인 "＋ 커스텀 엔진 추가" 폼 — id(유일·파일명안전 검증)/이름/배지/adapterKind(드롭다운: one-shot-text·agentmanager-bridge-jsonl·재사용 빌트인)/실행 exe/args(줄당 1개)/초기 모델 입력 → `EngineConfigStore.Upsert`로 `engines/<id>.json` 생성. JSON 손편집 불필요.
- **실행 전 trust 프롬프트**(A3, 보안): 커스텀 엔진(임의 exe+args)의 **첫 실행 전** exe+인자 목록을 모달로 보여주고 승인 요구. 승인 지문(SHA-256 over exe+args)을 `trusted-engines.json`에 저장 — 재시작 후에도 유지, exe/args가 바뀌면 **재승인**. 거부 시 프로세스·런슬롯·트랜스크립트 모두 무소비. args는 끝까지 인자 리스트 유지.
- **`agentmanager-bridge-jsonl` 어댑터**(A4): one-shot-text보다 풍부한 라인구분 JSON 프로토콜 — session_started/assistant_delta/thinking/tool_started/tool_result/token_usage/turn_completed를 정규화 이벤트로 매핑(ARGS 모드 `{prompt}` 치환 + STDIN 모드). 제3자 CLI가 도구/사고/토큰 가시성을 갖고 통합 가능. 스펙: `docs/BRIDGE_JSONL_PROTOCOL_KO.md`.
- **커스텀 엔진 표시명 버그 수정**(A1): 히스토리 행·예약 작업이 저장된 커스텀 `AgentId`를 빌트인 전용 `EngineRegistry.Get`으로 풀어 **cc 배지/이름으로 표기**되던 것 — 정적 provider 델리게이트(`EngineResolver`→`EngineDefFor`)로 교체.
- **Ollama egress 가드**(B2, 보안): 번역 Ollama 엔드포인트가 **loopback이 아니면**(외부 호스트) 사용자 텍스트가 무단 유출되지 않도록 경고 배너 + 옵트인 토글(`AllowRemoteOllamaEndpoint`, 기본 off) 전까지 번역기 생성 차단(fail-closed).
- **워커 스풀 크기 상한**(B2): 워커-태스크 스풀 무한 증가 방지 — 전체 2000 / 파일당 200, 초과 시 **reject-newest**(유입 거부 + 드롭 로깅, 무음 삭제 없음).
- **번역 활성화 게이팅 수정**(C2): 번역 enable이 Ollama 프로세스 상태에 하드코딩돼 agent/커스텀 provider를 못 켜던 것 — provider 무관 준비상태 신호(`TranslationReady`/`RefreshTranslationStatusAsync`)로 교체(TP4 나머지 config/UI는 이미 출시됨을 확인).
- **릴리스 코드서명 배선**(B1): `scripts/release.ps1`에 인증서 소스(cert-store thumbprint / .pfx, 시크릿 비커밋) + RFC-3161 타임스탬프 배선 + `docs/RELEASE_SIGNING_KO.md`. 미서명 경로 무결. **실제 서명 릴리스는 Authenticode 인증서 확보 후.**
- **연기**: models.json 완전 제거(A5) — v1.21.0/1.21.1에서 건너뛰어 업그레이드하는 사용자의 손편집 모델/effort 유실 위험(마이그레이션 감지용 schema-version 부재). 2~3릴리스 후 재검토.
- (검증) 빌드 0오류 · 스모크 전체 green(신규 engine-trust/bridge/one-shot assert 포함) · 양 Strings 중복 x:Key 없음 · 시작 크래시 없음 · 새 XAML 바인딩 정적 감사(모든 경로 VM 해석 + TwoWay 입력은 setter 보유) 통과. ※ computer-use 미가용으로 폼/모달 **인터랙티브 흐름 검증은 사용자 설치 후 권장**(각 기능 GUI 체크 단계 제공).

## 1.21.4
커스텀 엔진 세션 재시작 손상 수정 + PR #1(GPT-5.6 변형 · 고DPI 최대화). (버그+기능 패치)
- **커스텀 엔진 세션이 재시작마다 cc로 리셋되던 버그**(데이터 손상): 세션/예약이 저장된 `AgentId`(예: `"piworker"`)로 재구성될 때 `EngineRegistry.Get(id)`를 썼는데, 이 함수는 **빌트인 4종(cc/gx/agy/pi)만** 알아 모르는 id면 `All[0]=cc`를 반환한다. 그래서 재시작마다 복원된 커스텀 엔진 세션이 **cc로 바뀌고**(모델도 cc 목록에 없어 sonnet 폴백), 다음 자동저장에서 그 잘못된 엔진이 **영구 기록**됐다. 커스텀 인지 `AppViewModel.EngineDefFor(id)`(AllEngines=빌트인+커스텀 먼저 조회) 추가 후 세션을 id로 재구성하는 4곳(복원/포크/CLI복원/예약실행)을 전부 교체 + `engine.Models[0]` 안전화. GUI E2E: 커스텀 Pi-Worker 세션 생성→턴 완료→종료·재시작 시 `AgentId`가 `piworker` 유지(예전엔 cc) 확인.
- **[PR #1](https://github.com/Bo-sung/AgentManager/pull/1) (imda564)** — Codex 모델 목록에 **GPT-5.6 Sol/Terra/Luna** 변형 추가(기존 단일 `gpt-5.6` 대체; 신규 설치/카탈로그 시드에 반영). **고DPI 최대화 버그 수정**: 125%/150% 배율에서 최대화 창이 모니터 작업영역을 넘던 문제 — `WM_GETMINMAXINFO`를 `handled=true`로 처리해 WindowChrome이 물리픽셀 경계를 덮어쓰지 못하게 하고, 최대화 상태 복원을 hwnd 훅 설치 이후로 지연, `StateChanged`/`DpiChanged` 후 모니터 작업영역 경계를 `SetWindowPos`로 재적용(모니터 간 DPI 다른 이동 지원). 작성자 검증: FHD·QHD 150%에서 최대화/복원/저장상태.
- (검증) 빌드·스모크 green(모델 카탈로그 `gpt-5.6-sol` 시드 포함) · 커스텀 엔진 재시작 유지 GUI E2E · 최대화/복원 사이클(100%) 무회귀.

## 1.21.3
v1.21.2 커스텀 엔진 후속 수정 3건 — 실제 테스트에서 발견. (버그+기능 패치)
- **`설정 새로고침`이 `engines\*.json`도 재스캔**(버그): 수동 새로고침이 `settings.json`만 다시 읽어, 손으로 추가/편집한 커스텀 엔진(자기 파일만 바뀌고 settings.json은 그대로 ⇒ "변경 없음"으로 조기 종료)이 **앱을 재시작해야만** 나타났다. 이제 새로고침이 엔진 폴더도 재스캔해 피커·모델 관리·설정에 즉시 반영한다.
- **설정 Runtimes에 커스텀 엔진 카드**(기능): 그동안 Runtimes는 cc/gx/agy/pi 4종을 하드코딩해 커스텀 엔진은 카드가 없었다. `source:"custom"` 엔진을 순회하는 **데이터 주도 "커스텀 엔진" 섹션**을 추가 — 이름/배지·adapterKind·실행 경로(진입점)·활성 토글·삭제·모델 관리 링크. 빌트인 블록은 그대로.
- **중복 "주로 쓰는 모델" 제거**: 모델 관리 페이지에 선호 체크박스가 이미 있어 빌트인 카드의 "주로 쓰는 모델" 체크리스트를 제거(선호 편집은 모델 관리로 일원화).
- **크래시 수정**: 커스텀 카드의 `adapterKind`를 `<Run Text=…>`로 바인딩했더니 WPF에서 Run.Text가 기본 TwoWay라 **읽기 전용 속성 바인딩 예외로 설정 화면이 통째로 죽던** 것(빌드는 통과) 수정 — 단일 OneWay StringFormat 바인딩으로 교체. GUI E2E로 포착.
- (검증) 빌드·스모크 green · GUI E2E: 커스텀 엔진 카드 렌더링(2개 동시)·`설정 새로고침` 라이브 반영·삭제·빌트인 체크리스트 제거 모두 확인.

## 1.21.2
엔진 설정 오버홀 — 설정 파일 3분할 + 모델 관리 + 커스텀 엔진. (기능)
- **설정 파일 3분할**: 그동안 `settings.json`(엔진별 경로·`DefaultModels`·`PreferredModels`·`SkillDirs`·인증·비활성목록)과 `models.json`(카탈로그)에 흩어져 있던 값을 **엔진 1개 = 파일 1개**(`%LOCALAPPDATA%\AgentManager\engines\<id>.json`)로 모았다. 이제 3계층: **공통/전역 = `settings.json`**, **엔진별 = `engines\<id>.json`**(모델·추론 effort·경로·인증·활성·스킬 폴더), **런타임 = `state.json`**(사용량·한도 쿨다운·무시한 세션). 모두 손편집 + 라이브 리로드. 첫 실행 시 기존 `settings.json`+`models.json`에서 엔진별 파일로 **1회 자동 마이그레이션**(survivor의 effort/preferred 보존, 빈 결과는 덮어쓰지 않음).
- **폴더 선생성**: `engines\` 폴더를 **설치·업데이트(Velopack 훅) + 매 시작 시** 미리 생성 → 첫 실행 전에도 커스텀 매니페스트를 넣어둘 수 있고, `엔진 설정 폴더 열기`가 lazy-create와 경합하지 않음. (설정 하단 `엔진 설정 폴더 열기` — 폴더는 ShellExecute 실패 회피 위해 explorer로 명시 오픈.)
- **모델 관리 서브페이지**: 설정 → `모델 관리`에서 엔진별 모델을 **여러 개 한번에 추가**(줄바꿈/콤마/불릿 분리) · 삭제 · 기본 모델 지정. 모델 설정이 설정 뷰에서 차지하던 비중을 별도 페이지로 분리.
- **커스텀 엔진**: `engines\<id>.json`의 `source:"custom"` + `adapterKind` + `launch`(exe/args 템플릿)로 **사용자 정의 엔진**을 추가하면 New Agent 피커·모델 관리에 노출·실행된다(그 파일이 곧 매니페스트). **`AdapterFactory`가 엔진 id가 아니라 `adapterKind`로 프로토콜을 분기**(빌트인 cc=`claude-stream-json`/gx=`codex-json`/agy=`agy-pty`/pi=`pi-rpc`와 동일 경로) → 프로토콜과 엔진 id 분리. 단발형 CLI용 **`one-shot-text` 어댑터** 추가(`{prompt}`/`{model}`/`{cwd}` 치환, stdout 텍스트를 어시스턴트 응답으로; 프로세스 종료 시 `TurnCompleted` 합성해 완료 판정). 커스텀 엔진은 `launch.Exe`가 있으면 "설치됨"으로 간주해 피커에서 선택 가능.
- **파일 정리**: 계산 속성(`ModelList`/`AuthOrDefault`/`HasEfforts`)이 `engines\<id>.json`에 중복 직렬화되던 것 `[JsonIgnore]`로 제거(손편집 친화).
- (검증) 빌드 green · 전체 스모크 green(신규 `engine config`/`engine config migration`/`one-shot adapter` assert 포함) · GUI E2E: 읽기·쓰기 마이그레이션, 모델 관리 대량 추가, 커스텀 엔진 피커 노출 및 실제 실행 확인. 문서: `docs/ENGINE_CONFIG_OVERHAUL_KO.md`.

## 1.21.1
자기 업데이트가 스스로 막히던 버그 수정 — 설치 폴더(`current\`) 잠금. (버그 패치)
- **버그**: Velopack은 앱을 **설치 폴더(`…\current\`)를 작업 디렉터리로** 실행한다. 그래서 앱, 그리고 그 cwd를 **상속한 자식**(특히 장수 서버 `ollama serve`)이 `current\`를 붙들면, 업데이트 때 Velopack이 그 폴더를 교체하지 못하고("파일 사용 중", code 32) **이전 버전으로 롤백**된다. 특히 설정의 **`실행(serve)` 버튼**으로 띄운 ollama가 앱 종료 후 **고아로 남아 폴더를 무기한 잠가** 모든 자기 업데이트가 실패했다(실측: 고아 `ollama.exe`의 cwd = `…\current\`).
- **수정**:
  - 앱 시작 시 작업 디렉터리를 `…\current\` **밖(안정적 데이터 폴더)** 으로 이동 → 앱도, cwd를 상속한 자식도 설치 폴더를 잠그지 않음(우리 파일 접근은 절대경로/`AppContext.BaseDirectory` 사용).
  - `실행` 버튼의 ollama에 **명시적 작업 디렉터리** 지정.
  - 앱이 **직접 띄운** ollama만 참조를 기억해 **앱 종료/업데이트 직전에 그것만** 정리(트리 종료). **사용자가 직접(데스크톱·서비스·터미널) 켠 외부 ollama는 HTTP로만 통신하며 절대 건드리지 않음.**
- **적용 시점**: 이 수정은 **이 버전 이상을 설치한 뒤부터** 효과가 있다. 기존 1.20.x/1.21.0에서 이미 폴더가 잠겨 업데이트가 막혔다면, **PC 재부팅**(또는 해당 고아 ollama 종료)으로 잠금을 한 번 푼 뒤 `업데이트 확인 → 적용`으로 넘어오면 **이후로는 재발하지 않는다**.

## 1.21.0
모델 카탈로그 파일(`models.json`) + settings.json 리로드/유실 수정. (기능)
- **모델 카탈로그 `models.json`**: 엔진별 **지원 모델 목록**과 **모델별 추론(effort) 옵션·기본값**을 사용자 편집 파일(`%LOCALAPPDATA%\AgentManager\models.json`)로 관리 → 기존 하드코딩 목록을 대체. **설정파일에 모델을 직접 추가하면 필터/드롭다운/New-Agent 피커에 그대로 뜬다**(이전엔 목록이 코드에 박혀 있어 직접 추가가 반영 안 되던 문제 해결). 모델 항목은 문자열(엔진 기본 effort 상속) 또는 `{id, efforts?, defaultEffort?}` 객체 — 추론 단계가 모델마다 다른 현실 반영(gx `gpt-5.6-luna`의 `max`, cc `ultracode` 모델 게이팅 등). pi/agy 모델 조회 시 파일 자동 갱신(기존 per-model effort 보존, 빈 결과는 덮어쓰지 않음), **선호(preferred) 모델은 settings.json 그대로 유지**. 손상/부재 시 기본값으로 재생성. 실제 GUI New-Agent 드롭다운에서 손편집 반영 E2E 확인. 진단: `dotnet run --project src/AgentManager.Smoke -- --dump-model-catalog <파일>`. 문서: `docs/MODEL_CATALOG_KO.md`.
- **설정 UI 버튼**: `models.json 열기`(설정 폴더의 카탈로그를 기본 편집기로) · `설정 새로고침`(디스크의 settings.json 수동 리로드 — 감시가 놓쳤거나 즉시 반영 원할 때).
- **settings.json 라이브 리로드 견고화 + 유실 버그 수정**: 손편집 중 **파싱 실패/파일 잠금 시 `Load()`가 조용히 기본값을 반환**해 감시기가 그 기본값으로 메모리의 실제 설정을 **통째로 덮어써 유실**(다음 자동저장이 확정)되던 버그 수정 — `SettingsStore.TryLoad()`가 실패를 신호(null)하고, 리로드는 **실패 시 적용하지 않고 기존 설정 유지**. `FileSystemWatcher.Error` 재무장(버퍼 오버플로 시 조용히 죽던 것 복구). 설정 화면을 열지 않아도 **전체 라이브 반영**(`OnChanged("")` — 언어만 리소스 사전 교체로 재시작 반영, 저장 경로와 동일).
- (검증) 빌드 green · 전체 스모크 green(15 assert groups, 신규 `model catalog` 포함) · GUI E2E(모델 직접 편집 반영).

## 1.20.1
Pi Worker 런타임 번들 + 모델 목록/커스텀 모델 개선. (기능 패치)
- **Pi Worker 런타임 번들**: `pi-worker` 하네스 런타임을 설치본에 포함(`engines/pi-worker/` vendor → publish 출력 `runtimes/pi-worker/`) → **글로벌 npm 설치나 별도 경로 지정 없이 Pi Worker 사용 가능**. 탐지 우선순위: 사용자 override → 번들 → 글로벌 npm(레거시). vendor 소스: `@agentmanager/pi-worker-harness` 0.1.0(MIT, upstream `6e49dbd`, 런타임 npm deps 0 — 순수 node 실행). framework-dependent single-file · self-contained(Velopack) 모두 번들 포함(실측). ADR: `docs/PI_WORKER_BUNDLING_KO.md`. (참고: Custom Engine 플랫폼의 Phase A — 나머지 B~H는 후속.)
- **GPT-5.6 모델**: Codex(gx) 모델 목록에 `gpt-5.6` 추가(최신 우선).
- **컴포저 커스텀 모델 입력**: 세션 컴포저에서 목록에 없는 모델 ID를 직접 입력해 사용할 수 있는 항목 추가(전 엔진 공통 흐름과 정합).
- **agy 모델 조회**: Antigravity 모델 목록을 조회하는 경로 추가(설정/모델 선택 정합).

## 1.20.0
Pi Worker — Pi 엔진의 Worker 역할을 격리 런처 `pi-worker`로 실행. (기능)
- **역할별 실행 분리**: Pi 엔진 세션이 **Worker 역할**이면 공식 `pi` 대신 **`pi-worker`**(`@agentmanager/pi-worker-harness` 0.1.0, 공식 Pi `0.80.3` 래핑, 격리 config root `~/.pi-worker`, worker-guard 확장)로 실행한다. General/Main Pi는 그대로 공식 `pi`. **별도 엔진이 아니라** 같은 Pi 엔진/`PiAdapter` 하나를 공유하고 세션 Role로 실행 파일만 분기(`PiAdapter`가 exe `.js` 여부로 node/direct 구동).
- **경로 설정/탐지**: `PiWorkerPath`(빈 값=npm-global 자동탐지, 또는 harness `dist/cli/index.js`/셸 shim 직접 지정). 미설치 시 typed 오류. 기존 설정과 backward compatible.
- **역할별 세션 discovery**: Worker 세션 파일은 `~/.pi-worker/agent/sessions`, General은 `~/.pi/agent/sessions`(`CliSessionDiscovery.PiSessionsRoot(worker)` 단일화). transcript 재동기화도 역할별 루트 스캔.
- **공통 Worker 정책 확정**: 모든 Worker 역할 세션(cc·gx·agy·pi)에 `AGENTMANAGER_ROLE/SESSION_ID/PROJECT_ID/DELEGATION_DEPTH=0` 주입, **task-spool 미제공**(무제한 재귀 위임 차단). Main/General만 task-spool 사용·워커 생성 가능.
- **RPC 완료 판정 안정화**: pi 0.80.3의 `agent_end`는 시도마다 발생하며 `willRetry`를 동반 — **`willRetry==false`일 때만 턴 완료**로 처리(기존 첫 `agent_end` 즉시 종료가 auto-retry를 중간에 죽이던 문제 수정). 회복된 retry는 성공으로 보고.
- **extension_ui_request 안전 처리**: headless Worker에서 blocking UI 요청(select/confirm/input/editor)은 즉시 cancel 응답으로 무기한 대기 차단, fire-and-forget은 무시.
- **프로세스 정리**: 취소·완료 시 `entireProcessTree` kill이 pi-worker의 자식 공식 pi까지 도달(orphan 없음).
- (검증) 전체 스모크 green(특성화 3종) · 헤드리스 라이브 E2E · Published GUI E2E 11개 TC 전부 PASS(Main→Worker 위임 + 워커 보고서 원본 Main routing 포함). 상세: `docs/PI_WORKER_INTEGRATION_KO.md`, `docs/PI_WORKER_GUI_E2E_TEST_REPORT_KO.md`.

## 1.19.8
VRAM 표시 버그 수정 — 다중 어댑터 합산 → 주 GPU만 표시. (버그 패치)
- **버그**: v1.19.7에서 VRAM 사용량을 모든 어댑터(NVIDIA dGPU + AMD iGPU + 가상 모니터)의 `Dedicated Usage`를 **합산**해 Task Manager의 GPU별 값보다 과대 집계됨.
- **수정**: 가장 큰 `Dedicated Usage` 인스턴스(주 GPU=dGPU)만 표시하고 전용 비디오 메모리 전체(레지스트리 최대 `qwMemorySize`)와 짝맞춤 → Task Manager “Dedicated GPU memory” 및 `nvidia-smi`와 일치(실측 `13.0/16.0G` = `nvidia-smi 13,304/16,376 MB`).
- **참고**: “전용 GPU 메모리 사용량”은 드라이버·데스크톱 합성·브라우저·캐시 할당을 포함해 유휴 상태에서도 높게 나타날 수 있음(정상).

## 1.19.7
자원 모니터에 GPU VRAM 사용량/전체 추가. (기능 패치)
- **VRAM 표시**: 타이틀바 자원 스트립에 GPU VRAM(사용/전체) 추가 → `CPU · GPU · VRAM · RAM · NET`. 예: `VRAM 4.5/16.0G`.
- **측정 소스**: VRAM 전체 = 레지스트리 `HardwareInformation.qwMemorySize`(가장 큰 어댑터=dGPU, QWORD라 4GiB 이상도 정상), VRAM 사용 = `GPU Adapter Memory\Dedicated Usage` 합산(phys_0). 어댑터 식별 불가 시 `—`로 폴백(예외 X).
- **구현**: DXGI raw vtable `GetDesc`가 환경 의존 AV를 일으켜 회귀 → advapi32 레지스트리 P/Invoke로 전용 비디오 메모리를 읽어 의존성·안정성 개선(신규 NuGet 없음). Core `ResourceSnapshot`에 VRAM 필드 추가, 스모크 `--resource-monitor-check`에 VRAM 검증 추가(실측 `vram=4.5/16.0G` PASS).

## 1.19.6
타이틀바 우상단 호스트 자원 모니터 추가 — CPU/GPU/RAM/이더넷 실시간 표시. (기능 추가)
- **자원 모니터 스트립**: 타이틀바 우측(상태 카운터 좌측 Col 2)에 CPU·GPU·RAM·이더넷 송수신 속도를 1초 간격으로 표시하는 컴팩트 모노 스트립 추가. 예: `CPU 12% · GPU 5% · RAM 6.2/32G · ↑1.2 ↓0.3`.
- **측정 소스**: CPU=`Processor(_Total)\% Processor Time`, GPU=`GPU Engine` 성능카운터(첫 물리 GPU 엔진 합산, 0-100 클램프), RAM=`GlobalMemoryStatusEx`(사용/전체), 이더넷=`Network Interface` Bytes Recv/Sent per sec. GPU 카운터가 없으면 `—`로 폴백(예외 X).
- **헤드리스 Core 서비스**: `AgentManager.Core.Monitoring.ResourceMonitor`로 분리(`UsageService` 패턴) — 백그라운드 1Hz 샘플링, UI는 포맷만. Core에 둬 스모크 하네스가 테스트 가능. Windows 전용(Core의 `ConPtyHost`와 동일한 위치).
- **스모크 테스트**: `--resource-monitor-check` 추가 — 카운터 오픈 + 스냅샷 형식 검증. 실측 PASS(cpu/gpu/ram/net 전부 정상).
- **비용**: 1Hz 백그라운드 틱 1회(P/Invoke 1회 + 성능카운터 수 회 읽기)로 1% 미만 CPU·수 KB 상태. `System.Diagnostics.PerformanceCounter`는 net10.0-windows에 내장이라 UI엔 NuGet 불필요, plain net10.0 Core에만 패키지 추가.

## 1.19.5
bash 블록이 비어 보이는 문제 해소 — 명령 표시 + 중단 tool 마감. (기능 패치)
- **bash 명령 표시**: tool 블록 헤더에 실제 shell 명령(`CommandText`)을 서브라인으로 항상 표시(펼치지 않아도 보임). 기존엔 명령은 캡처만 하고 렌더하지 않아, 출력(Body)이 도착 전이거나 없으면 블록에 볼 게 없어 "빈 bash"로 보였다.
- **중단된 tool 마감**: 턴이 끝났는데 결과를 못 받은 tool 블록(정지/크래시/결과 미도착)을 `…`(대기) 대신 **"중단됨"**으로 표시. 실행 중~결과 도착 사이의 빈 상태와, 중단으로 영영 빈 채로 남던 블록을 구분/해소.
- (분석) cc 원본·AM 저장 트랜스크립트 모두 완료된 bash는 100% 출력이 채워져 있어 파싱/영속화 데이터 손실은 아니었고, 빈 값은 "실행 중/중단" 표시 부재가 원인이었다.

## 1.19.4
컴포저 모델 셀렉터를 전 엔진에서 설정과 일치. (기능 패치)
- **모델 목록 불일치 해소(cc/gx/agy/pi 전부)**: 세션 컴포저의 모델 드롭다운이 설정/New-Agent 피커와 **동일한 목록**(`DropdownModelsFor`: 동적 카탈로그 + "주로 쓰는 모델" 체크 부분집합 + custom 모델)을 쓰도록 통일. 1.19.3의 pi 전용 수정을 전 엔진으로 일반화했다. 기존엔 컴포저가 정적 별칭 전체를 써서, 선호를 체크한 경우 피커보다 많이 보이거나 custom 모델이 컴포저에 안 떠 세션 중 선택이 불가했다. 체크 토글 시 해당 엔진 세션의 컴포저도 즉시 갱신.

## 1.19.3
번역 실패 가시화 + pi 모델 목록 일치 + 재동기화/토글 자동 번역. (기능 패치)
- **번역 실패 알림**: 번역이 타임아웃/provider 에러로 실패하면 무음으로 원문을 흘리지 않고 하단에 경고 토스트로 사유를 표시(더 빠른 모델·타임아웃 상향 안내). Core에 `TranslateWithOutcomeAsync`(Translated/Skipped/Failed) 추가 — "타임아웃 실패"와 "이미 대상 언어라 스킵"을 구분. ↻ 재번역·재동기화/토글 벌크·실시간 스트리밍 3경로 모두 적용. 벌크는 첫 실패(=provider/모델 문제) 시 즉시 중단해 느린 모델로 낭비 방지.
- **pi 모델 목록 일치**: 세션 컴포저의 pi 모델 드롭다운이 설정과 동일한 동적 카탈로그(`pi --list-models`) + "주로 쓰는 모델" 체크 집합을 사용 → 설정에서 고른 모델과 입력창 목록 불일치를 해소(정적 placeholder 제거). 카탈로그 (재)로드 시 열린 메뉴도 갱신, 시작 시 복원된 pi 세션은 카탈로그를 자동 로드.
- **재동기화/임포트/토글 자동 번역**: 재동기화·CLI 임포트로 불러온 대화(엔진 원문)와, 번역 토글을 켰을 때 아직 원문인 기존 블록을 백그라운드로 순차 번역(번역 ON + provider 준비 시). 기존엔 실시간 스트리밍만 번역돼 재동기화 트랜스크립트가 원문으로 남던 문제 해소.

## 1.19.2
Ollama 번역 타임아웃을 설정에서 선택 가능. (기능 패치)
- **번역 타임아웃 선택**: 설정 › 번역·언어 › Ollama에 "번역 타임아웃" 드롭다운 추가(30·60·120·180·300·600초, 기본 60초 = 기존 동작 유지). 선택값이 `OllamaOptions.Timeout`으로 전달되며, 첫 시도 실패 시 재시도는 이 값의 2배까지 늘어난다.
- **배경**: 큰 모델(예: 12B)은 콜드로드·긴 응답에서 기본 60초를 넘기면 `OllamaTranslator.GenerateAsync`가 null을 반환 → 번역이 **조용히 원문으로 폴백**(에러/로그 없음)되어 "번역이 안 되는" 것처럼 보였다. 타임아웃을 올리면 큰 모델도 정상 번역된다. (실측: gemma 12B는 정상 번역·마스크 토큰 보존·GPU 적재까지 문제 없었고 유일한 원인은 속도였음.)
- **배선**: 모델/엔드포인트와 동일 경로(SettingsService → TranslatorFactory → OllamaOptions) · settings.json 영속(10~600초 클램프) · ko/en 문구 포함.

## 1.19.1
트랜스크립트 재동기화·관측 확장 — pi 재동기화 + agy 전체 로그 수집. (기능 패치)
- **pi 트랜스크립트 재동기화**: "트랜스크립트 재동기화"가 pi(pi.dev) 엔진을 지원(기존 cc/gx 전용 → cc/gx/pi). pi 세션 파일(`~/.pi/agent/sessions/<cwd 인코딩>/*.jsonl`)에서 전체 대화를 복원해 최근 tail을 GUI 트랜스크립트에 채운다. 디렉토리명 인코딩·세션 id·메시지(text/thinking/toolCall) 복원 파서를 `CliSessionDiscovery`에 추가했고, 재동기화 버튼도 pi에 활성화.
- **agy 트랜스크립트 전체 수집**: Antigravity 관측자가 `.system_generated/logs`의 **모든 `*.jsonl`** 트랜스크립트를 수집(기존 `transcript.jsonl` 단일 파일)한다. 다중 서브에이전트 트랜스크립트까지 놓치지 않으며, 리네임 미적용으로 빌드가 깨지던 호출점도 함께 수정.

## 1.19.0
Claude Code 네이티브 역량 확장 — Fable 모델 · ultracode 워크플로우.
- **Fable/opusplan 모델**: cc 모델 피커에 `fable`(Claude Fable 5, cc 최강 모델) · `best` · `opusplan`(Plan=opus / 실행=sonnet 하이브리드) 별칭 추가. 별칭이라 최신 버전 자동 추적.
- **ultracode effort**: effort 피커에 `ultracode` 추가 — xhigh 지원 모델(Opus·Sonnet·Fable)에서만 노출. cc의 **동적 워크플로우**(다중 서브에이전트 자동 조율)를 구동한다. `--effort` 값이 아니라 `--settings {"ultracode":true}`로 전달되며, 비용 주의 문구를 함께 표시.
- **워크플로우 = 단일 턴 완료**: ultracode 워크플로우가 스트림상 "런치 턴 + 리포트 턴" 2개로 도착하던 것을 **1턴으로 접어** 최종 답만 완료로 표시(조기완료·중복 메시지 제거). 런치가 에러면 즉시 실패로 마감해 무한 대기 방지.
- **워크플로우 서브에이전트 관측**: 워크플로우가 생성한 서브에이전트가 **네이티브 작업자** 탭에 실시간 표시(Hook 관측, `workflow-subagent`).
- **번역 유지**: 위 전부 헤드리스 stream-json 경로라 KO↔EN 번역 레이어가 그대로 적용된다.
- (내부) `am` CLI에 effort 인자 추가 · 솔루션에 CLI 포함 · 3중 QA(cc 심층 + 전체 앱 헤드리스 + computer-use 비주얼) 통과.

## 1.18.1
end-user 배포 인프라 — 설치기 + 자동 업데이트 + 앱 아이콘. (앱 기능 변화 없는 패키징 패치)
- **Velopack 설치기 (`Setup.exe`)**: 런타임 번들(self-contained) → 타겟에 .NET 설치 불필요. 사용자별 설치(관리자 불필요). `scripts\release.ps1`로 생성(self-contained publish → `vpk pack` → `Releases\` = Setup.exe + 업데이트 피드).
- **인앱 자동 업데이터**: 설치본은 GitHub Releases 피드를 확인해 full/delta 패키지를 받아 재시작 시 적용(About > Update). 개발 클론은 기존 git-pull 방식 유지.
- **앱 아이콘**: exe(탐색기/작업표시줄) · 창 제목표시줄 · 설치기/시작메뉴 바로가기에 적용.
- **코드 서명(선택)**: `release.ps1 -Sign "<signtool 인자>"` — 서명 시 SmartScreen 경고 완화 + 업데이트 무결성(이전에 보류한 업데이트-서명 항목의 해결 경로).

## 1.18.0
플러그인형 번역 provider (로컬 모델에 고정 X) + 번역 유출 차단.
- **번역 provider 플러그인화**: 번역을 로컬 Ollama에만 묶지 않고 설정에서 선택 — **Ollama(로컬)** · **설치된 에이전트(cc/gx/pi 재사용, 추가 설정 0)** · **커스텀 OpenAI-호환 엔드포인트**(base URL + model + 선택적 API 키; LM Studio·llama.cpp·vLLM + OpenAI·Groq·OpenRouter 등). provider-무관 전략(코드/@file 마스킹·스크립트 스킵·프롬프트 프레이밍)은 `TranslatorBase`로 공유. 기본값은 Ollama라 기존 사용자 체감 변화 없음.
- **에이전트 번역 모델 선택**: 에이전트 provider 선택 시 번역용 모델을 지정(비우면 엔진 기본; haiku 등 싸고 빠른 모델 권장). 목록은 컴포저/새 세션과 동일(내장 + 커스텀 추가 + 선호).
- **커스텀 엔드포인트 API 키는 DPAPI 암호화**(CurrentUser) 저장.
- **보안 — 번역 유출 경고**: 번역은 프롬프트+코드를 전송하므로, **로컬이 아닌 커스텀 엔드포인트 사용 시 경고 표시**(외부 전송·보안 유의; 원격은 HTTPS 권장). 자체 LAN 테스트 서버(평문 HTTP)도 쓸 수 있도록 차단이 아닌 알림 방식. 커스텀 키는 DPAPI 암호화.
- **보안 — 스풀/상태 읽기 크기 캡**: `.am/worker-tasks`·`.am/ask`·state.json 등을 16MB 캡으로 읽어 대용량 파일 투척 로컬 메모리 DoS 차단(첨부는 기존 512KB 캡).

## 1.17.0
헤드리스 Core 추출(phase a) + CLI 프론트엔드 · cc 대용량 프롬프트 stall 근본수정 · 트랜스크립트 재동기화 · 워크트리 라이브 브랜치 추적 · 모델 별칭 · 모델별 기본 effort · 세션 UX.
- **헤드리스 Core 추출 (구조 개편 phase a)**: 오케스트레이션을 WPF ViewModel에서 `AgentManager.Core`로 분리 — EngineRegistry · EngineAuthService · SettingsService · ProjectStore(저장 코디네이터) · TurnPlanner(턴 셋업) · UsageService · TranscriptProjector(이벤트→중립 델타 리듀서) · RunRegistry · ApprovalBroker. UI/디스패처 비의존 → 프론트엔드 교체 가능.
- **`AgentManager.Cli` (`am`) — 헤드리스 CLI**: Core만 참조하는 `net10.0`(비-windows) 프론트엔드. 실제 cc 턴을 GUI 없이 실행 — Core가 진짜 WPF-프리임을 증명(phase a 완료).
- **cc 대용량 프롬프트 stall 근본수정**: init + 유저 메시지를 stdin에 함께 배칭하던 게 cc의 stream-json 리더를 초선형 지연시키던 원인 → init ack(control_response) 이후 유저 메시지를 전송하도록 시퀀싱(4분→수초). 대형 텍스트 문서는 경로 패스스루, gx/agy 대형 프롬프트는 파일 오프로드(명령행 한도 회피).
- **트랜스크립트 재동기화**: 세션을 터미널에서 이어 작업한 뒤, 그 cwd+엔진의 최신 엔진 대화에서 최근 tail을 불러와 GUI 트랜스크립트를 복원(오래된 시작이 아니라 최근 끝을 보여줌).
- **워크트리 라이브 브랜치 추적**: 에이전트가 브랜치를 바꾸면 GUI가 실제 현재 브랜치를 표시하고 머지도 실제 브랜치로 수행. 공유(비워크트리) 세션도 실제 브랜치 표시. 한 번 격리된 세션도 머지 후엔 라벨이 "메인 트리"로 자동 전환(라이브 격리 상태 반영).
- **모델 별칭 + 설정에서 세부버전 추가**: cc `sonnet`/`opus`/`haiku`가 항상 최신을 자동 추적(새 모델 나와도 패치 불필요) + `opus[1m]`(1M 컨텍스트). 설정에서 특정 버전(예: claude-opus-4-6)을 직접 추가 가능.
- **모델별 기본 추론 강도(effort)**: 엔진+모델에 맞는 스마트 기본값(cc Opus→medium 등) — 게이팅이 아니라 기본값이라 사용자가 언제든 변경 가능.
- **세션 UX**: 워커를 UI에서 리네임 + 이름만 지정한 세션 생성 · 전 엔진 "터미널에서 열기"(외부 콘솔 escape hatch) · 연속 stderr 줄을 한 블록으로 합치기(에러 덤프 벽 방지).

## 1.16.4
워커 동명 충돌 핫픽스.
- **워커 브랜치 고유화**: 같은 이름의 워커를 둘 이상 만들면 `worker/<이름>` 브랜치가 겹쳐 둘째 워커가 격리 워크트리를 못 받던(`git worktree add` → "already used by worktree") 문제 수정. 브랜치에 고유 세션 id 접미사를 붙여 분리(worktree 디렉토리는 이미 id 기반이라 무영향).

## 1.16.3
agy 구조화(SDK API 모드) · 렌더/선택지 견고화 · 워커 리포트 개선.
- **agy API 모드 (Antigravity SDK · 옵트인)**: agy를 인증모드로 전환 — 구독이면 기존 CLI(텍스트 전용), API 키면 Antigravity SDK를 Python 브리지로 구동해 **구조화 이벤트**(툴 호출·thinking·권한·스트리밍)를 표시. 설정의 subscription/api 토글(cc/gx와 동일). 라이브엔 `pip install google-antigravity` + Gemini API 키 필요(종량과금).
- **agy 터미널에서 열기**: agy 세션을 외부 터미널(Windows Terminal/cmd)에서 인터랙티브로 열어 풀 TUI·인증을 그대로 쓰는 escape hatch.
- **agy 라이브 스트리밍**: 응답을 턴 끝 한 덩어리가 아니라 실시간으로 누적 표시.
- **마크다운 렌더 견고화**: 번역이 줄 끝에 붙인 코드펜스(``` ```lang ```)나 짝 안 맞는 펜스가 메시지 뒷부분을 통째로 코드블록에 삼키던 문제 수정 — 글루된 오프너는 제 줄로 복원, 짝 없는 펜스는 건너뜀.
- **선택지 감지 정밀화**: 문서·중간 열거를 클릭형 선택지로 오탐하던 휴리스틱 강화 — 선택지는 메시지 끝에 와야 하고(end-anchor), cue는 옵션 앞에 있어야 인정.
- **트랜스크립트 복사**: 원문 토글이 켜진 상태면 번역문이 아니라 **원문**을 복사.
- **워커 리포트 수신함**: 카드별 삭제(dismiss) + 안 본 리포트 **NEW 배지** + "모두 읽음"; 복사 시 자동 읽음 처리.
- **실패 워커 태스크 재시도**: 타임아웃 등으로 실패한 태스크가 큐에 남아 ▶로 재시도 가능(완료 history에 묻히지 않음).

## 1.16.2
프로젝트-로컬 상태 저장소 · 오피스/PDF 첨부 · 첨부 UX 개선.
- **프로젝트-로컬 상태 (`<project>/.am/project.json`)**: 프로젝트의 세션 + 워커 백로그/큐가 머신-전역 `state.json` 대신 프로젝트 폴더에 저장됨 → 공유 드라이브/다른 머신에서 열어도 같은 세션·백로그가 보임. 전역 state.json은 프로젝트 목록·활성 id·설정만 보관. 기존 데이터는 1회 자동 마이그레이션(멱등). 워크트리 디렉토리는 머신-로컬 유지(없으면 비격리로 graceful 처리).
- **오피스/PDF 첨부 — 경로 패스스루 (+ agy 폴백)**: `.pdf .docx .xlsx .pptx` 등 바이너리 문서를 `<cwd>/.am/attachments/`로 복사하고 경로를 프롬프트에 참조 → 에이전트가 자기 툴로 읽음(cc/gx/pi 실측 확인). 텍스트=인라인, 이미지=네이티브 유지. agy(ConPTY 단발)·복사 실패 시 경고 블록 + 경로로 폴백(크래시 없음).
- **파일 드래그앤드롭 첨부**: 세션 화면에 파일을 끌어다 놓으면 첨부(이미지/문서 자동 판별). 텍스트 드래그는 입력창에 그대로.
- **프로젝트 우클릭 → 폴더 열기 + 경로 표시**: 컨텍스트 메뉴에서 탐색기로 열기 + 실제 경로 확인.
- **워커 백로그 수동 갱신(🔄)**: Orchestrator 헤더 버튼 — 태스크 스풀(`<project>/.am/worker-tasks/` + 전역)을 즉시 재스캔·인입(외부 등록을 재시작 없이 반영).
- **수정**: 셸 출력 등 `@`/`/`/`>`로 끝나는 텍스트를 붙여넣으면 서제스천 팝업이 떠 Enter 전송을 가로채던 문제 — 트리거 팝업은 새로 타이핑된 1글자에만 열리도록.

## 1.16.1
선택지 표시 번역 · 트랜스크립트 복사 전면 개선 · 슬래시 명령 재편.
- **선택지 표시 번역 + 헤더 토글**: 번역 토글을 선택지 패널 헤더(✕ 왼쪽)로 이동. ON이면 질문·옵션을 UI 언어로 번역해 표시(전송값은 원문 유지)·직접입력은 번역 전송, OFF면 원문 표시·그대로 전송. 토글 시 즉시 재번역/원복.
- **트랜스크립트 복사**: 표를 FlowDocument `Table`로 렌더 → 드래그 선택·Ctrl+C 가능(이전엔 UI 섬이라 복사 불가). 코드블록은 읽기전용 TextBox로 부분 선택 가능(복사 버튼은 전체). 위임(Delegation) 프롬프트·리포트·에러도 선택가능으로 통일 — 본문 요소 대부분 복사 가능.
- **전체 복사/내보내기 메타 헤더**: 맨 위에 Engine·Model·Reasoning·Exported(추출시각)를 붙여 컨텍스트 보존.
- **슬래시 명령 재편**: `/`는 엔진 몫 — cc는 커스텀 명령(`.claude/commands`) 파일 발견·자동완성, gx/agy/pi는 각 CLI `/help` 빌트인 카탈로그 노출(노이즈 큐레이션). 앱 액션(clear/review/settings/help)은 `>` 접두로 분리. ※ 비대화형 실행상 cc만 실제 확장, 그 외는 발견·자동완성·텍스트 패스스루.
- 수정: 위저드 중 "기타" 자유입력이 흐름을 끊던 문제 · 워크트리 미사용 시 유령 `agent/<slug>` 라벨을 "메인 트리 (공유)"로 일관화.

## 1.16.0
구조화 선택지(ask-user) — 빠른응답을 ask_user_input 스타일 패널로 재설계 + 멀티셀렉트·페이지네이션 · 워크트리 미사용 명확화.
- **ask-user 스킬 (전엔진 구조화 선택지)**: cc/gx/agy/pi의 skills 폴더에 `ask-user` 스킬을 주입(시작 시 자동). 모델이 `$AGENTMANAGER_ASK_SPOOL`에 `{question, options, multi}` JSON을 쓰면 앱이 감시·수집해 클릭형 선택지 패널로 렌더 — 휴리스틱 텍스트 파서로는 불가능한 신뢰성 있는 질문·옵션. 워커 태스크 스풀과 동일 아키텍처(세션별 `<cwd>/.am/ask/<sessionId>/`, agy ConPTY 포함). 휴리스틱 A/B/C 감지는 폴백 유지.
- **멀티셀렉트 + 페이지네이션 (ChoiceFlow)**: 단일선택 QuickReplies를 통합 `ChoiceFlow`로 교체. `"multi": true` → 체크박스 행 + N개 선택 푸터 + 엔진색 제출(Space 토글·Ctrl+Enter 제출). `"questions": [...]` → 페이저(‹ N/M)로 질문을 차례로 넘기고 모든 답을 한 턴으로 전송. 위저드 중 "기타" 자유입력도 현재 질문 답으로 기록 후 진행(흐름 안 끊김).
- **빠른응답 패널 재설계 (Claude 데스크톱식)**: 플랫 행 + 엔진 브랜드색 포커스 바·마커·체크박스 · ↑↓ 키보드 네비 + 마커 단축(1-9/A-Z) · Esc 직접입력 · 선택지가 뜨면 입력창을 내리고 인라인 "기타" 입력 노출. 선택지 색은 엔진 테마(cc 주황·gx 보라·agy 청록·pi 회색)를 따름.
- **New Agent "워크트리 미사용" 명확화**: 의미가 거꾸로 읽히던 "격리" 토글을 "워크트리 미사용" 체크박스로 교체(체크 = 워크트리 안 만들고 메인 트리 공유). opt-out 세션의 모든 표시(리스트 칩·헤더 브레드크럼·컴포저 칩·상태줄)를 유령 `agent/<slug>` 대신 "⊟ 메인 트리 (공유)"로 일관화.
- 수정: 구조화 선택지가 턴 종료 휴리스틱 패스에 지워지던 문제 · 제출 버튼 패딩(옹졸한 동그라미) · New Agent TASK 필드 멀티라인 입력.

## 1.15.0
권한/안전 모드 재설계 · New Agent 옵션 확장 · 추론 단계 전엔진 실측 정렬.
- **권한/안전 모드 칩 (engine-aware)**: 상단바의 APPROVAL 토글 + 샌드박스 콤보를 컴포저의 단일 색상 칩으로 통합. 엔진 네이티브 모드를 그대로 노출 — cc `Plan/Default/Bypass`(`--permission-mode`), gx `Read-only/Workspace-write/Full access`(`--sandbox`), agy·pi는 잠금 정적 배지(agy 항상 권한 스킵, pi 권한 개념 없음). **색=위험 그라데이션**(r0 청록·r1 초록·r3 빨강·rn 회색)으로 현재 모드를 한눈에. 기존 `Sandbox`+`RequireApproval` 위 뷰라 런 경로 불변.
- **New Agent 모달 확장**: 추론 수준 피커(cc/gx/pi), worktree 격리 토글(끄면 프로젝트 루트 공유), "워커로 생성" 토글(작업 없이 워커 풀에 미리 생성).
- **추론 단계 전엔진 실측 정렬**: cc `--effort`(low~max), gx `model_reasoning_effort`(none~xhigh), pi `--thinking`(off~xhigh) — 각 CLI/API로 공식 enum 검증. agy는 추론이 모델 label에 내장(`agy models`) → 모델 드롭다운에 변형(Flash Low/Medium/High 등) 노출.
- **승인 버튼 버그 수정**: 트랜스크립트 블록(승인·죽은세션삭제)이 SessionView(UserControl) namescope에서 렌더되는데 `ElementName=Root` 바인딩이 안 풀려 버튼이 먹통이던 문제 — `RelativeSource AncestorType=Window`로 수정 + 동일 클래스 전수 감사·하드닝.
- **트랜스크립트 간격 균일화**: 블록 종류별 상하 Margin 불일치(WPF는 인접 Margin 합산)로 간격이 12~24px로 들쭉날쭉이던 것을 전부 8/8로 통일(균일 16px).
- 정리: 레거시 density 문자열 제거(UI Zoom으로 대체됨).

## 1.14.2
워커 보고 라우팅 — 멀티 엔진 수정.
- **agy 스풀 미수신**: agy(ConPTY 경로)가 `AGENTMANAGER_TASK_SPOOL`을 못 봐서 워커-프롬프트 스킬이 agy 자체 스크래치(`~/.gemini/antigravity-cli/scratch`)에 써 백로그로 유입 안 되던 문제 — ConPTY 프로세스에 `ExtraEnvironment` 주입(`CREATE_UNICODE_ENVIRONMENT`).
- **보고 origin 붕괴**: worktree 없는 세션들이 cwd(`<project>/.am/worker-tasks/`)를 공유 → 워처가 dir 단위 dedup이라 origin이 첫 세션으로 몰려 **모든 워커 보고가 한 세션으로만** 가던 문제 — 스풀을 세션별 `<cwd>/.am/worker-tasks/<sessionId>/`로 분리(env + watch 일치). cc/agy/pi/gx 전 엔진 라이브 검증.
- **pi libuv assertion**: pi(node)를 턴 종료 후 강제 kill할 때 나오는 `UV_HANDLE_CLOSING` teardown assertion(출력은 이미 생성됨, 작업은 성공)을 benign stderr로 처리 — 빨간 에러로 안 뜨게.

## 1.14.1
- **gitignore**: Claude Code 에이전트 런타임 무시 — 서브에이전트 worktree(`.claude/worktrees/`)와 머신 로컬 설정(`.claude/settings.local.json`)이 repo에 커밋되지 않도록.

## 1.14.0
Claude Design 동기화(오케스트레이터·위임) + 워커 보고 라우팅 수정 + 영속화 견고화.
- **오케스트레이터 워커 큐 재디자인**: 백로그/워커별 큐를 디자인 갱신본에 맞춤 — per-engine 브랜드 배지 · "N 대기" pill · 실행 중 accent 강조 · ⚡큐 실행/▶실행 칩 · ↑↓ 끝에서 비활성 · 완료 기록(실패=err) · 할당 피커 엔진 아이콘.
- **워커 위임 UI 재디자인**: 인라인 `DelegationCard`(브랜드 배지 · in-flight accent 테두리 · 상태 색/아이콘 · 보고 green 패널) · 위임 모달 워커 행(EngineIcon 배지 · 번역/상태 칩) · 유휴 없음 모달의 "바쁜 워커" 목록.
- **워커 보고 라우팅 수정**: 스킬이 중앙 spool에 쓰면 origin이 유실돼 워커 보고가 원 세션 "보고 수신함"에 안 오던 버그 — `AGENTMANAGER_TASK_SPOOL`을 세션의 `<cwd>/.am/worker-tasks/`로 지정해 origin이 항상 박히게(라이브 검증).
- **영속화 견고화(P1~P4)**: `WriteAtomic` 일시적 파일잠금 재시도(백오프) · 저장 코얼레싱(500ms 디바운스 + 종료 시 flush) · 오프스레드 쓰기 · 세션별 결함 격리(한 세션이 깨져도 나머지 저장) · 연속 저장실패 시 비차단 경고 배너. 빈 `catch{}`의 **침묵 제거** → 실패를 `save-errors.log`에 기록.
- **삭제된 워커 작업 정리**: 워커 삭제 시 대기 작업은 백로그로 복귀·완료 기록은 삭제(유령 큐 카드 방지).

## 1.13.3
죽은 세션 정리 액션.
- **죽은 cc 세션 삭제 버튼**: claude가 `--resume` 대상 대화를 찾지 못하면(`No conversation found with session ID: …`) 그 세션은 영영 이어갈 수 없음. 이 stderr를 감지(현재 **cc 전용** — 다른 엔진은 시그니처가 달라 추후 케이스 추가)해 에러 블록에 안내문 + **`이 세션 삭제`** 버튼을 표시 → 클릭 시 그 자리에서 죽은 세션 제거(worktree·브랜치까지 정리). 같은 에러 반복 시 버튼 중복 방지.

## 1.13.2
보고 수신함 선택 UX 정리.
- **전체 복사 → 전체 선택**: 헤더의 `전체 복사` 버튼을 **`전체 선택`** 체크박스 토글로 교체 — 복사 경로를 "선택 → 선택 복사" 하나로 통일(두 "복사" 버튼 중복 제거). 전체 선택 후 일부만 해제하면 "이것만 빼고 복사"도 가능.
- **일괄 모드 일관성**: 보고가 하나라도 선택되면 카드 우상단 **개별 복사 버튼을 숨김** — 다중 선택 중 개별 복사 버튼이 "선택분 무시하고 1개?"로 읽히던 모호함 제거(빠른 단건 복사 vs 일괄 선택, 두 모드 분리).

## 1.13.1
세션 삭제 시 에이전트 브랜치 정리(버그성 수정).
- **세션 삭제 → 브랜치 정리**: 세션 삭제가 worktree만 제거(`git worktree remove --force`)하고 `agent/*` 브랜치는 repo에 남겨 **머지된 세션 브랜치가 무한정 누적**되던 문제 수정. worktree 제거 후 `git branch -d`(안전 삭제)로 정리 — **머지된 브랜치만 삭제, 미머지 커밋이 달린 브랜치는 보존**(작업 손실 없음). Smoke가 양쪽 경로(머지→삭제, 미머지→보존) 검증.

## 1.13.0
워커 태스크 큐 + 탭형 사이드 패인 + 보고 복사.
- **워커 태스크 큐**: 워커-프롬프트 스킬이 작업을 spool에 기록 → **백로그**로 자동 유입(중앙 spool + 실행 세션 cwd `.am/worker-tasks/` 동시 감시 — 환경변수가 에이전트 셸에 안 보여 폴백된 경우까지 커버). 도메인 전체를 Core `WorkerTaskStore`로 이전(백로그 + **워커별 큐** + 수명주기 backlog→assigned→running→done/failed, 토큰0 테스트). 백로그에서 **할당**(`+ 새 워커`로 유휴 워커 즉시 생성 — 첫 실작업이 첫 깨끗한 턴, 오염 턴 제거) → 워커별 큐 → **큐 실행**(순차 자동-진행, 워커 동시성 cap 준수) · ↑↓ 재정렬 · 완료 작업은 **완료 기록** 토글 아래로 숨김.
- **탭형 사이드 패인**: 우측 패인을 **Diff / 네이티브 작업자 / 보고 수신함** 탭으로 분리 — 네이티브 작업자 뷰가 Diff 영역을 밀어내던 문제 해소.
- **작업 보고 + 복사**: 워커 작업의 최종 응답을 보고로 캡처해 **오리진 세션의 "보고 수신함" 탭**으로 라우팅(OriginSessionId). 카드별 **복사** · **전체 복사** · 체크박스 **선택 복사(N)** 로 클립보드 수동 전달(주입 대신 복사 — 어디든 붙여넣기).
- Fixes: `claude agents --json` 폴러가 **형제 AgentManager 세션을 서브에이전트로 오인**하던 attribution 버그(UI 경계 필터) · 스킬→백로그 전달 누락(cwd 폴백 감시) · 큐 러너의 전역 워커 cap 무시 · 크래시 중단 작업 `running` stuck(시작 시 재조정) · 위임 done/failed stale 오판.

## 1.12.0
스킬 주입 + 마크다운 복사.
- **스킬 주입**(설정 → 스킬): 공용 `SKILL.md`(Agent Skills 오픈 표준, 기본=워커-프롬프트 작성 스킬)를 편집하고 엔진별 설치 폴더를 지정 → **Save settings 시 각 엔진**(cc·gx·agy·pi) 스킬 폴더에 `<폴더>/<이름>/SKILL.md` 기록(엔진별 ✓/✗ 표시). 기본 경로 자동 채움(cc `~/.claude/skills`, gx `~/.codex/skills`, agy `~/.gemini/antigravity-cli/skills`, pi `~/.pi/agent/skills`), 폴더를 비우면 그 엔진은 건너뜀.
- **마크다운 코드블록 복사**: 코드블록(예: 워커 프롬프트)에 원클릭 **복사 버튼** 추가 — 기존엔 FlowDocument 선택이 코드블록 내부에 닿지 못해 복사가 아예 불가했음. 내부에 ``` 가 든 프롬프트도 한 덩어리로 유지되도록 **가변 길이 fence** 파싱.

## 1.11.0
- **크래시 처리**: 처리되지 않은 예외(UI 스레드/AppDomain) 발생 시 **오류 로그 팝업**(예외 요약 + `crash.log` 경로)을 띄우고 종료. 이전엔 UI 예외를 조용히 흡수하거나 치명 예외 시 대화상자 없이 사라졌음. (awaiter 없는 백그라운드 Task 예외는 크래시가 아니므로 기록만 유지.)

## 1.10.0
첨부·번역 UX 개선 + 컴포저 수정.
- **문서 첨부**: 이미지 외 마크다운/텍스트/코드 파일도 첨부 — 내용을 펜스 코드블록으로 프롬프트에 인라인(번역 이후 prepend라 손상 없음), **전 엔진** 동작. 이미지 첨부는 **실제 썸네일** 미리보기.
- **빠른-응답 버튼**: 어시스턴트 메시지가 `A)/B)` · `1./2.` 선택지로 끝나면 컴포저 위에 **원클릭 버튼** 표시 → 클릭 시 전송. 정규화 전사 텍스트 파싱이라 엔진·언어 무관(번호 목록은 질문 목록 오탐 방지).
- **메시지 재번역**: 어시스턴트 메시지마다 **↻ 재번역** 버튼 — 번역이 이상하거나 안 됐을 때 그 메시지만 원문 다시 번역. 전용 번역(languages) 아이콘.
- **번역 스킵 수정**: 영어 응답에 섞인 소수 한글(이름·경로 등)로 메시지 **전체 번역이 스킵**되던 버그 — 글자 비율(≥50%)로 판정.
- **Enter 전송**: 엔터=전송, Shift+엔터=줄바꿈(엔터가 줄바꿈만 되던 문제 수정).
- Fixes: Orchestrator 카드·사이드바에서 **이미 활성인 세션 "열기"가 안 되던** 버그, 세팅창 닫기 버튼 위치·가독성.

## 1.9.0
인앱 업데이트.
- **업데이트 확인**: About 모달 버전 옆 `업데이트 확인` 버튼이 GitHub 태그(`Bo-sung/AgentManager`)에서 최신 버전을 조회해 실행 버전과 비교. 최신이면 "최신 버전입니다", 새 버전이면 "vX.Y.Z 사용 가능" + [변경 내역]·[업데이트] 버튼.
- **별도 업데이터 프로세스**(`scripts/update.ps1`): `업데이트` 클릭 시 앱을 종료(exe 잠금 해제)하고, 업데이터가 PID 종료를 기다린 뒤 현재 브랜치 FF `git pull` → 변경 시 재빌드(publish.ps1) → 재실행. **소스 체크아웃에서 실행할 때만** 활성화. dist/·bin/은 gitignore라 실행 중 바이너리와 pull 충돌 없음.

## 1.8.0
4번째 엔진 **Pi(pi.dev)** 추가 + 엔진별 모델 큐레이션 + 테마 확장.
- **Pi 엔진**: pi.dev를 thin-proxy(RPC 모드)로 통합 — 멀티 provider(Anthropic/OpenAI/Google/zai 등) 하나의 엔진으로 사용. 공식 로고/색, RPC 이벤트→정규화 매핑(thinking/text/tool/usage), resume. provider·인증은 pi가 자체 관리(`~/.pi`), 앱은 호출+표시만.
- **동적 모델 카탈로그**: `pi --list-models`로 실모델 목록 조회(설정 "조회") + 연동 provider 표시. (실측: docs/PHASE0_PI_RPC_KO.md)
- **"주로 쓰는 모델" 체크리스트**(전 엔진, 접이식): 체크한 모델만 New Agent 피커·설정 드롭다운에 노출. 선택은 settings.json에 영속.
- **테마**: Claude Dark · Codex Light · Antigravity Light 추가(총 13종). 엔진 식별색을 **테마·강조색과 완전 독립**으로 고정(아이콘·텍스트·컴포저 외곽·알약). 타이틀바/메뉴바가 테마를 추종(라이트에서 안 보이던 문제 fix). agy 컴포저 입력 하이라이트 무지개(Google 4색). 라이트 3종 텍스트 대비 강화.
- 내부 리팩터: 엔진 어댑터 공통화(StdioJsonAdapter·AdapterJson), Shell.Open, JsonFile 스토어 IO(동작무변, Smoke 검증).

## 1.7.2
- **설정 런타임 카드**에도 공식 엔진 아이콘 적용(CC/GX/AG 텍스트 배지 → 로고).
- **엔진 아이콘 색 고정**: 강조색/테마를 바꿔도 각 모델 아이콘 색 유지(Claude 주황·Codex 보라). 이전엔 Claude가 강조색에 묶여 같이 변하던 버그 수정.
- **Antigravity 아이콘** 공식 CI 무지개 그라데이션(파랑→초록→빨강→주황)으로 변경.

## 1.7.1
- **공식 엔진 아이콘**: Claude(공식 심볼) · Codex(OpenAI 마크) · Antigravity(공식 "A" 마크의 단색 재현) 적용.
- README "로드맵" 섹션 → "변경 이력"(CHANGELOG 요약/링크)으로 교체.

## 1.7.0
엔진 가용성("사용 불가") 처리 + 번역 Ollama 연동 강화.
- **엔진 설치 게이팅**: New Agent 피커에서 미설치 엔진은 회색 + "미설치" 배지 + 선택 불가, 옆에 공식 설치 가이드 링크.
- **설치 & 세팅 가이드 모달**: 설정 → 런타임 "가이드" 버튼 → Markdown 렌더(테마 매칭). 엔진별 설치·연결 + Ollama 안내.
- **한도 소진 처리**: 구독 한도 도달 시 (opt-in) 저장된 API 키로 자동 전환, 끄면 해당 엔진 회색("한도 초과"). 판정 = 사용량 100% 또는 실제 rate-limit 실패.
- **번역 Ollama 연동**: 번역 ON은 Ollama 실행 중일 때만 적용(실행 시 핑 게이팅). 설정에 Ollama 상태(실행/꺼짐/미설치) + [실행](`ollama serve`)/[설치 가이드]. 꺼짐 시 번역 토글 옆 ⚠(클릭→설정) + 켜기 시도는 OFF로 되돌림.
- **번역 토글 이동**: 세션 헤더 → 컴포저(모델/effort 옆), New Agent 폼에도 번역 토글 추가.
- **CLI 세션 삭제 영속**: 삭제한 CLI 기록이 재시작 후 재발견으로 되살아나지 않음(dismiss 셋).
- Fixes: 중복 문자열 키 기동 크래시, ⚠ 아이콘 렌더, Ollama `localhost`→IPv4 탐지, editable ComboBox 선택(드롭다운).

## 1.6.0
사용량 표시 개선.
- 사용량 체크를 **엔진별**로 표시(활성 1개 → 전 엔진), **퍼센트 막대**(Claude 세션/주간, Codex 사용%, Ok/Warn/Err 색).
- "공식 수치가 아닌 대략적 추정치" 안내 문구. Antigravity는 무료 프리뷰(N/A).

## 1.5.1
- 사용량 체크 크래시 가드 — 전역 예외 핸들러 + 자식 CLI 오류 대화상자(WER) 억제.

## 1.5.0
- 브랜드 테마 3종(Claude · Codex · Antigravity) + 커스텀 강조색(hex).
- 엔진 경로 수동 설정 + 탐지 버튼(독립 설치 우선, Codex npm 우선).
- 번역·언어 설정 통합 + 번역 모델 드롭다운/설치 모델 조회.
- 창 최대화 상태 복원 fix.

## 1.4.0
- UI 줌(Ctrl+휠) — 본문/모달 독립 배율, 활성 영역만 조정.

## 1.3.0
- 워커 위임(메인↔워커 핸드오프) — 지속 풀 · 위임 모달 · 보고 수신함 · 일괄 fan-out · 크로스 엔진.

## 1.2.0
- 언어 설정 드롭다운화 + 설정 가능한 번역 언어 쌍(번역 전/후, 11개 언어).

## 1.1.0
- IDE 테마 프리셋 + 라이브 전환, VS Code식 settings.json(분리·라이브 리로드), 엔진 명칭/설정 재편, API 키 인증(DPAPI).

## 1.0.0
- 첫 릴리즈: 멀티 에이전트(Claude Code · Codex · Antigravity) 구동 · worktree 격리 · Review pane · 승인 broker · 로컬 LLM 번역 · 트랜스크립트 영속.
