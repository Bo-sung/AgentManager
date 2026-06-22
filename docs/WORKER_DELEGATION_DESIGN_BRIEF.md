# 디자인 핸드오프 — 워커 위임 팝업

> `design/`(JSX 프로토타입: `am-app.jsx` 등) 환경에 붙여넣을 프롬프트 + 상세 스펙.
> 기존 디자인 시스템을 **그대로** 따른다.

---

## 디자인 시스템 (기존 토큰/클래스 재사용)

- 모달 골격: `.overlay`(백드롭, 클릭 시 닫힘) → `.modal`(클릭 stopPropagation) → `.modal-h` / `.modal-b` / `.modal-f`.
- 헤더: `.lbl`(작은 캡션) + 제목(`var(--txt-0)`, 600, 13px) + 우측 `.x` 닫기 버튼(`Icon name="x"`).
- 필드: `.field` + `.lbl` + 입력. 가로배치는 `.modal-row`(flex). 텍스트 입력 `.modal-input`.
- 드롭다운: `.select-pill`(아이콘 + 값 + `chevdown`) → 펼치면 `.menu` / `.menu-item`(선택 시 `check`).
- 엔진 그리드: `.agent-grid` → `.agent-opt`(`.sel`), 내부 `.badge`(엔진색 `a.tint/a.line/a.soft`) + `.nm` + `.ds`.
- 버튼: 푸터 좌측 `.btn-ghost`(취소류), 우측 `.btn-primary`(주액션, 아이콘+텍스트).
- 색 변수: `--txt-0/1/2/3`, `--run`(실행), `--warn`(대기), `--ok`(완료), `--err`(실패), `--accent`. 엔진색은 `AGENTS[id].tint/line/soft/badge`.
- 상태칩: `StatusDot`/`.dot.<status>`(`spin`은 실행중). 라벨은 `STATUS_LABEL`.

레퍼런스: `NewAgentModal`(am-app.jsx:150) — 이 팝업들은 그 변형이다.

---

## 만들 것: 팝업 3종 (모두 JSX 컴포넌트로)

### A. WorkerAssignModal — 워커 할당(위임) 팝업
메인 응답 블록의 `▸ 워커로 위임` 버튼에서 열린다. 위임 대상 워커 선택 + 위임 프롬프트 확정.

**modal-h**: lbl="Delegate" · 제목="워커에 위임 (Delegate to worker)" · `.x`

**modal-b**
1. **field "워커(Worker)"** — 프로젝트 풀의 워커 리스트(가로 `.agent-grid` 또는 세로 리스트):
   - 각 워커 카드: 엔진 `.badge` + 이름(`.nm`) + 모델(`.ds`) + **상태칩**(idle=`--ok`/busy=`--run` spin) + **번역 정책 칩**(예: `TR ON · KO→EN` 또는 `TR OFF`, 작은 pill, `--txt-3`).
   - busy 워커는 흐리게(선택 비활성) 또는 "사용 중" 배지.
   - 맨 끝 **`+ 새 워커`** 옵션(점선 테두리 `.agent-opt`) → 누르면 새 워커 생성 행 펼침(아래 2번).
2. **새 워커 생성(선택 시 펼침)** — `NewAgentModal` 필드 차용:
   - Runtime(`.agent-grid` 엔진 선택) · Model(`.select-pill`+`.menu`) · 이름(`.modal-input`)
   - **번역 정책(고정값)**: 토글 `번역 ON/OFF` + (ON일 때) `번역 전 언어`/`번역 후 언어` `.select-pill` 2개. *주: 이 값은 워커에 고정됨* 안내 캡션.
   - **행동 규칙(preamble)**: `.modal-input` textarea(3~5줄), 전역 기본 템플릿 prefill, 편집 가능. *주: 위임 프롬프트 앞에 자동 부착되며 워커에 고정* 캡션.
3. **field "위임 프롬프트"** — `.modal-input`(textarea, 4~6줄). 메인 선택 텍스트로 prefill, 편집 가능.
4. **field "worktree"**(작게) — `.select-pill`로 `공유 / 독립` 선택(기본값은 추후 확정, placeholder로 "독립").

**modal-f**: 좌측 체크박스 `완료 시 보고 자동 주입` · 우측 `.btn-ghost`[취소] + `.btn-primary`[위임 보내기 / Delegate] (아이콘 `bolt`).

상태: 선택된 워커 없으면 위임 버튼 비활성.

### B. NoIdleWorkerModal — 유휴 워커 없음 팝업
`▸ 워커로 위임` 시점에 유휴 워커가 하나도 없을 때 A 대신 먼저 뜬다.

**modal**(좁게, width≈380)
- **modal-h**: lbl="Workers" · 제목="유휴 워커 없음 (No idle worker)" · `.x`
- **modal-b**: 본문 텍스트 — "현재 모든 워커가 사용 중입니다. 새 워커를 생성할까요?" (`--txt-2`). 사용 중 워커 N개를 작은 상태칩 리스트로 표시(선택).
- **modal-f**: `.btn-ghost`[닫기](팝업만 닫음, 위임 취소) + `.btn-primary`[생성](→ WorkerAssignModal를 *새 워커 생성* 상태로 연다).

### C. DelegationCard — 메인 트랜스크립트 위임 카드 (모달 아님, 인라인)
메인 응답 흐름에 들어가는 접힘/펼침 카드.
- 헤더: 워커 `.badge` + "워커 <name> · <model>에 위임" + **상태**(전송됨/실행중 spin/완료 `--ok`/실패 `--err`) + 펼침 chevron.
- 펼침 시: 위임 프롬프트(접힘 가능) + 보고 미리보기(완료 시) + 링크 `워커 세션 열기`.
- 우하단 액션:
  - 완료(ready): `.btn-primary`[보고 붙여넣기].
  - 실패: 에러 요약 + `.btn-ghost`[다시 위임].
- 메인 컴포저 근처에 **"보고 수신함" 배지**(ready 건수) + ready≥2면 `합쳐 붙여넣기(N건)` 버튼.

---

## D. 추가 디자인 표면 (결정 반영 — 팝업 외)

이번 결정들로 팝업 3종 외에 아래 표면도 디자인 필요:

1. **사이드바 `WORKERS` 그룹** (am-sidebar.jsx) — 활성 프로젝트 세션 목록에 별도 그룹. 각 워커 행: 엔진 `.badge` + 이름 + 모델(`.ds`) + 상태칩(idle/busy) + **담당 메인** 라벨(`↳ <main name>`, `--txt-3`). 그룹 헤더에 `+ 새 워커`. 우클릭/호버에 삭제. 일반 세션과 시각적으로 구별(역할 강조색/배지).
2. **Settings 추가** (am-settings.jsx):
   - **Orchestration** 섹션에 `워커 동시 실행 수(MaxConcurrentWorkers)` 숫자 필드(기존 동시 실행 cap 옆).
   - **Workers** 카드(신규): **전역 행동 규칙 preamble 템플릿** `textarea`(기본값 prefill) — 새 워커 생성 시 이 값으로 시작.
3. **메인 채팅 응답 블록** (am-chat.jsx) — 에이전트 응답 헤더(복사 `⧉` 옆)에 **`▸ 워커로 위임`** 액션 추가(본문 선택 시 선택 텍스트 위임).

## 디자인에 넘길 프롬프트 (복붙용)

```
AgentManager 디자인 프로토타입(design/am-*.jsx, 기존 .overlay/.modal/.modal-h/-b/-f,
.field/.lbl/.modal-input/.select-pill/.menu/.agent-grid/.agent-opt/.btn-ghost/.btn-primary,
CSS 변수 --txt-0..3/--run/--warn/--ok/--err/--accent, AGENTS[id].tint/line/soft/badge)을
그대로 따르는 "워커 위임" UI를 추가해줘. NewAgentModal(am-app.jsx)의 변형으로 만들 것.

만들 컴포넌트 3개:

1) WorkerAssignModal — 메인 응답의 '워커로 위임' 버튼에서 열림.
   - 워커 선택 리스트(엔진 배지+이름+모델+상태칩 idle/busy+번역정책 칩 'TR ON KO→EN'/'TR OFF'),
     busy 워커는 비활성.
   - '+ 새 워커' 선택 시 생성 폼 펼침: Runtime/Model/이름 + 번역 정책(ON/OFF 토글 + 번역 전/후 언어
     select-pill, '이 값은 워커에 고정' 캡션) + 행동 규칙(preamble) textarea(기본 템플릿 prefill,
     '위임 프롬프트 앞에 자동 부착·워커 고정' 캡션) + worktree 공유/독립 select.
   - '위임 프롬프트' textarea(메인 텍스트 prefill, 편집 가능).
   - 푸터: 좌측 체크박스 '완료 시 보고 자동 주입', 우측 [취소]/[위임 보내기](bolt 아이콘).
     워커 미선택 시 위임 버튼 비활성.

2) NoIdleWorkerModal — 유휴 워커가 없을 때 1) 대신 먼저 뜨는 좁은 모달(width~380).
   본문 '모든 워커 사용 중, 새 워커 생성?' + 사용중 워커 상태칩 리스트.
   푸터 [닫기](닫기만)/[생성](WorkerAssignModal을 새 워커 생성 상태로 오픈).

3) DelegationCard — 메인 트랜스크립트 인라인 카드(모달 아님).
   헤더: 워커 배지+'워커 <name>·<model>에 위임'+상태(전송됨/실행중 spin/완료/실패)+펼침.
   펼침: 위임 프롬프트+보고 미리보기+'워커 세션 열기' 링크.
   액션: 완료 시 [보고 붙여넣기], 실패 시 에러요약+[다시 위임].
   메인 컴포저 옆 '보고 수신함' 배지(ready 건수), ready≥2면 [합쳐 붙여넣기(N건)].

또한 팝업 외 표면도 같은 스타일로 추가:
- 사이드바(am-sidebar.jsx)에 'WORKERS' 그룹: 워커 행(배지+이름+모델+idle/busy 칩+'↳ 담당메인'),
  그룹 헤더 '+ 새 워커', 일반 세션과 구별되는 역할 강조.
- Settings(am-settings.jsx): Orchestration에 'MaxConcurrentWorkers' 숫자 필드,
  신규 'Workers' 카드에 전역 행동 규칙 preamble textarea.
- 메인 채팅 응답 헤더(am-chat.jsx, 복사 아이콘 옆)에 '워커로 위임' 액션.

샘플 데이터로 idle 1 + busy 1 워커, ready 보고 2건 상태를 보여줘.
다크/라이트 등 기존 테마 변수만 쓰고 하드코딩 색 금지.
```
