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
        [Header("Wiring (scene builder)")]
        public GameManager gameManager;
        public SettingsManager settings;
        public HUDController hud;
        public PlayerController player;

        [Header("Panels (built at runtime if null)")]
        public GameObject mainMenuPanel;
        public GameObject pausePanel;
        public GameObject settingsPanel;

        private Font font;

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
        private Toggle fullscreenToggle;
        private Toggle vsyncToggle;
        private Button resolutionButton;
        private Text resolutionValue;
        private readonly Dictionary<string, Button> keybindButtons = new Dictionary<string, Button>();

        private string listeningActionId;
        private bool settingsOpen;
        private GameState settingsReturnTo = GameState.Menu;

        private static readonly (int width, int height)[] ResolutionPresets =
        {
            (1280, 720),
            (1600, 900),
            (1920, 1080),
            (2560, 1440),
        };

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
            if (settings != null)
            {
                settings.SettingsChanged -= HandleSettingsChanged;
            }
        }

        private void Update()
        {
            ResolveReferences();
            UpdateRebindCapture();
            SyncPanels();
        }

        private void ResolveReferences()
        {
            if (settings == null)
            {
                settings = SettingsManager.Instance != null ? SettingsManager.Instance : FindAnyObjectByType<SettingsManager>();
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
            settingsOpen = false;
            listeningActionId = null;
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

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(settingsOpen);
            }

            if (settingsOpen)
            {
                if (mainMenuPanel != null)
                {
                    mainMenuPanel.SetActive(false);
                }

                if (pausePanel != null)
                {
                    pausePanel.SetActive(false);
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            GameState state = gameManager != null ? gameManager.State : GameState.Menu;

            if (mainMenuPanel != null)
            {
                mainMenuPanel.SetActive(state == GameState.Menu);
            }

            if (pausePanel != null)
            {
                pausePanel.SetActive(state == GameState.Paused);
            }

            if (bestLabel != null && settings != null)
            {
                bestLabel.text = $"BEST SCORE {settings.BestScore}  ·  BEST WAVE {settings.BestWave}";
            }

            if (state == GameState.Playing)
            {
                if (gameManager != null && gameManager.IsPlaying && player != null && player.ControlsEnabled)
                {
                    // GameManager owns the lock; MenuController just makes sure the
                    // cursor is hidden while playing if something left it visible.
                    if (Cursor.visible)
                    {
                        player.LockCursor(true);
                    }
                }
            }
            else if (state == GameState.Menu || state == GameState.Paused || state == GameState.GameOver)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
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
                    qualityValue.text = QualityName(settings.QualityIndex);
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

            if (fullscreenToggle != null)
            {
                fullscreenToggle.SetIsOnWithoutNotify(settings.Fullscreen);
            }

            if (vsyncToggle != null)
            {
                vsyncToggle.SetIsOnWithoutNotify(settings.VSyncEnabled);
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
        }

        private static string QualityName(int index)
        {
            return index switch
            {
                0 => "LOW",
                1 => "MEDIUM",
                2 => "HIGH",
                _ => "ULTRA",
            };
        }

        private void RefreshKeybindLabels()
        {
            foreach (KeyValuePair<string, Button> pair in keybindButtons)
            {
                if (pair.Value != null)
                {
                    string label = settings != null ? SettingsManager.PrettyKey(settings.GetKey(pair.Key)) : "—";
                    pair.Value.GetComponentInChildren<Text>().text = label;
                    if (listeningActionId == pair.Key)
                    {
                        pair.Value.GetComponentInChildren<Text>().text = "...";
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

            int current = 2;

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
            _ = restart;
        }

        private void PlayRestart()
        {
            Restart();
        }

        private void BuildMainMenu(Transform canvas)
        {
            Image bg = CreateFullScreenImage(canvas, "MainMenuPanel", new Color(0.015f, 0.02f, 0.03f, 0.94f));
            mainMenuPanel = bg.gameObject;

            Text title = CreateText(bg.transform, "MenuTitle", "SHOOTER", 110, new Color(0.45f, 0.85f, 1f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 300f),
                new Vector2(1200f, 140f), font, FontStyle.Bold);

            Text subtitle = CreateText(bg.transform, "MenuSubtitle", "ARENA FPS · ENDLESS WAVES · CHOOSE YOUR AUGMENTS",
                22, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 215f), new Vector2(1200f, 40f), font);

            bestLabel = CreateText(bg.transform, "BestLabel", "BEST SCORE 0 · BEST WAVE 0", 20,
                new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 160f), new Vector2(1200f, 36f), font);

            CreateButton(bg.transform, "PlayButton", "PLAY",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(420f, 76f), Play);
            CreateButton(bg.transform, "SettingsButton", "SETTINGS",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(420f, 64f), OpenSettingsFromMenu);
            CreateButton(bg.transform, "QuitButton", "QUIT",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -146f), new Vector2(420f, 64f), QuitGame);

            CreateText(bg.transform, "MenuHint",
                "WASD move · SHIFT sprint · SPACE jump · LMB fire · RMB aim · V melee · C slide · R reload · F shotgun · ESC pause",
                16, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -260f), new Vector2(1500f, 30f), font);

            _ = title;
            _ = subtitle;
        }

        private void BuildPauseMenu(Transform canvas)
        {
            Image bg = CreateFullScreenImage(canvas, "PausePanel", new Color(0f, 0f, 0f, 0.78f));
            pausePanel = bg.gameObject;

            CreateText(bg.transform, "PauseTitle", "PAUSED", 80, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 260f),
                new Vector2(1200f, 100f), font, FontStyle.Bold);

            CreateButton(bg.transform, "ResumeButton", "RESUME",
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

            CreateButton(bg.transform, "SettingsBack", "← BACK",
                new Vector2(0f, 1f), new Vector2(150f, -60f), new Vector2(240f, 60f), CloseSettings);
            CreateButton(bg.transform, "SettingsReset", "RESET TO DEFAULTS",
                new Vector2(1f, 1f), new Vector2(-180f, -60f), new Vector2(300f, 60f), ResetSettings);

            Transform column = bg.transform;

            sensitivitySlider = CreateSliderRow(column, "Sensitivity", 0.02f, 0.5f, settings != null ? settings.Sensitivity : 0.12f,
                370f, new Vector2(560f, 32f), out sensitivityValue, value => settings?.SetSensitivity(value));
            volumeSlider = CreateSliderRow(column, "Volume", 0f, 1f, settings != null ? settings.MasterVolume : 0.8f,
                320f, new Vector2(560f, 32f), out volumeValue, value => settings?.SetMasterVolume(value));
            fovSlider = CreateSliderRow(column, "Field Of View", 60f, 110f, settings != null ? settings.FieldOfView : 75f,
                270f, new Vector2(560f, 32f), out fovValue, value => settings?.SetFieldOfView(value));
            qualitySlider = CreateSliderRow(column, "Quality", 0f, 3f, settings != null ? settings.QualityIndex : 2,
                220f, new Vector2(560f, 32f), out qualityValue, value => settings?.SetQualityIndex(Mathf.RoundToInt(value)), 1f);

            invertToggle = CreateToggleRow(column, "Invert Y", settings != null && settings.InvertY,
                168f, value => settings?.SetInvertY(value));
            shakeToggle = CreateToggleRow(column, "Screen Shake", settings == null || settings.ShakeEnabled,
                118f, value => settings?.SetShakeEnabled(value));
            damageToggle = CreateToggleRow(column, "Damage Numbers", settings == null || settings.ShowDamageNumbers,
                68f, value => settings?.SetShowDamageNumbers(value));
            fullscreenToggle = CreateToggleRow(column, "Fullscreen", settings == null || settings.Fullscreen,
                18f, value => settings?.SetFullscreen(value));
            vsyncToggle = CreateToggleRow(column, "VSync", settings == null || settings.VSyncEnabled,
                -32f, value => settings?.SetVSync(value));

            CreateText(column, "ResolutionLabel", "RESOLUTION", 18, Color.white, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-80f, -92f), new Vector2(160f, 30f), font);
            resolutionButton = CreateButton(column, "ResolutionButton", "1920x1080",
                new Vector2(0.5f, 0.5f), new Vector2(260f, -92f), new Vector2(340f, 44f), CycleResolution, 18);
            resolutionValue = resolutionButton.GetComponentInChildren<Text>();

            CreateText(column, "KeybindTitle", "KEYBINDS — click a key, then press a new key (Esc cancels)", 18,
                new Color(0.45f, 0.85f, 1f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -158f), new Vector2(1100f, 30f), font, FontStyle.Bold);

            // Compact two-column keybind grid.
            SettingsManager.BindingDef[] defs = SettingsManager.AllBindings;
            const int rows = 6;
            for (int i = 0; i < rows; i++)
            {
                float y = -210f - i * 52f;
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
            Image image = AddImage(rect, new Color(0.12f, 0.16f, 0.22f, 0.95f));
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.75f, 0.9f, 1f);
            colors.pressedColor = new Color(0.35f, 0.65f, 0.9f);
            button.colors = colors;

            Text text = CreateText(rect, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, font, FontStyle.Bold);
            text.raycastTarget = false;

            button.onClick.AddListener(callback);
            return button;
        }

        private Slider CreateSliderRow(Transform parent, string label, float min, float max, float value, float y,
            Vector2 size, out Text valueText, UnityEngine.Events.UnityAction<float> onChanged, float step = 0f)
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
            slider.value = value;
            if (step > 0f)
            {
                slider.navigation = new Navigation { mode = Navigation.Mode.None };
                // Snap by quantizing in the callback wrapper below.
            }

            valueText = CreateText(track, "Value", "", 16, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(120f, 20f), font);

            slider.onValueChanged.AddListener(v =>
            {
                if (step > 0f)
                {
                    v = Mathf.Round(v / step) * step;
                    slider.SetValueWithoutNotify(v);
                }

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
