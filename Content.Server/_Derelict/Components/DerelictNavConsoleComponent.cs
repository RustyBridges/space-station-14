namespace Content.Server._Derelict.Components;

/// <summary>
/// The tug's navigation uplink: used to pick a derelict, leave it again and submit the quota.
/// </summary>
[RegisterComponent]
public sealed partial class DerelictNavConsoleComponent : Component;

/// <summary>
/// Marks the bonus objective of a site. Selling it pays out and is counted in the run summary.
/// </summary>
[RegisterComponent]
public sealed partial class DerelictObjectiveComponent : Component;
