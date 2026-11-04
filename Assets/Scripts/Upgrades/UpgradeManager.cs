using System;
using System.Collections.Generic;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using Shooter.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Shooter.Upgrades
{
    /// <summary>
    /// Rounds-style augment cards: after each cleared wave the game pauses and the
    /// player picks one of three offered augments. Augments stack, some trade a
    /// penalty for a bonus, and every stat they touch is read live by the weapon,
    /// the player controller or the player health.
    /// </summary>
    public sealed class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        [Header("Wiring (scene builder)")]
        public PlayerController player;
        public PlayerHealth playerHealth;
        public HUDController hud;

        [Header("Choice UI (scene builder)")]
        public GameObject choicePanel;
        public Text[] cardTitles;
        public Text[] cardDescriptions;
        public Text choiceHintText;

        public bool IsChoosing { get; private set; }

        private List<AugmentDef> offers = new List<AugmentDef>();
        private readonly Dictionary<string, int> ownedStacks = new Dictionary<string, int>();

        // --- live multiplier state (backing fields, mutated by augment defs) -----
        private float damageMultiplier = 1f;
        private float fireRateMultiplier = 1f;
        private float reloadSpeedMultiplier = 1f;
        private float magazineMultiplier = 1f;
        private float spreadMultiplier = 1f;
        private float recoilMultiplier = 1f;
        private float speedMultiplier = 1f;
        private float jumpMultiplier = 1f;
        private float slideSpeedMultiplier = 1f;
        private float slideCooldownMultiplier = 1f;
        private float maxHealthBonus;
        private float regenPerSecond;
        private float damageReduction;
        private float critChance;
        private float critMultiplier = 2f;
        private int extraAirJumps;
        private int bonusPierce;
        private int reserveBonus;
        private float lifestealPerKill;

        // --- synergy state (new cards) ----------------------------------------------
        private float damageFromSpeed;       // Kinetic Rounds / Shuttle
        private float speedPenaltyPerShot;   // Winded trade; see Weapon.AfterShotAccepted
        private float damageFromCrits;       // Double Or Nothing: live damage multiplier
        private int adrenaline;              // kills while chosen cards are owned
        private float adrenalineDamagePerKill;
        private float vampiricCritHeal;
        private float scalpelDamagePerCrit;
        private bool skyHunter;
        private float airborneDamageMultiplier;
        private float slideFirstHitMultiplier;
        private float critReducesSlideCooldown;
        private float critFeedsFireRate;
        private int explosiveRadius;         // kills within this many metres chain
        private float ammoPerKillFraction;

        private float critWindowEnd;         // last time Crit() fired
        private float baseCritChance;
        private int consecutiveCrits;
        private float fireRateBoostUntil;    // current rapid window
        private float airborneStartedAt = -1e9f;

        private Weapon weapon;
        private int airJumpsUsed;

        // --- read-only views used by gameplay code -------------------------------
        public float DamageMultiplier => damageMultiplier;
        public float FireRateMultiplier => fireRateMultiplier;
        public float ReloadSpeedMultiplier => reloadSpeedMultiplier;
        public float MagazineMultiplier => magazineMultiplier;
        public float SpreadMultiplier => spreadMultiplier;
        public float RecoilMultiplier => recoilMultiplier;
        public float SpeedMultiplier => speedMultiplier;
        public float JumpMultiplier => jumpMultiplier;
        public float SlideSpeedMultiplier => slideSpeedMultiplier;
        public float SlideCooldownMultiplier => slideCooldownMultiplier;
        public float MaxHealthBonus => maxHealthBonus;
        public float RegenPerSecond => regenPerSecond;
        public float DamageReduction => damageReduction;
        public float CritChance => critChance;
        public float CritMultiplier => critMultiplier;
        public int ExtraAirJumps => extraAirJumps;
        public int BonusPierce => bonusPierce;
        public int ReserveBonus => reserveBonus;
        public float LifestealPerKill => lifestealPerKill;
        public bool HasSkyHunter => skyHunter;
        public float AirborneDamageMultiplier => airborneDamageMultiplier;
        public float SpeedPenaltyPerShot => speedPenaltyPerShot;
        public float ScalpelDamagePerCrit => scalpelDamagePerCrit;

        /// <summary>Base crit chance (before Chain Crits extends it dynamically).</summary>
        public float BaseCritChance => baseCritChance;

        public int ConsecutiveCrits => consecutiveCrits;

        /// <summary>Effective crit chance including Chain Crits' decay window.</summary>
        public float EffectiveCritChance =>
            Mathf.Clamp01(baseCritChance + (consecutiveCrits > 0 && Time.time <= critWindowEnd ? 0.05f : 0f));

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Cached late so the scene builder has finished wiring everything.
            weapon = FindAnyObjectByType<Weapon>();
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
            }
        }

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (IsChoosing)
            {
                HandleChoiceInput();
                return;
            }

            // Regeneration only advances while the game is actually running; with
            // time scale frozen (choose screen or game over) Time.deltaTime is zero.
            if (regenPerSecond > 0f && playerHealth != null && !playerHealth.IsDead &&
                GameManager.Instance != null && GameManager.Instance.IsPlaying &&
                playerHealth.CurrentHealth < playerHealth.MaxHealth)
            {
                playerHealth.Heal(regenPerSecond * Time.deltaTime);
            }
        }

        // ---------------------------------------------------------------- offering

        /// <summary>
        /// Pauses the run and offers a choice of augments. Called by the wave spawner
        /// when a wave is cleared. Does nothing on game over or with an empty pool.
        /// </summary>
        public void OfferChoice()
        {
            if (IsChoosing || choicePanel == null ||
                GameManager.Instance == null || !GameManager.Instance.IsPlaying ||
                playerHealth == null || playerHealth.IsDead)
            {
                return;
            }

            offers = RollOffers(3);

            if (offers.Count == 0)
            {
                return;
            }

            IsChoosing = true;
            Time.timeScale = 0f;

            if (player != null)
            {
                player.ControlsEnabled = false;
            }

            choicePanel.SetActive(true);

            for (int i = 0; i < 3; i++)
            {
                bool present = i < offers.Count;

                if (i < cardTitles.Length && cardTitles[i] != null)
                {
                    cardTitles[i].text = present ? offers[i].Title : string.Empty;
                    cardTitles[i].gameObject.SetActive(present);
                }

                if (i < cardDescriptions.Length && cardDescriptions[i] != null)
                {
                    cardDescriptions[i].text = present ? offers[i].Description : string.Empty;
                    cardDescriptions[i].gameObject.SetActive(present);
                }
            }

            if (choiceHintText != null)
            {
                choiceHintText.text = offers.Count >= 2 ? "PRESS 1 / 2 / 3 TO PICK" : "PRESS 1 TO PICK";
            }

            Debug.Log($"[Shooter] Offering {offers.Count} augments: {string.Join(", ", offers.ConvertAll(o => o.Title))}.");
        }

        private void HandleChoiceInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame && offers.Count > 0)
            {
                Pick(0);
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame && offers.Count > 1)
            {
                Pick(1);
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame && offers.Count > 2)
            {
                Pick(2);
            }
        }

        /// <summary>Applies the chosen augment and resumes the run.</summary>
        private void Pick(int index)
        {
            if (index < 0 || index >= offers.Count)
            {
                return;
            }

            AugmentDef def = offers[index];
            offers = new List<AugmentDef>();

            ownedStacks.TryGetValue(def.Id, out int stacks);
            ownedStacks[def.Id] = stacks + 1;

            def.Apply(this);
            weapon?.OnAugmentsChanged();

            choicePanel.SetActive(false);
            IsChoosing = false;

            if (player != null)
            {
                player.ControlsEnabled = true;
            }

            // Only hand time back to the game if the run is still going (it could have
            // ended while the choice screen was up in theory).
            if (GameManager.Instance != null && GameManager.Instance.IsPlaying)
            {
                Time.timeScale = 1f;
            }

            hud?.ShowToast($"{def.Title.ToUpper()} ACQUIRED");
            RefreshOwnedLabel();
        }

        /// <summary>Resets per-run augment state. Kept for restarts and tests.</summary>
        public void ResetAugments()
        {
            ownedStacks.Clear();
            damageMultiplier = 1f;
            fireRateMultiplier = 1f;
            reloadSpeedMultiplier = 1f;
            magazineMultiplier = 1f;
            spreadMultiplier = 1f;
            recoilMultiplier = 1f;
            speedMultiplier = 1f;
            jumpMultiplier = 1f;
            slideSpeedMultiplier = 1f;
            slideCooldownMultiplier = 1f;
            maxHealthBonus = 0f;
            regenPerSecond = 0f;
            damageReduction = 0f;
            critChance = 0f;
            extraAirJumps = 0;
            bonusPierce = 0;
            reserveBonus = 0;
            lifestealPerKill = 0f;

            // Synergy state reset.
            damageFromSpeed = 0f;
            speedPenaltyPerShot = 0f;
            damageFromCrits = 0f;
            adrenaline = 0;
            adrenalineDamagePerKill = 0f;
            vampiricCritHeal = 0f;
            scalpelDamagePerCrit = 0f;
            skyHunter = false;
            airborneDamageMultiplier = 0f;
            slideFirstHitMultiplier = 0f;
            critReducesSlideCooldown = 0f;
            critFeedsFireRate = 0f;
            explosiveRadius = 0;
            ammoPerKillFraction = 0f;
            baseCritChance = 0f;
            consecutiveCrits = 0;
            critWindowEnd = 0f;
            fireRateBoostUntil = 0f;
            airborneStartedAt = -1e9f;
        }

        // ---------------------------------------------------------------- synergy modifiers

        public void AddDamageFromSpeed(float fraction) => damageFromSpeed += fraction;

        public void AddSpeedPenaltyPerShot(float fraction) => speedPenaltyPerShot += fraction;

        /// <summary>Double Or Nothing: stacking damage on each crit, capped at +90%.</summary>
        public void AddDamageFromCrits(float value) => damageFromCrits = Mathf.Min(0.9f, damageFromCrits + value);

        /// <summary>Adrenaline Surge: every kill feeds a flat damage tick (capped at eight kills).</summary>
        public void AddAdrenalineDamage(float value) => adrenalineDamagePerKill += value;

        public void AddVampiricCritHeal(float amount) => vampiricCritHeal += amount;

        public void AddScalpelDamage(float value) => scalpelDamagePerCrit += value;

        public void AddSkyHunter()
        {
            skyHunter = true;
            airborneDamageMultiplier = 0.3f;
        }

        public void AddSlideFirstHit(float value) => slideFirstHitMultiplier += value;

        public void AddCritReducesSlideCooldown(float value) => critReducesSlideCooldown += value;

        public void AddCritFeedsFireRate(float value) => critFeedsFireRate += value;

        public void AddExplosiveChain(int radius) => explosiveRadius = Mathf.Max(explosiveRadius, radius);


        public int ExplosiveRadius => explosiveRadius;

        /// <summary>Base damage fed per kill from Adrenaline Surge (max value is capped at eight kills).</summary>
        public float AdrenalineDamagePerKill
        {
            get
            {
                float fed = adrenalineDamagePerKill * adrenaline;
                return Mathf.Min(0.16f, fed);
            }
        }

        /// <summary>Salvage Rounds: a fraction of every kill refunds as reserve ammo.</summary>
        public void AddAmmoPerKillFraction(float fraction) => ammoPerKillFraction += fraction;

        // --- derived multipliers read by the weapon ---------------------------------

        /// <summary>Total damage multiplier at this moment (base cards + speed + crits).</summary>
        public float EffectiveDamageMultiplier =>
            damageMultiplier *
            (1f + damageFromSpeed * SpeedMultiplier) *
            (1f + (consecutiveCrits > 0 && Time.time <= critWindowEnd ? damageFromCrits : 0f));

        /// <summary>Fire-rate multiplier, including Chain Crits' rapid window.</summary>
        public float EffectiveFireRateMultiplier =>
            fireRateMultiplier * (Time.time <= fireRateBoostUntil ? 1f + critFeedsFireRate : 1f);

        /// <summary>Damage added to the next hit when the player slides (one-shot).</summary>
        public float SlideFirstHitDamageMultiplier
        {
            get
            {
                if (player == null || !player.IsSliding)
                {
                    return 0f;
                }

                return slideFirstHitMultiplier;
            }
        }

        /// <summary>Damage added to the shot immediately after an airborne period (one-shot).</summary>
        public float AirborneFirstHitDamageMultiplier
        {
            get
            {
                if (!skyHunter || airborneStartedAt < -1e8f)
                {
                    return 0f;
                }

                return Time.time - airborneStartedAt >= 0.5f ? airborneDamageMultiplier : 0f;
            }
        }

        // ---------------------------------------------------------------- modifiers

        /// <summary>Stack helper for percentage-based modifiers.</summary>
        public void AddDamageMultiplier(float value) => damageMultiplier *= 1f + value;

        public void AddFireRateMultiplier(float value) => fireRateMultiplier *= 1f + value;

        public void AddReloadSpeedMultiplier(float value) => reloadSpeedMultiplier *= 1f + value;

        public void AddMagazineMultiplier(float value) => magazineMultiplier *= 1f + value;

        /// <summary>Improves accuracy: multiplies the spread cone by (1 - value), floored at 10%.</summary>
        public void ImproveSpread(float value)
        {
            spreadMultiplier = Mathf.Max(0.1f, spreadMultiplier * (1f - value));
        }

        /// <summary>Worsens accuracy for trade-off cards: multiplies the spread cone by (1 + value).</summary>
        public void AddSpreadPenalty(float value)
        {
            spreadMultiplier *= 1f + value;
        }

        public void ReduceRecoil(float value)
        {
            recoilMultiplier = Mathf.Max(0.1f, recoilMultiplier * (1f - value));
        }

        public void AddSpeedMultiplier(float value) => speedMultiplier *= 1f + value;

        public void AddJumpMultiplier(float value) => jumpMultiplier *= 1f + value;

        public void AddSlideSpeed(float value) => slideSpeedMultiplier *= 1f + value;

        public void ReduceSlideCooldown(float value)
        {
            slideCooldownMultiplier = Mathf.Max(0.2f, slideCooldownMultiplier * (1f - value));
        }

        /// <summary>Raises max health and heals by the same amount so the card feels good.</summary>
        public void AddMaxHealth(float amount)
        {
            maxHealthBonus += amount;
            playerHealth?.IncreaseMaxHealth(amount);
        }

        public void AddRegen(float amount) => regenPerSecond += amount;

        public void AddDamageReduction(float value)
        {
            damageReduction = Mathf.Clamp(damageReduction + value, 0f, 0.45f);
        }

        public void AddCrit(float chance, float multiplier)
        {
            critChance = Mathf.Clamp01(critChance + chance);
            baseCritChance = critChance;

            if (multiplier > critMultiplier)
            {
                critMultiplier = multiplier;
            }
        }

        public void AddExtraAirJump()
        {
            extraAirJumps++;
        }

        public void AddBonusPierce(int amount)
        {
            bonusPierce = Mathf.Min(3, bonusPierce + amount);
        }

        public void AddReserveBonus(int amount)
        {
            reserveBonus += amount;
        }

        public void AddLifesteal(float amount) => lifestealPerKill += amount;

        // ---------------------------------------------------------------- hooks

        /// <summary>Called by enemy deaths, applies lifesteal heals and kill-based synergies.</summary>
        public void OnKill()
        {
            if (playerHealth != null && !playerHealth.IsDead)
            {
                if (lifestealPerKill > 0f)
                {
                    playerHealth.Heal(lifestealPerKill);
                }

                if (adrenalineDamagePerKill > 0f)
                {
                    adrenaline = Mathf.Min(8, adrenaline + 1);
                }

                // Salvage Rounds: a fraction of the round is refunded as reserve ammo.
                if (weapon != null && ammoPerKillFraction > 0f && weapon.State != null)
                {
                    weapon.AddAmmo(Mathf.CeilToInt(weapon.State.MagazineSize * ammoPerKillFraction));
                }
            }
        }

        /// <summary>Called on every weapon hit that lands a critical.</summary>
        public void OnCrit()
        {
            consecutiveCrits++;
            critWindowEnd = Time.time + 3f;

            if (vampiricCritHeal > 0f && playerHealth != null && !playerHealth.IsDead)
            {
                playerHealth.Heal(vampiricCritHeal);
            }

            // Chain Reaction: crits reset the slide cooldown and open a rapid-fire window.
            if (critReducesSlideCooldown > 0f && player != null)
            {
                player.ResetSlideCooldown();
                fireRateBoostUntil = Time.time + 2f;
            }
        }

        /// <summary>Called when the player lands, so airborne damage timing resets.</summary>
        public void OnLanded()
        {
            airborneStartedAt = -1e9f;
        }

        /// <summary>Called the frame the jump key fires, so airborne damage timing starts.</summary>
        public void OnJumpPressed()
        {
            airborneStartedAt = Time.time;
        }

        /// <summary>
        /// Winded interlock: each accepted hit drains a small fixed amount of speed.
        /// Rebuilds the weapon so fire rate never restores speed instead of damage.
        /// </summary>
        public void TakeSpeedPenalty()
        {
            if (speedPenaltyPerShot <= 0f)
            {
                return;
            }

            AddSpeedMultiplier(-speedPenaltyPerShot);
            weapon?.OnAugmentsChanged();
        }

        /// <summary>One extra air jump is consumed per airborne jump; reset on landing.</summary>
        public bool TryConsumeAirJump()
        {
            if (airJumpsUsed >= extraAirJumps)
            {
                return false;
            }

            airJumpsUsed++;
            return true;
        }

        public void ResetAirJumps()
        {
            airJumpsUsed = 0;
        }

        private void HandlePlayerDied()
        {
            // If the run ends while a choice is somehow open, close it and leave the
            // game-over freeze (time scale) in place.
            if (IsChoosing)
            {
                IsChoosing = false;
                choicePanel?.SetActive(false);
                offers = new List<AugmentDef>();
            }
        }

        private List<AugmentDef> RollOffers(int count)
        {
            List<AugmentDef> pool = new List<AugmentDef>();

            foreach (AugmentDef def in BuildPool())
            {
                if (def.CanOffer == null || def.CanOffer(this))
                {
                    pool.Add(def);
                }
            }

            var picked = new List<AugmentDef>();

            while (picked.Count < count && pool.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return picked;
        }

        private void RefreshOwnedLabel()
        {
            // Toasts already announce each pick; the run summary lives in the console
            // while there is no dedicated panel. Keep this cheap and simple.
            if (ownedStacks.Count == 0)
            {
                return;
            }

            var summary = new List<string>();

            foreach (KeyValuePair<string, int> pair in ownedStacks)
            {
                summary.Add(pair.Key + (pair.Value > 1 ? $" x{pair.Value}" : string.Empty));
            }

            Debug.Log($"[Shooter] Augments: {string.Join(", ", summary)}.");
        }

        // ---------------------------------------------------------------- pool

        private static AugmentDef[] BuildPool()
        {
            return new[]
            {
                // --- pure upgrades -------------------------------------------------
                new AugmentDef
                {
                    Id = "Heavy Rounds",
                    Title = "Heavy Rounds",
                    Description = "+30% weapon damage",
                    Apply = manager => manager.AddDamageMultiplier(0.3f),
                },
                new AugmentDef
                {
                    Id = "Trigger Finger",
                    Title = "Trigger Finger",
                    Description = "+25% fire rate",
                    Apply = manager => manager.AddFireRateMultiplier(0.25f),
                },
                new AugmentDef
                {
                    Id = "Speed Loader",
                    Title = "Speed Loader",
                    Description = "Reloads are 35% faster",
                    Apply = manager => manager.AddReloadSpeedMultiplier(0.35f),
                },
                new AugmentDef
                {
                    Id = "Big Magazine",
                    Title = "Big Magazine",
                    Description = "Magazine holds 50% more rounds",
                    Apply = manager => manager.AddMagazineMultiplier(0.5f),
                },
                new AugmentDef
                {
                    Id = "Deep Pockets",
                    Title = "Deep Pockets",
                    Description = "+120 reserve ammo (and max)",
                    Apply = manager => manager.AddReserveBonus(120),
                },
                new AugmentDef
                {
                    Id = "Steady Hands",
                    Title = "Steady Hands",
                    Description = "40% tighter spread",
                    Apply = manager => manager.ImproveSpread(0.4f),
                },
                new AugmentDef
                {
                    Id = "Tamed Recoil",
                    Title = "Tamed Recoil",
                    Description = "35% less recoil",
                    Apply = manager => manager.ReduceRecoil(0.35f),
                },
                new AugmentDef
                {
                    Id = "Track Shoes",
                    Title = "Track Shoes",
                    Description = "+20% move speed",
                    Apply = manager => manager.AddSpeedMultiplier(0.2f),
                },
                new AugmentDef
                {
                    Id = "Moon Legs",
                    Title = "Moon Legs",
                    Description = "Jump 25% higher",
                    Apply = manager => manager.AddJumpMultiplier(0.25f),
                },
                new AugmentDef
                {
                    Id = "Titan Frame",
                    Title = "Titan Frame",
                    Description = "+40 max health and heal 40",
                    Apply = manager => manager.AddMaxHealth(40f),
                },
                new AugmentDef
                {
                    Id = "Nanite Repair",
                    Title = "Nanite Repair",
                    Description = "Regenerate 2 health per second",
                    Apply = manager => manager.AddRegen(2f),
                },
                new AugmentDef
                {
                    Id = "Blood Rush",
                    Title = "Blood Rush",
                    Description = "Heal 3 health on every kill",
                    Apply = manager => manager.AddLifesteal(3f),
                },
                new AugmentDef
                {
                    Id = "Double Jump",
                    Title = "Double Jump",
                    Description = "One extra jump while airborne",
                    CanOffer = manager => manager.ExtraAirJumps < 1,
                    Apply = manager => manager.AddExtraAirJump(),
                },
                new AugmentDef
                {
                    Id = "Ricochet Rounds",
                    Title = "Ricochet Rounds",
                    Description = "Shots pierce 1 extra enemy",
                    CanOffer = manager => manager.BonusPierce < 3,
                    Apply = manager => manager.AddBonusPierce(1),
                },
                new AugmentDef
                {
                    Id = "Critical Strike",
                    Title = "Critical Strike",
                    Description = "20% chance to deal double damage",
                    Apply = manager => manager.AddCrit(0.2f, 2f),
                },
                new AugmentDef
                {
                    Id = "Greased Soles",
                    Title = "Greased Soles",
                    Description = "Slides recover 40% faster and boost speed 15% more",
                    Apply = manager =>
                    {
                        manager.ReduceSlideCooldown(0.4f);
                        manager.AddSlideSpeed(0.15f);
                    },
                },

                // --- trades: a real bonus with a real cost, Rounds style -----------
                new AugmentDef
                {
                    Id = "Glass Cannon",
                    Title = "Glass Cannon",
                    Description = "+50% damage, but -15% move speed",
                    Apply = manager =>
                    {
                        manager.AddDamageMultiplier(0.5f);
                        manager.AddSpeedMultiplier(-0.15f);
                    },
                },
                new AugmentDef
                {
                    Id = "Hollow Point",
                    Title = "Hollow Point",
                    Description = "+45% damage, 40% wider spread",
                    Apply = manager =>
                    {
                        manager.AddDamageMultiplier(0.45f);
                        manager.AddSpreadPenalty(0.4f);
                    },
                },
                new AugmentDef
                {
                    Id = "Hair Trigger",
                    Title = "Hair Trigger",
                    Description = "+45% fire rate, but -20% damage",
                    Apply = manager =>
                    {
                        manager.AddFireRateMultiplier(0.45f);
                        manager.AddDamageMultiplier(-0.2f);
                    },
                },
                new AugmentDef
                {
                    Id = "Kevlar Weave",
                    Title = "Kevlar Weave",
                    Description = "Take 15% less damage",
                    CanOffer = manager => manager.DamageReduction < 0.45f,
                    Apply = manager => manager.AddDamageReduction(0.15f),
                },
                new AugmentDef
                {
                    Id = "Adrenaline Surge",
                    Title = "Adrenaline Surge",
                    Description = "+40% metal: gain 2% damage per kill, up to 16%",
                    CanOffer = manager => manager.AdrenalineDamagePerKill < 0.16f,
                    Apply = manager => manager.AddAdrenalineDamage(0.02f),
                },
                new AugmentDef
                {
                    Id = "Winded",
                    Title = "Winded",
                    Description = "+60% damage, but move speed drops 2% per shot taken",
                    CanOffer = manager => manager.SpeedPenaltyPerShot < 0.05f,
                    Apply = manager =>
                    {
                        manager.AddDamageMultiplier(0.6f);
                        manager.AddSpeedPenaltyPerShot(0.02f);
                    },
                },
                new AugmentDef
                {
                    Id = "Chain Reaction",
                    Title = "Chain Reaction",
                    Description = "Killed enemies explode for half the damage of the deciding hit in a 6 m radius",
                    Apply = manager => manager.AddExplosiveChain(6),
                },

                // --- synergy cards: they make other builds better ---------------------
                new AugmentDef
                {
                    Id = "Kinetic Rounds",
                    Title = "Kinetic Rounds",
                    Description = "Shooting while moving deals up to +35% more damage",
                    Apply = manager => manager.AddDamageFromSpeed(0.35f),
                },
                new AugmentDef
                {
                    Id = "Double Or Nothing",
                    Title = "Double Or Nothing",
                    Description = "Damage dealt while your crit chain is live grows +45%",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager => manager.AddDamageFromCrits(0.45f),
                },
                new AugmentDef
                {
                    Id = "Vampiric Crits",
                    Title = "Vampiric Crits",
                    Description = "Critical hits heal you for 6 health",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager => manager.AddVampiricCritHeal(6f),
                },
                new AugmentDef
                {
                    Id = "Scalpel Rounds",
                    Title = "Scalpel Rounds",
                    Description = "+25% damage on critical hits",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager => manager.AddScalpelDamage(0.25f),
                },
                new AugmentDef
                {
                    Id = "Sky Hunter",
                    Title = "Sky Hunter",
                    Description = "While airborne, shots deal +30% damage",
                    Apply = manager => manager.AddSkyHunter(),
                },
                new AugmentDef
                {
                    Id = "Kerbside Kicker",
                    Title = "Kerbside Kicker",
                    Description = "The first hit of every slide deals +35% damage",
                    Apply = manager => manager.AddSlideFirstHit(0.35f),
                },
                new AugmentDef
                {
                    Id = "Crit Combo",
                    Title = "Crit Combo",
                    Description = "Criticals refresh your slide cooldown and +20% fire rate for 2 s",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager =>
                    {
                        manager.AddCritReducesSlideCooldown(1f);
                        manager.AddCritFeedsFireRate(0.2f);
                    },
                },
                new AugmentDef
                {
                    Id = "Salvage Rounds",
                    Title = "Salvage Rounds",
                    Description = "Kills refund 10% of a magazine as reserve ammo",
                    Apply = manager => manager.AddAmmoPerKillFraction(0.1f),
                },
            };
        }
    }

    /// <summary>One augment card: a title, a description and an apply action.</summary>
    public sealed class AugmentDef
    {
        public string Id;
        public string Title;
        public string Description;

        /// <summary>Optional gate for cards that are capped out.</summary>
        public Func<UpgradeManager, bool> CanOffer;

        public Action<UpgradeManager> Apply;
    }
}
