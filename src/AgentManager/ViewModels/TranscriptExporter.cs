using System.Text;

namespace AgentManager.ViewModels;

public static class TranscriptExporter
{
    public static string ToMarkdown(SessionViewModel s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + s.Title).AppendLine();
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
