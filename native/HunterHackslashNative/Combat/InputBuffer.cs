namespace HunterHackslashNative;

internal sealed class InputBuffer(float lifetime = 0.20f)
{
    private readonly float lifetime = Math.Max(0.001f, lifetime);
    private string? bufferedAction;
    private float timeRemaining;

    public string? BufferedAction => timeRemaining > 0 ? bufferedAction : null;
    public float TimeRemaining => timeRemaining;
    public bool HasInput => BufferedAction != null;

    public void Push(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        bufferedAction = action;
        timeRemaining = lifetime;
    }

    public void RegisterInput(string action) => Push(action);

    public void Tick(float dt)
    {
        if (timeRemaining <= 0) return;
        timeRemaining = Math.Max(0, timeRemaining - Math.Max(0, dt));
        if (timeRemaining <= 0) bufferedAction = null;
    }

    public bool TryConsume(bool windowOpen, out string action)
    {
        if (windowOpen && BufferedAction is { } buffered)
        {
            action = buffered;
            Clear();
            return true;
        }

        action = "";
        return false;
    }

    public void Clear()
    {
        bufferedAction = null;
        timeRemaining = 0;
    }
}
