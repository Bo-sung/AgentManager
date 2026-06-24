using System.Diagnostics;

namespace AgentManager;

/// <summary>OS 기본 핸들러로 URL/파일/폴더 열기. 링크 열기는 fire-and-forget이므로 예외는 삼킨다
/// — 여러 호출부(MarkdownViewer, 설정, 네비게이션)에 흩어져 있던
/// <c>try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }</c>
/// 관용구를 한 곳으로 모은 것.</summary>
internal static class Shell
{
    public static void Open(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* 링크/경로 열기 실패는 무시 */ }
    }
}
