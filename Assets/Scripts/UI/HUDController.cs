using Shooter.Managers;
using Shooter.Player;
using Shooter.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Shooter.UI
{
    /// <summary>
    /// Pulls state from the player, weapon, wave spawner and game manager each frame
    /// and refreshes only the labels whose values actually changed. A pull model keeps
    /// the HUD stateless: there are no event-ordering problems after a scene reload.
    /// </summary>
    public sealed class HUDController : MonoBehaviour
    {
        [Header("Gameplay wiring (scene builder)")]
        public PlayerHealth playerHealth;
        public PlayerController player;
        public Weapon weapon;
        public GameManager gameManager;
        public WaveSpawner waveSpawner;

        [Header("HUD elements (scene builder)")]
        public Image healthFill;
        public Text healthText;
        public Text ammoText;
        public Text reloadText;
        public Text waveText;
        public Text statusText;
        public Text scoreText;
        public GameObject gameOverPanel;
        public Text gameOverScoreText;
        public Text hintText;

        [Header("Toast (ammo pickups and similar)")]
        public Text toastText;

        [Header("Hit marker")]
        public CanvasGroup hitMarkerGroup;

        [SerializeField] private float hitMarkerDuration = 0.18f;

        [Header("Crosshair")]
        public RectTransform[] crosshairBars;

        [Tooltip("Pixels the crosshair opens at full spread.")]
        [SerializeField] private float crosshairMaxSpread = 14f;

        private static readonly Color HealthyColor = new Color(0.35f, 0.9f, 0.45f);
        private static readonly Color CriticalColor = new Color(0.92f, 0.22f, 0.22f);

        private int lastHealth = -1;
        private int lastMagazine = -1;
        private int lastReserve = -1;
        private int lastReloadPercent = -1;
        private int lastWave = -1;
        private int lastScore = -1;
        private string lastStatus;
        private GameState lastState = (GameState)(-1);
        private float toastUntil;
        private float hitMarkerUntil;
        private float crosshairSpread;
        private Vector2[] crosshairBasePositions;

        private void Awake()
        {
            // Remember where the bars rest so spread can push them outward and back.
            if (crosshairBars != null && crosshairBars.Length > 0)
            {
                crosshairBasePositions = new Vector2[crosshairBars.Length];

                for (int i = 0; i < crosshairBars.Length; i++)
                {
                    if (crosshairBars[i] != null)
                    {
                        crosshairBasePositions[i] = crosshairBars[i].anchoredPosition;
                    }
                }
            }
        }

        private void Update()
        {
            UpdateHealth();
            UpdateWeapon();
            UpdateWave();
            UpdateScore();
            UpdateState();
            UpdateToast();
            UpdateHitMarker();
            UpdateCrosshair();
        }

        /// <summary>Flashes the crosshair tick marks when a shot lands on an enemy.</summary>
        public void ShowHitMarker()
        {
            hitMarkerUntil = Time.unscaledTime + hitMarkerDuration;
        }

        private void UpdateHitMarker()
        {
            if (hitMarkerGroup == null)
            {
                return;
            }

            float remaining = hitMarkerUntil - Time.unscaledTime;
            hitMarkerGroup.alpha = Mathf.Clamp01(remaining / hitMarkerDuration);
        }

        /// <summary>
        /// The crosshair breathes with the gun: it opens while firing and while moving,
        /// tightens while aiming, and eases between states instead of snapping.
        /// </summary>
        private void UpdateCrosshair()
        {
            if (crosshairBars == null || crosshairBasePositions == null || crosshairBars.Length == 0)
            {
                return;
            }

            float spread = 0f;

            if (weapon != null)
            {
                // Firing bloom: full open right after a shot, closed again within half a second.
                spread += Mathf.Clamp01(1f - weapon.TimeSinceLastShot / 0.45f) * 0.9f;
            }

            if (player != null)
            {
                Vector3 velocity = player.Velocity;
                float speed = new Vector2(velocity.x, velocity.z).magnitude;
                spread += Mathf.Clamp01(speed / Mathf.Max(1f, player.SprintSpeed)) * 0.35f;

                if (player.IsAiming)
                {
                    spread *= 0.45f;
                }
            }

            crosshairSpread = Mathf.Lerp(crosshairSpread, spread, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            float offset = crosshairSpread * crosshairMaxSpread;

            // Bars point outward from the centre: 0 = up, 1 = down, 2 = left, 3 = right.
            Vector2[] directions =
            {
                Vector2.up, Vector2.down, Vector2.left, Vector2.right,
            };

            for (int i = 0; i < crosshairBars.Length && i < directions.Length; i++)
            {
                if (crosshairBars[i] == null || i >= crosshairBasePositions.Length)
                {
                    continue;
                }

                crosshairBars[i].anchoredPosition = crosshairBasePositions[i] + directions[i] * offset;
            }
        }

        /// <summary>Shows a short-lived message just above the crosshair.</summary>
        public void ShowToast(string message)
        {
            toastUntil = Time.unscaledTime + 1.6f;

            if (toastText != null)
            {
                toastText.text = message;
            }
        }

        private void UpdateToast()
        {
            if (toastText == null)
            {
                return;
            }

            float remaining = toastUntil - Time.unscaledTime;
            bool visible = remaining > 0f;

            if (toastText.enabled != visible)
            {
                toastText.enabled = visible;
            }

            if (visible)
            {
                Color color = toastText.color;
                color.a = Mathf.Clamp01(remaining);
                toastText.color = color;
            }
        }

        private void UpdateHealth()
        {
            if (playerHealth == null || healthFill == null)
            {
                return;
            }

            float ratio = playerHealth.MaxHealth > 0f
                ? Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth)
                : 0f;

            healthFill.fillAmount = Mathf.Lerp(healthFill.fillAmount, ratio, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            healthFill.color = Color.Lerp(CriticalColor, HealthyColor, ratio);

            int health = Mathf.CeilToInt(playerHealth.CurrentHealth);

            if (health != lastHealth && healthText != null)
            {
                healthText.text = health.ToString();
                lastHealth = health;
            }
        }

        private void UpdateWeapon()
        {
            if (weapon == null)
            {
                return;
            }

            int magazine = weapon.AmmoInMagazine;
            int reserve = weapon.ReserveAmmo;

            if ((magazine != lastMagazine || reserve != lastReserve) && ammoText != null)
            {
                ammoText.text = $"{magazine} / {reserve}";
                ammoText.color = magazine <= 5 ? CriticalColor : Color.white;
                lastMagazine = magazine;
                lastReserve = reserve;
            }

            if (reloadText == null)
            {
                return;
            }

            if (weapon.IsReloading)
            {
                int percent = Mathf.RoundToInt(weapon.ReloadProgress01 * 100f);

                if (percent != lastReloadPercent)
                {
                    reloadText.text = $"RELOADING {percent}%";
                    lastReloadPercent = percent;
                }

                reloadText.enabled = true;
            }
            else
            {
                reloadText.enabled = false;
                lastReloadPercent = -1;
            }
        }

        private void UpdateWave()
        {
            if (waveSpawner == null)
            {
                return;
            }

            int wave = waveSpawner.CurrentWave;

            if (wave != lastWave && waveText != null)
            {
                waveText.text = wave <= 0 ? "GET READY" : $"WAVE {wave}";
                lastWave = wave;
            }

            if (statusText == null)
            {
                return;
            }

            string status;

            if (waveSpawner.Phase == WavePhase.Countdown)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(waveSpawner.CountdownRemaining));
                status = wave <= 0 ? $"FIRST WAVE IN {seconds}" : $"NEXT WAVE IN {seconds}";
            }
            else
            {
                status = $"{waveSpawner.AliveCount} / {waveSpawner.WaveSize} ENEMIES";
            }

            if (status != lastStatus)
            {
                statusText.text = status;
                lastStatus = status;
            }
        }

        private void UpdateScore()
        {
            if (gameManager == null || scoreText == null)
            {
                return;
            }

            if (gameManager.Score != lastScore)
            {
                lastScore = gameManager.Score;
                scoreText.text = $"SCORE {lastScore}";
            }
        }

        private void UpdateState()
        {
            if (gameManager == null)
            {
                return;
            }

            if (gameManager.State == lastState)
            {
                return;
            }

            lastState = gameManager.State;

            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(lastState == GameState.GameOver);
            }

            if (lastState == GameState.GameOver && gameOverScoreText != null)
            {
                int wave = waveSpawner != null ? Mathf.Max(1, waveSpawner.CurrentWave) : 1;
                gameOverScoreText.text = $"SCORE {gameManager.Score}      WAVE {wave}";
            }

            if (hintText != null)
            {
                hintText.enabled = lastState == GameState.Playing;
            }
        }
    }
}
