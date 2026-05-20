namespace HunterHackslashNative;

internal sealed record Hurtbox(float Radius, float HeightScale = 0.58f)
{
    public static Hurtbox FromActor(Actor actor) => new(actor.Radius);
}
