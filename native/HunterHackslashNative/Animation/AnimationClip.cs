namespace HunterHackslashNative;

internal sealed record AnimationClip(
    string Id,
    string SheetState,
    IReadOnlyList<AnimationFrame> Frames,
    bool Loop);
