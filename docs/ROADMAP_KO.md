# AgentManager 기능 로드맵 & 계획

> 비전: **여러 코딩 에이전트(Claude/Codex/…)를 한 곳에서 구동·관리하고, 로컬 LLM 번역으로
> 한글로 일하면서 토큰을 아끼는** 데스크톱 매니저. IDE가 아님.

상태: ✅ 완료 · 🟡 부분 · ⬜ 미구현 / 난이도: S(소) M(중) L(대)

---

## 1. 기능 목록 (카테고리별)

### A. 세션 수명주기
- ✅ 세션 생성 (New Agent)
- 🟡 다중 세션 동시 실행
- ✅ 실행 중지/취소 (Stop) — S
- ⬜ 멀티턴 대화(세션 연속성) — M
- ⬜ 삭제 / 보관(archive) / 이름변경 — S
- ⬜ 복제 / 분기(fork) — M
- ⬜ 동시 실행 개수 제한(concurrency cap) — S

### B. 엔진/권한 제어
- ✅ 엔진 선택
- 🟡 모델 선택(표시만 → 실제 `--model` 연결) — S
- ⬜ 승인 프롬프트 UI(approve/reject) — M *(Claude 우선, Codex는 제약)*
- ⬜ 권한/샌드박스 모드 선택(read-only/workspace-write/yolo) — S
- ✅ 작업 디렉터리/worktree 세션별 지정 — M
- ⬜ Antigravity 어댑터 — M *(전환 후)*

### C. 번역 레이어 (차별점)
- 🟡 KO↔EN (기본 ON, UI 토글 없음)
- ⬜ 번역 토글 + "번역 중..." 인디케이터 — S
- ⬜ 메시지별 원본/번역 보기 — S
- ⬜ 번역 설정(모델/엔드포인트) 패널 — S
- ⬜ 서브에이전트 결과 번역 UI 확인 — S

### D. 관측/대시보드
- 🟡 세션 상태 / 🟡 토큰 / 🟡 할당량
- ⬜ 경과 시간 라이브 타이머 — S
- ⬜ 비용(total_cost_usd) 표시 — S
- ⬜ 전체 집계(총 토큰/비용/실행수) — S
- ⬜ 데스크톱 알림(완료/승인대기) — M

### E. 트랜스크립트 렌더링
- ✅ 유저/에이전트/툴/에러 블록
- ⬜ 마크다운(**굵게**/`코드`/목록/코드블록) — M
- ⬜ thinking 블록 — S
- ⬜ 스트리밍 타이핑 효과 — S
- ⬜ 메시지 액션(복사/재실행/IDE 열기) — S
- ⬜ 리뷰 드로어 diff 렌더링 + Keep/Discard — L

### F. 영속성/설정
- ✅ 세션 저장/복원(재시작 생존) — M
- ⬜ 설정 저장(엔진 경로/Ollama/번역 기본값/cwd) — S
- ⬜ 설정 패널 UI — M

### G. 마무리/UX
- ⬜ 사이드바 Active/Project 그룹화 — S
- ⬜ 입력 첨부(이미지) / @멘션 / 슬래시 — M
- ⬜ 세션 검색·필터 — S
- ⬜ 키보드 단축키 / 창 상태 기억 — S
- ⬜ 트랜스크립트 내보내기 — S

---

## 2. 핵심 기능 구현 노트

### A. Stop (중지) — S
- `AgentSession.RunAsync(ct)`에 `CancellationToken` 전달 → 취소 시 `proc.Kill(entireProcessTree:true)`
- `AppViewModel`: 세션별 `CancellationTokenSource` 보관, Stop 커맨드. UI: 컴포저 Send 자리에 실행 중 Stop 버튼(디자인의 stop 아이콘)

### B. 멀티턴 연속성 — M
- 현재: 매 Send = 새 프로세스(맥락 없음)
- 방식①(권장): **per-turn 프로세스 + resume.** `SessionStarted`에서 받은 sessionId 저장 → 다음 턴 `SessionOptions.ResumeSessionId`로 Claude `--resume`, Codex `exec resume <id>`/`--last`
- 방식②: Claude 프로세스 상주(양방향 stdin 유지). 복잡 → 보류
- 어댑터에 ResumeArgs 이미 자리 있음(ClaudeAdapter는 `--resume` 처리). Codex resume 인자 추가 필요

### C. 승인 프롬프트 UI — M ★아키텍처 주의
- **Claude**: `--permission-prompt-tool stdio`(비-bypass) → `PermissionRequest`(can_use_tool) 이벤트 수신 → UI approve/reject → **stdin으로 control_response 전송**. 즉 **프로세스 상주 + 양방향 stdin** 필요(현재 단발 구조와 충돌) → AgentSession에 "응답 보내기" 경로 추가
- **Codex**: `exec`는 단발/단방향이라 승인 왕복이 어려움 → `app-server`/`proto` 모드 별도 검토 필요(별도 스파이크). 당장은 Codex는 sandbox 모드(workspace-write)로, Claude만 승인 UI
- → **이 기능은 AgentSession을 "단발 실행"에서 "상주 세션"으로 진화시키는 분기점.** B(멀티턴)와 함께 설계하면 시너지

### F. 세션 영속성 — M
- 저장: 세션 메타(JSON) + 트랜스크립트 블록 직렬화 → `%AppData%/AgentManager/sessions/*.json`
- 블록 직렬화: TranscriptItem에 type 태그 + System.Text.Json 다형성(JsonDerivedType) 또는 수동 매핑
- 복원: 시작 시 로드 → 사이드바 채움(상태는 idle/done로). 실행 중 세션은 복원 시 종료 처리

### 횡단 결정 (먼저 합의할 것)
1. **세션 모델**: "단발 실행" 유지 + resume(간단) vs "상주 세션"(승인/멀티턴 자연스럽지만 복잡). → **승인 UI를 진지하게 할 거면 상주로, 아니면 resume로.**
2. **Codex 승인**: exec로는 한계 → 당장은 Claude만 승인 / Codex는 sandbox. 추후 app-server 스파이크.
3. **영속성 범위**: 트랜스크립트 전체 저장 vs 메타만. → 전체 저장(대화 복원) 권장.
4. **번역 기본값**: 전역 ON/OFF + 세션별 오버라이드?

---

## 3. 마일스톤 (제안)

**M1 — 기본 제어 (안전하게 쓸 수 있는 매니저)**
Stop · 멀티턴(resume) · 모델 선택 연결 · 경과타이머/비용

**M2 — 안전 & 번역 UX**
승인 프롬프트 UI(Claude) · 권한/샌드박스 모드 · 번역 토글+원본보기+인디케이터

**M3 — 영속성 & 가독성**
세션/설정 저장·복원 · 설정 패널 · 마크다운 렌더링 · thinking 블록

**M4 — 디자인 완성도**
리뷰 드로어 diff · 사이드바 그룹화 · 메시지 액션 · 스트리밍 타이핑 · 알림

**M5 — 확장**
Antigravity 어댑터 · 이미지 첨부 · 검색/필터 · Codex 승인(app-server)

---

## 4. 확정된 결정 (2026-06)
- ✔ **세션 모델 = 단발 실행 + resume** (간단·단계적). 상주 세션은 보류.
- ✔ **승인 UI 보류 = 둘 다 자동허용(bypass) 유지.** Claude=skip-permissions, Codex=bypass-sandbox.
  → M2의 승인 프롬프트/샌드박스 항목은 후순위로 내림.
- ⏳ 영속성 저장 위치/형식 — M3에서 결정 (`%AppData%/AgentManager/sessions/*.json` 유력)

### 결정에 따른 M1/P0 (현재 진행)
1. ✅ Stop(중지)
2. ✅ Worktree 격리
3. ✅ 우측 Review pane + Changes/Diff
4. ✅ Project 개념
5. ✅ 영속성
6. **Review actions** ← 추천
7. 멀티턴(resume)
8. 모델 선택 `--model` 연결
9. 경과 타이머 / 비용 표시
