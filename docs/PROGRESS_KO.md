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
| **P0구조-⑤ 3-pane 보강** (사이드바 Active/Project 그룹화 + 접이식 Review pane 토글 + 활성 하이라이트) | working tree |

## 🔜 다음 (구조 먼저 — FEATURES §5)
1. **Review actions** — worktree 변경 Merge/Discard/Commit ← 추천
2. 멀티턴(resume)
3. 번역 토글/원본보기 · 마크다운 · 설정 패널

## ⏸ 보류 / 후순위
- 승인 broker (현재 bypass 유지 — 결정됨)
- 멀티에이전트 파이프라인/Handoff → **P2** (결정됨)
- Browser QA · SSH/원격 · 컨테이너 · Scheduled · 팀공유 · Extension SDK → P2 이후
- Antigravity 어댑터 → 전환(6/18)·표면 확정 후

## 결정 로그 (요약)
- 세션 모델 = **단발+resume** / 승인 = **bypass 유지** / 파이프라인 = **P2**
- PTY ✗ (JSON 모드) · worktree 격리 기본 · 3-pane · Project 개념 · JSON 영속성 · 번역 1급
