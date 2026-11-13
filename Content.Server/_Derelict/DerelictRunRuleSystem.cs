using System.Linq;
using System.Numerics;
using Content.Server._Derelict.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._Derelict;
using Content.Shared.Cargo.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking.Rules;
using Content.Shared.Ghost.Components;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Station.Systems;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Derelict;

/// <summary>
/// Runs the Derelict game mode. A crew of up to four flies a salvage tug between derelicts, hauls back
/// whatever sells, and has to meet a rising quota every few drops.
/// </summary>
public sealed partial class DerelictRunRuleSystem : GameRuleSystem<DerelictRunRuleComponent>
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ApcSystem _apc = default!;
    [Dependency] private CargoSystem _cargo = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private TurfSystem _turf = default!;

    private static readonly Color AnnounceColor = Color.FromHex("#e0a13a");
    private static readonly Color DangerColor = Color.FromHex("#d8452f");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FTLCompletedEvent>(OnFtlCompleted);
        SubscribeLocalEvent<EntitySoldEvent>(OnEntitySold);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        InitializeConsole();
    }

    #region Lookup

    /// <summary>
    /// Returns the active run, if there is one.
    /// </summary>
    public bool TryGetRun(out Entity<DerelictRunRuleComponent> run)
    {
        var query = EntityQueryEnumerator<DerelictRunRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var comp, out var rule))
        {
            if (!GameTicker.IsGameRuleActive((uid, rule)))
                continue;

            run = (uid, comp);
            return true;
        }

        run = default;
        return false;
    }

    private string SiteName(ProtoId<DerelictSitePrototype>? site)
    {
        return site != null && _proto.TryIndex(site.Value, out var proto)
            ? Loc.GetString(proto.Name)
            : Loc.GetString("derelict-site-unknown");
    }

    private void Announce(string message, Color? color = null, bool sound = true)
    {
        _chat.DispatchGlobalAnnouncement(
            message,
            Loc.GetString("derelict-announcer"),
            playSound: sound,
            colorOverride: color ?? AnnounceColor);
    }

    #endregion

    #region Setup

    protected override void Started(Entity<DerelictRunRuleComponent, GameRuleComponent> rule, ref GameRuleStartedEvent args)
    {
        base.Started(rule, ref args);
        TrySetup((rule.Owner, rule.Comp1));
    }

    private bool TrySetup(Entity<DerelictRunRuleComponent> run)
    {
        var comp = run.Comp;
        if (comp.Phase != DerelictPhase.Setup)
            return true;

        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return false;

        foreach (var station in _station.GetStations())
        {
            if (_station.GetLargestGrid((station.Owner, station.Comp)) is not { } grid)
                continue;

            if (!HasComp<ShuttleComponent>(grid))
                continue;

            comp.Station = station.Owner;
            comp.Ship = grid;
            break;
        }

        if (!comp.Ship.IsValid())
        {
            Log.Debug($"Derelict setup: no shuttle grid found among {_station.GetStations().Count} stations (run level {GameTicker.RunLevel}).");
            return false;
        }

        var shipXform = Transform(comp.Ship);
        if (shipXform.MapUid is not { } homeMap)
            return false;

        comp.HomeMap = homeMap;
        comp.HomePosition = _xform.GetWorldPosition(shipXform);

        // The tug is its own trade post: bought crates appear on its buy pallets.
        EnsureComp<TradeStationComponent>(comp.Ship);

        if (TryComp<StationBankAccountComponent>(comp.Station, out var bank))
        {
            foreach (var account in bank.Accounts.Keys.ToList())
            {
                _cargo.TrySetBankAccount((comp.Station, bank),
                    account,
                    account == bank.PrimaryAccount ? comp.StartingCredits : 0);
            }
        }

        comp.Quota = comp.StartingQuota;
        comp.Phase = DerelictPhase.Orbit;
        RollOffers(comp);

        Announce(Loc.GetString("derelict-announce-start",
            ("quota", comp.Quota),
            ("drops", comp.DropsPerCycle)));
        return true;
    }

    /// <summary>
    /// Describes what setup can see. For diagnosing a run that is stuck in <see cref="DerelictPhase.Setup"/>.
    /// </summary>
    public string DescribeSetup()
    {
        var lines = new List<string> { $"run level: {GameTicker.RunLevel}" };
        foreach (var station in _station.GetStations())
        {
            var grid = _station.GetLargestGrid((station.Owner, station.Comp));
            lines.Add($"station {ToPrettyString(station.Owner)}: largest grid {ToPrettyString(grid)}, shuttle: {grid != null && HasComp<ShuttleComponent>(grid)}, map: {(grid != null ? Transform(grid.Value).MapUid : null)}");
        }

        return string.Join("\n", lines);
    }

    private void RollOffers(DerelictRunRuleComponent comp)
    {
        comp.Offered.Clear();
        var pool = comp.Sites.ToList();
        RobustRandom.Shuffle(pool);
        comp.Offered.AddRange(pool.Take(Math.Max(1, comp.SiteChoices)));
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        if (!TryGetRun(out var run))
            return;

        if (ev.JobId == run.Comp.CrewJob)
            run.Comp.Crew.Add(ev.Player.UserId);
    }

    #endregion

    #region Tick

    protected override void ActiveTick(EntityUid uid, DerelictRunRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);

        switch (comp.Phase)
        {
            case DerelictPhase.Setup:
                TrySetup((uid, comp));
                break;
            case DerelictPhase.OnSite:
                TickSite((uid, comp));
                break;
            case DerelictPhase.QuotaDue:
                if (Timing.CurTime >= comp.QuotaDeadline)
                    SubmitQuota((uid, comp));
                break;
        }
    }

    private void TickSite(Entity<DerelictRunRuleComponent> run)
    {
        var comp = run.Comp;
        var now = Timing.CurTime;

        if (comp.CurrentSite == null || !_proto.TryIndex(comp.CurrentSite.Value, out var site))
            return;

        var total = comp.SiteEnd - comp.SiteStart;
        var elapsed = now - comp.SiteStart;
        var left = comp.SiteEnd - now;

        // More things wake up the longer the crew stays.
        if (now >= comp.NextEscalation)
        {
            comp.NextEscalation = now + EscalationInterval(site);
            if (SpawnMonster(comp))
                Announce(Loc.GetString("derelict-announce-escalation"), DangerColor, false);
        }

        // The derelict itself gives out: first the power, then the hull.
        if (!comp.PowerFailed && elapsed >= total * 0.5)
        {
            comp.PowerFailed = true;
            KillSitePower(comp);
            Announce(Loc.GetString("derelict-announce-power"), DangerColor);
        }

        if (!comp.HullBreached && elapsed >= total * 0.75)
        {
            comp.HullBreached = true;
            BreachSiteHull(comp, 2 + site.Hazard);
            Announce(Loc.GetString("derelict-announce-hull"), DangerColor);
        }

        if (!comp.WarnedThree && left <= TimeSpan.FromMinutes(3))
        {
            comp.WarnedThree = true;
            Announce(Loc.GetString("derelict-announce-three-minutes"));
        }

        if (!comp.WarnedOne && left <= TimeSpan.FromMinutes(1))
        {
            comp.WarnedOne = true;
            Announce(Loc.GetString("derelict-announce-one-minute"), DangerColor);
        }

        if (left <= TimeSpan.Zero)
        {
            if (Depart(run))
                Announce(Loc.GetString("derelict-announce-autodepart"), DangerColor);

            return;
        }

        // Nobody left to fly the tug: the autopilot brings it home.
        if (now >= comp.NextWipeCheck)
        {
            comp.NextWipeCheck = now + TimeSpan.FromSeconds(5);
            if (comp.Crew.Count > 0 && elapsed > TimeSpan.FromSeconds(30) && !AnyCrewAlive(comp))
            {
                if (Depart(run))
                    Announce(Loc.GetString("derelict-announce-wipe"), DangerColor);
            }
        }
    }

    /// <summary>
    /// Moves the site clock (or the quota deadline) forward. For admins and tests.
    /// </summary>
    public void SkipTime(Entity<DerelictRunRuleComponent> run, TimeSpan time)
    {
        run.Comp.SiteStart -= time;
        run.Comp.SiteEnd -= time;
        run.Comp.NextEscalation -= time;
        run.Comp.QuotaDeadline -= time;
    }

    private static TimeSpan EscalationInterval(DerelictSitePrototype site)
    {
        return site.Duration / (site.Hazard + 3);
    }

    #endregion

    #region Launch

    /// <summary>
    /// Loads the given site and jumps the tug to it.
    /// </summary>
    public bool Launch(Entity<DerelictRunRuleComponent> run, ProtoId<DerelictSitePrototype> siteId, out string? failReason)
    {
        var comp = run.Comp;
        failReason = null;

        if (comp.Phase != DerelictPhase.Orbit)
        {
            failReason = Loc.GetString("derelict-nav-fail-phase");
            return false;
        }

        if (!_proto.TryIndex(siteId, out var site))
        {
            failReason = Loc.GetString("derelict-nav-fail-site");
            return false;
        }

        if (!TryComp<ShuttleComponent>(comp.Ship, out var shuttle)
            || HasComp<FTLComponent>(comp.Ship)
            || !_shuttle.CanFTL(comp.Ship, out _))
        {
            failReason = Loc.GetString("derelict-nav-fail-ftl");
            return false;
        }

        // Anyone still dead gets replaced before the next drop.
        ReplaceMissingCrew(run, includeBodiesAboard: true);

        var mapUid = _map.CreateMap(out var mapId, runMapInit: false);
        if (!_loader.TryLoadGrid(mapId, site.MapPath, out var grid))
        {
            Del(mapUid);
            failReason = Loc.GetString("derelict-nav-fail-site");
            Log.Error($"Failed to load derelict site {siteId} from {site.MapPath}");
            return false;
        }

        _map.InitializeMap(mapId);
        _metaData.SetEntityName(mapUid, Loc.GetString(site.Name));

        comp.CurrentSite = siteId;
        comp.SiteMap = mapUid;
        comp.SiteGrid = grid.Value.Owner;
        comp.PowerFailed = false;
        comp.HullBreached = false;
        comp.WarnedThree = false;
        comp.WarnedOne = false;
        comp.Phase = DerelictPhase.Jumping;
        comp.DropsUsed++;
        comp.TotalDrops++;

        PopulateSite(comp, site);

        _shuttle.FTLToDock(comp.Ship, shuttle, grid.Value.Owner, comp.FtlStartup, comp.FtlTravel);

        Announce(Loc.GetString("derelict-announce-launch",
            ("site", Loc.GetString(site.Name)),
            ("hazard", site.Hazard),
            ("drop", comp.DropsUsed),
            ("drops", comp.DropsPerCycle)));
        return true;
    }

    private void PopulateSite(DerelictRunRuleComponent comp, DerelictSitePrototype site)
    {
        if (comp.SiteGrid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tiles = GetFreeTiles((gridUid, grid));
        if (tiles.Count == 0)
            return;

        if (comp.Scrap.Count > 0)
        {
            for (var i = 0; i < site.ExtraScrap; i++)
            {
                Spawn(RobustRandom.Pick(comp.Scrap), RobustRandom.Pick(tiles));
            }
        }

        var objective = Spawn(comp.Objective, RobustRandom.Pick(tiles));
        EnsureComp<DerelictObjectiveComponent>(objective);

        for (var i = 0; i < site.Hazard; i++)
        {
            SpawnMonster(comp, tiles);
        }
    }

    /// <summary>
    /// All walkable, unobstructed tiles of a grid, as coordinates at the tile centre.
    /// </summary>
    private List<EntityCoordinates> GetFreeTiles(Entity<MapGridComponent> grid)
    {
        var result = new List<EntityCoordinates>();
        foreach (var tile in _map.GetAllTiles(grid.Owner, grid.Comp))
        {
            if (tile.Tile.IsEmpty || _turf.IsSpace(tile))
                continue;

            if (_turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                continue;

            result.Add(_map.GridTileToLocal(grid.Owner, grid.Comp, tile.GridIndices));
        }

        return result;
    }

    private bool SpawnMonster(DerelictRunRuleComponent comp, List<EntityCoordinates>? tiles = null)
    {
        if (comp.Monsters.Count == 0 || comp.SiteGrid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        tiles ??= GetFreeTiles((gridUid, grid));
        if (tiles.Count == 0)
            return false;

        // Never wake something up right on top of the crew.
        var crew = new List<Vector2>();
        var query = EntityQueryEnumerator<HumanoidProfileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == comp.SiteMap && _mobState.IsAlive(uid))
                crew.Add(_xform.GetWorldPosition(xform));
        }

        const float minDistance = 12f;
        EntityCoordinates? best = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var candidate = RobustRandom.Pick(tiles);
            best ??= candidate;
            var pos = _xform.ToMapCoordinates(candidate).Position;
            if (crew.All(c => (c - pos).Length() >= minDistance))
            {
                best = candidate;
                break;
            }
        }

        Spawn(RobustRandom.Pick(comp.Monsters), best!.Value);
        return true;
    }

    #endregion

    #region Site decay

    private void KillSitePower(DerelictRunRuleComponent comp)
    {
        var query = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var apc, out var xform))
        {
            if (xform.GridUid != comp.SiteGrid || !apc.MainBreakerEnabled)
                continue;

            _apc.ApcToggleBreaker(uid, apc);
        }
    }

    private void BreachSiteHull(DerelictRunRuleComponent comp, int breaches)
    {
        if (comp.SiteGrid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tiles = _map.GetAllTiles(gridUid, grid).Where(t => !t.Tile.IsEmpty).ToList();
        if (tiles.Count == 0)
            return;

        for (var i = 0; i < breaches; i++)
        {
            var tile = RobustRandom.Pick(tiles);
            var coords = _xform.ToMapCoordinates(_map.GridTileToLocal(gridUid, grid, tile.GridIndices));
            _explosion.QueueExplosion(coords, "Default", 60f, 4f, 12f, null);
        }
    }

    #endregion

    #region Departure

    /// <summary>
    /// Pulls the tug out of the current site, leaving behind whoever is not aboard.
    /// </summary>
    public bool Depart(Entity<DerelictRunRuleComponent> run)
    {
        var comp = run.Comp;
        if (comp.Phase != DerelictPhase.OnSite)
            return false;

        // The drive is still spooling down from the last jump.
        if (!TryComp<ShuttleComponent>(comp.Ship, out var shuttle) || HasComp<FTLComponent>(comp.Ship))
            return false;

        comp.Phase = DerelictPhase.Leaving;
        _shuttle.FTLToCoordinates(comp.Ship,
            shuttle,
            new EntityCoordinates(comp.HomeMap, comp.HomePosition),
            Angle.Zero,
            comp.FtlStartup,
            comp.FtlTravel);

        Announce(Loc.GetString("derelict-announce-depart", ("seconds", (int) comp.FtlStartup)));
        return true;
    }

    private void OnFtlCompleted(ref FTLCompletedEvent args)
    {
        if (!TryGetRun(out var run) || args.Entity != run.Comp.Ship)
            return;

        var comp = run.Comp;
        switch (comp.Phase)
        {
            case DerelictPhase.Jumping:
                ArriveAtSite(run);
                break;
            case DerelictPhase.Leaving:
                ArriveHome(run);
                break;
        }
    }

    private void ArriveAtSite(Entity<DerelictRunRuleComponent> run)
    {
        var comp = run.Comp;
        if (comp.CurrentSite == null || !_proto.TryIndex(comp.CurrentSite.Value, out var site))
            return;

        var now = Timing.CurTime;
        comp.Phase = DerelictPhase.OnSite;
        comp.SiteStart = now;
        comp.SiteEnd = now + site.Duration;
        comp.NextEscalation = now + EscalationInterval(site);
        comp.NextWipeCheck = now + TimeSpan.FromSeconds(30);

        Announce(Loc.GetString("derelict-announce-arrive",
            ("site", Loc.GetString(site.Name)),
            ("minutes", (int) site.Duration.TotalMinutes)));
    }

    private void ArriveHome(Entity<DerelictRunRuleComponent> run)
    {
        var comp = run.Comp;

        var replaced = ReplaceMissingCrew(run, includeBodiesAboard: false);

        if (comp.SiteMap is { } siteMap)
            QueueDel(siteMap);

        comp.SiteMap = null;
        comp.SiteGrid = null;
        comp.CurrentSite = null;

        var dropsLeft = comp.DropsPerCycle - comp.DropsUsed;
        if (dropsLeft > 0)
        {
            comp.Phase = DerelictPhase.Orbit;
            RollOffers(comp);
            Announce(Loc.GetString("derelict-announce-home",
                ("earned", comp.EarnedThisCycle),
                ("quota", comp.Quota),
                ("drops", dropsLeft),
                ("replaced", replaced)));
        }
        else
        {
            comp.Phase = DerelictPhase.QuotaDue;
            comp.QuotaDeadline = Timing.CurTime + comp.QuotaGrace;
            Announce(Loc.GetString("derelict-announce-quota-due",
                ("earned", comp.EarnedThisCycle),
                ("quota", comp.Quota),
                ("minutes", (int) comp.QuotaGrace.TotalMinutes),
                ("replaced", replaced)));
        }
    }

    #endregion

    #region Crew

    private bool AnyCrewAlive(DerelictRunRuleComponent comp)
    {
        foreach (var session in _player.Sessions)
        {
            if (!comp.Crew.Contains(session.UserId))
                continue;

            if (session.AttachedEntity is { } entity && _mobState.IsAlive(entity))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gives a fresh clone to every crew member who is dead or was left behind, for a fee.
    /// </summary>
    /// <param name="includeBodiesAboard">If false, dead crew whose body is on the tug are skipped so they can still be revived.</param>
    /// <returns>Number of crew replaced.</returns>
    private int ReplaceMissingCrew(Entity<DerelictRunRuleComponent> run, bool includeBodiesAboard)
    {
        var comp = run.Comp;
        var replace = new List<ICommonSession>();

        foreach (var session in _player.Sessions)
        {
            if (!comp.Crew.Contains(session.UserId))
                continue;

            if (NeedsReplacement(comp, session, includeBodiesAboard))
                replace.Add(session);
        }

        foreach (var session in replace)
        {
            _mind.WipeMind(session);
            GameTicker.MakeJoinGame(session, comp.Station, comp.CrewJob, silent: true);
        }

        if (replace.Count == 0)
            return 0;

        comp.CrewLost += replace.Count;
        ChargeFee(comp, replace.Count * comp.ReplacementFee);
        return replace.Count;
    }

    private bool NeedsReplacement(DerelictRunRuleComponent comp, ICommonSession session, bool includeBodiesAboard)
    {
        // Still in the lobby (e.g. after a manual respawn).
        if (!GameTicker.UserHasJoinedGame(session.UserId))
            return false;

        var body = session.AttachedEntity;
        if (body == null || HasComp<GhostComponent>(body))
        {
            // Fall back to the body the player left behind, if it still exists.
            body = _mind.TryGetMind(session.UserId, out _, out var mind) ? mind.OwnedEntity : null;
            if (body != null && HasComp<GhostComponent>(body))
                body = null;
        }

        if (body == null || TerminatingOrDeleted(body))
            return true;

        var aboard = Transform(body.Value).GridUid == comp.Ship;
        var alive = !_mobState.IsDead(body.Value);

        if (alive)
        {
            // Alive but not on the tug when it left: stranded.
            return !aboard && Transform(body.Value).MapUid != comp.HomeMap;
        }

        return includeBodiesAboard || !aboard;
    }

    private void ChargeFee(DerelictRunRuleComponent comp, int amount)
    {
        if (!TryComp<StationBankAccountComponent>(comp.Station, out var bank))
            return;

        var balance = _cargo.GetBalanceFromAccount((comp.Station, bank), bank.PrimaryAccount);
        _cargo.TrySetBankAccount((comp.Station, bank), bank.PrimaryAccount, Math.Max(0, balance - amount));
    }

    #endregion

    #region Money

    private void OnEntitySold(ref EntitySoldEvent args)
    {
        if (!TryGetRun(out var run) || args.Station != run.Comp.Station)
            return;

        var comp = run.Comp;
        var total = 0.0;
        var recorders = 0;
        foreach (var sold in args.Sold)
        {
            total += _pricing.GetPrice(sold);
            if (HasComp<DerelictObjectiveComponent>(sold))
                recorders++;
        }

        var earned = (int) Math.Round(total);
        comp.EarnedThisCycle += earned;
        comp.TotalEarned += earned;

        if (recorders > 0)
        {
            comp.RecordersRecovered += recorders;
            Announce(Loc.GetString("derelict-announce-recorder"));
        }
    }

    /// <summary>
    /// Settles the quota: either the next cycle starts or the run ends.
    /// </summary>
    public void SubmitQuota(Entity<DerelictRunRuleComponent> run)
    {
        var comp = run.Comp;
        if (comp.Phase != DerelictPhase.QuotaDue)
            return;

        if (comp.EarnedThisCycle >= comp.Quota)
        {
            var oldQuota = comp.Quota;
            comp.Cycle++;
            comp.DropsUsed = 0;
            comp.EarnedThisCycle = 0;
            comp.Quota = (int) (MathF.Round(oldQuota * comp.QuotaGrowth / 50f) * 50f);
            comp.Phase = DerelictPhase.Orbit;
            RollOffers(comp);

            Announce(Loc.GetString("derelict-announce-quota-met",
                ("old", oldQuota),
                ("quota", comp.Quota),
                ("drops", comp.DropsPerCycle)));
            return;
        }

        comp.Failed = true;
        comp.Phase = DerelictPhase.Finished;
        Announce(Loc.GetString("derelict-announce-quota-failed",
            ("earned", comp.EarnedThisCycle),
            ("quota", comp.Quota)), DangerColor);

        GameTicker.EndRound();
        var delay = comp.RestartDelay;
        Robust.Shared.Timing.Timer.Spawn(delay, () =>
        {
            if (GameTicker.RunLevel == GameRunLevel.PostRound)
                GameTicker.RestartRound();
        });
    }

    protected override void AppendRoundEndText(Entity<DerelictRunRuleComponent> rule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(rule, ref args);

        var comp = rule.Comp;
        args.AddLine(Loc.GetString(comp.Failed ? "derelict-round-end-failed" : "derelict-round-end-ended"));
        args.AddLine(Loc.GetString("derelict-round-end-stats",
            ("cycles", comp.Cycle - 1),
            ("drops", comp.TotalDrops),
            ("earned", comp.TotalEarned),
            ("lost", comp.CrewLost),
            ("recorders", comp.RecordersRecovered)));
    }

    #endregion
}
