namespace Shooter.Combat
{
    /// <summary>
    /// Optional companion to <see cref="IDamageable"/>: an object can report a
    /// multiplier it will apply to incoming damage. Weapons read it so the damage
    /// number they float matches the health the target actually loses (the boss's
    /// exposed core doubles damage for a window).
    /// </summary>
    public interface IDamageScale
    {
        float IncomingDamageScale { get; }
    }
}
