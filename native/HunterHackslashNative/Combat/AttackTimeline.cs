using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HunterHackslashNative;

internal sealed record ActionProfile(
    float Duration,
    float Startup,
    float ActiveStart,
    float ActiveEnd,
    float Recovery,
    float CancelAt,
    float Damage,
    float Range,
    float Arc,
    float HitStop,
    float Shake,
    float Knockback,
    float Stagger,
    float HitFlash,
    float Lunge)
{
    public string? Animation { get; init; }
    public Hitbox Hitbox { get; init; } = Hitbox.Cone(Range, Arc);
    public IReadOnlyList<AttackFrameEvent> Events { get; init; } = [];
    public string Source { get; init; } = "code";

    public ActionState CreateState(string kind, float angle, float damageScale, float rangeBonus, string defaultAnimation)
    {
        return new ActionState(kind, angle, Duration, ActiveStart, Damage * damageScale, Range + rangeBonus, Arc)
        {
            Animation = Animation ?? defaultAnimation,
            Startup = Startup,
            ActiveEnd = ActiveEnd,
            Recovery = Recovery,
            CancelAt = CancelAt,
            HitStop = HitStop,
            Shake = Shake,
            Knockback = Knockback,
            Stagger = Stagger,
            HitFlash = HitFlash,
            Lunge = Lunge,
            Hitbox = Hitbox.WithRangeBonus(rangeBonus),
            Events = Events,
            TimelineSource = Source
        };
    }
}

internal sealed class ActionState(string kind, float angle, float duration, float activeAt, float damage, float range, float arc)
{
    public string Kind = kind;
    public string Animation = kind;
    public float Angle = angle;
    public float Duration = duration;
    public float Startup = activeAt;
    public float ActiveAt = activeAt;
    public float ActiveEnd = Math.Min(duration, activeAt + 0.05f);
    public float Recovery = Math.Max(0, duration - activeAt);
    public float CancelAt = duration;
    public float Damage = damage;
    public float Range = range;
    public float Arc = arc;
    public float HitStop = 0.024f;
    public float Shake = 0.22f;
    public float Knockback = 3.0f;
    public float Stagger = 0.16f;
    public float HitFlash = 0.16f;
    public float Lunge;
    public float Elapsed;
    public bool HitDone;
    public Hitbox Hitbox = Hitbox.Cone(range, arc);
    public IReadOnlyList<AttackFrameEvent> Events = [];
    public string TimelineSource = "code";
    public bool ActiveNow => Elapsed >= ActiveAt && Elapsed <= ActiveEnd;
    public bool CanCancel => Elapsed >= CancelAt || Elapsed >= Duration;
    public string PhaseLabel => Elapsed < ActiveAt ? "startup" : ActiveNow ? "active" : CanCancel ? "cancel" : "recovery";
}

internal sealed class AttackTimelineStore
{
    private readonly Dictionary<string, ActionProfile?> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string? LastError { get; private set; }

    public ActionProfile ResolvePlayerProfile(string characterName, string kind, int lightStep, ActionProfile fallback)
    {
        LastError = null;
        var slug = Slug(characterName);
        var file = PlayerAttackFileName(kind, lightStep);
        if (file == null) return fallback with { Source = "code:fallback" };

        var key = $"{slug}/{file}";
        if (cache.TryGetValue(key, out var cached))
        {
            return cached ?? fallback with { Source = "code:fallback" };
        }

        var relative = Path.Combine("data", "attacks", slug, file);
        var path = ResolveDataPath(relative);
        if (path == null)
        {
            cache[key] = null;
            return fallback with { Source = "code:fallback" };
        }

        var profile = TryLoad(path, fallback);
        cache[key] = profile;
        return profile ?? fallback with { Source = "code:fallback" };
    }

    private ActionProfile? TryLoad(string path, ActionProfile fallback)
    {
        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<AttackTimelineDto>(json, JsonOptions);
            if (dto == null)
            {
                LastError = $"Attack data is empty: {path}";
                return null;
            }

            var duration = Required(dto.Duration, fallback.Duration, "duration", path);
            var activeStart = Required(dto.ActiveStart, fallback.ActiveStart, "activeStart", path);
            var activeEnd = Required(dto.ActiveEnd, fallback.ActiveEnd, "activeEnd", path);
            if (duration <= 0) throw new InvalidDataException("duration must be greater than 0");
            if (activeStart < 0 || activeEnd < activeStart || activeEnd > duration)
            {
                throw new InvalidDataException("activeStart/activeEnd must be inside duration");
            }

            var arc = dto.ArcRadians ?? DegreesToRadians(dto.ArcDegrees) ?? fallback.Arc;
            var profile = new ActionProfile(
                duration,
                dto.Startup ?? activeStart,
                activeStart,
                activeEnd,
                dto.Recovery ?? Math.Max(0, duration - activeEnd),
                dto.CancelAt ?? fallback.CancelAt,
                dto.Damage ?? fallback.Damage,
                dto.Range ?? fallback.Range,
                arc,
                dto.HitStop ?? fallback.HitStop,
                dto.Shake ?? fallback.Shake,
                dto.Knockback ?? fallback.Knockback,
                dto.Stagger ?? fallback.Stagger,
                dto.HitFlash ?? fallback.HitFlash,
                dto.Lunge ?? fallback.Lunge)
            {
                Animation = dto.Animation ?? fallback.Animation,
                Hitbox = BuildHitbox(dto.Hitbox, dto.Range ?? fallback.Range, arc),
                Events = BuildEvents(dto),
                Source = $"json:{Path.GetFileName(path)}"
            };
            return profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LastError = $"Attack data fallback: {Path.GetFileName(path)} - {ex.Message}";
            Debug.WriteLine(LastError);
            return null;
        }
    }

    private static float Required(float? value, float fallback, string name, string path)
    {
        if (value.HasValue) return value.Value;
        throw new InvalidDataException($"{name} is required in {Path.GetFileName(path)}; fallback would be {fallback:0.###}");
    }

    private static Hitbox BuildHitbox(HitboxDto? dto, float fallbackRange, float fallbackArc)
    {
        if (dto == null) return Hitbox.Cone(fallbackRange, fallbackArc);
        var shape = dto.Shape?.Trim().ToLowerInvariant();
        if (shape == "circle")
        {
            return Hitbox.Circle(dto.Radius ?? dto.Range ?? fallbackRange, dto.Offset ?? 0f);
        }

        var arc = dto.ArcRadians ?? DegreesToRadians(dto.ArcDegrees) ?? fallbackArc;
        return Hitbox.Cone(dto.Range ?? fallbackRange, arc, dto.Offset ?? 0f);
    }

    private static IReadOnlyList<AttackFrameEvent> BuildEvents(AttackTimelineDto dto)
    {
        var result = new List<AttackFrameEvent>();
        AddEvents(result, dto.EffectEvents, AttackFrameEventKind.Effect);
        AddEvents(result, dto.SoundEvents, AttackFrameEventKind.Sound);
        return result;
    }

    private static void AddEvents(List<AttackFrameEvent> target, IEnumerable<AttackEventDto>? source, AttackFrameEventKind kind)
    {
        if (source == null) return;
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            target.Add(new AttackFrameEvent(Math.Max(0, item.Time), item.Name, kind, item.Condition));
        }
    }

    private static string? PlayerAttackFileName(string kind, int lightStep)
    {
        if (kind == "light" && lightStep == 1) return "light1.json";
        return null;
    }

    private static string? ResolveDataPath(string relative)
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

    private static float? DegreesToRadians(float? degrees) => degrees.HasValue ? degrees.Value * MathF.PI / 180f : null;

    private static string Slug(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_') switch
        {
            "gon" => "gon",
            _ => value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_')
        };
    }

    private sealed class AttackTimelineDto
    {
        public string? Animation { get; set; }
        public float? Duration { get; set; }
        public float? Startup { get; set; }
        public float? ActiveStart { get; set; }
        public float? ActiveEnd { get; set; }
        public float? Recovery { get; set; }
        public float? CancelAt { get; set; }
        public float? Damage { get; set; }
        public float? Range { get; set; }
        public float? ArcRadians { get; set; }
        public float? ArcDegrees { get; set; }
        public float? HitStop { get; set; }
        public float? Shake { get; set; }
        public float? Knockback { get; set; }
        public float? Stagger { get; set; }
        public float? HitFlash { get; set; }
        public float? Lunge { get; set; }
        public HitboxDto? Hitbox { get; set; }
        public List<AttackEventDto>? EffectEvents { get; set; }
        public List<AttackEventDto>? SoundEvents { get; set; }
    }

    private sealed class HitboxDto
    {
        public string? Shape { get; set; }
        public float? Range { get; set; }
        public float? ArcRadians { get; set; }
        public float? ArcDegrees { get; set; }
        public float? Radius { get; set; }
        public float? Offset { get; set; }
    }

    private sealed class AttackEventDto
    {
        public float Time { get; set; }
        public string Name { get; set; } = "";
        public string? Condition { get; set; }
    }
}
