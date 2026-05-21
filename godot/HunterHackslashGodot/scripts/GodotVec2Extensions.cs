using Godot;
using HunterHackslashNative;

internal static class GodotVec2Extensions
{
    public static Vector2 ToGodot(this Vec2 value) => new(value.X, value.Y);
    public static Vec2 ToVec2(this Vector2 value) => new(value.X, value.Y);
}
