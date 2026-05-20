namespace HunterHackslashNative;

internal sealed class AnimationStateMachine
{
    public string State { get; private set; } = "idle";
    public float Time { get; private set; }

    public void Play(string state, bool restart = false)
    {
        if (!restart && State == state) return;
        State = state;
        Time = 0;
    }

    public void Advance(float dt)
    {
        Time += Math.Max(0, dt);
    }
}
