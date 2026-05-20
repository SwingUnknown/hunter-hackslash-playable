using System.Drawing;

namespace HunterHackslashNative;

internal static class DrawingAdapters
{
    public static PointF ToPointF(this Vec2 value) => new(value.X, value.Y);
}
