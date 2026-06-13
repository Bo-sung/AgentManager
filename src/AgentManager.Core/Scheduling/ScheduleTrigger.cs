using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentManager.Core.Scheduling;

public sealed record ScheduleTrigger
{
    public required string Kind { get; init; } // "Cron" 또는 "Event"
    public required string CadenceText { get; init; } // 사람이 읽는 텍스트
    public string? CronExpression { get; init; } // 5필드 Cron 표현식 (Kind == "Cron" 일 때)
    public string? TargetPath { get; init; } // 이벤트 대상 경로 (Kind == "Event" 일 때)

    public DateTime? GetNextRunUtc(DateTime? lastRunUtc)
    {
        if (!string.Equals(Kind, "Cron", StringComparison.OrdinalIgnoreCase))
        {
            // NotImplemented: Event-based trigger evaluation is not implemented in v1.
            return null;
        }

        var cronExpr = CronExpression ?? TryParseCadenceToCron(CadenceText);
        if (string.IsNullOrEmpty(cronExpr))
        {
            return null;
        }

        try
        {
            var evaluator = new CronExpressionEvaluator(cronExpr);
            var baseTime = lastRunUtc ?? DateTime.UtcNow;
            return evaluator.GetNextRunUtc(baseTime);
        }
        catch
        {
            return null;
        }
    }

    public static string? TryParseCadenceToCron(string cadenceText)
    {
        if (string.IsNullOrWhiteSpace(cadenceText)) return null;
        var text = cadenceText.Trim().ToLowerInvariant();

        // 1. 단순 5필드 cron 표현식인지 확인
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 5)
        {
            bool isCron = true;
            foreach (var p in parts)
            {
                if (p != "*" && !p.All(c => char.IsDigit(c) || c == ',' || c == '-' || c == '/' || c == '*'))
                {
                    isCron = false;
                    break;
                }
            }
            if (isCron) return text;
        }

        // 2. 시간 형식(HH:mm) 추출
        var timeMatch = Regex.Match(text, @"\b(\d{1,2}):(\d{2})\b");
        if (!timeMatch.Success) return null;

        int hour = int.Parse(timeMatch.Groups[1].Value);
        int minute = int.Parse(timeMatch.Groups[2].Value);
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return null;

        // "every day", "daily", "매일"
        if (text.Contains("every day") || text.Contains("daily") || text.Contains("매일"))
        {
            return $"{minute} {hour} * * *";
        }

        // 요일 매핑
        var dayOfWeekMap = new Dictionary<string, int>
        {
            { "monday", 1 }, { "mondays", 1 }, { "mon", 1 }, { "월", 1 }, { "월요일", 1 },
            { "tuesday", 2 }, { "tuesdays", 2 }, { "tue", 2 }, { "화", 2 }, { "화요일", 2 },
            { "wednesday", 3 }, { "wednesdays", 3 }, { "wed", 3 }, { "수", 3 }, { "수요일", 3 },
            { "thursday", 4 }, { "thursdays", 4 }, { "thu", 4 }, { "목", 4 }, { "목요일", 4 },
            { "friday", 5 }, { "fridays", 5 }, { "fri", 5 }, { "금", 5 }, { "금요일", 5 },
            { "saturday", 6 }, { "saturdays", 6 }, { "sat", 6 }, { "토", 6 }, { "토요일", 6 },
            { "sunday", 0 }, { "sundays", 0 }, { "sun", 0 }, { "일", 0 }, { "일요일", 0 }
        };

        var tokens = text.Split(new[] { ' ', '·', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var kvp in dayOfWeekMap)
        {
            if (tokens.Contains(kvp.Key))
            {
                return $"{minute} {hour} * * {kvp.Value}";
            }
        }

        return null;
    }
}

internal sealed class CronExpressionEvaluator
{
    private readonly HashSet<int>? _minutes;
    private readonly HashSet<int>? _hours;
    private readonly HashSet<int>? _daysOfMonth;
    private readonly HashSet<int>? _months;
    private readonly HashSet<int>? _daysOfWeek; // 0=Sunday, ..., 6=Saturday

    public CronExpressionEvaluator(string expression)
    {
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            throw new ArgumentException("Cron expression must have exactly 5 fields.");

        _minutes = ParseField(parts[0], 0, 59);
        _hours = ParseField(parts[1], 0, 23);
        _daysOfMonth = ParseField(parts[2], 1, 31);
        _months = ParseField(parts[3], 1, 12);
        _daysOfWeek = ParseField(parts[4], 0, 7); // 0 or 7 = Sunday
        if (_daysOfWeek != null && _daysOfWeek.Contains(7))
        {
            _daysOfWeek.Add(0);
        }
    }

    private static HashSet<int>? ParseField(string field, int min, int max)
    {
        if (field == "*") return null;

        var result = new HashSet<int>();
        var parts = field.Split(',');
        foreach (var part in parts)
        {
            if (part.Contains('/'))
            {
                var stepParts = part.Split('/');
                var range = stepParts[0];
                var step = int.Parse(stepParts[1]);

                int start = min;
                int end = max;

                if (range != "*")
                {
                    if (range.Contains('-'))
                    {
                        var rangeParts = range.Split('-');
                        start = int.Parse(rangeParts[0]);
                        end = int.Parse(rangeParts[1]);
                    }
                    else
                    {
                        start = int.Parse(range);
                    }
                }

                for (int i = start; i <= end; i += step)
                {
                    if (i >= min && i <= max) result.Add(i);
                }
            }
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                int start = int.Parse(rangeParts[0]);
                int end = int.Parse(rangeParts[1]);
                for (int i = start; i <= end; i++)
                {
                    if (i >= min && i <= max) result.Add(i);
                }
            }
            else
            {
                if (int.TryParse(part, out int val))
                {
                    if (val >= min && val <= max) result.Add(val);
                }
            }
        }
        return result;
    }

    public DateTime GetNextRunUtc(DateTime baseTime)
    {
        var dt = new DateTime(baseTime.Year, baseTime.Month, baseTime.Day, baseTime.Hour, baseTime.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
        var limit = baseTime.AddYears(5);

        while (dt < limit)
        {
            if (_months != null && !_months.Contains(dt.Month))
            {
                dt = new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                continue;
            }

            if (_daysOfMonth != null && !_daysOfMonth.Contains(dt.Day))
            {
                dt = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);
                continue;
            }

            if (_daysOfWeek != null && !_daysOfWeek.Contains((int)dt.DayOfWeek))
            {
                dt = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);
                continue;
            }

            if (_hours != null && !_hours.Contains(dt.Hour))
            {
                dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
                continue;
            }

            if (_minutes != null && !_minutes.Contains(dt.Minute))
            {
                dt = dt.AddMinutes(1);
                continue;
            }

            return dt;
        }

        throw new InvalidOperationException("Could not find next run time within 5 years.");
    }
}
