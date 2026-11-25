using System.Collections.Generic;
using Shooter.Enemies;
using Shooter.Effects;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using Shooter.Upgrades;
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
    /// Builds the Foundry geometry, combat encounters and interface.
    /// </summary>
    public static class ShooterSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/FoundryArena.unity";

        private const string MaterialsFolder = "Assets/Materials";
        private const string PrefabsFolder = "Assets/Prefabs/Foundry";
        private const string ScenesFolder = "Assets/Scenes";

        private const float ArenaHalfSize = 22f;
        private const float WallHeight = 6f;

        [MenuItem("Shooter/Rebuild Arena Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/Scripts");
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(FoundryAssets.Materials);
            EnsureFolder(ScenesFolder);

            ArenaMaterials materials = CreateMaterials();
            EnemyPrefabRig enemyPrefabs = CreateEnemyPrefabs(materials);
            GameObject bossPrefab = CreateBossPrefab(materials, enemyPrefabs);
            GameObject ammoCachePrefab = CreateAmmoCachePrefab(materials);
            GameObject healthCachePrefab = CreateHealthCachePrefab(materials);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureLighting(materials);
            BuildArena(materials);
            PlayerRig rig = BuildPlayer(materials);
            BuildPostProcessing(rig);
            List<Transform> spawnPoints = BuildSpawnPoints();
            ManagersRig managers = BuildManagers(enemyPrefabs, bossPrefab, ammoCachePrefab, healthCachePrefab,
                rig, spawnPoints);
            HUDController hud = BuildHud(rig, managers);
            FoundryHudSkin.Apply(hud.gameObject);

            // Wired after the HUD exists, so these components can raise UI feedback.
            rig.Weapon.hud = hud;
            managers.Waves.hud = hud;

            MenuController menu = BuildMenus(rig, managers, hud);

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
            public Material TrooperBody;
            public Material TrooperDark;
            public Material TrooperGlow;
            public Material RunnerBody;
            public Material RunnerDark;
            public Material RunnerGlow;
            public Material BulwarkBody;
            public Material BulwarkDark;
            public Material BulwarkGlow;
            public Material MarksmanBody;
            public Material MarksmanDark;
            public Material MarksmanGlow;
            public Material EliteBody;
            public Material EliteDark;
            public Material EliteGlow;
            public Material BossBody;
            public Material BossDark;
            public Material BossTrim;
            public Material BossGlow;
            public Material Gun;
            public Material GunAccent;
            public Material Impact;
            public Material Muzzle;
            public Material Tracer;
            public Material Ammo;
            public Material Spark;
            public Material EnemyDebris;
            public Material Accent;
            public Material AmmoCache;
            public Material HealthCache;
            public Material Skybox;
            public Material DarkMetal;
            public Material Stone;
            public Material Hazard;
            public Material EnemyBolt;
            public Material Grip;
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
                TrooperBody = GetOrCreateLitMaterial("TrooperBody", new Color(0.42f, 0.075f, 0.085f), 0.35f, 0f),
                TrooperDark = GetOrCreateLitMaterial("TrooperDark", new Color(0.030f, 0.030f, 0.035f), 0.15f, 0f),
                TrooperGlow = GetOrCreateUnlitMaterial("TrooperGlow", new Color(1f, 0.55f, 0.18f)),
                RunnerBody = GetOrCreateLitMaterial("RunnerBody", new Color(0.38f, 0.08f, 0.48f), 0.3f, 0f),
                RunnerDark = GetOrCreateLitMaterial("RunnerDark", new Color(0.025f, 0.022f, 0.035f), 0.15f, 0f),
                RunnerGlow = GetOrCreateUnlitMaterial("RunnerGlow", new Color(1f, 0.26f, 0.82f)),
                BulwarkBody = GetOrCreateLitMaterial("BulwarkBody", new Color(0.18f, 0.33f, 0.38f), 0.45f, 0.35f),
                BulwarkDark = GetOrCreateLitMaterial("BulwarkDark", new Color(0.028f, 0.032f, 0.04f), 0.2f, 0.1f),
                BulwarkGlow = GetOrCreateUnlitMaterial("BulwarkGlow", new Color(0.16f, 0.85f, 1f)),
                MarksmanBody = GetOrCreateLitMaterial("MarksmanBody", new Color(0.07f, 0.32f, 0.24f), 0.35f, 0f),
                MarksmanDark = GetOrCreateLitMaterial("MarksmanDark", new Color(0.025f, 0.03f, 0.03f), 0.15f, 0f),
                MarksmanGlow = GetOrCreateUnlitMaterial("MarksmanGlow", new Color(0.30f, 1f, 0.52f)),
                EliteBody = GetOrCreateLitMaterial("EliteBody", new Color(0.50f, 0.30f, 0.06f), 0.5f, 0.25f),
                EliteDark = GetOrCreateLitMaterial("EliteDark", new Color(0.035f, 0.028f, 0.020f), 0.2f, 0f),
                EliteGlow = GetOrCreateUnlitMaterial("EliteGlow", new Color(1f, 0.58f, 0.14f)),
                // The Warden: dark lacquered carapace, blackened plate and bronze
                // trim, with a furnace core that doubles as its weak point.
                BossBody = GetOrCreateLitMaterial("BossBody", new Color(0.34f, 0.085f, 0.10f), 0.52f, 0.55f),
                BossDark = GetOrCreateLitMaterial("BossDark", new Color(0.075f, 0.070f, 0.080f), 0.42f, 0.45f),
                BossTrim = GetOrCreateLitMaterial("BossTrim", new Color(0.47f, 0.21f, 0.11f), 0.70f, 0.88f),
                BossGlow = GetOrCreateEmissiveMaterial("BossGlow", new Color(0.20f, 0.07f, 0.02f),
                    new Color(1f, 0.44f, 0.10f) * 2.2f),
                Gun = GetOrCreateLitMaterial("Gun", new Color(0.11f, 0.11f, 0.13f), 0.55f, 0.6f),
                GunAccent = GetOrCreateLitMaterial("GunAccent", new Color(0.38f, 0.38f, 0.43f), 0.5f, 0.7f),
                Impact = GetOrCreateUnlitMaterial("Impact", new Color(0.03f, 0.03f, 0.03f)),
                Muzzle = GetOrCreateUnlitMaterial("Muzzle", new Color(1f, 0.85f, 0.35f)),
                Tracer = GetOrCreateUnlitMaterial("Tracer", new Color(0.30f, 0.82f, 1f)),
                Ammo = GetOrCreateUnlitMaterial("Ammo", new Color(0.45f, 1f, 0.5f)),
                Spark = GetOrCreateUnlitMaterial("Spark", new Color(1f, 0.87f, 0.45f)),
                EnemyDebris = GetOrCreateLitMaterial("EnemyDebris", new Color(0.42f, 0.09f, 0.11f), 0.3f, 0f),
                Accent = GetOrCreateEmissiveMaterial("Accent", new Color(0.08f, 0.12f, 0.16f),
                    new Color(0.15f, 0.75f, 1f) * 1.6f),
                AmmoCache = GetOrCreateEmissiveMaterial("AmmoCache", new Color(0.24f, 0.16f, 0.05f),
                    new Color(1f, 0.68f, 0.2f) * 1.7f),
                HealthCache = GetOrCreateEmissiveMaterial("HealthCache", new Color(0.05f, 0.22f, 0.08f),
                    new Color(0.25f, 1f, 0.35f) * 1.7f),
                DarkMetal = GetOrCreateLitMaterial("DarkMetal", new Color(0.10f, 0.12f, 0.15f), 0.28f, 0.7f),
                Stone = GetOrCreateLitMaterial("Stone", new Color(0.37f, 0.38f, 0.42f), 0.08f, 0f),
                Hazard = GetOrCreateEmissiveMaterial("Hazard", new Color(0.85f, 0.23f, 0.04f),
                    new Color(1f, 0.28f, 0.03f) * 1.7f),
                EnemyBolt = GetOrCreateUnlitMaterial("EnemyBolt", new Color(1f, 0.24f, 0.1f)),
                Grip = GetOrCreateLitMaterial("Grip", new Color(0.035f, 0.04f, 0.045f), 0.08f, 0.05f),
                Skybox = GetOrCreateSkyboxMaterial(),
            };
        }

        private static Material GetOrCreateLitMaterial(string name, Color color, float smoothness, float metallic)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");
            if (existing != null)
                return existing;

            // Palette materials start on the Foundry shader family so a clean rebuild
            // keeps the procedural surface look instead of falling back to flat URP Lit.
            Shader shader = Shader.Find(FoundryShaderMigration.ShaderNameFor(name));

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

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
            Material existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");
            if (existing != null)
                return existing;

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
            Material existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");
            if (existing != null)
                return existing;

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
            Material existing = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Skybox.mat");
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Skybox/Procedural");

            if (shader == null)
            {
                Debug.LogWarning("[Shooter] Skybox/Procedural not found; scene will use a flat background.");
                return null;
            }

            Material material = GetOrCreateMaterialAsset("Skybox", shader);

            if (material.HasProperty("_SkyTint")) material.SetColor("_SkyTint", new Color(0.13f, 0.18f, 0.30f));
            if (material.HasProperty("_GroundColor")) material.SetColor("_GroundColor", new Color(0.05f, 0.07f, 0.10f));
            if (material.HasProperty("_Exposure")) material.SetFloat("_Exposure", 1.35f);
            if (material.HasProperty("_AtmosphereThickness")) material.SetFloat("_AtmosphereThickness", 1.1f);
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
            sun.intensity = 1.5f;
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

            // Emissive trim along the wall base: with the bloom in BuildPostProcessing
            // these glow and give the arena a clear boundary.
            CreatePrimitive("Trim North", PrimitiveType.Cube, new Vector3(0f, 0.28f, ArenaHalfSize - 0.68f),
                new Vector3(ArenaHalfSize * 2f - 2f, 0.12f, 0.06f), materials.Accent, arena.transform);
            CreatePrimitive("Trim South", PrimitiveType.Cube, new Vector3(0f, 0.28f, -ArenaHalfSize + 0.68f),
                new Vector3(ArenaHalfSize * 2f - 2f, 0.12f, 0.06f), materials.Accent, arena.transform);
            CreatePrimitive("Trim East", PrimitiveType.Cube, new Vector3(ArenaHalfSize - 0.68f, 0.28f, 0f),
                new Vector3(0.06f, 0.12f, ArenaHalfSize * 2f - 2f), materials.Accent, arena.transform);
            CreatePrimitive("Trim West", PrimitiveType.Cube, new Vector3(-ArenaHalfSize + 0.68f, 0.28f, 0f),
                new Vector3(0.06f, 0.12f, ArenaHalfSize * 2f - 2f), materials.Accent, arena.transform);

            BuildFurnace(materials, arena.transform);
            BuildLoadingDocks(materials, arena.transform);
            BuildCoverClusters(materials, arena.transform);
            BuildBarrels(materials, arena.transform);
            BuildFloorGuides(materials, arena.transform);
            BuildArenaDetails(materials, arena.transform);
            ApplyArenaModels(arena.transform);
            BuildTransferBridge(arena.transform);

            ground.isStatic = true;
        }

        private static void ApplyArenaModels(Transform arena)
        {
            var models = new Dictionary<string, GameObject>();
            foreach (MeshRenderer renderer in arena.GetComponentsInChildren<MeshRenderer>(true))
            {
                string label = renderer.name;
                string asset = label switch
                {
                    "Ground" => "Ground",
                    "Wall Pier" => "Pillar",
                    "Container" or "L Wall" or "L Wall Return" => "Cover",
                    "Wall North" or "Wall South" or "Wall East" or "Wall West" => "Wall",
                    "Barrel" => "Drum",
                    "Barrel Ring" => "Conduit",
                    _ when label.StartsWith("Pipe ") => "Conduit",
                    _ when label.StartsWith("Platform ") => "Platform",
                    _ when label.StartsWith("Ramp ") => "Ramp",
                    _ when label.StartsWith("Trim ") => "Trim",
                    _ => "Hardware",
                };

                if (!models.TryGetValue(asset, out GameObject source))
                {
                    source = FoundryAssets.LoadProp(asset);
                    models.Add(asset, source);
                }

                GameObject visual = Object.Instantiate(source, renderer.transform, false);
                visual.name = "Foundry Visual";
                if (asset == "Hardware" || asset == "Drum" || asset == "Conduit")
                    visual.GetComponentInChildren<MeshRenderer>().sharedMaterial = renderer.sharedMaterial;
                else
                    FoundryAssets.Skin(visual);
                renderer.enabled = false;
            }
        }

        private const float DockHeight = 4.6f;
        private const float DockCenterX = 15.5f;

        private static void BuildFurnace(ArenaMaterials materials, Transform parent)
        {
            // The refractory shell and incandescent slots face the south receiving yard.
            Vector3 center = new Vector3(-2f, 0f, 10f);
            CreatePrimitive("Furnace Plinth", PrimitiveType.Cube, center + Vector3.up * 0.35f,
                new Vector3(6f, 0.7f, 5f), materials.Stone, parent);
            CreatePrimitive("Furnace Shell", PrimitiveType.Cube, center + Vector3.up * 2.7f,
                new Vector3(5f, 4f, 4f), materials.DarkMetal, parent);
            for (int row = 0; row < 3; row++)
            {
                GameObject slot = CreatePrimitive("Furnace Vent", PrimitiveType.Cube,
                    center + new Vector3(0f, 1.5f + row * 0.85f, -2.03f),
                    new Vector3(3.9f, 0.32f, 0.1f), materials.Hazard, parent);
                StripCollider(slot);
            }

            foreach (float x in new[] { -1.45f, 1.45f })
            {
                CreatePrimitive("Exhaust Stack", PrimitiveType.Cube, center + new Vector3(x, 6.3f, 0f),
                    new Vector3(1.3f, 3.2f, 1.3f), materials.Stone, parent);
                CreatePrimitive("Exhaust Collar", PrimitiveType.Cube, center + new Vector3(x, 7.7f, 0f),
                    new Vector3(1.5f, 0.3f, 1.5f), materials.DarkMetal, parent);
            }
        }

        private static void BuildLoadingDocks(ArenaMaterials materials, Transform parent)
        {
            const float rampRun = 9f;
            const float thickness = 0.4f;
            float angle = Mathf.Atan2(DockHeight, rampRun) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(rampRun * rampRun + DockHeight * DockHeight);

            foreach (int side in new[] { -1, 1 })
            {
                float x = side * DockCenterX;
                string name = side < 0 ? "West" : "East";
                CreatePrimitive($"Platform {name} Dock", PrimitiveType.Cube,
                    new Vector3(x, DockHeight / 2f, 0f), new Vector3(8f, DockHeight, 6f),
                    materials.Wall, parent);

                foreach (int end in new[] { -1, 1 })
                {
                    Quaternion rotation = Quaternion.Euler(end * angle, 0f, 0f);
                    Vector3 surface = new Vector3(x, DockHeight / 2f, end * (3f + rampRun / 2f));
                    CreatePrimitive($"Ramp {name} {(end < 0 ? "South" : "North")}", PrimitiveType.Cube,
                        surface - rotation * Vector3.up * (thickness / 2f),
                        new Vector3(3.8f, thickness, length), materials.CoverAlt, parent, rotation);
                }
            }
        }

        private static void BuildTransferBridge(Transform arena)
        {
            const int spanCount = 6;
            const float spanLength = 3.9f;
            const float deckHeight = DockHeight + 0.6f;
            const float deckWidth = 2.4f;
            const float halfLength = spanCount * spanLength / 2f;

            GameObject span = FoundryAssets.LoadProp("CatwalkSpan");
            GameObject landing = FoundryAssets.LoadProp("CatwalkLanding");
            GameObject pier = FoundryAssets.LoadProp("CatwalkPier");

            var deck = new GameObject("Transfer Bridge");
            deck.transform.SetParent(arena, false);
            deck.transform.localPosition = Vector3.up * deckHeight;
            deck.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // A continuous box under the deck keeps traversal smooth over model seams.
            BoxCollider deckCollider = deck.AddComponent<BoxCollider>();
            deckCollider.center = new Vector3(0f, -0.15f, 0f);
            deckCollider.size = new Vector3(deckWidth, 0.3f, 2f * halfLength);

            for (int index = 0; index < spanCount; index++)
            {
                float z = (index + 0.5f) * spanLength - halfLength;
                PlaceCatwalkModule(span, deck.transform, new Vector3(0f, 0f, z), Quaternion.identity);
            }

            foreach (int end in new[] { -1, 1 })
            {
                // The landing model starts at platform height and descends toward +Z.
                Vector3 position = new Vector3(0f, DockHeight - deckHeight, end * halfLength);
                Quaternion rotation = Quaternion.Euler(0f, end == 1 ? 0f : 180f, 0f);
                GameObject steps = PlaceCatwalkModule(landing, deck.transform, position, rotation);

                for (int index = 0; index < 2; index++)
                {
                    float height = 0.4f - 0.2f * index;
                    BoxCollider tread = steps.AddComponent<BoxCollider>();
                    tread.center = new Vector3(0f, height / 2f, 0.27f + 0.56f * index);
                    tread.size = new Vector3(deckWidth, height, 0.57f);
                }
            }

            foreach (float z in new[] { -spanLength * 2f, spanLength * 2f })
            {
                GameObject support = PlaceCatwalkModule(pier, deck.transform,
                    new Vector3(0f, -deckHeight, z), Quaternion.identity);
                support.transform.localScale = new Vector3(1f, deckHeight / 2.6f, 1f);
                BoxCollider column = support.AddComponent<BoxCollider>();
                column.center = Vector3.up * 1.15f;
                column.size = new Vector3(0.75f, 2.3f, 1.8f);
            }
        }

        private static GameObject PlaceCatwalkModule(GameObject asset, Transform parent,
            Vector3 position, Quaternion rotation)
        {
            var module = new GameObject(asset.name);
            module.transform.SetParent(parent, false);
            module.transform.SetLocalPositionAndRotation(position, rotation);

            // The FBX root carries Blender's axis conversion. Rotate the mount instead.
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(asset, module.transform);
            FoundryAssets.Skin(visual);
            return module;
        }

        private static void BuildCoverClusters(ArenaMaterials materials, Transform parent)
        {
            // A staggered freight yard leaves a broad approach beneath the bridge.
            foreach (Vector3 position in new[] { new Vector3(-8f, 1f, -9f),
                new Vector3(7f, 1f, -15f), new Vector3(9f, 1f, 10f) })
            {
                CreatePrimitive("Container", PrimitiveType.Cube, position, new Vector3(4f, 2f, 2f),
                    materials.Cover, parent);
            }

            CreateLWall(materials, parent, new Vector3(4f, 0f, -5f), 25f);
            CreateLWall(materials, parent, new Vector3(-9f, 0f, 16f), 155f);
        }

        /// <summary>Two boxes at right angles: a wall with a short return.</summary>
        private static void CreateLWall(ArenaMaterials materials, Transform parent, Vector3 position, float angle)
        {
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);

            CreatePrimitive("L Wall", PrimitiveType.Cube, new Vector3(position.x, 0.8f, position.z),
                new Vector3(3.6f, 1.6f, 0.8f), materials.Cover, parent, rotation);

            Vector3 offset = rotation * new Vector3(1.4f, 0f, 1.4f);
            CreatePrimitive("L Wall Return", PrimitiveType.Cube,
                new Vector3(position.x + offset.x, 0.8f, position.z + offset.z),
                new Vector3(0.8f, 1.6f, 3.2f), materials.Cover, parent, rotation);
        }

        /// <summary>
        /// Barrel props: cheap cover that reads as "industrial", with an emissive
        /// hazard ring so they stand out from the crates. The barrel keeps its
        /// collider for cover; the ring is visual only.
        /// </summary>
        private static void BuildBarrels(ArenaMaterials materials, Transform parent)
        {
            Vector3[] barrelPositions =
            {
                new Vector3(-10.7f, 0.45f, -9f),
                new Vector3(-10.8f, 0.45f, -8f),
                new Vector3(7.8f, 0.45f, 11.6f),
                new Vector3(9f, 0.45f, 11.8f),
            };

            foreach (Vector3 position in barrelPositions)
            {
                GameObject barrel = CreatePrimitive("Barrel", PrimitiveType.Cylinder, position,
                    new Vector3(0.72f, 0.45f, 0.72f), materials.Cover, parent);
                barrel.isStatic = true;

                GameObject ring = CreatePrimitive("Barrel Ring", PrimitiveType.Cylinder,
                    position + new Vector3(0f, 0.18f, 0f), new Vector3(0.78f, 0.08f, 0.78f),
                    materials.Accent, parent);
                StripCollider(ring);
                ring.isStatic = true;
            }
        }

        /// <summary>Glowing floor strips mark the crossing beneath the transfer bridge.</summary>
        private static void BuildFloorGuides(ArenaMaterials materials, Transform parent)
        {
            Vector3[] positions =
            {
                new Vector3(8.2f, 0.02f, 0f),
                new Vector3(-8.2f, 0.02f, 0f),
                new Vector3(0f, 0.02f, 8.2f),
                new Vector3(0f, 0.02f, -8.2f),
            };

            foreach (Vector3 position in positions)
            {
                GameObject guide = CreatePrimitive("Floor Guide", PrimitiveType.Cube, position,
                    Mathf.Abs(position.x) > Mathf.Abs(position.z)
                        ? new Vector3(4f, 0.02f, 0.3f)
                        : new Vector3(0.3f, 0.02f, 4f),
                    materials.Accent, parent);
                StripCollider(guide);
            }
        }

        private static void BuildArenaDetails(ArenaMaterials materials, Transform parent)
        {
            BuildWallRuns(materials, parent);
            BuildEnvironmentLights(materials, parent);
            BuildHazardChevrons(materials, parent);
        }

        private static void BuildEnvironmentLights(ArenaMaterials materials, Transform parent)
        {
            // Warm wall fixtures and cyan side accents: the arena now has warm/cool contrast,
            // which makes the pale grey architecture read instead of bleach out.
            BuildWallLamp(materials, parent, new Vector3(0f, WallHeight - 1.6f, ArenaHalfSize - 1.1f),
                new Vector3(4.8f, 0.32f, 0.2f), new Color(1f, 0.72f, 0.35f), materials.Hazard);
            BuildWallLamp(materials, parent, new Vector3(0f, WallHeight - 1.6f, -ArenaHalfSize + 1.1f),
                new Vector3(4.8f, 0.32f, 0.2f), new Color(1f, 0.72f, 0.35f), materials.Hazard);
            BuildWallLamp(materials, parent, new Vector3(ArenaHalfSize - 1.1f, WallHeight - 2.6f, 0f),
                new Vector3(0.2f, 0.32f, 4.8f), new Color(0.25f, 0.75f, 1f), materials.Accent);
            BuildWallLamp(materials, parent, new Vector3(-ArenaHalfSize + 1.1f, WallHeight - 2.6f, 0f),
                new Vector3(0.2f, 0.32f, 4.8f), new Color(0.25f, 0.75f, 1f), materials.Accent);

            // A soft fill from high over the transfer bridge keeps the combat readable while the
            // perimeter goes moody. It does not replace shadows; it sculpts mid-tones.
            GameObject fill = new GameObject("Arena Fill Light");
            fill.transform.SetParent(parent, false);
            fill.transform.position = new Vector3(0f, WallHeight + 5f, 0f);
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(0.35f, 0.48f, 0.65f);
            fillLight.intensity = 90f;
            fillLight.range = 55f;
            fillLight.shadows = LightShadows.None;
        }

        private static void BuildWallLamp(ArenaMaterials materials, Transform parent, Vector3 position, Vector3 scale, Color glow, Material glowMaterial)
        {
            GameObject housing = CreatePrimitive("Lamp Housing", PrimitiveType.Cube, position, scale, materials.DarkMetal, parent);
            StripCollider(housing);

            CreatePrimitive("Lamp Glow", PrimitiveType.Cube,
                position + (position.x != 0f ? new Vector3(-Mathf.Sign(position.x) * 0.08f, 0, 0f) : new Vector3(0, 0, -Mathf.Sign(position.z) * 0.08f)),
                position.x != 0f ? new Vector3(0.06f, scale.y, scale.z) : new Vector3(scale.x, scale.y, 0.06f),
                glowMaterial, parent);

            GameObject lightObject = new GameObject("Wall Fixture Light");
            lightObject.transform.SetParent(housing.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 18f;
            light.color = glow;
            light.shadows = LightShadows.None;
        }

        private static void BuildWallRuns(ArenaMaterials materials, Transform parent)
        {
            // Corner posts give the perimeter a silhouette instead of looking like a box.
            BuildWallCorners(materials, parent);

            float top = WallHeight - 2f;

            // Horizontal service runs along each wall, elevated above the fight.
            CreatePipe("Pipe North", materials.DarkMetal, parent, new Vector3(0f, top, ArenaHalfSize - 0.95f),
                new Vector3(ArenaHalfSize * 2f - 6f, 0, 0));
            CreatePipe("Pipe South", materials.DarkMetal, parent, new Vector3(0f, top, -ArenaHalfSize + 0.95f),
                new Vector3(ArenaHalfSize * 2f - 6f, 0, 0));
            CreatePipe("Pipe East", materials.DarkMetal, parent, new Vector3(ArenaHalfSize - 0.95f, top + 0.8f, 0f),
                new Vector3(0, 0, ArenaHalfSize * 2f - 6f));
            CreatePipe("Pipe West", materials.DarkMetal, parent, new Vector3(-ArenaHalfSize + 0.95f, top + 0.8f, 0f),
                new Vector3(0, 0, ArenaHalfSize * 2f - 6f));

            // Deep wall plates break up the long runs. They are thin but they still catch the light.
            foreach (float x in new[] { -16f, 0f, 16f })
            {
                BuildWallPlate(materials, parent, new Vector3(x, 4.2f, ArenaHalfSize - 0.75f), new Vector3(7f, 5.5f, 0.12f));
                BuildWallPlate(materials, parent, new Vector3(x, 4.2f, -ArenaHalfSize + 0.75f), new Vector3(7f, 5.5f, 0.12f));
            }

            foreach (float z in new[] { -16f, 0f, 16f })
            {
                BuildWallPlate(materials, parent, new Vector3(ArenaHalfSize - 0.75f, 4.2f, z), new Vector3(0.12f, 5.5f, 7f));
                BuildWallPlate(materials, parent, new Vector3(-ArenaHalfSize + 0.75f, 4.2f, z), new Vector3(0.12f, 5.5f, 7f));
            }

            // Vertical light rails, evenly spacedâ€”reads as a deliberate light rig rather than lights on box walls.
            foreach (float x in new[] { -24f, -8f, 8f, 24f })
            {
                BuildLuminousRail(materials, parent, new Vector3(x, 4.5f, ArenaHalfSize - 0.68f), new Vector3(0.28f, 5.6f, 0.1f));
                BuildLuminousRail(materials, parent, new Vector3(x, 4.5f, -ArenaHalfSize + 0.68f), new Vector3(0.28f, 5.6f, 0.1f));
            }

            foreach (float z in new[] { -24f, -8f, 8f, 24f })
            {
                BuildLuminousRail(materials, parent, new Vector3(ArenaHalfSize - 0.68f, 4.5f, z), new Vector3(0.1f, 5.6f, 0.28f));
                BuildLuminousRail(materials, parent, new Vector3(-ArenaHalfSize + 0.68f, 4.5f, z), new Vector3(0.1f, 5.6f, 0.28f));
            }
        }

        private static void BuildWallCorners(ArenaMaterials materials, Transform parent)
        {
            foreach (var xSign in new[] { -1f, 1f })
            {
                foreach (var zSign in new[] { -1f, 1f })
                {
                    Vector3 position = new Vector3(
                        xSign * (ArenaHalfSize - 0.55f), WallHeight * 0.5f,
                        zSign * (ArenaHalfSize - 0.55f));

                    // Stone pier, dark plinth, emissive crown â€” different materials, not same wall replicated.
                    CreatePrimitive("Wall Pier", PrimitiveType.Cube, position,
                        new Vector3(1.4f, WallHeight, 1.4f), materials.Stone, parent);
                    CreatePrimitive("Pier Plinth", PrimitiveType.Cube,
                        position + Vector3.down * (WallHeight * 0.5f - 0.5f),
                        new Vector3(2f, 1f, 2f), materials.DarkMetal, parent);
                    CreatePrimitive("Pier Crown", PrimitiveType.Cube,
                        position + Vector3.up * (WallHeight * 0.5f - 0.7f),
                        new Vector3(1.7f, 0.16f, 1.7f), materials.Hazard, parent);
                }
            }
        }

        private static void CreatePipe(string name, Material material, Transform parent, Vector3 position, Vector3 extent)
        {
            // Extent is the horizontal axis length; the pipe's other two axes are a fixed 0.6 m diameter.
            bool runsX = Mathf.Abs(extent.x) > 0.001f;
            Vector3 scale = runsX
                ? new Vector3(0.6f, extent.x * 0.5f, 0.6f)
                : new Vector3(0.6f, extent.z * 0.5f, 0.6f);

            Quaternion rotation = runsX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            GameObject pipe = CreatePrimitive(name, PrimitiveType.Cylinder, position, scale, material, parent);
            pipe.transform.rotation = rotation;
            SetStaticDecoration(pipe);
        }

        private static void BuildWallPlate(ArenaMaterials materials, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject plate = CreatePrimitive("Wall Plate", PrimitiveType.Cube, position, scale, materials.Stone, parent);
            SetStaticDecoration(plate);
        }

        private static void BuildLuminousRail(ArenaMaterials materials, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject rail = CreatePrimitive("Light Rail", PrimitiveType.Cube, position, scale, materials.Accent, parent);
            StripCollider(rail);
            SetStaticDecoration(rail);
        }

        private static void BuildHazardChevrons(ArenaMaterials materials, Transform parent)
        {
            // Four chevron pairs drawn on the ground near each lane entry â€” the arena reads as a
            // managed dangerous place, not a blank room, without adding gameplay obstacles.
            foreach (var offset in new[] { -1f, 1f })
            {
                foreach (var xSign in new[] { -1f, 1f })
                {
                    Vector3 position = new Vector3(xSign * 21f, 0.02f, offset * 6f);
                    CreateHazardChevron(materials, parent, position, xSign > 0f ? 225f : -45f);
                }

                foreach (var zSign in new[] { -1f, 1f })
                {
                    Vector3 position = new Vector3(offset * 6f, 0.02f, zSign * 21f);
                    CreateHazardChevron(materials, parent, position, zSign > 0f ? 135f : -135f);
                }
            }
        }

        private static void CreateHazardChevron(ArenaMaterials materials, Transform parent, Vector3 position, float yaw)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject a = CreatePrimitive("Hazard Chevron A", PrimitiveType.Cube, position + Quaternion.Euler(0f, -35f, 0f) * Vector3.right * 0.65f,
                new Vector3(2.3f, 0.02f, 0.32f), materials.Hazard, parent, rotation);

            GameObject b = CreatePrimitive("Hazard Chevron B", PrimitiveType.Cube, position + Quaternion.Euler(0f, 35f, 0f) * Vector3.right * 0.65f + Vector3.up * 0.005f,
                new Vector3(2.3f, 0.02f, 0.32f), materials.Hazard, parent, rotation);

            StripCollider(a);
            StripCollider(b);
            SetStaticDecoration(a);
            SetStaticDecoration(b);
        }

        private static void SetStaticDecoration(GameObject gameObject)
        {
            gameObject.isStatic = true;

            foreach (Transform child in gameObject.transform)
            {
                child.gameObject.isStatic = true;
            }
        }

        // ---------------------------------------------------------------- post processing

        /// <summary>
        /// Creates a global URP volume (bloom, vignette, ACES tonemapping) and enables
        /// post-processing on the player camera.
        /// </summary>
        private static void BuildPostProcessing(PlayerRig rig)
        {
            var volumeObject = new GameObject("Post Processing");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = LoadPostProcessing();

            if (rig.Camera != null)
            {
                rig.Camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            }
        }

        private static VolumeProfile LoadPostProcessing()
        {
            const string profilePath = "Assets/Settings/ShooterPostProcessing.asset";

            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (existing != null)
                return existing;

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

            return profile;
        }

        private static List<Transform> BuildSpawnPoints()
        {
            var parent = new GameObject("Spawn Points");
            var points = new List<Transform>();

            // Enemies come in from the four lanes and the wall centres rather than a
            // plain ring, so they push in from several directions at once. Every point
            // sits in open ground, clear of cover, ramps and platforms.
            Vector3[] positions =
            {
                new Vector3(-17f, 0f, 17f),
                new Vector3(0f, 0f, 18f),
                new Vector3(17f, 0f, 17f),
                new Vector3(-10f, 0f, 5f),
                new Vector3(10f, 0f, 5f),
                new Vector3(-19f, 0f, -9f),
                new Vector3(19f, 0f, -9f),
                new Vector3(-17f, 0f, -17f),
                new Vector3(17f, 0f, -17f),
                new Vector3(-6f, 0f, -18f),
                new Vector3(8f, 0f, 18f),
                new Vector3(-12f, 0f, 7f),
            };

            for (int i = 0; i < positions.Length; i++)
            {
                var point = new GameObject($"Spawn {i + 1:00}");
                point.transform.SetParent(parent.transform, false);
                point.transform.position = positions[i];
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
            player.transform.position = new Vector3(0f, 0.4f, -14f);

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
            weaponObject.transform.localPosition = new Vector3(0.22f, -0.19f, 0.35f);

            var model = new GameObject("Model");
            model.transform.SetParent(weaponObject.transform, false);

            GameObject rifleAsset = FoundryAssets.LoadPlayer();
            GameObject rifle = Object.Instantiate(rifleAsset, model.transform, false);
            rifle.name = "Rifle";
            FindDeep(rifle.transform, "HandL").gameObject.SetActive(false);
            FindDeep(rifle.transform, "HandR").gameObject.SetActive(false);
            FoundryAssets.Skin(rifle);
            Animation viewAnimation = FoundryAssets.Bind(rifle, rifleAsset, FoundryAssets.PlayerNames);

            // Every piece is collider-free and on the Ignore Raycast layer so the
            // weapon can never eat its own shots.
            foreach (Transform part in rifle.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = 2;

                Collider collider = part.GetComponent<Collider>();

                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }
            }

            Transform muzzleAnchor = FindDeep(rifle.transform, "MuzzlePoint");
            Transform slide = FindDeep(rifle.transform, "Slide");

            if (muzzleAnchor == null)
            {
                throw new System.InvalidOperationException("ShooterRifle.fbx has no MuzzlePoint anchor.");
            }

            Vector3 muzzleLocal = weaponObject.transform.InverseTransformPoint(muzzleAnchor.position);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(weaponObject.transform, false);
            muzzle.transform.localPosition = muzzleLocal;

            // Muzzle flash: a star of emissive petals plus a point light. The root
            // is rotated randomly per shot by Weapon.PlayMuzzleFlash.
            var flashRoot = new GameObject("MuzzleFlash");
            flashRoot.transform.SetParent(muzzle.transform, false);
            Animation muzzleBurst = PlaceEffect("ChargeFlash", 5, flashRoot.transform, Quaternion.Euler(90f, 0f, 0f));

            foreach (Transform flashPart in flashRoot.GetComponentsInChildren<Transform>(true))
            {
                StripCollider(flashPart.gameObject);
                flashPart.gameObject.layer = 2;
            }

            var flashLightObject = new GameObject("MuzzleLight");
            flashLightObject.transform.SetParent(muzzle.transform, false);
            Light flashLight = flashLightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(0.30f, 0.82f, 1f);
            flashLight.range = 8f;
            flashLight.intensity = 7f;
            flashLight.shadows = LightShadows.None;

            // Underbarrel shotgun anchors and a second, wider flash rig.
            Transform shotgunMuzzleAnchor = FindDeep(rifle.transform, "ShotgunMuzzle");
            Transform pump = FindDeep(rifle.transform, "Pump");
            Transform shotgunMuzzle = null;
            GameObject shotgunFlashRoot = null;
            Light shotgunLight = null;
            Animation breachBurst = null;

            if (shotgunMuzzleAnchor != null)
            {
                var shotgunMuzzleObject = new GameObject("ShotgunMuzzle");
                shotgunMuzzleObject.transform.SetParent(weaponObject.transform, false);
                shotgunMuzzleObject.transform.localPosition =
                    weaponObject.transform.InverseTransformPoint(shotgunMuzzleAnchor.position);
                shotgunMuzzle = shotgunMuzzleObject.transform;

                shotgunFlashRoot = new GameObject("ShotgunFlash");
                shotgunFlashRoot.transform.SetParent(shotgunMuzzle, false);
                breachBurst = PlaceEffect("BreachFlash", 7, shotgunFlashRoot.transform, Quaternion.Euler(90f, 0f, 0f));

                foreach (Transform flashPart in shotgunFlashRoot.GetComponentsInChildren<Transform>(true))
                {
                    StripCollider(flashPart.gameObject);
                    flashPart.gameObject.layer = 2;
                }

                var shotgunLightObject = new GameObject("ShotgunLight");
                shotgunLightObject.transform.SetParent(shotgunMuzzle, false);
                shotgunLight = shotgunLightObject.AddComponent<Light>();
                shotgunLight.type = LightType.Point;
                shotgunLight.color = new Color(0.30f, 0.82f, 1f);
                shotgunLight.range = 10f;
                shotgunLight.intensity = 0f;
                shotgunLight.shadows = LightShadows.None;
            }

            LineRenderer tracer = weaponObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.widthMultiplier = 0.028f;
            tracer.numCapVertices = 4;
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
            weapon.punchModel = AssetDatabase.LoadAssetAtPath<GameObject>(FoundryAssets.Folder + "/Punch.fbx");
            weapon.player = player;
            weapon.muzzle = muzzle.transform;
            weapon.muzzleFlash = flashRoot;
            weapon.muzzleBurst = muzzleBurst;
            weapon.breachBurst = breachBurst;
            weapon.impactMark = BuildLooseProp("ImpactMark", materials.Impact);
            weapon.muzzleLight = flashLight;
            weapon.tracer = tracer;
            weapon.audioSource = audioSource;
            weapon.impactMaterial = materials.Impact;
            weapon.sparkMaterial = materials.Spark;
            weapon.explosionMaterial = materials.Muzzle;
            weapon.slide = slide;
            weapon.viewAnimation = viewAnimation;
            weapon.sightModel = model.transform;
            weapon.sightDot = FindDeep(rifle.transform, "OpticDot");
            weapon.shotgunMuzzle = shotgunMuzzle;
            weapon.shotgunFlash = shotgunFlashRoot;
            weapon.shotgunLight = shotgunLight;
            weapon.pump = pump;

            flashRoot.SetActive(false);
            flashLight.enabled = false;

            if (shotgunFlashRoot != null)
            {
                shotgunFlashRoot.SetActive(false);
            }

            if (shotgunLight != null)
            {
                shotgunLight.enabled = false;
            }

            return weapon;
        }

        /// <summary>
        /// Loads the Blender rifle and makes sure it imports without generating
        /// duplicate materials. The material mapping lives in the builder so the
        /// rifle always uses the project's URP assets.
        /// </summary>
        // ---------------------------------------------------------------- enemy prefabs

        private sealed class EnemyPrefabRig
        {
            public GameObject Trooper;
            public GameObject Exploder;
            public GameObject Runner;
            public GameObject Bulwark;
            public GameObject Marksman;
            public GameObject Drone;
            public GameObject Elite;
        }

        private sealed class EnemyPalette
        {
            public Material Body;
            public Material Dark;
            public Material Glow;
        }

        private static EnemyPrefabRig CreateEnemyPrefabs(ArenaMaterials materials)
        {
            return new EnemyPrefabRig
            {
                Trooper = CreateEnemyPrefab("Trooper", materials),
                Exploder = CreateEnemyPrefab("Exploder", materials),
                Runner = CreateEnemyPrefab("Runner", materials),
                Bulwark = CreateEnemyPrefab("Bulwark", materials),
                Marksman = CreateEnemyPrefab("Marksman", materials),
                Drone = CreateEnemyPrefab("Drone", materials),
                Elite = CreateEnemyPrefab("Elite", materials),
            };
        }

        /// <summary>
        /// Builds a combat prefab with the variant’s model and hit volume.
        /// </summary>
        private static GameObject CreateEnemyPrefab(string variant, ArenaMaterials materials)
        {
            var root = new GameObject("Enemy" + variant);
            bool drone = variant == "Drone";

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = drone ? 0.9f : 1.8f;
            controller.radius = drone ? 0.45f : 0.4f;
            controller.center = Vector3.up * (controller.height * 0.5f);
            controller.slopeLimit = 45f;
            controller.stepOffset = drone ? 0f : 0.3f;

            // The model lives under one child so the CharacterController defines
            // the physics shape and no visual piece contributes a collider.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            EnemyPalette palette = EnemyPaletteFor(variant, materials);
            if (drone)
            {
                GameObject hull = Object.Instantiate(FoundryAssets.LoadProp("EnemyDrone"), model.transform, false);
                hull.name = "Drone Hull";
                FoundryAssets.Skin(hull);
            }
            else
            {
                GameObject source = FoundryAssets.LoadActor("Enemy" + variant);
                GameObject rig = Object.Instantiate(source, model.transform, false);
                rig.name = "Rig";
                FoundryAssets.Skin(rig);
                EnemyAnimator animator = root.AddComponent<EnemyAnimator>();
                animator.model = model.transform;
                animator.authored = FoundryAssets.Bind(rig, source, FoundryAssets.ActorNames);
            }

            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = 2; // Ignore Raycast, matching the player rig.

                Collider collider = part.GetComponent<Collider>();

                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }
            }

            EnemyHealth health = root.AddComponent<EnemyHealth>();
            health.debrisMaterial = palette.Body;

            EnemyAI ai = root.AddComponent<EnemyAI>();

            EnemyRangedAttack ranged = root.AddComponent<EnemyRangedAttack>();
            ranged.projectileMaterial = materials.EnemyBolt;
            ranged.telegraphMaterial = materials.EnemyBolt;
            ranged.sparkMaterial = materials.Spark;

            if (drone)
            {
                var flightSettings = new SerializedObject(ai);
                flightSettings.FindProperty("hoverHeight").floatValue = 3.7f;
                flightSettings.ApplyModifiedPropertiesWithoutUndo();

                var weaponSettings = new SerializedObject(ranged);
                weaponSettings.FindProperty("eyeHeight").floatValue = 0.47f;
                weaponSettings.FindProperty("muzzleForward").floatValue = 0.65f;
                weaponSettings.FindProperty("damage").floatValue = 7f;
                weaponSettings.ApplyModifiedPropertiesWithoutUndo();
            }

            // Every enemy also carries the exploder component, inert until the
            // spawner initialises it for the Boomer variant.
            EnemyExploder exploder = root.AddComponent<EnemyExploder>();
            exploder.explosionMaterial = materials.Muzzle;
            exploder.sparkMaterial = materials.Spark;

            string path = $"{PrefabsFolder}/Enemy{variant}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] Enemy{variant} prefab saved to {path}.");
            return prefab;
        }

        private static EnemyPalette EnemyPaletteFor(string variant, ArenaMaterials materials)
        {
            return variant switch
            {
                "Runner" => new EnemyPalette
                {
                    Body = materials.RunnerBody, Dark = materials.RunnerDark, Glow = materials.RunnerGlow,
                },
                "Bulwark" or "Drone" => new EnemyPalette
                {
                    Body = materials.BulwarkBody, Dark = materials.BulwarkDark, Glow = materials.BulwarkGlow,
                },
                "Marksman" => new EnemyPalette
                {
                    Body = materials.MarksmanBody, Dark = materials.MarksmanDark, Glow = materials.MarksmanGlow,
                },
                "Elite" => new EnemyPalette
                {
                    Body = materials.EliteBody, Dark = materials.EliteDark, Glow = materials.EliteGlow,
                },
                _ => new EnemyPalette
                {
                    Body = materials.TrooperBody, Dark = materials.TrooperDark, Glow = materials.TrooperGlow,
                },
            };
        }

        private static Material EnemyMaterialFor(string partName, EnemyPalette palette)
        {
            if (partName.Contains("Visor") || partName.Contains("Lens") ||
                partName.Contains("Glow") || partName.Contains("Core") ||
                partName.Contains("Eye") || partName.Contains("Rune"))
            {
                return palette.Glow;
            }

            if (partName.Contains("Dark") || partName.Contains("Boot") ||
                partName.Contains("Claw") || partName.Contains("Horn") ||
                partName.Contains("Spike") || partName.Contains("Cape") ||
                partName.Contains("Joint") || partName.Contains("Crest") ||
                partName.Contains("ChestPlate") || partName.Contains("Backpack") ||
                partName.Contains("Trim") || partName.Contains("Antenna") ||
                partName.Contains("Knee"))
            {
                return palette.Dark;
            }

            return palette.Body;
        }

        // ------------------------------------------------------------ boss prefab

        /// <summary>
        /// Builds The Warden's prefab around the authored Blender rig. The boss owns
        /// its own controller and clip player, so it shares only health and the
        /// spawner's wave bookkeeping with the grunts.
        /// </summary>
        private static GameObject CreateBossPrefab(ArenaMaterials materials, EnemyPrefabRig enemyPrefabs)
        {
            var root = new GameObject("BossWarden");

            CharacterController controller = root.AddComponent<CharacterController>();
            // Sized to the torso, not to the shoulder spikes: a capsule wide enough
            // to cover the pauldrons snags on the arena's pillars and cover, and the
            // grunts use the same torso-sized convention.
            controller.height = 4.5f;
            controller.radius = 1.05f;
            controller.center = new Vector3(0f, 2.25f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.6f;

            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * (controller.height / 3.48f);
            GameObject source = FoundryAssets.LoadActor("BossWarden");
            GameObject rig = Object.Instantiate(source, model.transform, false);
            rig.name = "Rig";
            FoundryAssets.Skin(rig);

            foreach (Transform part in rig.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = 2; // Ignore Raycast, matching the player rig.

                Collider collider = part.GetComponent<Collider>();

                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }
            }

            Animation animation = rig.GetComponent<Animation>();

            if (animation == null)
            {
                animation = rig.AddComponent<Animation>();
            }

            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;

            EnemyHealth health = root.AddComponent<EnemyHealth>();
            health.debrisMaterial = materials.BossDark;

            BossAnimator animator = root.AddComponent<BossAnimator>();
            animator.idle = FoundryAssets.Clip(source, "Idle");
            animator.walk = FoundryAssets.Clip(source, "Walk");
            animator.slam = FoundryAssets.Clip(source, "Slam");
            animator.volley = FoundryAssets.Clip(source, "Volley");
            animator.roar = FoundryAssets.Clip(source, "Roar");
            animator.hit = FoundryAssets.Clip(source, "Hit");
            animator.death = FoundryAssets.Clip(source, "Death");

            if (animator.idle == null || animator.death == null)
            {
                throw new System.InvalidOperationException(
                    "Boss clips failed to import as legacy animations. " +
                    "Re-run Tools/Blender/build_boss.py and rebuild the scene.");
            }

            BossController boss = root.AddComponent<BossController>();
            boss.ringMaterial = materials.Hazard;
            boss.boltMaterial = materials.EnemyBolt;
            boss.sparkMaterial = materials.Spark;
            boss.summonPrefabs = new[] { enemyPrefabs.Trooper, enemyPrefabs.Runner };

            string path = $"{PrefabsFolder}/BossWarden.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] BossWarden prefab saved to {path}.");
            return prefab;
        }

        private static Animation PlaceEffect(string name, int frames, Transform parent, Quaternion rotation)
        {
            GameObject source = FoundryAssets.LoadEffect(name, frames);
            GameObject visual = Object.Instantiate(source, parent, false);
            visual.transform.localRotation = rotation;
            FoundryAssets.Skin(visual);
            foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            return FoundryAssets.Bind(visual, source, new[] { "Effect" });
        }

        private static GameObject BuildLooseProp(string name, Material material)
        {
            var root = new GameObject(name) { layer = 2 };
            var orientation = new GameObject("Orientation");
            orientation.transform.SetParent(root.transform, false);
            orientation.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GameObject visual = Object.Instantiate(FoundryAssets.LoadProp(name), orientation.transform, false);
            visual.layer = 2;
            MeshRenderer renderer = visual.GetComponentInChildren<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildEffectLibrary(GameObject managers)
        {
            var effects = new (string Name, int Frames)[]
            {
                ("ImpactSpark", 11), ("Explosion", 19), ("ShockRing", 24),
                ("EnemyBolt", 9), ("Tracer", 5), ("DebrisShard", 18),
            };
            var prefabs = new GameObject[effects.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                var root = new GameObject(effects[i].Name) { layer = 2 };
                Quaternion orientation = i == (int)FoundryEffect.Impact
                    ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                PlaceEffect(effects[i].Name, effects[i].Frames, root.transform, orientation);
                prefabs[i] = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/{root.name}.prefab");
                Object.DestroyImmediate(root);
            }
            managers.AddComponent<FoundryEffects>().Configure(prefabs);
        }

        private static void PlacePickupVisual(string name, Transform parent)
        {
            GameObject source = FoundryAssets.LoadPickup(name);
            GameObject visual = Object.Instantiate(source, parent, false);
            FoundryAssets.Skin(visual);
            Animation animation = FoundryAssets.Bind(visual, source, new[] { "Idle" });
            animation.clip = FoundryAssets.Clip(source, "Idle");
            animation.playAutomatically = true;
        }

        private static GameObject CreateAmmoCachePrefab(ArenaMaterials materials)
        {
            var root = new GameObject("AmmoCache");

            PlacePickupVisual("AmmoCache", root.transform);

            AmmoPickup pickup = root.AddComponent<AmmoPickup>();
            pickup.Configure(90, 1.9f, 0f, 90f, 0.12f);

            string path = $"{PrefabsFolder}/AmmoCache.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] Ammo cache prefab saved to {path}.");
            return prefab;
        }

        /// <summary>
        /// Builds the medikit that bosses, elites and regular kills drop.
        /// One-shot, glowing green so it reads at a distance in fog.
        /// </summary>
        private static GameObject CreateHealthCachePrefab(ArenaMaterials materials)
        {
            var root = new GameObject("HealthCache");

            PlacePickupVisual("HealthCache", root.transform);

            HealthPickup pickup = root.AddComponent<HealthPickup>();
            pickup.Configure(25f);

            string path = $"{PrefabsFolder}/HealthCache.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] Health cache prefab saved to {path}.");
            return prefab;
        }

        // ---------------------------------------------------------------- managers
        private sealed class ManagersRig
        {
            public GameManager Game;
            public WaveSpawner Waves;
            public AudioManager Audio;
            public UpgradeManager Upgrades;
            public SettingsManager Settings;
        }

        private static ManagersRig BuildManagers(EnemyPrefabRig enemyPrefabs, GameObject bossPrefab,
            GameObject ammoCachePrefab, GameObject healthCachePrefab, PlayerRig rig,
            List<Transform> spawnPoints)
        {
            var managers = new GameObject("Managers");
            BuildEffectLibrary(managers);

            SettingsManager settingsManager = managers.AddComponent<SettingsManager>();
            GameManager gameManager = managers.AddComponent<GameManager>();
            WaveSpawner waveSpawner = managers.AddComponent<WaveSpawner>();
            AudioManager audioManager = managers.AddComponent<AudioManager>();
            UpgradeManager upgradeManager = managers.AddComponent<UpgradeManager>();

            waveSpawner.enemyPrefab = enemyPrefabs.Trooper;
            waveSpawner.trooperPrefab = enemyPrefabs.Trooper;
            waveSpawner.exploderPrefab = enemyPrefabs.Exploder;
            waveSpawner.runnerPrefab = enemyPrefabs.Runner;
            waveSpawner.bulwarkPrefab = enemyPrefabs.Bulwark;
            waveSpawner.marksmanPrefab = enemyPrefabs.Marksman;
            waveSpawner.dronePrefab = enemyPrefabs.Drone;
            waveSpawner.elitePrefab = enemyPrefabs.Elite;
            waveSpawner.bossPrefab = bossPrefab;
            waveSpawner.ammoCachePrefab = ammoCachePrefab;
            waveSpawner.healthCachePrefab = healthCachePrefab;
            waveSpawner.playerTarget = rig.Health;
            waveSpawner.spawnPoints = spawnPoints.ToArray();

            gameManager.playerHealth = rig.Health;
            gameManager.playerController = rig.Controller;
            gameManager.waveSpawner = waveSpawner;

            // Choice panel, card texts and the HUD reference are wired later in
            // BuildHud; the panel object does not exist yet at this point.
            upgradeManager.player = rig.Controller;
            upgradeManager.playerHealth = rig.Health;

            return new ManagersRig { Game = gameManager, Waves = waveSpawner, Audio = audioManager,
                Upgrades = upgradeManager, Settings = settingsManager };
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

            HUDController hud = canvasObject.AddComponent<HUDController>();
            BuildCombatReadout(canvasObject.transform, font, hud);

            // Wave (top center).
            Text waveText = CreateText(canvasObject.transform, "WaveText", "GET READY", 32, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                new Vector2(700f, 44f), font, FontStyle.Bold);

            Text statusText = CreateText(canvasObject.transform, "StatusText", "FIRST WAVE IN 3", 18,
                new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 26f), font);

            // Boss bar (top center, under the wave label). Hidden until a boss spawns;
            // the HUD controller toggles the whole group.
            RectTransform bossPanel = CreateRect("BossBar", canvasObject.transform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(780f, 60f));

            Text bossNameText = CreateText(bossPanel, "BossName", "THE WARDEN", 20,
                new Color(1f, 0.86f, 0.72f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), Vector2.zero, new Vector2(460f, 24f), font, FontStyle.Bold);

            Text bossPhaseText = CreateText(bossPanel, "BossPhase", "PHASE I", 15,
                new Color(1f, 0.8f, 0.6f), TextAnchor.MiddleRight, new Vector2(1f, 1f),
                new Vector2(1f, 1f), Vector2.zero, new Vector2(300f, 22f), font, FontStyle.Bold);

            RectTransform bossBarRect = CreateRect("BossTrack", bossPanel, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(780f, 20f));
            AddImage(bossBarRect, new Color(0f, 0f, 0f, 0.62f));

            RectTransform bossFillRect = CreateRect("BossFill", bossBarRect, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            bossFillRect.offsetMin = new Vector2(2f, 2f);
            bossFillRect.offsetMax = new Vector2(-2f, -2f);
            Image bossFill = AddImage(bossFillRect, new Color(0.92f, 0.35f, 0.14f));
            bossFill.type = Image.Type.Filled;
            bossFill.fillMethod = Image.FillMethod.Horizontal;
            bossFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            bossFill.fillAmount = 1f;

            // One pip per phase: they light up as the fight escalates.
            Image[] bossPhasePips = new Image[3];

            for (int i = 0; i < bossPhasePips.Length; i++)
            {
                RectTransform pip = CreateRect("BossPhasePip" + i, bossPanel, new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2((i - 1) * 54f, -50f), new Vector2(48f, 5f));
                bossPhasePips[i] = AddImage(pip, new Color(1f, 1f, 1f, 0.15f));
            }

            bossPanel.gameObject.SetActive(false);

            // Score (top right).
            Text scoreText = CreateText(canvasObject.transform, "ScoreText", "SCORE 0", 26, Color.white,
                TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -24f),
                new Vector2(420f, 36f), font, FontStyle.Bold);

            // Kill-chain readout, directly under the score. Hidden until two kills chain.
            Text comboText = CreateText(canvasObject.transform, "ComboText", string.Empty, 19,
                new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleRight, new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-36f, -62f), new Vector2(420f, 28f), font, FontStyle.Bold);
            comboText.enabled = false;

            // Crosshair (center). The bars are captured so the HUD can animate their
            // spread while firing and moving.
            RectTransform crosshair = CreateRect("Crosshair", canvasObject.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            Color crosshairColor = new Color(1f, 1f, 1f, 0.85f);
            RectTransform crosshairTop = CreateCrosshairBar(crosshair, "Top", new Vector2(0f, 9f), new Vector2(2f, 9f), crosshairColor);
            RectTransform crosshairBottom = CreateCrosshairBar(crosshair, "Bottom", new Vector2(0f, -9f), new Vector2(2f, 9f), crosshairColor);
            RectTransform crosshairLeft = CreateCrosshairBar(crosshair, "Left", new Vector2(-9f, 0f), new Vector2(9f, 2f), crosshairColor);
            RectTransform crosshairRight = CreateCrosshairBar(crosshair, "Right", new Vector2(9f, 0f), new Vector2(9f, 2f), crosshairColor);
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
                "WASD move  /  double-tap W sprint  /  SPACE jump  /  SHIFT air dash  /  LMB fire  /  RMB aim  /  F melee  /  C slide  /  R recharge  /  ESC pause",
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

            // Augment choice screen, built last so it draws over the game-over panel.
            UpgradePanelRig upgradePanel = BuildUpgradePanel(canvasObject.transform, font);

            hud.playerHealth = rig.Health;
            hud.weapon = rig.Weapon;
            hud.gameManager = managers.Game;
            hud.waveSpawner = managers.Waves;
            hud.waveText = waveText;
            hud.statusText = statusText;
            hud.scoreText = scoreText;
            hud.comboText = comboText;
            hud.gameOverPanel = gameOverPanel.gameObject;
            hud.gameOverScoreText = gameOverScore;
            hud.hintText = hintText;
            RectTransform diagnostics = CreateRect("Diagnostics", canvasObject.transform,
                Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            diagnostics.anchorMax = Vector2.one;
            diagnostics.offsetMin = Vector2.zero;
            diagnostics.offsetMax = Vector2.zero;
            Canvas diagnosticsCanvas = diagnostics.gameObject.AddComponent<Canvas>();
            diagnosticsCanvas.overrideSorting = true;
            diagnosticsCanvas.sortingOrder = 150;
            Vector2 topLeft = new Vector2(0f, 1f);
            hud.fpsText = CreateText(diagnostics, "FpsText", "", 18, Color.white, TextAnchor.MiddleLeft,
                topLeft, topLeft, new Vector2(22f, -5f), new Vector2(115f, 24f), font, FontStyle.Bold);
            Outline fpsOutline = hud.fpsText.gameObject.AddComponent<Outline>();
            fpsOutline.effectColor = Color.black;
            fpsOutline.effectDistance = new Vector2(1f, -1f);
            hud.fpsText.enabled = false;
            hud.toastText = toastText;
            hud.hitMarkerGroup = hitMarkerGroup;
            hud.bossBarRoot = bossPanel.gameObject;
            hud.bossBarFill = bossFill;
            hud.bossNameText = bossNameText;
            hud.bossPhaseText = bossPhaseText;
            hud.bossPhasePips = bossPhasePips;
            hud.crosshairBars = new[] { crosshairTop, crosshairBottom, crosshairLeft, crosshairRight };
            hud.crosshairGroup = crosshair.gameObject.AddComponent<CanvasGroup>();
            hud.player = rig.Controller;

            // The upgrade manager needs the finished HUD for toasts plus the choice panel.
            UpgradeManager upgrades = managers.Upgrades;
            upgrades.hud = hud;
            upgrades.choicePanel = upgradePanel.Panel;
            upgrades.cardRoots = upgradePanel.Roots;
            upgrades.cardBackgrounds = upgradePanel.Backgrounds;
            upgrades.cardAccents = upgradePanel.Accents;
            upgrades.cardBadges = upgradePanel.Badges;
            upgrades.cardRarities = upgradePanel.Rarities;
            upgrades.cardTitles = upgradePanel.Titles;
            upgrades.cardDescriptions = upgradePanel.Descriptions;
            upgrades.choiceHintText = upgradePanel.Hint;

            DamageFlash flash = canvasObject.AddComponent<DamageFlash>();
            flash.playerHealth = rig.Health;
            flash.flashImage = damageImage;

            // Keep the compiler honest about intentionally unused labels.
            _ = gameOverTitle;
            _ = gameOverHint;

            return hud;
        }

        private static void BuildCombatReadout(Transform canvas, Font font, HUDController hud)
        {
            Vector2 bottomCenter = new Vector2(0.5f, 0f);
            Vector2 bottomRight = new Vector2(1f, 0f);
            Color mint = new Color(0.46f, 0.91f, 0.80f);
            Color energy = new Color(0.30f, 0.82f, 1f);
            Color muted = new Color(0.66f, 0.75f, 0.81f);
            Sprite plate = FoundryHudSkin.Plate;

            RectTransform readout = CreateRect("CombatReadout", canvas, Vector2.zero, bottomCenter,
                Vector2.zero, new Vector2(0f, 112f));
            readout.anchorMax = bottomRight;
            hud.combatReadout = readout;

            RectTransform health = BuildInstrumentPanel(readout, "HealthPanel", 206f, false, mint, plate);
            RectTransform ammo = BuildInstrumentPanel(readout, "AmmoPanel", 218f, true, energy, plate);

            AddImage(CreateRect("MedicalCrossHorizontal", health, Vector2.zero, bottomCenter,
                new Vector2(21f, 70f), new Vector2(12f, 3f)), mint);
            AddImage(CreateRect("MedicalCrossVertical", health, Vector2.zero, bottomCenter,
                new Vector2(21f, 66f), new Vector2(3f, 11f)), mint);
            for (int i = 0; i < 3; i++)
            {
                AddImage(CreateRect("ChargeBar" + i, ammo, Vector2.zero, Vector2.zero,
                    new Vector2(15f, 67f + i * 4f), new Vector2(13f, 2f)), energy);
            }

            CreateText(health, "HealthLabel", "HEALTH", 13, mint, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.zero, new Vector2(34f, 62f), new Vector2(86f, 20f), font, FontStyle.Bold);
            CreateText(ammo, "AmmoLabel", "ENERGY", 13, energy, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.zero, new Vector2(35f, 62f), new Vector2(99f, 20f), font, FontStyle.Bold);
            hud.healthText = CreateText(health, "HealthText", "", 36, Color.white, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.zero, new Vector2(15f, 22f), new Vector2(176f, 42f), font, FontStyle.Bold);
            hud.ammoText = CreateText(ammo, "AmmoText", "", 36, Color.white, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.zero, new Vector2(15f, 22f), new Vector2(95f, 42f), font, FontStyle.Bold);

            AddImage(CreateRect("ReserveDivider", ammo, Vector2.zero, Vector2.zero,
                new Vector2(122f, 29f), new Vector2(1f, 29f)), new Color(0.40f, 0.46f, 0.49f, 0.75f));
            CreateText(ammo, "ReserveLabel", "RESERVE", 12, muted, TextAnchor.MiddleRight,
                bottomRight, bottomRight, new Vector2(-15f, 49f), new Vector2(75f, 15f), font, FontStyle.Bold);
            hud.reserveText = CreateText(ammo, "ReserveText", "", 23, muted, TextAnchor.MiddleRight,
                bottomRight, bottomRight, new Vector2(-15f, 23f), new Vector2(75f, 27f), font, FontStyle.Bold);

            Sprite indicator = FoundryHudSkin.Indicator;
            hud.healthSegments = BuildReadoutMeter(health, "HealthMeter", 176f, 10, mint, indicator);
            hud.ammoSegments = BuildReadoutMeter(ammo, "AmmoMeter", 188f, 12, energy, indicator);
            hud.reloadText = CreateText(ammo, "ReloadText", "CHARGING", 12, energy, TextAnchor.MiddleRight,
                bottomRight, bottomRight, new Vector2(-15f, 63f), new Vector2(86f, 19f), font, FontStyle.Bold);
            hud.reloadText.enabled = false;

            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 topRight = Vector2.one;
            RectTransform shellChip = CreateRect("ShotgunChip", ammo, topRight, bottomRight,
                new Vector2(0f, 5f), new Vector2(176f, 26f));
            AddImage(shellChip, new Color(0.09f, 0.13f, 0.16f, 0.96f)).sprite = plate;
            hud.shotgunChip = shellChip.gameObject;
            hud.shotgunText = CreateText(shellChip, "ShotgunText", "", 13, energy,
                TextAnchor.MiddleCenter, center, center, Vector2.zero, new Vector2(156f, 22f),
                font, FontStyle.Bold);
            hud.shotgunChip.SetActive(false);
        }

        private static RectTransform BuildInstrumentPanel(Transform readout, string name, float width,
            bool right, Color accent, Sprite plate)
        {
            Vector2 corner = right ? new Vector2(1f, 0f) : Vector2.zero;
            RectTransform panel = CreateRect(name, readout, corner, corner,
                new Vector2(right ? -24f : 24f, 30f), new Vector2(width, 87f));
            AddImage(panel, Color.Lerp(new Color(0.18f, 0.25f, 0.29f), accent, 0.27f)).sprite = plate;

            Vector2 center = new Vector2(0.5f, 0.5f);
            RectTransform face = CreateRect("Face", panel, center, center, Vector2.zero,
                new Vector2(width - 3f, 84f));
            AddImage(face, new Color(0.055f, 0.085f, 0.111f, 0.98f)).sprite = plate;
            AddImage(CreateRect("Accent", panel, Vector2.zero, Vector2.zero,
                new Vector2(15f, 85f), new Vector2(38f, 2f)), accent);
            return panel;
        }

        private static Image[] BuildReadoutMeter(Transform panel, string name, float width, int count, Color color,
            Sprite sprite)
        {
            const float gap = 3f;
            const float height = 8f;
            float segmentWidth = (width - gap * (count - 1)) / count;
            var segments = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var size = new Vector2(segmentWidth, height);
                var position = new Vector2(15f + i * (segmentWidth + gap), 12f);
                RectTransform track = CreateRect(name + "Track" + i, panel, Vector2.zero, Vector2.zero,
                    position, size);
                AddImage(track, new Color(0.24f, 0.30f, 0.34f)).sprite = sprite;
                RectTransform fillRect = CreateRect("Fill", track, Vector2.zero, Vector2.zero, Vector2.zero, size);
                Image fill = AddImage(fillRect, color);
                fill.sprite = sprite;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                segments[i] = fill;
            }
            return segments;
        }

        // ---------------------------------------------------------------- menus

        /// <summary>
        /// Main menu + pause + settings. The controller builds its Canvas at
        /// runtime (see MenuController), so the builder only wires references;
        /// this keeps the scene reproducible without hand-building a Menu Canvas.
        /// </summary>
        private static MenuController BuildMenus(PlayerRig rig, ManagersRig managers, HUDController hud)
        {
            GameObject menuObject = new GameObject("Menu");
            MenuController menu = menuObject.AddComponent<MenuController>();
            menu.gameManager = managers.Game;
            menu.settings = managers.Settings;
            menu.hud = hud;
            menu.foundryPlate = FoundryHudSkin.Plate;
            menu.foundryEmblem = FoundryHudSkin.Emblem;
            menu.player = rig.Controller;
            return menu;
        }

        /// <summary>Everything the upgrade manager needs from the pick-augment screen.</summary>
        private sealed class UpgradePanelRig
        {
            public GameObject Panel;
            public GameObject[] Roots;
            public Image[] Backgrounds;
            public Image[] Accents;
            public Image[] Badges;
            public Text[] Rarities;
            public Text[] Titles;
            public Text[] Descriptions;
            public Text Hint;
        }

        private static UpgradePanelRig BuildUpgradePanel(Transform canvas, Font font)
        {
            const int cardCount = 4;
            var result = new UpgradePanelRig
            {
                Roots = new GameObject[cardCount],
                Backgrounds = new Image[cardCount],
                Accents = new Image[cardCount],
                Badges = new Image[cardCount],
                Rarities = new Text[cardCount],
                Titles = new Text[cardCount],
                Descriptions = new Text[cardCount],
            };

            Image backdrop = CreateFullScreenImage(canvas, "UpgradePanel", new Color(0.018f, 0.029f, 0.040f, 0.96f));
            result.Panel = backdrop.gameObject;
            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 topLeft = new Vector2(0f, 1f);
            Vector2 topRight = Vector2.one;
            Color signal = new Color(0.47f, 0.91f, 0.82f);
            Color secondaryText = new Color(0.78f, 0.84f, 0.87f);
            Sprite plate = FoundryHudSkin.Plate;

            CreateText(backdrop.transform, "UpgradeSubtitle", "WAVE CLEARED", 22, signal,
                TextAnchor.MiddleLeft, center, topLeft, new Vector2(-516f, 343f),
                new Vector2(760f, 30f), font, FontStyle.Bold);
            CreateText(backdrop.transform, "UpgradeTitle", "Choose an augment", 54, Color.white,
                TextAnchor.MiddleLeft, center, topLeft, new Vector2(-519f, 311f),
                new Vector2(900f, 68f), font, FontStyle.Bold);
            Image headingRule = AddImage(CreateRect("HeadingRule", backdrop.transform, center, center,
                new Vector2(0f, 228f), new Vector2(1032f, 2f)), new Color(0.23f, 0.38f, 0.40f));
            headingRule.raycastTarget = false;

            result.Hint = CreateText(backdrop.transform, "ChoiceHint", "Press 1 / 2 / 3 / 4 to select", 22, secondaryText,
                TextAnchor.MiddleCenter, center, center, new Vector2(0f, -305f),
                new Vector2(1032f, 42f), font);

            for (int i = 0; i < cardCount; i++)
            {
                var offset = new Vector2(i % 2 == 0 ? -267f : 267f, i < 2 ? 106f : -146f);
                RectTransform card = CreateRect("Card" + (i + 1), backdrop.transform, center, center,
                    offset, new Vector2(516f, 230f));
                result.Roots[i] = card.gameObject;

                Image back = AddImage(card, new Color(0.078f, 0.108f, 0.135f));
                back.sprite = plate;
                back.raycastTarget = false;
                result.Backgrounds[i] = back;

                Image accent = AddImage(CreateRect("AccentStrip", card, topLeft, topLeft,
                    new Vector2(38f, -37f), new Vector2(24f, 3f)), Color.white);
                accent.raycastTarget = false;
                result.Accents[i] = accent;

                result.Rarities[i] = CreateText(card, "CardRarity", "", 18, Color.white,
                    TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(74f, -23f),
                    new Vector2(286f, 30f), font, FontStyle.Bold);

                RectTransform badge = CreateRect("KeyBadge", card, topRight, topRight,
                    new Vector2(-40f, -23f), new Vector2(50f, 40f));
                Image badgeImage = AddImage(badge, signal);
                badgeImage.sprite = plate;
                badgeImage.raycastTarget = false;
                result.Badges[i] = badgeImage;

                CreateText(badge, "KeyText", (i + 1).ToString(), 22,
                    new Color(0.025f, 0.045f, 0.058f), TextAnchor.MiddleCenter,
                    center, center, Vector2.zero, new Vector2(50f, 40f), font, FontStyle.Bold);

                result.Titles[i] = CreateText(card, "CardTitle", "", 31, Color.white,
                    TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(38f, -63f),
                    new Vector2(440f, 48f), font, FontStyle.Bold);

                result.Descriptions[i] = CreateText(card, "CardDescription", "", 23, secondaryText,
                    TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(38f, -123f),
                    new Vector2(440f, 85f), font);
                result.Descriptions[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                card.gameObject.SetActive(false);
            }

            result.Panel.SetActive(false);
            return result;
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
                new Vector3(-10f, 0.55f, -5f),
                new Vector3(9f, 0.55f, -10f),
                new Vector3(2f, 0.55f, 13f),
                new Vector3(-9f, 0.55f, 8f),
                new Vector3(-DockCenterX, DockHeight + 0.55f, 0f),
                new Vector3(DockCenterX, DockHeight + 0.55f, 0f),
            };

            foreach (Vector3 position in positions)
            {
                var crate = new GameObject("Ammo Crate");
                crate.transform.SetParent(parent.transform, false);
                crate.transform.position = position;

                PlacePickupVisual("AmmoCache", crate.transform);

                AmmoPickup pickup = crate.AddComponent<AmmoPickup>();
                pickup.weapon = rig.Weapon;
                pickup.player = rig.Controller;
                pickup.hud = hud;
            }

            Debug.Log($"[Shooter] Placed {positions.Length} ammo crates.");
        }

        private static RectTransform CreateCrosshairBar(Transform parent, string name, Vector2 position, Vector2 size,
            Color color, float rotation = 0f)
        {
            RectTransform rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position, size);

            if (!Mathf.Approximately(rotation, 0f))
            {
                rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }

            AddImage(rect, color);
            return rect;
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

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }
            return null;
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
