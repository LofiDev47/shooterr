using UnityEngine;

namespace Shooter.Weapons
{
    /// <summary>
    /// Rate-limits the empty-magazine click. Without it, holding fire with no
    /// rounds left plays the click and retries the reload on every single frame.
    /// Pure so the behaviour is headless-testable.
    /// </summary>
    public sealed class DryClickGate
    {
        private readonly float interval;
        private float nextAllowedAt = -1f;

        public DryClickGate(float interval = 0.35f)
        {
            this.interval = Mathf.Max(0.05f, interval);
        }

        /// <summary>True at most once per interval while the gun is dry.</summary>
        public bool ShouldPlay(float now, bool isReloading, int ammoInMagazine)
        {
            if (isReloading || ammoInMagazine > 0 || now < nextAllowedAt)
            {
                return false;
            }

            nextAllowedAt = now + interval;
            return true;
        }

        /// <summary>Re-arms the gate (scene reload / tests).</summary>
        public void Reset() => nextAllowedAt = -1f;
    }
}
