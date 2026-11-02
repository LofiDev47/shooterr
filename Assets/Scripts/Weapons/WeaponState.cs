using UnityEngine;

namespace Shooter.Weapons
{
    /// <summary>
    /// Pure ammo/fire-rate/reload logic for a weapon, with no Unity object dependencies.
    /// Keeping it separate from <see cref="Weapon"/> makes the rules easy to unit test
    /// and easy to reason about. All timings are absolute times supplied by the caller.
    /// </summary>
    public sealed class WeaponState
    {
        public int MagazineSize { get; }
        public int AmmoInMagazine { get; private set; }
        public int ReserveAmmo { get; private set; }
        public int MaxReserve { get; }
        public float FireInterval { get; }
        public float ReloadDuration { get; }
        public bool IsReloading { get; private set; }

        /// <summary>Absolute time at which the current reload finishes.</summary>
        public float ReloadEndTime { get; private set; }

        private float nextFireTime;

        public WeaponState(int magazineSize, int reserveAmmo, float fireInterval, float reloadDuration, int maxReserve = 300)
        {
            MagazineSize = Mathf.Max(1, magazineSize);
            MaxReserve = Mathf.Max(MagazineSize, maxReserve);
            ReserveAmmo = Mathf.Clamp(reserveAmmo, 0, MaxReserve);
            AmmoInMagazine = MagazineSize;
            FireInterval = Mathf.Max(0.01f, fireInterval);
            ReloadDuration = Mathf.Max(0.01f, reloadDuration);
        }

        /// <summary>True when a shot may be fired right now.</summary>
        public bool CanFire(float now) =>
            !IsReloading && AmmoInMagazine > 0 && now >= nextFireTime;

        /// <summary>Consumes one round and starts the fire-rate cooldown.</summary>
        public bool TryFire(float now)
        {
            if (!CanFire(now))
            {
                return false;
            }

            AmmoInMagazine--;
            nextFireTime = now + FireInterval;
            return true;
        }

        /// <summary>Begins a reload if it makes sense to do so.</summary>
        public bool TryStartReload(float now)
        {
            if (IsReloading || AmmoInMagazine >= MagazineSize || ReserveAmmo <= 0)
            {
                return false;
            }

            IsReloading = true;
            ReloadEndTime = now + ReloadDuration;
            return true;
        }

        /// <summary>
        /// Advances timed state. Returns true on the frame a reload completes, so the
        /// caller can play a sound and refresh the HUD.
        /// </summary>
        public bool Tick(float now)
        {
            if (!IsReloading || now < ReloadEndTime)
            {
                return false;
            }

            int needed = MagazineSize - AmmoInMagazine;
            int taken = Mathf.Min(needed, ReserveAmmo);
            AmmoInMagazine += taken;
            ReserveAmmo -= taken;
            IsReloading = false;
            return true;
        }

        /// <summary>Adds ammo to the reserve, respecting the reserve cap.</summary>
        public void AddReserveAmmo(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            ReserveAmmo = Mathf.Min(MaxReserve, ReserveAmmo + amount);
        }

        /// <summary>0..1 progress of the active reload (0 when not reloading).</summary>
        public float ReloadProgress(float now)
        {
            if (!IsReloading)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - (ReloadEndTime - now) / ReloadDuration);
        }
    }
}
