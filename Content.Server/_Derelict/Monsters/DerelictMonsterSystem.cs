using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.NPC;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Derelict.Monsters;

/// <summary>
/// Drives the derelict threats. Every behaviour is one simple rule so that crews can learn to survive it:
/// the watcher freezes while looked at, the listener only knows where noise came from,
/// and the stalker only goes after people who are alone.
/// </summary>
public sealed partial class DerelictMonsterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private Content.Shared.Damage.Systems.DamageableSystem _damageable = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>Moving faster than this counts as running, which the listener hears.</summary>
    private const float RunSpeed = 3.2f;

    /// <summary>How long after speaking a crew member still counts as making noise.</summary>
    private static readonly TimeSpan SpeechMemory = TimeSpan.FromSeconds(2);

    private readonly Dictionary<EntityUid, TimeSpan> _lastSpoke = new();
    private readonly List<Crew> _crew = new();

    private record struct Crew(EntityUid Uid, EntityUid? Map, Vector2 Position, Angle Facing, bool Running, bool Speaking);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DerelictMonsterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DerelictMonsterComponent, UpdateCanMoveEvent>(OnCanMove);
        SubscribeLocalEvent<DerelictMonsterComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnMapInit(Entity<DerelictMonsterComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<ActiveNPCComponent>(ent);
        _combat.SetInCombatMode(ent, true);
        ent.Comp.NextThink = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, 1f));
    }

    private void OnCanMove(Entity<DerelictMonsterComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.Observed)
            args.Cancel();
    }

    private void OnAttackAttempt(Entity<DerelictMonsterComponent> ent, ref AttackAttemptEvent args)
    {
        if (ent.Comp.Observed)
            args.Cancel();
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        // Whispers are quiet enough.
        if (args.ObfuscatedMessage != null)
            return;

        _lastSpoke[args.Source] = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var gathered = false;

        var query = EntityQueryEnumerator<DerelictMonsterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (now < comp.NextThink)
                continue;

            comp.NextThink = now + comp.ThinkInterval;

            if (!_mobState.IsAlive(uid))
            {
                _steering.Unregister(uid);
                continue;
            }

            if (!gathered)
            {
                GatherCrew(now);
                gathered = true;
            }

            switch (comp.Behavior)
            {
                case DerelictMonsterBehavior.Watcher:
                    ThinkWatcher((uid, comp, xform));
                    break;
                case DerelictMonsterBehavior.Listener:
                    ThinkListener((uid, comp, xform), now);
                    break;
                case DerelictMonsterBehavior.Stalker:
                    ThinkStalker((uid, comp, xform), now);
                    break;
            }
        }
    }

    private void GatherCrew(TimeSpan now)
    {
        _crew.Clear();

        var query = EntityQueryEnumerator<HumanoidProfileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            var (position, rotation) = _xform.GetWorldPositionRotation(xform);
            var running = TryComp<PhysicsComponent>(uid, out var physics) && physics.LinearVelocity.Length() > RunSpeed;
            var speaking = _lastSpoke.TryGetValue(uid, out var spoke) && now - spoke <= SpeechMemory;
            _crew.Add(new Crew(uid, xform.MapUid, position, rotation, running, speaking));
        }

        // Forget speakers that no longer matter.
        if (_lastSpoke.Count > 64)
            _lastSpoke.Clear();
    }

    #region Watcher

    private void ThinkWatcher(Entity<DerelictMonsterComponent, TransformComponent> ent)
    {
        var comp = ent.Comp1;
        var position = _xform.GetWorldPosition(ent.Comp2);

        var observed = false;
        Crew? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var crew in _crew)
        {
            if (crew.Map != ent.Comp2.MapUid)
                continue;

            var delta = position - crew.Position;
            var distance = delta.Length();

            if (distance < nearestDistance && distance <= comp.SenseRange)
            {
                nearest = crew;
                nearestDistance = distance;
            }

            if (observed || distance > comp.ObserveRange)
                continue;

            // Standing on top of it counts as looking at it.
            if (distance > 0.75f)
            {
                var facing = crew.Facing.ToWorldVec();
                var cos = Vector2.Dot(facing, delta / distance);
                if (cos < MathF.Cos(MathHelper.DegreesToRadians(comp.ObserveHalfAngle)))
                    continue;
            }

            if (_examine.InRangeUnOccluded(crew.Uid, ent.Owner, comp.ObserveRange))
                observed = true;
        }

        SetObserved((ent.Owner, comp), observed);

        if (observed || nearest == null)
        {
            _steering.Unregister(ent.Owner);
            return;
        }

        Chase(ent.Owner, nearest.Value.Uid);
        if (nearestDistance <= comp.AttackRange)
            Attack(ent.Owner, nearest.Value.Uid);
    }

    private void SetObserved(Entity<DerelictMonsterComponent> ent, bool observed)
    {
        if (ent.Comp.Observed == observed)
            return;

        ent.Comp.Observed = observed;
        _blocker.UpdateCanMove(ent.Owner);
    }

    #endregion

    #region Listener

    private void ThinkListener(Entity<DerelictMonsterComponent, TransformComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp1;
        var position = _xform.GetWorldPosition(ent.Comp2);

        Crew? loudest = null;
        var loudestDistance = float.MaxValue;
        Crew? touching = null;

        foreach (var crew in _crew)
        {
            if (crew.Map != ent.Comp2.MapUid)
                continue;

            var distance = (position - crew.Position).Length();

            // Bumping into it gives you away no matter how quiet you are.
            if (distance <= comp.AttackRange * 0.75f || distance <= comp.AttackRange && (crew.Running || crew.Speaking))
                touching ??= crew;

            var heard = crew.Running && distance <= comp.SenseRange
                        || crew.Speaking && distance <= comp.SpeechRange;

            if (heard && distance < loudestDistance)
            {
                loudest = crew;
                loudestDistance = distance;
            }
        }

        if (touching != null)
            Attack(ent.Owner, touching.Value.Uid);

        if (loudest != null)
        {
            // It only knows where the sound came from, not where you went afterwards.
            comp.Heard = Transform(loudest.Value.Uid).Coordinates;
            comp.NextWander = now + TimeSpan.FromSeconds(6);
        }

        if (comp.Heard is { } heardAt)
        {
            if (!heardAt.IsValid(EntityManager))
            {
                comp.Heard = null;
                return;
            }

            var target = _xform.ToMapCoordinates(heardAt).Position;
            if ((target - position).Length() <= 1f)
            {
                comp.Heard = null;
                _steering.Unregister(ent.Owner);
                return;
            }

            MoveTo(ent.Owner, heardAt);
            return;
        }

        Wander(ent, now);
    }

    #endregion

    #region Stalker

    private void ThinkStalker(Entity<DerelictMonsterComponent, TransformComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp1;
        var position = _xform.GetWorldPosition(ent.Comp2);

        Crew? prey = null;
        var preyDistance = float.MaxValue;
        Crew? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var crew in _crew)
        {
            if (crew.Map != ent.Comp2.MapUid)
                continue;

            var distance = (position - crew.Position).Length();
            if (distance > comp.SenseRange)
                continue;

            if (distance < nearestDistance)
            {
                nearest = crew;
                nearestDistance = distance;
            }

            if (distance < preyDistance && IsAlone(crew, comp.CompanyRange))
            {
                prey = crew;
                preyDistance = distance;
            }
        }

        if (prey != null)
        {
            Chase(ent.Owner, prey.Value.Uid);
            if (preyDistance <= comp.AttackRange)
                Attack(ent.Owner, prey.Value.Uid);

            return;
        }

        // A group is too much for it: keep its distance.
        if (nearest != null && nearestDistance < comp.CompanyRange * 2f)
        {
            var away = position - nearest.Value.Position;
            away = away.LengthSquared() < 0.01f ? _random.NextAngle().ToVec() : Vector2.Normalize(away);
            var fleeTo = new MapCoordinates(position + away * 6f, ent.Comp2.MapID);
            MoveTo(ent.Owner, _xform.ToCoordinates(fleeTo));
            return;
        }

        Wander(ent, now);
    }

    private bool IsAlone(Crew crew, float range)
    {
        foreach (var other in _crew)
        {
            if (other.Uid == crew.Uid || other.Map != crew.Map)
                continue;

            if ((other.Position - crew.Position).Length() <= range)
                return false;
        }

        return true;
    }

    #endregion

    /// <summary>
    /// Lists every threat and every humanoid with position and health. For admins and tests.
    /// </summary>
    public string Describe()
    {
        var lines = new List<string>();
        var monsters = EntityQueryEnumerator<DerelictMonsterComponent, TransformComponent>();
        while (monsters.MoveNext(out var uid, out var comp, out var xform))
        {
            var pos = _xform.GetWorldPosition(xform);
            lines.Add($"{comp.Behavior} {ToPrettyString(uid)} map {xform.MapUid} at {pos.X:0.0},{pos.Y:0.0} observed={comp.Observed} heard={comp.Heard != null}");
        }

        var crew = EntityQueryEnumerator<HumanoidProfileComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out _, out var xform))
        {
            var pos = _xform.GetWorldPosition(xform);
            var damage = _damageable.GetTotalDamage(uid).Float();
            lines.Add($"crew {ToPrettyString(uid)} map {xform.MapUid} at {pos.X:0.0},{pos.Y:0.0} alive={_mobState.IsAlive(uid)} damage={damage:0}");
        }

        return string.Join("\n", lines);
    }

    #region Shared behaviour

    private void Chase(EntityUid uid, EntityUid target)
    {
        MoveTo(uid, new EntityCoordinates(target, Vector2.Zero));
    }

    private void MoveTo(EntityUid uid, EntityCoordinates coordinates)
    {
        var steering = _steering.Register(uid, coordinates);
        steering.Flags = PathFlags.Interact | PathFlags.Prying;
        steering.Range = 0.8f;
    }

    private void Attack(EntityUid uid, EntityUid target)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var weapon))
            return;

        _melee.AttemptLightAttack(uid, uid, weapon, target);
    }

    /// <summary>
    /// Shuffles around aimlessly so the derelict never feels empty.
    /// </summary>
    private void Wander(Entity<DerelictMonsterComponent, TransformComponent> ent, TimeSpan now)
    {
        if (now < ent.Comp1.NextWander)
            return;

        ent.Comp1.NextWander = now + TimeSpan.FromSeconds(_random.NextFloat(5f, 12f));

        var offset = _random.NextAngle().ToVec() * _random.NextFloat(2f, 6f);
        MoveTo(ent.Owner, ent.Comp2.Coordinates.Offset(offset));
    }

    #endregion
}
