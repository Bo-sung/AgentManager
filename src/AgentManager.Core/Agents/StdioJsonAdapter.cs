using System.Diagnostics;
using System.Text.Json;
using AgentManager.Core.Events;

namespace AgentManager.Core.Agents;

/// <summary>stdout JSONL을 파싱하는 stdio 어댑터 공통 베이스. 줄 단위 파싱 가드(공백 스킵 +
/// JsonDocument 파싱 실패 무시 + RootElement.Clone)를 한 곳에 두고, 실제 매핑은
/// <see cref="ParseRoot"/>에 위임한다. PTY 엔진(agy)은 이 베이스를 쓰지 않는다.</summary>
public abstract class StdioJsonAdapter : IAgentAdapter
{
    public abstract string Id { get; }
    public abstract AgentCapabilities Capabilities { get; }
    public abstract bool CloseStdinAfterStart { get; }

    public abstract ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt);
    public abstract IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options);

    // 인터페이스 기본구현(DIM)을 베이스에서 virtual로 실체화한다 — 그래야 파생 클래스의 override가
    // IAgentAdapter 경유 호출에서도 디스패치된다(DIM은 파생 일반 메서드를 가린다).
    public virtual bool KillAfterTurnCompleted => false;
    public virtual string? BuildPermissionResponse(PermissionRequest request, PermissionDecision decision) => null;

    public IEnumerable<NormalizedEvent> ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        JsonElement root;
        try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
        catch { return []; }
        return ParseRoot(root, line);
    }

    /// <summary>파싱된 한 줄(JSON 루트)을 정규화 이벤트로 매핑한다.
    /// <paramref name="line"/>은 RawUnknown 보존용 원문.</summary>
    protected abstract IEnumerable<NormalizedEvent> ParseRoot(JsonElement root, string line);
}
