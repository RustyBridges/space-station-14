using Content.Server._Derelict.Components;
using Content.Shared._Derelict;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._Derelict;

public sealed partial class DerelictRunRuleSystem
{
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private const string OptionLaunchPrefix = "launch:";
    private const string OptionDepart = "depart";
    private const string OptionQuota = "quota";

    private TimeSpan _nextConsoleUpdate;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<DerelictNavConsoleComponent, BoundUIOpenedEvent>(OnConsoleOpened);
        SubscribeLocalEvent<DerelictNavConsoleComponent, DerelictNavSelectMessage>(OnConsoleSelect);
        SubscribeLocalEvent<DerelictNavConsoleComponent, ExaminedEvent>(OnConsoleExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Keep the clock on open uplinks ticking.
        if (Timing.CurTime < _nextConsoleUpdate)
            return;

        _nextConsoleUpdate = Timing.CurTime + TimeSpan.FromSeconds(1);
        UpdateConsoles();
    }

    private void OnConsoleOpened(Entity<DerelictNavConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateConsoles();
    }

    private void UpdateConsoles()
    {
        if (!TryGetRun(out var run))
            return;

        DerelictNavBuiState? state = null;
        var query = EntityQueryEnumerator<DerelictNavConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (!_ui.IsUiOpen((uid, ui), DerelictNavUiKey.Key))
                continue;

            state ??= new DerelictNavBuiState(GetStatusText(run.Comp), GetOptions(run.Comp));
            _ui.SetUiState((uid, ui), DerelictNavUiKey.Key, state);
        }
    }

    private List<DerelictNavOption> GetOptions(DerelictRunRuleComponent comp)
    {
        var options = new List<DerelictNavOption>();

        switch (comp.Phase)
        {
            case DerelictPhase.Orbit:
                foreach (var siteId in comp.Offered)
                {
                    if (!_proto.TryIndex(siteId, out var site))
                        continue;

                    options.Add(new DerelictNavOption(OptionLaunchPrefix + siteId.Id,
                        Loc.GetString("derelict-nav-verb-launch",
                            ("site", Loc.GetString(site.Name)),
                            ("hazard", site.Hazard),
                            ("minutes", (int) site.Duration.TotalMinutes))));
                }

                break;
            case DerelictPhase.OnSite:
                options.Add(new DerelictNavOption(OptionDepart, Loc.GetString("derelict-nav-verb-depart")));
                break;
            case DerelictPhase.QuotaDue:
                options.Add(new DerelictNavOption(OptionQuota,
                    Loc.GetString("derelict-nav-verb-quota",
                        ("earned", comp.EarnedThisCycle),
                        ("quota", comp.Quota))));
                break;
        }

        return options;
    }

    private void OnConsoleSelect(Entity<DerelictNavConsoleComponent> ent, ref DerelictNavSelectMessage args)
    {
        if (!TryGetRun(out var run))
            return;

        var user = args.Actor;

        if (args.Id.StartsWith(OptionLaunchPrefix))
        {
            ProtoId<DerelictSitePrototype> site = args.Id[OptionLaunchPrefix.Length..];

            // Only sites that are actually on offer; the id comes from the client.
            if (!run.Comp.Offered.Contains(site))
                return;

            if (!Launch(run, site, out var reason) && reason != null)
                _popup.PopupEntity(reason, ent.Owner, user, PopupType.MediumCaution);
        }
        else if (args.Id == OptionDepart)
        {
            if (run.Comp.Phase == DerelictPhase.OnSite && !Depart(run))
                _popup.PopupEntity(Loc.GetString("derelict-nav-fail-ftl"), ent.Owner, user, PopupType.MediumCaution);
        }
        else if (args.Id == OptionQuota)
        {
            SubmitQuota(run);
        }

        UpdateConsoles();
    }

    private void OnConsoleExamined(Entity<DerelictNavConsoleComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryGetRun(out var run))
            return;

        args.PushMarkup(GetStatusText(run.Comp));
    }

    /// <summary>
    /// Multi-line status of the run, shown on the navigation console.
    /// </summary>
    public string GetStatusText(DerelictRunRuleComponent comp)
    {
        var lines = new List<string>
        {
            Loc.GetString("derelict-status-quota",
                ("cycle", comp.Cycle),
                ("earned", comp.EarnedThisCycle),
                ("quota", comp.Quota)),
            Loc.GetString("derelict-status-drops",
                ("left", Math.Max(0, comp.DropsPerCycle - comp.DropsUsed)),
                ("drops", comp.DropsPerCycle)),
        };

        switch (comp.Phase)
        {
            case DerelictPhase.Orbit:
                lines.Add(Loc.GetString("derelict-status-orbit"));
                break;
            case DerelictPhase.Jumping:
                lines.Add(Loc.GetString("derelict-status-jumping", ("site", SiteName(comp.CurrentSite))));
                break;
            case DerelictPhase.OnSite:
            {
                var left = comp.SiteEnd - Timing.CurTime;
                if (left < TimeSpan.Zero)
                    left = TimeSpan.Zero;

                lines.Add(Loc.GetString("derelict-status-onsite",
                    ("site", SiteName(comp.CurrentSite)),
                    ("time", $"{(int) left.TotalMinutes}:{left.Seconds:00}")));
                break;
            }
            case DerelictPhase.Leaving:
                lines.Add(Loc.GetString("derelict-status-leaving"));
                break;
            case DerelictPhase.QuotaDue:
            {
                var left = comp.QuotaDeadline - Timing.CurTime;
                if (left < TimeSpan.Zero)
                    left = TimeSpan.Zero;

                lines.Add(Loc.GetString("derelict-status-quota-due",
                    ("time", $"{(int) left.TotalMinutes}:{left.Seconds:00}")));
                break;
            }
            case DerelictPhase.Finished:
                lines.Add(Loc.GetString("derelict-status-finished"));
                break;
        }

        return string.Join("\n", lines);
    }
}
