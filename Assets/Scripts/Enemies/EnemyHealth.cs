using System;
using Shooter.Combat;
using Shooter.Effects;
using Shooter.Managers;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Health, hit feedback and death handling for a capsule enemy.
    /// Implements <see cref="IDamageable"/> so the weapon can damage it without knowing its type.
    /// </summary>
    public sealed class EnemyHealth : MonoBehaviour, IDamageable
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

        /// <summary>Raised once when the enemy dies, used by the wave spawner to count survivors.</summary>
        public event Action<EnemyHealth> Died;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer[] renderers;
        private MaterialPropertyBlock propertyBlock;
        private float flashUntil;
        private float deathTimer = -1f;
        private Vector3 deathScale;
        private Collider bodyCollider;
        private Color tintColor = Color.white;
        private bool hasTint;

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
        public void Initialize(float healthMultiplier, float scale = 1f, Color? tint = null)
        {
            CacheComponents();

            MaxHealth = baseMaxHealth * Mathf.Max(0.05f, healthMultiplier);
            CurrentHealth = MaxHealth;
            IsDead = false;
            deathTimer = -1f;
            transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

            hasTint = tint.HasValue;
            tintColor = tint ?? Color.white;
            ApplyTint();

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

        /// <inheritdoc />
        public void TakeDamage(float amount, Vector3 hitPoint)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            Flash();
            AudioManager.Instance?.PlayEnemyHit(hitPoint);

            if (CurrentHealth <= 0f)
            {
                Die();
            }
        }

        private void Update()
        {
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
            deathTimer = 0f;

            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            AudioManager.Instance?.PlayEnemyDeath(transform.position);

            int pieces = Mathf.RoundToInt(debrisPerScale * Mathf.Max(1f, deathScale.y));
            DebrisBurst.Spawn(transform.position + Vector3.up * deathScale.y * 0.9f, Vector3.up,
                debrisMaterial, pieces, 4.5f, 0.11f, 1.2f);

            GameManager.Instance?.AddScore(scoreValue);
            Died?.Invoke(this);
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

            // Elites keep their tint; plain enemies fall back to the shared material colour.
            if (hasTint)
            {
                ApplyTint();
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

        private void ApplyTint()
        {
            if (!hasTint || renderers == null || renderers.Length == 0)
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
                propertyBlock.SetColor(BaseColorId, tintColor);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void CacheComponents()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>();
            }

            if (bodyCollider == null)
            {
                bodyCollider = GetComponent<Collider>();
            }
        }
    }
}
