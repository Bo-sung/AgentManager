# [Delegated task] AgentManager — rewrite README.md

> Paste this whole document into the session. Working directory: `J:\prj\AgentManager`

## Task
Rewrite the root `README.md` so it accurately describes the product as it exists today.

**You may ONLY edit `README.md`. Do not touch any other file. Do not edit code.**

## Sources of truth (read these first)
- `docs/FEATURES_KO.md` — confirmed feature set with current status marks
- `docs/PROGRESS_KO.md` — what is done (with commit hashes)
- `docs/DESIGN_SPEC_KO.md` — architecture summary

## Required structure (Korean, concise)
1. **제목 + 한 줄 소개** — Standalone Multi-Agent Development Control Plane + 로컬 LLM 한↔영 번역 레이어 (WPF/.NET 10, Windows)
2. **주요 기능** (현재 실제 동작하는 것만): 멀티 세션 병렬 관제(Claude Code/Codex), 세션별 git worktree 격리, Review pane(변경/diff/Merge·Commit·Discard), 승인 broker(Claude), 번역 레이어(KO→EN 입력/EN→KO 출력, 토큰 절감, ORIGINAL 토글), Artifacts(태스크리스트/테스트/요약), 멀티턴 resume, 모델 선택, 이미지 첨부(Ctrl+V), 비용/토큰/할당량 표시, IDE 핸드오프, 영속성
3. **요구 사항**: Windows + .NET 10 SDK, `claude` CLI(로그인 필요), Ollama(+ 번역 모델, 기본 exaone3.5:7.8b — 선택), Codex CLI(선택)
4. **빌드/실행**:
   ```powershell
   dotnet build AgentManager.slnx
   dotnet run --project src/AgentManager/AgentManager.csproj
   ```
   (scripts/publish.ps1이 존재하면 배포 섹션도 한 줄 추가)
5. **빠른 사용법**: 프로젝트 등록 → New Agent → 대화(한글 가능, TR ON) → Review pane에서 diff 확인 → Merge/Discard → Open IDE
6. **문서 목차**: docs/ 안의 각 문서 한 줄 설명
7. **라이선스**: MIT (기존 LICENSE 유지 — 본문 끝에 한 줄)

## Rules
- 존재하지 않는 기능을 적지 말 것 (FEATURES_KO의 ✅ 항목만 "주요 기능"에)
- 예정 기능은 "로드맵" 한 줄 섹션으로만 (승인 Stage2/Antigravity/Scheduled 등)
- 커밋: `git add README.md && git commit -m "docs: rewrite README to match the shipped product"` + 끝줄 `Co-Authored-By: Gemini <noreply@google.com>`
- 끝나면 `dotnet build J:\prj\AgentManager\AgentManager.slnx -c Debug`가 여전히 0 에러인지 확인(코드 안 건드렸으니 당연히 통과해야 함)
