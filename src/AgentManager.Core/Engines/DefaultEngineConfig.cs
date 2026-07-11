using AgentManager.Core.Agents;
using AgentManager.Core.Models;

namespace AgentManager.Core.Engines;

/// <summary>
/// Seed defaults for <see cref="EngineConfigStore"/> — one <see cref="EngineConfig"/> per built-in engine, built
/// from <see cref="EngineRegistry.All"/> (identity/cli) + <see cref="DefaultModelCatalog"/> (models + per-model
/// efforts), so a fresh <c>engines/&lt;id&gt;.json</c> reproduces today's behavior exactly. Users then edit the
/// files (add models, set preferred, fix efforts, override path/auth) or drop in a custom engine file.
/// </summary>
public static class DefaultEngineConfig
{
    /// <summary>Protocol/adapter kind for each built-in engine (drives the future AdapterFactory in Phase B; stored
    /// now so the schema is stable). Kept in sync with <see cref="EngineRegistry.CreateAdapter"/>.</summary>
    public static string AdapterKindFor(string id) => id switch
    {
        "cc" => "claude-stream-json",
        "gx" => "codex-json",
        "agy" => "agy-pty",
        "pi" => "pi-rpc",
        _ => "",
    };

    public static IReadOnlyList<EngineConfig> Build()
    {
        var cats = DefaultModelCatalog.Build();
        var list = new List<EngineConfig>();
        foreach (var def in EngineRegistry.All)
        {
            cats.TryGetValue(def.Id, out var cat);
            var models = (cat?.Models ?? [])
                .Select(m => new EngineModelConfig(m.Id, m.Efforts, m.DefaultEffort, Preferred: false))
                .ToArray();
            list.Add(new EngineConfig(
                Id: def.Id,
                Name: def.Name,
                Badge: def.Badge,
                Source: "builtin",
                AdapterKind: AdapterKindFor(def.Id),
                Cli: def.Cli,
                Desc: def.Desc,
                InstallUrl: def.InstallUrl,
                Enabled: def.Enabled,
                Auth: new EngineAuthConfig(),
                DefaultEfforts: cat?.DefaultEfforts ?? [],
                Models: models,
                AllowedRoles: ["Plain", "Main", "Worker"]));
        }
        return list;
    }
}
