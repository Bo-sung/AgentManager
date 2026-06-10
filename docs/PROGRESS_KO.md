# AgentManager — 진행 상태 (living tracker)

> 단일 진실 소스. 한 기능 끝낼 때마다 갱신. 상세 계획은 FEATURES_KO.md, 매핑은 PHASE0_*.

## ✅ 완료
| 항목 | 커밋 |
|---|---|
| 클린룸 스펙 + WPF 스캐폴드 | 1183276 |
| Phase 0: Claude stream-json 매핑(실측) | 075c124 |
| Phase 0: Codex exec --json 매핑(실측) | 0589a35 |
| Core 골격: 정규화이벤트 + Claude/Codex 어댑터 + 번역기 + 세션 + 오프라인 스모크 | 729537b |
| WPF UI: 오케스트레이터 디자인 포팅 + Core 연동(실엔진 실행) | 2ff9b50 |
| 한글 mojibake 수정(stdio UTF-8) | d325938 |
| 확정 기능셋 문서화(리서치 반영) | cf4a2d9 |
| **M1-① Stop(중지)** | ae26f06 |
| **P0구조-① Worktree 격리** (Core GitWorktree + 세션별 worktree cwd) | 2314af2 |
| **P0구조-② 우측 Review pane** (변경파일 목록 + diff 뷰) | f82a194 |
| **P0구조-③ Project 개념** (프로젝트 등록/선택 + 세션 소속 + project cwd) | f82a194 |
| **P0구조-④ JSON 영속성** (project/session/transcript 저장·복원) | f82a194 |
| **P0구조-⑤ 3-pane 보강** (사이드바 Active/Project 그룹화 + 접이식 Review pane 토글 + 활성 하이라이트) | a20fc6a |
| **P0구조-⑥ Review actions** (Merge ▸ main = 커밋+머지 / Discard = reset+clean) | e1d95c4 |
| **M1-⑦ 멀티턴(resume)** (SessionStarted id 저장 + 다음 턴 Claude/Codex resume 인자) | 76ab03e |
| **M2-① 번역 토글/원본보기** (세션별 TR ON/OFF + 번역 응답 ORIGINAL 표시) | 76ab03e |
| **M3-① 마크다운 렌더링** (assistant 응답 heading/list/code fence/inline code/bold 표시) | 76ab03e |
| **M1-⑧ 실행 상태 가시화** (RUNNING 바 + 경과 시간 + 마지막 출력 신호 + 무응답 경고) | 76ab03e |
| **M3-② 설정 패널** (provider 경로 + Ollama endpoint/model + 새 세션 번역 기본값 저장) | 76ab03e |
| **M1-⑨ 비용/토큰 정산 + 모델 연결 (로직)** (TurnCompleted.Usage 보정, CostUsd 누적·영속, Total 집계 속성, SessionOptions.Model) | e48fab7 |
| **A-① 세션 수명주기 (로직)** (Delete=중지+worktree제거, Archive 토글+ArchivedSessions, Rename, 영속성) | 9b77dc2 |

> **작업 방식(합의)**: 기능(Core/VM) 우선, View XAML 바인딩은 나중에 일괄.
> UI 노출 대기 중: 집계 표시(Σ TKN/COST) · 세션 컨텍스트 메뉴(삭제/보관/이름변경) · 아카이브 그룹

| **검증 패스** (Smoke: sandbox/model 인자 매트릭스 + GitWorktree e2e · 실 2턴 resume="47" 성공, session_id 유지) | ec1aab6 |

## 🔜 다음 (무거운 로직 — 결정 2026-06)
1. **승인 broker Stage 1 (Claude만)** ← 진행 중. Stage 2+에서 Codex(app-server 스파이크)·Antigravity 확장 예정
2. **Artifacts 라이트** — 기존 이벤트 파생(태스크리스트/테스트결과/요약). 풀(파일 기반)은 보류
3. **MCP 패스스루** — Project.McpConfigPath → `--mcp-config`. 풀 registry는 수요 확인 후(패스스루가 하위 계층이라 마이그레이션 무손실)
4. **(UI 일괄 패스)** 집계 표시 · 컨텍스트 메뉴 · 아카이브 그룹 · 샌드박스 선택 · Commit/피드백 버튼 · cap 설정 · 승인 UI

## ⏸ 보류 / 후순위
- 승인 broker (현재 bypass 유지 — 결정됨)
- 멀티에이전트 파이프라인/Handoff → **P2** (결정됨)
- Browser QA · SSH/원격 · 컨테이너 · Scheduled · 팀공유 · Extension SDK → P2 이후
- Antigravity 어댑터 → 전환(6/18)·표면 확정 후

## 결정 로그 (요약)
- 세션 모델 = **단발+resume** / 승인 = **bypass 유지** / 파이프라인 = **P2**
- PTY ✗ (JSON 모드) · worktree 격리 기본 · 3-pane · Project 개념 · JSON 영속성 · 번역 1급
