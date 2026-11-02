using Shooter.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Shooter.UI
{
    /// <summary>
    /// Full-screen red flash when the player takes damage, fading out in real time
    /// (unscaled), so it still fades on the game-over screen where time is frozen.
    /// </summary>
    public sealed class DamageFlash : MonoBehaviour
    {
        public PlayerHealth playerHealth;
        public Image flashImage;

        [SerializeField] private float fadeSpeed = 2.2f;

        private float alpha;

        private void Start()
        {
            if (playerHealth == null)
            {
                Debug.LogWarning("[Shooter] DamageFlash has no PlayerHealth reference.");
                return;
            }

            playerHealth.Damaged += HandleDamaged;
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.Damaged -= HandleDamaged;
            }
        }

        private void HandleDamaged(float amount)
        {
            // Heavier hits flash harder, within sensible bounds.
            alpha = Mathf.Clamp(0.18f + amount / 60f, 0.18f, 0.55f);
        }

        private void Update()
        {
            if (flashImage == null)
            {
                return;
            }

            if (alpha <= 0f && flashImage.color.a <= 0f)
            {
                return;
            }

            alpha = Mathf.MoveTowards(alpha, 0f, fadeSpeed * Time.unscaledDeltaTime);

            Color color = flashImage.color;
            color.a = alpha;
            flashImage.color = color;
        }
    }
}
