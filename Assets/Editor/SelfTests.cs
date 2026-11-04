using Shooter.Enemies;
using Shooter.Player;
using Shooter.Upgrades;
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
            TestAmmoPickups();
            TestPlayerHealth();
            TestEnemyHealth();
            TestAugmentMultipliersAndCaps();
            TestAugmentAirJumps();
            TestAugmentsOnPlayerHealth();
            TestWeaponStateMagazineClamp();

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
            // Queried at the start time (0.2) rather than 1.7, which is start + duration.
            Check(state.ReloadProgress(0.2f) < 0.001f, "reload progress starts at 0");
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

        private static void TestAmmoPickups()
        {
            // Ammo crates add to the reserve and never push it past the cap.
            var state = new WeaponState(30, 100, 0.1f, 1f, 300);
            state.AddReserveAmmo(45);
            Check(state.ReserveAmmo == 145, "ammo crate adds reserve ammo");

            // Consume and reload so reserve drops, then top up again.
            state.TryFire(0f);
            state.TryStartReload(0.1f);
            state.Tick(1.2f);
            Check(state.ReserveAmmo < 145, "reloading spends reserve ammo");

            state.AddReserveAmmo(500);
            Check(state.ReserveAmmo == 300, "reserve ammo is capped at the maximum");

            state.AddReserveAmmo(-20);
            Check(state.ReserveAmmo == 300, "negative ammo is ignored");

            var full = new WeaponState(30, 300, 0.1f, 1f, 300);
            Check(full.ReserveAmmo == full.MaxReserve, "a full reserve reports as full");
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

        private static void TestAugmentMultipliersAndCaps()
        {
            var root = new GameObject("AugmentTestRoot");
            var upgrades = root.AddComponent<UpgradeManager>();

            // Percentage stacks multiply together.
            upgrades.AddDamageMultiplier(0.3f);
            upgrades.AddDamageMultiplier(0.3f);
            Check(Mathf.Approximately(upgrades.DamageMultiplier, 1.69f), "damage augments stack multiplicatively");

            upgrades.ImproveSpread(0.4f);
            upgrades.ImproveSpread(0.4f);
            Check(Mathf.Approximately(upgrades.SpreadMultiplier, 0.36f), "spread augments stack down to 36%");

            // Floors: repeated picks must never degenerate the stats to zero.
            for (int i = 0; i < 6; i++)
            {
                upgrades.ImproveSpread(0.4f);
                upgrades.ReduceRecoil(0.35f);
                upgrades.ReduceSlideCooldown(0.4f);
                upgrades.AddDamageReduction(0.15f);
            }

            Check(Mathf.Approximately(upgrades.SpreadMultiplier, 0.1f), "spread improvement is floored at 10%");
            Check(Mathf.Approximately(upgrades.RecoilMultiplier, 0.1f), "recoil reduction is floored at 10%");
            Check(Mathf.Approximately(upgrades.SlideCooldownMultiplier, 0.2f), "slide cooldown is floored at 20%");
            Check(Mathf.Approximately(upgrades.DamageReduction, 0.45f), "damage reduction is capped at 45%");

            upgrades.ResetAugments();
            Check(Mathf.Approximately(upgrades.DamageMultiplier, 1f), "reset restores the damage multiplier");
            Check(Mathf.Approximately(upgrades.DamageReduction, 0f), "reset clears damage reduction");

            Object.DestroyImmediate(root);
        }

        private static void TestAugmentAirJumps()
        {
            var root = new GameObject("AugmentJumpTestRoot");
            var upgrades = root.AddComponent<UpgradeManager>();

            Check(!upgrades.TryConsumeAirJump(), "no air jump is available before the augment");
            upgrades.AddExtraAirJump();
            Check(upgrades.TryConsumeAirJump(), "the extra air jump is available on purchase");
            Check(!upgrades.TryConsumeAirJump(), "a single extra jump cannot be used twice in the air");
            upgrades.ResetAirJumps();
            Check(upgrades.TryConsumeAirJump(), "landing restores the air jump");

            Object.DestroyImmediate(root);
        }

        private static void TestAugmentsOnPlayerHealth()
        {
            var root = new GameObject("AugmentHealthTestRoot");
            var upgrades = root.AddComponent<UpgradeManager>();
            var health = root.AddComponent<PlayerHealth>();
            upgrades.playerHealth = health; // The manager routes health augments through wiring.
            health.ResetState();

            health.TakeDamage(10f);
            Check(Mathf.Approximately(health.CurrentHealth, 90f), "baseline damage is unaffected without augments");

            upgrades.AddMaxHealth(40f);
            Check(Mathf.Approximately(health.MaxHealth, 140f), "Titan Frame raises maximum health");
            Check(Mathf.Approximately(health.CurrentHealth, 130f), "the max-health bonus also heals");

            // TakeDamage reads the static instance; point it at this manager for the
            // check and restore it afterwards so other tests are not affected.
            var instanceProperty = typeof(UpgradeManager).GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            object previousInstance = instanceProperty.GetValue(null);
            instanceProperty.SetValue(null, upgrades);

            upgrades.AddDamageReduction(0.15f);
            health.TakeDamage(10f);
            Check(Mathf.Approximately(health.CurrentHealth, 121.5f), "Kevlar Weave reduces damage by 15%");
            instanceProperty.SetValue(null, previousInstance);

            Object.DestroyImmediate(root);
        }

        private static void TestWeaponStateMagazineClamp()
        {
            var state = new WeaponState(30, 150, 0.1f, 1f);

            state.SetMagazineAmmo(9999);
            Check(state.AmmoInMagazine == state.MagazineSize, "magazine count clamps to the magazine size");
            state.SetMagazineAmmo(-5);
            Check(state.AmmoInMagazine == 0, "magazine count clamps at zero");

            // The reserve bonus from augments only makes sense if the cap moves too.
            var deep = new WeaponState(30, 300, 0.1f, 1f, 420);
            deep.AddReserveAmmo(120);
            Check(deep.MaxReserve == 420, "the raised reserve cap is applied on construction");
            Check(deep.ReserveAmmo == 420, "the reserve bonus fits inside the raised cap");
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
