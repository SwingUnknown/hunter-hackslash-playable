using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HunterHackslashNative;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new GameForm());
    }
}

internal sealed class GameForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly HashSet<Keys> keys = [];
    private readonly Random rng = new(7);
    private readonly List<Actor> enemies = [];
    private readonly List<Hazard> hazards = [];
    private readonly List<Effect> effects = [];
    private readonly List<DamageText> damageTexts = [];
    private readonly List<UpgradeOption> upgradeChoices = [];
    private readonly Dictionary<SpriteKey, Bitmap> spriteCache = [];
    private readonly Dictionary<string, Bitmap> actionSheets = [];
    private readonly Dictionary<string, Bitmap> illustrationCache = [];
    private readonly AttackTimelineStore attackTimelines = new();
    private readonly InputBuffer actionBuffer = new(0.20f);
    private readonly CharacterSpec[] characters;
    private static readonly KinematicSettings PlayerKinematics = new(Acceleration: 62f, Deceleration: 46f, TurnAcceleration: 82f);
    private readonly StageSpec[] stages;
    private const int SpriteDirections = 16;
    private const int SpriteFrames = 16;
    private const int SpriteCacheLimit = 1800;
    private const int SheetFrameWidth = 144;
    private const int SheetFrameHeight = 216;
    private const int SheetFrames = 18;
    private const int SheetDirections = 8;
    private const int SheetDirsPerRow = 4;
    private const float CameraZoom = 1.22f;
    private const float ArenaRadius = 10.8f;
    private static readonly string[] SheetStates = ["idle", "run", "dash", "light1", "light2", "light3", "light4", "heavy", "skill", "ultimate", "hit"];

    private GameMode mode = GameMode.Menu;
    private Actor player = null!;
    private int selectedCharacter;
    private int selectedStage;
    private int lightChain;
    private int nextUpgradeAt;
    private int upgradesTaken;
    private bool bossSpawned;
    private float aura;
    private float maxAura = 100;
    private float damageBonus;
    private float speedBonus;
    private float rangeBonus;
    private float auraRegenBonus;
    private float killHeal;
    private float echoStrike;
    private float chainBurst;
    private float dashCooldown;
    private float heavyCooldown;
    private float qCooldown;
    private float eCooldown;
    private float rCooldown;
    private float ultimateCooldown;
    private float lastTime;
    private float elapsed;
    private float stageTime;
    private float spawnTimer;
    private float hitStop;
    private float shake;
    private int kills;
    private int combo;
    private int fps = 60;
    private int fpsFrames;
    private float fpsTimer;
    private bool debugCombat;
    private string? combatDataWarning;

    public GameForm()
    {
        Text = "Hunter Hackslash Native";
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(960, 540);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 15, 18);
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        characters =
        [
            new("Gon", Color.FromArgb(88, 204, 110), Color.FromArgb(246, 226, 152), Color.FromArgb(22, 34, 28), 1.0f, 8.8f),
            new("Killua", Color.FromArgb(120, 212, 248), Color.FromArgb(225, 240, 247), Color.FromArgb(244, 249, 255), 0.92f, 9.9f),
            new("Kurapika", Color.FromArgb(242, 207, 107), Color.FromArgb(48, 76, 150), Color.FromArgb(234, 204, 88), 1.0f, 8.6f),
            new("Awakened Gon", Color.FromArgb(214, 255, 104), Color.FromArgb(28, 35, 24), Color.FromArgb(5, 7, 6), 1.35f, 8.2f)
        ];
        stages =
        [
            new("Greed Card Plains", StageTheme.Card, Color.FromArgb(95, 224, 205), 24, 13, 1.10f),
            new("Yorknew Auction Raid", StageTheme.Auction, Color.FromArgb(238, 202, 116), 30, 14, 1.20f),
            new("Chimera Forest Outbreak", StageTheme.Forest, Color.FromArgb(204, 240, 76), 38, 16, 1.34f)
        ];

        KeyDown += (_, e) =>
        {
            keys.Add(e.KeyCode);
            if (e.KeyCode == Keys.F3)
            {
                debugCombat = !debugCombat;
                e.SuppressKeyPress = true;
                Invalidate();
                return;
            }
            if (mode == GameMode.Menu)
            {
                if (e.KeyCode is Keys.D1 or Keys.NumPad1) selectedCharacter = 0;
                if (e.KeyCode is Keys.D2 or Keys.NumPad2) selectedCharacter = 1;
                if (e.KeyCode is Keys.D3 or Keys.NumPad3) selectedCharacter = 2;
                if (e.KeyCode is Keys.D4 or Keys.NumPad4) selectedCharacter = 3;
                if (e.KeyCode is Keys.Left or Keys.A) selectedCharacter = PositiveModulo(selectedCharacter - 1, characters.Length);
                if (e.KeyCode is Keys.Right or Keys.D) selectedCharacter = PositiveModulo(selectedCharacter + 1, characters.Length);
                if (e.KeyCode is Keys.Up or Keys.W) selectedStage = PositiveModulo(selectedStage - 1, stages.Length);
                if (e.KeyCode is Keys.Down or Keys.S) selectedStage = PositiveModulo(selectedStage + 1, stages.Length);
                if (e.KeyCode == Keys.Tab) selectedStage = (selectedStage + 1) % stages.Length;
                if (e.KeyCode == Keys.Enter) StartRun();
            }
            else if (mode == GameMode.Reward)
            {
                if (e.KeyCode is Keys.D1 or Keys.NumPad1) ApplyUpgrade(0);
                if (e.KeyCode is Keys.D2 or Keys.NumPad2) ApplyUpgrade(1);
                if (e.KeyCode is Keys.D3 or Keys.NumPad3) ApplyUpgrade(2);
                if (e.KeyCode == Keys.Enter) ApplyUpgrade(0);
            }
            else if (mode is GameMode.Clear or GameMode.GameOver)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (mode == GameMode.Clear) selectedStage = Math.Min(stages.Length - 1, selectedStage + 1);
                    mode = GameMode.Menu;
                }
            }
            else if (mode == GameMode.Playing)
            {
                if (e.KeyCode == Keys.J) StartAttack("light");
                if (e.KeyCode == Keys.K) StartAttack("heavy");
                if (e.KeyCode == Keys.Space) StartDash();
                if (e.KeyCode == Keys.Q) StartAttack("skill-q");
                if (e.KeyCode == Keys.E) StartAttack("skill-e");
                if (e.KeyCode == Keys.R) StartAttack("skill-r");
                if (e.KeyCode == Keys.F) StartAttack("ultimate");
                if (e.KeyCode == Keys.Escape) mode = GameMode.Menu;
            }
            e.SuppressKeyPress = e.KeyCode is Keys.Space or Keys.Tab;
        };
        KeyUp += (_, e) => keys.Remove(e.KeyCode);
        MouseDown += (_, e) => HandleMouseDown(e);
        timer.Tick += (_, _) => TickFrame();
        timer.Start();
    }

    private void HandleMouseDown(MouseEventArgs e)
    {
        if (mode == GameMode.Reward)
        {
            for (var i = 0; i < upgradeChoices.Count; i++)
            {
                if (UpgradeCardRect(i).Contains(e.Location))
                {
                    ApplyUpgrade(i);
                    return;
                }
            }
        }
        if (mode != GameMode.Menu) return;
        for (var i = 0; i < characters.Length; i++)
        {
            if (CharacterCardRect(i).Contains(e.Location))
            {
                selectedCharacter = i;
                if (e.Clicks > 1) StartRun();
                Invalidate();
                return;
            }
        }
        for (var i = 0; i < stages.Length; i++)
        {
            if (StageCardRect(i).Contains(e.Location))
            {
                selectedStage = i;
                if (e.Clicks > 1) StartRun();
                Invalidate();
                return;
            }
        }
        if (StartButtonRect().Contains(e.Location)) StartRun();
    }

    private void StartRun()
    {
        var spec = characters[selectedCharacter];
        player = new Actor(spec.Name, 0, 0, true)
        {
            MaxHp = selectedCharacter == 3 ? 1800 : selectedCharacter == 1 ? 900 : 1200,
            Hp = selectedCharacter == 3 ? 1800 : selectedCharacter == 1 ? 900 : 1200,
            Speed = spec.Speed,
            Radius = selectedCharacter == 3 ? 0.55f : 0.42f,
            Build = spec.Build,
            Palette = Palette.ForPlayer(spec)
        };
        enemies.Clear();
        hazards.Clear();
        effects.Clear();
        damageTexts.Clear();
        keys.Clear();
        stageTime = 0;
        spawnTimer = 0;
        kills = 0;
        combo = 0;
        lightChain = 0;
        nextUpgradeAt = 8;
        upgradesTaken = 0;
        bossSpawned = false;
        actionBuffer.Clear();
        maxAura = selectedCharacter == 3 ? 120 : selectedCharacter == 1 ? 110 : 100;
        aura = selectedCharacter == 3 ? 80 : 58;
        damageBonus = 0;
        speedBonus = 0;
        rangeBonus = 0;
        auraRegenBonus = 0;
        killHeal = 0;
        echoStrike = 0;
        chainBurst = 0;
        dashCooldown = heavyCooldown = qCooldown = eCooldown = rCooldown = ultimateCooldown = 0;
        upgradeChoices.Clear();
        hitStop = 0;
        shake = 0;
        mode = GameMode.Playing;
        SpawnWave(8);
        WarmSpriteCache();
    }

    private void TickFrame()
    {
        var now = (float)clock.Elapsed.TotalSeconds;
        var rawDt = Math.Clamp(now - lastTime, 0.001f, 0.033f);
        lastTime = now;
        elapsed += rawDt;
        UpdateFps(rawDt);
        if (mode == GameMode.Playing) UpdateGame(rawDt);
        Invalidate();
    }

    private void UpdateFps(float dt)
    {
        fpsFrames++;
        fpsTimer += dt;
        if (fpsTimer >= 0.4f)
        {
            fps = (int)MathF.Round(fpsFrames / fpsTimer);
            fpsFrames = 0;
            fpsTimer = 0;
        }
    }

    private void UpdateGame(float rawDt)
    {
        var stage = stages[selectedStage];
        stageTime += rawDt;
        var dt = rawDt;
        if (hitStop > 0)
        {
            hitStop = Math.Max(0, hitStop - rawDt);
            dt = 0;
        }
        aura = Math.Min(maxAura, aura + rawDt * (3.2f + auraRegenBonus));
        actionBuffer.Tick(rawDt);
        UpdateCooldowns(rawDt);

        UpdatePlayer(dt);
        UpdateEnemies(dt, stage);
        UpdateHazards(dt, stage);
        ResolveBodyPush();
        UpdateEffects(rawDt);

        spawnTimer -= dt;
        if (spawnTimer <= 0 && enemies.Count(e => e.Alive) < stage.MaxEnemies && kills < stage.KillGoal)
        {
            SpawnWave(Math.Clamp(4 + kills / 7, 4, 9));
            spawnTimer = Math.Max(1.4f, 4.2f - kills * 0.035f);
        }
        if (mode != GameMode.Playing) return;
        if (player.Hp <= 0)
        {
            mode = GameMode.GameOver;
            return;
        }

        if (kills >= nextUpgradeAt && upgradesTaken < 3 && !bossSpawned)
        {
            OpenUpgradeChoice();
            return;
        }

        if (kills >= stage.KillGoal && !bossSpawned)
        {
            SpawnBoss(stage);
            bossSpawned = true;
        }

        if (bossSpawned && enemies.All(e => !e.Alive))
        {
            mode = GameMode.Clear;
        }
    }

    private void UpdatePlayer(float dt)
    {
        player.HitFlash = Math.Max(0, player.HitFlash - dt);
        player.Stagger = Math.Max(0, player.Stagger - dt);
        var move = MoveInput();
        var actionMoveScale = PlayerActionMoveScale(player.Action);
        if (player.Stagger > 0) actionMoveScale *= 0.16f;
        if (move.Length > 0.01f)
        {
            if (player.Action == null) player.Facing = MathF.Atan2(move.Y, move.X);
        }
        var maxSpeed = player.Speed * (1 + speedBonus) * actionMoveScale;
        var kinematics = PlayerKinematics with
        {
            Acceleration = PlayerKinematics.Acceleration * (player.Action == null ? 1f : 0.62f),
            Deceleration = PlayerKinematics.Deceleration * (player.Action == null ? 1f : 1.32f),
            TurnAcceleration = PlayerKinematics.TurnAcceleration * (player.Action == null ? 1f : 0.72f)
        };
        player.Velocity = KinematicMotor.StepVelocity(player.Velocity, move, maxSpeed, dt, kinematics);

        if (player.Action is { } action)
        {
            action.Elapsed += dt;
            if (action.Kind == "dash")
            {
                var d = Vec2.FromAngle(action.Angle);
                player.Velocity = d * (18.6f * (1 + speedBonus * 0.75f));
                if (rng.NextDouble() < 0.35) AddAfterimage(player);
            }
            else if (!action.HitDone && action.Elapsed >= action.ActiveAt)
            {
                action.HitDone = true;
                player.Velocity += Vec2.FromAngle(action.Angle) * action.Lunge;
                ResolvePlayerAttack(action);
            }
            if (action.Elapsed >= action.Duration) player.Action = null;
        }

        player.Position += player.Velocity * dt;
        player.Position = ClampArena(player.Position);
        player.Motion += dt * (player.Velocity.Length * 1.25f + 2.2f);
        player.Invulnerable = Math.Max(0, player.Invulnerable - dt);
        TryConsumeBufferedAction();
    }

    private static float PlayerActionMoveScale(ActionState? action)
    {
        if (action == null) return 1f;
        if (action.Kind == "dash") return 0f;
        if (action.Elapsed < action.ActiveAt) return 0.28f;
        if (action.Elapsed <= action.ActiveEnd) return 0.10f;
        return action.CanCancel ? 0.72f : 0.44f;
    }

    private Vec2 MoveInput()
    {
        var x = 0f;
        var y = 0f;
        if (keys.Contains(Keys.W) || keys.Contains(Keys.Up)) y -= 1;
        if (keys.Contains(Keys.S) || keys.Contains(Keys.Down)) y += 1;
        if (keys.Contains(Keys.A) || keys.Contains(Keys.Left)) x -= 1;
        if (keys.Contains(Keys.D) || keys.Contains(Keys.Right)) x += 1;
        return new Vec2(x, y).Normalized();
    }

    private void StartDash()
    {
        if (player.Action != null && !player.Action.CanCancel)
        {
            BufferAction("dash");
            return;
        }
        if (dashCooldown > 0)
        {
            ShowNotReady("DASH", dashCooldown);
            return;
        }
        var move = MoveInput();
        if (move.Length > 0.1f) player.Facing = MathF.Atan2(move.Y, move.X);
        player.Action = new ActionState("dash", player.Facing, 0.24f, 0.02f, 0, 1.1f, 1.2f)
        {
            ActiveEnd = 0.18f,
            CancelAt = 0.20f
        };
        player.Invulnerable = 0.18f;
        dashCooldown = CooldownDuration("dash");
        effects.Add(Effect.Ring(player.Position, characters[selectedCharacter].Aura, 1.0f, 0.22f));
    }

    private void StartAttack(string kind)
    {
        if (player.Action != null && !player.Action.CanCancel)
        {
            BufferAction(kind);
            return;
        }
        var cooldown = CooldownRemaining(kind);
        if (cooldown > 0)
        {
            ShowNotReady(kind, cooldown);
            return;
        }
        var cost = ActionCost(kind);
        if (aura < cost)
        {
            damageTexts.Add(new DamageText(player.Position + new Vec2(0, -0.55f), "NO AURA", 0.48f));
            effects.Add(Effect.Ring(player.Position, Color.FromArgb(115, 145, 160), 0.8f, 0.18f));
            return;
        }
        aura -= cost;
        lightChain = kind == "light" ? lightChain % 4 + 1 : 0;
        var fallback = PlayerActionProfile(kind, lightChain);
        var profile = attackTimelines.ResolvePlayerProfile(player.Name, kind, lightChain, fallback);
        ReportAttackDataWarning();
        player.Action = profile.CreateState(kind, player.Facing, 1 + damageBonus, rangeBonus, ActionAnimation(kind, lightChain));
        SetCooldown(kind);
    }

    private void ReportAttackDataWarning()
    {
        if (attackTimelines.LastError == null || combatDataWarning == attackTimelines.LastError) return;
        combatDataWarning = attackTimelines.LastError;
        Debug.WriteLine(combatDataWarning);
        if (debugCombat)
        {
            damageTexts.Add(new DamageText(player.Position + new Vec2(0, -0.75f), "ATTACK DATA FALLBACK", 0.9f));
        }
    }

    private void BufferAction(string kind)
    {
        actionBuffer.Push(kind);
    }

    private void TryConsumeBufferedAction()
    {
        var windowOpen = player.Action == null || player.Action.CanCancel;
        if (!actionBuffer.TryConsume(windowOpen, out var kind)) return;
        if (kind == "dash") StartDash();
        else StartAttack(kind);
    }

    private static ActionProfile PlayerActionProfile(string kind, int lightStep)
    {
        return kind switch
        {
            "heavy" => new(0.39f, 0.12f, 0.145f, 0.230f, 0.160f, 0.280f, 82f, 2.28f, 2.32f, 0.040f, 0.78f, 7.2f, 0.24f, 0.14f, 2.8f),
            "skill-q" => new(0.42f, 0.095f, 0.125f, 0.225f, 0.195f, 0.290f, 98f, 2.82f, 1.62f, 0.038f, 0.66f, 5.4f, 0.22f, 0.14f, 2.3f),
            "skill-e" => new(0.34f, 0.050f, 0.075f, 0.165f, 0.175f, 0.210f, 72f, 2.58f, 1.22f, 0.034f, 0.62f, 6.0f, 0.20f, 0.13f, 5.2f),
            "skill-r" => new(0.52f, 0.155f, 0.190f, 0.345f, 0.175f, 0.410f, 92f, 2.62f, MathF.PI * 2, 0.038f, 0.78f, 4.8f, 0.25f, 0.14f, 1.7f),
            "ultimate" => new(0.72f, 0.250f, 0.300f, 0.500f, 0.220f, 0.610f, 210f, 3.62f, MathF.PI * 2, 0.060f, 1.25f, 8.4f, 0.36f, 0.16f, 3.5f),
            _ => lightStep switch
            {
                2 => new(0.215f, 0.040f, 0.060f, 0.112f, 0.103f, 0.122f, 42f, 1.74f, 1.52f, 0.024f, 0.36f, 4.1f, 0.15f, 0.12f, 1.45f),
                3 => new(0.230f, 0.048f, 0.066f, 0.132f, 0.098f, 0.138f, 46f, 1.86f, 1.66f, 0.026f, 0.40f, 4.6f, 0.16f, 0.13f, 1.65f),
                4 => new(0.270f, 0.070f, 0.092f, 0.172f, 0.098f, 0.186f, 58f, 2.05f, 1.86f, 0.032f, 0.52f, 5.5f, 0.20f, 0.14f, 2.15f),
                _ => new(0.205f, 0.038f, 0.058f, 0.108f, 0.097f, 0.118f, 40f, 1.68f, 1.50f, 0.022f, 0.32f, 3.8f, 0.14f, 0.12f, 1.35f)
            }
        };
    }

    private static float ActionCost(string kind) => kind switch
    {
        "heavy" => 6,
        "skill-q" => 18,
        "skill-e" => 16,
        "skill-r" => 28,
        "ultimate" => 72,
        _ => 0
    };

    private void UpdateCooldowns(float dt)
    {
        dashCooldown = Math.Max(0, dashCooldown - dt);
        heavyCooldown = Math.Max(0, heavyCooldown - dt);
        qCooldown = Math.Max(0, qCooldown - dt);
        eCooldown = Math.Max(0, eCooldown - dt);
        rCooldown = Math.Max(0, rCooldown - dt);
        ultimateCooldown = Math.Max(0, ultimateCooldown - dt);
    }

    private float CooldownRemaining(string kind) => kind switch
    {
        "dash" => dashCooldown,
        "heavy" => heavyCooldown,
        "skill-q" => qCooldown,
        "skill-e" => eCooldown,
        "skill-r" => rCooldown,
        "ultimate" => ultimateCooldown,
        _ => 0
    };

    private static float CooldownDuration(string kind) => kind switch
    {
        "dash" => 0.42f,
        "heavy" => 0.32f,
        "skill-q" => 1.65f,
        "skill-e" => 1.95f,
        "skill-r" => 3.8f,
        "ultimate" => 10.8f,
        _ => 0
    };

    private void SetCooldown(string kind)
    {
        var duration = CooldownDuration(kind);
        switch (kind)
        {
            case "heavy":
                heavyCooldown = duration;
                break;
            case "skill-q":
                qCooldown = duration;
                break;
            case "skill-e":
                eCooldown = duration;
                break;
            case "skill-r":
                rCooldown = duration;
                break;
            case "ultimate":
                ultimateCooldown = duration;
                break;
        }
    }

    private void ShowNotReady(string label, float seconds)
    {
        damageTexts.Add(new DamageText(player.Position + new Vec2(0, -0.55f), $"{label.ToUpperInvariant()} {seconds:0.0}s", 0.45f));
        effects.Add(Effect.Ring(player.Position, Color.FromArgb(120, 150, 165), 0.72f, 0.16f));
    }

    private void ResolvePlayerAttack(ActionState action)
    {
        var hits = 0;
        var dir = Vec2.FromAngle(action.Angle);
        var impactColor = ActionColor(action);
        foreach (var enemy in enemies.Where(e => e.Alive))
        {
            if (!HitResolver.CanHit(action, player.Position, enemy.Position, enemy.Radius)) continue;
            var damage = action.Damage * (selectedCharacter == 3 ? 1.35f : selectedCharacter == 1 ? 0.92f : 1f);
            ApplyEnemyHit(enemy, damage, dir, action, impactColor);
            hits++;
            damageTexts.Add(new DamageText(enemy.Position + new Vec2(0, -0.4f), ((int)damage).ToString(), 0.55f));
            effects.Add(Effect.Impact(enemy.Position, action.Angle, impactColor, ImpactRadius(action), 0.22f));
            effects.Add(Effect.Burst(enemy.Position, Color.White, action.Kind == "ultimate" ? 0.72f : action.Kind == "heavy" ? 0.50f : 0.38f, 0.2f));
            if (enemy.Hp <= 0)
            {
                DefeatEnemy(enemy);
            }
        }
        if (hits > 0)
        {
            aura = Math.Min(maxAura, aura + 4 + hits * 2.2f);
            SpawnUpgradeStrikes(action, dir, impactColor, hits);
            hitStop = Math.Max(hitStop, action.HitStop + Math.Min(3, hits - 1) * 0.006f);
            shake = Math.Max(shake, action.Shake + Math.Min(3, hits - 1) * 0.06f);
        }
        SpawnActionEffects(action, hits);
    }

    private void ApplyEnemyHit(Actor enemy, float damage, Vec2 dir, ActionState action, Color color)
    {
        enemy.Hp -= damage;
        enemy.HitFlash = Math.Max(enemy.HitFlash, action.HitFlash);
        enemy.HitAngle = action.Angle;
        enemy.Stagger = Math.Max(enemy.Stagger, action.Stagger);
        enemy.Velocity += dir * action.Knockback;
        if (action.Kind is "heavy" or "skill-r" or "ultimate")
        {
            effects.Add(Effect.Ring(enemy.Position, color, action.Kind == "ultimate" ? 1.25f : 0.78f, 0.18f));
        }
    }

    private static float ImpactRadius(ActionState action)
    {
        return action.Kind switch
        {
            "ultimate" => 1.45f,
            "heavy" => 1.02f,
            "skill-r" => 1.10f,
            "skill-q" or "skill-e" => 0.86f,
            _ => 0.64f
        };
    }

    private void DefeatEnemy(Actor enemy)
    {
        if (!enemy.Alive) return;
        enemy.Alive = false;
        kills++;
        combo++;
        aura = Math.Min(maxAura, aura + (enemy.IsBoss ? 35 : 8));
        if (killHeal > 0) player.Hp = Math.Min(player.MaxHp, player.Hp + killHeal * (enemy.IsBoss ? 4 : 1));
        effects.Add(Effect.Ring(enemy.Position, enemy.IsBoss ? Color.FromArgb(255, 240, 130) : Color.FromArgb(242, 207, 107), enemy.IsBoss ? 2.4f : 1.1f, enemy.IsBoss ? 0.55f : 0.35f));
        effects.Add(Effect.Burst(enemy.Position, Color.White, enemy.IsBoss ? 1.2f : 0.46f, 0.2f));
        if (enemy.IsBoss)
        {
            shake = Math.Max(shake, 1.1f);
            damageTexts.Add(new DamageText(enemy.Position + new Vec2(0, -0.8f), "BOSS DOWN", 1.0f));
        }
    }

    private void SpawnUpgradeStrikes(ActionState action, Vec2 dir, Color color, int hits)
    {
        var front = player.Position + dir * Math.Min(action.Range, action.Kind == "ultimate" ? 2.9f : 1.85f);
        if (chainBurst > 0)
        {
            var count = Math.Min(enemies.Count(e => e.Alive), 1 + (int)chainBurst);
            foreach (var target in enemies.Where(e => e.Alive).OrderBy(e => (e.Position - front).Length).Take(count))
            {
                if ((target.Position - front).Length > 3.2f + chainBurst * 0.35f) continue;
                ApplyBonusDamage(target, 16f * chainBurst * (1 + damageBonus), dir, Color.FromArgb(132, 224, 255), "arc");
                effects.Add(Effect.Lightning(player.Position, MathF.Atan2(target.Position.Y - player.Position.Y, target.Position.X - player.Position.X), Color.FromArgb(132, 224, 255), 1.8f, 0.16f, 2 + (int)chainBurst));
            }
        }

        if (echoStrike > 0 && action.Kind is "heavy" or "skill-r" or "ultimate")
        {
            var radius = action.Kind == "ultimate" ? 2.7f + echoStrike * 0.25f : 1.55f + echoStrike * 0.22f;
            foreach (var target in enemies.Where(e => e.Alive && (e.Position - front).Length <= radius))
            {
                ApplyBonusDamage(target, 18f * echoStrike * (1 + damageBonus), dir, color, "echo");
            }
            effects.Add(Effect.Shockwave(front, color, radius, 0.28f));
            effects.Add(Effect.GroundCrack(front, action.Angle, Color.FromArgb(25, 35, 28), radius * 0.75f, 0.32f));
        }
    }

    private void ApplyBonusDamage(Actor target, float damage, Vec2 dir, Color color, string label)
    {
        target.Hp -= damage;
        target.HitFlash = 0.14f;
        target.HitAngle = MathF.Atan2(dir.Y, dir.X);
        target.Stagger = Math.Max(target.Stagger, 0.12f);
        target.Velocity += dir * 1.8f;
        damageTexts.Add(new DamageText(target.Position + new Vec2(0, -0.55f), $"{label} {(int)damage}", 0.50f));
        effects.Add(Effect.Impact(target.Position, target.HitAngle, color, 0.55f, 0.18f));
        if (target.Hp <= 0) DefeatEnemy(target);
    }

    private Color ActionColor(ActionState action)
    {
        if (selectedCharacter == 1) return Color.FromArgb(132, 224, 255);
        if (selectedCharacter == 2) return Color.FromArgb(246, 216, 104);
        if (selectedCharacter == 3) return Color.FromArgb(218, 255, 104);
        return action.Kind == "heavy" || action.Kind == "ultimate" ? Color.FromArgb(250, 255, 176) : characters[selectedCharacter].Aura;
    }

    private void SpawnActionEffects(ActionState action, int hits)
    {
        var color = ActionColor(action);
        var dir = Vec2.FromAngle(action.Angle);
        var front = player.Position + dir * Math.Min(action.Range, action.Kind == "ultimate" ? 2.8f : 1.65f);
        effects.Add(Effect.Slash(player.Position, action.Angle, color, action.Range, action.Arc, action.Kind == "ultimate" ? 0.28f : 0.18f));

        if (action.Kind == "light")
        {
            if (hits > 0) effects.Add(Effect.Focus(front, action.Angle, color, 1.0f, 0.16f));
            return;
        }

        if (action.Kind == "heavy")
        {
            effects.Add(Effect.Impact(front, action.Angle, color, 1.0f, 0.18f));
            effects.Add(Effect.Burst(front, Color.White, 0.42f, 0.16f));
            effects.Add(Effect.Shockwave(front, color, 1.65f, 0.28f));
            effects.Add(Effect.GroundCrack(front, action.Angle, Color.FromArgb(38, 52, 38), 1.45f, 0.38f));
            return;
        }

        if (selectedCharacter == 1)
        {
            effects.Add(Effect.Focus(front, action.Angle, Color.White, action.Kind == "ultimate" ? 2.0f : 1.15f, 0.18f));
            effects.Add(Effect.Lightning(player.Position, action.Angle, color, action.Kind == "ultimate" ? 4.5f : 2.4f, action.Kind == "ultimate" ? 0.42f : 0.22f, action.Kind == "ultimate" ? 7 : 4));
            effects.Add(Effect.Impact(front, action.Angle, color, action.Kind == "ultimate" ? 1.55f : 0.92f, 0.20f));
            effects.Add(Effect.Shockwave(player.Position, color, action.Kind == "ultimate" ? 3.2f : 1.6f, 0.3f));
            if (action.Kind == "skill-e") AddAfterimage(player);
            return;
        }

        if (selectedCharacter == 2)
        {
            effects.Add(Effect.Chain(player.Position, action.Angle, color, action.Kind == "ultimate" ? 4.0f : 2.5f, action.Kind == "ultimate" ? 0.48f : 0.3f));
            effects.Add(Effect.Focus(front, action.Angle, color, action.Kind == "ultimate" ? 2.0f : 1.2f, 0.20f));
            effects.Add(Effect.Impact(front, action.Angle, color, action.Kind == "ultimate" ? 1.35f : 0.78f, 0.18f));
            if (action.Kind is "skill-r" or "ultimate") effects.Add(Effect.Shockwave(player.Position, color, action.Kind == "ultimate" ? 3.4f : 2.0f, 0.34f));
            return;
        }

        if (selectedCharacter == 3)
        {
            effects.Add(Effect.Focus(player.Position, action.Angle, color, action.Kind == "ultimate" ? 4.8f : 2.5f, 0.32f));
            effects.Add(Effect.Burst(front, Color.White, action.Kind == "ultimate" ? 0.90f : 0.48f, 0.20f));
            effects.Add(Effect.Impact(front, action.Angle, color, action.Kind == "ultimate" ? 1.85f : 1.05f, 0.22f));
            effects.Add(Effect.Shockwave(front, color, action.Kind == "ultimate" ? 4.4f : 2.4f, action.Kind == "ultimate" ? 0.58f : 0.36f));
            effects.Add(Effect.GroundCrack(front, action.Angle, Color.FromArgb(20, 28, 16), action.Kind == "ultimate" ? 3.4f : 1.85f, 0.5f));
            return;
        }

        effects.Add(Effect.Focus(front, action.Angle, color, 1.5f, 0.2f));
        effects.Add(Effect.Impact(front, action.Angle, color, action.Kind == "ultimate" ? 1.35f : 0.78f, 0.18f));
        if (action.Kind is "skill-r" or "ultimate") effects.Add(Effect.Shockwave(player.Position, color, action.Kind == "ultimate" ? 3.2f : 2.0f, 0.34f));
    }

    private void OpenUpgradeChoice()
    {
        upgradeChoices.Clear();
        var pool = UpgradePool();
        var offset = PositiveModulo(kills + selectedCharacter * 3 + selectedStage * 5 + upgradesTaken * 7, pool.Length);
        for (var i = 0; i < pool.Length && upgradeChoices.Count < 3; i++)
        {
            var candidate = pool[(offset + i * 3) % pool.Length];
            if (upgradeChoices.All(u => u.Id != candidate.Id)) upgradeChoices.Add(candidate);
        }
        while (upgradeChoices.Count < 3) upgradeChoices.Add(pool[upgradeChoices.Count]);
        mode = GameMode.Reward;
        damageTexts.Add(new DamageText(player.Position + new Vec2(0, -0.8f), "NEN BREAK", 0.9f));
        effects.Add(Effect.Focus(player.Position, player.Facing, characters[selectedCharacter].Aura, 2.0f, 0.45f));
    }

    private UpgradeOption[] UpgradePool()
    {
        return
        [
            new("output", "Ken Output", "+18% all attack damage", Color.FromArgb(250, 255, 176)),
            new("footwork", "Rhythm Step", "+11% movement and dash speed", Color.FromArgb(120, 212, 248)),
            new("reach", "Ryu Reach", "+0.22 attack range", Color.FromArgb(128, 230, 160)),
            new("aura", "Ten Flow", "+25 max aura and faster aura recovery", Color.FromArgb(150, 235, 255)),
            new("vital", "Hunter Stamina", "+190 max HP and immediate heal", Color.FromArgb(255, 126, 126)),
            new("drain", "Clean Finish", "Kills restore HP and extra aura", Color.FromArgb(255, 190, 116)),
            new("echo", "Impact Echo", "Heavy and big skills create a second shock hit", Color.FromArgb(242, 207, 107)),
            new("chain", selectedCharacter == 1 ? "Godspeed Arc" : selectedCharacter == 2 ? "Chain Spread" : "Nen Arc", "Hits jump to nearby enemies", Color.FromArgb(132, 224, 255)),
            new("burst", "Combo Furnace", "+10% damage and +12 max aura", Color.FromArgb(214, 255, 104))
        ];
    }

    private void ApplyUpgrade(int index)
    {
        if (mode != GameMode.Reward || index < 0 || index >= upgradeChoices.Count) return;
        var upgrade = upgradeChoices[index];
        switch (upgrade.Id)
        {
            case "output":
                damageBonus += 0.18f;
                break;
            case "footwork":
                speedBonus += 0.11f;
                break;
            case "reach":
                rangeBonus += 0.22f;
                break;
            case "aura":
                maxAura += 25;
                auraRegenBonus += 1.25f;
                aura = Math.Min(maxAura, aura + 35);
                break;
            case "vital":
                player.MaxHp += 190;
                player.Hp = Math.Min(player.MaxHp, player.Hp + 260);
                break;
            case "drain":
                killHeal += 12;
                maxAura += 8;
                aura = Math.Min(maxAura, aura + 28);
                break;
            case "echo":
                echoStrike += 1;
                break;
            case "chain":
                chainBurst += 1;
                break;
            case "burst":
                damageBonus += 0.10f;
                maxAura += 12;
                aura = Math.Min(maxAura, aura + 22);
                break;
        }

        upgradesTaken++;
        nextUpgradeAt += 7 + upgradesTaken * 2;
        mode = GameMode.Playing;
        damageTexts.Add(new DamageText(player.Position + new Vec2(0, -0.7f), upgrade.Name, 0.9f));
        effects.Add(Effect.Ring(player.Position, upgrade.Color, 1.7f, 0.42f));
        effects.Add(Effect.Burst(player.Position, Color.White, 0.72f, 0.18f));
    }

    private void UpdateEnemies(float dt, StageSpec stage)
    {
        foreach (var enemy in enemies.Where(e => e.Alive))
        {
            enemy.HitFlash = Math.Max(0, enemy.HitFlash - dt);
            enemy.Stagger = Math.Max(0, enemy.Stagger - dt);
            enemy.AttackCooldown = Math.Max(0, enemy.AttackCooldown - dt);
            enemy.SpecialCooldown = Math.Max(0, enemy.SpecialCooldown - dt);
            if (enemy.Stagger <= 0)
            {
                var toPlayer = player.Position - enemy.Position;
                var dist = Math.Max(0.001f, toPlayer.Length);
                var dir = toPlayer / dist;
                enemy.Facing = MathF.Atan2(dir.Y, dir.X);
                TryEnemySpecial(enemy, stage, dist, dir);
                var engage = enemy.IsBoss ? 1.55f : 0.76f;
                if (dist > engage + enemy.Radius + player.Radius)
                {
                    var pressure = enemy.IsBoss ? 0.24f : 0.32f;
                    var tempo = enemy.IsBoss ? 0.98f : kills < 8 ? 1.18f : 1.06f;
                    enemy.Velocity = enemy.Velocity * (1 - pressure) + dir * (enemy.Speed * stage.Difficulty * tempo) * pressure;
                }
                else
                {
                    enemy.Velocity *= 0.55f;
                    if (enemy.AttackCooldown <= 0)
                    {
                        enemy.AttackCooldown = enemy.IsBoss ? 0.86f + (float)rng.NextDouble() * 0.38f : 1.25f + (float)rng.NextDouble() * 0.65f;
                        var profile = EnemyMeleeProfile(enemy, stage);
                        enemy.Action = profile.CreateState("enemy-hit", enemy.Facing, 1f, 0f, enemy.IsBoss ? "ultimate" : enemy.Build > 1.08f ? "heavy" : "light1");
                    }
                }
            }

            if (enemy.Action is { } action)
            {
                action.Elapsed += dt;
                if (!action.HitDone && action.Elapsed >= action.ActiveAt)
                {
                    action.HitDone = true;
                    enemy.Velocity += Vec2.FromAngle(action.Angle) * action.Lunge;
                    if (HitResolver.CanHit(action, enemy.Position, player.Position, player.Radius) && player.Invulnerable <= 0)
                    {
                        ApplyPlayerHit(action.Damage, enemy.Position, action.Angle, action.Knockback, action.Stagger, action.HitFlash, enemy.IsBoss ? 0.34f : 0.22f, action.Shake, stage.EnemyAura);
                    }
                    effects.Add(Effect.Slash(enemy.Position, action.Angle, stage.EnemyAura, 1.1f, 1.45f, 0.13f));
                    if (enemy.IsBoss)
                    {
                        var slam = enemy.Position + Vec2.FromAngle(action.Angle) * 1.1f;
                        effects.Add(Effect.Shockwave(slam, stage.EnemyAura, 1.8f, 0.34f));
                        effects.Add(Effect.GroundCrack(slam, action.Angle, Color.FromArgb(40, 34, 42), 1.3f, 0.42f));
                        shake = Math.Max(shake, 0.55f);
                    }
                }
                if (action.Elapsed >= action.Duration) enemy.Action = null;
            }
            enemy.Position += enemy.Velocity * dt;
            enemy.Velocity *= 0.94f;
            enemy.Position = ClampArena(enemy.Position);
            enemy.Motion += dt * (enemy.Velocity.Length * 1.35f + 1.5f);
        }

        enemies.RemoveAll(e => !e.Alive && e.Hp < -30);
    }

    private static ActionProfile EnemyMeleeProfile(Actor enemy, StageSpec stage)
    {
        if (enemy.IsBoss)
        {
            return new(0.54f, 0.20f, 0.24f, 0.37f, 0.17f, 0.46f, 44f * stage.Difficulty, 1.96f, 2.7f, 0.032f, 0.66f, 5.4f, 0.26f, 0.15f, 1.4f);
        }
        if (enemy.Build > 1.08f)
        {
            return new(0.38f, 0.13f, 0.16f, 0.25f, 0.13f, 0.32f, 24f * stage.Difficulty, 1.10f, 1.85f, 0.020f, 0.40f, 4.0f, 0.17f, 0.13f, 0.75f);
        }
        return new(0.29f, 0.09f, 0.12f, 0.18f, 0.11f, 0.24f, 19f * stage.Difficulty, 0.94f, 1.62f, 0.016f, 0.32f, 3.1f, 0.14f, 0.12f, 0.55f);
    }

    private void ApplyPlayerHit(float damage, Vec2 source, float angle, float knockback, float stagger, float flash, float invulnerable, float shakeAmount, Color color)
    {
        player.Hp -= damage;
        player.HitFlash = Math.Max(player.HitFlash, flash);
        player.HitAngle = MathF.Atan2(player.Position.Y - source.Y, player.Position.X - source.X);
        player.Stagger = Math.Max(player.Stagger, stagger);
        player.Invulnerable = Math.Max(player.Invulnerable, invulnerable);
        player.Velocity += Vec2.FromAngle(player.HitAngle) * knockback;
        if (player.Action is { Kind: not "dash" and not "ultimate" }) player.Action = null;
        shake = Math.Max(shake, shakeAmount);
        damageTexts.Add(new DamageText(player.Position + new Vec2(0, -0.55f), $"{(int)damage}", 0.55f));
        effects.Add(Effect.Ring(player.Position, Color.FromArgb(255, 104, 104), 1.16f, 0.22f));
        effects.Add(Effect.Impact(player.Position, angle, color, 0.72f, 0.20f));
    }

    private void TryEnemySpecial(Actor enemy, StageSpec stage, float dist, Vec2 dir)
    {
        if (enemy.SpecialCooldown > 0 || enemy.Action != null || dist > (enemy.IsBoss ? 9.5f : 7.2f)) return;
        var side = new Vec2(-dir.Y, dir.X);
        var color = stage.EnemyAura;

        if (enemy.IsBoss)
        {
            enemy.SpecialCooldown = stage.Theme switch
            {
                StageTheme.Card => 2.3f,
                StageTheme.Auction => 1.85f,
                _ => 2.15f
            };
            if (stage.Theme == StageTheme.Card)
            {
                for (var i = -1; i <= 1; i++)
                {
                    var target = player.Position + side * (i * 1.25f) + dir * (0.4f + Math.Abs(i) * 0.35f);
                    SpawnMine(target, 0.76f, 42f * stage.Difficulty, 0.72f, color, enemy.IsBoss);
                }
                effects.Add(Effect.Focus(player.Position, enemy.Facing, color, 1.45f, 0.24f));
            }
            else if (stage.Theme == StageTheme.Auction)
            {
                for (var i = -1; i <= 1; i++)
                {
                    var angle = enemy.Facing + i * 0.22f;
                    var v = Vec2.FromAngle(angle);
                    SpawnProjectile(enemy.Position + v * 0.72f, v * 6.4f, 0.22f, 34f * stage.Difficulty, 2.2f, color, true);
                }
                effects.Add(Effect.Slash(enemy.Position, enemy.Facing, color, 2.1f, 1.8f, 0.20f));
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    var target = player.Position + Vec2.FromAngle(enemy.Facing + i * MathF.PI * 0.5f) * (0.9f + (i % 2) * 0.55f);
                    SpawnMine(target, 0.64f, 32f * stage.Difficulty, 0.58f + i * 0.08f, color, true);
                }
                effects.Add(Effect.Shockwave(enemy.Position, color, 2.2f, 0.32f));
            }
            return;
        }

        if (enemy.Name.Contains("Gunner", StringComparison.OrdinalIgnoreCase))
        {
            enemy.SpecialCooldown = 2.2f + (float)rng.NextDouble() * 0.8f;
            SpawnProjectile(enemy.Position + dir * 0.55f, dir * 5.7f, 0.16f, 18f * stage.Difficulty, 1.8f, color, false);
            effects.Add(Effect.Focus(enemy.Position, enemy.Facing, color, 0.9f, 0.15f));
        }
        else if (enemy.Name.Contains("Bomb", StringComparison.OrdinalIgnoreCase) || enemy.Name.Contains("Card", StringComparison.OrdinalIgnoreCase))
        {
            enemy.SpecialCooldown = 2.6f + (float)rng.NextDouble() * 1.0f;
            var target = player.Position + side * (((float)rng.NextDouble() - 0.5f) * 1.4f) + player.Velocity * 0.12f;
            SpawnMine(target, enemy.Name.Contains("Bomb", StringComparison.OrdinalIgnoreCase) ? 0.58f : 0.46f, (enemy.Name.Contains("Bomb", StringComparison.OrdinalIgnoreCase) ? 25f : 17f) * stage.Difficulty, 0.72f, color, false);
        }
        else if (enemy.Name.Contains("Commander", StringComparison.OrdinalIgnoreCase))
        {
            enemy.SpecialCooldown = 2.8f + (float)rng.NextDouble() * 0.75f;
            SpawnProjectile(enemy.Position + dir * 0.55f, dir * 4.4f, 0.20f, 22f * stage.Difficulty, 2.0f, color, false);
            SpawnMine(player.Position + side * (((float)rng.NextDouble() - 0.5f) * 1.0f), 0.46f, 18f * stage.Difficulty, 0.82f, color, false);
        }
    }

    private void SpawnProjectile(Vec2 position, Vec2 velocity, float radius, float damage, float life, Color color, bool boss)
    {
        hazards.Add(new Hazard(HazardKind.Projectile, position, velocity, radius, damage, life, color)
        {
            Delay = 0.10f,
            Facing = MathF.Atan2(velocity.Y, velocity.X),
            Boss = boss
        });
    }

    private void SpawnMine(Vec2 position, float radius, float damage, float delay, Color color, bool boss)
    {
        hazards.Add(new Hazard(HazardKind.Mine, ClampArena(position), new Vec2(), radius, damage, 1.35f + delay, color)
        {
            Delay = delay,
            Boss = boss
        });
    }

    private void UpdateHazards(float dt, StageSpec stage)
    {
        foreach (var h in hazards)
        {
            h.Age += dt;
            if (h.Age >= h.Delay) h.Position += h.Velocity * dt;
            if (h.Kind == HazardKind.Projectile && h.Position.Length > ArenaRadius + 1.3f) h.Age = h.Life;
            var armed = h.Age >= h.Delay;
            if (!armed || h.Hit || player.Invulnerable > 0) continue;
            var dist = (player.Position - h.Position).Length;
            var radius = h.Radius + player.Radius + (h.Kind == HazardKind.Mine ? 0.08f : 0);
            if (dist > radius) continue;
            h.Hit = true;
            h.Age = h.Life;
            var hitAngle = MathF.Atan2(player.Position.Y - h.Position.Y, player.Position.X - h.Position.X);
            ApplyPlayerHit(h.Damage, h.Position, hitAngle, h.Boss ? 5.2f : 3.4f, h.Boss ? 0.28f : 0.18f, 0.16f, 0.26f, h.Boss ? 0.72f : 0.42f, Color.FromArgb(255, 104, 104));
            if (h.Kind == HazardKind.Mine) effects.Add(Effect.Shockwave(h.Position, h.Color, h.Radius * 1.8f, 0.28f));
        }
        hazards.RemoveAll(h => h.Age >= h.Life);
        if (hazards.Count > 44) hazards.RemoveRange(0, hazards.Count - 44);
    }

    private void ResolveBodyPush()
    {
        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (!enemy.Alive) continue;
            Separate(player, enemy, 0.42f);
            for (var j = i + 1; j < enemies.Count; j++)
            {
                if (enemies[j].Alive) Separate(enemy, enemies[j], 0.24f);
            }
        }
    }

    private static void Separate(Actor a, Actor b, float strength)
    {
        var delta = b.Position - a.Position;
        var dist = Math.Max(0.001f, delta.Length);
        var min = a.Radius + b.Radius;
        if (dist >= min) return;
        var push = delta / dist * ((min - dist) * strength);
        if (a.IsPlayer) b.Position += push * 1.35f;
        else if (b.IsPlayer) a.Position -= push * 1.35f;
        else
        {
            a.Position -= push * 0.5f;
            b.Position += push * 0.5f;
        }
    }

    private void UpdateEffects(float dt)
    {
        foreach (var e in effects) e.Age += dt;
        foreach (var d in damageTexts) d.Age += dt;
        effects.RemoveAll(e => e.Age >= e.Life);
        damageTexts.RemoveAll(d => d.Age >= d.Life);
        if (effects.Count > 90) effects.RemoveRange(0, effects.Count - 90);
        if (damageTexts.Count > 28) damageTexts.RemoveRange(0, damageTexts.Count - 28);
        shake = Math.Max(0, shake - dt * 7.4f);
    }

    private void SpawnWave(int count)
    {
        var stage = stages[selectedStage];
        for (var i = 0; i < count && enemies.Count(e => e.Alive) < stage.MaxEnemies; i++)
        {
            var angle = (float)(rng.NextDouble() * Math.PI * 2);
            var radius = 4.8f + (float)rng.NextDouble() * 3.9f;
            var pos = ClampArena(player.Position + Vec2.FromAngle(angle) * radius);
            var big = rng.NextDouble() < 0.16 + selectedStage * 0.03;
            var enemyName = EnemyNameForStage(stage.Theme, big);
            var hp = (big ? 112f : 56f) * stage.Difficulty;
            enemies.Add(new Actor(enemyName, pos.X, pos.Y, false)
            {
                MaxHp = hp,
                Hp = hp,
                Speed = big ? 4.35f : 5.25f,
                Radius = big ? 0.48f : 0.36f,
                Build = big ? 1.18f : 0.96f,
                Palette = Palette.ForEnemy(stage.EnemyAura, big)
            });
        }
    }

    private void SpawnBoss(StageSpec stage)
    {
        var angle = (float)(rng.NextDouble() * Math.PI * 2);
        var pos = ClampArena(player.Position + Vec2.FromAngle(angle) * 6.7f);
        var bossName = BossNameForStage(stage.Theme);
        var hp = 460 + selectedStage * 180 + upgradesTaken * 70;
        enemies.Add(new Actor(bossName, pos.X, pos.Y, false)
        {
            MaxHp = hp,
            Hp = hp,
            Speed = 3.25f + selectedStage * 0.22f,
            Radius = 0.78f,
            Build = 1.62f,
            IsBoss = true,
            Palette = Palette.ForEnemy(stage.EnemyAura, true),
            AttackCooldown = 0.8f
        });
        damageTexts.Add(new DamageText(pos + new Vec2(0, -1.0f), "BOSS ARRIVED", 1.1f));
        effects.Add(Effect.Shockwave(pos, stage.EnemyAura, 3.1f, 0.62f));
        effects.Add(Effect.Ring(pos, Color.FromArgb(255, 240, 130), 2.2f, 0.48f));
        shake = Math.Max(shake, 0.85f);
    }

    private static string EnemyNameForStage(StageTheme theme, bool elite)
    {
        return theme switch
        {
            StageTheme.Card => elite ? "Bomb Trapper" : "Card Thief",
            StageTheme.Auction => elite ? "Shadow Beast Tank" : "Mafia Gunner",
            StageTheme.Forest => elite ? "Ant Commander" : "Ant Soldier",
            _ => elite ? "Elite" : "Soldier"
        };
    }

    private static string BossNameForStage(StageTheme theme)
    {
        return theme switch
        {
            StageTheme.Card => "Bomber Leader Boss",
            StageTheme.Auction => "Butler Captain Boss",
            StageTheme.Forest => "Ant King Boss",
            _ => "Arena Boss"
        };
    }

    private static Vec2 ClampArena(Vec2 value)
    {
        var d = value.Length;
        return d > ArenaRadius ? value / d * ArenaRadius : value;
    }

    private void AddAfterimage(Actor source)
    {
        effects.Add(new Effect(EffectKind.Afterimage, source.Position, characters[selectedCharacter].Aura, 0.18f)
        {
            Facing = source.Facing,
            Build = source.Build,
            Palette = source.Palette,
            Motion = source.Motion
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.FromArgb(9, 14, 17));

        if (mode == GameMode.Menu)
        {
            DrawMenu(g);
            return;
        }

        DrawGame(g);
        if (mode == GameMode.Reward) DrawUpgradeOverlay(g);
        if (mode == GameMode.Clear) DrawOverlay(g, "STAGE CLEAR", "Enter: next map / menu");
        if (mode == GameMode.GameOver) DrawOverlay(g, "DOWN", "Enter: return to menu");
    }

    private void DrawMenu(Graphics g)
    {
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(10, 17, 20), Color.FromArgb(30, 40, 38), 90f);
        g.FillRectangle(bg, ClientRectangle);
        DrawMenuBackdrop(g);

        using var title = new Font("Segoe UI", 31, FontStyle.Bold);
        using var h2 = new Font("Segoe UI", 14, FontStyle.Bold);
        using var text = new Font("Segoe UI", 10, FontStyle.Regular);
        using var small = new Font("Segoe UI", 8, FontStyle.Bold);
        using var dim = new SolidBrush(Color.FromArgb(184, 224, 230, 220));
        using var faint = new SolidBrush(Color.FromArgb(128, 224, 230, 220));

        g.DrawString("Hunter Hackslash Native", title, Brushes.WhiteSmoke, 44, 34);
        g.DrawString("Click a hunter or use A/D. Click a map or use W/S. Enter starts.", text, dim, 50, 84);

        var start = StartButtonRect();
        FillRound(g, start, Color.FromArgb(52, 76, 56), 10);
        StrokeRound(g, start, characters[selectedCharacter].Aura, 10, 2.5f);
        g.DrawString("START RUN", h2, Brushes.WhiteSmoke, start.X + 28, start.Y + 18);

        g.DrawString("Hunters", h2, Brushes.WhiteSmoke, 48, 124);
        for (var i = 0; i < characters.Length; i++)
        {
            var rect = CharacterCardRect(i);
            var selected = i == selectedCharacter;
            var spec = characters[i];
            var cardColor = selected ? Color.FromArgb(42, 56, 48) : Color.FromArgb(24, 31, 34);
            FillRound(g, rect, cardColor, 10);
            TryDrawMenuIllustration(g, spec, rect, selected);
            StrokeRound(g, rect, selected ? spec.Aura : Color.FromArgb(52, 72, 70), 10, selected ? 3 : 1.4f);

            using var aura = new SolidBrush(Color.FromArgb(selected ? 90 : 46, spec.Aura));
            g.FillEllipse(aura, rect.X + 16, rect.Y + 16, 36, 36);
            g.DrawString($"{i + 1}", h2, Brushes.WhiteSmoke, rect.X + 27, rect.Y + 22);
            g.DrawString(spec.Name, h2, Brushes.WhiteSmoke, rect.X + 60, rect.Y + 16);
            g.DrawString(CharacterRoleLabel(i), small, faint, rect.X + 62, rect.Y + 42);

            DrawMenuPreviewActor(g, spec, rect, selected, i);

            DrawStatPill(g, rect.X + 14, rect.Bottom - 62, $"Speed {spec.Speed:0.0}", selected ? spec.Aura : Color.FromArgb(90, 100, 104));
            DrawStatPill(g, rect.X + 14, rect.Bottom - 34, i == 3 ? "Awakened" : i == 1 ? "Lightning" : i == 2 ? "Chains" : "Impact", selected ? spec.Aura : Color.FromArgb(90, 100, 104));
        }

        g.DrawString("Maps", h2, Brushes.WhiteSmoke, 48, ClientSize.Height - 192);
        for (var i = 0; i < stages.Length; i++)
        {
            var rect = StageCardRect(i);
            var selected = i == selectedStage;
            var stage = stages[i];
            FillRound(g, rect, selected ? Color.FromArgb(42, 48, 58) : Color.FromArgb(23, 29, 34), 10);
            StrokeRound(g, rect, selected ? stage.EnemyAura : Color.FromArgb(50, 64, 68), 10, selected ? 2.5f : 1.2f);
            using var aura = new SolidBrush(Color.FromArgb(selected ? 82 : 42, stage.EnemyAura));
            g.FillRectangle(aura, rect.X, rect.Y, 8, rect.Height);
            g.DrawString(stage.Name, h2, Brushes.WhiteSmoke, rect.X + 22, rect.Y + 14);
            g.DrawString($"{stage.KillGoal} kills  /  {stage.MaxEnemies} max enemies  /  x{stage.Difficulty:0.00}", small, dim, rect.X + 24, rect.Y + 48);
            g.DrawString(StageThemeLabel(stage.Theme), small, faint, rect.X + 24, rect.Y + 68);
        }

        g.DrawString("In battle: WASD move, J attack, K heavy, Space dash, Q/E/R/F skills", text, dim, 48, ClientSize.Height - 42);
    }

    private void DrawMenuBackdrop(Graphics g)
    {
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        using var floor = new SolidBrush(Color.FromArgb(40, 54, 50));
        var y = h * 0.58f;
        g.FillPolygon(floor, [
            new PointF(0, y),
            new PointF(w, y - 34),
            new PointF(w, h),
            new PointF(0, h)
        ]);
        using var line = new Pen(Color.FromArgb(34, 230, 235, 220), 1);
        for (var i = 0; i < 14; i++)
        {
            var x = i * w / 13f;
            g.DrawLine(line, w * 0.5f, y - 60, x, h);
        }
        for (var i = 0; i < 7; i++)
        {
            var yy = y + i * 42;
            g.DrawBezier(line, -40, yy, w * 0.26f, yy + 18, w * 0.72f, yy - 18, w + 40, yy + 6);
        }
    }

    private RectangleF CharacterCardRect(int index)
    {
        var margin = 44f;
        var gap = 18f;
        var top = 154f;
        var width = (ClientSize.Width - margin * 2 - gap * 3) / 4f;
        var height = Math.Min(340f, ClientSize.Height - 370f);
        return new RectangleF(margin + index * (width + gap), top, width, Math.Max(278f, height));
    }

    private RectangleF StageCardRect(int index)
    {
        var margin = 44f;
        var gap = 16f;
        var width = (ClientSize.Width - margin * 2 - gap * 2) / 3f;
        return new RectangleF(margin + index * (width + gap), ClientSize.Height - 166, width, 92);
    }

    private RectangleF UpgradeCardRect(int index)
    {
        var width = Math.Min(330f, (ClientSize.Width - 160f) / 3f);
        var gap = 22f;
        var total = width * 3 + gap * 2;
        var x = (ClientSize.Width - total) * 0.5f + index * (width + gap);
        return new RectangleF(x, ClientSize.Height * 0.43f, width, 168f);
    }

    private RectangleF StartButtonRect() => new(ClientSize.Width - 300, 54, 252, 58);

    private void DrawMenuPreviewActor(Graphics g, CharacterSpec spec, RectangleF rect, bool selected, int index)
    {
        var actor = new Actor(spec.Name, 0, 0, true)
        {
            Palette = Palette.ForPlayer(spec),
            Build = spec.Build,
            Facing = -MathF.PI / 2 + MathF.Sin(elapsed * 0.8f + index) * 0.28f,
            Motion = elapsed * 0.75f + index * 0.6f,
            Velocity = selected ? new Vec2(0.6f, 0) : new Vec2()
        };
        if (selected)
        {
            var kind = index switch { 1 => "skill-e", 2 => "skill-q", 3 => "ultimate", _ => "heavy" };
            actor.Action = new ActionState(kind, actor.Facing, 1, 0.35f, 0, 1, 1)
            {
                Animation = ActionAnimation(kind, 1),
                Elapsed = elapsed * 0.72f % 1f
            };
        }
        var previewHeight = spec.Build > 1.2f ? rect.Height * 0.56f : rect.Height * 0.49f;
        var previewWidth = previewHeight * 0.52f;
        var old = g.Transform;
        g.TranslateTransform(rect.X + rect.Width * 0.5f, rect.Bottom - 86);
        var state = SpriteState(actor);
        var frame = SheetFrame(actor, state);
        if (!TryDrawActionSheet(g, actor, new PointF(0, 0), previewHeight, state, frame))
        {
            DrawRig(g, actor, previewWidth, previewHeight);
        }
        g.Transform = old;
    }

    private void TryDrawMenuIllustration(Graphics g, CharacterSpec spec, RectangleF rect, bool selected)
    {
        var path = CharacterIllustrationPath(spec.Name);
        if (path == null) return;
        var image = LoadIllustration(path);
        if (image == null) return;

        var oldClip = g.Clip;
        using var clipPath = RoundedRectPath(rect, 10);
        g.SetClip(clipPath);
        var src = new RectangleF(0, image.Height * 0.16f, image.Width, image.Height * 0.66f);
        var dest = new RectangleF(rect.X + rect.Width * 0.06f, rect.Y + rect.Height * 0.20f, rect.Width * 0.88f, rect.Height * 0.64f);
        using var attrs = new System.Drawing.Imaging.ImageAttributes();
        var alpha = selected ? 0.46f : 0.24f;
        var matrix = new System.Drawing.Imaging.ColorMatrix
        {
            Matrix00 = 1f,
            Matrix11 = 1f,
            Matrix22 = 1f,
            Matrix33 = alpha,
            Matrix44 = 1f
        };
        attrs.SetColorMatrix(matrix);
        g.DrawImage(image, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        using var shade = new SolidBrush(Color.FromArgb(selected ? 72 : 122, 8, 12, 14));
        g.FillRectangle(shade, rect);
        g.Clip = oldClip;
    }

    private static GraphicsPath RoundedRectPath(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string CharacterRoleLabel(int index) => index switch
    {
        1 => "fast step / electric skill",
        2 => "mid range / chain control",
        3 => "expensive power form",
        _ => "close combat / impact"
    };

    private static string StageThemeLabel(StageTheme theme) => theme switch
    {
        StageTheme.Auction => "urban lanes and ranged pressure",
        StageTheme.Forest => "wide horde arena",
        _ => "card field and trap flow"
    };

    private static void DrawStatPill(Graphics g, float x, float y, string label, Color color)
    {
        using var font = new Font("Segoe UI", 8, FontStyle.Bold);
        using var bg = new SolidBrush(Color.FromArgb(74, color));
        using var fg = new SolidBrush(Color.FromArgb(230, 245, 240, 223));
        var rect = new RectangleF(x, y, 112, 22);
        FillRound(g, rect, Color.FromArgb(54, color), 6);
        g.DrawString(label, font, fg, x + 9, y + 4);
    }

    private void DrawGame(Graphics g)
    {
        var ox = shake > 0 ? ((float)rng.NextDouble() - 0.5f) * shake * 14f : 0;
        var oy = shake > 0 ? ((float)rng.NextDouble() - 0.5f) * shake * 10f : 0;
        g.TranslateTransform(ox, oy);
        DrawMap(g, stages[selectedStage]);
        foreach (var effect in effects.Where(e => e.Kind is EffectKind.Ring or EffectKind.Burst or EffectKind.Shockwave or EffectKind.GroundCrack)) DrawEffect(g, effect);
        foreach (var hazard in hazards) DrawHazard(g, hazard);
        var actors = enemies.Where(e => e.Alive).Append(player).OrderBy(a => a.Position.Y).ToArray();
        foreach (var actor in actors) DrawActor(g, actor);
        foreach (var effect in effects.Where(e => e.Kind is EffectKind.Slash or EffectKind.Afterimage or EffectKind.Impact or EffectKind.Lightning or EffectKind.Chain or EffectKind.Focus)) DrawEffect(g, effect);
        foreach (var text in damageTexts) DrawDamageText(g, text);
        if (debugCombat) DrawCombatDebug(g);
        g.ResetTransform();
        DrawHud(g);
    }

    private void DrawMap(Graphics g, StageSpec stage)
    {
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(48, 69, 62), Color.FromArgb(25, 36, 42), 90f);
        if (stage.Theme == StageTheme.Card) bg.LinearColors = [Color.FromArgb(29, 74, 72), Color.FromArgb(15, 35, 42)];
        if (stage.Theme == StageTheme.Auction) bg.LinearColors = [Color.FromArgb(56, 42, 54), Color.FromArgb(18, 19, 30)];
        if (stage.Theme == StageTheme.Forest) bg.LinearColors = [Color.FromArgb(43, 82, 49), Color.FromArgb(15, 32, 24)];
        g.FillRectangle(bg, ClientRectangle);

        var poly = ArenaPolygon();
        using var floor = new SolidBrush(stage.Theme switch
        {
            StageTheme.Auction => Color.FromArgb(82, 55, 66),
            StageTheme.Forest => Color.FromArgb(48, 86, 51),
            _ => Color.FromArgb(42, 92, 84)
        });
        g.FillPolygon(floor, poly);
        using var gridPen = new Pen(Color.FromArgb(14, 245, 240, 223), 1);
        for (var i = -10; i <= 10; i += 2)
        {
            var a = WorldToScreen(new Vec2(i, -ArenaRadius));
            var b = WorldToScreen(new Vec2(i, ArenaRadius));
            g.DrawLine(gridPen, a, b);
            a = WorldToScreen(new Vec2(-ArenaRadius, i));
            b = WorldToScreen(new Vec2(ArenaRadius, i));
            g.DrawLine(gridPen, a, b);
        }

        if (stage.Theme == StageTheme.Card)
        {
            using var p1 = new Pen(Color.FromArgb(126, 102, 230, 207), 2.4f);
            using var p2 = new Pen(Color.FromArgb(112, 235, 126, 75), 2.0f);
            using var glow = new SolidBrush(Color.FromArgb(48, 95, 224, 205));
            using var tileFill = new SolidBrush(Color.FromArgb(30, 230, 255, 242));
            for (var i = 0; i < 16; i++)
            {
                var world = new Vec2(-8.8f + i * 1.18f, MathF.Sin(i * 1.7f) * 4.0f + MathF.Cos(i) * 0.8f);
                var pos = WorldToScreen(world);
                g.FillEllipse(tileFill, pos.X - 34, pos.Y - 13, 68, 26);
                DrawRotatedRect(g, pos, 64, 38, i * 17, i % 2 == 0 ? p1 : p2);
            }
            for (var i = 0; i < 4; i++)
            {
                var w = new Vec2(-7.2f + i * 4.8f, i % 2 == 0 ? -5.1f : 5.0f);
                var pos = WorldToScreen(w);
                g.FillEllipse(glow, pos.X - 92, pos.Y - 34, 184, 68);
                g.DrawEllipse(p1, pos.X - 62, pos.Y - 22, 124, 44);
                g.DrawEllipse(p2, pos.X - 34, pos.Y - 12, 68, 24);
            }
        }
        else if (stage.Theme == StageTheme.Forest)
        {
            using var blade = new Pen(Color.FromArgb(96, 165, 220, 98), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var vine = new Pen(Color.FromArgb(74, 58, 110, 54), 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var nest = new SolidBrush(Color.FromArgb(86, 48, 72, 42));
            using var pool = new SolidBrush(Color.FromArgb(70, 204, 240, 76));
            for (var i = 0; i < 28; i++)
            {
                var x = 16 + i * (ClientSize.Width - 32) / 27f;
                g.DrawBezier(i % 3 == 0 ? vine : blade, x, ClientSize.Height, x - 36, 520, x + 46, 350, x + (float)Math.Sin(i) * 24, 108);
            }
            for (var i = 0; i < 7; i++)
            {
                var pos = WorldToScreen(new Vec2(-8.6f + i * 2.9f, i % 2 == 0 ? 5.6f : -5.4f));
                g.FillEllipse(nest, pos.X - 54, pos.Y - 18, 108, 36);
                g.FillEllipse(pool, pos.X - 32, pos.Y - 9, 64, 18);
            }
        }
        else
        {
            using var red = new SolidBrush(Color.FromArgb(86, 170, 38, 54));
            using var gold = new Pen(Color.FromArgb(148, 238, 202, 116), 2.2f);
            using var velvet = new SolidBrush(Color.FromArgb(76, 238, 202, 116));
            var a = WorldToScreen(new Vec2(-3.2f, -ArenaRadius));
            var b = WorldToScreen(new Vec2(3.2f, -ArenaRadius));
            var c = WorldToScreen(new Vec2(4.8f, ArenaRadius * 0.92f));
            var d = WorldToScreen(new Vec2(-4.8f, ArenaRadius * 0.92f));
            g.FillPolygon(red, [a, b, c, d]);
            for (var i = 0; i < 7; i++)
            {
                var left = WorldToScreen(new Vec2(-9.6f, -8.2f + i * 2.7f));
                var right = WorldToScreen(new Vec2(9.6f, -8.2f + i * 2.7f));
                g.FillRectangle(velvet, left.X - 25, left.Y - 23, 50, 46);
                g.FillRectangle(velvet, right.X - 25, right.Y - 23, 50, 46);
                g.DrawRectangle(gold, left.X - 25, left.Y - 23, 50, 46);
                g.DrawRectangle(gold, right.X - 25, right.Y - 23, 50, 46);
            }
        }

        using var edge = new Pen(Color.FromArgb(84, stage.EnemyAura), 2.2f);
        g.DrawPolygon(edge, poly);
        using var vignette = new GraphicsPath();
        vignette.AddEllipse(-ClientSize.Width * 0.12f, -ClientSize.Height * 0.08f, ClientSize.Width * 1.24f, ClientSize.Height * 1.16f);
        using var dark = new SolidBrush(Color.FromArgb(18, 0, 0, 0));
        using var region = new Region(ClientRectangle);
        region.Exclude(vignette);
        g.FillRegion(dark, region);
    }

    private PointF[] ArenaPolygon()
    {
        return
        [
            WorldToScreen(new Vec2(-ArenaRadius, -ArenaRadius)),
            WorldToScreen(new Vec2(ArenaRadius, -ArenaRadius)),
            WorldToScreen(new Vec2(ArenaRadius + 1.1f, ArenaRadius * 0.96f)),
            WorldToScreen(new Vec2(-ArenaRadius - 1.1f, ArenaRadius * 0.96f))
        ];
    }

    private void DrawActor(Graphics g, Actor actor)
    {
        var screen = WorldToScreen(actor.Position);
        var height = actor.IsPlayer ? (actor.Build > 1.2f ? 172f : 132f) : 118f * actor.Build;
        var width = height * 0.52f;
        DrawShadow(g, screen, width);
        var state = SpriteState(actor);
        if (TryDrawActionSheet(g, actor, screen, height, state, SheetFrame(actor, state)))
        {
            DrawSheetHitFlash(g, actor, screen, height);
            return;
        }

        var frame = SpriteFrame(actor, state);
        var dir = DirectionIndex(actor.Facing);
        var sprite = GetSpriteFrame(actor, RigState(state), dir, frame, (int)MathF.Round(width), (int)MathF.Round(height));
        var originX = sprite.Width * 0.5f;
        var originY = sprite.Height - 24f;
        g.DrawImageUnscaled(sprite, (int)MathF.Round(screen.X - originX), (int)MathF.Round(screen.Y - originY));
    }

    private bool TryDrawActionSheet(Graphics g, Actor actor, PointF foot, float rigHeight, string state, int frame)
    {
        var relative = ActionSheetPath(actor);
        if (relative == null) return false;
        var sheet = LoadActionSheet(relative);
        if (sheet == null) return false;

        var sheetState = SheetState(state);
        var stateIndex = Array.IndexOf(SheetStates, sheetState);
        if (stateIndex < 0) return false;

        var dirIndex = SheetDirectionIndex(actor.Facing);
        var source = new Rectangle(
            ((dirIndex % SheetDirsPerRow) * SheetFrames + PositiveModulo(frame, SheetFrames)) * SheetFrameWidth,
            (stateIndex * 2 + dirIndex / SheetDirsPerRow) * SheetFrameHeight,
            SheetFrameWidth,
            SheetFrameHeight);
        if (source.Right > sheet.Width || source.Bottom > sheet.Height) return false;

        var drawHeight = rigHeight * (actor.IsPlayer ? actor.Build > 1.2f ? 1.30f : 1.25f : 1.30f);
        var drawWidth = drawHeight * SheetFrameWidth / SheetFrameHeight;
        var dest = new RectangleF(foot.X - drawWidth * 0.5f, foot.Y - drawHeight * 0.92f, drawWidth, drawHeight);
        var oldInterpolation = g.InterpolationMode;
        var oldPixel = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(sheet, dest, source, GraphicsUnit.Pixel);
        g.InterpolationMode = oldInterpolation;
        g.PixelOffsetMode = oldPixel;
        return true;
    }

    private void DrawSheetHitFlash(Graphics g, Actor actor, PointF foot, float rigHeight)
    {
        if (actor.HitFlash <= 0) return;
        var drawHeight = rigHeight * (actor.IsPlayer ? actor.Build > 1.2f ? 1.30f : 1.25f : 1.30f);
        var drawWidth = drawHeight * SheetFrameWidth / SheetFrameHeight;
        using var flash = new SolidBrush(Color.FromArgb((int)Math.Clamp(actor.HitFlash / 0.16f * 120, 0, 120), Color.White));
        g.FillEllipse(flash, foot.X - drawWidth * 0.32f, foot.Y - drawHeight * 0.70f, drawWidth * 0.64f, drawHeight * 0.52f);
    }

    private Bitmap? LoadActionSheet(string relative)
    {
        if (actionSheets.TryGetValue(relative, out var cached)) return cached;
        var path = ResolveAssetPath(relative);
        if (path == null) return null;
        var bitmap = new Bitmap(path);
        actionSheets[relative] = bitmap;
        return bitmap;
    }

    private Bitmap? LoadIllustration(string relative)
    {
        if (illustrationCache.TryGetValue(relative, out var cached)) return cached;
        var path = ResolveAssetPath(relative);
        if (path == null) return null;
        var bitmap = new Bitmap(path);
        illustrationCache[relative] = bitmap;
        return bitmap;
    }

    private static string? ResolveAssetPath(string relative)
    {
        var direct = Path.Combine(AppContext.BaseDirectory, relative);
        if (File.Exists(direct)) return direct;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && current != null; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? ActionSheetPath(Actor actor)
    {
        if (actor.IsPlayer)
        {
            return actor.Name switch
            {
                "Gon" => Path.Combine("assets", "characters", "action8", "deluxe_v1", "gon_action8.png"),
                "Killua" => Path.Combine("assets", "characters", "action8", "deluxe_v1", "killua_action8.png"),
                "Kurapika" => Path.Combine("assets", "characters", "action8", "deluxe_v1", "kurapika_action8.png"),
                "Awakened Gon" => Path.Combine("assets", "characters", "action8", "deluxe_v1", "adult_gon_action8.png"),
                _ => null
            };
        }

        var slug = actor.Name.ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return slug switch
        {
            "card_thief" or "bomb_trapper" or "mafia_gunner" or "shadow_beast_tank" or "ant_soldier" or "ant_commander"
                or "bomber_leader_boss" or "butler_captain_boss" or "ant_king_boss" or "assassin_butler"
                or "guard_beast" or "arena_trickster_boss" or "nen_boxer" or "rookie_fighter"
                => Path.Combine("assets", "enemies", "action8", "deluxe_v1", $"{slug}_action8.png"),
            _ => null
        };
    }

    private static string? CharacterIllustrationPath(string name)
    {
        return name switch
        {
            "Gon" => Path.Combine("assets", "illustrations", "deluxe_v1", "gon_illustration.png"),
            "Killua" => Path.Combine("assets", "illustrations", "deluxe_v1", "killua_illustration.png"),
            "Kurapika" => Path.Combine("assets", "illustrations", "deluxe_v1", "kurapika_illustration.png"),
            "Awakened Gon" => Path.Combine("assets", "illustrations", "deluxe_v1", "adult_gon_illustration.png"),
            _ => null
        };
    }

    private int SheetFrame(Actor actor, string state)
    {
        if (actor.Action is { } action) return Math.Clamp((int)(action.Elapsed / Math.Max(0.001f, action.Duration) * SheetFrames), 0, SheetFrames - 1);
        if (state == "hit") return Math.Clamp((int)((1 - actor.HitFlash / 0.16f) * SheetFrames), 0, SheetFrames - 1);
        if (state == "run") return PositiveModulo((int)MathF.Floor(actor.Motion * 1.35f), SheetFrames);
        return PositiveModulo((int)MathF.Floor(elapsed * 6.5f + actor.Motion), SheetFrames);
    }

    private static int SheetDirectionIndex(float angle) => PositiveModulo((int)MathF.Round((MathF.PI / 2 - angle) / (MathF.PI / 4)), SheetDirections);

    private static string SheetState(string state)
    {
        if (state is "light1" or "light2" or "light3" or "light4" or "heavy" or "ultimate" or "dash" or "run" or "idle" or "hit") return state;
        if (state is "skill-q" or "skill-e" or "skill-r" or "skill") return "skill";
        if (state is "light" or "enemy-hit") return "light1";
        return "idle";
    }

    private static string RigState(string state)
    {
        if (state is "light1" or "light2" or "light3" or "light4") return "light";
        if (state == "skill") return "skill-q";
        return state;
    }

    private static string ActionAnimation(string kind, int lightStep)
    {
        return kind switch
        {
            "light" => $"light{Math.Clamp(lightStep, 1, 4)}",
            "skill-q" or "skill-e" or "skill-r" => "skill",
            _ => kind
        };
    }

    private Bitmap GetSpriteFrame(Actor actor, string state, int dirIndex, int frame, int width, int height)
    {
        var key = new SpriteKey(ActorSpriteId(actor), PaletteStamp(actor.Palette), state, dirIndex, frame, width, height);
        if (spriteCache.TryGetValue(key, out var cached)) return cached;
        if (spriteCache.Count > SpriteCacheLimit) ClearSpriteCache();

        var pad = Math.Max(48, height / 3);
        var bmpW = Math.Max(96, width + pad * 2);
        var bmpH = Math.Max(128, height + pad + 32);
        var bitmap = new Bitmap(bmpW, bmpH);
        using var sg = Graphics.FromImage(bitmap);
        sg.SmoothingMode = SmoothingMode.AntiAlias;
        sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
        sg.Clear(Color.Transparent);
        sg.TranslateTransform(bmpW * 0.5f, bmpH - 24f);
        var synthetic = BuildSpriteActor(actor, state, dirIndex, frame);
        DrawRig(sg, synthetic, width, height);
        spriteCache[key] = bitmap;
        return bitmap;
    }

    private Actor BuildSpriteActor(Actor source, string state, int dirIndex, int frame)
    {
        var angle = dirIndex * MathF.PI * 2 / SpriteDirections;
        var synthetic = new Actor(source.Name, 0, 0, source.IsPlayer)
        {
            Palette = source.Palette,
            Build = source.Build,
            Facing = angle,
            Radius = source.Radius,
            Motion = frame * (MathF.PI * 2 / SpriteFrames) / 5.2f,
            HitAngle = angle + MathF.PI
        };
        var rigState = RigState(state);
        if (rigState is "run" or "dash") synthetic.Velocity = Vec2.FromAngle(angle) * (rigState == "dash" ? 9 : 4);
        if (rigState == "hit")
        {
            synthetic.HitFlash = 0.16f;
            synthetic.Stagger = 0.12f;
        }
        if (rigState is "light" or "heavy" or "skill-q" or "skill-e" or "skill-r" or "ultimate" or "enemy-hit" or "dash")
        {
            synthetic.Action = new ActionState(rigState, angle, 1f, 0.35f, 0, 1, 1)
            {
                Elapsed = (frame + 0.5f) / SpriteFrames
            };
        }
        return synthetic;
    }

    private void WarmSpriteCache()
    {
        var stage = stages[selectedStage];
        PreloadActionSheets(stage);
        if (ActionSheetPath(player) is { } playerSheet && LoadActionSheet(playerSheet) != null) return;

        var playerStates = new[] { "idle", "run", "light", "heavy", "skill-q", "skill-e", "skill-r", "ultimate", "hit" };
        var enemyNormal = new Actor("Soldier", 0, 0, false) { Build = 0.96f, Palette = Palette.ForEnemy(stage.EnemyAura, false), Radius = 0.36f };
        var enemyElite = new Actor("Elite", 0, 0, false) { Build = 1.18f, Palette = Palette.ForEnemy(stage.EnemyAura, true), Radius = 0.48f };
        var enemyStates = new[] { "idle", "run", "enemy-hit", "hit" };
        var dirs = Enumerable.Range(0, SpriteDirections).Where(i => i % 2 == 0).ToArray();
        var frames = Enumerable.Range(0, SpriteFrames).Where(i => i % 2 == 0).ToArray();

        var pHeight = player.IsPlayer ? (player.Build > 1.2f ? 172 : 132) : (int)(118 * player.Build);
        var pWidth = (int)MathF.Round(pHeight * 0.52f);
        foreach (var state in playerStates)
        foreach (var dir in dirs)
        foreach (var frame in frames)
            GetSpriteFrame(player, state, dir, frame, pWidth, pHeight);

        foreach (var enemy in new[] { enemyNormal, enemyElite })
        {
            var eHeight = (int)MathF.Round(118f * enemy.Build);
            var eWidth = (int)MathF.Round(eHeight * 0.52f);
            foreach (var state in enemyStates)
            foreach (var dir in dirs)
            foreach (var frame in frames)
                GetSpriteFrame(enemy, state, dir, frame, eWidth, eHeight);
        }
    }

    private void PreloadActionSheets(StageSpec stage)
    {
        foreach (var spec in characters)
        {
            var actor = new Actor(spec.Name, 0, 0, true) { Build = spec.Build, Palette = Palette.ForPlayer(spec) };
            if (ActionSheetPath(actor) is { } path) LoadActionSheet(path);
        }

        foreach (var elite in new[] { false, true })
        {
            var actor = new Actor(EnemyNameForStage(stage.Theme, elite), 0, 0, false);
            if (ActionSheetPath(actor) is { } path) LoadActionSheet(path);
        }

        var boss = new Actor(BossNameForStage(stage.Theme), 0, 0, false) { IsBoss = true };
        if (ActionSheetPath(boss) is { } bossPath) LoadActionSheet(bossPath);
    }

    private static string SpriteState(Actor actor)
    {
        if (actor.HitFlash > 0.02f || actor.Stagger > 0.02f) return "hit";
        if (actor.Action?.Animation is { } animation) return animation;
        return actor.Velocity.Length > 0.25f ? "run" : "idle";
    }

    private int SpriteFrame(Actor actor, string state)
    {
        if (actor.Action is { } action) return Math.Clamp((int)(action.Elapsed / Math.Max(0.001f, action.Duration) * SpriteFrames), 0, SpriteFrames - 1);
        if (state == "hit") return Math.Clamp((int)((1 - actor.HitFlash / 0.16f) * SpriteFrames), 0, SpriteFrames - 1);
        if (state == "run") return PositiveModulo((int)MathF.Floor(actor.Motion * 1.6f), SpriteFrames);
        return PositiveModulo((int)MathF.Floor(elapsed * 7f + actor.Motion), SpriteFrames);
    }

    private static int DirectionIndex(float angle) => PositiveModulo((int)MathF.Round(angle / (MathF.PI * 2) * SpriteDirections), SpriteDirections);
    private static int PositiveModulo(int value, int modulo) => (value % modulo + modulo) % modulo;
    private static string ActorSpriteId(Actor actor) => actor.IsPlayer ? $"player:{actor.Name}:{actor.Build:0.00}" : $"enemy:{actor.Name}:{actor.Build:0.00}";
    private static int PaletteStamp(Palette pal) => HashCode.Combine(pal.Skin.ToArgb(), pal.Hair.ToArgb(), pal.Jacket.ToArgb(), pal.Pants.ToArgb(), pal.Accent.ToArgb(), pal.Outline.ToArgb(), pal.LongHair);

    private void ClearSpriteCache()
    {
        foreach (var sprite in spriteCache.Values) sprite.Dispose();
        spriteCache.Clear();
    }

    private void ClearActionSheets()
    {
        foreach (var sheet in actionSheets.Values) sheet.Dispose();
        actionSheets.Clear();
    }

    private void ClearIllustrations()
    {
        foreach (var image in illustrationCache.Values) image.Dispose();
        illustrationCache.Clear();
    }

    private void DrawRig(Graphics g, Actor actor, float width, float height)
    {
        var pal = actor.Palette;
        var facing = actor.Facing;
        var dir = Vec2.FromAngle(facing);
        dir = new Vec2(dir.X, dir.Y * 0.62f).Normalized();
        if (dir.Length < 0.01f) dir = new Vec2(0, -1);
        var side = new Vec2(-dir.Y, dir.X);
        var action = actor.Action;
        var actionKind = action?.Kind ?? "";
        var actionT = action == null ? 0 : Math.Clamp(action.Elapsed / Math.Max(0.001f, action.Duration), 0, 1);
        var attacking = actionKind is "light" or "heavy" or "skill-q" or "skill-e" or "skill-r" or "ultimate" or "enemy-hit";
        var attack = attacking ? MathF.Sin(actionT * MathF.PI) : 0;
        var windup = attacking ? Math.Max(0, 1 - actionT / 0.32f) : 0;
        var strike = attacking ? MathF.Sin(Math.Clamp((actionT - 0.08f) / 0.68f, 0, 1) * MathF.PI) : 0;
        var hit = Math.Clamp(actor.HitFlash / 0.16f, 0, 1);
        var hitDir = Vec2.FromAngle(actor.HitAngle);
        var kick = actor.IsPlayer && actionKind is ("heavy" or "skill-e");
        var stomp = actor.IsPlayer && actionKind is ("skill-r" or "ultimate");
        var bothHands = actor.IsPlayer && actionKind is ("skill-r" or "ultimate");
        var cycle = MathF.Sin(actor.Motion * 5.2f);
        var moving = actor.Velocity.Length > 0.25f;
        var scale = height / 132f;

        if (pal.LongHair) DrawLongHair(g, dir, side, height, scale, cycle, pal);

        var hip = new Vec2(0, -height * 0.30f);
        var chest = new Vec2(side.X * cycle * (moving ? width * 0.025f : 0), -height * 0.61f)
            + dir * (strike * width * 0.10f - windup * width * 0.045f)
            + hitDir * (hit * width * 0.08f);
        var head = chest + dir * height * 0.04f + new Vec2(0, -height * 0.25f);
        var stride = moving ? width * 0.28f * cycle : attack * width * 0.10f;
        var front = kick ? -1 : 1;
        Vec2? actionFoot = null;

        for (var s = -1; s <= 1; s += 2)
        {
            var phase = cycle * s;
            var h = hip + side * (s * width * 0.15f);
            var attackStep = 0f;
            var lift = Math.Max(0, -phase) * height * 0.05f;
            if (attacking)
            {
                attackStep = s == front ? width * (kick ? 0.25f + strike * 0.55f - windup * 0.13f : 0.10f + strike * 0.16f) : -width * (kick ? 0.10f + strike * 0.18f : 0.04f + strike * 0.05f);
                if (kick && s == front) lift += height * (0.04f + strike * 0.10f);
                if (stomp && s == front) lift += height * (windup * 0.10f - strike * 0.03f);
            }
            var foot = side * (s * width * 0.17f) + dir * (phase * stride + attackStep);
            foot = foot with { Y = foot.Y - lift };
            var knee = Mix(h, foot, 0.55f) + side * (s * width * 0.06f) - dir * (phase * width * 0.06f + attackStep * 0.14f) + new Vec2(0, -height * (0.04f + (kick && s == front ? strike * 0.035f : 0)));
            DrawLimb(g, h, knee, foot, 8f * scale, pal.Pants, pal.Outline);
            DrawBoot(g, foot, dir, side, s, 12f * scale * (kick && s == front ? 1.18f : 1), 5f * scale, pal.Boots, pal.Outline, kick && s == front ? strike : 0);
            if (kick && s == front && strike > 0.18f) actionFoot = foot;
        }
        if (actionFoot is { } footAccent) DrawActionAccent(g, footAccent, dir, side, actor, strike, width * 1.15f);

        DrawTorso(g, chest, width * 0.38f, height * 0.32f, pal);

        for (var s = -1; s <= 1; s += 2)
        {
            var shoulder = chest + side * (s * width * 0.19f) + new Vec2(0, -height * 0.10f);
            var active = attacking && !kick && (bothHands || s == front);
            var guard = attacking && (kick || s != front);
            var swing = moving ? -cycle * s : 0;
            var hand = active
                ? shoulder + dir * (width * (0.18f + strike * (bothHands ? 0.48f : 0.62f) - windup * 0.24f)) + side * (s * width * (bothHands ? 0.16f : 0.12f - strike * 0.04f + windup * 0.06f)) + new Vec2(0, height * (0.08f - strike * 0.18f + windup * 0.08f))
                : shoulder + dir * (swing * width * 0.14f - (guard ? strike * width * 0.16f : 0)) + side * (s * width * 0.13f) + new Vec2(0, height * (0.22f + (guard ? strike * 0.04f : 0)));
            var elbow = Mix(shoulder, hand, active ? 0.44f : 0.52f) + side * (s * width * (active ? 0.10f + windup * 0.08f : 0.06f)) + new Vec2(0, active ? -height * (0.03f + windup * 0.04f) : 0);
            DrawArm(g, shoulder, elbow, hand, 6.5f * scale, pal.Jacket, pal.Skin, pal.Outline);
            DrawHand(g, hand, dir, side, 4.2f * scale, pal.Skin, pal.Outline, active ? strike : 0);
            if (active) DrawActionAccent(g, hand, dir, side, actor, attack, width);
        }

        DrawHead(g, head, 11f * scale, pal, dir, side);
        if (actor.HitFlash > 0)
        {
            using var white = new SolidBrush(Color.FromArgb((int)(actor.HitFlash / 0.16f * 120), Color.White));
            g.FillEllipse(white, chest.X - width * 0.32f, chest.Y - height * 0.28f, width * 0.64f, height * 0.55f);
        }
    }

    private void DrawTorso(Graphics g, Vec2 c, float width, float height, Palette pal)
    {
        using var outline = new SolidBrush(pal.Outline);
        using var jacket = new SolidBrush(pal.Jacket);
        using var jacketShade = new SolidBrush(Darken(pal.Jacket, 0.20f));
        using var jacketLight = new SolidBrush(Lighten(pal.Jacket, 0.16f));
        using var shirt = new SolidBrush(pal.Shirt);
        using var accent = new SolidBrush(pal.Accent);
        using var outer = RoundedDiamond(c, width * 0.86f, height * 0.57f, 7);
        g.FillPath(outline, outer);
        using var inner = RoundedDiamond(c + new Vec2(0, height * 0.01f), width * 0.74f, height * 0.51f, 6);
        g.FillPath(jacket, inner);
        using var leftPanel = new GraphicsPath();
        leftPanel.AddPolygon([
            new PointF(c.X - width * 0.55f, c.Y - height * 0.22f),
            new PointF(c.X - width * 0.10f, c.Y - height * 0.05f),
            new PointF(c.X - width * 0.20f, c.Y + height * 0.47f),
            new PointF(c.X - width * 0.58f, c.Y + height * 0.38f)
        ]);
        g.FillPath(jacketShade, leftPanel);
        using var rightPanel = new GraphicsPath();
        rightPanel.AddPolygon([
            new PointF(c.X + width * 0.12f, c.Y - height * 0.08f),
            new PointF(c.X + width * 0.54f, c.Y - height * 0.24f),
            new PointF(c.X + width * 0.56f, c.Y + height * 0.36f),
            new PointF(c.X + width * 0.20f, c.Y + height * 0.46f)
        ]);
        g.FillPath(jacketLight, rightPanel);
        using var shirtPath = RoundedDiamond(c + new Vec2(0, height * 0.04f), width * 0.32f, height * 0.44f, 3);
        g.FillPath(shirt, shirtPath);
        using var seam = new Pen(pal.Outline, Math.Max(1.4f, width * 0.035f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var trim = new Pen(pal.Accent, Math.Max(1.2f, width * 0.025f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(seam, c.X - width * 0.48f, c.Y - height * 0.24f, c.X - width * 0.13f, c.Y + height * 0.39f);
        g.DrawLine(seam, c.X + width * 0.48f, c.Y - height * 0.24f, c.X + width * 0.13f, c.Y + height * 0.39f);
        g.DrawLine(trim, c.X - width * 0.25f, c.Y + height * 0.18f, c.X + width * 0.25f, c.Y + height * 0.18f);
        g.FillRectangle(outline, c.X - width * 0.40f, c.Y + height * 0.29f, width * 0.80f, Math.Max(3, height * 0.09f));
        g.FillRectangle(accent, c.X - width * 0.34f, c.Y + height * 0.31f, width * 0.68f, Math.Max(2, height * 0.045f));
    }

    private static GraphicsPath RoundedDiamond(Vec2 c, float rx, float ry, int curve)
    {
        var p = new GraphicsPath();
        p.AddBezier(c.X - rx, c.Y - ry * 0.58f, c.X - rx * 0.4f, c.Y - ry, c.X + rx * 0.4f, c.Y - ry, c.X + rx, c.Y - ry * 0.58f);
        p.AddLine(c.X + rx, c.Y - ry * 0.58f, c.X + rx * 0.62f, c.Y + ry);
        p.AddBezier(c.X + rx * 0.62f, c.Y + ry, c.X + rx * 0.24f, c.Y + ry + curve, c.X - rx * 0.24f, c.Y + ry + curve, c.X - rx * 0.62f, c.Y + ry);
        p.CloseFigure();
        return p;
    }

    private static void DrawLimb(Graphics g, Vec2 a, Vec2 b, Vec2 c, float w, Color fill, Color outline)
    {
        DrawTaperedSegment(g, a, b, w * 0.92f, w * 0.70f, Lighten(fill, 0.06f), outline);
        DrawTaperedSegment(g, b, c, w * 0.68f, w * 0.54f, Darken(fill, 0.04f), outline);
        var axis = (c - a).Normalized();
        if (axis.Length < 0.01f) axis = new Vec2(1, 0);
        FillRotatedEllipse(g, b, w * 1.25f, w * 0.86f, Degrees(axis), Lighten(fill, 0.10f), outline, 1.5f);
        using var crease = new Pen(Color.FromArgb(88, Darken(fill, 0.35f)), Math.Max(1, w * 0.13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(crease, (b - axis * w * 0.35f).ToPointF(), (b + axis * w * 0.25f).ToPointF());
    }

    private static void DrawArm(Graphics g, Vec2 shoulder, Vec2 elbow, Vec2 hand, float w, Color sleeve, Color skin, Color outline)
    {
        DrawTaperedSegment(g, shoulder, elbow, w * 0.86f, w * 0.64f, Lighten(sleeve, 0.06f), outline);
        DrawTaperedSegment(g, elbow, hand, w * 0.50f, w * 0.38f, skin, outline);
        var forearm = (hand - elbow).Normalized();
        if (forearm.Length < 0.01f) forearm = new Vec2(1, 0);
        FillRotatedEllipse(g, elbow, w * 1.05f, w * 0.70f, Degrees(forearm), sleeve, outline, 1.5f);
        var cuff = Mix(elbow, hand, 0.18f);
        FillRotatedEllipse(g, cuff, w * 0.90f, w * 0.44f, Degrees(forearm), Darken(sleeve, 0.18f), outline, 1.0f);
    }

    private static void DrawBoot(Graphics g, Vec2 p, Vec2 dir, Vec2 side, int sideSign, float w, float h, Color fill, Color outline, float extension)
    {
        var axis = (dir * (0.80f + extension * 0.45f) + side * (sideSign * 0.32f)).Normalized();
        if (axis.Length < 0.01f) axis = new Vec2(1, 0);
        var toe = w * (0.10f + extension * 0.34f);
        var center = p + axis * toe * 0.34f;
        var old = g.Transform;
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(Degrees(axis));
        using var o = new SolidBrush(outline);
        using var f = new SolidBrush(fill);
        using var hi = new SolidBrush(Lighten(fill, 0.18f));
        g.FillEllipse(o, -w * 0.70f, -h * 1.15f, w * 1.40f + toe, h * 2.30f);
        g.FillEllipse(f, -w * 0.60f, -h * 0.93f, w * 1.22f + toe, h * 1.86f);
        g.FillEllipse(hi, -w * 0.24f, -h * 0.72f, w * 0.62f + toe * 0.35f, h * 0.52f);
        using var sole = new Pen(Darken(fill, 0.45f), Math.Max(1, h * 0.28f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(sole, -w * 0.38f, h * 0.52f, w * 0.56f + toe, h * 0.48f);
        g.Transform = old;
    }

    private static void DrawHand(Graphics g, Vec2 p, Vec2 dir, Vec2 side, float r, Color fill, Color outline, float power)
    {
        var axis = (dir * (0.80f + power * 0.25f) + side * 0.18f).Normalized();
        if (axis.Length < 0.01f) axis = new Vec2(1, 0);
        var old = g.Transform;
        g.TranslateTransform(p.X, p.Y);
        g.RotateTransform(Degrees(axis));
        using var o = new SolidBrush(outline);
        using var f = new SolidBrush(fill);
        using var thumb = new SolidBrush(Darken(fill, 0.10f));
        g.FillEllipse(o, -r * 1.22f - 1.5f, -r * 0.88f - 1.5f, r * 2.44f + 3, r * 1.76f + 3);
        g.FillEllipse(f, -r * 1.12f, -r * 0.78f, r * 2.24f, r * 1.56f);
        g.FillEllipse(thumb, -r * 0.15f, r * 0.08f, r * 0.90f, r * 0.56f);
        using var knuckle = new Pen(Darken(fill, 0.32f), Math.Max(1, r * 0.16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < 3; i++)
        {
            var x = -r * 0.58f + i * r * 0.42f + power * r * 0.24f;
            g.DrawLine(knuckle, x, -r * 0.48f, x + r * 0.18f, -r * 0.08f);
        }
        if (power > 0.20f)
        {
            using var flash = new Pen(Color.FromArgb((int)Math.Clamp(90 + power * 120, 0, 255), Color.White), Math.Max(1, r * 0.22f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(flash, r * 0.30f, -r * 0.62f, r * (1.05f + power * 0.55f), -r * 0.82f);
        }
        g.Transform = old;
    }

    private void DrawHead(Graphics g, Vec2 head, float r, Palette pal, Vec2 dir, Vec2 side)
    {
        using var outline = new SolidBrush(pal.Outline);
        using var skin = new SolidBrush(pal.Skin);
        using var hair = new SolidBrush(pal.Hair);
        var isKillua = pal.Hair.R > 210 && pal.Hair.G > 210 && pal.Hair.B > 210;
        var isKurapika = pal.Hair.R > 180 && pal.Hair.G > 140 && pal.Hair.B < 140 && !pal.LongHair;

        using var face = new GraphicsPath();
        face.AddBezier(head.X - r * 0.86f, head.Y - r * 0.62f, head.X - r * 0.82f, head.Y - r * 1.08f, head.X + r * 0.82f, head.Y - r * 1.08f, head.X + r * 0.86f, head.Y - r * 0.62f);
        face.AddBezier(head.X + r * 0.86f, head.Y - r * 0.62f, head.X + r * 0.72f, head.Y + r * 0.66f, head.X + r * 0.30f, head.Y + r * 1.08f, head.X, head.Y + r * 1.15f);
        face.AddBezier(head.X, head.Y + r * 1.15f, head.X - r * 0.30f, head.Y + r * 1.08f, head.X - r * 0.72f, head.Y + r * 0.66f, head.X - r * 0.86f, head.Y - r * 0.62f);
        g.FillEllipse(outline, head.X - r - 2, head.Y - r * 1.15f - 2, r * 2 + 4, r * 2.38f + 4);
        g.FillPath(skin, face);

        using var faceShade = new SolidBrush(Color.FromArgb(42, Darken(pal.Skin, 0.32f)));
        g.FillEllipse(faceShade, head.X - r * 0.82f + dir.X * r * 0.18f, head.Y + r * 0.18f, r * 1.64f, r * 0.90f);

        using var hairCap = new GraphicsPath();
        hairCap.AddBezier(head.X - r * 0.98f, head.Y - r * 0.46f, head.X - r * 1.04f, head.Y - r * 1.48f, head.X + r * 0.98f, head.Y - r * 1.52f, head.X + r * 1.00f, head.Y - r * 0.45f);
        hairCap.AddBezier(head.X + r * 1.00f, head.Y - r * 0.45f, head.X + r * 0.42f, head.Y - r * 0.12f, head.X - r * 0.42f, head.Y - r * 0.12f, head.X - r * 0.98f, head.Y - r * 0.46f);
        g.FillPath(hair, hairCap);

        if (isKurapika)
        {
            g.FillEllipse(hair, head.X - r * 1.08f, head.Y - r * 0.72f, r * 0.52f, r * 1.38f);
            g.FillEllipse(hair, head.X + r * 0.56f, head.Y - r * 0.72f, r * 0.52f, r * 1.38f);
            using var lockPen = new Pen(Darken(pal.Hair, 0.20f), Math.Max(1, r * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawBezier(lockPen, head.X - r * 0.68f, head.Y - r * 0.68f, head.X - r * 0.98f, head.Y - r * 0.08f, head.X - r * 0.76f, head.Y + r * 0.52f, head.X - r * 0.50f, head.Y + r * 0.78f);
            g.DrawBezier(lockPen, head.X + r * 0.68f, head.Y - r * 0.68f, head.X + r * 0.98f, head.Y - r * 0.08f, head.X + r * 0.76f, head.Y + r * 0.52f, head.X + r * 0.50f, head.Y + r * 0.78f);
        }
        else
        {
            var spikes = pal.LongHair ? 11 : isKillua ? 12 : 8;
            for (var i = 0; i < spikes; i++)
            {
                var u = i / Math.Max(1f, spikes - 1f) - 0.5f;
                var root = head + side * (u * r * (isKillua ? 1.85f : 1.55f)) + new Vec2(0, -r * (pal.LongHair ? 1.10f : 1.14f));
                var lift = isKillua ? 1.12f + Math.Abs(u) * 0.78f : pal.LongHair ? 0.92f + Math.Abs(u) * 0.42f : 0.86f + Math.Abs(u) * 0.96f;
                var spread = isKillua ? u * r * 1.35f : u * r * 1.02f;
                var tip = root + side * spread - new Vec2(0, r * lift);
                DrawHairSpike(g, root, tip, r * (isKillua ? 0.34f : 0.28f), pal.Hair, pal.Outline);
            }
        }

        using var hairHi = new Pen(Color.FromArgb(isKillua ? 120 : 70, Lighten(pal.Hair, 0.40f)), Math.Max(1, r * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawBezier(hairHi, head.X - r * 0.48f, head.Y - r * 1.02f, head.X - r * 0.10f, head.Y - r * 1.38f, head.X + r * 0.34f, head.Y - r * 1.30f, head.X + r * 0.62f, head.Y - r * 0.92f);

        if (dir.Y > -0.45f)
        {
            using var eye = new SolidBrush(Color.FromArgb(12, 18, 18));
            using var brow = new Pen(pal.Outline, Math.Max(1, r * 0.10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var eyeShift = dir.X * r * 0.16f;
            g.DrawLine(brow, head.X - r * 0.56f + eyeShift, head.Y - r * 0.22f, head.X - r * 0.20f + eyeShift, head.Y - r * 0.28f);
            g.DrawLine(brow, head.X + r * 0.20f + eyeShift, head.Y - r * 0.28f, head.X + r * 0.56f + eyeShift, head.Y - r * 0.22f);
            g.FillEllipse(eye, head.X - r * 0.45f + eyeShift, head.Y - r * 0.10f, r * 0.18f, r * 0.20f);
            g.FillEllipse(eye, head.X + r * 0.31f + eyeShift, head.Y - r * 0.10f, r * 0.18f, r * 0.20f);
            using var nose = new Pen(Color.FromArgb(88, Darken(pal.Skin, 0.36f)), Math.Max(1, r * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(nose, head.X + dir.X * r * 0.22f, head.Y + r * 0.02f, head.X + dir.X * r * 0.32f, head.Y + r * 0.30f);
            using var mouth = new Pen(Color.FromArgb(110, Darken(pal.Skin, 0.52f)), Math.Max(1, r * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(mouth, head.X - r * 0.18f, head.Y + r * 0.54f, head.X + r * 0.22f, head.Y + r * 0.52f);
        }
    }

    private static void DrawTaperedSegment(Graphics g, Vec2 a, Vec2 b, float wa, float wb, Color fill, Color outline)
    {
        var d = b - a;
        if (d.Length < 0.01f) return;
        d = d.Normalized();
        var n = new Vec2(-d.Y, d.X);
        using var outlineBrush = new SolidBrush(outline);
        using var fillBrush = new SolidBrush(fill);
        using var outlinePath = TaperedPath(a, b, wa + 2.2f, wb + 2.2f, n);
        using var fillPath = TaperedPath(a, b, wa, wb, n);
        g.FillPath(outlineBrush, outlinePath);
        g.FillEllipse(outlineBrush, a.X - wa - 2.2f, a.Y - wa - 2.2f, (wa + 2.2f) * 2, (wa + 2.2f) * 2);
        g.FillEllipse(outlineBrush, b.X - wb - 2.2f, b.Y - wb - 2.2f, (wb + 2.2f) * 2, (wb + 2.2f) * 2);
        g.FillPath(fillBrush, fillPath);
        g.FillEllipse(fillBrush, a.X - wa, a.Y - wa, wa * 2, wa * 2);
        g.FillEllipse(fillBrush, b.X - wb, b.Y - wb, wb * 2, wb * 2);
        using var shine = new Pen(Color.FromArgb(44, Color.White), Math.Max(1, Math.Min(wa, wb) * 0.15f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(shine, (a - n * wa * 0.34f + d * wa * 0.24f).ToPointF(), (b - n * wb * 0.22f - d * wb * 0.32f).ToPointF());
    }

    private static GraphicsPath TaperedPath(Vec2 a, Vec2 b, float wa, float wb, Vec2 n)
    {
        var path = new GraphicsPath();
        path.AddPolygon([
            (a + n * wa).ToPointF(),
            (b + n * wb).ToPointF(),
            (b - n * wb).ToPointF(),
            (a - n * wa).ToPointF()
        ]);
        path.CloseFigure();
        return path;
    }

    private static void FillRotatedEllipse(Graphics g, Vec2 center, float width, float height, float degrees, Color fill, Color outline, float pad)
    {
        var old = g.Transform;
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(degrees);
        using var o = new SolidBrush(outline);
        using var f = new SolidBrush(fill);
        g.FillEllipse(o, -width * 0.5f - pad, -height * 0.5f - pad, width + pad * 2, height + pad * 2);
        g.FillEllipse(f, -width * 0.5f, -height * 0.5f, width, height);
        g.Transform = old;
    }

    private static void DrawHairSpike(Graphics g, Vec2 root, Vec2 tip, float halfWidth, Color fill, Color outline)
    {
        var d = (tip - root).Normalized();
        if (d.Length < 0.01f) d = new Vec2(0, -1);
        var n = new Vec2(-d.Y, d.X);
        using var o = new SolidBrush(outline);
        using var f = new SolidBrush(fill);
        using var po = new GraphicsPath();
        po.AddPolygon([(root - n * (halfWidth + 1.5f)).ToPointF(), tip.ToPointF(), (root + n * (halfWidth + 1.5f)).ToPointF()]);
        g.FillPath(o, po);
        using var pf = new GraphicsPath();
        pf.AddPolygon([(root - n * halfWidth).ToPointF(), tip.ToPointF(), (root + n * halfWidth).ToPointF()]);
        g.FillPath(f, pf);
    }

    private static Color Lighten(Color color, float amount) => Blend(color, Color.White, amount);
    private static Color Darken(Color color, float amount) => Blend(color, Color.Black, amount);

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            from.A,
            (int)MathF.Round(from.R + (to.R - from.R) * amount),
            (int)MathF.Round(from.G + (to.G - from.G) * amount),
            (int)MathF.Round(from.B + (to.B - from.B) * amount));
    }

    private static float Degrees(Vec2 v) => MathF.Atan2(v.Y, v.X) * 180f / MathF.PI;

    private void DrawLongHair(Graphics g, Vec2 dir, Vec2 side, float height, float scale, float cycle, Palette pal)
    {
        using var pen = new Pen(pal.Hair, 4.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var hi = new Pen(pal.Accent, 1.8f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < 18; i++)
        {
            var u = i / 17f - 0.5f;
            var root = side * (u * 48f * scale) + new Vec2(0, -height * 0.86f);
            var tip = side * (u * 34f * scale + MathF.Sin(cycle + i) * 3f) - dir * (16f * scale) + new Vec2(0, -height * 0.18f + Math.Abs(u) * 10f);
            var mid = Mix(root, tip, 0.48f) - dir * 24f * scale;
            g.DrawBezier(i % 3 == 0 ? hi : pen, root.ToPointF(), mid.ToPointF(), mid.ToPointF(), tip.ToPointF());
        }
    }

    private void DrawActionAccent(Graphics g, Vec2 hand, Vec2 dir, Vec2 side, Actor actor, float power, float width)
    {
        using var pen = new Pen(actor.Palette.Accent, Math.Max(2, width * 0.025f + power * 3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var glow = new Pen(Color.FromArgb((int)(90 + power * 90), actor.Palette.Accent), Math.Max(4, width * 0.05f + power * 6)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var aura = new SolidBrush(Color.FromArgb((int)Math.Clamp(28 + power * 70, 0, 140), actor.Palette.Accent));
        using var white = new Pen(Color.FromArgb((int)Math.Clamp(60 + power * 150, 0, 230), Color.White), Math.Max(1, width * 0.010f + power * 1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.FillEllipse(aura, hand.X - width * (0.12f + power * 0.04f), hand.Y - width * (0.09f + power * 0.04f), width * (0.24f + power * 0.08f), width * (0.16f + power * 0.08f));
        g.DrawBezier(
            glow,
            (hand - dir * width * (0.24f + power * 0.10f) + side * width * 0.08f).ToPointF(),
            (hand - dir * width * 0.05f - new Vec2(0, width * 0.18f)).ToPointF(),
            (hand + dir * width * 0.16f - new Vec2(0, width * 0.16f)).ToPointF(),
            (hand + dir * width * (0.35f + power * 0.16f) - side * width * 0.08f).ToPointF());
        g.DrawBezier(
            white,
            (hand - dir * width * 0.12f + side * width * 0.04f).ToPointF(),
            (hand + dir * width * 0.04f - new Vec2(0, width * 0.10f)).ToPointF(),
            (hand + dir * width * 0.18f - new Vec2(0, width * 0.08f)).ToPointF(),
            (hand + dir * width * (0.30f + power * 0.12f)).ToPointF());
        if (actor.Name.Contains("Killua", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 4; i++)
            {
                var mid = hand + dir * (width * (0.12f + i * 0.045f)) + side * ((i - 1.5f) * width * 0.045f);
                var end = hand + dir * (width * (0.30f + i * 0.07f)) + side * ((i - 1.5f) * width * 0.08f + MathF.Sin(elapsed * 18 + i) * 3);
                g.DrawLine(glow, hand.ToPointF(), end.ToPointF());
                g.DrawLine(pen, mid.ToPointF(), end.ToPointF());
                g.DrawLine(white, mid.ToPointF(), end.ToPointF());
            }
        }
        else if (actor.Name.Contains("Kurapika", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < 5; i++)
            {
                var u = i / 4f;
                var a = hand + dir * (width * (0.12f + u * 0.38f)) + side * (MathF.Sin(u * MathF.PI * 3) * width * 0.08f);
                var b = hand + dir * (width * (0.18f + u * 0.44f)) + side * (MathF.Sin(u * MathF.PI * 3 + 0.6f) * width * 0.08f);
                g.DrawLine(pen, a.ToPointF(), b.ToPointF());
                DrawRotatedEllipseStroke(g, b.ToPointF(), width * 0.055f, width * 0.032f, Degrees(dir) + i * 72, pen, white);
            }
        }
        else
        {
            g.DrawEllipse(pen, hand.X + dir.X * width * 0.03f - width * 0.09f, hand.Y + dir.Y * width * 0.03f - width * 0.05f, width * 0.18f, width * 0.1f);
            g.DrawEllipse(white, hand.X + dir.X * width * 0.10f - width * 0.055f, hand.Y + dir.Y * width * 0.06f - width * 0.032f, width * 0.11f, width * 0.064f);
            if (actor.Name.Contains("Awakened", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = -1; i <= 1; i++)
                {
                    var start = hand + side * (i * width * 0.06f);
                    var end = start + dir * width * (0.34f + power * 0.18f) - new Vec2(0, width * (0.05f + Math.Abs(i) * 0.03f));
                    g.DrawLine(glow, start.ToPointF(), end.ToPointF());
                    g.DrawLine(white, start.ToPointF(), end.ToPointF());
                }
            }
        }
    }

    private void DrawEffect(Graphics g, Effect e)
    {
        var p = WorldToScreen(e.Position);
        var t = Math.Clamp(e.Age / e.Life, 0, 1);
        using var pen = new Pen(Color.FromArgb((int)((1 - t) * 180), e.Color), Math.Max(1, 3 + e.Radius * 1.8f));
        using var brush = new SolidBrush(Color.FromArgb((int)((1 - t) * 90), e.Color));
        if (e.Kind == EffectKind.Ring)
        {
            var r = e.Radius * 42 * (0.20f + t * 0.86f);
            using var core = new Pen(Color.FromArgb((int)((1 - t) * 150), Color.White), Math.Max(1, 1.6f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(pen, p.X - r, p.Y - r * 0.48f, r * 2, r * 0.96f);
            g.DrawEllipse(core, p.X - r * 0.78f, p.Y - r * 0.36f, r * 1.56f, r * 0.72f);
            using var dust = new SolidBrush(Color.FromArgb((int)((1 - t) * 38), e.Color));
            for (var i = 0; i < 8; i++)
            {
                var a = e.Seed + i * MathF.PI * 0.25f;
                var d = new Vec2(MathF.Cos(a) * r * 0.92f, MathF.Sin(a) * r * 0.34f);
                g.FillEllipse(dust, p.X + d.X - 3, p.Y + d.Y - 2, 6, 4);
            }
        }
        else if (e.Kind == EffectKind.Burst)
        {
            var r = e.Radius * 48 * (0.42f + t * 0.72f);
            using var glow = new SolidBrush(Color.FromArgb((int)((1 - t) * 76), e.Color));
            using var core = new SolidBrush(Color.FromArgb((int)((1 - t) * 190), Color.White));
            g.FillEllipse(glow, p.X - r, p.Y - r * 0.62f, r * 2, r * 1.24f);
            g.FillEllipse(core, p.X - r * 0.28f, p.Y - r * 0.18f, r * 0.56f, r * 0.36f);
            using var ray = new Pen(Color.FromArgb((int)((1 - t) * 190), e.Color), Math.Max(1, e.Radius * 2.2f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (var i = 0; i < 12; i++)
            {
                var a = e.Seed + i * MathF.PI / 6f;
                var start = OffsetPoint(p, new Vec2(MathF.Cos(a) * r * 0.16f, MathF.Sin(a) * r * 0.09f));
                var end = OffsetPoint(p, new Vec2(MathF.Cos(a) * r * 0.90f, MathF.Sin(a) * r * 0.45f));
                g.DrawLine(ray, start, end);
            }
        }
        else if (e.Kind == EffectKind.Slash)
        {
            var dir = Vec2.FromAngle(e.Facing);
            var side = new Vec2(-dir.Y, dir.X);
            var a = OffsetPoint(p, dir * e.Radius * 22 - side * e.Arc * 18);
            var b = OffsetPoint(p, dir * e.Radius * 48);
            var c = OffsetPoint(p, dir * e.Radius * 22 + side * e.Arc * 18);
            for (var layer = 0; layer < 3; layer++)
            {
                var offset = side * ((layer - 1) * e.Arc * (5 + layer * 2));
                using var trail = new Pen(Color.FromArgb((int)((1 - t) * (44 + layer * 26)), e.Color), Math.Max(3, e.Radius * (7.8f - layer * 1.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawBezier(trail, OffsetPoint(a, offset), OffsetPoint(b, offset * 0.25f), OffsetPoint(b, offset * 0.25f), OffsetPoint(c, offset));
            }
            using var cut = new Pen(Color.FromArgb((int)((1 - t) * 210), Color.White), Math.Max(1.4f, e.Radius * 1.15f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawBezier(pen, a, b, b, c);
            g.DrawBezier(cut, a, b, b, c);
        }
        else if (e.Kind == EffectKind.Impact)
        {
            var dir = Vec2.FromAngle(e.Facing);
            var side = new Vec2(-dir.Y, dir.X);
            var center = OffsetPoint(p, dir * e.Radius * 20 + new Vec2(0, -32));
            using var glow = new SolidBrush(Color.FromArgb((int)((1 - t) * 115), e.Color));
            using var white = new SolidBrush(Color.FromArgb((int)((1 - t) * 210), Color.White));
            g.FillEllipse(glow, center.X - e.Radius * 24 * (1 - t * 0.35f), center.Y - e.Radius * 14, e.Radius * 48, e.Radius * 28);
            g.FillEllipse(white, center.X - e.Radius * 8, center.Y - e.Radius * 5, e.Radius * 16, e.Radius * 10);
            using var ray = new Pen(Color.FromArgb((int)((1 - t) * 230), e.Color), Math.Max(2, e.Radius * 3.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var rayCore = new Pen(Color.FromArgb((int)((1 - t) * 210), Color.White), Math.Max(1, e.Radius * 1.15f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (var i = 0; i < e.Count; i++)
            {
                var u = i / Math.Max(1f, e.Count - 1f) - 0.5f;
                var spread = side * (u * e.Radius * 58);
                var start = OffsetPoint(center, spread * 0.18f - dir * e.Radius * 8);
                var end = OffsetPoint(center, spread + dir * e.Radius * (24 + (i % 3) * 8) + new Vec2(0, MathF.Sin(e.Seed + i) * 8));
                g.DrawLine(ray, start, end);
                if (i % 2 == 0) g.DrawLine(rayCore, start, end);
            }
        }
        else if (e.Kind == EffectKind.Shockwave)
        {
            var r = e.Radius * 58 * (0.18f + t * 0.82f);
            using var shock = new Pen(Color.FromArgb((int)((1 - t) * 210), e.Color), Math.Max(2, 9 * (1 - t) * e.Width)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var inner = new Pen(Color.FromArgb((int)((1 - t) * 130), Color.White), Math.Max(1, 2.2f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(shock, p.X - r, p.Y - r * 0.34f - 8, r * 2, r * 0.68f);
            g.DrawEllipse(inner, p.X - r * 0.70f, p.Y - r * 0.24f - 8, r * 1.40f, r * 0.48f);
            using var fill = new SolidBrush(Color.FromArgb((int)((1 - t) * 35), e.Color));
            g.FillEllipse(fill, p.X - r * 0.72f, p.Y - r * 0.23f - 8, r * 1.44f, r * 0.46f);
            using var debris = new SolidBrush(Color.FromArgb((int)((1 - t) * 85), Darken(e.Color, 0.20f)));
            for (var i = 0; i < 10; i++)
            {
                var a = e.Seed + i * MathF.PI * 0.2f;
                var x = MathF.Cos(a) * r * (0.54f + 0.05f * (i % 3));
                var y = MathF.Sin(a) * r * 0.24f;
                g.FillEllipse(debris, p.X + x - 2, p.Y - 8 + y - 1.5f, 4, 3);
            }
        }
        else if (e.Kind == EffectKind.Lightning)
        {
            var dir = Vec2.FromAngle(e.Facing);
            var side = new Vec2(-dir.Y, dir.X);
            using var glow = new Pen(Color.FromArgb((int)((1 - t) * 72), e.Color), Math.Max(5, 12f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var bolt = new Pen(Color.FromArgb((int)((1 - t) * 235), e.Color), Math.Max(2, 4.2f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var core = new Pen(Color.FromArgb((int)((1 - t) * 245), Color.White), Math.Max(1, 1.5f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var branch = new Pen(Color.FromArgb((int)((1 - t) * 170), Lighten(e.Color, 0.34f)), Math.Max(1, 2.1f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (var b = 0; b < e.Count; b++)
            {
                var offset = b - (e.Count - 1) * 0.5f;
                var points = new PointF[7];
                for (var i = 0; i < points.Length; i++)
                {
                    var u = i / (points.Length - 1f);
                    var jitter = MathF.Sin(e.Seed + b * 2.1f + i * 1.7f + elapsed * 18) * 12 * (1 - t);
                    var pos = new Vec2(0, -42) + dir * (e.Radius * 54 * u) + side * (offset * 9 + jitter);
                    points[i] = OffsetPoint(p, pos);
                }
                g.DrawLines(glow, points);
                g.DrawLines(bolt, points);
                g.DrawLines(core, points);
                for (var i = 2; i < points.Length - 1; i += 2)
                {
                    var branchDir = (dir * 0.55f + side * ((i % 4 == 0 ? 1 : -1) * 0.65f)).Normalized();
                    var end = OffsetPoint(points[i], branchDir * e.Radius * (13 + b * 2) * (1 - t * 0.35f));
                    g.DrawLine(branch, points[i], end);
                }
            }
        }
        else if (e.Kind == EffectKind.Chain)
        {
            var dir = Vec2.FromAngle(e.Facing);
            var side = new Vec2(-dir.Y, dir.X);
            using var glow = new Pen(Color.FromArgb((int)((1 - t) * 70), e.Color), Math.Max(4, 7.5f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var chain = new Pen(Color.FromArgb((int)((1 - t) * 225), e.Color), Math.Max(2, 3.0f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var core = new Pen(Color.FromArgb((int)((1 - t) * 160), Color.White), Math.Max(1, 1.2f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var start = OffsetPoint(p, new Vec2(0, -38));
            for (var i = 1; i <= e.Count; i++)
            {
                var u = i / (float)e.Count;
                var pos = OffsetPoint(p, new Vec2(0, -38) + dir * (e.Radius * 54 * u) + side * (MathF.Sin(u * MathF.PI * 4 + e.Seed) * 9));
                g.DrawLine(glow, start, pos);
                g.DrawLine(chain, start, pos);
                DrawRotatedEllipseStroke(g, pos, 13f * (1 - t * 0.18f), 7.5f * (1 - t * 0.10f), Degrees(dir) + (i % 2 == 0 ? 82 : -12), chain, core);
                start = pos;
            }
            using var seal = new Pen(Color.FromArgb((int)((1 - t) * 210), e.Color), Math.Max(1, 2.4f * (1 - t)));
            DrawRotatedRect(g, start, 20 * (1 - t * 0.25f), 20 * (1 - t * 0.25f), Degrees(dir) + elapsed * 80, seal);
        }
        else if (e.Kind == EffectKind.GroundCrack)
        {
            var dir = Vec2.FromAngle(e.Facing);
            var side = new Vec2(-dir.Y, dir.X);
            using var crack = new Pen(Color.FromArgb((int)((1 - t) * 190), e.Color), Math.Max(2, 4.4f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var edge = new Pen(Color.FromArgb((int)((1 - t) * 110), Lighten(e.Color, 0.35f)), Math.Max(1, 1.4f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fillCrack = new SolidBrush(Color.FromArgb((int)((1 - t) * 120), Darken(e.Color, 0.35f)));
            using var debris = new SolidBrush(Color.FromArgb((int)((1 - t) * 105), Darken(e.Color, 0.12f)));
            for (var i = 0; i < e.Count; i++)
            {
                var u = i / Math.Max(1f, e.Count - 1f) - 0.5f;
                var origin = OffsetPoint(p, side * (u * e.Radius * 54));
                var end = OffsetPoint(origin, dir * (e.Radius * (18 + (i % 4) * 9) * (1 + t)) + side * (MathF.Sin(e.Seed + i) * 13));
                var originV = new Vec2(origin.X, origin.Y);
                var endV = new Vec2(end.X, end.Y);
                var crackDir = (endV - originV).Normalized();
                if (crackDir.Length < 0.01f) crackDir = dir;
                var n = new Vec2(-crackDir.Y, crackDir.X);
                using var fissure = new GraphicsPath();
                var w = (4.0f + (i % 3) * 1.2f) * (1 - t);
                fissure.AddPolygon([
                    (originV + n * w).ToPointF(),
                    (Mix(originV, endV, 0.58f) + n * w * 0.42f + side * MathF.Sin(e.Seed + i) * 5).ToPointF(),
                    endV.ToPointF(),
                    (Mix(originV, endV, 0.44f) - n * w * 0.55f).ToPointF(),
                    (originV - n * w).ToPointF()
                ]);
                g.FillPath(fillCrack, fissure);
                g.DrawLine(crack, origin, end);
                if (i % 2 == 0) g.DrawLine(edge, OffsetPoint(origin, side * 2), OffsetPoint(end, side * 2));
                g.FillEllipse(debris, end.X - 2.5f, end.Y - 2, 5, 4);
            }
        }
        else if (e.Kind == EffectKind.Focus)
        {
            var dir = Vec2.FromAngle(e.Facing);
            var side = new Vec2(-dir.Y, dir.X);
            using var focus = new Pen(Color.FromArgb((int)((1 - t) * 180), e.Color), Math.Max(1, 2.5f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var corePen = new Pen(Color.FromArgb((int)((1 - t) * 170), Color.White), Math.Max(1, 1.2f * (1 - t))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var glow = new SolidBrush(Color.FromArgb((int)((1 - t) * 42), e.Color));
            var target = OffsetPoint(p, new Vec2(0, -36) + dir * e.Radius * 36);
            var pulse = e.Radius * 36 * (0.52f + t * 0.20f);
            g.FillEllipse(glow, target.X - pulse, target.Y - pulse * 0.45f, pulse * 2, pulse * 0.90f);
            for (var i = 0; i < e.Count; i++)
            {
                var u = i / Math.Max(1f, e.Count - 1f) - 0.5f;
                var start = OffsetPoint(p, new Vec2(0, -36) - dir * e.Radius * (18 + (i % 3) * 3) + side * (u * e.Radius * 92));
                g.DrawLine(focus, start, target);
                if (i % 4 == 0) g.DrawLine(corePen, start, target);
            }
            using var center = new SolidBrush(Color.FromArgb((int)((1 - t) * 180), Color.White));
            g.FillEllipse(center, target.X - 5, target.Y - 3, 10, 6);
        }
        else if (e.Kind == EffectKind.Afterimage && e.Palette != null)
        {
            var old = g.Transform;
            g.TranslateTransform(p.X, p.Y);
            var source = e.Palette.Value;
            var pal = source with { Skin = Color.FromArgb(90, source.Skin), Jacket = Color.FromArgb(80, e.Color), Pants = Color.FromArgb(70, source.Pants) };
            var ghost = new Actor("ghost", 0, 0, false) { Palette = pal, Build = e.Build, Facing = e.Facing, Motion = e.Motion };
            DrawRig(g, ghost, 62 * e.Build, 132 * e.Build);
            g.Transform = old;
        }
    }

    private void DrawHazard(Graphics g, Hazard h)
    {
        var p = WorldToScreen(h.Position);
        var armed = h.Age >= h.Delay;
        var prep = h.Delay <= 0 ? 1 : Math.Clamp(h.Age / h.Delay, 0, 1);
        var alpha = h.Boss ? 230 : 190;
        if (h.Kind == HazardKind.Projectile)
        {
            var dir = h.Velocity.Normalized();
            if (dir.Length < 0.01f) dir = Vec2.FromAngle(h.Facing);
            var trail = dir * -34f;
            using var glow = new Pen(Color.FromArgb(armed ? 90 : 45, h.Color), h.Boss ? 12 : 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var core = new Pen(Color.FromArgb(armed ? alpha : 110, h.Color), h.Boss ? 4.5f : 3.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var white = new SolidBrush(Color.FromArgb(armed ? 210 : 110, Color.White));
            g.DrawLine(glow, OffsetPoint(p, trail), p);
            g.DrawLine(core, OffsetPoint(p, trail * 0.45f), p);
            var r = h.Radius * 46f * (h.Boss ? 1.2f : 1f);
            g.FillEllipse(white, p.X - r * 0.30f, p.Y - r * 0.22f, r * 0.60f, r * 0.44f);
            return;
        }

        var scale = Math.Min(ClientSize.Width / 26f, ClientSize.Height / 16f);
        var rx = h.Radius * scale;
        var ry = rx * 0.48f;
        using var fill = new SolidBrush(Color.FromArgb(armed ? (h.Boss ? 82 : 58) : 22, h.Color));
        using var ring = new Pen(Color.FromArgb(armed ? alpha : 120, h.Color), armed ? (h.Boss ? 4.2f : 3.0f) : 2.0f) { DashStyle = armed ? DashStyle.Solid : DashStyle.Dash };
        using var innerRing = new Pen(Color.FromArgb(armed ? 180 : 80, Color.White), 1.4f);
        var pulse = armed ? 1.0f + MathF.Sin(elapsed * 18f) * 0.08f : 0.55f + prep * 0.45f;
        g.FillEllipse(fill, p.X - rx * pulse, p.Y - ry * pulse, rx * 2 * pulse, ry * 2 * pulse);
        g.DrawEllipse(ring, p.X - rx * pulse, p.Y - ry * pulse, rx * 2 * pulse, ry * 2 * pulse);
        g.DrawEllipse(innerRing, p.X - rx * 0.42f, p.Y - ry * 0.42f, rx * 0.84f, ry * 0.84f);
        if (!armed)
        {
            using var wedge = new Pen(Color.FromArgb(190, Color.White), 2.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var angle = -90f;
            g.DrawArc(wedge, p.X - rx * 0.72f, p.Y - ry * 0.72f, rx * 1.44f, ry * 1.44f, angle, 360f * prep);
        }
    }

    private void DrawDamageText(Graphics g, DamageText text)
    {
        var p = WorldToScreen(text.Position);
        var t = text.Age / text.Life;
        using var font = new Font("Segoe UI", 13, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb((int)((1 - t) * 255), Color.White));
        g.DrawString(text.Text, font, brush, p.X - 8, p.Y - 38 - t * 24);
    }

    private void DrawCombatDebug(Graphics g)
    {
        using var hurtPen = new Pen(Color.FromArgb(210, 110, 235, 255), 1.5f);
        using var activePen = new Pen(Color.FromArgb(240, 255, 76, 76), 2.2f);
        using var inactivePen = new Pen(Color.FromArgb(170, 255, 210, 76), 1.5f) { DashStyle = DashStyle.Dash };
        using var activeFill = new SolidBrush(Color.FromArgb(42, 255, 64, 64));
        using var inactiveFill = new SolidBrush(Color.FromArgb(22, 255, 210, 76));

        DrawActorHurtbox(g, player, hurtPen);
        foreach (var enemy in enemies.Where(e => e.Alive)) DrawActorHurtbox(g, enemy, hurtPen);
        DrawActionHitbox(g, player, player.Action, activePen, inactivePen, activeFill, inactiveFill);
        foreach (var enemy in enemies.Where(e => e.Alive)) DrawActionHitbox(g, enemy, enemy.Action, activePen, inactivePen, activeFill, inactiveFill);
    }

    private void DrawActorHurtbox(Graphics g, Actor actor, Pen pen)
    {
        var p = WorldToScreen(actor.Position);
        var scale = WorldScale();
        var rx = actor.Radius * scale;
        var ry = actor.Radius * scale * 0.58f;
        g.DrawEllipse(pen, p.X - rx, p.Y - ry, rx * 2, ry * 2);
    }

    private void DrawActionHitbox(Graphics g, Actor actor, ActionState? action, Pen activePen, Pen inactivePen, Brush activeFill, Brush inactiveFill)
    {
        if (action == null || action.Kind == "dash") return;
        var active = action.ActiveNow;
        var pen = active ? activePen : inactivePen;
        var fill = active ? activeFill : inactiveFill;
        var hitbox = action.Hitbox;
        var origin = actor.Position + Vec2.FromAngle(action.Angle) * hitbox.Offset;
        if (hitbox.Shape == HitboxShape.Circle || hitbox.Arc >= MathF.PI * 1.95f)
        {
            var p = WorldToScreen(origin);
            var scale = WorldScale();
            var radius = hitbox.Shape == HitboxShape.Circle ? hitbox.Radius : hitbox.Range;
            var rx = radius * scale;
            var ry = radius * scale * 0.58f;
            g.FillEllipse(fill, p.X - rx, p.Y - ry, rx * 2, ry * 2);
            g.DrawEllipse(pen, p.X - rx, p.Y - ry, rx * 2, ry * 2);
            return;
        }

        var points = new List<PointF> { WorldToScreen(origin) };
        const int steps = 14;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var angle = action.Angle - hitbox.Arc * 0.5f + hitbox.Arc * t;
            points.Add(WorldToScreen(origin + Vec2.FromAngle(angle) * hitbox.Range));
        }
        var polygon = points.ToArray();
        g.FillPolygon(fill, polygon);
        g.DrawPolygon(pen, polygon);
    }

    private void DrawHud(Graphics g)
    {
        using var text = new Font("Segoe UI", 10, FontStyle.Bold);
        using var big = new Font("Segoe UI", 13, FontStyle.Bold);
        FillRound(g, new RectangleF(16, 16, 286, 74), Color.FromArgb(196, 8, 13, 17), 7);
        g.DrawString(stages[selectedStage].Name, text, Brushes.Gainsboro, 30, 30);
        var objective = bossSpawned ? enemies.Any(e => e.Alive && e.IsBoss) ? "Defeat the boss" : "Boss defeated" : $"Kills {kills} / {stages[selectedStage].KillGoal}";
        g.DrawString(mode is GameMode.Playing or GameMode.Reward ? objective : mode.ToString(), big, Brushes.White, 30, 51);
        using var micro = new Font("Segoe UI", 8, FontStyle.Bold);
        using var dim = new SolidBrush(Color.FromArgb(178, 224, 230, 220));
        g.DrawString($"{enemies.Count(e => e.Alive)} enemies  Combo {combo}", micro, dim, 30, 74);
        if (enemies.FirstOrDefault(e => e.Alive && e.IsBoss) is { } boss)
        {
            FillRound(g, new RectangleF(ClientSize.Width * 0.5f - 250, 18, 500, 42), Color.FromArgb(214, 12, 10, 16), 8);
            g.DrawString(boss.Name, text, Brushes.White, ClientSize.Width * 0.5f - 232, 25);
            DrawBar(g, ClientSize.Width * 0.5f - 120, 29, 340, 13, boss.Hp / boss.MaxHp, Color.FromArgb(255, 205, 90), $"{Math.Max(0, (int)boss.Hp)} / {(int)boss.MaxHp}");
        }
        var y = ClientSize.Height - 72;
        FillRound(g, new RectangleF(18, y - 8, 514, 56), Color.FromArgb(202, 8, 13, 17), 7);
        g.DrawString(player.Name, big, Brushes.White, 32, y + 10);
        DrawBar(g, 142, y, 260, 11, player.Hp / player.MaxHp, Color.FromArgb(255, 86, 100), $"{Math.Max(0, (int)player.Hp)}");
        DrawBar(g, 142, y + 18, 260, 11, aura / Math.Max(1, maxAura), Color.FromArgb(110, 225, 255), $"{(int)aura}");
        DrawSkillStrip(g, 418, y - 1);
        if (debugCombat)
        {
            var debug = player.Action != null ? $"{player.Action.Kind} {player.Action.PhaseLabel} {player.Action.Elapsed:0.00}/{player.Action.Duration:0.00} {player.Action.TimelineSource} ev{player.Action.Events.Count}" : "HITBOX DEBUG";
            FillRound(g, new RectangleF(ClientSize.Width - 440, 18, 414, 28), Color.FromArgb(186, 8, 12, 16), 6);
            g.DrawString(debug, micro, Brushes.Gainsboro, ClientSize.Width - 428, 25);
        }
    }

    private void DrawSkillStrip(Graphics g, float x, float y)
    {
        using var font = new Font("Segoe UI", 8, FontStyle.Bold);
        var items = new[]
        {
            ("J", 0f, 0f, 0f),
            ("K", heavyCooldown, CooldownDuration("heavy"), ActionCost("heavy")),
            ("Sp", dashCooldown, CooldownDuration("dash"), 0f),
            ("Q", qCooldown, CooldownDuration("skill-q"), ActionCost("skill-q")),
            ("E", eCooldown, CooldownDuration("skill-e"), ActionCost("skill-e")),
            ("R", rCooldown, CooldownDuration("skill-r"), ActionCost("skill-r")),
            ("F", ultimateCooldown, CooldownDuration("ultimate"), ActionCost("ultimate"))
        };
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var rect = new RectangleF(x + i * 39f, y, 34f, 34f);
            var ready = item.Item2 <= 0 && aura >= item.Item4;
            FillRound(g, rect, ready ? Color.FromArgb(76, 38, 54, 58) : Color.FromArgb(112, 18, 20, 24), 6);
            StrokeRound(g, rect, ready ? Color.FromArgb(110, 225, 255) : Color.FromArgb(90, 92, 104, 110), 5, 1.1f);
            if (item.Item2 > 0 && item.Item3 > 0)
            {
                var ratio = Math.Clamp(item.Item2 / item.Item3, 0, 1);
                using var shade = new SolidBrush(Color.FromArgb(148, 0, 0, 0));
                g.FillRectangle(shade, rect.X, rect.Y, rect.Width, rect.Height * ratio);
            }
            if (aura < item.Item4)
            {
                using var shade = new SolidBrush(Color.FromArgb(95, 45, 70, 90));
                g.FillRectangle(shade, rect);
            }
            var labelSize = g.MeasureString(item.Item1, font);
            g.DrawString(item.Item1, font, Brushes.White, rect.X + (rect.Width - labelSize.Width) * 0.5f, rect.Y + 10);
        }
    }

    private static void DrawBar(Graphics g, float x, float y, float w, float h, float value, Color color, string label)
    {
        using var bg = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
        using var fill = new SolidBrush(color);
        g.FillRectangle(bg, x, y, w, h);
        g.FillRectangle(fill, x, y, w * Math.Clamp(value, 0, 1), h);
        using var font = new Font("Segoe UI", 8, FontStyle.Bold);
        g.DrawString(label, font, Brushes.White, x + w - 44, y - 2);
    }

    private void DrawOverlay(Graphics g, string title, string subtitle)
    {
        using var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        g.FillRectangle(shade, ClientRectangle);
        using var font = new Font("Segoe UI", 36, FontStyle.Bold);
        using var sub = new Font("Segoe UI", 13, FontStyle.Bold);
        var size = g.MeasureString(title, font);
        g.DrawString(title, font, Brushes.White, (ClientSize.Width - size.Width) / 2, ClientSize.Height * 0.38f);
        var subSize = g.MeasureString(subtitle, sub);
        g.DrawString(subtitle, sub, Brushes.Gainsboro, (ClientSize.Width - subSize.Width) / 2, ClientSize.Height * 0.49f);
    }

    private void DrawUpgradeOverlay(Graphics g)
    {
        using var shade = new SolidBrush(Color.FromArgb(186, 0, 0, 0));
        g.FillRectangle(shade, ClientRectangle);
        using var title = new Font("Segoe UI", 30, FontStyle.Bold);
        using var sub = new Font("Segoe UI", 12, FontStyle.Bold);
        using var cardTitle = new Font("Segoe UI", 15, FontStyle.Bold);
        using var body = new Font("Segoe UI", 10, FontStyle.Bold);
        var titleText = $"Nen Breakthrough {upgradesTaken + 1}/3";
        var titleSize = g.MeasureString(titleText, title);
        g.DrawString(titleText, title, Brushes.White, (ClientSize.Width - titleSize.Width) * 0.5f, ClientSize.Height * 0.23f);
        var hint = "Choose a reward: click a card or press 1 / 2 / 3";
        var hintSize = g.MeasureString(hint, sub);
        g.DrawString(hint, sub, Brushes.Gainsboro, (ClientSize.Width - hintSize.Width) * 0.5f, ClientSize.Height * 0.32f);

        for (var i = 0; i < upgradeChoices.Count; i++)
        {
            var option = upgradeChoices[i];
            var rect = UpgradeCardRect(i);
            FillRound(g, rect, Color.FromArgb(232, 17, 24, 28), 10);
            StrokeRound(g, rect, option.Color, 10, 2.2f);
            using var glow = new SolidBrush(Color.FromArgb(48, option.Color));
            g.FillEllipse(glow, rect.X + rect.Width - 114, rect.Y - 38, 170, 170);
            using var number = new Font("Segoe UI", 12, FontStyle.Bold);
            using var numBrush = new SolidBrush(option.Color);
            g.DrawString($"{i + 1}", number, numBrush, rect.X + 18, rect.Y + 16);
            g.DrawString(option.Name, cardTitle, Brushes.White, rect.X + 46, rect.Y + 15);
            g.DrawString(option.Description, body, Brushes.Gainsboro, new RectangleF(rect.X + 22, rect.Y + 62, rect.Width - 44, 70));
            using var small = new Font("Segoe UI", 9, FontStyle.Bold);
            using var muted = new SolidBrush(Color.FromArgb(180, 225, 230, 220));
            g.DrawString($"Current: DMG x{1 + damageBonus:0.00} | Aura {(int)aura}/{(int)maxAura}", small, muted, rect.X + 22, rect.Bottom - 36);
        }
    }

    private PointF WorldToScreen(Vec2 w)
    {
        var scale = WorldScale();
        return new PointF(ClientSize.Width * 0.5f + w.X * scale, ClientSize.Height * 0.50f + w.Y * scale * 0.58f);
    }

    private float WorldScale() => Math.Min(ClientSize.Width / 26f, ClientSize.Height / 16f) * CameraZoom;

    private static void DrawShadow(Graphics g, PointF p, float width)
    {
        using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        g.FillEllipse(shadow, p.X - width * 0.34f, p.Y - width * 0.12f, width * 0.68f, width * 0.24f);
    }

    private static void FillRound(Graphics g, RectangleF rect, Color color, float radius)
    {
        using var brush = new SolidBrush(color);
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private static void StrokeRound(Graphics g, RectangleF rect, Color color, float radius, float width)
    {
        using var pen = new Pen(color, width);
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    private static void DrawRotatedRect(Graphics g, PointF center, float w, float h, float degrees, Pen pen)
    {
        var old = g.Transform;
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(degrees);
        g.DrawRectangle(pen, -w / 2, -h / 2, w, h);
        g.DrawRectangle(pen, -w / 4, -h / 4, w / 2, h / 2);
        g.Transform = old;
    }

    private static void DrawRotatedEllipseStroke(Graphics g, PointF center, float w, float h, float degrees, Pen outer, Pen inner)
    {
        var old = g.Transform;
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(degrees);
        g.DrawEllipse(outer, -w / 2, -h / 2, w, h);
        g.DrawEllipse(inner, -w * 0.32f, -h * 0.32f, w * 0.64f, h * 0.64f);
        g.Transform = old;
    }

    private static PointF OffsetPoint(PointF point, Vec2 offset) => new(point.X + offset.X, point.Y + offset.Y);

    private static Vec2 Mix(Vec2 a, Vec2 b, float t) => a + (b - a) * t;
    private static float AngleDelta(float a, float b) => MathF.Atan2(MathF.Sin(b - a), MathF.Cos(b - a));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearSpriteCache();
            ClearActionSheets();
            ClearIllustrations();
            timer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal enum GameMode { Menu, Playing, Reward, Clear, GameOver }
internal enum StageTheme { Card, Auction, Forest }
internal enum EffectKind { Ring, Burst, Slash, Afterimage, Impact, Shockwave, Lightning, Chain, GroundCrack, Focus }
internal enum HazardKind { Projectile, Mine }
internal readonly record struct SpriteKey(string ActorId, int PaletteStamp, string State, int Direction, int Frame, int Width, int Height);

internal sealed record CharacterSpec(string Name, Color Aura, Color Jacket, Color Hair, float Build, float Speed);
internal sealed record StageSpec(string Name, StageTheme Theme, Color EnemyAura, int KillGoal, int MaxEnemies, float Difficulty);
internal sealed record UpgradeOption(string Id, string Name, string Description, Color Color);

internal sealed class Actor(string name, float x, float y, bool isPlayer)
{
    public string Name = name;
    public Vec2 Position = new(x, y);
    public Vec2 Velocity;
    public bool IsPlayer = isPlayer;
    public bool Alive = true;
    public bool IsBoss;
    public float MaxHp = 100;
    public float Hp = 100;
    public float Speed = 4;
    public float Radius = 0.38f;
    public float Facing = -MathF.PI / 2;
    public float Motion;
    public float HitFlash;
    public float HitAngle;
    public float Stagger;
    public float AttackCooldown;
    public float SpecialCooldown;
    public float Invulnerable;
    public float Build = 1;
    public ActionState? Action;
    public Palette Palette = Palette.Default;
}

internal sealed class Hazard(HazardKind kind, Vec2 position, Vec2 velocity, float radius, float damage, float life, Color color)
{
    public HazardKind Kind = kind;
    public Vec2 Position = position;
    public Vec2 Velocity = velocity;
    public float Radius = radius;
    public float Damage = damage;
    public float Life = life;
    public Color Color = color;
    public float Age;
    public float Delay;
    public float Facing;
    public bool Boss;
    public bool Hit;
}

internal sealed class Effect(EffectKind kind, Vec2 position, Color color, float life)
{
    public EffectKind Kind = kind;
    public Vec2 Position = position;
    public Color Color = color;
    public float Life = life;
    public float Age;
    public float Radius = 1;
    public float Arc = 1;
    public float Facing;
    public int Count = 8;
    public float Width = 1;
    public float Seed;
    public float Build = 1;
    public float Motion;
    public Palette? Palette;

    public static Effect Ring(Vec2 p, Color c, float radius, float life) => new(EffectKind.Ring, p, c, life) { Radius = radius, Seed = p.X * 11.3f + p.Y * 29.7f };
    public static Effect Burst(Vec2 p, Color c, float radius, float life) => new(EffectKind.Burst, p, c, life) { Radius = radius, Seed = p.X * 37.1f + p.Y * 15.9f };
    public static Effect Slash(Vec2 p, float angle, Color c, float radius, float arc, float life) => new(EffectKind.Slash, p, c, life) { Facing = angle, Radius = radius, Arc = arc };
    public static Effect Impact(Vec2 p, float angle, Color c, float radius, float life) => new(EffectKind.Impact, p, c, life) { Facing = angle, Radius = radius, Count = 12, Seed = p.X * 13.7f + p.Y * 41.3f };
    public static Effect Shockwave(Vec2 p, Color c, float radius, float life) => new(EffectKind.Shockwave, p, c, life) { Radius = radius, Width = 1.2f, Seed = p.X * 9.7f + p.Y * 21.4f };
    public static Effect Lightning(Vec2 p, float angle, Color c, float radius, float life, int count) => new(EffectKind.Lightning, p, c, life) { Facing = angle, Radius = radius, Count = count, Seed = p.X * 17.1f + p.Y * 31.9f };
    public static Effect Chain(Vec2 p, float angle, Color c, float radius, float life) => new(EffectKind.Chain, p, c, life) { Facing = angle, Radius = radius, Count = 12, Seed = p.X * 12.7f + p.Y * 18.4f };
    public static Effect GroundCrack(Vec2 p, float angle, Color c, float radius, float life) => new(EffectKind.GroundCrack, p, c, life) { Facing = angle, Radius = radius, Count = 10, Seed = p.X * 19.7f + p.Y * 23.4f };
    public static Effect Focus(Vec2 p, float angle, Color c, float radius, float life) => new(EffectKind.Focus, p, c, life) { Facing = angle, Radius = radius, Count = 18, Seed = p.X * 22.7f + p.Y * 10.4f };
}

internal sealed class DamageText(Vec2 position, string text, float life)
{
    public Vec2 Position = position;
    public string Text = text;
    public float Life = life;
    public float Age;
}

internal readonly record struct Palette(Color Skin, Color Hair, Color Jacket, Color Shirt, Color Pants, Color Boots, Color Accent, Color Outline, bool LongHair = false)
{
    public static readonly Palette Default = new(Color.FromArgb(224, 176, 136), Color.FromArgb(40, 38, 36), Color.FromArgb(60, 74, 88), Color.FromArgb(230, 215, 170), Color.FromArgb(32, 42, 54), Color.FromArgb(18, 22, 26), Color.FromArgb(242, 207, 107), Color.FromArgb(8, 12, 14));

    public static Palette ForPlayer(CharacterSpec c)
    {
        if (c.Name == "Awakened Gon") return new Palette(Color.FromArgb(215, 160, 108), c.Hair, c.Jacket, Color.FromArgb(242, 218, 126), Color.FromArgb(35, 48, 30), Color.FromArgb(20, 16, 12), c.Aura, Color.FromArgb(4, 7, 5), true);
        if (c.Name == "Killua") return new Palette(Color.FromArgb(236, 190, 160), c.Hair, c.Jacket, Color.FromArgb(98, 200, 236), Color.FromArgb(70, 88, 115), Color.FromArgb(30, 42, 62), c.Aura, Color.FromArgb(16, 28, 38));
        if (c.Name == "Kurapika") return new Palette(Color.FromArgb(240, 194, 140), c.Hair, c.Jacket, Color.FromArgb(240, 214, 110), Color.FromArgb(230, 218, 194), Color.FromArgb(78, 62, 48), c.Aura, Color.FromArgb(20, 24, 40));
        return new Palette(Color.FromArgb(242, 190, 138), c.Hair, c.Jacket, Color.FromArgb(245, 232, 185), Color.FromArgb(38, 64, 52), Color.FromArgb(52, 40, 32), c.Aura, Color.FromArgb(10, 20, 16));
    }

    public static Palette ForEnemy(Color aura, bool elite)
    {
        return elite
            ? new Palette(Color.FromArgb(205, 160, 110), Color.FromArgb(28, 34, 24), Color.FromArgb(86, 112, 48), Color.FromArgb(230, 244, 100), Color.FromArgb(24, 34, 24), Color.FromArgb(12, 18, 12), aura, Color.FromArgb(8, 12, 8))
            : new Palette(Color.FromArgb(214, 178, 140), Color.FromArgb(40, 52, 46), Color.FromArgb(58, 100, 112), Color.FromArgb(220, 240, 230), Color.FromArgb(32, 44, 58), Color.FromArgb(16, 22, 28), aura, Color.FromArgb(10, 18, 20));
    }
}
