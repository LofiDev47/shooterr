using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using UnityEngine;

namespace Shooter.Weapons
{
    /// <summary>
    /// Spinning medikit that heals the player. Dropped by bosses, elites and
    /// randomly by regular kills, so health is a reward for aggression instead
    /// of a fixed regen. Mirrors <see cref="AmmoPickup"/> so both pickups share
    /// the same bob/spin/pickup feel.
    /// </summary>
    public sealed class HealthPickup : MonoBehaviour
    {
        [Header("Wiring (scene builder)")]
        public PlayerHealth playerHealth;
        public PlayerController player;
        public HUDController hud;

        [Header("Heal")]
        [SerializeField] private float healAmount = 25f;
        [SerializeField] private float pickupRadius = 1.6f;

        [Header("Motion")]
        [SerializeField] private float spinSpeed = 70f;
        [SerializeField] private float bobHeight = 0.12f;
        [SerializeField] private float bobSpeed = 2.4f;

        private Vector3 basePosition;

        /// <summary>Health restored when the pickup is collected.</summary>
        public float HealAmount
        {
            get => healAmount;
            set => healAmount = Mathf.Max(0f, value);
        }

        public void Configure(float heal, float radius = 1.6f, float spin = 70f, float bob = 0.12f)
        {
            healAmount = Mathf.Max(0f, heal);
            pickupRadius = Mathf.Max(0.1f, radius);
            spinSpeed = spin;
            bobHeight = bob;
        }

        private void Awake()
        {
            basePosition = transform.position;
        }

        private void Update()
        {
            float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = basePosition + Vector3.up * bob;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            ResolveReferences();

            if (playerHealth == null || player == null)
            {
                return;
            }

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            // Leave it on the ground at full health instead of wasting it.
            if (playerHealth.CurrentHealth >= playerHealth.MaxHealth)
            {
                return;
            }

            Vector3 delta = player.transform.position - transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude <= pickupRadius * pickupRadius)
            {
                Collect();
            }
        }

        private void Collect()
        {
            float before = playerHealth.CurrentHealth;
            playerHealth.Heal(healAmount);
            float healed = playerHealth.CurrentHealth - before;

            if (healed <= 0f)
            {
                return;
            }

            AudioManager.Instance?.PlayAmmoPickup(transform.position);
            hud?.ShowToast($"+{Mathf.RoundToInt(healed)} HP");

            // One-shot: medikits never respawn.
            Destroy(gameObject);
        }

        private void ResolveReferences()
        {
            if (playerHealth != null && player != null && hud != null)
            {
                return;
            }

            if (playerHealth == null)
            {
                playerHealth = FindAnyObjectByType<PlayerHealth>();
            }

            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
            }

            if (hud == null)
            {
                hud = FindAnyObjectByType<HUDController>();
            }
        }
    }
}
