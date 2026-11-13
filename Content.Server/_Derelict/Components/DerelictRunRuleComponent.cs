using Content.Shared._Derelict;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._Derelict.Components;

public enum DerelictPhase : byte
{
    /// <summary>Waiting for the ship/station to exist.</summary>
    Setup,
    /// <summary>Parked in deep space; crew sells, shops and picks the next site.</summary>
    Orbit,
    /// <summary>FTL towards a site.</summary>
    Jumping,
    /// <summary>Docked at / next to a derelict, clock running.</summary>
    OnSite,
    /// <summary>FTL back to deep space.</summary>
    Leaving,
    /// <summary>All drops of the cycle are used up; the quota has to be submitted.</summary>
    QuotaDue,
    /// <summary>Run is over.</summary>
    Finished,
}

/// <summary>
/// Game rule that drives a Derelict run: drops, the site clock, escalation, selling and the quota.
/// </summary>
[RegisterComponent, Access(typeof(DerelictRunRuleSystem), Other = AccessPermissions.Read)]
public sealed partial class DerelictRunRuleComponent : Component
{
    // ---- Configuration ----

    /// <summary>Sites that can be offered.</summary>
    [DataField(required: true)]
    public List<ProtoId<DerelictSitePrototype>> Sites = new();

    /// <summary>How many sites the navigation console offers at a time.</summary>
    [DataField]
    public int SiteChoices = 3;

    /// <summary>Drops the crew gets before the quota is due.</summary>
    [DataField]
    public int DropsPerCycle = 3;

    [DataField]
    public int StartingQuota = 2500;

    /// <summary>Quota multiplier applied after every met quota.</summary>
    [DataField]
    public float QuotaGrowth = 1.4f;

    [DataField]
    public int StartingCredits = 500;

    /// <summary>Charged for every crew member that has to be replaced with a fresh clone.</summary>
    [DataField]
    public int ReplacementFee = 250;

    /// <summary>Job given to replacement clones.</summary>
    [DataField]
    public string CrewJob = "DerelictSalvager";

    /// <summary>Threats that can be on a site. Picked at random.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Monsters = new();

    /// <summary>Scrap spawners scattered through a site. Picked at random.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Scrap = new();

    /// <summary>The bonus objective item hidden on every site.</summary>
    [DataField]
    public EntProtoId Objective = "DerelictFlightRecorder";

    /// <summary>How long the crew gets to submit the quota before it is submitted for them.</summary>
    [DataField]
    public TimeSpan QuotaGrace = TimeSpan.FromMinutes(6);

    [DataField]
    public float FtlStartup = 8f;

    [DataField]
    public float FtlTravel = 18f;

    [DataField]
    public TimeSpan RestartDelay = TimeSpan.FromSeconds(45);

    // ---- State ----

    [ViewVariables] public DerelictPhase Phase = DerelictPhase.Setup;
    [ViewVariables] public EntityUid Station;
    [ViewVariables] public EntityUid Ship;
    [ViewVariables] public EntityUid HomeMap;
    [ViewVariables] public System.Numerics.Vector2 HomePosition;

    [ViewVariables] public int Cycle = 1;
    /// <summary>Drops already used in the current cycle.</summary>
    [ViewVariables] public int DropsUsed;
    [ViewVariables] public int Quota;
    [ViewVariables] public int EarnedThisCycle;
    [ViewVariables] public int TotalEarned;
    [ViewVariables] public int TotalDrops;
    [ViewVariables] public int CrewLost;
    [ViewVariables] public int RecordersRecovered;

    [ViewVariables] public List<ProtoId<DerelictSitePrototype>> Offered = new();

    [ViewVariables] public ProtoId<DerelictSitePrototype>? CurrentSite;
    [ViewVariables] public EntityUid? SiteMap;
    [ViewVariables] public EntityUid? SiteGrid;
    [ViewVariables] public TimeSpan SiteStart;
    [ViewVariables] public TimeSpan SiteEnd;
    [ViewVariables] public TimeSpan NextEscalation;
    [ViewVariables] public bool PowerFailed;
    [ViewVariables] public bool HullBreached;
    [ViewVariables] public bool WarnedThree;
    [ViewVariables] public bool WarnedOne;
    [ViewVariables] public TimeSpan NextWipeCheck;

    [ViewVariables] public TimeSpan QuotaDeadline;

    /// <summary>Everyone who has joined the crew this round.</summary>
    [ViewVariables] public HashSet<NetUserId> Crew = new();

    /// <summary>Set when the run failed, for the round end summary.</summary>
    [ViewVariables] public bool Failed;
}
