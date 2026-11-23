using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using UnityEngine;

namespace Shooter.Weapons
{
    /// <summary>
    /// Spinning, bobbing ammo crate. Walking into it adds reserve ammo, then it disappears
    /// for a while and respawns. Proximity is a plain distance check rather than a trigger,
    /// so it cannot be broken by trigger/rigidbody setup; the crate also has no collider and
    /// therefore never blocks movement.
    /// </summary>
    public sealed class AmmoPickup : MonoBehaviour
    {
        [Header("Wiring (scene builder)")]
        public Weapon weapon;
        public PlayerController player;
        public HUDController hud;

        [Header("Ammo")]
        [SerializeField] private int ammoAmount = 45;
        [SerializeField] private float pickupRadius = 1.5f;

        [Tooltip("Seconds before the crate comes back. Zero means it is used up for good.")]
        [SerializeField] private float respawnDelay = 20f;

        [Header("Motion")]
        [SerializeField] private float spinSpeed = 55f;
        [SerializeField] private float bobHeight = 0.15f;
        [SerializeField] private float bobSpeed = 2.2f;

        private Vector3 basePosition;
        private bool basePositionCaptured;
        private Renderer[] renderers;
        private float respawnAt;
        private bool collected;
        private bool referencesResolved;

        /// <summary>Rounds added when the pickup is collected. Can be tuned per drop.</summary>
        public int AmmoAmount
        {
            get => ammoAmount;
            set => ammoAmount = Mathf.Max(0, value);
        }

        /// <summary>
        /// Sets up a pickup. Used by the scene builder for the arena crates and by the
        /// wave spawner for the caches elites drop.
        /// </summary>
        /// <param name="amount">Rounds handed over on pickup.</param>
        /// <param name="radius">Pickup radius in metres.</param>
        /// <param name="respawn">Seconds before it returns. Zero means one-shot.</param>
        /// <param name="spin">Degrees per second of spin.</param>
        /// <param name="bob">Bob height in metres.</param>
        public void Configure(int amount, float radius, float respawn, float spin = 55f, float bob = 0.15f)
        {
            ammoAmount = Mathf.Max(0, amount);
            pickupRadius = Mathf.Max(0.1f, radius);
            respawnDelay = Mathf.Max(0f, respawn);
            spinSpeed = spin;
            bobHeight = bob;
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Update()
        {
            // Captured lazily so pickups placed after Awake (elite caches) keep
            // their real spawn position instead of the prefab origin.
            if (!basePositionCaptured)
            {
                basePosition = transform.position;
                basePositionCaptured = true;
            }

            float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = basePosition + Vector3.up * bob;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            if (collected)
            {
                if (Time.time >= respawnAt)
                {
                    SetCollected(false);
                }

                return;
            }

            ResolveReferences();

            if (weapon == null || player == null)
            {
                return;
            }

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            // Leave the crate in place while the reserve is full.
            if (!weapon.CanReceiveAmmo)
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
            int added = weapon.AddAmmo(ammoAmount);

            if (added <= 0)
            {
                return;
            }

            AudioManager.Instance?.PlayAmmoPickup(transform.position);
            hud?.ShowToast($"+{added} ENERGY");

            if (respawnDelay <= 0f)
            {
                // One-shot drop (an elite's cache): take it and it is gone.
                Destroy(gameObject);
                return;
            }

            SetCollected(true);
        }

        /// <summary>
        /// Finds the weapon, player and HUD when the pickup was spawned at runtime and
        /// therefore never got scene-builder wiring.
        /// </summary>
        private void ResolveReferences()
        {
            if (referencesResolved)
            {
                return;
            }

            if (weapon == null)
            {
                weapon = FindAnyObjectByType<Weapon>();
            }

            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
            }

            if (hud == null)
            {
                hud = FindAnyObjectByType<HUDController>();
            }

            referencesResolved = weapon != null && player != null;
        }

        private void SetCollected(bool value)
        {
            collected = value;

            if (value)
            {
                respawnAt = Time.time + respawnDelay;
            }

            if (renderers == null)
            {
                return;
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = !value;
                }
            }
        }
    }
}
