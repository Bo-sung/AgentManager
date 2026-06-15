namespace AgentManager.Core.Observation;

public static class NativeHookCommandFactory
{
    public static string WindowsPowerShellSpoolScript(string spoolDirectory)
    {
        Directory.CreateDirectory(spoolDirectory);
        var scriptPath = Path.Combine(spoolDirectory, "am-hook-spool.ps1");
        const string script = """
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$json = [Console]::In.ReadToEnd()
$dir = $PSScriptRoot
if (-not [string]::IsNullOrWhiteSpace($json)) {
    $name = ('{0:yyyyMMddHHmmssfff}-{1}.json' -f [DateTime]::UtcNow, [Guid]::NewGuid().ToString('N'))
    [System.IO.File]::WriteAllText([System.IO.Path]::Combine($dir, $name), $json, [System.Text.UTF8Encoding]::new($false))
}
exit 0
""";
        File.WriteAllText(scriptPath, script, new System.Text.UTF8Encoding(false));
        return "powershell -NoProfile -ExecutionPolicy Bypass -File " + QuoteForCommandLine(scriptPath);
    }

    public static string WindowsPowerShellSpoolWriter(string? spoolDirectory = null)
    {
        var dirExpression = string.IsNullOrWhiteSpace(spoolDirectory)
            ? "$env:AGENTMANAGER_HOOK_SPOOL"
            : "'" + spoolDirectory.Replace("'", "''") + "'";
        var script =
            "[Console]::InputEncoding=[Text.Encoding]::UTF8;" +
            "$json=[Console]::In.ReadToEnd();" +
            "$dir=" + dirExpression + ";" +
            "if($dir){" +
            "[IO.Directory]::CreateDirectory($dir)|Out-Null;" +
            "$name=('{0:yyyyMMddHHmmssfff}-{1}.json' -f [DateTime]::UtcNow,[Guid]::NewGuid().ToString('N'));" +
            "[IO.File]::WriteAllText([IO.Path]::Combine($dir,$name),$json,[Text.UTF8Encoding]::new($false))" +
            "}";
        return "powershell -NoProfile -ExecutionPolicy Bypass -Command " + QuoteForCommandLine(script);
    }

    private static string QuoteForCommandLine(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";
}
