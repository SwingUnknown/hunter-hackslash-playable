namespace HunterHackslashNative;

internal sealed record Hurtbox(float Radius, float HeightScale = 0.58f)
{
    public bool Contains(Vec2 ownerPosition, Vec2 point)
    {
        return (point - ownerPosition).Length <= Radius;
    }
}
