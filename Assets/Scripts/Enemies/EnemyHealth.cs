using System;
using Shooter.Combat;
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

        private void Awake()
        {
            CacheComponents();

            MaxHealth = baseMaxHealth;
            CurrentHealth = MaxHealth;
        }

        /// <summary>
        /// Applies per-wave scaling. Called by the spawner right after instantiation.
        /// </summary>
        public void Initialize(float healthMultiplier)
        {
            CacheComponents();

            MaxHealth = baseMaxHealth * Mathf.Max(0.05f, healthMultiplier);
            CurrentHealth = MaxHealth;
            IsDead = false;
            deathTimer = -1f;
            transform.localScale = Vector3.one;

            if (bodyCollider != null)
            {
                bodyCollider.enabled = true;
            }
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
