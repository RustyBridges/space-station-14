using Content.Server._Derelict.Components;
using Content.Shared._Derelict;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server._Derelict;

public sealed partial class DerelictRunRuleSystem
{
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<DerelictNavConsoleComponent, GetVerbsEvent<ActivationVerb>>(OnConsoleVerbs);
        SubscribeLocalEvent<DerelictNavConsoleComponent, ExaminedEvent>(OnConsoleExamined);
    }

    private void OnConsoleVerbs(Entity<DerelictNavConsoleComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !TryGetRun(out var run))
            return;

        var comp = run.Comp;
        var user = args.User;
        var console = ent.Owner;

        switch (comp.Phase)
        {
            case DerelictPhase.Orbit:
            {
                var priority = 0;
                foreach (var siteId in comp.Offered)
                {
                    if (!_proto.TryIndex(siteId, out var site))
                        continue;

                    var id = siteId;
                    args.Verbs.Add(new ActivationVerb
                    {
                        Text = Loc.GetString("derelict-nav-verb-launch",
                            ("site", Loc.GetString(site.Name)),
                            ("hazard", site.Hazard),
                            ("minutes", (int) site.Duration.TotalMinutes)),
                        Priority = priority--,
                        ConfirmationPopup = true,
                        Act = () => ConsoleLaunch(console, user, id),
                    });
                }

                break;
            }
            case DerelictPhase.OnSite:
                args.Verbs.Add(new ActivationVerb
                {
                    Text = Loc.GetString("derelict-nav-verb-depart"),
                    ConfirmationPopup = true,
                    Act = () =>
                    {
                        if (TryGetRun(out var current) && !Depart(current))
                            _popup.PopupEntity(Loc.GetString("derelict-nav-fail-ftl"), console, user, PopupType.MediumCaution);
                    },
                });
                break;
            case DerelictPhase.QuotaDue:
                args.Verbs.Add(new ActivationVerb
                {
                    Text = Loc.GetString("derelict-nav-verb-quota",
                        ("earned", comp.EarnedThisCycle),
                        ("quota", comp.Quota)),
                    ConfirmationPopup = true,
                    Act = () =>
                    {
                        if (TryGetRun(out var current))
                            SubmitQuota(current);
                    },
                });
                break;
        }
    }

    private void ConsoleLaunch(EntityUid console, EntityUid user, ProtoId<DerelictSitePrototype> site)
    {
        if (!TryGetRun(out var run))
            return;

        if (!Launch(run, site, out var reason) && reason != null)
            _popup.PopupEntity(reason, console, user, PopupType.MediumCaution);
    }

    private void OnConsoleExamined(Entity<DerelictNavConsoleComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryGetRun(out var run))
            return;

        args.PushMarkup(GetStatusText(run.Comp));
    }

    /// <summary>
    /// Multi-line status of the run, shown when examining the navigation console.
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
