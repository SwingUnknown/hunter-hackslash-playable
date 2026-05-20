namespace HunterHackslashNative;

internal static class HitResolver
{
    public static bool CanHit(ActionState action, Vec2 attackerPosition, Actor target)
    {
        return action.Hitbox.Contains(attackerPosition, action.Angle, target.Position, target.Radius);
    }
}
