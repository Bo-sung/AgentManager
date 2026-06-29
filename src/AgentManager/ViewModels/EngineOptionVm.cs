using AgentManager.Core.Agents;

namespace AgentManager.ViewModels;

/// <summary>New Agent 피커 항목: 엔진 정의(Core <see cref="EngineDef"/>) + 설치 여부. Id/Name/Desc/Badge/InstallUrl을
/// 위임 노출해 기존 템플릿·아이콘(EngineIconByDef는 {Binding Id}) 바인딩과 그대로 호환된다. UI 전용 래퍼라 WPF 층에 남는다.</summary>
public sealed class EngineOptionVm(EngineDef def, bool isInstalled, bool isLimited = false, bool willUseApi = false)
{
    public EngineDef Def { get; } = def;
    public bool IsInstalled { get; } = isInstalled;
    public bool IsLimited { get; } = isLimited;     // 사용량 한도 소진
    public bool WillUseApi { get; } = willUseApi;    // 소진이어도 API 자동전환으로 사용 가능

    /// <summary>선택(실행) 가능 — 설치됨 + (한도 OK 또는 API 자동전환).</summary>
    public bool IsAvailable => IsInstalled && (!IsLimited || WillUseApi);
    public bool Dimmed => !IsAvailable;
    public bool ShowInstallBadge => !IsInstalled;
    public bool ShowLimitBadge => IsInstalled && IsLimited && !WillUseApi;  // 한도 초과(회색)
    public bool ShowApiBadge => IsInstalled && IsLimited && WillUseApi;     // API 전환(사용 가능)

    public string Id => Def.Id;
    public string Name => Def.Name;
    public string Desc => Def.Desc;
    public string Badge => Def.Badge;
    public string InstallUrl => Def.InstallUrl;
}
