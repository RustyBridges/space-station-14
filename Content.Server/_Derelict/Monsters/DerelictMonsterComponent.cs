using Robust.Shared.Map;

namespace Content.Server._Derelict.Monsters;

public enum DerelictMonsterBehavior : byte
{
    /// <summary>Only moves and attacks while no living crew member is looking at it.</summary>
    Watcher,
    /// <summary>Blind. Goes to where it last heard running or talking and attacks whatever it touches.</summary>
    Listener,
    /// <summary>Hunts crew who are on their own and backs off from groups.</summary>
    Stalker,
}

/// <summary>
/// A derelict threat driven by <see cref="DerelictMonsterSystem"/>. Each behaviour follows one rule the crew can learn.
/// </summary>
[RegisterComponent, Access(typeof(DerelictMonsterSystem))]
public sealed partial class DerelictMonsterComponent : Component
{
    [DataField(required: true)]
    public DerelictMonsterBehavior Behavior;

    /// <summary>How far away it notices crew (watcher, stalker) or hears running (listener).</summary>
    [DataField]
    public float SenseRange = 20f;

    /// <summary>Listener only: how far away it hears speech.</summary>
    [DataField]
    public float SpeechRange = 14f;

    /// <summary>Watcher only: how far away a crew member can be and still pin it by looking.</summary>
    [DataField]
    public float ObserveRange = 11f;

    /// <summary>Watcher only: half-angle of the cone in front of a crew member that counts as looking, in degrees.</summary>
    [DataField]
    public float ObserveHalfAngle = 65f;

    /// <summary>Stalker only: a crew member with a living crewmate within this distance is not alone.</summary>
    [DataField]
    public float CompanyRange = 5f;

    [DataField]
    public float AttackRange = 1.4f;

    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.2);

    [ViewVariables] public TimeSpan NextThink;
    [ViewVariables] public TimeSpan NextWander;

    /// <summary>Watcher: currently pinned by someone's gaze.</summary>
    [ViewVariables] public bool Observed;

    /// <summary>Listener: where it last heard something, if it has not checked that spot yet.</summary>
    [ViewVariables] public EntityCoordinates? Heard;
}
