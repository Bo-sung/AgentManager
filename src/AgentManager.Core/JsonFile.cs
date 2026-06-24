using System.IO;
using System.Text.Json;

namespace AgentManager.Core;

/// <summary>JSON 파일 영속화 공통 헬퍼 — 원자적 쓰기(temp→Move overwrite)와 안전 읽기(없음/손상→fallback).
/// 여러 스토어(Settings/State/Schedule)에 복제돼 있던 동일 IO 메커니즘을 모은 것.
/// 예외 정책은 호출부에 맡긴다: <see cref="WriteAtomic"/>은 throw(원자성/실패 노출),
/// <see cref="ReadOrDefault"/>는 swallow→fallback.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>temp 파일에 쓴 뒤 Move(overwrite)로 교체 — 쓰기 중 중단돼도 원본이 깨지지 않는다.</summary>
    public static void WriteAtomic<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, options ?? Indented));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>파일이 있으면 역직렬화, 없거나 파싱 실패면 <paramref name="fallback"/>() 결과.</summary>
    public static T ReadOrDefault<T>(string path, Func<T> fallback, JsonSerializerOptions? options = null)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options ?? Indented) ?? fallback();
        }
        catch { /* 손상/파싱 실패 → fallback */ }
        return fallback();
    }
}
