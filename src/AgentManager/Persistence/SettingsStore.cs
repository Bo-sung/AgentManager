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

    /// <summary>
    /// 라이브 리로드 전용 로더: 읽기/파싱 <b>실패</b> 시 <c>null</c>을 돌려준다(기본값이 아님).
    /// 손편집 중 일시적 문법 오류나 파일 잠금이 발생했을 때 호출부가 <b>적용을 건너뛸</b> 수 있게 하기 위함 —
    /// 실패를 기본값으로 오인해 적용하면 메모리의 실제 설정이 통째로 날아가기 때문이다.
    /// 파일이 <b>정말로 없을</b> 때만 기본값을 돌려준다.
    /// </summary>
    public static AppSettingsDto? TryLoad()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            return JsonSerializer.Deserialize<AppSettingsDto>(File.ReadAllText(SettingsPath), Options) ?? new();
        }
        catch { return null; } // 파싱/읽기 실패 → 신호(기본값으로 대체하지 않음)
    }

    public static void Save(AppSettingsDto settings)
    {
        try { JsonFile.WriteAtomic(SettingsPath, settings, Options); }
        catch { /* 영속화 실패가 UI/실행을 막지 않게 */ }
    }
}
