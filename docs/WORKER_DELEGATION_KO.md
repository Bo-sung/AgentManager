# 워커 위임 (Worker Delegation) — 설계 문서

> 상태: **설계(합의 전)** · 대상 마일스톤: 멀티에이전트 파이프라인/Handoff(P2)의 첫 실사용 형태
> 결정된 방향: **트리거 = 수동 UI 버튼(반자동)** · **워커 수명 = 지속 풀 + 사용자 관리**

---

## 1. 배경 / 문제

대형 모델(예: Opus) 하나가 자잘한 작업까지 전부 처리하면 토큰 소모가 크다. 그래서 사용자는 다음 패턴을 **수동으로** 운영해 왔다:

- **메인 세션** = 대형 모델. 계획·조율·통합만 담당, 워커에게 줄 **프롬프트만 작성**.
- **워커 세션** = 소형/저가 모델. 메인이 지시한 단순 작업을 실행.
- 현재 수작업 루프: 메인이 출력한 프롬프트를 사용자가 **복사 → 워커 세션에 붙여넣기 → 실행 → 워커 보고를 복사 → 메인에 붙여넣기**.

이 복붙 왕복을 AgentManager가 대신 처리하게 한다.

## 2. 목표 / 비목표

**목표**
- 메인 세션 출력에서 한 번의 버튼으로 워커에게 **위임 프롬프트 전송**.
- 워커 완료 시 **보고를 메인에 한 번의 버튼으로 복귀**(메인의 다음 입력으로 주입).
- 워커는 **지속 풀**로 유지(메인과 별도 모델/엔진), 사용자가 풀을 **생성·삭제·모델 변경·재사용** 관리.
- 메인/워커 **비용을 분리 집계**, 위임/보고 내역을 메인 트랜스크립트에 가시화.

**비목표(이번 단계)**
- 모델이 스스로 위임 시점을 판단하는 완전 자동(=MCP 위임 도구)은 **다음 단계**로 둔다(§10). 단, 하부 엔진은 공유하도록 설계.
- 다단계 파이프라인/DAG, 워커→워커 재위임, 병렬 fan-out 자동 조율은 범위 밖.

## 3. 개념 & 용어

| 용어 | 의미 |
|------|------|
| **메인 세션(Main)** | 대형 모델 세션. 위임을 *발신*하고 보고를 *수신*. |
| **워커 세션(Worker)** | 소형 모델 세션. 위임 프롬프트를 실행하고 보고를 생성. |
| **워커 풀(Worker Pool)** | 재사용 가능한 워커 세션 집합. 사용자가 관리(생성/삭제/모델·엔진 지정). |
| **위임(Delegation)** | 메인 → 워커로 보낸 1건의 프롬프트. |
| **보고(Report)** | 워커가 위임에 대해 출력한 최종 결과(최종 `AgentTextBlock`). |

세션은 새 속성 **Role ∈ {Main, Worker, Plain}** 으로 구분한다(`Plain` = 기존 일반 세션, 기본값).

## 4. 사용자 흐름 (수동 버튼 · 반자동)

1. 사용자가 메인 세션에서 평소처럼 작업 → 메인이 "이 작업은 워커에게" 하는 **프롬프트를 출력**(또는 메인 응답 일부를 위임 대상으로 선택).
2. 해당 메인 응답 블록(`AgentTextBlock`)에 **`▸ 워커로 위임`** 버튼. 누르면:
   - 워커 선택 팝업: 풀의 워커 중 하나 선택(또는 *새 워커* 즉석 생성, 모델/엔진 지정).
   - 위임 프롬프트 미리보기·편집 후 전송.
3. AgentManager가 선택한 워커 세션에 프롬프트를 `RunTurnAsync`로 실행(지속 워커면 `--resume`로 같은 워커 재사용).
4. 워커 진행은 기존 **Live workers/세션 UI**로 관측. 메인 트랜스크립트엔 *"워커 #k에 위임함"* 표식(접힌 카드)이 남는다.
5. 워커 완료 → 메인 트랜스크립트의 위임 카드가 **`▸ 보고를 메인에 붙여넣기`** 상태로 전환. 누르면 워커의 최종 보고가 메인의 **다음 입력(Draft)** 에 주입(자동 전송 옵션 가능).
   - "자동 복귀" 토글을 켜면 4→5가 자동 연결(완료 시 보고를 메인 입력으로 바로 주입).

> 반자동인 이유: 트리거가 사용자 클릭이라 안전(의도치 않은 토큰 소모 방지). 추후 §10에서 모델 주도 자동으로 승격.

## 5. 아키텍처 매핑 (기존 코드 재사용)

이미 있는 빌딩블록으로 대부분 구성된다:

| 필요 동작 | 재사용 지점 |
|-----------|-------------|
| 워커 세션 생성(엔진·모델 지정) | `CreateSession()` 패턴 → 파라미터화한 `CreateWorkerSession(engine, model, project)` 추출 ([AppViewModel.cs](../src/AgentManager/ViewModels/AppViewModel.cs)) |
| 코드에서 턴 실행 | `RunTurnAsync(s, prompt, images?)` — 이미 UI 밖에서 호출 선례 있음([AppViewModel.Usage.cs](../src/AgentManager/ViewModels/AppViewModel.Usage.cs)) |
| 워커 보고 추출 | 워커 턴 종료 시 마지막 `AgentTextBlock`(최종 `AssistantText`) 캡처 ([Blocks.cs:36](../src/AgentManager/ViewModels/Blocks.cs), [AppViewModel.Run.cs:430](../src/AgentManager/ViewModels/AppViewModel.Run.cs)) |
| 멀티턴 재사용 | 단발+resume 구조 — 워커에 `EngineSessionId` 보관 후 재위임 시 resume |
| 워커 가시화 | 관측 레이어 + `WorkItemKind.AgentManagerWorker`/`ManagedByAgentManager`(이미 enum 자리 있음, [ObservedWorkItem.cs](../src/AgentManager.Core/Observation/ObservedWorkItem.cs)) |
| 격리 | 워커도 세션이므로 자체 worktree 격리 그대로 적용(또는 메인 worktree 공유 옵션, §9) |

핵심: **워커도 평범한 `SessionViewModel` + `RunTurnAsync`**. 위임은 "메인의 한 응답을 워커의 입력으로, 워커의 보고를 메인의 입력으로 연결"하는 **얇은 오케스트레이션 레이어**일 뿐이다.

### 오케스트레이션 타입(신규)
```
DelegationCoordinator
  - StartDelegation(main, worker, prompt) : DelegationId
  - 워커 RunTurnAsync 완료 감시 → 최종 보고 추출
  - CompleteDelegation(id, report) → 메인 트랜스크립트 카드 갱신 + (옵션) 메인 입력 주입
Delegation(record): Id, MainSessionId, WorkerSessionId, Prompt, Report?, State, Cost
```

## 6. 데이터 모델 변경

- `SessionViewModel`에 추가: `Role`(Main|Worker|Plain), `PoolId?`(워커 풀 소속), 워커의 `ManagedByMainSessionId?`.
- `AppSettingsDto` / `state.json`: 워커 풀 정의(워커 세션 id·모델·엔진·이름) + 세션 Role 영속.
- `Delegation` 이력: 세션별 위임/보고 내역(메인 트랜스크립트 카드 복원용). 트랜스크립트에 새 블록 `DelegationBlock`(메인 측, 접힘/펼침 + 상태 + 보고 미리보기) 추가.
- 비용: 세션별 `CostUsd`는 이미 존재 → 메인 카드에 "이 위임 비용(워커)"을 별도 표기, 풋터 총비용은 메인+워커 합산이되 라벨 구분.

## 7. UI 변경

- **메인 응답 블록**: `AgentTextBlock`에 `▸ 워커로 위임` 액션(응답 헤더의 복사 `⧉` 옆). 블록 본문 일부 선택 후 위임도 허용.
- **워커 선택 팝업**: 풀 워커 리스트(모델·상태) + *새 워커* (엔진/모델 피커 재사용, [New Agent 모달]).
- **DelegationBlock(메인 트랜스크립트)**: "워커 #k · <model> 에 위임" 카드 — 상태(전송됨/실행중/완료/실패), 보고 미리보기, `보고 붙여넣기` 버튼, `워커 세션 열기` 링크.
- **워커 풀 관리 패널**: 사이드바 또는 Settings에 "Workers" 섹션 — 워커 생성/삭제/이름·모델 변경, 소속 메인 표시, 유휴/사용중 상태. (Live workers strip와 구분: 그건 *네이티브 서브에이전트* 관측, 이건 *AgentManager 소유 워커*.)
- **자동 복귀 토글**: 위임 시 "완료되면 보고를 메인에 자동 주입" 체크.

## 8. 비용 · 번역 · 관측 상호작용

- **비용**: 메인=대형/워커=소형이 핵심 동기 → 위임 카드와 풋터에서 **메인/워커 비용을 분리 표기**. 절감 효과가 한눈에 보이게.
- **번역**: 위임 프롬프트·보고는 **에이전트 간 통신**이므로 기본은 **번역하지 않고 원문(영어 권장) 그대로** 주고받아 토큰·왜곡을 줄인다. 사용자가 화면에서 읽을 때만 표시용 번역(기존 ORIGINAL 토글) 적용. (= 위임 경로는 `번역 후 언어`로 통일하는 옵션.)
- **관측**: 워커 세션을 `WorkItemKind.AgentManagerWorker`로 표면화해 메인의 워커 활동을 한 곳에서 추적.

## 9. 엣지 케이스 / 결정 필요

- **워커가 사용 중**: 지속 풀 워커에 위임이 겹치면 → 큐잉 vs 거부 vs 새 워커 생성. (기본: 큐잉)
- **동시 실행 cap**: 메인+워커가 `MaxConcurrentSessions`를 같이 소비 → 워커 위임이 cap에 막힐 수 있음. 위임용 별도 예약 슬롯 검토.
- **worktree**: 워커가 메인과 **같은 코드**를 만져야 하면 메인 worktree 공유, 단순 조회/생성 작업이면 자체 worktree. → 위임 시 "worktree: 공유/독립" 선택.
- **실패/타임아웃**: 워커 실패 시 보고 대신 에러 요약을 메인 카드에 표기, 메인엔 주입하지 않음(사용자 확인).
- **보고 추출 정확도**: 최종 `AgentTextBlock` 1개로 충분한가, 아니면 워커 턴 전체 요약이 필요한가. (기본: 최종 응답, 옵션으로 전체.)

## 10. 단계적 구현

**MVP (이번 합의 대상)** — 수동 버튼 + 지속 풀
1. 데이터 모델: `SessionViewModel.Role/PoolId`, `Delegation`/`DelegationBlock`, 풀 영속.
2. `CreateWorkerSession` 추출 + `DelegationCoordinator`.
3. UI: 메인 블록 위임 버튼 → 워커 선택 → 전송, 워커 완료 → 보고 붙여넣기.
4. 워커 풀 관리 패널 + 메인/워커 비용 분리 표기.

**확장 1** — 자동 복귀 토글(완료 시 보고 자동 주입), 위임 큐잉.

**확장 2 (다음 마일스톤)** — **MCP 위임 도구**: AgentManager가 `delegate(prompt, model?, engine?)` MCP 도구를 노출 → 메인 모델이 *스스로* 호출하면 워커가 돌고 보고가 **tool result로 자동 복귀**. MVP의 `DelegationCoordinator`·워커 풀을 그대로 재사용하고, 트리거만 "버튼"에서 "모델의 도구 호출"로 바뀐다. (Claude·Codex 모두 MCP 지원, 패스스루 이미 배선됨 [ClaudeAdapter.cs:50](../src/AgentManager.Core/Agents/ClaudeAdapter.cs).)

## 11. 미해결 결정 (합의 필요)

1. 워커 풀 위치 — 사이드바 전용 섹션 vs Settings vs 프로젝트별.
2. 위임 경로 번역 정책 — 무번역(원문) 기본 vs `번역 후 언어` 강제.
3. worktree 공유/독립 기본값.
4. 워커 busy 시 정책(큐/거부/새 워커).
5. 동시성 cap에서 워커 슬롯 분리 여부.

## 12. 로드맵 관계

- ROADMAP의 **멀티에이전트 파이프라인/Handoff(P2)** 의 첫 구체화. 데이터 모델 자리(`AgentManagerWorker`/`ManagedByAgentManager`)는 이미 마련됨.
- 완료 시 PROGRESS/ROADMAP/README(§기능) 갱신 — *master 병합 전 마지막 커밋에서 README 반영* 규칙 적용.
