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
    /// Owns run-level state: score, playing/game-over transitions and restart.
    /// The HUD reads these values directly (pull model) so there is exactly one
    /// source of truth for the run.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Wiring (scene builder)")]
        public PlayerHealth playerHealth;
        public PlayerController playerController;
        public WaveSpawner waveSpawner;

        public GameState State { get; private set; } = GameState.Playing;
        public bool IsPlaying => State == GameState.Playing;
        public int Score { get; private set; }

        private void Awake()
        {
            Instance = this;
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
            if (State != GameState.GameOver || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.rKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
            {
                Restart();
            }
        }

        /// <summary>Awards points for a kill. Ignored once the run is over.</summary>
        public void AddScore(int amount)
        {
            if (!IsPlaying || amount <= 0)
            {
                return;
            }

            Score += amount;
        }

        private void HandlePlayerDied()
        {
            if (State == GameState.GameOver)
            {
                return;
            }

            State = GameState.GameOver;

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
