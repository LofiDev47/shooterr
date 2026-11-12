using System;
using Shooter.Combat;
using Shooter.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Shooter.Managers
{
    public enum GameState
    {
        Menu,
        Playing,
        Paused,
        GameOver,
    }

    /// <summary>
    /// Owns run-level state: score, kill chains, hit-stop and menu / playing /
    /// paused / game-over transitions. The HUD reads these values directly
    /// (pull model) so there is exactly one source of truth for the run.
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

        public GameState State { get; private set; } = GameState.Menu;
        public bool IsPlaying => State == GameState.Playing;
        public int Score { get; private set; }

        public event Action<GameState> StateChanged;

        /// <summary>Live kill-chain state, read by the HUD.</summary>
        public ComboTracker Combo { get; private set; }

        private float hitStopUntil;
        private static bool autoStartNextRun;
        private UI.MenuController menu;

        private void Awake()
        {
            Instance = this;
            Combo = new ComboTracker(comboWindow, comboTierStep, comboMaxMultiplier);
            State = GameState.Menu;
            Score = 0;
        }

        private void Start()
        {
            if (autoStartNextRun)
            {
                autoStartNextRun = false;
                StartGame();
                return;
            }

            // Generated scenes built before the menu existed have no MenuController.
            // Start the run instead of stranding the player in a frozen menu state.
            if (FindAnyObjectByType<UI.MenuController>() == null)
            {
                StartGame();
                return;
            }

            State = GameState.Menu;
            Time.timeScale = 0f;

            if (playerController != null)
            {
                playerController.ControlsEnabled = false;
                playerController.LockCursor(false);
            }

            StateChanged?.Invoke(State);
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

            if (Keyboard.current == null)
            {
                return;
            }

            // The settings page is a modal screen owned by MenuController. While it
            // owns input, Enter/Space must not start a run behind it and Esc must not
            // resume a paused run underneath it. MenuController keeps the flag up for
            // the frame it closes on, so the close key cannot leak through when
            // Update order puts the menu first.
            if (menu == null)
            {
                menu = FindAnyObjectByType<UI.MenuController>();
            }

            if (menu != null && menu.IsModalOpen)
            {
                return;
            }

            SettingsManager settings = SettingsManager.Instance;

            switch (State)
            {
                case GameState.GameOver:
                    if (WasPressed(settings, SettingsManager.ActionRestart) ||
                        Keyboard.current.enterKey.wasPressedThisFrame)
                    {
                        Restart();
                    }
                    break;

                case GameState.Menu:
                    if (Keyboard.current.enterKey.wasPressedThisFrame ||
                        Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                        Keyboard.current.spaceKey.wasPressedThisFrame)
                    {
                        // Don't start from a stray Space that was still held from a previous run.
                        // Unity still reports wasPressedThisFrame only on the edge, so this is safe.
                        StartGame();
                    }
                    break;

                case GameState.Playing:
                    if (WasPressed(settings, SettingsManager.ActionPause))
                    {
                        Pause();
                    }
                    break;

                case GameState.Paused:
                    if (WasPressed(settings, SettingsManager.ActionPause))
                    {
                        Resume();
                    }
                    break;
            }
        }

        /// <summary>
        /// The bound key wins; the literal default only steps in when there is no
        /// SettingsManager at all. Checking both meant rebinding Pause or Restart
        /// added a second key instead of replacing the default one.
        /// </summary>
        private static bool WasPressed(SettingsManager settings, string actionId)
        {
            if (settings != null)
            {
                return settings.WasPressed(actionId);
            }

            if (actionId == SettingsManager.ActionPause)
            {
                return SettingsManager.WasPressed(Key.Escape);
            }

            if (actionId == SettingsManager.ActionRestart)
            {
                return SettingsManager.WasPressed(Key.R);
            }

            return false;
        }

        /// <summary>Moves from the main menu into the run. Also used after a quit to menu.</summary>
        public void StartGame()
        {
            if (State != GameState.Menu && State != GameState.Paused)
            {
                return;
            }

            SetState(GameState.Playing);
            Time.timeScale = 1f;

            if (playerController != null)
            {
                playerController.ControlsEnabled = true;
                playerController.LockCursor(true);
            }
        }

        /// <summary>Pauses the run: freezes time, releases the cursor, shows the pause menu.</summary>
        public void Pause()
        {
            if (State != GameState.Playing)
            {
                return;
            }

            SetState(GameState.Paused);
            Time.timeScale = 0f;

            if (playerController != null)
            {
                playerController.ControlsEnabled = false;
                playerController.LockCursor(false);
            }
        }

        /// <summary>Resumes from the pause menu.</summary>
        public void Resume()
        {
            if (State != GameState.Paused)
            {
                return;
            }

            SetState(GameState.Playing);
            Time.timeScale = 1f;

            if (playerController != null)
            {
                playerController.ControlsEnabled = true;
                playerController.LockCursor(true);
            }
        }

        /// <summary>Reloads the arena so the menu owns a fresh run.</summary>
        public void QuitToMenu()
        {
            autoStartNextRun = false;
            Time.timeScale = 1f;
            State = GameState.Menu;
            LoadActiveScene();
        }

        /// <summary>Reloads the active scene and jumps straight back into play.</summary>
        public void Restart()
        {
            autoStartNextRun = true;
            Time.timeScale = 1f;
            LoadActiveScene();
        }

        private void LoadActiveScene()
        {
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

        private void SetState(GameState next)
        {
            State = next;
            StateChanged?.Invoke(State);
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

            int waveReached = waveSpawner != null ? Mathf.Max(1, waveSpawner.CurrentWave) : 1;
            SettingsManager.Instance?.RecordRun(Score, waveReached);

            SetState(GameState.GameOver);
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
    }
}
