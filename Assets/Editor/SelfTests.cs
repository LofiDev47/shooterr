using Shooter.Combat;
using Shooter.Enemies;
using Shooter.Managers;
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
            TestExploderInertUntilArmed();
            TestAugmentMultipliersAndCaps();
            TestAugmentAirJumps();
            TestTraversalAndMeleeAugments();
            TestAugmentsOnPlayerHealth();
            TestWeaponStateMagazineClamp();
            TestWeaponStateRebuildTiming();
            TestSynergyDerivedMultipliers();
            TestSynergyCritHooks();
            TestSynergyKillHooks();
            TestAirborneAndCoyoteFraming();
            TestWindedSpeedFloor();
            TestCritFireRateReachesRifle();
            TestDryClickGate();
            TestWaveClearChimeGain();
            TestProjectileSweptHit();
            TestComboTracker();
            TestHeadshotBounds();
            TestShotgunState();
            TestRangeFalloffAndMelee();
            TestBallisticsAugments();
            TestBossWaveNumbers();
            TestBossPhaseThresholds();
            TestBossPhaseStepping();
            TestBossCoreDamage();

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

        /// <summary>
        /// Every enemy prefab carries an EnemyExploder; only the ones the spawner arms
        /// through Initialize() may detonate. Without the guard an ordinary grunt's
        /// death event fires the blast and damages its neighbours.
        /// </summary>
        private static void TestExploderInertUntilArmed()
        {
            var gameObject = new GameObject("SelfTest_Exploder");
            EnemyExploder exploder = gameObject.AddComponent<EnemyExploder>();

            exploder.Detonate();
            Check(!exploder.IsExploded, "an unarmed exploder does not detonate");
            Check(!exploder.IsFusing, "an unarmed exploder never starts a fuse");

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
            // Plain comparison: Unity's Mathf.Approximately has been seen to return
            // false for two equal zeros on the first run right after a script compile.
            Check(upgrades.DamageReduction <= 0f,
                $"reset clears damage reduction (left {upgrades.DamageReduction:R})");

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

        private static void TestTraversalAndMeleeAugments()
        {
            var root = new GameObject("AugmentTraversalTestRoot");
            var upgrades = root.AddComponent<UpgradeManager>();
            var poolMethod = typeof(UpgradeManager).GetMethod("BuildPool",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var pool = (AugmentDef[])poolMethod.Invoke(null, null);
            AugmentDef traversal = System.Array.Find(pool, card => card.Id == "Traversal Harness");
            AugmentDef brawler = System.Array.Find(pool, card => card.Id == "Brawler Kit");

            traversal.Apply(upgrades);
            brawler.Apply(upgrades);
            Check(Mathf.Approximately(upgrades.WallClimbMultiplier, 1.35f) &&
                  Mathf.Approximately(upgrades.WallKickMultiplier, 1.15f), "Traversal Harness buffs both wall moves");
            Check(Mathf.Approximately(upgrades.MeleeRangeMultiplier, 1.20f) &&
                  Mathf.Approximately(upgrades.MeleeDamageMultiplier, 1.30f), "Brawler Kit buffs melee reach and damage");
            Check(!traversal.CanOffer(upgrades) && !brawler.CanOffer(upgrades), "specialized augments are offered once per run");

            upgrades.ResetAugments();
            Check(upgrades.WallClimbMultiplier == 1f && upgrades.WallKickMultiplier == 1f &&
                  upgrades.MeleeRangeMultiplier == 1f && upgrades.MeleeDamageMultiplier == 1f &&
                  traversal.CanOffer(upgrades) && brawler.CanOffer(upgrades), "new runs clear and restore specialized augments");

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

        private static void TestWeaponStateRebuildTiming()
        {
            // Rebuilding stats mid-fight must keep the fire cooldown: Winded calls this
            // on every accepted hit, so dropping it lets the rifle fire at tap speed.
            var state = new WeaponState(30, 150, 0.1f, 1.5f);
            Check(state.TryFire(0f), "timing fixture fires its first round");

            var rebuilt = new WeaponState(40, 150, 0.1f, 1.5f);
            rebuilt.SetMagazineAmmo(state.AmmoInMagazine);
            rebuilt.CarryTimingFrom(state);
            Check(!rebuilt.CanFire(0.05f), "a stat rebuild keeps the fire cooldown");
            Check(rebuilt.CanFire(0.1f), "the rebuilt weapon fires once the original cooldown elapses");

            var reloading = new WeaponState(30, 150, 0.1f, 1.5f);
            reloading.TryFire(0f);
            Check(reloading.TryStartReload(0.2f), "timing fixture starts a reload");

            var rebuiltReload = new WeaponState(40, 150, 0.1f, 1.2f);
            rebuiltReload.SetMagazineAmmo(reloading.AmmoInMagazine);
            rebuiltReload.CarryTimingFrom(reloading);
            Check(rebuiltReload.IsReloading, "a stat rebuild keeps an in-flight reload");
            Check(!rebuiltReload.CanFire(0.5f), "a rebuilt weapon cannot fire mid-reload");
            Check(rebuiltReload.Tick(reloading.ReloadEndTime), "a carried reload still completes");
            Check(rebuiltReload.AmmoInMagazine == 40 && rebuiltReload.ReserveAmmo == 139,
                "a carried reload tops up into the new magazine and spends the reserve");
        }

        private static void TestSynergyDerivedMultipliers()
        {
            var root = new GameObject("SynergyTestRoot");
            var upgrades = root.AddComponent<UpgradeManager>();

            // Kinetic Rounds: damage scales with real movement, not with the speed stat.
            upgrades.AddSpeedMultiplier(0.2f);
            upgrades.AddDamageFromSpeed(0.35f);
            upgrades.SetMovementFactor(0f);
            Check(Mathf.Approximately(upgrades.EffectiveDamageMultiplier, 1f),
                "Kinetic Rounds pay nothing while standing still");

            upgrades.SetMovementFactor(1f);
            Check(Mathf.Approximately(upgrades.EffectiveDamageMultiplier, 1.35f),
                "Kinetic Rounds reach +35% at a full sprint");

            upgrades.AddCrit(0.2f, 2f);
            upgrades.AddDamageFromCrits(0.45f);
            // Not in a crit window: the crit bonus should not apply.
            Check(Mathf.Approximately(upgrades.EffectiveDamageMultiplier, 1.35f),
                "crit-fed damage only applies inside the window");

            upgrades.OnCrit(); // opens the window
            Check(Mathf.Approximately(upgrades.EffectiveDamageMultiplier, 1.35f * 1.45f),
                "crit-fed damage applies during the window");

            // Scalpel Rounds ride the critical multiplier itself.
            upgrades.AddScalpelDamage(0.25f);
            Check(Mathf.Approximately(upgrades.CriticalDamageMultiplier, 2.5f),
                "Scalpel Rounds add 25% on a critical hit");

            upgrades.AddCritFeedsFireRate(0.2f);
            Check(Mathf.Approximately(upgrades.EffectiveFireRateMultiplier, 1.2f),
                "crit-fed fire rate applies during the window");

            // Every reset must drop the weapon-mode cards too, not only the stats.
            upgrades.UnlockShotgun();
            upgrades.AddRicochetBounce();
            upgrades.AddBulletVelocity(0.2f);
            upgrades.AddBulletExplosion(2.4f, 0.35f);

            upgrades.ResetAugments();
            Check(Mathf.Approximately(upgrades.EffectiveDamageMultiplier, 1f), "synergy damage resets cleanly");
            Check(Mathf.Approximately(upgrades.EffectiveFireRateMultiplier, 1f), "synergy fire rate resets cleanly");
            Check(!upgrades.ShotgunUnlocked && upgrades.RicochetBounces == 0 &&
                  Mathf.Approximately(upgrades.BulletVelocityMultiplier, 1f) &&
                  upgrades.ExplosionRadius == 0f &&
                  upgrades.ExplosionDamageFraction == 0f &&
                  upgrades.MovementFactor == 0f,
                "reset clears the weapon-mode and ballistics cards");

            Object.DestroyImmediate(root);
        }

        private static void TestSynergyCritHooks()
        {
            var root = new GameObject("SynergyCritRoot");
            var upgrades = root.AddComponent<UpgradeManager>();
            var health = root.AddComponent<PlayerHealth>();
            upgrades.playerHealth = health;
            health.ResetState();

            health.TakeDamage(40f);
            Check(Mathf.Approximately(health.CurrentHealth, 60f), "crit-hook fixture starts damaged");

            upgrades.AddVampiricCritHeal(6f);
            upgrades.OnCrit();
            Check(Mathf.Approximately(health.CurrentHealth, 66f), "Vampiric Crits heal on a crit");

            upgrades.AddCritFeedsFireRate(0.2f);
            upgrades.OnCrit();
            Check(Mathf.Approximately(upgrades.EffectiveFireRateMultiplier, 1.2f),
                "Crit Combo opens the rapid-fire window");

            Object.DestroyImmediate(root);
        }

        private static void TestSynergyKillHooks()
        {
            var root = new GameObject("SynergyKillRoot");
            var upgrades = root.AddComponent<UpgradeManager>();
            var health = root.AddComponent<PlayerHealth>();
            upgrades.playerHealth = health;
            health.ResetState();

            health.TakeDamage(30f);
            upgrades.AddAdrenalineDamage(0.02f);
            upgrades.AddLifesteal(3f);
            upgrades.AddAmmoPerKillFraction(0.1f);

            upgrades.OnKill();
            Check(Mathf.Approximately(health.CurrentHealth, 73f), "Blood Rush heals on a kill");
            Check(Mathf.Approximately(upgrades.AdrenalineDamagePerKill, 0.02f), "Adrenaline Surge feeds one kill");

            for (int i = 0; i < 10; i++)
            {
                upgrades.OnKill();
            }

            Check(Mathf.Approximately(upgrades.AdrenalineDamagePerKill, 0.16f), "Adrenaline Surge caps at eight kills");
            Check(Mathf.Approximately(upgrades.EffectiveDamageMultiplier, 1.16f),
                "Adrenaline Surge reaches +16% damage at the cap");

            Object.DestroyImmediate(root);
        }

        private static void TestAirborneAndCoyoteFraming()
        {
            // The framing regression: ground state read before Move used to re-arm
            // the coyote window on take-off and clear Sky Hunter in the same frame.
            Check(PlayerController.JustLeftGround(true, false), "leaving ground is a real transition");
            Check(!PlayerController.JustLeftGround(false, false), "staying airborne does not re-arm airtime");
            Check(PlayerController.JustLanded(false, true), "touching down clears airtime");
            Check(!PlayerController.CanGroundedJump(1f, -999f, 0.12f),
                "a jump's negative sentinel prevents a free coyote jump");
            Check(PlayerController.CanGroundedJump(1f, 0.95f, 0.12f), "coyote window works after a real landing");
            Check(!PlayerController.CanGroundedJump(1.2f, 0.95f, 0.12f), "coyote window expires");

            Check(UpgradeManager.SkyHunterBonus(10f, -1e9f, 0.3f) == 0f,
                "Sky Hunter is inert while grounded");
            Check(UpgradeManager.SkyHunterBonus(10.49f, 10f, 0.3f) == 0f,
                "Sky Hunter needs the full airborne window");
            Check(Mathf.Approximately(UpgradeManager.SkyHunterBonus(10.5f, 10f, 0.3f), 0.3f),
                "Sky Hunter pays out after 0.5 s of airtime");

            var root = new GameObject("SkyHunterRoot");
            var upgrades = root.AddComponent<UpgradeManager>();
            upgrades.AddSkyHunter();
            upgrades.OnJumpPressed();
            Check(upgrades.AirborneFirstHitDamageMultiplier == 0f, "Sky Hunter does not pay out on take-off");

            var startedAt = typeof(UpgradeManager).GetField("airborneStartedAt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            startedAt.SetValue(upgrades, Time.time - 0.6f);
            Check(upgrades.AirborneFirstHitDamageMultiplier > 0f, "Sky Hunter pays out mid-air");
            upgrades.OnLanded();
            Check(upgrades.AirborneFirstHitDamageMultiplier == 0f, "landing clears Sky Hunter");

            Object.DestroyImmediate(root);
        }

        private static void TestWindedSpeedFloor()
        {
            var root = new GameObject("WindedRoot");
            var upgrades = root.AddComponent<UpgradeManager>();
            upgrades.AddSpeedPenaltyPerShot(0.02f);

            upgrades.TakeSpeedPenalty();
            Check(upgrades.SpeedMultiplier < 1f, "each accepted hit drains speed");
            Check(upgrades.SpeedMultiplier > UpgradeManager.MinSpeedMultiplier, "one hit is far from the floor");

            for (int i = 0; i < 200; i++)
            {
                upgrades.TakeSpeedPenalty();
            }

            Check(upgrades.SpeedMultiplier >= UpgradeManager.MinSpeedMultiplier,
                "Winded never drops below its speed floor");
            Check(upgrades.SpeedMultiplier <= UpgradeManager.MinSpeedMultiplier + 0.0001f,
                "Winded settles exactly at the floor");

            upgrades.ResetAugments();
            Check(upgrades.SpeedMultiplier == 1f, "reset restores base speed");

            Object.DestroyImmediate(root);
        }

        private static void TestCritFireRateReachesRifle()
        {
            var upgradesRoot = new GameObject("CritRateRoot");
            var upgrades = upgradesRoot.AddComponent<UpgradeManager>();
            var weaponRoot = new GameObject("CritRateWeapon");
            var weapon = weaponRoot.AddComponent<Weapon>();

            var instanceProperty = typeof(UpgradeManager).GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            object previous = instanceProperty.GetValue(null);
            instanceProperty.SetValue(null, upgrades);

            weapon.RebuildState(); // edit-mode AddComponent does not run Awake
            float baseInterval = weapon.State.FireInterval;

            upgrades.AddCritFeedsFireRate(0.2f);
            upgrades.OnCrit();
            weapon.ApplyLiveFireRate();
            Check(weapon.State.FireInterval < baseInterval - 0.001f,
                "Crit Combo's rapid window shortens the rifle's fire interval");

            var boostUntil = typeof(UpgradeManager).GetField("fireRateBoostUntil",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            boostUntil.SetValue(upgrades, Time.time - 1f);
            weapon.ApplyLiveFireRate();
            Check(Mathf.Approximately(weapon.State.FireInterval, baseInterval),
                "the rifle returns to its normal interval when the window closes");

            instanceProperty.SetValue(null, previous);
            Object.DestroyImmediate(weaponRoot);
            Object.DestroyImmediate(upgradesRoot);
        }

        private static void TestDryClickGate()
        {
            var gate = new DryClickGate();

            Check(gate.ShouldPlay(0f, false, 0), "an empty gun clicks once");
            Check(!gate.ShouldPlay(0.01f, false, 0), "holding fire does not click every frame");
            Check(!gate.ShouldPlay(1f, true, 0), "no click while reloading");
            Check(!gate.ShouldPlay(1f, false, 5), "no click with a loaded magazine");
            Check(gate.ShouldPlay(0.35f, false, 0), "the click returns after its cooldown");

            gate.Reset();
            Check(gate.ShouldPlay(0.36f, false, 0), "reset re-arms the gate");
        }

        private static void TestWaveClearChimeGain()
        {
            Check(AudioManager.WaveSine(-0.42, 1480.0, 18.0) == 0f,
                "a note before its start offset is silent");
            Check(AudioManager.Decay(-0.42, 18.0) <= 1f,
                "negative time never turns the envelope into gain");
            Check(AudioManager.Decay(0.0, 18.0) == 1f, "the envelope starts at unit gain");
            Check(Mathf.Abs(AudioManager.WaveSine(0.42, 1480.0, 18.0)) <= 1f,
                "the chime stays within unit amplitude");
        }

        private static void TestProjectileSweptHit()
        {
            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.forward;

            Check(EnemyProjectile.SweptHit(origin, direction, 1f,
                    new Vector3(0.2f, 0f, 0.5f), 0.3f, out float hitDistance),
                "a bolt hits a player at the midpoint of its step");
            Check(Mathf.Abs(hitDistance - 0.5f) < 0.01f, "the hit reports the closest approach distance");

            Check(!EnemyProjectile.SweptHit(origin, direction, 1f,
                    new Vector3(0f, 0f, 1.4f), 0.3f, out _),
                "a player beyond the step is not hit");

            Check(!EnemyProjectile.SweptHit(origin, direction, 0.4f,
                    new Vector3(0.2f, 0f, 0.9f), 0.3f, out _),
                "geometry-clamped travel stops a later hit (cover still works)");

            Check(!EnemyProjectile.SweptHit(origin, direction, 1f,
                    new Vector3(0.2f, 0f, -0.4f), 0.3f, out _),
                "a player behind the bolt is not hit");

            Check(EnemyProjectile.CanHitPlayer(0.4f, 1f, true),
                "a player in front of cover is still hit");
            Check(!EnemyProjectile.CanHitPlayer(1f, 1f, true),
                "a player behind cover is not hit by the clamped sweep");
            Check(EnemyProjectile.CanHitPlayer(0.9f, 1f, false),
                "an unobstructed bolt can hit near the end of its step");
        }

        private static void TestRangeFalloffAndMelee()
        {
            Check(Mathf.Approximately(
                    Weapon.RangeDamageMultiplier(10f, 1f, 18f, 60f, 0.65f), 1f),
                "rifle keeps full damage inside its falloff window");

            float slow = Weapon.RangeDamageMultiplier(40f, 1f, 18f, 60f, 0.65f);
            float fast = Weapon.RangeDamageMultiplier(40f, 1.7f, 18f, 60f, 0.65f);

            Check(slow < 0.95f, "rifle damage falls off at long range");
            Check(fast > slow + 0.02f, "high velocity retains more damage at range");
            Check(Weapon.RangeDamageMultiplier(200f, 1f, 18f, 60f, 0.65f) >= 0.649f,
                "rifle falloff never drops below its floor");

            Check(Weapon.IsInMeleeArc(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 2f), 2.6f, 0.3f),
                "melee hits a target in front");
            Check(!Weapon.IsInMeleeArc(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -2f), 2.6f, 0.3f),
                "melee does not hit a target behind");
            Check(!Weapon.IsInMeleeArc(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 3.2f), 2.6f, 0.3f),
                "melee respects its range");
        }

        private static void TestBallisticsAugments()
        {
            var root = new GameObject("BallisticsRoot");
            var upgrades = root.AddComponent<UpgradeManager>();

            Check(!upgrades.ShotgunUnlocked, "underbarrel starts locked");
            upgrades.UnlockShotgun();
            Check(upgrades.ShotgunUnlocked, "Breach Tube unlocks the underbarrel");

            upgrades.AddRicochetBounce();
            upgrades.AddRicochetBounce();
            upgrades.AddRicochetBounce();
            Check(upgrades.RicochetBounces == 2, "ricochet bounces cap at two");

            upgrades.AddBulletVelocity(0.35f);
            upgrades.AddBulletVelocity(0.35f);
            upgrades.AddBulletVelocity(0.35f);
            Check(Mathf.Approximately(upgrades.BulletVelocityMultiplier, 1.7f),
                "bullet velocity caps at 1.7x");

            upgrades.AddBulletExplosion(2.4f, 0.35f);
            upgrades.AddBulletExplosion(2.4f, 0.35f);
            Check(Mathf.Approximately(upgrades.ExplosionRadius, 2.4f) &&
                  Mathf.Approximately(upgrades.ExplosionDamageFraction, 0.35f),
                "explosive rounds cap at one stack");

            Object.DestroyImmediate(root);
        }

        private static void TestShotgunState()
        {
            var shotgun = new ShotgunState(4);

            Check(shotgun.Shells == 4 && shotgun.Capacity == 4, "shotgun starts with four shells");
            Check(shotgun.CanFire(0f), "shotgun can fire when loaded");

            Check(shotgun.TryFire(0f, 0.9f, 2.4f), "first shell fires");
            Check(shotgun.Shells == 3, "firing spends one shell");
            Check(!shotgun.TryFire(0.2f, 0.9f, 2.4f), "cooldown blocks a second shell");
            Check(shotgun.TryFire(0.9f, 0.9f, 2.4f), "second shell fires after the cooldown");

            shotgun.TryFire(1.8f, 0.9f, 2.4f);
            Check(shotgun.TryFire(2.7f, 0.9f, 2.4f), "last shell fires");
            Check(shotgun.Shells == 0, "tube empties after the fourth shell");
            Check(shotgun.IsReloading, "an empty tube reloads automatically");
            Check(!shotgun.TryFire(3.0f, 0.9f, 2.4f), "cannot fire while reloading");

            shotgun.Tick(5.0f);
            Check(shotgun.IsReloading, "reload does not finish early");
            Check(shotgun.ReloadProgress(4.0f) > 0.4f, "reload progress advances");

            shotgun.Tick(5.2f);
            Check(shotgun.Shells == 4 && !shotgun.IsReloading, "reload refills the tube");
        }

        private static void TestComboTracker()
        {
            var combo = new ComboTracker(2f, 0.5f, 2f);

            Check(combo.Count == 0 && Mathf.Approximately(combo.Multiplier, 1f), "combo starts idle");
            Check(combo.RegisterKill(0f, 10) == 10, "first kill pays base score");

            Check(combo.RegisterKill(0.5f, 10) == 15, "second chained kill pays 1.5x");
            Check(combo.RegisterKill(1f, 10) == 20, "third chained kill pays 2x");
            Check(combo.RegisterKill(1.5f, 10) == 20, "combo multiplier is capped at 2x");
            Check(combo.Count == 4, "combo counts every chained kill");
            Check(combo.Remaining(2f) > 0f, "a live combo reports time remaining");

            combo.Tick(4f);
            Check(combo.Count == 0, "combo lapses after its window");
            Check(Mathf.Approximately(combo.Multiplier, 1f), "lapsed combo resets the multiplier");
            Check(combo.RegisterKill(10f, 10) == 10, "a kill after the window restarts at base score");
            Check(combo.Best == 4, "combo remembers the best chain");
        }

        private static void TestHeadshotBounds()
        {
            var bounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(1f, 1.8f, 1f));

            Check(Weapon.IsHeadshot(bounds, new Vector3(0f, 1.7f, 0f)), "top of the body counts as a headshot");
            Check(!Weapon.IsHeadshot(bounds, new Vector3(0f, 1.2f, 0f)), "centre mass is not a headshot");
            Check(!Weapon.IsHeadshot(new Bounds(Vector3.zero, Vector3.zero), Vector3.zero),
                "degenerate bounds never headshot");
        }

        private static void TestBossWaveNumbers()
        {
            Check(!WaveSpawner.IsBossWaveNumber(0), "wave 0 is not a boss wave");
            Check(!WaveSpawner.IsBossWaveNumber(1), "wave 1 is not a boss wave");
            Check(!WaveSpawner.IsBossWaveNumber(4), "wave 4 is not a boss wave");
            Check(WaveSpawner.IsBossWaveNumber(5), "wave 5 is a boss wave");
            Check(WaveSpawner.IsBossWaveNumber(10), "wave 10 is a boss wave");
            Check(!WaveSpawner.IsBossWaveNumber(6), "wave 6 is not a boss wave");
        }

        private static void TestBossPhaseThresholds()
        {
            Check(BossController.PhaseFor(1f) == 1, "a full-health boss is in phase one");
            Check(BossController.PhaseFor(0.67f) == 1, "a boss above two-thirds is still phase one");
            Check(BossController.PhaseFor(0.66f) == 2, "hitting two-thirds enters phase two");
            Check(BossController.PhaseFor(0.5f) == 2, "a half-health boss is in phase two");
            Check(BossController.PhaseFor(0.33f) == 3, "hitting one-third enters phase three");
            Check(BossController.PhaseFor(0.05f) == 3, "a nearly dead boss stays in phase three");
        }

        private static void TestBossPhaseStepping()
        {
            // A burst can cross both thresholds at once. The boss then owes one roar
            // per phase, so phase two's stat step and summon wave are not skipped.
            Check(BossController.PendingPhaseRoars(1, 2) == 1, "crossing one threshold owes one roar");
            Check(BossController.PendingPhaseRoars(1, 3) == 2, "crossing two thresholds owes two roars");
            Check(BossController.PendingPhaseRoars(2, 2) == 0, "staying in the same phase owes nothing");
            Check(BossController.PendingPhaseRoars(2, 1) == 0, "healing up never owes a roar");
            Check(BossController.PendingPhaseRoars(1, 2) + BossController.PendingPhaseRoars(2, 3) ==
                  BossController.PendingPhaseRoars(1, 3),
                "stepping one phase at a time adds up to the same escalation");
        }

        private static void TestBossCoreDamage()
        {
            var gameObject = new GameObject("SelfTest_BossHealth");
            EnemyHealth health = gameObject.AddComponent<EnemyHealth>();
            health.Initialize(10f);

            Check(Mathf.Approximately(health.MaxHealth, 600f), "boss health scales off the base pool");

            health.IncomingDamageScale = 2f;
            health.TakeDamage(50f, Vector3.zero);
            Check(Mathf.Approximately(health.CurrentHealth, 500f),
                "an exposed core doubles the damage the boss takes");

            health.IncomingDamageScale = 0f;
            Check(health.IncomingDamageScale > 0f,
                "the incoming damage scale never reaches zero");

            // The roar is hard immunity, not a tiny multiplier: hits must not move
            // the bar, flash the model or raise Damaged the way a scaled hit does.
            int hitsWhileImmune = 0;
            System.Action<float, Vector3> onDamaged = (_, _) => hitsWhileImmune++;
            health.Damaged += onDamaged;

            float beforeRoar = health.CurrentHealth;
            health.SetInvulnerable(true);
            health.TakeDamage(50f, Vector3.zero);
            Check(Mathf.Approximately(health.CurrentHealth, beforeRoar),
                "a phase roar makes the boss immune");
            Check(hitsWhileImmune == 0, "an immune boss does not raise the hit event");

            health.SetInvulnerable(false);
            health.Damaged -= onDamaged;

            health.IncomingDamageScale = 1f;
            health.TakeDamage(10f, Vector3.zero);
            Check(Mathf.Approximately(health.CurrentHealth, 490f),
                "normal damage applies once the roar ends");

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
