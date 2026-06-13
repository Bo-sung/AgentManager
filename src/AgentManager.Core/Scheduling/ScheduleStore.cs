using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgentManager.Core.Scheduling;

public static class ScheduleStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "schedules.json");

    public static List<ScheduledJob> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<ScheduledJob>>(json, Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<ScheduledJob> jobs)
    {
        var dir = Path.GetDirectoryName(StorePath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(jobs, Options));
        File.Move(temp, StorePath, overwrite: true);
    }
}
