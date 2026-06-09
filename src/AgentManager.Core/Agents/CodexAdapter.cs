using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentManager.Core.Events;

namespace AgentManager.Core.Agents;

/// <summary>
/// Codex CLI adapter (codex exec --json). Schema reference:
/// docs/PHASE0_CODEX_EXEC_JSON_KO.md (measured). NOTE: stdin must be closed after
/// start or codex hangs ("Reading additional input from stdin...").
/// </summary>
public sealed class CodexAdapter : IAgentAdapter
{
    public string Id => "codex";
    public AgentCapabilities Capabilities { get; } = new(
        Permissions: false, Thinking: false, Sessions: true, Images: true, TokenUsage: true, Quota: false);
    public bool CloseStdinAfterStart => true;

    public ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            StandardInputEncoding = Utf8NoBom,
        };
        psi.ArgumentList.Add("exec");
        if (!string.IsNullOrWhiteSpace(options.ResumeSessionId))
        {
            psi.ArgumentList.Add("resume");
            psi.ArgumentList.Add(options.ResumeSessionId);
        }
        psi.ArgumentList.Add("--json");
        if (options.BypassPermissions && options.Sandbox == SandboxMode.DangerFullAccess)
            psi.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
        else
        {
            psi.ArgumentList.Add("--sandbox");
            psi.ArgumentList.Add(options.Sandbox == SandboxMode.ReadOnly ? "read-only" : "workspace-write");
        }
        if (!string.IsNullOrWhiteSpace(options.Model)) { psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(options.Model); }
        psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(options.WorkingDirectory);
        psi.ArgumentList.Add(prompt); // prompt is a positional arg for Codex
        return psi;
    }

    public IReadOnlyList<string> InitialStdinLines(string prompt) => []; // prompt goes via args; stdin is closed

    public IEnumerable<NormalizedEvent> ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;
        JsonElement root;
        try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
        catch { yield break; }

        var type = Str(root, "type");
        switch (type)
        {
            case "thread.started":
                yield return new SessionStarted(Str(root, "thread_id") ?? "", null, 0, null);
                break;

            case "turn.started":
                break; // internal boundary

            case "item.started":
            case "item.completed":
                if (root.TryGetProperty("item", out var item))
                {
                    var itemType = Str(item, "type");
                    var id = Str(item, "id") ?? "";
                    if (itemType == "command_execution")
                    {
                        if (type == "item.started")
                            yield return new ToolUseStarted(id, "shell", JsonSerializer.Serialize(new { command = Str(item, "command") }));
                        else
                            yield return new ToolResult(
                                id,
                                Str(item, "aggregated_output") ?? "",
                                item.TryGetProperty("exit_code", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.GetInt32() != 0);
                    }
                    else if (itemType == "agent_message" && type == "item.completed")
                    {
                        var text = Str(item, "text");
                        if (!string.IsNullOrWhiteSpace(text)) yield return new AssistantText(text!.Trim());
                    }
                    else
                    {
                        yield return new RawUnknown(itemType ?? "item", line);
                    }
                }
                break;

            case "turn.completed":
                if (root.TryGetProperty("usage", out var u))
                    yield return new TokenUsage(
                        Lng(u, "input_tokens"), Lng(u, "output_tokens"),
                        CacheReadTokens: Lng(u, "cached_input_tokens"),
                        ReasoningTokens: Lng(u, "reasoning_output_tokens"));
                yield return new TurnCompleted(null, false, null, null);
                break;

            default:
                yield return new RawUnknown(type ?? "?", line);
                break;
        }
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Lng(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
