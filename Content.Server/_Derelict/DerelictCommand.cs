using System.Linq;
using Content.Server._Derelict.Components;
using Content.Server._Derelict.Monsters;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Derelict;

/// <summary>
/// Admin control over a Derelict run, mostly for testing and unsticking rounds.
/// </summary>
[AdminCommand(AdminFlags.Round)]
public sealed partial class DerelictCommand : LocalizedEntityCommands
{
    [Dependency] private DerelictRunRuleSystem _derelict = default!;
    [Dependency] private DerelictMonsterSystem _monsters = default!;

    public override string Command => "derelict";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_derelict.TryGetRun(out var run))
        {
            shell.WriteError("No Derelict run is active.");
            return;
        }

        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "status":
                shell.WriteLine($"phase: {run.Comp.Phase}");
                shell.WriteLine(_derelict.GetStatusText(run.Comp));
                if (run.Comp.Phase == DerelictPhase.Setup)
                    shell.WriteLine(_derelict.DescribeSetup());
                break;
            case "threats":
                shell.WriteLine(_monsters.Describe());
                break;
            case "sites":
                shell.WriteLine("offered: " + string.Join(", ", run.Comp.Offered));
                shell.WriteLine("all: " + string.Join(", ", run.Comp.Sites));
                break;
            case "launch":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: derelict launch <siteId>");
                    return;
                }

                if (_derelict.Launch(run, args[1], out var reason))
                    shell.WriteLine($"Jumping to {args[1]}.");
                else
                    shell.WriteError(reason ?? "Launch failed.");
                break;
            case "depart":
                shell.WriteLine(_derelict.Depart(run) ? "Departing." : "The tug is not on site.");
                break;
            case "submit":
                _derelict.SubmitQuota(run);
                shell.WriteLine($"phase: {run.Comp.Phase}");
                break;
            case "skiptime":
                if (args.Length < 2 || !float.TryParse(args[1], out var seconds))
                {
                    shell.WriteError("Usage: derelict skiptime <seconds>");
                    return;
                }

                _derelict.SkipTime(run, TimeSpan.FromSeconds(seconds));
                shell.WriteLine($"Skipped {seconds} seconds.");
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromOptions(new[] { "status", "sites", "threats", "launch", "depart", "submit", "skiptime" });

        if (args.Length == 2 && args[0] == "launch" && _derelict.TryGetRun(out var run))
            return CompletionResult.FromOptions(run.Comp.Sites.Select(s => s.Id));

        return CompletionResult.Empty;
    }
}
