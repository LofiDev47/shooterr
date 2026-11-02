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
        [SerializeField] private float respawnDelay = 20f;

        [Header("Motion")]
        [SerializeField] private float spinSpeed = 55f;
        [SerializeField] private float bobHeight = 0.15f;
        [SerializeField] private float bobSpeed = 2.2f;

        private Vector3 basePosition;
        private Renderer[] renderers;
        private float respawnAt;
        private bool collected;

        private void Awake()
        {
            basePosition = transform.position;
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Update()
        {
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
            hud?.ShowToast($"+{added} AMMO");
            SetCollected(true);
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
