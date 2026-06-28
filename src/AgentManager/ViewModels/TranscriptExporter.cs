using System;
using System.Text;

namespace AgentManager.ViewModels;

public static class TranscriptExporter
{
    public static string ToMarkdown(SessionViewModel s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + s.Title).AppendLine();
        // 세션 메타: 엔진 · 모델 · 추론단계 · 추출시각 (맨 위 한 줄로 컨텍스트 보존)
        var reasoning = string.IsNullOrWhiteSpace(s.ReasoningEffort) ? "default" : s.ReasoningEffort;
        sb.AppendLine($"- **Engine**: {s.AgentName} (`{s.Cli}`)");
        sb.AppendLine($"- **Model**: {s.Model}");
        sb.AppendLine($"- **Reasoning**: {reasoning}");
        sb.AppendLine($"- **Exported**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine().AppendLine("---").AppendLine();
        foreach (var item in s.Transcript)
        {
            switch (item)
            {
                case UserBlock u:
                    sb.AppendLine("## 🧑 User").AppendLine(u.Text);
                    if (u.HasSent) sb.AppendLine().AppendLine("> sent (EN): " + u.SentText);
                    sb.AppendLine();
                    break;
                case AgentTextBlock a: sb.AppendLine("## 🤖 " + s.AgentName).AppendLine(a.Text).AppendLine(); break;
                case ToolBlock t:
                    sb.AppendLine("### 🔧 " + t.Name);
                    if (!string.IsNullOrWhiteSpace(t.Body)) sb.AppendLine("```").AppendLine(t.Body.TrimEnd()).AppendLine("```");
                    sb.AppendLine();
                    break;
                case ErrorBlock err: sb.AppendLine("### ❌ " + err.Title).AppendLine(err.Body).AppendLine(); break;
                case ApprovalBlock p: sb.AppendLine("### ⚠ Approval: " + p.ToolName + " → " + p.State).AppendLine(); break;
                case WorkingBlock w: sb.AppendLine("> " + w.Text).AppendLine(); break;
            }
        }
        return sb.ToString();
    }
}
