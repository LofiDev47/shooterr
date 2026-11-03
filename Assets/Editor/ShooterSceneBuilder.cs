using System.Collections.Generic;
using Shooter.Enemies;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using Shooter.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Builds the entire playable arena from code so the scene is reproducible:
    /// menu item "Shooter/Rebuild Arena Scene" or -executeMethod Shooter.EditorTools.ShooterSceneBuilder.Build.
    /// Only built-in primitives and generated materials are used.
    /// </summary>
    public static class ShooterSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Arena.unity";

        private const string MaterialsFolder = "Assets/Materials";
        private const string PrefabsFolder = "Assets/Prefabs";
        private const string ScenesFolder = "Assets/Scenes";

        private const float ArenaHalfSize = 22f;
        private const float WallHeight = 6f;
        private const float SpawnRadius = 16.5f;

        [MenuItem("Shooter/Rebuild Arena Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/Scripts");
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ScenesFolder);

            ArenaMaterials materials = CreateMaterials();
            GameObject enemyPrefab = CreateEnemyPrefab(materials);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureLighting(materials);
            BuildArena(materials);
            PlayerRig rig = BuildPlayer(materials);
            BuildPostProcessing(rig);
            List<Transform> spawnPoints = BuildSpawnPoints();
            ManagersRig managers = BuildManagers(enemyPrefab, rig, spawnPoints);
            HUDController hud = BuildHud(rig, managers);

            // Wired after the HUD exists, so these components can raise UI feedback.
            rig.Weapon.hud = hud;
            managers.Waves.hud = hud;

            BuildAmmoPickups(materials, rig, hud);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"[Shooter] Scene built and saved to {ScenePath}.");
        }

        // ---------------------------------------------------------------- materials

        private sealed class ArenaMaterials
        {
            public Material Floor;
            public Material Wall;
            public Material Cover;
            public Material CoverAlt;
            public Material Enemy;
            public Material EnemyEye;
            public Material Gun;
            public Material GunAccent;
            public Material Impact;
            public Material Muzzle;
            public Material Tracer;
            public Material Ammo;
            public Material Spark;
            public Material EnemyDebris;
            public Material Accent;
            public Material Skybox;
        }

        private static ArenaMaterials CreateMaterials()
        {
            return new ArenaMaterials
            {
                Floor = GetOrCreateLitMaterial("Floor", new Color(0.17f, 0.18f, 0.21f), 0.15f, 0f),
                Wall = GetOrCreateLitMaterial("Wall", new Color(0.30f, 0.33f, 0.39f), 0.2f, 0f),
                Cover = GetOrCreateLitMaterial("Cover", new Color(0.48f, 0.31f, 0.16f), 0.25f, 0f),
                CoverAlt = GetOrCreateLitMaterial("CoverAlt", new Color(0.20f, 0.36f, 0.28f), 0.25f, 0f),
                Enemy = GetOrCreateLitMaterial("Enemy", new Color(0.62f, 0.15f, 0.17f), 0.35f, 0f),
                EnemyEye = GetOrCreateUnlitMaterial("EnemyEye", new Color(1f, 0.72f, 0.2f)),
                Gun = GetOrCreateLitMaterial("Gun", new Color(0.11f, 0.11f, 0.13f), 0.55f, 0.6f),
                GunAccent = GetOrCreateLitMaterial("GunAccent", new Color(0.38f, 0.38f, 0.43f), 0.5f, 0.7f),
                Impact = GetOrCreateUnlitMaterial("Impact", new Color(0.03f, 0.03f, 0.03f)),
                Muzzle = GetOrCreateUnlitMaterial("Muzzle", new Color(1f, 0.85f, 0.35f)),
                Tracer = GetOrCreateUnlitMaterial("Tracer", new Color(1f, 0.92f, 0.55f)),
                Ammo = GetOrCreateUnlitMaterial("Ammo", new Color(0.45f, 1f, 0.5f)),
                Spark = GetOrCreateUnlitMaterial("Spark", new Color(1f, 0.87f, 0.45f)),
                EnemyDebris = GetOrCreateLitMaterial("EnemyDebris", new Color(0.42f, 0.09f, 0.11f), 0.3f, 0f),
                Accent = GetOrCreateEmissiveMaterial("Accent", new Color(0.08f, 0.12f, 0.16f),
                    new Color(0.15f, 0.75f, 1f) * 1.6f),
                Skybox = GetOrCreateSkyboxMaterial(),
            };
        }

        private static Material GetOrCreateLitMaterial(string name, Color color, float smoothness, float metallic)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = GetOrCreateMaterialAsset(name, shader);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateUnlitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material material = GetOrCreateMaterialAsset(name, shader);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Lit material with emission enabled, so bloom makes it glow.</summary>
        private static Material GetOrCreateEmissiveMaterial(string name, Color baseColor, Color emission)
        {
            Material material = GetOrCreateLitMaterial(name, baseColor, 0.45f, 0f);
            material.EnableKeyword("_EMISSION");

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
            }

            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateMaterialAsset(string name, Shader shader)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        private static Material GetOrCreateSkyboxMaterial()
        {
            Shader shader = Shader.Find("Skybox/Procedural");

            if (shader == null)
            {
                Debug.LogWarning("[Shooter] Skybox/Procedural not found; scene will use a flat background.");
                return null;
            }

            Material material = GetOrCreateMaterialAsset("Skybox", shader);

            if (material.HasProperty("_SkyTint")) material.SetColor("_SkyTint", new Color(0.22f, 0.28f, 0.42f));
            if (material.HasProperty("_GroundColor")) material.SetColor("_GroundColor", new Color(0.10f, 0.10f, 0.12f));
            if (material.HasProperty("_Exposure")) material.SetFloat("_Exposure", 1.1f);
            if (material.HasProperty("_AtmosphereThickness")) material.SetFloat("_AtmosphereThickness", 0.85f);
            if (material.HasProperty("_SunSize")) material.SetFloat("_SunSize", 0.04f);

            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- lighting

        private static void ConfigureLighting(ArenaMaterials materials)
        {
            var lightObject = new GameObject("Sun");
            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = materials.Skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.48f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.26f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.18f);
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 95f;

            // URP is assigned per quality level in this project, so check both places.
            if (GraphicsSettings.defaultRenderPipeline == null && QualitySettings.renderPipeline == null)
            {
                Debug.LogWarning("[Shooter] No render pipeline asset is assigned; URP materials may render incorrectly.");
            }
        }

        // ---------------------------------------------------------------- arena

        private static void BuildArena(ArenaMaterials materials)
        {
            var arena = new GameObject("Arena");

            // Ground: top surface sits exactly at y = 0.
            GameObject ground = CreatePrimitive("Ground", PrimitiveType.Cube, new Vector3(0f, -0.25f, 0f),
                new Vector3(ArenaHalfSize * 2f, 0.5f, ArenaHalfSize * 2f), materials.Floor, arena.transform);

            // Perimeter walls.
            CreatePrimitive("Wall North", PrimitiveType.Cube, new Vector3(0f, WallHeight * 0.5f, ArenaHalfSize - 0.3f),
                new Vector3(ArenaHalfSize * 2f, WallHeight, 0.6f), materials.Wall, arena.transform);
            CreatePrimitive("Wall South", PrimitiveType.Cube, new Vector3(0f, WallHeight * 0.5f, -ArenaHalfSize + 0.3f),
                new Vector3(ArenaHalfSize * 2f, WallHeight, 0.6f), materials.Wall, arena.transform);
            CreatePrimitive("Wall East", PrimitiveType.Cube, new Vector3(ArenaHalfSize - 0.3f, WallHeight * 0.5f, 0f),
                new Vector3(0.6f, WallHeight, ArenaHalfSize * 2f), materials.Wall, arena.transform);
            CreatePrimitive("Wall West", PrimitiveType.Cube, new Vector3(-ArenaHalfSize + 0.3f, WallHeight * 0.5f, 0f),
                new Vector3(0.6f, WallHeight, ArenaHalfSize * 2f), materials.Wall, arena.transform);

            // Cover layout is fixed so every rebuild produces the same arena.
            Vector3[] coverPositions =
            {
                new Vector3(6f, 0.6f, -4f),
                new Vector3(-7f, 0.6f, 3f),
                new Vector3(0f, 0.75f, 7f),
                new Vector3(9f, 0.5f, 7f),
                new Vector3(-9f, 0.5f, -6f),
                new Vector3(4f, 0.4f, 12f),
                new Vector3(-5f, 0.4f, 13f),
                new Vector3(12f, 0.5f, -9f),
                new Vector3(-13f, 0.5f, -10f),
            };

            Vector3[] coverScales =
            {
                new Vector3(2f, 1.2f, 2f),
                new Vector3(2.4f, 1.2f, 2.4f),
                new Vector3(3f, 1.5f, 1.2f),
                new Vector3(4f, 1f, 0.6f),
                new Vector3(4f, 1f, 0.6f),
                new Vector3(1.5f, 0.8f, 1.5f),
                new Vector3(1.5f, 0.8f, 1.5f),
                new Vector3(2f, 1f, 2f),
                new Vector3(2f, 1f, 2f),
            };

            float[] coverAngles = { 0f, 20f, 0f, 20f, -15f, 45f, -45f, 0f, 30f };

            for (int i = 0; i < coverPositions.Length; i++)
            {
                Material material = i % 3 == 1 ? materials.CoverAlt : materials.Cover;
                CreatePrimitive($"Cover {i + 1:00}", PrimitiveType.Cube, coverPositions[i], coverScales[i], material,
                    arena.transform, Quaternion.Euler(0f, coverAngles[i], 0f));
            }

            // Corner pillars give the arena a readable silhouette.
            Vector3[] pillarPositions =
            {
                new Vector3(13f, 2f, 13f),
                new Vector3(-13f, 2f, 13f),
                new Vector3(13f, 2f, -13f),
                new Vector3(-13f, 2f, -13f),
            };

            foreach (Vector3 position in pillarPositions)
            {
                CreatePrimitive("Pillar", PrimitiveType.Cube, position, new Vector3(1.2f, 4f, 1.2f),
                    materials.Wall, arena.transform);
            }

            // Emissive trim: a strip along each wall base and a cap on every pillar.
            // Paired with the bloom in BuildPostProcessing these glow.
            CreatePrimitive("Trim North", PrimitiveType.Cube, new Vector3(0f, 0.28f, ArenaHalfSize - 0.68f),
                new Vector3(ArenaHalfSize * 2f - 2f, 0.12f, 0.06f), materials.Accent, arena.transform);
            CreatePrimitive("Trim South", PrimitiveType.Cube, new Vector3(0f, 0.28f, -ArenaHalfSize + 0.68f),
                new Vector3(ArenaHalfSize * 2f - 2f, 0.12f, 0.06f), materials.Accent, arena.transform);
            CreatePrimitive("Trim East", PrimitiveType.Cube, new Vector3(ArenaHalfSize - 0.68f, 0.28f, 0f),
                new Vector3(0.06f, 0.12f, ArenaHalfSize * 2f - 2f), materials.Accent, arena.transform);
            CreatePrimitive("Trim West", PrimitiveType.Cube, new Vector3(-ArenaHalfSize + 0.68f, 0.28f, 0f),
                new Vector3(0.06f, 0.12f, ArenaHalfSize * 2f - 2f), materials.Accent, arena.transform);

            foreach (Vector3 pillar in pillarPositions)
            {
                CreatePrimitive("Pillar Cap", PrimitiveType.Cube, pillar + new Vector3(0f, 2.06f, 0f),
                    new Vector3(1.34f, 0.12f, 1.34f), materials.Accent, arena.transform);
            }

            ground.isStatic = true;
        }

        // ---------------------------------------------------------------- post processing

        /// <summary>
        /// Creates a global URP volume (bloom, vignette, ACES tonemapping) and enables
        /// post-processing on the player camera.
        /// </summary>
        private static void BuildPostProcessing(PlayerRig rig)
        {
            const string profilePath = "Assets/Settings/ShooterPostProcessing.asset";

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) != null)
            {
                AssetDatabase.DeleteAsset(profilePath);
            }

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.85f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.55f;

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.3f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.45f;
            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.02f, 0.02f, 0.05f);

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            ColorAdjustments adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.postExposure.overrideState = true;
            adjustments.postExposure.value = 0.15f;
            adjustments.contrast.overrideState = true;
            adjustments.contrast.value = 10f;
            adjustments.saturation.overrideState = true;
            adjustments.saturation.value = 8f;

            EditorUtility.SetDirty(profile);

            var volumeObject = new GameObject("Post Processing");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;

            if (rig.Camera != null)
            {
                rig.Camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }

            Debug.Log("[Shooter] Post-processing volume created (bloom, vignette, ACES).");
        }

        private static List<Transform> BuildSpawnPoints()
        {
            var parent = new GameObject("Spawn Points");
            var points = new List<Transform>();

            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                var point = new GameObject($"Spawn {i + 1:00}");
                point.transform.SetParent(parent.transform, false);
                point.transform.position = new Vector3(
                    Mathf.Cos(angle) * SpawnRadius,
                    0f,
                    Mathf.Sin(angle) * SpawnRadius);
                points.Add(point.transform);
            }

            return points;
        }

        // ---------------------------------------------------------------- player

        private sealed class PlayerRig
        {
            public GameObject Root;
            public PlayerController Controller;
            public PlayerHealth Health;
            public Camera Camera;
            public Weapon Weapon;
        }

        private static PlayerRig BuildPlayer(ArenaMaterials materials)
        {
            var player = new GameObject("Player");
            player.layer = 2; // "Ignore Raycast": the player's own weapon ray must never hit it.
            player.transform.position = new Vector3(0f, 0.2f, -12f);

            CharacterController controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.04f;

            PlayerController playerController = player.AddComponent<PlayerController>();
            PlayerHealth playerHealth = player.AddComponent<PlayerHealth>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.62f, 0f);

            var cameraObject = new GameObject("PlayerCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pivot.transform, false);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            playerController.cameraPivot = pivot.transform;
            playerController.playerCamera = camera;

            Weapon weapon = BuildWeapon(camera.transform, materials, playerController);

            return new PlayerRig
            {
                Root = player,
                Controller = playerController,
                Health = playerHealth,
                Camera = camera,
                Weapon = weapon,
            };
        }

        private static Weapon BuildWeapon(Transform cameraTransform, ArenaMaterials materials, PlayerController player)
        {
            var weaponObject = new GameObject("Weapon");
            weaponObject.transform.SetParent(cameraTransform, false);
            weaponObject.transform.localPosition = new Vector3(0.22f, -0.16f, 0.35f);

            // Simple 3-cube gun model.
            CreatePrimitive("Body", PrimitiveType.Cube, new Vector3(0f, 0f, 0.18f),
                new Vector3(0.10f, 0.12f, 0.42f), materials.Gun, weaponObject.transform);
            CreatePrimitive("Barrel", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.48f),
                new Vector3(0.05f, 0.06f, 0.32f), materials.GunAccent, weaponObject.transform);
            CreatePrimitive("Grip", PrimitiveType.Cube, new Vector3(0f, -0.1f, 0.06f),
                new Vector3(0.06f, 0.16f, 0.1f), materials.Gun, weaponObject.transform,
                Quaternion.Euler(12f, 0f, 0f));

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(weaponObject.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.02f, 0.66f);

            // Muzzle flash: emissive sphere plus a short-lived point light.
            GameObject flash = CreatePrimitive("MuzzleFlash", PrimitiveType.Sphere, Vector3.zero,
                new Vector3(0.14f, 0.14f, 0.14f), materials.Muzzle, muzzle.transform);
            StripCollider(flash);

            var flashLightObject = new GameObject("MuzzleLight");
            flashLightObject.transform.SetParent(muzzle.transform, false);
            Light flashLight = flashLightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.82f, 0.45f);
            flashLight.range = 8f;
            flashLight.intensity = 7f;
            flashLight.shadows = LightShadows.None;

            LineRenderer tracer = weaponObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.widthMultiplier = 0.012f;
            tracer.numCapVertices = 0;
            tracer.alignment = LineAlignment.View;
            tracer.textureMode = LineTextureMode.Stretch;
            tracer.shadowCastingMode = ShadowCastingMode.Off;
            tracer.receiveShadows = false;
            tracer.sharedMaterial = materials.Tracer;
            tracer.enabled = false;

            AudioSource audioSource = weaponObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.8f;

            Weapon weapon = weaponObject.AddComponent<Weapon>();
            weapon.aimCamera = cameraTransform.GetComponent<Camera>();
            weapon.player = player;
            weapon.muzzle = muzzle.transform;
            weapon.muzzleFlash = flash;
            weapon.muzzleLight = flashLight;
            weapon.tracer = tracer;
            weapon.audioSource = audioSource;
            weapon.impactMaterial = materials.Impact;
            weapon.sparkMaterial = materials.Spark;

            flash.SetActive(false);
            flashLight.enabled = false;

            return weapon;
        }

        // ---------------------------------------------------------------- enemies

        private static GameObject CreateEnemyPrefab(ArenaMaterials materials)
        {
            var root = new GameObject("Enemy");

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.3f;

            var body = CreatePrimitive("Body", PrimitiveType.Capsule, new Vector3(0f, 0.9f, 0f),
                new Vector3(0.8f, 0.9f, 0.8f), materials.Enemy, root.transform);
            StripCollider(body);

            GameObject eye = CreatePrimitive("Eye", PrimitiveType.Cube, new Vector3(0f, 1.45f, 0.33f),
                new Vector3(0.42f, 0.12f, 0.08f), materials.EnemyEye, root.transform);
            StripCollider(eye);

            EnemyHealth enemyHealth = root.AddComponent<EnemyHealth>();
            enemyHealth.debrisMaterial = materials.EnemyDebris;
            root.AddComponent<EnemyAI>();

            string path = $"{PrefabsFolder}/Enemy.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] Enemy prefab saved to {path}.");
            return prefab;
        }

        // ---------------------------------------------------------------- managers

        private sealed class ManagersRig
        {
            public GameManager Game;
            public WaveSpawner Waves;
            public AudioManager Audio;
        }

        private static ManagersRig BuildManagers(GameObject enemyPrefab, PlayerRig rig, List<Transform> spawnPoints)
        {
            var managers = new GameObject("Managers");

            GameManager gameManager = managers.AddComponent<GameManager>();
            WaveSpawner waveSpawner = managers.AddComponent<WaveSpawner>();
            AudioManager audioManager = managers.AddComponent<AudioManager>();

            waveSpawner.enemyPrefab = enemyPrefab;
            waveSpawner.playerTarget = rig.Health;
            waveSpawner.spawnPoints = spawnPoints.ToArray();

            gameManager.playerHealth = rig.Health;
            gameManager.playerController = rig.Controller;
            gameManager.waveSpawner = waveSpawner;

            return new ManagersRig { Game = gameManager, Waves = waveSpawner, Audio = audioManager };
        }

        // ---------------------------------------------------------------- HUD

        private static HUDController BuildHud(PlayerRig rig, ManagersRig managers)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont("DejaVu Sans", 16);
                Debug.LogWarning("[Shooter] Built-in font missing; using an OS font for the HUD.");
            }

            var canvasObject = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Health (bottom left).
            RectTransform healthPanel = CreateRect("HealthPanel", canvasObject.transform, new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(36f, 36f), new Vector2(320f, 64f));

            Text healthLabel = CreateText(healthPanel, "HealthLabel", "HEALTH", 14, new Color(1f, 1f, 1f, 0.55f),
                TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 36f),
                new Vector2(320f, 20f), font);

            RectTransform healthBar = CreateRect("HealthBar", healthPanel, new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, new Vector2(320f, 26f));
            Image healthBackground = AddImage(healthBar, new Color(0f, 0f, 0f, 0.55f));

            RectTransform healthFillRect = CreateRect("HealthFill", healthBar, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            healthFillRect.offsetMin = new Vector2(2f, 2f);
            healthFillRect.offsetMax = new Vector2(-2f, -2f);
            Image healthFill = AddImage(healthFillRect, new Color(0.35f, 0.9f, 0.45f));
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            healthFill.fillAmount = 1f;

            Text healthText = CreateText(healthBar, "HealthText", "100", 17, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 26f), font,
                FontStyle.Bold);

            // Ammo (bottom right).
            RectTransform ammoPanel = CreateRect("AmmoPanel", canvasObject.transform, new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(-36f, 36f), new Vector2(360f, 110f));

            Text ammoText = CreateText(ammoPanel, "AmmoText", "30 / 150", 40, Color.white, TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 40f), new Vector2(360f, 48f), font,
                FontStyle.Bold);

            Text ammoLabel = CreateText(ammoPanel, "AmmoLabel", "AMMO", 14, new Color(1f, 1f, 1f, 0.55f),
                TextAnchor.MiddleRight, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 92f),
                new Vector2(360f, 20f), font);

            Text reloadText = CreateText(ammoPanel, "ReloadText", "RELOADING", 18, new Color(1f, 0.82f, 0.3f),
                TextAnchor.MiddleRight, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f),
                new Vector2(360f, 24f), font, FontStyle.Bold);
            reloadText.enabled = false;

            // Wave (top center).
            Text waveText = CreateText(canvasObject.transform, "WaveText", "GET READY", 32, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                new Vector2(700f, 44f), font, FontStyle.Bold);

            Text statusText = CreateText(canvasObject.transform, "StatusText", "FIRST WAVE IN 3", 18,
                new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 26f), font);

            // Score (top right).
            Text scoreText = CreateText(canvasObject.transform, "ScoreText", "SCORE 0", 26, Color.white,
                TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -24f),
                new Vector2(420f, 36f), font, FontStyle.Bold);

            // Crosshair (center).
            RectTransform crosshair = CreateRect("Crosshair", canvasObject.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            Color crosshairColor = new Color(1f, 1f, 1f, 0.85f);
            CreateCrosshairBar(crosshair, "Top", new Vector2(0f, 9f), new Vector2(2f, 9f), crosshairColor);
            CreateCrosshairBar(crosshair, "Bottom", new Vector2(0f, -9f), new Vector2(2f, 9f), crosshairColor);
            CreateCrosshairBar(crosshair, "Left", new Vector2(-9f, 0f), new Vector2(9f, 2f), crosshairColor);
            CreateCrosshairBar(crosshair, "Right", new Vector2(9f, 0f), new Vector2(9f, 2f), crosshairColor);

            // Hit marker: four diagonal ticks that flash when a shot lands on an enemy.
            RectTransform hitMarker = CreateRect("HitMarker", canvasObject.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            CanvasGroup hitMarkerGroup = hitMarker.gameObject.AddComponent<CanvasGroup>();
            hitMarkerGroup.alpha = 0f;

            Color hitMarkerColor = new Color(1f, 0.95f, 0.75f, 1f);
            CreateCrosshairBar(hitMarker, "HitTopLeft", new Vector2(-6f, 6f), new Vector2(2f, 9f),
                hitMarkerColor, -45f);
            CreateCrosshairBar(hitMarker, "HitTopRight", new Vector2(6f, 6f), new Vector2(2f, 9f),
                hitMarkerColor, 45f);
            CreateCrosshairBar(hitMarker, "HitBottomLeft", new Vector2(-6f, -6f), new Vector2(2f, 9f),
                hitMarkerColor, 45f);
            CreateCrosshairBar(hitMarker, "HitBottomRight", new Vector2(6f, -6f), new Vector2(2f, 9f),
                hitMarkerColor, -45f);

            // Pickup toast (just above the crosshair, hidden until something happens).
            Text toastText = CreateText(canvasObject.transform, "ToastText", string.Empty, 22,
                new Color(0.65f, 1f, 0.5f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 72f), new Vector2(700f, 34f), font, FontStyle.Bold);
            toastText.enabled = false;

            // Control hints (bottom center).
            Text hintText = CreateText(canvasObject.transform, "HintText",
                "WASD move   ·   SHIFT sprint   ·   SPACE jump   ·   LMB fire   ·   RMB aim   ·   CTRL slide   ·   R reload   ·   ESC free cursor",
                15, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1400f, 24f), font);

            // Damage flash sits above the HUD but below the game-over screen.
            Image damageImage = CreateFullScreenImage(canvasObject.transform, "DamageFlash",
                new Color(0.8f, 0.05f, 0.05f, 0f));

            // Game over (last sibling = drawn on top).
            Image gameOverPanel = CreateFullScreenImage(canvasObject.transform, "GameOverPanel",
                new Color(0f, 0f, 0f, 0.82f));

            Text gameOverTitle = CreateText(gameOverPanel.transform, "GameOverTitle", "GAME OVER", 84,
                new Color(0.95f, 0.2f, 0.2f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1200f, 110f), font, FontStyle.Bold);

            Text gameOverScore = CreateText(gameOverPanel.transform, "GameOverScore", "SCORE 0      WAVE 1", 34,
                Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 20f), new Vector2(1200f, 50f), font, FontStyle.Bold);

            Text gameOverHint = CreateText(gameOverPanel.transform, "GameOverHint", "PRESS R TO RESTART", 24,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(1200f, 40f), font);

            gameOverPanel.gameObject.SetActive(false);

            HUDController hud = canvasObject.AddComponent<HUDController>();
            hud.playerHealth = rig.Health;
            hud.weapon = rig.Weapon;
            hud.gameManager = managers.Game;
            hud.waveSpawner = managers.Waves;
            hud.healthFill = healthFill;
            hud.healthText = healthText;
            hud.ammoText = ammoText;
            hud.reloadText = reloadText;
            hud.waveText = waveText;
            hud.statusText = statusText;
            hud.scoreText = scoreText;
            hud.gameOverPanel = gameOverPanel.gameObject;
            hud.gameOverScoreText = gameOverScore;
            hud.hintText = hintText;
            hud.toastText = toastText;
            hud.hitMarkerGroup = hitMarkerGroup;

            DamageFlash flash = canvasObject.AddComponent<DamageFlash>();
            flash.playerHealth = rig.Health;
            flash.flashImage = damageImage;

            // Keep the compiler honest about intentionally unused labels.
            _ = healthLabel;
            _ = ammoLabel;
            _ = gameOverTitle;
            _ = gameOverHint;
            _ = healthBackground;

            return hud;
        }

        // ---------------------------------------------------------------- pickups

        /// <summary>
        /// Places respawning ammo crates in open ground away from spawn points and cover,
        /// so the run stays sustainable as waves get longer.
        /// </summary>
        private static void BuildAmmoPickups(ArenaMaterials materials, PlayerRig rig, HUDController hud)
        {
            var parent = new GameObject("Ammo Pickups");

            Vector3[] positions =
            {
                new Vector3(0f, 0.55f, 0f),
                new Vector3(9f, 0.55f, -12f),
                new Vector3(-9f, 0.55f, 12f),
                new Vector3(14f, 0.55f, 6f),
                new Vector3(-14f, 0.55f, -6f),
                new Vector3(0f, 0.55f, -7f),
            };

            foreach (Vector3 position in positions)
            {
                var crate = new GameObject("Ammo Crate");
                crate.transform.SetParent(parent.transform, false);
                crate.transform.position = position;

                GameObject box = CreatePrimitive("Box", PrimitiveType.Cube, Vector3.zero,
                    new Vector3(0.42f, 0.3f, 0.42f), materials.Ammo, crate.transform);
                StripCollider(box);

                GameObject lid = CreatePrimitive("Lid", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f),
                    new Vector3(0.3f, 0.3f, 0.3f), materials.Ammo, crate.transform,
                    Quaternion.Euler(0f, 45f, 0f));
                StripCollider(lid);

                AmmoPickup pickup = crate.AddComponent<AmmoPickup>();
                pickup.weapon = rig.Weapon;
                pickup.player = rig.Controller;
                pickup.hud = hud;
            }

            Debug.Log($"[Shooter] Placed {positions.Length} ammo crates.");
        }

        private static void CreateCrosshairBar(Transform parent, string name, Vector2 position, Vector2 size,
            Color color, float rotation = 0f)
        {
            RectTransform rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position, size);

            if (!Mathf.Approximately(rotation, 0f))
            {
                rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }

            AddImage(rect, color);
        }

        // ---------------------------------------------------------------- helpers

        private static GameObject CreatePrimitive(string name, PrimitiveType type, Vector3 position, Vector3 scale,
            Material material, Transform parent = null, Quaternion? rotation = null)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.transform.localScale = scale;

            if (rotation.HasValue)
            {
                gameObject.transform.localRotation = rotation.Value;
            }

            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();

            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            return gameObject;
        }

        private static void StripCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();

            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);

            var rect = (RectTransform)gameObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateFullScreenImage(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);

            var rect = (RectTransform)gameObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize, Color color,
            TextAnchor alignment, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Font font,
            FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = CreateRect(name, parent, anchor, pivot, position, size);

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
                string name = System.IO.Path.GetFileName(path);

                if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                {
                    EnsureFolder(parent);
                }

                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
