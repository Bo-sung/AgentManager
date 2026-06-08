# AgentManager

로컬 LLM 번역 레이어를 갖춘 **멀티 에이전트 매니저** (WPF / .NET 10, Windows).

CLI 에이전트(Claude Code, Codex 등)를 **구동·관리**하고, 한글↔영어 번역으로 토큰을 절감하면서
한국어 UX를 제공합니다. **IDE가 아니라 에이전트 매니저**입니다.

## 상태
초기 스캐폴드 단계. 설계는 [docs/DESIGN_SPEC_KO.md](docs/DESIGN_SPEC_KO.md) 참고.

## 빌드
```powershell
dotnet build AgentManager.slnx
dotnet run --project src/AgentManager
```
요구: .NET SDK 10+, Windows.

## 구조
```
AgentManager.slnx          솔루션
src/AgentManager/          WPF 앱
docs/DESIGN_SPEC_KO.md      설계 스펙(클린룸)
```

## 출처/라이선스
- 이 프로젝트는 **처음부터 작성한 오리지널 코드**입니다.
- 별도의 VS Code 익스텐션(claude-code-chat 포크, "personal use only")의 **소스를 복사/포팅하지 않습니다.**
  익스텐션은 개념·프로토콜을 검증한 PoC이며, 본 제품은 그 지식만 이어받아 새로 구현합니다.
- 라이선스: [LICENSE](LICENSE) (MIT).
