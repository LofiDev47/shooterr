using Shooter.Combat;
using Shooter.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Shooter.Managers
{
    public enum GameState
    {
        Playing,
        GameOver
    }

    /// <summary>
    /// Owns run-level state: score, kill chains, hit-stop and playing/game-over
    /// transitions. The HUD reads these values directly (pull model) so there is
    /// exactly one source of truth for the run.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Wiring (scene builder)")]
        public PlayerHealth playerHealth;
        public PlayerController playerController;
        public WaveSpawner waveSpawner;

        [Header("Kill chain")]
        [Tooltip("Seconds between kills before the multiplier lapses.")]
        [SerializeField] private float comboWindow = 3.5f;
        [Tooltip("Multiplier added for every extra kill in the chain.")]
        [SerializeField] private float comboTierStep = 0.25f;
        [SerializeField] private float comboMaxMultiplier = 4f;

        [Header("Kill hit-stop")]
        [Tooltip("Unscaled seconds the world freezes on a kill.")]
        [SerializeField] private float killHitStopDuration = 0.045f;
        [Range(0.01f, 1f)]
        [SerializeField] private float killHitStopScale = 0.12f;

        public GameState State { get; private set; } = GameState.Playing;
        public bool IsPlaying => State == GameState.Playing;
        public int Score { get; private set; }

        /// <summary>Live kill-chain state, read by the HUD.</summary>
        public ComboTracker Combo { get; private set; }

        private float hitStopUntil;

        private void Awake()
        {
            Instance = this;
            Combo = new ComboTracker(comboWindow, comboTierStep, comboMaxMultiplier);
            Time.timeScale = 1f;
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
            Combo?.Tick(Time.time);
            UpdateHitStop();

            if (State != GameState.GameOver || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.rKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
            {
                Restart();
            }
        }

        /// <summary>Awards points for a kill, multiplied by the current chain.</summary>
        public void AddKillScore(int baseScore)
        {
            if (!IsPlaying || baseScore <= 0)
            {
                return;
            }

            Score += Combo.RegisterKill(Time.time, baseScore);
        }

        /// <summary>Awards flat points that are not part of the kill chain.</summary>
        public void AddScore(int amount)
        {
            if (!IsPlaying || amount <= 0)
            {
                return;
            }

            Score += amount;
        }

        /// <summary>
        /// Freezes the world for a few unscaled milliseconds so a kill lands with
        /// weight. Ignored while paused for an augment choice or after game over.
        /// </summary>
        public void RequestKillHitStop()
        {
            if (!IsPlaying || IsChoosingUpgrade() || killHitStopDuration <= 0f)
            {
                return;
            }

            Time.timeScale = Mathf.Clamp(killHitStopScale, 0.01f, 1f);
            hitStopUntil = Time.unscaledTime + killHitStopDuration;
        }

        private void UpdateHitStop()
        {
            if (hitStopUntil <= 0f || Time.unscaledTime < hitStopUntil)
            {
                return;
            }

            hitStopUntil = 0f;

            // The upgrade screen and the game-over screen own timeScale; never
            // resume the world underneath either of them.
            if (IsPlaying && !IsChoosingUpgrade())
            {
                Time.timeScale = 1f;
            }
        }

        private static bool IsChoosingUpgrade()
        {
            return Upgrades.UpgradeManager.Instance != null && Upgrades.UpgradeManager.Instance.IsChoosing;
        }

        private void HandlePlayerDied()
        {
            if (State == GameState.GameOver)
            {
                return;
            }

            State = GameState.GameOver;
            hitStopUntil = 0f;
            Combo?.Reset();

            if (playerController != null)
            {
                playerController.ControlsEnabled = false;
                playerController.LockCursor(false);
            }

            // Freeze gameplay, but the HUD keeps updating because Unity still ticks Update.
            Time.timeScale = 0f;
            AudioManager.Instance?.PlayGameOver();
        }

        /// <summary>Reloads the active scene and resets time scale.</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();

            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
            }
            else
            {
                SceneManager.LoadScene(scene.name);
            }
        }
    }
}
