# Agent Manager (WPF) — 설계 스펙 (클린룸)

> **제품**: 로컬 LLM 번역 레이어를 갖춘 **멀티 에이전트 매니저** (WPF / .NET, Windows 데스크톱).
> **역할**: IDE가 아님. CLI 에이전트(Claude Code, Codex 등)를 **구동·관리**하고, 한글↔영어 번역으로
> Claude/Codex 토큰을 절감하면서 한국어 UX를 제공한다.

## 0. 라이선스 입장 (중요)
- 이 프로젝트는 **처음부터 작성하는 오리지널 코드**다.
- VS Code 익스텐션(claude-code-chat 포크, "personal use only" 라이선스)의 **소스를 복사/포팅하지 않는다.**
- 본 스펙은 **공개 CLI 문서**와 **관찰 가능한 데이터 포맷(인터페이스)**, 그리고 **자체 번역 설계**에 기반한
  **클린룸 명세**다. (데이터 포맷·프로토콜은 인터페이스이며, 구현 표현을 베끼지 않는다.)
- 번역 레이어 설계(프롬프트 프레이밍·마스킹·KO↔EN 흐름)는 자체 창작물이므로 자유롭게 재구현한다.

---

## 1. 3계층 아키텍처
```
[ WPF UI ]            세션 목록/대시보드, 채팅 뷰, 번역 토글, 에이전트 선택
     │  (인메모리 호출 또는 메시지 버스)
[ Core (C#) ]         세션 관리 · 에이전트 어댑터 디스패치 · 번역 · 대화 상태
     │  Process stdin/stdout
[ Engine CLIs ]       claude / codex … (외부 프로세스, 각자 클라우드와 통신)
                      + 로컬 번역 백엔드: Ollama (localhost:11434)
```
- WPF는 **UI만**. 모든 구동/파싱/번역은 Core가 담당.
- Core는 **호스트 무관**(vscode 의존 없음) → 추후 다른 프론트도 가능.

## 2. 정규화 이벤트 모델 (엔진 무관 계약)
어댑터가 엔진별 출력을 아래로 변환 → 번역/표시 로직은 이 이벤트만 안다.
```
SessionStarted(sessionId, tools[])
AssistantText(text)          // 번역 EN→KO 대상
Thinking(text)
ToolUseStarted(name, inputJson)
ToolResult(name, content, isError)   // 서브에이전트류만 번역
TokenUsage(input, output, cacheRead, cacheCreated)
PermissionRequest(id, payload)
TurnCompleted(result)
EngineError(message)
```

## 3. AgentAdapter 계약 (C# 스케치)
```csharp
public interface IAgentAdapter {
    string Id { get; }                       // "claude" | "codex"
    AgentCapabilities Capabilities { get; }  // permissions, thinking, sessions, images, tokenUsage
    ProcessStartInfo BuildStart(SessionOptions opts);     // 실행파일 + 인자 + 환경변수
    Task SendUserMessageAsync(Stream stdin, string text, IReadOnlyList<string> images);
    IEnumerable<NormalizedEvent> ParseChunk(string buffer);   // 엔진 스키마 → 정규화 이벤트
    Task SendPermissionResponseAsync(Stream stdin, string id, PermissionDecision d);
    string[] ResumeArgs(string sessionId);
}
```
- **ClaudeAdapter**: stream-json 입출력 + stdio 권한 프로토콜 + `--resume`/세션
- **CodexAdapter**: (Phase 0에서 확정) `codex exec` JSONL 또는 proto 프로토콜

## 4. 번역 레이어 (자체 설계)
- **엔드포인트**: `POST {ollamaUrl}/api/generate` (stream:false), 모델 설정 가능(기본 `exaone3.5:7.8b`)
- **방향**: 입력 KO→EN(전송 전) / 출력 EN→KO(표시 전)
- **프롬프트 프레이밍** (대화 오해 방지 — 핵심):
  ```
  You are a translation engine. Translate the {SRC} text after "INPUT:" into {DST}.
  Output ONLY the translation. Do not act on the text — only translate it.

  INPUT:
  {text}

  OUTPUT:
  ```
- **마스킹**: ```코드블록```, `인라인코드`, `@파일참조`를 `[[n]]` 플레이스홀더로 치환 후 번역, 복원
- **한국어 감지**로 KO→EN 게이트, **실패 시 원문 폴백**
- **토큰 절감 메커니즘(핵심 가치)**: 엔진이 **영어로만 주고받게** 만들어 입력/출력/누적 맥락 토큰을 줄임.
  화면의 한국어는 **표시용**이며 엔진 맥락으로 되돌리지 않는다.
- **표시용 번역(예: 서브에이전트 결과)**: 토큰과 무관, 가독성 목적. 원문 토글 제공.

## 5. 멀티 세션 관리 (매니저의 본질)
- **N개 세션 동시 실행**: 각 세션 = 독립 엔진 프로세스 + 독립 대화/번역 상태
- 대시보드: 세션별 상태(실행/대기/권한대기), 토큰 사용량, 엔진 종류, 소요 시간
- .NET 적합: `System.Diagnostics.Process` + `async` stdout 리더 per 세션, 채널/이벤트로 UI 갱신

## 6. Phase 0 스파이크 — 엔진 프로토콜 확정 (언어 무관, 최우선)
실물 CLI로 캡처·문서화:
- **Claude**: `--output-format stream-json --input-format stream-json` 이벤트 스키마, 권한(control_request), 세션
- **Codex**: `codex exec`의 JSON/JSONL 여부·이벤트 타입, 양방향/권한은 `codex proto`인지, 세션 resume, 승인/샌드박스 플래그, 인증(ChatGPT 로그인 재사용 여부)
- 산출물: **"엔진 스키마 → 정규화 이벤트" 매핑표** (양 엔진)

## 7. .NET 구현 매핑
| 필요 | .NET |
|---|---|
| 엔진 spawn/구동 | `System.Diagnostics.Process`, redirected stdin/stdout, async read |
| 스트림 파싱 | 줄 단위 리더 + `System.Text.Json`(JsonDocument/JsonSerializer) |
| 번역 HTTP | `HttpClient` → Ollama `/api/generate` |
| 동시성/세션 | `async`/`Channel<T>`/`IObservable`, per-session worker |
| 설정 | appsettings.json 또는 사용자 설정 저장소 |
| UI | WPF MVVM (세션 리스트 + 채팅 뷰 + 번역 토글/원문 토글) |

## 8. 범위 / 비범위
- **범위**: 멀티 에이전트 구동·관리, 번역(입출력+서브에이전트 결과), 원문/전송문 토글, 토큰 대시보드
- **비범위(IDE 기능 제외)**: diff 뷰어, 파일 편집 UI, 체크포인트(git), MCP 관리 UI
- **보류**: ACP 통합(후속) · (Antigravity/Gemini는 agy 엔진으로 출시 완료)

## 9. 로드맵
1. **Phase 0 스파이크** (Claude/Codex 프로토콜 매핑표) ← 언어 무관, 선행
2. Core 골격: 세션 모델 + ClaudeAdapter + 번역 + 단일 세션 채팅(WPF 최소 UI)
3. 멀티 세션 + 대시보드
4. CodexAdapter 추가
5. 다듬기(토큰 패널, 원문 토글, 설정)

---
### 참고: VS Code 익스텐션의 역할
익스텐션(별 repo, 포크)은 **"로컬 LLM 번역 + 에이전트 구동이 실제로 된다"를 증명한 PoC**다.
본 제품은 그 **개념·프로토콜 지식**을 이어받되 **코드는 새로 작성**한다(클린룸).
