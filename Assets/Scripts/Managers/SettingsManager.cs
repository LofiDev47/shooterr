using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shooter.Managers
{
    /// <summary>
    /// Persistent player settings + key bindings, saved with PlayerPrefs.
    /// Loaded in Awake and re-applied whenever changed. The PlayerController,
    /// AudioManager and HUD read through <see cref="Instance"/> so the scene
    /// builder never needs to wire settings onto every component.
    /// </summary>
    public sealed class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        public static readonly string ActionForward = "Forward";
        public static readonly string ActionBackward = "Backward";
        public static readonly string ActionStrafeLeft = "StrafeLeft";
        public static readonly string ActionStrafeRight = "StrafeRight";
        public static readonly string ActionJump = "Jump";
        public static readonly string ActionSprint = "Sprint";
        public static readonly string ActionDash = "Dash";
        public static readonly string ActionSlide = "Slide";
        public static readonly string ActionReload = "Reload";
        public static readonly string ActionShotgun = "Shotgun";
        public static readonly string ActionMelee = "Melee";
        public static readonly string ActionInspect = "Inspect";
        public static readonly string ActionPause = "Pause";
        public static readonly string ActionRestart = "Restart";

        [Serializable]
        public sealed class BindingDef
        {
            public string Id;
            public string Label;
            public Key DefaultKey;
        }

        private static readonly BindingDef[] DefaultBindings =
        {
            new BindingDef { Id = ActionForward, Label = "MOVE FORWARD", DefaultKey = Key.W },
            new BindingDef { Id = ActionBackward, Label = "MOVE BACKWARD", DefaultKey = Key.S },
            new BindingDef { Id = ActionStrafeLeft, Label = "STRAFE LEFT", DefaultKey = Key.A },
            new BindingDef { Id = ActionStrafeRight, Label = "STRAFE RIGHT", DefaultKey = Key.D },
            new BindingDef { Id = ActionJump, Label = "JUMP", DefaultKey = Key.Space },
            new BindingDef { Id = ActionSprint, Label = "SPRINT", DefaultKey = Key.LeftShift },
            new BindingDef { Id = ActionDash, Label = "DASH", DefaultKey = Key.LeftShift },
            new BindingDef { Id = ActionSlide, Label = "SLIDE", DefaultKey = Key.C },
            new BindingDef { Id = ActionReload, Label = "RELOAD", DefaultKey = Key.R },
            new BindingDef { Id = ActionShotgun, Label = "SHOTGUN", DefaultKey = Key.G },
            new BindingDef { Id = ActionMelee, Label = "MELEE", DefaultKey = Key.E },
            new BindingDef { Id = ActionInspect, Label = "INSPECT WEAPON", DefaultKey = Key.F },
            new BindingDef { Id = ActionPause, Label = "PAUSE", DefaultKey = Key.Escape },
            new BindingDef { Id = ActionRestart, Label = "RESTART", DefaultKey = Key.R },
        };

        private static readonly List<BindingDef> Bindings = new List<BindingDef>(DefaultBindings);

        private const string KeyPrefixBind = "Shooter.Key.";
        private const string KeyWeaponBindingsVersion = "Shooter.WeaponBindingsVersion";
        private const string KeySensitivity = "Shooter.Sensitivity";
        private const string KeyVolume = "Shooter.MasterVolume";
        private const string KeyFov = "Shooter.FieldOfView";
        private const string KeyInvertY = "Shooter.InvertY";
        private const string KeyShake = "Shooter.ShakeEnabled";
        private const string KeyDamageNumbers = "Shooter.ShowDamageNumbers";
        private const string KeyShowFps = "Shooter.ShowFps";
        private const string KeyQuality = "Shooter.Quality";
        private const string KeyFullscreen = "Shooter.Fullscreen";
        private const string KeyVSync = "Shooter.VSync";
        private const string KeyResolutionWidth = "Shooter.ResWidth";
        private const string KeyResolutionHeight = "Shooter.ResHeight";
        private const string KeyBestScore = "Shooter.BestScore";
        private const string KeyBestWave = "Shooter.BestWave";

        public event Action SettingsChanged;

        public float Sensitivity { get; private set; } = 0.12f;
        public float MasterVolume { get; private set; } = 0.8f;
        public float FieldOfView { get; private set; } = 75f;
        public bool InvertY { get; private set; }
        public bool ShakeEnabled { get; private set; } = true;
        public bool ShowDamageNumbers { get; private set; } = true;
        public bool ShowFps { get; private set; }
        public int QualityIndex { get; private set; } = 2;
        public bool Fullscreen { get; private set; } = true;
        public bool VSyncEnabled { get; private set; } = true;
        public int ResWidth { get; private set; } = 1920;
        public int ResHeight { get; private set; } = 1080;

        public int BestScore { get; private set; }
        public int BestWave { get; private set; }

        private readonly Dictionary<string, Key> boundKeys = new Dictionary<string, Key>();

        private void Awake()
        {
            // Deliberately NOT DontDestroyOnLoad and deliberately no "destroy the
            // duplicate" guard. This component sits on the shared "Managers" object
            // next to GameManager, WaveSpawner, AudioManager and UpgradeManager, so
            // DontDestroyOnLoad(gameObject) moves the whole manager graph across
            // loads -- and Restart / QuitToMenu are scene reloads that need a fresh
            // graph bound to the new player, spawn points and HUD. The old graph
            // then outlived its scene and the reloaded copy was destroyed instead.
            //
            // Assigning unconditionally is also what makes the reload safe in both
            // engine call orders: if the outgoing instance is destroyed after this
            // Awake, its OnDestroy sees a different Instance and leaves it alone.
            // Settings themselves are replayed from PlayerPrefs by Load(), so
            // nothing here needs to survive a load.
            Instance = this;
            Load();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            // Awake order across the manager object is not guaranteed, and
            // AudioManager reads MasterVolume from Instance in its own Awake.
            // Re-apply once every Awake has run so a scene reload cannot leave
            // audio or quality at a default.
            Apply();
        }

        public static BindingDef[] AllBindings => DefaultBindings;

        public Key GetKey(string actionId)
        {
            if (boundKeys.TryGetValue(actionId, out Key bound))
            {
                return bound;
            }

            foreach (BindingDef def in DefaultBindings)
            {
                if (def.Id == actionId)
                {
                    return def.DefaultKey;
                }
            }

            return Key.None;
        }

        public void SetKey(string actionId, Key key)
        {
            if (string.IsNullOrEmpty(actionId) || key == Key.None)
            {
                return;
            }

            boundKeys[actionId] = key;
            PlayerPrefs.SetInt(KeyPrefixBind + actionId, (int)key);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public bool IsHeld(string actionId)
        {
            Key key = GetKey(actionId);
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].isPressed;
        }

        public bool WasPressed(string actionId)
        {
            Key key = GetKey(actionId);
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        }

        // Direct Key-based queries used by GameManager / Weapon where the
        // semantic action is fixed but we still want rebinding to apply.
        public static bool IsHeld(Key key)
        {
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].isPressed;
        }

        public static bool WasPressed(Key key)
        {
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        }

        public static string PrettyKey(Key key)
        {
            if (key == Key.None)
            {
                return "—";
            }

            string name = key.ToString();
            if (name.StartsWith("Left", StringComparison.Ordinal))
            {
                name = name.Substring(4) + " (Left)";
            }
            else if (name.StartsWith("Right", StringComparison.Ordinal))
            {
                name = name.Substring(5) + " (Right)";
            }

            if (name.StartsWith("Digit", StringComparison.Ordinal))
            {
                name = name.Substring(5);
            }
            else if (name.StartsWith("Numpad", StringComparison.Ordinal))
            {
                name = "Num " + name.Substring(6);
            }

            return name.ToUpperInvariant();
        }

        // ---------------------------------------------------------------- setters

        public void SetSensitivity(float value)
        {
            Sensitivity = Mathf.Clamp(value, 0.02f, 0.5f);
            PlayerPrefs.SetFloat(KeySensitivity, Sensitivity);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public void SetMasterVolume(float value)
        {
            MasterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyVolume, MasterVolume);
            PlayerPrefs.Save();
            ApplyAudio();
            SettingsChanged?.Invoke();
        }

        public void SetFieldOfView(float value)
        {
            FieldOfView = Mathf.Clamp(value, 60f, 110f);
            PlayerPrefs.SetFloat(KeyFov, FieldOfView);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public void SetInvertY(bool value)
        {
            InvertY = value;
            PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public void SetShakeEnabled(bool value)
        {
            ShakeEnabled = value;
            PlayerPrefs.SetInt(KeyShake, value ? 1 : 0);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public void SetShowDamageNumbers(bool value)
        {
            ShowDamageNumbers = value;
            PlayerPrefs.SetInt(KeyDamageNumbers, value ? 1 : 0);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public void SetShowFps(bool value)
        {
            ShowFps = value;
            PlayerPrefs.SetInt(KeyShowFps, value ? 1 : 0);
            PlayerPrefs.Save();
            SettingsChanged?.Invoke();
        }

        public void SetQualityIndex(int value)
        {
            QualityIndex = Mathf.Clamp(value, 0, 3);
            PlayerPrefs.SetInt(KeyQuality, QualityIndex);
            PlayerPrefs.Save();
            ApplyQuality();
            SettingsChanged?.Invoke();
        }

        public void SetFullscreen(bool value)
        {
            Fullscreen = value;
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyGraphics();
            SettingsChanged?.Invoke();
        }

        public void SetVSync(bool value)
        {
            VSyncEnabled = value;
            PlayerPrefs.SetInt(KeyVSync, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyQuality();
            SettingsChanged?.Invoke();
        }

        public void SetResolution(int width, int height)
        {
            ResWidth = Mathf.Max(640, width);
            ResHeight = Mathf.Max(360, height);
            PlayerPrefs.SetInt(KeyResolutionWidth, ResWidth);
            PlayerPrefs.SetInt(KeyResolutionHeight, ResHeight);
            PlayerPrefs.Save();
            ApplyGraphics();
            SettingsChanged?.Invoke();
        }

        public void RecordRun(int score, int wave)
        {
            bool changed = false;

            if (score > BestScore)
            {
                BestScore = score;
                PlayerPrefs.SetInt(KeyBestScore, BestScore);
                changed = true;
            }

            if (wave > BestWave)
            {
                BestWave = wave;
                PlayerPrefs.SetInt(KeyBestWave, BestWave);
                changed = true;
            }

            if (changed)
            {
                PlayerPrefs.Save();
                SettingsChanged?.Invoke();
            }
        }

        public void ResetToDefaults()
        {
            Sensitivity = 0.12f;
            MasterVolume = 0.8f;
            FieldOfView = 75f;
            InvertY = false;
            ShakeEnabled = true;
            ShowDamageNumbers = true;
            ShowFps = false;
            QualityIndex = 2;
            Fullscreen = true;
            VSyncEnabled = true;
            ResWidth = 1920;
            ResHeight = 1080;
            boundKeys.Clear();

            PlayerPrefs.SetFloat(KeySensitivity, Sensitivity);
            PlayerPrefs.SetFloat(KeyVolume, MasterVolume);
            PlayerPrefs.SetFloat(KeyFov, FieldOfView);
            PlayerPrefs.SetInt(KeyInvertY, 0);
            PlayerPrefs.SetInt(KeyShake, 1);
            PlayerPrefs.SetInt(KeyDamageNumbers, 1);
            PlayerPrefs.SetInt(KeyShowFps, 0);
            PlayerPrefs.SetInt(KeyQuality, QualityIndex);
            PlayerPrefs.SetInt(KeyFullscreen, 1);
            PlayerPrefs.SetInt(KeyVSync, 1);
            PlayerPrefs.SetInt(KeyResolutionWidth, ResWidth);
            PlayerPrefs.SetInt(KeyResolutionHeight, ResHeight);

            foreach (BindingDef def in DefaultBindings)
            {
                PlayerPrefs.DeleteKey(KeyPrefixBind + def.Id);
            }

            PlayerPrefs.Save();
            Apply();
            SettingsChanged?.Invoke();
        }

        // ---------------------------------------------------------------- apply

        public void Apply()
        {
            ApplyAudio();
            ApplyQuality();
            ApplyGraphics();
        }

        private void ApplyAudio()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.masterVolume = MasterVolume;
            }
        }

        private void ApplyQuality()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            int count = QualitySettings.names != null ? QualitySettings.names.Length : 1;
            int target = Mathf.Clamp(QualityIndex, 0, Mathf.Max(0, count - 1));
            QualitySettings.SetQualityLevel(target, true);
            QualitySettings.vSyncCount = VSyncEnabled ? 1 : 0;
            QualitySettings.antiAliasing = QualityIndex <= 0 ? 0 : QualityIndex == 1 ? 2 : QualityIndex == 2 ? 4 : 8;
            Application.targetFrameRate = VSyncEnabled ? -1 : (QualityIndex >= 3 ? -1 : 60);
        }

        private void ApplyGraphics()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (!Application.isEditor && Screen.currentResolution.width != ResWidth)
            {
                Screen.SetResolution(ResWidth, ResHeight,
                    Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed,
                    Screen.currentResolution.refreshRateRatio);
            }
            else if (Application.isEditor)
            {
                Screen.fullScreen = false;
            }
        }

        // ---------------------------------------------------------------- persistence

        private void Load()
        {
            Sensitivity = PlayerPrefs.GetFloat(KeySensitivity, 0.12f);
            MasterVolume = PlayerPrefs.GetFloat(KeyVolume, 0.8f);
            FieldOfView = PlayerPrefs.GetFloat(KeyFov, 75f);
            InvertY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            ShakeEnabled = PlayerPrefs.GetInt(KeyShake, 1) == 1;
            ShowDamageNumbers = PlayerPrefs.GetInt(KeyDamageNumbers, 1) == 1;
            ShowFps = PlayerPrefs.GetInt(KeyShowFps, 0) == 1;
            QualityIndex = PlayerPrefs.GetInt(KeyQuality, 2);
            Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;
            VSyncEnabled = PlayerPrefs.GetInt(KeyVSync, 1) == 1;
            ResWidth = PlayerPrefs.GetInt(KeyResolutionWidth, 1920);
            ResHeight = PlayerPrefs.GetInt(KeyResolutionHeight, 1080);
            BestScore = PlayerPrefs.GetInt(KeyBestScore, 0);
            BestWave = PlayerPrefs.GetInt(KeyBestWave, 0);

            MigrateWeaponBindings();
            boundKeys.Clear();

            foreach (BindingDef def in DefaultBindings)
            {
                int saved = PlayerPrefs.GetInt(KeyPrefixBind + def.Id, int.MinValue);

                if (saved != int.MinValue)
                {
                    boundKeys[def.Id] = (Key)SavedKey(saved, def.DefaultKey);
                }
            }

            Apply();
        }

        private static int SavedKey(int value, Key fallback)
        {
            return System.Enum.IsDefined(typeof(Key), value) ? value : (int)fallback;
        }

        private static void MigrateWeaponBindings()
        {
            if (PlayerPrefs.GetInt(KeyWeaponBindingsVersion, 0) >= 1)
                return;

            string melee = KeyPrefixBind + ActionMelee;
            string shotgun = KeyPrefixBind + ActionShotgun;
            if (PlayerPrefs.GetInt(melee, 0) == (int)Key.V)
                PlayerPrefs.DeleteKey(melee);
            if (PlayerPrefs.GetInt(shotgun, 0) == (int)Key.F)
                PlayerPrefs.DeleteKey(shotgun);
            PlayerPrefs.SetInt(KeyWeaponBindingsVersion, 1);
            PlayerPrefs.Save();
        }
    }
}
