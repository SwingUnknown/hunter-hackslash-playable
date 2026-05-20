namespace HunterHackslashNative;

internal enum HitboxShape
{
    Cone,
    Circle
}

internal sealed record Hitbox(HitboxShape Shape, float Range, float Arc, float Radius, float Offset)
{
    public static Hitbox Cone(float range, float arc, float offset = 0f) => new(HitboxShape.Cone, range, arc, 0f, offset);
    public static Hitbox Circle(float radius, float offset = 0f) => new(HitboxShape.Circle, radius, MathF.PI * 2f, radius, offset);

    public Hitbox WithRangeBonus(float bonus)
    {
        if (bonus <= 0) return this;
        return Shape == HitboxShape.Circle
            ? this with { Range = Range + bonus, Radius = Radius + bonus }
            : this with { Range = Range + bonus };
    }

    public bool Contains(Vec2 origin, float angle, Vec2 target, float targetRadius)
    {
        var center = origin + Vec2.FromAngle(angle) * Offset;
        var to = target - center;
        var dist = to.Length;
        if (Shape == HitboxShape.Circle)
        {
            return dist <= Radius + targetRadius;
        }

        if (dist > Range + targetRadius) return false;
        if (Arc >= MathF.PI * 1.95f) return true;
        var delta = MathF.Abs(MathF.Atan2(MathF.Sin(MathF.Atan2(to.Y, to.X) - angle), MathF.Cos(MathF.Atan2(to.Y, to.X) - angle)));
        return delta <= Arc * 0.5f;
    }
}
