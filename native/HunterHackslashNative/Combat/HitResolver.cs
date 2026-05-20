namespace HunterHackslashNative;

internal static class HitResolver
{
    public static bool CanHit(ActionState action, Vec2 attackerPosition, Vec2 targetPosition, float targetRadius)
    {
        return action.Hitbox.Contains(attackerPosition, action.Angle, targetPosition, targetRadius);
    }
}
