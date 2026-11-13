using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Derelict;

/// <summary>
/// A derelict the salvage crew can jump to during a run.
/// </summary>
[Prototype]
public sealed partial class DerelictSitePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Name shown on the navigation console and in announcements.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Grid file that is loaded as the derelict.
    /// </summary>
    [DataField(required: true)]
    public ResPath MapPath;

    /// <summary>
    /// 1 (calm) to 3 (lethal). Scales how many threats are on site and how fast it escalates.
    /// </summary>
    [DataField]
    public int Hazard = 1;

    /// <summary>
    /// How long the tug can stay before the autopilot pulls it out.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(12);

    /// <summary>
    /// Number of additional scrap spawners scattered through the derelict on top of whatever the map holds.
    /// </summary>
    [DataField]
    public int ExtraScrap = 14;
}
