# AgentManager — 확정 기능셋 (Antigravity + VS Code Agents 리서치 반영)

## 0. 제품 정의
> **Standalone Multi-Agent Development Control Plane (+ 로컬 LLM 번역 레이어)**
> Claude Code / Codex / Antigravity CLI 등 코딩 에이전트를 **project 단위로 실행**하고,
> worktree 격리 · 승인 · diff 리뷰 · evidence(artifact) · 세션 수명주기를 통합 관리하는
> **agent-first 데스크톱 클라이언트**. 여기에 **한글↔영어 로컬 번역(토큰 절감)**을 1급 기능으로 더한다.

- Antigravity에서 채택: **Project, Artifact/Evidence, Browser 검증, Scheduled, Skills/MCP**
- VS Code Agents에서 채택: **Sessions list, Agent target/Permission picker, Worktree 격리, Changes 패널, Handoff, Fork, Debug view**
- 우리 고유: **로컬 LLM 번역 레이어**(리서치 제품엔 없음 — 핵심 차별점)

## 1. 이미 가진 것 (리서치가 검증한 아키텍처) ✅
- **Provider Adapter** 패턴 (`IAgentAdapter`: Claude/Codex) ← 리서치의 핵심 권장과 일치
- **Normalized event stream** (`NormalizedEvent`) ← 리서치 "normalized event types"와 정확히 같은 방향
- **Session console** UI(3-pane 중 좌+중), 다중 세션, Stop, 상태/토큰/할당량
- **번역 레이어** (KO↔EN, 마스킹, 폴백)
- 구조화 JSON 모드(stream-json / exec --json) — **PTY 안 씀**(아래 결정 참조)

## 2. 횡단 아키텍처 결정 (먼저 잠금)
| 결정 | 내용 | 근거 |
|---|---|---|
| **JSON 모드 유지(PTY ✗)** | 인터랙티브 PTY 대신 stream-json/exec --json로 구동·파싱 | 우리 강점·normalized event와 정합. Claude/Codex 모두 headless JSON 지원 |
| **Worktree 격리 = 기본** | 세션마다 `git worktree` 생성 → 변경 격리, 끝나면 diff/merge/discard | 병렬 에이전트의 안전 기본값(VSCode 패턴). 현재 레포 직접쓰기는 위험 |
| **3-pane 레이아웃 채택** | 좌 nav · 중 console · **우 Review pane(evidence/diff/artifacts)** | Antigravity Auxiliary + VSCode Changes 결합. 현재 2-pane → 우 패널 추가 |
| **Project 개념 도입** | 폴더(멀티폴더) 묶음 = agent scope. 세션은 project에 속함 | 양 레퍼런스 공통 기본 단위 |
| **영속성 = JSON 파일 시작** | `%AppData%/AgentManager/`에 project/session/transcript JSON. 후에 SQLite 검토 | 솔로 규모엔 JSON로 충분, 점진 |
| **세션 모델 = 단발+resume** | (기확정) 매 턴 새 프로세스 + resume | 이미 합의 |
| **승인 = 단계적** | MVP는 bypass 유지(기확정) → 이후 Claude 승인 broker | 안전성은 P1로 |
| **번역 = 1급** | 입력 KO→EN(토큰절감), 출력 EN→KO(표시), 세션별 토글 | 우리 차별점 |

## 3. 확정 기능셋 (우선순위)

### P0 — MVP (실제로 쓸 수 있는 관제 클라이언트)
| # | 기능 | 상태 | 출처 |
|---|---|---|---|
| 1 | Provider 등록/탐지 (Claude/Codex 경로·설치확인) | 🟡 일부 | 공통 |
| 2 | **Project 등록** (local folder, 멀티폴더) | ✅ | 공통 |
| 3 | Session 생성 (provider·cwd·prompt·번역 토글) | ✅ | 공통 |
| 4 | Process manager (spawn/stream/**stop**/resume) | 🟡 stop✅ resume🟡 | 공통 |
| 5 | Event parser → normalized events | ✅ | 공통 |
| 6 | Session list (running/waiting/done/failed) + 상태 | ✅ | VSCode |
| 7 | **Worktree 격리** (세션별 git worktree) | ✅ | VSCode |
| 8 | **Changes/Diff 패널** (변경 파일 + diff 뷰) | ✅ | VSCode+AG |
| 9 | **우측 Review pane** (Overview/Changes/Actions) | ✅ 목록/diff/Merge/Discard | 공통 |
| 10 | 멀티턴(resume) | 🟡 구현, 실제 CLI 2턴 검증 대기 | 공통 |
| 11 | 영속성 (세션/transcript 저장·복원) | ✅ | VSCode |
| 12 | 번역 토글 + 원본보기 + 인디케이터 | 🟡 세션별 토글/ORIGINAL✅, 인디케이터⬜ | 우리 |
| 13 | 설정 (provider 경로, Ollama 모델/URL, 기본값) | 🟡 provider/Ollama/번역 기본값✅ | 공통 |
| 14 | 마크다운 렌더링 | 🟡 기본 렌더링✅, 고급 Markdown⬜ | 공통 |

### P1 — 제품 경쟁력
| 기능 | 상태 | 출처 |
|---|---|---|
| 승인 broker (Claude can_use_tool approve/deny) | ⬜ (bypass 유지 — 결정) | 공통 |
| 권한/샌드박스 모드 선택 | 🟡 로직✅ (Codex --sandbox, Claude ReadOnly→plan) · UI⬜ | 공통 |
| Merge / Discard / Commit (worktree→main) | ✅ Merge·Discard·Commit-only(로직) | VSCode |
| Session fork (context 상속 분기) | 🟡 로직✅ (transcript+engine세션 상속) · UI⬜ | VSCode |
| Diff 인라인 피드백 → agent 재지시 | 🟡 로직✅ (diff+피드백→후속 턴) · UI⬜ | VSCode |
| 모델 선택 실제 연결 / Agent target 선택 | ✅ SessionOptions.Model → --model/-m | VSCode |
| Artifacts 패널 (plan/task list/test result) | ⬜ | AG |
| MCP registry (project별 allowlist) | ⬜ | 공통 |
| 경과타이머 / 비용 / 집계 대시보드 | 🟡 경과✅ · 비용/집계 로직✅(UI 노출 대기) | 공통 |
| 사이드바 grouping (project/상태) | ✅ Active/Project 그룹 + 아카이브 그룹(로직) | VSCode |
| 세션 수명주기 (삭제/보관/이름변경) | 🟡 로직✅(커맨드·영속성) · UI(컨텍스트 메뉴)⬜ | 공통 |
| 동시 실행 cap (병렬 세션 수 제한) | 🟡 로직✅ (기본 3, 영속) · 설정 UI⬜ | 공통 |
| **IDE 핸드오프** (Open IDE — 활성 세션 worktree를 에디터로) | ✅ VS Code로 열기, 미설치 시 탐색기 폴백 | VSCode+AG |
| **이미지 첨부** (컴포저 ⊞ — 엔진 `-i`/base64 전달) | ✅ Ctrl+V 붙여넣기 + 파일선택 + 칩 표시 | 공통 |

### P2 — 고급/차별화
| 기능 | 비고 |
|---|---|
| **Multi-agent pipeline / Handoff** (Claude plan→Codex impl→verify) | 제품 차별점, 그러나 무거움 |
| Antigravity CLI 어댑터 | 전환(6/18)·표면 확정 후 |
| Browser QA / screenshot 검증 | AG 강점, 매우 무거움 |
| Scheduled tasks (cron) | sidecar 필요. 사이드바에 비활성 자리 존재 |
| Activity History 뷰 (세션 횡단 활동 이력) | 사이드바에 비활성 자리 존재 |
| Skills/Hooks registry | 확장 |
| SSH/원격 runner, 컨테이너 격리 | 매우 무거움 |
| Subagent orchestration / Judge agent | 고급 |
| Debug view (prompt/context/tool inspect) | 개발자용 |

## 4. 솔직한 스코프 경고 (솔로/WPF 기준)
- **무거워서 후순위 강력 권장**: Browser QA, SSH/원격, 컨테이너, Scheduled, 파이프라인 자동화, 팀 공유, Extension SDK. → P2 이후 또는 보류.
- **PTY 인터랙티브는 안 함**: JSON 모드로 충분하고 normalized event가 깨끗함. (truly interactive 필요 CLI가 생기면 그때 재검토)
- **MVP의 진짜 핵심 4개**: ② Project · ⑦ Worktree 격리 · ⑧⑨ Diff/Review pane · ⑪ 영속성. 이게 "터미널 멀티플렉서"와 "agent 관제 클라이언트"를 가르는 선.

## 5. 근시일 계획 변경 (기존 M1 → 재정렬)
리서치 반영해 **구조(P0 핵심 4)를 앞으로** 당깁니다:
1. ✅ Stop (완료)
2. ✅ Worktree 격리 (완료)
3. ✅ 우측 Review pane + Changes/Diff (완료)
4. ✅ Project 개념 (완료)
5. ✅ 영속성 (완료)
6. 🟡 멀티턴(resume) · 번역 토글/원본보기 · 마크다운 · 설정 패널
> 이후 P1(승인·merge·fork·artifacts), P2(파이프라인·Antigravity…)

## 6. 확정 대기 (네 결정 필요)
- [x] P0에 **Review pane + Project**를 먼저 완성할지 vs 간단한 멀티턴/설정 먼저
- [x] 영속성: JSON 시작 OK?
- [ ] 파이프라인/Handoff를 "제품 차별점"으로 일찍 투자할지 vs P2로
