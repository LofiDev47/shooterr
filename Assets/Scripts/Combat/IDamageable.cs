namespace Shooter.Combat
{
    /// <summary>
    /// Anything the player's weapon can hurt. Kept as an interface so the weapon
    /// does not need to know about specific enemy implementations.
    /// </summary>
    public interface IDamageable
    {
        /// <param name="amount">Raw damage to apply.</param>
        /// <param name="hitPoint">World-space impact point, used for feedback effects.</param>
        void TakeDamage(float amount, UnityEngine.Vector3 hitPoint);
    }
}
