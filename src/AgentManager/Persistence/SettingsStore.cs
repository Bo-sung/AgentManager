using System.IO;
using System.Text.Json;
using AgentManager.Core;

namespace AgentManager.Persistence;

/// <summary>
/// 사용자 설정을 VS Code식 <c>settings.json</c>(손편집 가능)으로 따로 저장한다.
/// 세션/트랜스크립트가 들어찬 거대한 state.json과 분리되어, 사람이 직접 열어 편집하기 좋다.
/// 구버전에서 올라온 경우 state.json의 "Settings" 노드를 1회 마이그레이션한다.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "settings.json");

    public static AppSettingsDto Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettingsDto>(File.ReadAllText(SettingsPath), Options) ?? new();

            // 마이그레이션: 구버전 state.json의 Settings 노드 → settings.json
            if (File.Exists(AppStateStore.StatePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AppStateStore.StatePath));
                if (doc.RootElement.TryGetProperty("Settings", out var s))
                {
                    var migrated = s.Deserialize<AppSettingsDto>(Options) ?? new();
                    Save(migrated);
                    return migrated;
                }
            }
        }
        catch { /* 손상/파싱 실패 → 기본값 */ }
        return new();
    }

    public static void Save(AppSettingsDto settings)
    {
        try { JsonFile.WriteAtomic(SettingsPath, settings, Options); }
        catch { /* 영속화 실패가 UI/실행을 막지 않게 */ }
    }
}
