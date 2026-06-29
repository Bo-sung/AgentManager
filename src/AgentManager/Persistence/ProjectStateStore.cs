using System.IO;
using System.Text.Json;
using AgentManager.Core;
using AgentManager.Core.Workers;

namespace AgentManager.Persistence;

/// <summary>프로젝트 단위 영속화 — 세션 + 워커 백로그/큐를 <c>&lt;project&gt;/.am/project.json</c>에 저장한다.
/// 전역 <c>state.json</c>은 프로젝트 목록/활성 id/설정만 들고, 프로젝트의 작업 데이터는 프로젝트 폴더를
/// 따라간다(공유 드라이브/다른 머신에서 열어도 같은 세션·백로그가 보인다). <c>.am/</c>은 이미 gitignore됨.</summary>
public sealed record ProjectStateDto
{
    /// <summary>스키마 버전(향후 마이그레이션 판별용). 현재 1.</summary>
    public int Schema { get; init; } = 1;
    public List<SessionDto> Sessions { get; init; } = [];
    /// <summary>이 프로젝트 소속 워커 작업(백로그 + 각 워커 큐). projectId로 필터링된 슬라이스.</summary>
    public List<WorkerTaskDto> WorkerTasks { get; init; } = [];
}

/// <summary><c>&lt;project.Path&gt;/.am/project.json</c> 입출력. 원자적 쓰기(temp→Move) + 손상 시 null.
/// 모든 IO는 best-effort — 프로젝트 폴더가 읽기 전용/공유 드라이브라 호출부가 흡수한다(save-errors.log).</summary>
public static class ProjectStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>프로젝트 로컬 상태 디렉토리 = <c>&lt;projectPath&gt;/.am</c> (기존 워커-태스크 스풀과 동일 폴더).</summary>
    public static string DirFor(string projectPath) => Path.Combine(projectPath, ".am");

    /// <summary>프로젝트 로컬 상태 파일 경로 = <c>&lt;projectPath&gt;/.am/project.json</c>.</summary>
    public static string PathFor(string projectPath) => Path.Combine(DirFor(projectPath), "project.json");

    /// <summary>프로젝트 로컬 상태 로드. 파일이 없거나 파손되면 null(호출부가 레거시 전역 state.json으로 폴백 = 마이그레이션).</summary>
    public static ProjectStateDto? Load(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return null;
        var path = PathFor(projectPath);
        return JsonFile.ReadOrDefault<ProjectStateDto?>(path, () => null, Options);
    }

    /// <summary>프로젝트 로컬 상태 원자적 저장. 폴더 자동 생성(.am/). 실패 시 throw(호출부가 로그/흡수).</summary>
    public static void Save(string projectPath, ProjectStateDto dto)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;
        JsonFile.WriteAtomic(PathFor(projectPath), dto, Options);
    }
}
