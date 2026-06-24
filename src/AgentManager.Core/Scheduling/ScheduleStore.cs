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

    private static string? _customStorePath;
    public static string StorePath
    {
        get => _customStorePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "schedules.json");
        set => _customStorePath = value;
    }

    public static List<ScheduledJob> Load()
        => JsonFile.ReadOrDefault(StorePath, () => new List<ScheduledJob>(), Options);

    public static void Save(List<ScheduledJob> jobs)
        => JsonFile.WriteAtomic(StorePath, jobs, Options);
}
