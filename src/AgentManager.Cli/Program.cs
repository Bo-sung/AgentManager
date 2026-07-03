using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Orchestration;
using AgentManager.Core.Session;

// AgentManager CLI — a THIN headless frontend over the same Core services the WPF GUI uses.
// Its existence is the proof that overhaul phase (a) extracted enough: this project targets plain
// net10.0 (not net10.0-windows) and references ONLY AgentManager.Core, so if it builds and runs a turn,
// the orchestration surface is genuinely WPF-free. It composes the extracted Core pieces directly:
//   EngineRegistry/TurnPlanner (resolve + options) · RunRegistry (cancellation) · ApprovalBroker
//   (permission round-trip) · AgentSession (the engine) · TranscriptProjector (event → neutral deltas,
//   which a console renders as text instead of WPF blocks).

return await Run(args);

static async Task<int> Run(string[] args)
{
    var opts = CliOptions.Parse(args);
    if (opts is null) { Usage(); return 2; }

    // --- resolve the engine through the same Core decision tree the GUI uses ---
    var resolution = TurnPlanner.ResolveEngine(new EngineResolveRequest(
        AgentId: opts.Engine,
        RequireApproval: opts.RequireApproval,
        ApiMode: false,                 // CLI uses the standard subscription/CLI adapter
        HasApiKey: false,
        ClaudePath: null, CodexPath: null, AgyPath: null, PiPath: null, // resolve from PATH
        ResolvePython: () => null));
    if (!resolution.Ok)
    {
        Console.Error.WriteLine(resolution.Error switch
        {
            EngineSetupError.EngineUnavailable => $"engine '{opts.Engine}' is not installed / not on PATH.",
            _ => $"engine setup failed: {resolution.Error}",
        });
        return 1;
    }

    var cwd = Path.GetFullPath(opts.Cwd ?? Directory.GetCurrentDirectory());
    var sessionId = "cli-" + Guid.NewGuid().ToString("N")[..8];

    var options = TurnPlanner.BuildOptions(new TurnOptionsRequest(
        AgentId: opts.Engine,
        WorkingDirectory: cwd,
        RequireApproval: opts.RequireApproval,
        Sandbox: SandboxMode.DangerFullAccess,
        ResumeSessionId: null,
        Model: opts.Model,
        McpConfigPath: null,
        Images: Array.Empty<string>(),
        AttachedDocsText: "",
        AdditionalDirectories: Array.Empty<string>(),
        ReasoningEffort: opts.Effort,
        ApiEnv: new Dictionary<string, string>(),
        TaskSpoolDir: Path.Combine(cwd, ".am", "worker-tasks", sessionId),
        AskSpoolDir: Path.Combine(cwd, ".am", "ask", sessionId),
        NativeHookSpoolDirectory: null));

    var runs = new RunRegistry();
    var broker = new ApprovalBroker();
    var projector = new TranscriptProjector();
    var renderer = new ConsoleRenderer();

    var session = new AgentSession(resolution.Adapter!, resolution.Exe!);
    session.EventReceived += ev =>
    {
        foreach (var delta in projector.Project(sessionId, opts.Engine, ev))
            renderer.Apply(delta);
    };
    if (opts.RequireApproval)
        session.PermissionHandler = pr => HandleApproval(broker, pr);

    // Ctrl+C cancels the turn (fires the engine process-tree kill via the registry token).
    var token = runs.Start(sessionId);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; runs.Cancel(sessionId); };

    Console.Error.WriteLine($"[{opts.Engine}] {Path.GetFileName(cwd)} · {sessionId}");
    try
    {
        await session.RunAsync(options, opts.Prompt, token);
        renderer.Flush();
        return renderer.TurnFailed ? 1 : 0;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("\n[stopped]");
        return 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"\n[failed] {ex.Message}");
        return 1;
    }
    finally
    {
        runs.Complete(sessionId);
    }
}

// Route the engine's permission request through the Core broker, answered on stdin. Proves a headless
// frontend can satisfy the same approval contract the GUI does — without any WPF dialog.
static Task<PermissionDecision> HandleApproval(ApprovalBroker broker, PermissionRequest pr)
{
    var task = broker.Request(pr.RequestId);
    Console.Error.Write($"\n[approve] {pr.ToolName}? [y/N] ");
    var line = Console.ReadLine()?.Trim().ToLowerInvariant();
    var allow = line is "y" or "yes";
    broker.Resolve(pr.RequestId, new PermissionDecision(allow, allow ? null : "denied by user"));
    return task;
}

static void Usage()
{
    Console.Error.WriteLine(
        """
        am — AgentManager CLI (headless frontend over AgentManager.Core)

          am <engine> [options] <prompt...>

        engines:  cc (Claude Code) · gx (Codex) · agy (Antigravity) · pi (Pi)

        options:
          --cwd <dir>     working directory (default: current)
          --model <name>  engine model (default: engine default)
          --approve       require tool approval (prompted on stdin); default bypasses
          -                read the prompt from stdin

        examples:
          am cc "list the files and summarize the project"
          am gx --cwd ./service --approve "add a health check endpoint"
          echo "explain this error" | am cc -
        """);
}

/// <summary>Parsed CLI invocation.</summary>
sealed record CliOptions(string Engine, string? Cwd, string? Model, string? Effort, bool RequireApproval, string Prompt)
{
    static readonly string[] Engines = ["cc", "gx", "agy", "pi"];

    public static CliOptions? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        var engine = args[0];
        if (!Engines.Contains(engine)) return null;

        string? cwd = null, model = null, effort = null;
        var requireApproval = false;
        var rest = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cwd" when i + 1 < args.Length: cwd = args[++i]; break;
                case "--model" when i + 1 < args.Length: model = args[++i]; break;
                case "--effort" when i + 1 < args.Length: effort = args[++i]; break;
                case "--approve": requireApproval = true; break;
                case "-": rest.Add(Console.In.ReadToEnd()); break;
                default: rest.Add(args[i]); break;
            }
        }

        var prompt = string.Join(' ', rest).Trim();
        return string.IsNullOrWhiteSpace(prompt) ? null : new CliOptions(engine, cwd, model, effort, requireApproval, prompt);
    }
}

/// <summary>The CLI's transcript projection: it APPLIES the same neutral <see cref="TranscriptDelta"/>s
/// the GUI applies, but renders them as console text instead of WPF blocks. This is the frontend half the
/// projector deliberately leaves out — proving the deltas carry enough for any frontend.</summary>
sealed class ConsoleRenderer
{
    private bool _streaming;
    public bool TurnFailed { get; private set; }

    public void Apply(TranscriptDelta delta)
    {
        switch (delta)
        {
            case AssistantStreamAppend d:
                Console.Write(d.Delta);
                _streaming = true;
                break;
            case AssistantStreamReplace d:
                // already streamed live; close the line (translation would differ, but CLI runs untranslated)
                EndStream();
                if (!string.IsNullOrEmpty(d.Text) && d.Text.Length > 0 && !d.Text.EndsWith('\n'))
                    Console.WriteLine();
                break;
            case AssistantStreamEnd:
                EndStream();
                break;
            case AssistantAdd d:
                EndStream();
                Console.WriteLine(d.Text);
                break;
            case ThinkingAdd d:
                EndStream();
                Console.Error.WriteLine($"  ﹙thinking﹚ {Clip(d.Text, 200)}");
                break;
            case ToolAdd d:
                EndStream();
                Console.Error.WriteLine($"  · {d.Name}{(string.IsNullOrEmpty(d.CommandText) ? "" : " " + Clip(d.CommandText!, 120))}");
                break;
            case ToolFinished d:
                if (d.IsError) Console.Error.WriteLine($"    ✗ {Clip(d.Content, 200)}");
                break;
            case ErrorAdd d:
                EndStream();
                Console.Error.WriteLine($"  ! {d.Message}");
                break;
            case TurnFinished d:
                EndStream();
                TurnFailed = d.IsError;
                Console.Error.WriteLine(d.IsError ? "[failed]" : "[done]");
                break;
            // EngineSessionIdSet / ActivitySignal / Tokens / Quota / Status / artifacts: not surfaced in the CLI
        }
    }

    public void Flush() => EndStream();

    private void EndStream()
    {
        if (_streaming) { Console.WriteLine(); _streaming = false; }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
