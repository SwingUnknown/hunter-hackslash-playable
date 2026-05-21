namespace HunterHackslashNative;

internal readonly record struct KinematicSettings(
    float Acceleration,
    float Deceleration,
    float TurnAcceleration,
    float StopSpeed = 0.02f);

internal static class KinematicMotor
{
    public static Vec2 StepVelocity(Vec2 currentVelocity, Vec2 inputDirection, float maxSpeed, float dt, KinematicSettings settings)
    {
        dt = Math.Max(0, dt);
        inputDirection = inputDirection.Normalized();
        if (inputDirection.Length > 0.001f && maxSpeed > 0.001f)
        {
            var desired = inputDirection * maxSpeed;
            var turning = currentVelocity.Length > 0.001f && Vec2.Dot(currentVelocity.Normalized(), inputDirection) < 0.35f;
            var acceleration = turning ? settings.TurnAcceleration : settings.Acceleration;
            return MoveToward(currentVelocity, desired, acceleration * dt);
        }

        return MoveToward(currentVelocity, new Vec2(), settings.Deceleration * dt, settings.StopSpeed);
    }

    private static Vec2 MoveToward(Vec2 current, Vec2 target, float maxDelta, float stopSpeed = 0f)
    {
        var delta = target - current;
        var distance = delta.Length;
        if (distance <= Math.Max(maxDelta, stopSpeed)) return target;
        return current + delta / distance * maxDelta;
    }
}
