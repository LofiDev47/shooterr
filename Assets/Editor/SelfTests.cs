using Shooter.Enemies;
using Shooter.Player;
using Shooter.Weapons;
using UnityEditor;
using UnityEngine;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Headless sanity checks for the logic that does not need a rendering device.
    /// Run with:
    /// Unity -batchmode -quit -projectPath . -executeMethod Shooter.EditorTools.SelfTests.RunAll
    /// Exits with code 1 if any assertion fails.
    /// </summary>
    public static class SelfTests
    {
        private static int checks;
        private static int failures;

        public static void RunAll()
        {
            checks = 0;
            failures = 0;

            TestWeaponAmmoAndFireRate();
            TestWeaponReload();
            TestWeaponEmptyMagazine();
            TestPlayerHealth();
            TestEnemyHealth();

            Debug.Log($"[SelfTest] Finished: {checks - failures}/{checks} checks passed, {failures} failed.");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        private static void TestWeaponAmmoAndFireRate()
        {
            var state = new WeaponState(30, 150, 0.1f, 1.5f);

            Check(state.AmmoInMagazine == 30, "weapon starts with a full magazine");
            Check(state.ReserveAmmo == 150, "weapon starts with the configured reserve");
            Check(state.CanFire(0f), "weapon can fire when loaded");

            Check(state.TryFire(0f), "first shot fires");
            Check(state.AmmoInMagazine == 29, "firing consumes exactly one round");
            Check(!state.CanFire(0.05f), "fire rate blocks a shot inside the interval");
            Check(state.TryFire(0.1f), "shot fires once the interval elapses");
            Check(state.AmmoInMagazine == 28, "second shot consumes another round");
            Check(!state.CanFire(-1f), "cannot fire in the past relative to the cooldown");
        }

        private static void TestWeaponReload()
        {
            var state = new WeaponState(30, 150, 0.1f, 1.5f);

            Check(!state.TryStartReload(0f), "cannot reload a full magazine");

            state.TryFire(0f);
            state.TryFire(0.1f);

            Check(state.TryStartReload(0.2f), "reload starts with a partial magazine and reserve ammo");
            Check(!state.TryFire(0.3f), "cannot fire mid-reload");
            Check(!state.TryStartReload(0.4f), "cannot start a second reload while reloading");
            Check(Mathf.Approximately(state.ReloadProgress(0.2f), 0f), "reload progress starts at 0");
            Check(state.ReloadProgress(0.95f) > 0.4f, "reload progress advances over time");

            Check(!state.Tick(1.0f), "reload does not finish early");
            Check(state.Tick(1.7f), "reload finishes at start + duration");
            Check(!state.IsReloading, "reload flag clears when finished");
            Check(state.AmmoInMagazine == 30, "reload refills the magazine");
            Check(state.ReserveAmmo == 148, "reload only deducts the rounds actually loaded");
        }

        private static void TestWeaponEmptyMagazine()
        {
            var state = new WeaponState(2, 0, 0.1f, 1f);

            Check(state.TryFire(0f), "empty-reserve weapon can still fire its magazine");
            Check(state.TryFire(0.1f), "second round fires");
            Check(!state.TryFire(0.2f), "weapon cannot fire with an empty magazine");
            Check(!state.TryStartReload(0.3f), "weapon cannot reload without reserve ammo");
            Check(state.AmmoInMagazine == 0, "magazine stays empty");
        }

        private static void TestPlayerHealth()
        {
            var gameObject = new GameObject("SelfTest_Player");
            PlayerHealth health = gameObject.AddComponent<PlayerHealth>();
            health.ResetState();

            int deathCount = 0;
            int damageEvents = 0;
            health.Died += () => deathCount++;
            health.Damaged += _ => damageEvents++;

            Check(Mathf.Approximately(health.CurrentHealth, health.MaxHealth), "player starts at full health");

            health.TakeDamage(30f);
            Check(Mathf.Approximately(health.CurrentHealth, 70f), "damage reduces health by the exact amount");
            Check(damageEvents == 1, "damage event fires once per hit");

            health.TakeDamage(-10f);
            Check(Mathf.Approximately(health.CurrentHealth, 70f), "negative damage is ignored");

            health.TakeDamage(500f);
            Check(health.CurrentHealth == 0f, "overkill clamps health at zero");
            Check(deathCount == 1, "death event fires exactly once");

            health.TakeDamage(10f);
            Check(deathCount == 1, "damage after death does not fire the death event again");

            Object.DestroyImmediate(gameObject);
        }

        private static void TestEnemyHealth()
        {
            var gameObject = new GameObject("SelfTest_Enemy");
            EnemyHealth health = gameObject.AddComponent<EnemyHealth>();
            health.Initialize(2f);

            Check(Mathf.Approximately(health.MaxHealth, 120f), "enemy health scales with the wave multiplier");

            int deathCount = 0;
            health.Died += _ => deathCount++;

            health.TakeDamage(70f, Vector3.zero);
            Check(Mathf.Approximately(health.CurrentHealth, 50f), "enemy takes damage");
            Check(!health.IsDead, "enemy survives non-lethal damage");

            health.TakeDamage(80f, Vector3.zero);
            Check(health.CurrentHealth == 0f, "enemy health clamps at zero");
            Check(health.IsDead, "enemy reports death");
            Check(deathCount == 1, "enemy death event fires once");

            health.TakeDamage(10f, Vector3.zero);
            Check(deathCount == 1, "damage after death is ignored");

            Object.DestroyImmediate(gameObject);
        }

        private static void Check(bool condition, string description)
        {
            checks++;

            if (condition)
            {
                Debug.Log($"[SelfTest] PASS — {description}");
                return;
            }

            failures++;
            Debug.LogError($"[SelfTest] FAIL — {description}");
        }
    }
}
