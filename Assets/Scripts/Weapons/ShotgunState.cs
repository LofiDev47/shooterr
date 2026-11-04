using UnityEngine;

namespace Shooter.Weapons
{
    /// <summary>
    /// Ammo, cooldown and reload rules for the underbarrel breach shotgun.
    /// Kept free of Unity objects so the timing can be tested headlessly.
    /// </summary>
    public sealed class ShotgunState
    {
        private float nextFireAt;
        private float reloadStartedAt;
        private float reloadDuration = 1f;

        public int Shells { get; private set; }
        public int Capacity { get; }

        /// <summary>True while the tube is being reloaded.</summary>
        public bool IsReloading => ReloadEndsAt >= 0f;

        /// <summary>Time at which the current reload finishes, or -1.</summary>
        public float ReloadEndsAt { get; private set; } = -1f;

        public ShotgunState(int capacity)
        {
            Capacity = Mathf.Max(1, capacity);
            Shells = Capacity;
        }

        public bool CanFire(float now) => Shells > 0 && !IsReloading && now >= nextFireAt;

        /// <summary>
        /// Consumes one shell and starts the cooldown. Emptying the tube starts
        /// the reload automatically.
        /// </summary>
        public bool TryFire(float now, float cooldown, float reloadTime)
        {
            if (!CanFire(now))
            {
                return false;
            }

            Shells--;
            nextFireAt = now + Mathf.Max(0.05f, cooldown);

            if (Shells <= 0)
            {
                reloadStartedAt = now;
                reloadDuration = Mathf.Max(0.1f, reloadTime);
                ReloadEndsAt = now + reloadDuration;
            }

            return true;
        }

        /// <summary>Finishes the reload when its timer elapses.</summary>
        public void Tick(float now)
        {
            if (IsReloading && now >= ReloadEndsAt)
            {
                Shells = Capacity;
                ReloadEndsAt = -1f;
            }
        }

        /// <summary>0 at reload start, 1 when the tube is full again.</summary>
        public float ReloadProgress(float now)
        {
            if (!IsReloading)
            {
                return 1f;
            }

            float elapsed = now - reloadStartedAt;
            return Mathf.Clamp01(elapsed / Mathf.Max(0.001f, reloadDuration));
        }

        /// <summary>Refills the tube immediately (scene reload / tests).</summary>
        public void Reset()
        {
            Shells = Capacity;
            ReloadEndsAt = -1f;
            nextFireAt = 0f;
        }
    }
}
