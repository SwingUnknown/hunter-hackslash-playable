namespace HunterHackslashNative;

internal sealed record AnimationFrame(
    int Index,
    float Duration,
    Vec2 RootOffset,
    Vec2 FrontFootAnchor,
    Vec2 BackFootAnchor);
