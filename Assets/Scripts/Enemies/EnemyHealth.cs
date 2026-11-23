using System;
using System.Linq;
using Shooter.Combat;
using Shooter.Effects;
using Shooter.Managers;
using Shooter.Upgrades;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Health, hit feedback and death handling for a capsule enemy.
    /// Implements <see cref="IDamageable"/> so the weapon can damage it without knowing its type.
    /// </summary>
    public sealed class EnemyHealth : MonoBehaviour, IDamageable, IDamageScale
    {
        [SerializeField] private float baseMaxHealth = 60f;
        [SerializeField] private int scoreValue = 10;
        [SerializeField] private float flashDuration = 0.08f;
        [SerializeField] private float deathDuration = 0.35f;

        [Header("Effects (wired by the scene builder)")]
        public Material debrisMaterial;

        [Tooltip("Debris pieces spawned on death, per unit of scale.")]
        [SerializeField] private int debrisPerScale = 10;

        public float MaxHealth { get; private set; }
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        /// <summary>True for elite enemies, which drop an ammo cache when they die.</summary>
        public bool IsElite { get; private set; }

        /// <summary>True for boss enemies (every 5th wave), which drop a cache like elites.</summary>
        public bool IsBoss { get; private set; }

        /// <summary>
        /// Multiplier applied to every incoming hit. Bosses use it for the
        /// exposed-core window (2). Never below a sliver, so a zero cannot become
        /// a heal and damage still reads. Immunity is <see cref="IsInvulnerable"/>,
        /// not a near-zero scale: a scaled hit would still flash the model, play
        /// audio and raise <see cref="Damaged"/>.
        /// </summary>
        public float IncomingDamageScale
        {
            get => incomingDamageScale;
            set => incomingDamageScale = Mathf.Max(0.001f, value);
        }

        /// <summary>
        /// Hard immunity, used by boss phase roars. A hit is discarded before any
        /// health, flash, audio or <see cref="Damaged"/> feedback is produced.
        /// </summary>
        public bool IsInvulnerable { get; private set; }

        private float incomingDamageScale = 1f;
        private float holdDeathSeconds;

        /// <summary>Raised once when the enemy dies, used by the wave spawner to count survivors.</summary>
        public event Action<EnemyHealth> Died;

        /// <summary>(damage, hitPoint) on every accepted hit, used by the AI to flinch.</summary>
        public event Action<float, Vector3> Damaged;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer[] renderers;
        private MaterialPropertyBlock propertyBlock;
        private float flashUntil;
        private float deathTimer = -1f;
        private Vector3 deathScale;
        private Collider bodyCollider;
        private Color tintColor = Color.white;
        private bool hasTint;

        private const float StatusDuration = 2f;
        private const float BurnTickInterval = 0.5f;
        private const float BurnTickDamage = 10f;
        private float burnUntil;
        private float nextBurnTick;
        private float chillUntil;
        private float markUntil;

        public float MovementStatusMultiplier =>
            Time.time < chillUntil ? (IsBoss ? 0.90f : 0.75f) : 1f;

        private void Awake()
        {
            CacheComponents();

            MaxHealth = baseMaxHealth;
            CurrentHealth = MaxHealth;
        }

        /// <summary>
        /// Applies per-wave scaling, plus optional elite size and colour.
        /// Called by the spawner right after instantiation.
        /// </summary>
        /// <param name="healthMultiplier">Per-wave health scaling.</param>
        /// <param name="scale">Uniform size multiplier (CharacterController scales with it).</param>
        /// <param name="tint">Optional body colour override used to mark elites.</param>
        /// <param name="isElite">True for elites, which drop an ammo cache on death.</param>
        public void Initialize(float healthMultiplier, float scale = 1f, Color? tint = null, bool isElite = false, bool isBoss = false)
        {
            CacheComponents();

            MaxHealth = baseMaxHealth * Mathf.Max(0.05f, healthMultiplier);
            CurrentHealth = MaxHealth;
            IsDead = false;
            IsElite = isElite;
            IsBoss = isBoss;
            deathTimer = -1f;
            incomingDamageScale = 1f;
            IsInvulnerable = false;
            holdDeathSeconds = 0f;
            burnUntil = nextBurnTick = chillUntil = markUntil = 0f;
            flashUntil = 0f;
            transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

            hasTint = tint.HasValue;
            tintColor = tint ?? Color.white;
            ClearFlash();

            if (bodyCollider != null)
            {
                bodyCollider.enabled = true;
            }
        }

        /// <summary>Overrides the score awarded for killing this enemy.</summary>
        public void SetScoreValue(int value)
        {
            scoreValue = Mathf.Max(0, value);
        }

        /// <summary>
        /// Keeps the corpse in place for <paramref name="seconds"/> after death
        /// instead of shrinking away. The boss uses it so its authored death clip
        /// plays to the end. Zero restores the normal shrink.
        /// </summary>
        public void SetDeathHold(float seconds)
        {
            holdDeathSeconds = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Turns hard immunity on or off. While on, hits are discarded silently,
        /// which is what a boss phase roar should look like.
        /// </summary>
        public void SetInvulnerable(bool value)
        {
            IsInvulnerable = value;
        }

        /// <inheritdoc />
        public void TakeDamage(float amount, Vector3 hitPoint)
        {
            ApplyDamage(amount, hitPoint, true);
        }

        /// <summary>Applies direct weapon damage and returns the amount for hit feedback.</summary>
        public float TakeWeaponHit(float amount, Vector3 hitPoint, UpgradeManager upgrades)
        {
            float now = Time.time;
            amount *= upgrades != null ? upgrades.PriorityTargetDamageMultiplier(IsBoss, IsElite) : 1f;

            if (now < markUntil)
            {
                amount *= 1.18f;
            }

            float dealt = ApplyDamage(amount, hitPoint, true);
            if (dealt <= 0f || IsDead || upgrades == null)
            {
                return dealt;
            }

            if (upgrades.IncendiaryHits)
            {
                if (burnUntil <= now)
                {
                    nextBurnTick = now + BurnTickInterval;
                }

                burnUntil = now + StatusDuration;
            }

            if (upgrades.CryoHits)
            {
                chillUntil = now + StatusDuration;
            }

            if (upgrades.MarkingHits)
            {
                markUntil = now + StatusDuration;
            }

            return dealt;
        }

        private float ApplyDamage(float amount, Vector3 hitPoint, bool directFeedback)
        {
            if (IsDead || IsInvulnerable || amount <= 0f)
            {
                return 0f;
            }

            amount *= incomingDamageScale;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            lastHitDamage = amount;

            if (directFeedback)
            {
                Flash();
                AudioManager.Instance?.PlayEnemyHit(hitPoint);
                Damaged?.Invoke(amount, hitPoint);
            }

            if (CurrentHealth <= 0f)
            {
                Die();
            }

            return amount;
        }

        private void UpdateStatuses(float now)
        {
            if (IsDead || (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            {
                return;
            }

            while (burnUntil > 0f && nextBurnTick <= burnUntil && nextBurnTick <= now && !IsDead)
            {
                nextBurnTick += BurnTickInterval;
                ApplyDamage(BurnTickDamage, transform.position, false);
            }

            bool expired = ExpireStatus(ref burnUntil, now);
            expired |= ExpireStatus(ref chillUntil, now);
            expired |= ExpireStatus(ref markUntil, now);

            if (expired && flashUntil == 0f && !IsDead)
            {
                ClearFlash();
            }
        }

        private static bool ExpireStatus(ref float until, float now)
        {
            if (until <= 0f || now < until)
            {
                return false;
            }

            until = 0f;
            return true;
        }

        private void Update()
        {
            UpdateStatuses(Time.time);

            if (flashUntil > 0f && Time.time >= flashUntil)
            {
                ClearFlash();
            }

            if (deathTimer >= 0f)
            {
                deathTimer += Time.deltaTime;
                float t = Mathf.Clamp01(deathTimer / deathDuration);
                transform.localScale = Vector3.Lerp(deathScale, Vector3.zero, t);

                if (t >= 1f)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void Die()
        {
            IsDead = true;
            deathScale = transform.localScale;
            if (TryGetComponent(out EnemyAnimator animator))
                animator.PlayDeath(deathDuration);

            // A held corpse keeps its pose: deathTimer stays negative so Update
            // never shrinks it, and the object removes itself after the hold.
            if (holdDeathSeconds > 0f)
            {
                deathTimer = -1f;
                Destroy(gameObject, holdDeathSeconds);
            }
            else
            {
                deathTimer = 0f;
            }

            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            AudioManager.Instance?.PlayEnemyDeath(transform.position);

            int pieces = Mathf.RoundToInt(debrisPerScale * Mathf.Max(1f, deathScale.y));
            DebrisBurst.Spawn(transform.position + Vector3.up * deathScale.y * 0.9f, Vector3.up,
                debrisMaterial, pieces, 4.5f, 0.11f, 1.2f);

            // Kill-chain score plus a short freeze so the kill lands with weight.
            GameManager.Instance?.AddKillScore(scoreValue);
            GameManager.Instance?.RequestKillHitStop();

            // Lifesteal augments heal off every confirmed kill.
            UpgradeManager.Instance?.OnKill(IsBoss, IsElite);

            ChainExplode(lastHitDamage);

            Died?.Invoke(this);
        }

        private float lastHitDamage;

        /// <summary>
        /// Chain Reaction card: a kill detonates nearby enemies. The damage is half
        /// the damage that landed the deciding hit, so critting through a wall of
        /// enemies punches a real hole. Runs once per enemy, so chains terminate.
        /// </summary>
        private void ChainExplode(float triggeringDamage)
        {
            UpgradeManager upgrades = UpgradeManager.Instance;

            if (upgrades == null || upgrades.ExplosiveRadius <= 0)
            {
                return;
            }

            float radius = upgrades.ExplosiveRadius;
            float chainDamage = triggeringDamage * 0.5f;

            if (chainDamage <= 0f)
            {
                return;
            }

            foreach (EnemyHealth other in FindObjectsByType<EnemyHealth>())
            {
                if (other == null || other.IsDead || other == this)
                {
                    continue;
                }

                if (Vector3.Distance(transform.position, other.transform.position) <= radius)
                {
                    other.TakeDamage(chainDamage, transform.position);
                }
            }
        }

        private void Flash()
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, Color.white);
                renderer.SetPropertyBlock(propertyBlock);
            }

            flashUntil = Time.time + flashDuration;
        }

        private void ClearFlash()
        {
            flashUntil = 0f;

            float now = Time.time;
            bool burning = !IsDead && now < burnUntil;
            bool chilled = !IsDead && now < chillUntil;
            bool marked = !IsDead && now < markUntil;

            if (burning || chilled || marked || hasTint)
            {
                Color color = tintColor;
                if (marked)
                {
                    color = Color.Lerp(color, new Color(0.85f, 0.25f, 1f), 0.55f);
                }
                if (chilled)
                {
                    color = Color.Lerp(color, new Color(0.15f, 0.75f, 1f), 0.55f);
                }
                if (burning)
                {
                    color = Color.Lerp(color, new Color(1f, 0.35f, 0.08f), 0.55f);
                }

                ApplyTint(color);
                return;
            }

            if (renderers == null)
            {
                return;
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(null);
                }
            }
        }

        private void ApplyTint(Color color)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private static readonly string[] GlowMarkers =
        {
            "Eye", "Visor", "Glow", "Lens", "Core", "Rune",
        };

        private void CacheComponents()
        {
            if (renderers == null || renderers.Length == 0)
            {
                // Emissive parts keep their own colour: they are excluded from hit
                // flashes and tints so every variant always shows the same bright eye.
                renderers = GetComponentsInChildren<Renderer>();
                renderers = renderers
                    .Where(renderer => renderer != null &&
                        !GlowMarkers.Any(marker => renderer.name.Contains(marker)))
                    .ToArray();
            }

            if (bodyCollider == null)
            {
                bodyCollider = GetComponent<Collider>();
            }
        }
    }
}
