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
    /// <summary>How a card speaks for itself: the colour slot it renders in.</summary>
    public enum AugmentRarity
    {
        Standard, // straight buff
        Advanced, // trade or multi-part build
        Master,   // build-defining payoff
    }

    /// <summary>Rounds-style augments: pauses after each cleared wave and offers four cards colour-coded by rarity (Standard / Advanced / Master).</summary>
    public sealed class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        [Header("Wiring (scene builder)")]
        public PlayerController player;
        public PlayerHealth playerHealth;
        public HUDController hud;

        [Header("Choice UI (scene builder)")]
        public GameObject choicePanel;
        public GameObject[] cardRoots;
        public Image[] cardBackgrounds;
        public Image[] cardAccents;
        public Image[] cardBadges;
        public Text[] cardRarities;
        public Text[] cardTitles;
        public Text[] cardDescriptions;
        public Text choiceHintText;

        public bool IsChoosing { get; private set; }

        private List<AugmentDef> offers = new List<AugmentDef>();
        private readonly Dictionary<string, int> ownedStacks = new Dictionary<string, int>();

        private static string RarityLabel(AugmentRarity rarity)
        {
            return rarity switch
            {
                AugmentRarity.Standard => "STANDARD",
                AugmentRarity.Advanced => "ADVANCED",
                AugmentRarity.Master => "MASTER",
                _ => "?",
            };
        }

        private static Color RarityColor(AugmentRarity rarity) => rarity switch
        {
            AugmentRarity.Standard => new Color(0.73f, 0.80f, 0.88f),
            AugmentRarity.Advanced => new Color(0.45f, 0.93f, 0.82f),
            AugmentRarity.Master => new Color(1.00f, 0.77f, 0.39f),
            _ => Color.white,
        };

        private static Color RarityBack(AugmentRarity rarity) => rarity switch
        {
            AugmentRarity.Standard => new Color(0.087f, 0.113f, 0.145f),
            AugmentRarity.Advanced => new Color(0.054f, 0.154f, 0.153f),
            AugmentRarity.Master => new Color(0.169f, 0.122f, 0.073f),
            _ => new Color(0.084f, 0.105f, 0.128f),
        };

        private void SetCardVisuals(int index, AugmentDef def)
        {
            Color accent = RarityColor(def.Rarity);

            if (cardBackgrounds != null && index < cardBackgrounds.Length && cardBackgrounds[index] != null)
            {
                cardBackgrounds[index].color = RarityBack(def.Rarity);
            }

            if (cardAccents != null && index < cardAccents.Length && cardAccents[index] != null)
            {
                cardAccents[index].color = accent;
            }

            if (cardBadges != null && index < cardBadges.Length && cardBadges[index] != null)
            {
                cardBadges[index].color = accent;
            }

            if (cardRarities != null && index < cardRarities.Length && cardRarities[index] != null)
            {
                cardRarities[index].text = RarityLabel(def.Rarity);
                cardRarities[index].color = accent;
            }

        }

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

        // --- weapon-mode / ballistics cards ------------------------------------------
        private bool shotgunUnlocked;
        private int ricochetBounces;
        private float bulletVelocityMultiplier = 1f;
        private float explosionRadius;
        private float explosionDamageFraction;

        private float critWindowEnd;         // last time Crit() fired
        private float baseCritChance;
        private int consecutiveCrits;
        private float fireRateBoostUntil;    // current rapid window
        private float airborneStartedAt = -1e9f;

        private Weapon weapon;
        private int airJumpsUsed;
        private bool slipstream;
        private bool siegeBreaker;
        private bool trophyHunter;
        public bool IncendiaryHits { get; private set; }
        public bool CryoHits { get; private set; }
        public bool MarkingHits { get; private set; }

        public float PriorityTargetDamageMultiplier(bool isBoss, bool isElite)
        {
            if (!siegeBreaker)
            {
                return 1f;
            }

            return isBoss ? 1.5f : isElite ? 1.2f : 1f;
        }

        // --- read-only views used by gameplay code -------------------------------
        public float DamageMultiplier => damageMultiplier;
        public float FireRateMultiplier => fireRateMultiplier;
        public float ReloadSpeedMultiplier => reloadSpeedMultiplier;
        public float MagazineMultiplier => magazineMultiplier;
        public float SpreadMultiplier => spreadMultiplier;
        public float RecoilMultiplier => recoilMultiplier;
        public float SpeedMultiplier => speedMultiplier;
        public float DashSpeedMultiplier => slipstream ? 1.10f : 1f;
        public float DashDurationMultiplier => slipstream ? 1.15f : 1f;
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
        public bool ShotgunUnlocked => shotgunUnlocked;
        public int RicochetBounces => ricochetBounces;
        public float BulletVelocityMultiplier => bulletVelocityMultiplier;
        public float ExplosionRadius => explosionRadius;
        public float ExplosionDamageFraction => explosionDamageFraction;

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

            offers = RollOffers(4);

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

            for (int i = 0; i < 4; i++)
            {
                bool present = i < offers.Count;

                if (cardRoots != null && i < cardRoots.Length && cardRoots[i] != null)
                {
                    cardRoots[i].SetActive(present);
                }

                if (present)
                {
                    SetCardVisuals(i, offers[i]);
                }

                bool childPresent = present;

                if (cardTitles != null && i < cardTitles.Length && cardTitles[i] != null)
                {
                    cardTitles[i].text = childPresent ? offers[i].Title : string.Empty;
                    cardTitles[i].gameObject.SetActive(childPresent);
                }

                if (cardDescriptions != null && i < cardDescriptions.Length && cardDescriptions[i] != null)
                {
                    cardDescriptions[i].text = childPresent ? offers[i].Description : string.Empty;
                    cardDescriptions[i].gameObject.SetActive(childPresent);
                }
            }

            if (choiceHintText != null)
            {
                string hint = "Press ";
                for (int i = 0; i < offers.Count; i++)
                {
                    hint += i > 0 ? " / " + (i + 1) : (i + 1).ToString();
                }

                choiceHintText.text = hint + " to select";
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
            else if (Keyboard.current.digit4Key.wasPressedThisFrame && offers.Count > 3)
            {
                Pick(3);
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
            slipstream = false;
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
            MovementFactor = 0f;

            // Weapon-mode / ballistics cards: forgetting these leaves a restarted
            // run with the shotgun already unlocked and rounds still ricocheting.
            shotgunUnlocked = false;
            ricochetBounces = 0;
            bulletVelocityMultiplier = 1f;
            explosionRadius = 0f;
            explosionDamageFraction = 0f;
            siegeBreaker = false;
            trophyHunter = false;
            IncendiaryHits = false;
            CryoHits = false;
            MarkingHits = false;
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

        /// <summary>
        /// How fast the player is actually moving, 0 (standing) to 1 (full sprint).
        /// Kinetic Rounds reads this instead of the speed stat, so the card pays for
        /// movement rather than for owning Track Shoes. <see cref="PlayerController"/>
        /// writes it every frame; it defaults to 0 so a missing writer cannot hand
        /// out a free damage bonus.
        /// </summary>
        public float MovementFactor { get; private set; }

        /// <summary>Reports the player's current movement, 0..1, for movement-gated cards.</summary>
        public void SetMovementFactor(float value)
        {
            MovementFactor = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Total damage multiplier at this moment (base cards + movement + crits +
        /// the per-kill adrenaline feed). Everything is read live at use time.
        /// </summary>
        public float EffectiveDamageMultiplier =>
            damageMultiplier *
            (1f + damageFromSpeed * MovementFactor) *
            (1f + AdrenalineDamagePerKill) *
            (1f + (consecutiveCrits > 0 && Time.time <= critWindowEnd ? damageFromCrits : 0f));

        /// <summary>Damage multiplier applied on a critical hit (Scalpel Rounds included).</summary>
        public float CriticalDamageMultiplier => critMultiplier * (1f + scalpelDamagePerCrit);

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

        /// <summary>Damage bonus for shots fired after 0.5 s of real airtime.</summary>
        public float AirborneFirstHitDamageMultiplier =>
            skyHunter ? SkyHunterBonus(Time.time, airborneStartedAt, airborneDamageMultiplier) : 0f;

        /// <summary>Pure airborne-window check, exposed for headless tests.</summary>
        public static float SkyHunterBonus(float now, float airborneStartedAt, float multiplier) =>
            airborneStartedAt < -1e8f || now - airborneStartedAt < 0.5f ? 0f : multiplier;

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

        /// <summary>Breach Tube: permanently enables the underbarrel shotgun.</summary>
        public void UnlockShotgun() => shotgunUnlocked = true;

        /// <summary>Ricochet Rounds: one extra wall bounce per stack, capped at two.</summary>
        public void AddRicochetBounce() => ricochetBounces = Mathf.Min(2, ricochetBounces + 1);

        /// <summary>High-Velocity Rounds: increases the range/damage-retention multiplier.</summary>
        public void AddBulletVelocity(float amount) =>
            bulletVelocityMultiplier = Mathf.Min(1.7f, bulletVelocityMultiplier + amount);

        /// <summary>Explosive Rounds: adds blast radius and splash fraction, capped.</summary>
        public void AddBulletExplosion(float radius, float fraction)
        {
            explosionRadius = Mathf.Min(2.4f, explosionRadius + radius);
            explosionDamageFraction = Mathf.Min(0.35f, explosionDamageFraction + fraction);
        }

        public void AddReserveBonus(int amount)
        {
            reserveBonus += amount;
        }

        public void AddLifesteal(float amount) => lifestealPerKill += amount;

        // ---------------------------------------------------------------- hooks

        /// <summary>Called by enemy deaths, applies lifesteal heals and kill-based synergies.</summary>
        public void OnKill(bool isBoss = false, bool isElite = false)
        {
            if (playerHealth != null && !playerHealth.IsDead)
            {
                if (lifestealPerKill > 0f)
                {
                    playerHealth.Heal(lifestealPerKill);
                }

                if (trophyHunter && (isBoss || isElite))
                {
                    playerHealth.Heal(playerHealth.MaxHealth * (isBoss ? 0.30f : 0.10f));
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

            // Crit Combo: the slide reset and the rapid-fire window are independent
            // effects. The fire-rate window must not depend on a player reference.
            if (critReducesSlideCooldown > 0f)
            {
                player?.ResetSlideCooldown();
            }

            if (critFeedsFireRate > 0f)
            {
                fireRateBoostUntil = Time.time + 2f;
            }
        }

        /// <summary>Floor for Winded's per-hit speed drain (30% of base speed).</summary>
        public const float MinSpeedMultiplier = 0.3f;

        /// <summary>Called on the post-Move frame ground contact is lost.</summary>
        public void OnJumpPressed()
        {
            airborneStartedAt = Time.time;
        }

        /// <summary>Called on the post-Move frame the player touches down.</summary>
        public void OnLanded()
        {
            airborneStartedAt = -1e9f;
        }

        /// <summary>
        /// Winded interlock: each accepted hit drains a small fixed amount of speed,
        /// floored so the player never grinds to a halt. No weapon rebuild is needed
        /// here — speed is sampled live and none of the rebuilt stats depend on it.
        /// </summary>
        public void TakeSpeedPenalty()
        {
            if (speedPenaltyPerShot <= 0f || speedMultiplier <= MinSpeedMultiplier)
            {
                return;
            }

            speedMultiplier = Mathf.Max(MinSpeedMultiplier, speedMultiplier * (1f - speedPenaltyPerShot));
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

            // Make the new underbarrel mode discoverable: until it is taken it is
            // guaranteed to appear in every offer (the player can still pick another).
            if (!shotgunUnlocked)
            {
                AugmentDef breach = pool.Find(def => def.Id == "Breach Tube");

                if (breach != null)
                {
                    picked.Add(breach);
                    pool.Remove(breach);
                }
            }

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
                    Rarity = AugmentRarity.Standard,
                    Title = "Heavy Rounds",
                    Description = "+30% weapon damage",
                    Apply = manager => manager.AddDamageMultiplier(0.3f),
                },
                new AugmentDef
                {
                    Id = "Trigger Finger",
                    Rarity = AugmentRarity.Standard,
                    Title = "Trigger Finger",
                    Description = "+25% fire rate",
                    Apply = manager => manager.AddFireRateMultiplier(0.25f),
                },
                new AugmentDef
                {
                    Id = "Speed Loader",
                    Rarity = AugmentRarity.Standard,
                    Title = "Rapid Recharge",
                    Description = "Recharges are 35% faster",
                    Apply = manager => manager.AddReloadSpeedMultiplier(0.35f),
                },
                new AugmentDef
                {
                    Id = "Big Magazine",
                    Rarity = AugmentRarity.Standard,
                    Title = "Expanded Power Cell",
                    Description = "Power cell holds 50% more energy",
                    Apply = manager => manager.AddMagazineMultiplier(0.5f),
                },
                new AugmentDef
                {
                    Id = "Deep Pockets",
                    Rarity = AugmentRarity.Standard,
                    Title = "Deep Pockets",
                    Description = "+120 reserve energy and capacity",
                    Apply = manager => manager.AddReserveBonus(120),
                },
                new AugmentDef
                {
                    Id = "Steady Hands",
                    Rarity = AugmentRarity.Standard,
                    Title = "Steady Hands",
                    Description = "40% tighter spread",
                    Apply = manager => manager.ImproveSpread(0.4f),
                },
                new AugmentDef
                {
                    Id = "Tamed Recoil",
                    Rarity = AugmentRarity.Standard,
                    Title = "Tamed Recoil",
                    Description = "35% less recoil",
                    Apply = manager => manager.ReduceRecoil(0.35f),
                },
                new AugmentDef
                {
                    Id = "Track Shoes",
                    Rarity = AugmentRarity.Standard,
                    Title = "Track Shoes",
                    Description = "+20% move speed",
                    Apply = manager => manager.AddSpeedMultiplier(0.2f),
                },
                new AugmentDef
                {
                    Id = "Moon Legs",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Moon Legs",
                    Description = "Jump 25% higher",
                    Apply = manager => manager.AddJumpMultiplier(0.25f),
                },
                new AugmentDef
                {
                    Id = "Titan Frame",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Titan Frame",
                    Description = "+40 max health and heal 40",
                    Apply = manager => manager.AddMaxHealth(40f),
                },
                new AugmentDef
                {
                    Id = "Nanite Repair",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Nanite Repair",
                    Description = "Regenerate 2 health per second",
                    Apply = manager => manager.AddRegen(2f),
                },
                new AugmentDef
                {
                    Id = "Blood Rush",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Blood Rush",
                    Description = "Heal 3 health on every kill",
                    Apply = manager => manager.AddLifesteal(3f),
                },
                new AugmentDef
                {
                    Id = "Double Jump",
                    Rarity = AugmentRarity.Master,
                    Title = "Double Jump",
                    Description = "One extra jump while airborne",
                    CanOffer = manager => manager.ExtraAirJumps < 1,
                    Apply = manager => manager.AddExtraAirJump(),
                },
                new AugmentDef
                {
                    Id = "Piercing Rounds",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Piercing Bolts",
                    Description = "Shots pierce 1 extra enemy",
                    CanOffer = manager => manager.BonusPierce < 3,
                    Apply = manager => manager.AddBonusPierce(1),
                },
                new AugmentDef
                {
                    Id = "Ricochet Rounds",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Ricochet Bolts",
                    Description = "Rifle bolts bounce off walls once for 70% damage. Stacks twice.",
                    CanOffer = manager => manager.RicochetBounces < 2,
                    Apply = manager => manager.AddRicochetBounce(),
                },
                new AugmentDef
                {
                    Id = "High-Velocity Rounds",
                    Rarity = AugmentRarity.Advanced,
                    Title = "High-Velocity Bolts",
                    Description = "+35% rifle velocity: full damage carries much farther",
                    CanOffer = manager => manager.BulletVelocityMultiplier < 1.7f,
                    Apply = manager => manager.AddBulletVelocity(0.35f),
                },
                new AugmentDef
                {
                    Id = "Explosive Rounds",
                    Rarity = AugmentRarity.Master,
                    Title = "Explosive Bolts",
                    Description = "Rifle bolts detonate on impact for 35% damage in a 2.4 m radius",
                    CanOffer = manager => manager.ExplosionRadius < 2.4f,
                    Apply = manager => manager.AddBulletExplosion(2.4f, 0.35f),
                },
                new AugmentDef
                {
                    Id = "Breach Tube",
                    Rarity = AugmentRarity.Master,
                    Title = "Breach Tube",
                    Description = "Mount an underbarrel energy shotgun with four charges. Uses the Shotgun binding.",
                    CanOffer = manager => !manager.ShotgunUnlocked,
                    Apply = manager => manager.UnlockShotgun(),
                },
                new AugmentDef
                {
                    Id = "Critical Strike",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Critical Strike",
                    Description = "20% chance to deal double damage",
                    Apply = manager => manager.AddCrit(0.2f, 2f),
                },
                new AugmentDef
                {
                    Id = "Greased Soles",
                    Rarity = AugmentRarity.Advanced,
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
                    Rarity = AugmentRarity.Advanced,
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
                    Rarity = AugmentRarity.Advanced,
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
                    Rarity = AugmentRarity.Advanced,
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
                    Rarity = AugmentRarity.Advanced,
                    Title = "Kevlar Weave",
                    Description = "Take 15% less damage",
                    CanOffer = manager => manager.DamageReduction < 0.45f,
                    Apply = manager => manager.AddDamageReduction(0.15f),
                },
                new AugmentDef
                {
                    Id = "Adrenaline Surge",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Adrenaline Surge",
                    Description = "+40% metal: gain 2% damage per kill, up to 16%",
                    CanOffer = manager => manager.AdrenalineDamagePerKill < 0.16f,
                    Apply = manager => manager.AddAdrenalineDamage(0.02f),
                },
                new AugmentDef
                {
                    Id = "Winded",
                    Rarity = AugmentRarity.Master,
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
                    Rarity = AugmentRarity.Master,
                    Title = "Chain Reaction",
                    Description = "Killed enemies explode for half the damage of the deciding hit in a 6 m radius",
                    Apply = manager => manager.AddExplosiveChain(6),
                },

                // --- synergy cards: they make other builds better ---------------------
                new AugmentDef
                {
                    Id = "Kinetic Rounds",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Kinetic Rounds",
                    Description = "Shooting while moving deals up to +35% more damage",
                    Apply = manager => manager.AddDamageFromSpeed(0.35f),
                },
                new AugmentDef
                {
                    Id = "Double Or Nothing",
                    Rarity = AugmentRarity.Master,
                    Title = "Double Or Nothing",
                    Description = "Damage dealt while your crit chain is live grows +45%",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager => manager.AddDamageFromCrits(0.45f),
                },
                new AugmentDef
                {
                    Id = "Vampiric Crits",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Vampiric Crits",
                    Description = "Critical hits heal you for 6 health",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager => manager.AddVampiricCritHeal(6f),
                },
                new AugmentDef
                {
                    Id = "Scalpel Rounds",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Scalpel Rounds",
                    Description = "+25% damage on critical hits",
                    CanOffer = manager => manager.CritChance > 0f || manager.ConsecutiveCrits > 0,
                    Apply = manager => manager.AddScalpelDamage(0.25f),
                },
                new AugmentDef
                {
                    Id = "Sky Hunter",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Sky Hunter",
                    Description = "While airborne, shots deal +30% damage",
                    Apply = manager => manager.AddSkyHunter(),
                },
                new AugmentDef
                {
                    Id = "Kerbside Kicker",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Kerbside Kicker",
                    Description = "The first hit of every slide deals +35% damage",
                    Apply = manager => manager.AddSlideFirstHit(0.35f),
                },
                new AugmentDef
                {
                    Id = "Crit Combo",
                    Rarity = AugmentRarity.Master,
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
                    Rarity = AugmentRarity.Advanced,
                    Title = "Energy Recovery",
                    Description = "Kills return 10% of a full power cell to reserve energy",
                    Apply = manager => manager.AddAmmoPerKillFraction(0.1f),
                },
                new AugmentDef
                {
                    Id = "Slipstream",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Slipstream",
                    Description = "Air dash 10% faster and for 15% longer",
                    CanOffer = manager => !manager.slipstream,
                    Apply = manager => manager.slipstream = true,
                },
                new AugmentDef
                {
                    Id = "Runner's Plating",
                    Rarity = AugmentRarity.Standard,
                    Title = "Runner's Plating",
                    Description = "Move 10% faster and gain 8% damage reduction",
                    CanOffer = manager => !manager.ownedStacks.ContainsKey("Runner's Plating"),
                    Apply = manager =>
                    {
                        manager.AddSpeedMultiplier(0.10f);
                        manager.AddDamageReduction(0.08f);
                    },
                },
                new AugmentDef
                {
                    Id = "Second Wind",
                    Rarity = AugmentRarity.Master,
                    Title = "Second Wind",
                    Description = "Gain an air jump and shorten slide cooldown by 25%",
                    CanOffer = manager => !manager.ownedStacks.ContainsKey("Second Wind"),
                    Apply = manager =>
                    {
                        manager.AddExtraAirJump();
                        manager.ReduceSlideCooldown(0.25f);
                    },
                },
                new AugmentDef
                {
                    Id = "Field Medic",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Field Medic",
                    Description = "+20 max health and regenerate 1.5 health per second",
                    CanOffer = manager => !manager.ownedStacks.ContainsKey("Field Medic"),
                    Apply = manager =>
                    {
                        manager.AddMaxHealth(20f);
                        manager.AddRegen(1.5f);
                    },
                },
                new AugmentDef
                {
                    Id = "Siege Breaker",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Siege Breaker",
                    Description = "+50% direct damage to bosses and +20% to elites",
                    CanOffer = manager => !manager.siegeBreaker,
                    Apply = manager => manager.siegeBreaker = true,
                },
                new AugmentDef
                {
                    Id = "Trophy Hunter",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Trophy Hunter",
                    Description = "Recover 30% max health on boss kills and 10% on elite kills",
                    CanOffer = manager => !manager.trophyHunter,
                    Apply = manager => manager.trophyHunter = true,
                },
                new AugmentDef
                {
                    Id = "Incendiary Rounds",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Incendiary Rounds",
                    Description = "Direct hits burn for 20 damage per second for 2s. Hits refresh the burn.",
                    CanOffer = manager => !manager.IncendiaryHits,
                    Apply = manager => manager.IncendiaryHits = true,
                },
                new AugmentDef
                {
                    Id = "Cryo Coating",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Cryo Coating",
                    Description = "Direct hits slow movement by 25% for 2s (10% on bosses)",
                    CanOffer = manager => !manager.CryoHits,
                    Apply = manager => manager.CryoHits = true,
                },
                new AugmentDef
                {
                    Id = "Target Paint",
                    Rarity = AugmentRarity.Advanced,
                    Title = "Target Paint",
                    Description = "Direct hits mark enemies for 2s. Follow-up direct hits deal 18% more damage.",
                    CanOffer = manager => !manager.MarkingHits,
                    Apply = manager => manager.MarkingHits = true,
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

        /// <summary>Power tier: colours the badge, title and body of the card.</summary>
        public AugmentRarity Rarity;

        /// <summary>Optional gate for cards that are capped out.</summary>
        public Func<UpgradeManager, bool> CanOffer;

        public Action<UpgradeManager> Apply;
    }
}
