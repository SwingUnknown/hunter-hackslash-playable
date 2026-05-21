using Godot;
using HunterHackslashNative;

[GlobalClass]
[GodotClassName("PlayerAdapter")]
[ScriptPath("res://scripts/PlayerAdapter.cs")]
public partial class PlayerAdapter : CharacterBody2D
{
    [Export] public float MaxSpeedUnits { get; set; } = 7.2f;
    [Export] public float PixelsPerUnit { get; set; } = 72f;
    [Export] public float Acceleration { get; set; } = 62f;
    [Export] public float Deceleration { get; set; } = 46f;
    [Export] public float TurnAcceleration { get; set; } = 82f;
    [Export] public float AttackBrakeDeceleration { get; set; } = 135f;
    [Export] public float StopSpeed { get; set; } = 0.02f;

    private readonly InputBuffer inputBuffer = new(0.20f);
    private readonly AttackTimelineStore attackTimelines = new();
    private Vec2 motorVelocity;
    private ActionState? currentAction;
    private float facingAngle;
    private bool attackWasDown;
    private string lastPhase = "";
    private int nextEventIndex;
    private int comboCount;

    public override void _Ready()
    {
        EnsureDebugBody();
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        inputBuffer.Tick(dt);

        var moveInput = ReadMoveInput();
        if (moveInput.LengthSquared() > 0.01f && CanMoveFreely)
        {
            facingAngle = Mathf.Atan2(moveInput.Y, moveInput.X);
        }

        Rotation = facingAngle;

        ReadAttackInput();
        TickAttack(dt);
        TryConsumeBufferedAttack();

        var canMove = CanMoveFreely;
        var input = canMove ? moveInput.ToVec2() : new Vec2();
        var settings = new KinematicSettings(
            Acceleration,
            canMove ? Deceleration : AttackBrakeDeceleration,
            TurnAcceleration,
            StopSpeed);

        motorVelocity = KinematicMotor.StepVelocity(
            motorVelocity,
            input,
            MaxSpeedUnits,
            dt,
            settings);

        Velocity = motorVelocity.ToGodot() * PixelsPerUnit;
        MoveAndSlide();

        motorVelocity = Velocity.ToVec2() / PixelsPerUnit;
        UpdateDebugBodyColor();
    }

    private bool CanMoveFreely => currentAction == null || currentAction.CanCancel;

    private void ReadAttackInput()
    {
        var attackDown = Input.IsPhysicalKeyPressed(Key.J) || Input.IsMouseButtonPressed(MouseButton.Left);
        if (attackDown && !attackWasDown)
        {
            inputBuffer.RegisterInput("light");
            GD.Print($"[InputBuffer] light1 buffered for {inputBuffer.TimeRemaining:0.000}s");
        }

        attackWasDown = attackDown;
    }

    private void TickAttack(float dt)
    {
        if (currentAction == null) return;

        var previousElapsed = currentAction.Elapsed;
        currentAction.Elapsed = MathF.Min(currentAction.Duration, currentAction.Elapsed + Math.Max(0, dt));
        LogPhaseChanges();
        LogFrameEvents(previousElapsed, currentAction.Elapsed);

        if (!currentAction.HitDone && previousElapsed < currentAction.ActiveAt && currentAction.Elapsed >= currentAction.ActiveAt)
        {
            currentAction.HitDone = true;
            motorVelocity += Vec2.FromAngle(currentAction.Angle) * currentAction.Lunge;
            GD.Print($"[AttackTimeline] Active hitbox armed: {currentAction.Kind} damage={currentAction.Damage:0.#} range={currentAction.Range:0.##}");
        }

        if (previousElapsed < currentAction.CancelAt && currentAction.CanCancel)
        {
            GD.Print($"[AttackTimeline] Cancel window open at {currentAction.Elapsed:0.000}s");
        }

        if (currentAction.Elapsed >= currentAction.Duration)
        {
            GD.Print($"[AttackTimeline] Finished: {currentAction.Kind} #{comboCount}");
            currentAction = null;
            lastPhase = "";
            nextEventIndex = 0;
        }
    }

    private void TryConsumeBufferedAttack()
    {
        if (!inputBuffer.TryConsume(currentAction == null || currentAction.CanCancel, out var action)) return;

        var chained = currentAction != null;
        StartLightAttack(chained);
    }

    private void StartLightAttack(bool chained)
    {
        comboCount = chained ? comboCount + 1 : 1;
        var fallback = LightFallbackProfile();
        var profile = attackTimelines.ResolvePlayerProfile("gon", "light", 1, fallback);

        currentAction = profile.CreateState(
            "light",
            facingAngle,
            damageScale: 1f,
            rangeBonus: 0f,
            defaultAnimation: "light1");

        lastPhase = "";
        nextEventIndex = 0;

        if (attackTimelines.LastError != null)
        {
            GD.Print($"[AttackTimeline] data fallback: {attackTimelines.LastError}");
        }

        GD.Print(chained
            ? $"[AttackTimeline] Combo chain success -> light1 #{comboCount} ({currentAction.TimelineSource})"
            : $"[AttackTimeline] Start light1 #{comboCount} ({currentAction.TimelineSource})");

        LogPhaseChanges();
    }

    private void LogPhaseChanges()
    {
        if (currentAction == null) return;
        var phase = currentAction.PhaseLabel;
        if (phase == lastPhase) return;

        lastPhase = phase;
        var label = phase switch
        {
            "startup" => "Startup entered",
            "active" => "Active entered",
            "recovery" => "Recovery entered",
            "cancel" => "Cancel recovery",
            _ => phase
        };
        GD.Print($"[AttackTimeline] {label}: {currentAction.Kind} t={currentAction.Elapsed:0.000}/{currentAction.Duration:0.000}");
    }

    private void LogFrameEvents(float from, float to)
    {
        if (currentAction == null) return;

        while (nextEventIndex < currentAction.Events.Count)
        {
            var frameEvent = currentAction.Events[nextEventIndex];
            if (frameEvent.Time > to) break;

            if (frameEvent.Time >= from)
            {
                GD.Print($"[AttackTimeline] Event {frameEvent.Kind}: {frameEvent.Name} t={frameEvent.Time:0.000} condition={frameEvent.Condition ?? "always"}");
            }

            nextEventIndex++;
        }
    }

    private static ActionProfile LightFallbackProfile() => new(
        0.205f,
        0.038f,
        0.058f,
        0.108f,
        0.097f,
        0.118f,
        40f,
        1.68f,
        1.50f,
        0.022f,
        0.32f,
        3.8f,
        0.14f,
        0.12f,
        1.35f);

    private static Vector2 ReadMoveInput()
    {
        var x = Axis(Key.A, Key.D);
        var y = Axis(Key.W, Key.S);
        var input = new Vector2(x, y);
        return input.LengthSquared() > 1f ? input.Normalized() : input;
    }

    private static float Axis(Key negative, Key positive)
    {
        var value = 0f;
        if (Input.IsPhysicalKeyPressed(negative)) value -= 1f;
        if (Input.IsPhysicalKeyPressed(positive)) value += 1f;
        return value;
    }

    private void EnsureDebugBody()
    {
        if (GetNodeOrNull<CollisionShape2D>("CollisionShape2D") == null)
        {
            var collision = new CollisionShape2D
            {
                Name = "CollisionShape2D",
                Shape = new CircleShape2D { Radius = 18f }
            };
            AddChild(collision);
        }

        if (GetNodeOrNull<Polygon2D>("DebugBody") == null)
        {
            var body = new Polygon2D
            {
                Name = "DebugBody",
                Color = new Color(0.12f, 0.92f, 0.78f, 1f),
                Polygon =
                [
                    new Vector2(0, -24),
                    new Vector2(16, 10),
                    new Vector2(0, 20),
                    new Vector2(-16, 10)
                ]
            };
            AddChild(body);
        }

        if (GetNodeOrNull<Camera2D>("Camera2D") == null)
        {
            var camera = new Camera2D
            {
                Name = "Camera2D",
                Enabled = true,
                Zoom = new Vector2(1.25f, 1.25f)
            };
            AddChild(camera);
        }
    }

    private void UpdateDebugBodyColor()
    {
        var body = GetNodeOrNull<Polygon2D>("DebugBody");
        if (body == null) return;

        body.Color = currentAction?.PhaseLabel switch
        {
            "startup" => new Color(1.00f, 0.82f, 0.18f, 1f),
            "active" => new Color(1.00f, 0.18f, 0.13f, 1f),
            "recovery" => new Color(0.36f, 0.62f, 1.00f, 1f),
            "cancel" => new Color(0.20f, 1.00f, 0.42f, 1f),
            _ => new Color(0.12f, 0.92f, 0.78f, 1f)
        };
    }
}
