using System.Text.RegularExpressions;

namespace AgentManager.Core;

/// <summary>One detected choice in an assistant message. <see cref="Text"/> is sent verbatim
/// when clicked; <see cref="Label"/> is the (truncated) button caption; <see cref="Marker"/>
/// is the option letter/number chip (A, B, 1, 2 …).</summary>
public sealed record QuickReplyOption(string Marker, string Label, string Text);

/// <summary>
/// Engine-agnostic detection of "pick one of these" options at the end of an assistant
/// message (e.g. "A) … B) …" or "1. … 2. …"), so the UI can offer one-click replies.
/// Operates purely on the rendered text, so it works for every engine and in any language
/// (option markers are language-neutral). Biased toward precision: letter lists are accepted
/// directly; numbered lists need a choice cue and must not look like a list of questions.
/// </summary>
public static partial class QuickReplyParser
{
    public static IReadOnlyList<QuickReplyOption> Parse(string? assistantText)
    {
        if (string.IsNullOrWhiteSpace(assistantText)) return [];
        // 일부 엔진(agy 등)은 첫 선택지를 앞 문장에 붙여 낸다("…적절합니다.A) 테스트 …"). 문장부호 뒤에
        // 바로 붙은 글자 마커 앞에 줄바꿈을 넣어 줄-시작 규칙으로 잡히게 정규화(파서 내부 복사본만, 표시 무관).
        assistantText = GlueRx().Replace(assistantText, "\n");
        var lines = assistantText.Replace("\r", "").Split('\n');

        // Collect option-looking lines: (original index, kind L/N, marker, content).
        var opts = new List<(int idx, char kind, string marker, string content)>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = StripLead(lines[i]);
            if (line.Length == 0) continue;
            var m = LetterRx().Match(line);
            if (m.Success) { opts.Add((i, 'L', m.Groups[1].Value.ToUpperInvariant(), Clean(m.Groups[2].Value))); continue; }
            m = NumberRx().Match(line);
            if (m.Success) { opts.Add((i, 'N', m.Groups[1].Value, Clean(m.Groups[2].Value))); }
        }
        if (opts.Count < 2) return [];

        // Group into runs broken by a kind change or a non-blank, non-option line in between.
        var runs = new List<List<int>>();
        var cur = new List<int> { 0 };
        for (int k = 1; k < opts.Count; k++)
        {
            bool sameKind = opts[k].kind == opts[cur[^1]].kind;
            bool onlyBlankBetween = AllBlank(lines, opts[cur[^1]].idx + 1, opts[k].idx - 1);
            if (sameKind && onlyBlankBetween) cur.Add(k);
            else { runs.Add(cur); cur = new List<int> { k }; }
        }
        runs.Add(cur);

        // Prefer the run closest to the end (options usually trail the message).
        for (int r = runs.Count - 1; r >= 0; r--)
        {
            var run = runs[r];
            if (run.Count < 2) continue;
            var picked = run.Select(ix => opts[ix]).ToList();
            if (!Sequential(picked)) continue;                       // A,B,C… or 1,2,3…
            if (picked[0].kind == 'N' && !LooksLikeChoice(assistantText, picked)) continue;

            return picked.Take(6)
                .Where(p => p.content.Length is > 0 and <= 240)
                .Select(p => new QuickReplyOption(p.marker, Truncate(p.content, 80), $"{p.marker}) {p.content}"))
                .ToList();
        }
        return [];
    }

    static string StripLead(string s)
    {
        s = s.Trim();
        s = Regex.Replace(s, @"^(?:[-*>]\s+)+", "");   // leading list bullets / quotes
        s = s.Replace("**", "");                        // drop bold markers (around marker or text)
        return s.Trim();
    }

    static string Clean(string s)
    {
        s = s.Trim().Trim('*', '`').Trim();
        s = Regex.Replace(s, @"[,]?\s+or\s*$", "", RegexOptions.IgnoreCase); // trailing ", or"
        return s.TrimEnd(',', ';', ' ').Trim();
    }

    static bool AllBlank(string[] lines, int from, int to)
    {
        for (int i = from; i <= to && i < lines.Length; i++)
            if (i >= 0 && lines[i].Trim().Length != 0) return false;
        return true;
    }

    static bool Sequential(List<(int idx, char kind, string marker, string content)> p)
    {
        if (p[0].kind == 'L')
        {
            for (int i = 0; i < p.Count; i++)
                if (p[i].marker.Length != 1 || p[i].marker[0] != (char)('A' + i)) return false;
            return true;
        }
        for (int i = 0; i < p.Count; i++)
            if (p[i].marker != (i + 1).ToString()) return false;
        return true;
    }

    /// <summary>Numbered lists are often just enumerated points/questions, not a pick-one choice.
    /// Require a choice cue and reject lists that are mostly questions.</summary>
    static bool LooksLikeChoice(string text, List<(int idx, char kind, string marker, string content)> p)
    {
        int questionish = p.Count(x => x.content.TrimEnd().EndsWith('?'));
        if (questionish * 2 >= p.Count) return false;
        return CueRx().IsMatch(text);
    }

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    /// <summary>문장부호([.:!?。．：！？]) 뒤에 바로 붙은 "X) " 글자 마커 앞 — 줄바꿈 삽입 지점.</summary>
    [GeneratedRegex(@"(?<=[.:!?。．：！？])[ \t]*(?=[A-Za-z]\)[ \t])")]
    private static partial Regex GlueRx();
    [GeneratedRegex(@"^([A-Za-z])\)\s+(.+)$")]
    private static partial Regex LetterRx();
    [GeneratedRegex(@"^(\d{1,2})[.)]\s+(.+)$")]
    private static partial Regex NumberRx();
    [GeneratedRegex(@"choose|pick|which|option|select|prefer|proceed|want me to|shall I|go with|할까요|하시겠|드릴까요|선택|원하|진행|어느|골라|택", RegexOptions.IgnoreCase)]
    private static partial Regex CueRx();
}
