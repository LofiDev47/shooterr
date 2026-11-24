using System.Collections.Generic;
using Shooter.Managers;
using Shooter.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Shooter.UI
{
    /// <summary>
    /// Main menu, pause menu and settings UI. Builds its own Canvas at runtime
    /// so the scene stays reproducible from code (the scene builder only adds
    /// this component) while still wiring to the existing HUD / GameManager.
    /// Settings are persisted through <see cref="SettingsManager"/>.
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        public Sprite foundryPlate;
        public Sprite foundryEmblem;
        private static readonly Color MenuBackdrop = new Color(0.023f, 0.038f, 0.052f, 0.97f);
        private static readonly Color SignalAccent = new Color(0.45f, 0.96f, 0.81f);
        [Header("Wiring (scene builder)")]
        public GameManager gameManager;
        public SettingsManager settings;
        public HUDController hud;
        public PlayerController player;

        [Header("Panels (built at runtime if null)")]
        public GameObject mainMenuPanel;
        public GameObject pausePanel;
        public GameObject settingsPanel;

        /// <summary>
        /// True while a menu-owned page owns the keyboard. GameManager checks this so
        /// Enter/Space cannot start a run behind the settings screen and Esc cannot
        /// resume one underneath it. It stays true for the frame the page closes on,
        /// so the closing key cannot leak into the game when Update order puts this
        /// component before GameManager.
        /// </summary>
        public bool IsModalOpen => settingsOpen || Time.frameCount <= inputBlockedUntilFrame;

        private Font font;

        private Button playButton;
        private Button resumeButton;
        private Button settingsBackButton;
        private Button gameOverRestartButton;
        private Text menuHint;

        private bool consumedInputThisFrame;
        private int inputBlockedUntilFrame = -1;
        private GameObject pendingSelection;
        private MenuScreen lastScreen = MenuScreen.None;
        private bool settingsSubscribed;

        private Text bestLabel;
        private Slider sensitivitySlider;
        private Slider volumeSlider;
        private Slider fovSlider;
        private Slider qualitySlider;
        private Text sensitivityValue;
        private Text volumeValue;
        private Text fovValue;
        private Text qualityValue;
        private Toggle invertToggle;
        private Toggle shakeToggle;
        private Toggle damageToggle;
        private Toggle fpsToggle;
        private Toggle fullscreenToggle;
        private Toggle vsyncToggle;
        private Toggle postProcessingToggle;
        private Button frameRateButton;
        private Text frameRateValue;
        private Text resolutionValue;
        private readonly Dictionary<string, Button> keybindButtons = new Dictionary<string, Button>();

        private string listeningActionId;
        private bool settingsOpen;
        private GameState settingsReturnTo = GameState.Menu;

        private static readonly (int width, int height)[] ResolutionPresets =
        {
            (960, 540),
            (1280, 720),
            (1600, 900),
            (1920, 1080),
            (2560, 1440),
            (3840, 2160),
        };

        /// <summary>Which menu page is on screen; focus is placed once per change.</summary>
        private enum MenuScreen
        {
            None,
            Main,
            Paused,
            Settings,
            GameOver,
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            EnsureEventSystem();
            EnsureUI();
            EnsureGameOverButtons();
            SyncSettingsUI();
            SyncPanels();
        }

        private void OnEnable()
        {
            if (settings != null)
            {
                settings.SettingsChanged += HandleSettingsChanged;
            }
        }

        private void OnDisable()
        {
            if (settings != null && settingsSubscribed)
            {
                settings.SettingsChanged -= HandleSettingsChanged;
                settingsSubscribed = false;
            }
        }

        private void Update()
        {
            if (gameManager == null || settings == null || hud == null || player == null)
            {
                ResolveReferences();
            }

            consumedInputThisFrame = false;
            UpdateRebindCapture();

            if (!consumedInputThisFrame)
            {
                HandleMenuInput();
            }

            SyncPanels();
        }

        /// <summary>
        /// Esc backs out of the settings page. GameManager ignores its own keys while
        /// <see cref="IsModalOpen"/> is true, so this is the keyboard way out.
        /// </summary>
        private void HandleMenuInput()
        {
            if (!settingsOpen || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseSettings();
            }
        }

        private void ResolveReferences()
        {
            if (settings == null)
            {
                settings = SettingsManager.Instance != null ? SettingsManager.Instance : FindAnyObjectByType<SettingsManager>();
            }

            if (settings != null && !settingsSubscribed)
            {
                settings.SettingsChanged += HandleSettingsChanged;
                settingsSubscribed = true;
            }

            if (gameManager == null)
            {
                gameManager = GameManager.Instance != null ? GameManager.Instance : FindAnyObjectByType<GameManager>();
            }

            if (hud == null)
            {
                hud = FindAnyObjectByType<HUDController>();
            }

            if (player == null && hud != null)
            {
                player = hud.player;
            }
        }

        // ---------------------------------------------------------------- public actions

        public void Play()
        {
            if (gameManager != null)
            {
                gameManager.StartGame();
            }
        }

        public void Resume()
        {
            if (gameManager != null)
            {
                gameManager.Resume();
            }
        }

        public void Restart()
        {
            if (gameManager != null)
            {
                gameManager.Restart();
            }
        }

        public void QuitToMenu()
        {
            if (gameManager != null)
            {
                gameManager.QuitToMenu();
            }
        }

        public void OpenSettingsFromMenu()
        {
            settingsOpen = true;
            settingsReturnTo = GameState.Menu;
            SyncSettingsUI();
        }

        public void OpenSettingsFromPause()
        {
            settingsOpen = true;
            settingsReturnTo = GameState.Paused;
            SyncSettingsUI();
        }

        public void CloseSettings()
        {
            bool wasOpen = settingsOpen || !string.IsNullOrEmpty(listeningActionId);
            settingsOpen = false;
            listeningActionId = null;

            // Hold the modal flag for this frame so the key that closed the page
            // cannot also pause/resume/start the game behind it.
            if (wasOpen)
            {
                inputBlockedUntilFrame = Time.frameCount;
            }

            RefreshKeybindLabels();
        }

        public void ResetSettings()
        {
            if (settings != null)
            {
                settings.ResetToDefaults();
            }
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- panel sync

        private void SyncPanels()
        {
            if (mainMenuPanel == null && settingsPanel == null)
            {
                return;
            }

            GameState state = gameManager != null ? gameManager.State : GameState.Menu;
            MenuScreen screen;

            if (settingsOpen)
            {
                screen = MenuScreen.Settings;
            }
            else if (state == GameState.Menu)
            {
                screen = MenuScreen.Main;
            }
            else if (state == GameState.Paused)
            {
                screen = MenuScreen.Paused;
            }
            else if (state == GameState.GameOver)
            {
                screen = MenuScreen.GameOver;
            }
            else
            {
                screen = MenuScreen.None;
            }

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(screen == MenuScreen.Settings);
            }

            if (mainMenuPanel != null)
            {
                mainMenuPanel.SetActive(screen == MenuScreen.Main);
            }

            if (pausePanel != null)
            {
                pausePanel.SetActive(screen == MenuScreen.Paused);
            }

            if (bestLabel != null && settings != null)
            {
                bestLabel.text = $"BEST SCORE {settings.BestScore}  ·  BEST WAVE {settings.BestWave}";
            }

            if (screen == MenuScreen.None)
            {
                if (gameManager != null && gameManager.IsPlaying && player != null && player.ControlsEnabled && Cursor.visible)
                {
                    // GameManager owns the lock; MenuController just makes sure the
                    // cursor is hidden while playing if something left it visible.
                    player.LockCursor(true);
                }
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            UpdateSelection(screen);
            ApplyPendingSelection();
        }

        /// <summary>
        /// Gives each page a focused element when the page changes. Doing this every
        /// frame would fight arrow-key navigation, so it only fires on a transition.
        /// </summary>
        private void UpdateSelection(MenuScreen screen)
        {
            if (screen == lastScreen)
            {
                return;
            }

            lastScreen = screen;

            switch (screen)
            {
                case MenuScreen.Main:
                    QueueSelection(playButton);
                    break;
                case MenuScreen.Paused:
                    QueueSelection(resumeButton);
                    break;
                case MenuScreen.Settings:
                    QueueSelection(settingsBackButton);
                    break;
                case MenuScreen.GameOver:
                    QueueSelection(gameOverRestartButton);
                    break;
                default:
                    pendingSelection = null;
                    break;
            }
        }

        private void QueueSelection(Button button)
        {
            pendingSelection = button != null ? button.gameObject : null;
        }

        /// <summary>
        /// Applied while the target is actually visible: the game-over panel belongs
        /// to the HUD and may switch on a frame after the state change.
        /// </summary>
        private void ApplyPendingSelection()
        {
            if (pendingSelection == null || EventSystem.current == null || !pendingSelection.activeInHierarchy)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(pendingSelection);
            pendingSelection = null;
        }

        // ---------------------------------------------------------------- settings sync

        private void HandleSettingsChanged()
        {
            SyncSettingsUI();
        }

        private void SyncSettingsUI()
        {
            if (settings == null)
            {
                return;
            }

            if (sensitivitySlider != null)
            {
                sensitivitySlider.SetValueWithoutNotify(settings.Sensitivity);
                if (sensitivityValue != null)
                {
                    sensitivityValue.text = settings.Sensitivity.ToString("0.00");
                }
            }

            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(settings.MasterVolume);
                if (volumeValue != null)
                {
                    volumeValue.text = Mathf.RoundToInt(settings.MasterVolume * 100f) + "%";
                }
            }

            if (fovSlider != null)
            {
                fovSlider.SetValueWithoutNotify(settings.FieldOfView);
                if (fovValue != null)
                {
                    fovValue.text = Mathf.RoundToInt(settings.FieldOfView).ToString();
                }
            }

            if (qualitySlider != null)
            {
                qualitySlider.SetValueWithoutNotify(settings.QualityIndex);
                if (qualityValue != null)
                {
                    qualityValue.text = SettingsManager.QualityName(settings.QualityIndex).ToUpperInvariant();
                }
            }

            if (invertToggle != null)
            {
                invertToggle.SetIsOnWithoutNotify(settings.InvertY);
            }

            if (shakeToggle != null)
            {
                shakeToggle.SetIsOnWithoutNotify(settings.ShakeEnabled);
            }

            if (damageToggle != null)
            {
                damageToggle.SetIsOnWithoutNotify(settings.ShowDamageNumbers);
            }

            if (fpsToggle != null)
                fpsToggle.SetIsOnWithoutNotify(settings.ShowFps);

            if (fullscreenToggle != null)
            {
                fullscreenToggle.SetIsOnWithoutNotify(settings.Fullscreen);
            }

            if (vsyncToggle != null)
            {
                vsyncToggle.SetIsOnWithoutNotify(settings.VSyncEnabled);
            }

            if (postProcessingToggle != null)
            {
                postProcessingToggle.SetIsOnWithoutNotify(settings.PostProcessingEnabled);
            }

            if (frameRateButton != null)
            {
                frameRateButton.interactable = !settings.VSyncEnabled;
                frameRateValue.text = settings.VSyncEnabled ? "VSync active" : settings.FrameRateLimit switch
                {
                    -1 => "Unlimited",
                    0 => "Preset default",
                    _ => $"{settings.FrameRateLimit} FPS",
                };
            }

            if (resolutionValue != null)
            {
                resolutionValue.text = $"{settings.ResWidth}x{settings.ResHeight}";
            }

            if (bestLabel != null)
            {
                bestLabel.text = $"BEST SCORE {settings.BestScore}  ·  BEST WAVE {settings.BestWave}";
            }

            RefreshKeybindLabels();
            RefreshMenuHint();
        }

        /// <summary>
        /// The control hint is generated from the live bindings, so a rebind cannot
        /// leave the main menu advertising the old keys.
        /// </summary>
        private void RefreshMenuHint()
        {
            if (menuHint == null)
            {
                return;
            }

            if (settings == null)
            {
                menuHint.text = "WASD move · double-tap W sprint · SPACE jump · LMB fire · RMB aim · E melee · F inspect · C slide · R recharge · G shotgun · ESC pause" +
                    "\nPress SPACE near a wall while airborne to wall jump. Traversal Harness unlocks SHIFT air dash.";
                return;
            }

            string K(string actionId)
            {
                return SettingsManager.PrettyKey(settings.GetKey(actionId));
            }

            menuHint.text =
                $"{K(SettingsManager.ActionForward)}{K(SettingsManager.ActionStrafeLeft)}" +
                $"{K(SettingsManager.ActionBackward)}{K(SettingsManager.ActionStrafeRight)} move" +
                $" · double-tap {K(SettingsManager.ActionForward)} sprint" +
                $" · {K(SettingsManager.ActionJump)} jump" +
                " · LMB fire · RMB aim" +
                $" · {K(SettingsManager.ActionMelee)} melee" +
                $" · {K(SettingsManager.ActionInspect)} inspect" +
                $" · {K(SettingsManager.ActionSlide)} slide" +
                $" · {K(SettingsManager.ActionReload)} recharge" +
                $" · {K(SettingsManager.ActionShotgun)} shotgun" +
                $" · {K(SettingsManager.ActionPause)} pause" +
                $"\nPress {K(SettingsManager.ActionJump)} near a wall while airborne to wall jump." +
                $" Traversal Harness unlocks {K(SettingsManager.ActionDash)} air dash.";
        }

        private void RefreshKeybindLabels()
        {
            foreach (KeyValuePair<string, Button> pair in keybindButtons)
            {
                if (pair.Value != null)
                {
                    Text label = pair.Value.GetComponentInChildren<Text>();
                    if (label != null)
                    {
                        label.text = settings != null ? SettingsManager.PrettyKey(settings.GetKey(pair.Key)) : "—";
                        if (listeningActionId == pair.Key)
                        {
                            label.text = "...";
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- rebind capture

        private void UpdateRebindCapture()
        {
            if (string.IsNullOrEmpty(listeningActionId) || Keyboard.current == null || settings == null)
            {
                return;
            }

            foreach (Key key in System.Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None)
                {
                    continue;
                }

                if (!Keyboard.current[key].wasPressedThisFrame)
                {
                    continue;
                }

                // This keystroke belongs to the capture: it must not also be read as
                // menu navigation or as a pause toggle on the same frame.
                consumedInputThisFrame = true;

                // Esc cancels any rebinding except Pausing itself, where Esc is a valid target.
                if (key == Key.Escape && listeningActionId != SettingsManager.ActionPause && listeningActionId != SettingsManager.ActionRestart)
                {
                    listeningActionId = null;
                    RefreshKeybindLabels();
                    return;
                }

                settings.SetKey(listeningActionId, key);
                listeningActionId = null;
                RefreshKeybindLabels();
                return;
            }
        }

        private void BeginRebind(string actionId)
        {
            listeningActionId = actionId;
            RefreshKeybindLabels();
        }

        private void CycleResolution()
        {
            if (settings == null)
            {
                return;
            }

            int current = 0;

            for (int i = 0; i < ResolutionPresets.Length; i++)
            {
                if (ResolutionPresets[i].width == settings.ResWidth && ResolutionPresets[i].height == settings.ResHeight)
                {
                    current = (i + 1) % ResolutionPresets.Length;
                    break;
                }
            }

            (int width, int height) preset = ResolutionPresets[current];
            settings.SetResolution(preset.width, preset.height);
        }

        // ---------------------------------------------------------------- construction

        private void EnsureEventSystem()
        {
            EventSystem existing = FindAnyObjectByType<EventSystem>();

            if (existing == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(go);
                return;
            }

            // Migrate old EventSystems built with StandaloneInputModule.
            // This project uses the Input System package with activeInputHandler = 1
            // (Input System only), so StandaloneInputModule receives no input and
            // menu buttons never get clicks.
            var standalone = existing.GetComponent<StandaloneInputModule>();

            if (standalone != null)
            {
                Destroy(standalone);
            }

            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private void EnsureUI()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont("DejaVu Sans", 16);
            }

            if (mainMenuPanel != null && settingsPanel != null)
            {
                return;
            }

            var canvasObject = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<CanvasGroup>();

            BuildMainMenu(canvasObject.transform);
            BuildPauseMenu(canvasObject.transform);
            BuildSettingsMenu(canvasObject.transform);

            // This component lives on the same object conceptually; attach to canvas for tidy hierarchy.
            // (Kept as separate MenuController on Managers; UI is under Menu Canvas.)
            _ = canvasObject;
        }

        private void EnsureGameOverButtons()
        {
            Transform panel = null;

            if (hud != null && hud.gameOverPanel != null)
            {
                panel = hud.gameOverPanel.transform;
            }
            else
            {
                GameObject found = GameObject.Find("GameOverPanel");
                if (found != null)
                {
                    panel = found.transform;
                }
            }

            if (panel == null || panel.Find("RestartButtonMenu") != null)
            {
                return;
            }

            Button restart = CreateButton(panel, "RestartButtonMenu", "RESTART",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(360f, 64f), PlayRestart);
            _ = CreateButton(panel, "MenuButtonMenu", "MAIN MENU",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(360f, 64f), () => QuitToMenu());
            gameOverRestartButton = restart;
        }

        private void PlayRestart()
        {
            Restart();
        }

        private void BuildMainMenu(Transform canvas)
        {
            Image bg = CreateFullScreenImage(canvas, "MainMenuPanel", MenuBackdrop);
            mainMenuPanel = bg.gameObject;

            Vector2 center = new Vector2(0.5f, 0.5f);
            const float consoleX = -350f;
            RectTransform stage = CreateRect("EmblemStage", bg.transform, center, center, center,
                new Vector2(356f, 26f), new Vector2(620f, 650f));
            Image stageImage = AddImage(stage, new Color(0.045f, 0.085f, 0.10f));
            stageImage.sprite = foundryPlate;
            stageImage.raycastTarget = false;
            Image mark = AddImage(CreateRect("FoundryEmblem", stage, center, center, center,
                Vector2.zero, new Vector2(478f, 478f)), SignalAccent);
            mark.sprite = foundryEmblem;
            mark.raycastTarget = false;
            mark.enabled = foundryEmblem != null;

            CreateText(bg.transform, "MenuTitle", "SIGNAL\nFOUNDRY", 88, Color.white,
                TextAnchor.MiddleLeft, center, center, new Vector2(consoleX, 240f),
                new Vector2(540f, 210f), font, FontStyle.Bold);
            bestLabel = CreateText(bg.transform, "BestLabel", "BEST SCORE 0 · BEST WAVE 0", 20,
                SignalAccent, TextAnchor.MiddleLeft, center, center,
                new Vector2(consoleX, 104f), new Vector2(540f, 36f), font);

            playButton = CreateButton(bg.transform, "PlayButton", "ENTER ARENA",
                center, new Vector2(consoleX - 60f, 24f), new Vector2(420f, 76f), Play);
            CreateButton(bg.transform, "SettingsButton", "SETTINGS",
                center, new Vector2(consoleX - 60f, -66f), new Vector2(420f, 64f), OpenSettingsFromMenu);
            CreateButton(bg.transform, "QuitButton", "QUIT",
                center, new Vector2(consoleX - 60f, -143f), new Vector2(420f, 64f), QuitGame);

            menuHint = CreateText(bg.transform, "MenuHint", "",
                16, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -378f), new Vector2(1500f, 64f), font);
            RefreshMenuHint();
        }

        private void BuildPauseMenu(Transform canvas)
        {
            Image bg = CreateFullScreenImage(canvas, "PausePanel", new Color(0f, 0f, 0f, 0.78f));
            pausePanel = bg.gameObject;

            CreateText(bg.transform, "PauseTitle", "PAUSED", 80, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 260f),
                new Vector2(1200f, 100f), font, FontStyle.Bold);

            resumeButton = CreateButton(bg.transform, "ResumeButton", "RESUME",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(420f, 72f), Resume);
            CreateButton(bg.transform, "PauseSettingsButton", "SETTINGS",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 28f), new Vector2(420f, 64f), OpenSettingsFromPause);
            CreateButton(bg.transform, "PauseRestartButton", "RESTART",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -64f), new Vector2(420f, 64f), Restart);
            CreateButton(bg.transform, "PauseQuitButton", "QUIT TO MENU",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -156f), new Vector2(420f, 64f), QuitToMenu);
        }

        private void BuildSettingsMenu(Transform canvas)
        {
            Image bg = CreateFullScreenImage(canvas, "SettingsPanel", new Color(0.02f, 0.03f, 0.05f, 0.96f));
            settingsPanel = bg.gameObject;
            settingsPanel.SetActive(false);

            CreateText(bg.transform, "SettingsTitle", "SETTINGS", 64, new Color(0.45f, 0.85f, 1f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 460f),
                new Vector2(1200f, 80f), font, FontStyle.Bold);

            settingsBackButton = CreateButton(bg.transform, "SettingsBack", "← BACK",
                new Vector2(0f, 1f), new Vector2(150f, -60f), new Vector2(240f, 60f), CloseSettings);
            CreateButton(bg.transform, "SettingsReset", "RESET TO DEFAULTS",
                new Vector2(1f, 1f), new Vector2(-180f, -60f), new Vector2(300f, 60f), ResetSettings);

            Transform column = bg.transform;
            Vector2 center = new Vector2(0.5f, 0.5f);
            RectTransform gameplay = CreateRect("GameplaySettings", column, center, new Vector2(-340f, 0f), Vector2.zero);
            RectTransform graphics = CreateRect("GraphicsSettings", column, center, new Vector2(300f, 0f), Vector2.zero);

            CreateText(gameplay, "GameplayTitle", "GAMEPLAY", 24, SignalAccent, TextAnchor.MiddleLeft,
                center, center, new Vector2(-80f, 382f), new Vector2(240f, 30f), font, FontStyle.Bold);
            CreateText(graphics, "GraphicsTitle", "GRAPHICS", 24, SignalAccent, TextAnchor.MiddleLeft,
                center, center, new Vector2(-80f, 382f), new Vector2(240f, 30f), font, FontStyle.Bold);

            Vector2 sliderSize = new Vector2(230f, 28f);
            sensitivitySlider = CreateSliderRow(gameplay, "Sensitivity", 0.02f, 0.5f, settings != null ? settings.Sensitivity : 0.12f,
                327f, sliderSize, out sensitivityValue, value => settings?.SetSensitivity(value));
            volumeSlider = CreateSliderRow(gameplay, "Volume", 0f, 1f, settings != null ? settings.MasterVolume : 0.8f,
                260f, sliderSize, out volumeValue, value => settings?.SetMasterVolume(value));
            fovSlider = CreateSliderRow(gameplay, "Field Of View", 60f, 110f, settings != null ? settings.FieldOfView : 75f,
                193f, sliderSize, out fovValue, value => settings?.SetFieldOfView(value));

            invertToggle = CreateToggleRow(gameplay, "Invert Y", settings != null && settings.InvertY,
                126f, value => settings?.SetInvertY(value));
            shakeToggle = CreateToggleRow(gameplay, "Screen Shake", settings == null || settings.ShakeEnabled,
                70f, value => settings?.SetShakeEnabled(value));
            damageToggle = CreateToggleRow(gameplay, "Damage Numbers", settings == null || settings.ShowDamageNumbers,
                14f, value => settings?.SetShowDamageNumbers(value));

            qualitySlider = CreateSliderRow(graphics, "Quality Preset", 0f, SettingsManager.QualityPresetCount - 1,
                settings != null ? settings.QualityIndex : SettingsManager.DefaultQualityIndex,
                327f, sliderSize, out qualityValue, value => settings?.SetQualityIndex((int)value), wholeNumbers: true);
            postProcessingToggle = CreateToggleRow(graphics, "Post-processing", settings == null || settings.PostProcessingEnabled,
                260f, value => settings?.SetPostProcessing(value));
            fullscreenToggle = CreateToggleRow(graphics, "Fullscreen", settings == null || settings.Fullscreen,
                204f, value => settings?.SetFullscreen(value));
            vsyncToggle = CreateToggleRow(graphics, "VSync", settings == null || settings.VSyncEnabled,
                148f, value => settings?.SetVSync(value));
            fpsToggle = CreateToggleRow(graphics, "Show FPS", settings != null && settings.ShowFps,
                92f, value => settings?.SetShowFps(value));

            CreateText(graphics, "FrameRateLabel", "FRAME RATE LIMIT", 18, Color.white, TextAnchor.MiddleLeft,
                center, center, new Vector2(-80f, 36f), new Vector2(240f, 30f), font);
            frameRateButton = CreateButton(graphics, "FrameRateButton", "VSync active", center,
                new Vector2(200f, 36f), new Vector2(230f, 44f), () => settings?.CycleFrameRateLimit(), 18);
            frameRateValue = frameRateButton.GetComponentInChildren<Text>();

            CreateText(graphics, "ResolutionLabel", "RESOLUTION", 18, Color.white, TextAnchor.MiddleLeft,
                center, center, new Vector2(-80f, -20f), new Vector2(240f, 30f), font);
            Button resolutionButton = CreateButton(graphics, "ResolutionButton", "1920x1080", center,
                new Vector2(200f, -20f), new Vector2(230f, 44f), CycleResolution, 18);
            resolutionValue = resolutionButton.GetComponentInChildren<Text>();

            CreateText(column, "KeybindTitle", "KEYBINDS — click a key, then press a new key (Esc cancels)", 18,
                new Color(0.45f, 0.85f, 1f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -158f), new Vector2(1100f, 30f), font, FontStyle.Bold);

            // Compact two-column keybind grid.
            SettingsManager.BindingDef[] defs = SettingsManager.AllBindings;
            int rows = (defs.Length + 1) / 2;
            for (int i = 0; i < rows; i++)
            {
                float y = -210f - i * 45f;
                CreateKeybindRow(column, defs[i], new Vector2(-260f, y), new Vector2(420f, 44f));
                if (defs.Length > i + rows)
                {
                    CreateKeybindRow(column, defs[i + rows], new Vector2(300f, y), new Vector2(420f, 44f));
                }
            }
        }

        private void CreateKeybindRow(Transform parent, SettingsManager.BindingDef def, Vector2 position, Vector2 size)
        {
            CreateText(parent, def.Id + "Label", def.Label, 16, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position + new Vector2(-90f, 0f), new Vector2(220f, 30f), font);
            string id = def.Id;
            Button button = CreateButton(parent, def.Id + "Bind", "—", new Vector2(0.5f, 0.5f), position + new Vector2(110f, 0f),
                new Vector2(170f, 42f), () => BeginRebind(id), 16);
            keybindButtons[def.Id] = button;
        }

        // ---------------------------------------------------------------- helpers

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            return CreateRect(name, parent, anchor, anchor, new Vector2(0.5f, 0.5f), position, size);
        }

        private Text CreateText(Transform parent, string name, string content, int size, Color color, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 sizeDelta, Font useFont, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), position, sizeDelta);
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.font = useFont != null ? useFont : font;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        private static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private Image CreateFullScreenImage(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = AddImage(rect, color);
            image.raycastTarget = true;
            return image;
        }

        private Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 position,
            Vector2 size, UnityEngine.Events.UnityAction callback, int fontSize = 24)
        {
            RectTransform rect = CreateRect(name, parent, anchor, position, size);
            Image image = AddImage(rect, new Color(0.12f, 0.25f, 0.29f, 1f));
            image.sprite = foundryPlate;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = SignalAccent;
            colors.pressedColor = new Color(0.40f, 0.71f, 0.65f);
            button.colors = colors;

            Text text = CreateText(rect, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, font, FontStyle.Bold);
            text.raycastTarget = false;

            button.onClick.AddListener(callback);
            return button;
        }

        private Slider CreateSliderRow(Transform parent, string label, float min, float max, float value, float y,
            Vector2 size, out Text valueText, UnityEngine.Events.UnityAction<float> onChanged, bool wholeNumbers = false)
        {
            CreateText(parent, label + "Label", label.ToUpperInvariant(), 18, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-80f, y), new Vector2(240f, 30f), font);

            RectTransform track = CreateRect(label + "Slider", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(200f, y), size);
            Image bg = AddImage(track, new Color(0.1f, 0.12f, 0.16f, 0.95f));

            RectTransform fillRect = CreateRect("Fill", track, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);
            Image fill = AddImage(fillRect, new Color(0.2f, 0.55f, 0.9f, 1f));

            RectTransform handleRect = CreateRect("Handle", track, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 40f));
            Image handleImage = AddImage(handleRect, Color.white);

            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = value;

            valueText = CreateText(track, "Value", "", 16, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(120f, 20f), font);

            slider.onValueChanged.AddListener(v =>
            {
                onChanged?.Invoke(v);
                SyncSettingsUI();
            });

            _ = bg;
            return slider;
        }

        private Toggle CreateToggleRow(Transform parent, string label, bool start, float y,
            UnityEngine.Events.UnityAction<bool> onChanged)
        {
            CreateText(parent, label + "Label", label.ToUpperInvariant(), 18, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-80f, y), new Vector2(240f, 30f), font);

            RectTransform box = CreateRect(label + "Toggle", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(200f, y), new Vector2(36f, 36f));
            Image bgImage = AddImage(box, new Color(0.1f, 0.12f, 0.16f, 0.95f));

            RectTransform checkRect = CreateRect("Checkmark", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            Image checkImage = AddImage(checkRect, new Color(0.2f, 0.55f, 0.9f, 1f));

            Toggle toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bgImage;
            toggle.isOn = start;
            toggle.group = null;
            toggle.onValueChanged.AddListener(v =>
            {
                onChanged?.Invoke(v);
                SyncSettingsUI();
            });

            // Toggle needs the checkmark gizmo; use graphic swap via isOn-driven appearance is handled by Toggle itself.
            toggle.graphic = checkImage;
            return toggle;
        }
    }
}
