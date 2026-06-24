using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentManager.Core.Agents;

/// <summary>어댑터 공통 JSON 접근자 + stdio 프로세스 팩토리.
/// 여러 어댑터에 동일하게 복제돼 있던 헬퍼를 한 곳으로 모은 것 — 동작은 그대로다.</summary>
internal static class AdapterJson
{
    /// <summary>BOM 없는 UTF-8 — CLI stdio 인코딩(모든 stdio 어댑터 공통).</summary>
    internal static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    internal static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static long Lng(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    /// <summary>stdio 리다이렉트 + CreateNoWindow + BOM 없는 UTF-8 인코딩이 설정된 ProcessStartInfo.
    /// 호출부는 ArgumentList만 채우면 된다.</summary>
    internal static ProcessStartInfo NewStdioStartInfo(string executablePath, string workingDirectory)
        => new()
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            StandardInputEncoding = Utf8NoBom,
        };
}

/// <summary>정규화 이벤트(ToolUseStarted.Name)에서 엔진 간 공유하는 도구 이름.
/// Codex exec/app-server가 동일 문자열을 쓰므로 상수로 모은다.</summary>
internal static class ToolNames
{
    internal const string Shell = "shell";
    internal const string ApplyPatch = "apply_patch";
    internal const string WebSearch = "web_search";
}

/// <summary>Win32 인자 인용 규칙: 역슬래시는 큰따옴표 직전에서만 특수 — 따옴표 앞 역슬래시 런만
/// 두 배 + \" . ConPTY 명령줄 조립용(PTY 엔진: agy 등).</summary>
internal static class Win32Args
{
    internal static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        var bs = 0;
        foreach (var c in s)
        {
            if (c == '\\') { bs++; continue; }
            if (c == '"') { sb.Append('\\', bs * 2 + 1).Append('"'); bs = 0; continue; }
            sb.Append('\\', bs); bs = 0; sb.Append(c);
        }
        sb.Append('\\', bs * 2).Append('"');
        return sb.ToString();
    }
}
