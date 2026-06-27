using System.IO;
using System.Text.Json;
using System.Threading;

namespace AgentManager.Core;

/// <summary>JSON 파일 영속화 공통 헬퍼 — 원자적 쓰기(temp→Move overwrite)와 안전 읽기(없음/손상→fallback).
/// 여러 스토어(Settings/State/Schedule)에 복제돼 있던 동일 IO 메커니즘을 모은 것.
/// 예외 정책은 호출부에 맡긴다: <see cref="WriteAtomic"/>은 throw(원자성/실패 노출),
/// <see cref="ReadOrDefault"/>는 swallow→fallback.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>temp 파일에 쓴 뒤 Move(overwrite)로 교체 — 쓰기 중 중단돼도 원본이 깨지지 않는다.
    /// 일시적 파일 잠금(백신/백업 도구/다른 인스턴스)에 대비해 IO 실패 시 짧게 백오프하며 재시도하고,
    /// 모두 실패하면 마지막 예외를 다시 던진다(호출부가 기록/처리). 직렬화는 루프 밖에서 한 번만 한다
    /// (직렬화 오류는 일시적이지 않으므로 재시도 의미 없음).</summary>
    public static void WriteAtomic<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, options ?? Indented);

        // 50/120/300/600ms 백오프로 최대 4회. 마지막 시도 실패면 그대로 throw.
        ReadOnlySpan<int> backoffMs = [50, 120, 300, 600];
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < backoffMs.Length)
            {
                Thread.Sleep(backoffMs[attempt]);
            }
        }
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
