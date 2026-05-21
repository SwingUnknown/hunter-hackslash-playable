namespace HunterHackslashNative;

internal enum AttackFrameEventKind
{
    Effect,
    Sound
}

internal sealed record AttackFrameEvent(
    float Time,
    string Name,
    AttackFrameEventKind Kind,
    string? Condition);
