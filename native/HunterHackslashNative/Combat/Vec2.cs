namespace HunterHackslashNative;

internal readonly record struct Vec2(float X, float Y)
{
    public float Length => MathF.Sqrt(X * X + Y * Y);

    public Vec2 Normalized()
    {
        var len = Length;
        return len > 0.0001f ? this / len : new Vec2();
    }

    public static Vec2 FromAngle(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    public static float AngleOf(Vec2 value) => MathF.Atan2(value.Y, value.X);
    public static float AngleDelta(float from, float to) => MathF.Atan2(MathF.Sin(to - from), MathF.Cos(to - from));

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, float b) => new(a.X * b, a.Y * b);
    public static Vec2 operator *(float a, Vec2 b) => b * a;
    public static Vec2 operator /(Vec2 a, float b) => new(a.X / b, a.Y / b);
}
