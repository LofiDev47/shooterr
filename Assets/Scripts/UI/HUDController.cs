using System.Collections.Generic;
using Shooter.Combat;
using Shooter.Enemies;
using Shooter.Managers;
using Shooter.Player;
using Shooter.Upgrades;
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
        private static readonly Vector2[] CrosshairDirections =
            { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

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
        public Text shotgunText;
        public Text waveText;
        public Text statusText;
        public Text scoreText;
        public Text comboText;
        public GameObject gameOverPanel;
        public Text gameOverScoreText;
        public Text hintText;

        [Header("Toast (ammo pickups and similar)")]
        public Text toastText;

        [Header("Hit marker")]
        public CanvasGroup hitMarkerGroup;

        [Header("Boss bar (shown only while The Warden is alive)")]
        public GameObject bossBarRoot;
        public Image bossBarFill;
        public Text bossNameText;
        public Text bossPhaseText;
        [Tooltip("One pip per phase; lit pips show the phase the boss is in.")]
        public Image[] bossPhasePips;

        [SerializeField] private Color bossPhaseOneColor = new Color(0.92f, 0.35f, 0.14f);
        [SerializeField] private Color bossPhaseTwoColor = new Color(1f, 0.55f, 0.12f);
        [SerializeField] private Color bossPhaseThreeColor = new Color(1f, 0.82f, 0.25f);
        [SerializeField] private Color bossExposedColor = new Color(1f, 0.95f, 0.6f);

        [SerializeField] private float hitMarkerDuration = 0.18f;

        [Header("Damage numbers")]
        [Tooltip("Floating damage text at the hit point. Headshots are gold and larger.")]
        public bool showDamageNumbers = true;
        [SerializeField] private int damageNumberFontSize = 18;
        [SerializeField] private float damageNumberLifetime = 0.85f;
        [SerializeField] private float damageNumberRise = 60f;

        [Header("Crosshair")]
        public CanvasGroup crosshairGroup;
        public RectTransform[] crosshairBars;

        [Tooltip("Pixels the crosshair opens at full spread.")]
        [SerializeField] private float crosshairMaxSpread = 14f;

        private const string HintBase =
            "WASD move   ·   SHIFT sprint   ·   SPACE jump   ·   LMB fire   ·   RMB aim   ·   V melee   ·   C slide   ·   R reload   ·   ESC pause";
        private const string HintWithShotgun =
            "WASD move   ·   SHIFT sprint   ·   SPACE jump   ·   LMB fire   ·   RMB aim   ·   V melee   ·   F shotgun   ·   C slide   ·   R reload   ·   ESC pause";

        private static readonly Color HealthyColor = new Color(0.35f, 0.9f, 0.45f);
        private static readonly Color CriticalColor = new Color(0.92f, 0.22f, 0.22f);
        private static readonly Color NormalHitColor = new Color(1f, 0.95f, 0.75f, 1f);
        private static readonly Color HeadshotColor = new Color(1f, 0.68f, 0.14f, 1f);

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
        private Image[] hitMarkerImages;
        private int lastComboCount = -1;
        private float lastComboMultiplier = -1f;
        private readonly List<DamageMarker> activeDamageNumbers = new List<DamageMarker>();
        private Font markerFont;

        /// <summary>Live damage numbers, exposed for tests and balance work.</summary>
        public int ActiveDamageNumberCount => activeDamageNumbers.Count;

        private sealed class DamageMarker
        {
            public Text Text;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Age;
        }

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

            markerFont = healthText != null ? healthText.font : toastText != null ? toastText.font : null;
        }

        private void ResolveSettingsFlag()
        {
            if (SettingsManager.Instance != null)
            {
                showDamageNumbers = SettingsManager.Instance.ShowDamageNumbers;
            }
        }

        private void Update()
        {
            ResolveSettingsFlag();
            UpdateHealth();
            UpdateWeapon();
            UpdateShotgun();
            UpdateHint();
            UpdateWave();
            UpdateBossBar();
            UpdateScore();
            UpdateCombo();
            UpdateState();
            UpdateToast();
            UpdateHitMarker();
            UpdateCrosshair();
            UpdateDamageNumbers();
        }

        /// <summary>
        /// Flashes the crosshair tick marks when a shot lands on an enemy. Headshots
        /// flash longer and gold so precision reads instantly.
        /// </summary>
        public void ShowHitMarker(bool headshot = false)
        {
            hitMarkerUntil = Time.unscaledTime + (headshot ? hitMarkerDuration * 1.7f : hitMarkerDuration);

            if (hitMarkerGroup == null)
            {
                return;
            }

            hitMarkerImages ??= hitMarkerGroup.GetComponentsInChildren<Image>(true);
            Color color = headshot ? HeadshotColor : NormalHitColor;

            foreach (Image image in hitMarkerImages)
            {
                if (image != null)
                {
                    image.color = color;
                }
            }
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
            if (crosshairGroup != null)
                crosshairGroup.alpha = 1f - (weapon != null ? weapon.SightBlend : 0f);

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

            for (int i = 0; i < crosshairBars.Length && i < CrosshairDirections.Length; i++)
            {
                if (crosshairBars[i] == null || i >= crosshairBasePositions.Length)
                {
                    continue;
                }

                crosshairBars[i].anchoredPosition = crosshairBasePositions[i] + CrosshairDirections[i] * offset;
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

        /// <summary>Underbarrel shell count and reload state; hidden until the augment is taken.</summary>
        private void UpdateShotgun()
        {
            if (weapon == null || shotgunText == null)
            {
                return;
            }

            bool unlocked = UpgradeManager.Instance != null && UpgradeManager.Instance.ShotgunUnlocked;

            if (shotgunText.enabled != unlocked)
            {
                shotgunText.enabled = unlocked;
            }

            if (!unlocked)
            {
                return;
            }

            string text;
            Color color;

            if (weapon.IsShotgunReloading)
            {
                text = "SHELLS  RELOADING";
                color = new Color(1f, 0.55f, 0.18f);
            }
            else
            {
                text = $"SHELLS {weapon.ShotgunShells}/{weapon.ShotgunCapacity}";
                color = weapon.ShotgunShells > 0
                    ? new Color(1f, 0.78f, 0.32f)
                    : new Color(1f, 1f, 1f, 0.3f);
            }

            if (shotgunText.text != text)
            {
                shotgunText.text = text;
                shotgunText.color = color;
            }
        }

        /// <summary>The F hint only appears once the Breach Tube has been taken.</summary>
        private void UpdateHint()
        {
            if (hintText == null)
            {
                return;
            }

            bool unlocked = UpgradeManager.Instance != null && UpgradeManager.Instance.ShotgunUnlocked;
            string text = unlocked ? HintWithShotgun : HintBase;

            if (hintText.text != text)
            {
                hintText.text = text;
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
                if (wave <= 0)
                {
                    waveText.text = "GET READY";
                }
                else if (waveSpawner.IsBossWave)
                {
                    waveText.text = $"BOSS WAVE {wave}";
                    waveText.color = new Color(1f, 0.45f, 0.25f);
                }
                else
                {
                    waveText.text = $"WAVE {wave}";
                    waveText.color = Color.white;
                }

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

                if (WaveSpawner.IsBossWaveNumber(wave + 1))
                {
                    status = $"BOSS WAVE NEXT IN {seconds}";
                }
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

        /// <summary>
        /// Boss bar: name, phase label and pip row above a single wide fill. It
        /// pulls from <see cref="BossController.Current"/> so no extra wiring is
        /// needed when a boss spawns mid-wave, and hides itself once it dies.
        /// </summary>
        private void UpdateBossBar()
        {
            if (bossBarRoot == null)
            {
                return;
            }

            BossController boss = BossController.Current;
            bool visible = boss != null && !boss.IsDefeated;

            if (bossBarRoot.activeSelf != visible)
            {
                bossBarRoot.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            float ratio = boss.HealthRatio;

            if (bossBarFill != null)
            {
                // Fast snap while taking damage, so chip damage reads immediately.
                bossBarFill.fillAmount = Mathf.Lerp(bossBarFill.fillAmount, ratio,
                    1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
                bossBarFill.color = boss.CoreExposed
                    ? bossExposedColor
                    : boss.Phase >= 3 ? bossPhaseThreeColor
                    : boss.Phase >= 2 ? bossPhaseTwoColor
                    : bossPhaseOneColor;
            }

            if (bossNameText != null && bossNameText.text != boss.BossName)
            {
                bossNameText.text = boss.BossName;
            }

            if (bossPhaseText != null)
            {
                Color phaseColor = boss.CoreExposed ? bossExposedColor : new Color(1f, 0.8f, 0.6f);
                bossPhaseText.color = phaseColor;

                if (bossPhaseText.text != boss.PhaseLabel)
                {
                    bossPhaseText.text = boss.PhaseLabel;
                }
            }

            if (bossPhasePips == null)
            {
                return;
            }

            for (int i = 0; i < bossPhasePips.Length; i++)
            {
                Image pip = bossPhasePips[i];

                if (pip == null)
                {
                    continue;
                }

                Color pipColor = i < boss.Phase
                    ? Color.Lerp(bossPhaseOneColor, bossPhaseThreeColor, i / 2f)
                    : new Color(1f, 1f, 1f, 0.15f);

                if (pip.color != pipColor)
                {
                    pip.color = pipColor;
                }
            }
        }

        private void UpdateScore()
        {            if (gameManager == null || scoreText == null)
            {
                return;
            }

            if (gameManager.Score != lastScore)
            {
                lastScore = gameManager.Score;
                scoreText.text = $"SCORE {lastScore}";
            }
        }

        /// <summary>
        /// Shows the live kill chain once it reaches two kills. The label only
        /// refreshes on a change, so the text is not rebuilt every frame.
        /// </summary>
        private void UpdateCombo()
        {
            if (comboText == null || gameManager == null || gameManager.Combo == null)
            {
                return;
            }

            ComboTracker combo = gameManager.Combo;
            bool visible = combo.Count >= 2;

            if (comboText.enabled != visible)
            {
                comboText.enabled = visible;
            }

            if (!visible)
            {
                lastComboCount = combo.Count;
                lastComboMultiplier = combo.Multiplier;
                return;
            }

            if (combo.Count != lastComboCount || !Mathf.Approximately(combo.Multiplier, lastComboMultiplier))
            {
                lastComboCount = combo.Count;
                lastComboMultiplier = combo.Multiplier;
                comboText.text = $"x{combo.Multiplier:0.##}  ·  {combo.Count} KILL CHAIN";
                comboText.color = Color.Lerp(
                    new Color(1f, 0.85f, 0.35f), new Color(1f, 0.42f, 0.16f),
                    Mathf.InverseLerp(2f, 4f, combo.Multiplier));
            }
        }

        /// <summary>
        /// Spawns a floating damage number at the world hit point. Critical hits are
        /// orange, headshots are gold and larger; both stack (heads are the stronger
        /// signal).
        /// </summary>
        public void ShowDamageNumber(Vector3 worldPoint, float amount, bool headshot, bool critical)
        {
            if (!showDamageNumbers || markerFont == null || player == null || player.playerCamera == null)
            {
                return;
            }

            Vector3 screen = player.playerCamera.WorldToScreenPoint(worldPoint);

            if (screen.z < 0f)
            {
                return; // Behind the camera.
            }

            RectTransform canvasRect = transform as RectTransform;

            if (canvasRect == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
            {
                return;
            }

            var go = new GameObject("DamageNumber");
            go.transform.SetParent(canvasRect, false);

            Text text = go.AddComponent<Text>();
            text.font = markerFont;
            text.text = Mathf.RoundToInt(Mathf.Max(0f, amount)).ToString();
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.fontSize = headshot ? damageNumberFontSize + 6 : damageNumberFontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = critical
                ? new Color(1f, 0.45f, 0.12f, 1f)
                : headshot ? new Color(1f, 0.82f, 0.2f, 1f) : new Color(1f, 1f, 1f, 1f);

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(90f, 28f);

            Vector2 position = local + new Vector2(Random.Range(-16f, 16f), Random.Range(6f, 18f));
            rect.anchoredPosition = position;

            activeDamageNumbers.Add(new DamageMarker
            {
                Text = text,
                Position = position,
                Velocity = new Vector2(Random.Range(-12f, 12f), damageNumberRise),
                Age = 0f,
            });
        }

        private void UpdateDamageNumbers()
        {
            float deltaTime = Time.unscaledDeltaTime;

            for (int i = activeDamageNumbers.Count - 1; i >= 0; i--)
            {
                DamageMarker marker = activeDamageNumbers[i];

                if (marker.Text == null)
                {
                    activeDamageNumbers.RemoveAt(i);
                    continue;
                }

                marker.Age += deltaTime;

                if (marker.Age >= damageNumberLifetime)
                {
                    Destroy(marker.Text.gameObject);
                    activeDamageNumbers.RemoveAt(i);
                    continue;
                }

                // Ease outward motion to a stop and rise without uniform drag.
                marker.Velocity = Vector2.Lerp(marker.Velocity, Vector2.zero, 1f - Mathf.Exp(-5f * deltaTime));
                marker.Position += marker.Velocity * deltaTime;

                Color color = marker.Text.color;
                color.a = 1f - marker.Age / damageNumberLifetime;
                marker.Text.color = color;
                marker.Text.rectTransform.anchoredPosition = marker.Position;
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
