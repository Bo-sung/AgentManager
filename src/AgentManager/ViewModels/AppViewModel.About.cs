using System;
using System.IO;
using System.Reflection;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    public string AppVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version 
                          ?? Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString(3) ?? "1.0.0";
        }
    }

    public string AboutBuildLabel
    {
        get
        {
            string buildDateStr = "20260614";
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                {
                    buildDateStr = File.GetLastWriteTime(loc).ToString("yyyyMMdd");
                }
            }
            catch { }

            int count = AllEngines.Length;
            return AgentManager.App.L("L.AboutBuildPattern", buildDateStr, count);
        }
    }
}
