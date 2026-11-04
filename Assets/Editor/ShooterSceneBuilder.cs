using System.Collections.Generic;
using Shooter.Enemies;
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

        [MenuItem("Shooter/Rebuild Arena Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/Scripts");
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ScenesFolder);

            ArenaMaterials materials = CreateMaterials();
            GameObject enemyPrefab = CreateEnemyPrefab(materials);
            GameObject ammoCachePrefab = CreateAmmoCachePrefab(materials);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureLighting(materials);
            BuildArena(materials);
            PlayerRig rig = BuildPlayer(materials);
            BuildPostProcessing(rig);
            List<Transform> spawnPoints = BuildSpawnPoints();
            ManagersRig managers = BuildManagers(enemyPrefab, ammoCachePrefab, rig, spawnPoints);
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
            public Material Pad;
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
            public Material AmmoCache;
            public Material Skybox;
        }

        private static ArenaMaterials CreateMaterials()
        {
            return new ArenaMaterials
            {
                Floor = GetOrCreateLitMaterial("Floor", new Color(0.17f, 0.18f, 0.21f), 0.15f, 0f),
                Pad = GetOrCreateLitMaterial("Pad", new Color(0.21f, 0.24f, 0.29f), 0.35f, 0.1f),
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
                AmmoCache = GetOrCreateEmissiveMaterial("AmmoCache", new Color(0.24f, 0.16f, 0.05f),
                    new Color(1f, 0.68f, 0.2f) * 1.7f),
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

            BuildCentralPad(materials, arena.transform);
            BuildCornerPlatforms(materials, arena.transform);
            BuildInnerPillars(materials, arena.transform);
            BuildCoverClusters(materials, arena.transform);
            BuildBarrels(materials, arena.transform);
            BuildFloorGuides(materials, arena.transform);

            ground.isStatic = true;
        }

        /// <summary>
        /// The raised centre pad: an open 12x12 duelling floor ringed by glowing trim.
        /// It is only 12 cm high, well under the controller step height, so nobody
        /// stumbles on the way in.
        /// </summary>
        private static void BuildCentralPad(ArenaMaterials materials, Transform parent)
        {
            CreatePrimitive("Central Pad", PrimitiveType.Cube, new Vector3(0f, 0.06f, 0f),
                new Vector3(12f, 0.12f, 12f), materials.Pad, parent);

            GameObject[] trims =
            {
                CreatePrimitive("Pad Trim North", PrimitiveType.Cube, new Vector3(0f, 0.13f, 5.94f),
                    new Vector3(12f, 0.02f, 0.12f), materials.Accent, parent),
                CreatePrimitive("Pad Trim South", PrimitiveType.Cube, new Vector3(0f, 0.13f, -5.94f),
                    new Vector3(12f, 0.02f, 0.12f), materials.Accent, parent),
                CreatePrimitive("Pad Trim East", PrimitiveType.Cube, new Vector3(5.94f, 0.13f, 0f),
                    new Vector3(0.12f, 0.02f, 12f), materials.Accent, parent),
                CreatePrimitive("Pad Trim West", PrimitiveType.Cube, new Vector3(-5.94f, 0.13f, 0f),
                    new Vector3(0.12f, 0.02f, 12f), materials.Accent, parent),
            };

            foreach (GameObject trim in trims)
            {
                StripCollider(trim);
            }
        }

        /// <summary>
        /// Four raised corner platforms, each reached by a ramp from the mid lanes.
        /// They give the player high ground and break up the skyline.
        /// </summary>
        private static void BuildCornerPlatforms(ArenaMaterials materials, Transform parent)
        {
            const float platformSize = 8f;
            const float platformHeight = 2f;
            const float rampRun = 6f;
            const float rampWidth = 3f;
            const float rampThickness = 0.4f;

            Vector3[] centers =
            {
                new Vector3(15.5f, 0f, 15.5f),
                new Vector3(-15.5f, 0f, 15.5f),
                new Vector3(15.5f, 0f, -15.5f),
                new Vector3(-15.5f, 0f, -15.5f),
            };

            float rampAngle = Mathf.Atan2(platformHeight, rampRun) * Mathf.Rad2Deg;
            float slopeLength = Mathf.Sqrt(rampRun * rampRun + platformHeight * platformHeight);

            foreach (Vector3 center in centers)
            {
                string side = center.x > 0f ? "East" : "West";

                CreatePrimitive($"Platform {side} {(center.z > 0f ? "North" : "South")}", PrimitiveType.Cube,
                    new Vector3(center.x, platformHeight * 0.5f, center.z),
                    new Vector3(platformSize, platformHeight, platformSize), materials.Wall, parent);

                // Glowing lip around the platform top.
                float lip = platformSize * 0.5f - 0.06f;

                foreach (int axis in new[] { 0, 1 })
                {
                    for (int side2 = -1; side2 <= 1; side2 += 2)
                    {
                        Vector3 position = axis == 0
                            ? new Vector3(center.x, platformHeight + 0.02f, center.z + side2 * lip)
                            : new Vector3(center.x + side2 * lip, platformHeight + 0.02f, center.z);

                        Vector3 scale = axis == 0
                            ? new Vector3(platformSize, 0.04f, 0.12f)
                            : new Vector3(0.12f, 0.04f, platformSize);

                        GameObject trim = CreatePrimitive("Edge Trim", PrimitiveType.Cube, position, scale,
                            materials.Accent, parent);
                        StripCollider(trim);
                    }
                }

                // Ramp: rises from the arena floor up to the platform edge. The cube is
                // rotated so its top face lands exactly on the slope line.
                bool eastSide = center.x > 0f;
                float rampX = eastSide ? 8.5f : -8.5f;
                float rampCenterY = platformHeight * 0.5f - 0.21f;

                CreatePrimitive($"Ramp {side} {(center.z > 0f ? "North" : "South")}", PrimitiveType.Cube,
                    new Vector3(rampX, rampCenterY, center.z),
                    new Vector3(slopeLength, rampThickness, rampWidth), materials.CoverAlt, parent,
                    Quaternion.Euler(0f, 0f, eastSide ? rampAngle : -rampAngle));
            }
        }

        /// <summary>Four pillars framing the central pad; they break long sight lines.</summary>
        private static void BuildInnerPillars(ArenaMaterials materials, Transform parent)
        {
            Vector3[] positions =
            {
                new Vector3(8.5f, 1.7f, 8.5f),
                new Vector3(-8.5f, 1.7f, 8.5f),
                new Vector3(8.5f, 1.7f, -8.5f),
                new Vector3(-8.5f, 1.7f, -8.5f),
            };

            foreach (Vector3 position in positions)
            {
                CreatePrimitive("Pillar", PrimitiveType.Cube, position, new Vector3(1.4f, 3.4f, 1.4f),
                    materials.Wall, parent);

                CreatePrimitive("Pillar Cap", PrimitiveType.Cube, position + new Vector3(0f, 1.76f, 0f),
                    new Vector3(1.54f, 0.12f, 1.54f), materials.Accent, parent);
            }
        }

        /// <summary>
        /// Cover grouped into readable clusters: stacked containers in the east and west
        /// lanes, L-shaped walls in the north and south lanes, and loose barriers around
        /// the outer ring.
        /// </summary>
        private static void BuildCoverClusters(ArenaMaterials materials, Transform parent)
        {
            // Stacked containers: full cover in the side lanes.
            foreach (float side in new[] { 1f, -1f })
            {
                CreatePrimitive("Container", PrimitiveType.Cube, new Vector3(13.5f * side, 0.8f, 0f),
                    new Vector3(2.2f, 1.6f, 4.2f), materials.Cover, parent,
                    Quaternion.Euler(0f, 8f * side, 0f));

                CreatePrimitive("Container Top", PrimitiveType.Cube, new Vector3(13.5f * side, 2.05f, 0f),
                    new Vector3(1.6f, 0.9f, 2.6f), materials.CoverAlt, parent,
                    Quaternion.Euler(0f, -18f * side, 0f));
            }

            // L-shaped walls facing the centre. They sit inboard of the ramp corridors
            // so they never block the way up to a platform.
            CreateLWall(materials, parent, new Vector3(2.6f, 0f, 11.6f), -14f);
            CreateLWall(materials, parent, new Vector3(-2.6f, 0f, 11.6f), 14f);
            CreateLWall(materials, parent, new Vector3(2.6f, 0f, -11.6f), 14f);
            CreateLWall(materials, parent, new Vector3(-2.6f, 0f, -11.6f), -14f);

            // Loose barriers around the outer ring.
            Vector3[] barrierPositions =
            {
                new Vector3(18.2f, 0f, 6.5f),
                new Vector3(18.2f, 0f, -6.5f),
                new Vector3(-18.2f, 0f, 6.5f),
                new Vector3(-18.2f, 0f, -6.5f),
                new Vector3(7.2f, 0f, 18.6f),
                new Vector3(-7.2f, 0f, 18.6f),
                new Vector3(7.2f, 0f, -18.6f),
                new Vector3(-7.2f, 0f, -18.6f),
            };

            float[] barrierAngles = { 25f, -25f, -25f, 25f, 65f, -65f, -65f, 65f };

            for (int i = 0; i < barrierPositions.Length; i++)
            {
                Vector3 position = barrierPositions[i];
                CreatePrimitive("Barrier", PrimitiveType.Cube, new Vector3(position.x, 0.65f, position.z),
                    new Vector3(3.4f, 1.3f, 0.7f), materials.Cover, parent,
                    Quaternion.Euler(0f, barrierAngles[i], 0f));
            }
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
                new Vector3(11.3f, 0.45f, 2.9f),
                new Vector3(12.7f, 0.45f, 3.7f),
                new Vector3(-11.3f, 0.45f, -2.9f),
                new Vector3(-12.7f, 0.45f, -3.7f),
                new Vector3(2.6f, 0.45f, 10.8f),
                new Vector3(3.9f, 0.45f, 11.4f),
                new Vector3(-2.6f, 0.45f, -10.8f),
                new Vector3(-3.9f, 0.45f, -11.4f),
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

        /// <summary>Glowing floor strips that lead from the central pad out to the lanes.</summary>
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

            // Enemies come in from the four lanes and the wall centres rather than a
            // plain ring, so they push in from several directions at once. Every point
            // sits in open ground, clear of cover, ramps and platforms.
            Vector3[] positions =
            {
                new Vector3(17f, 0f, -3.5f),
                new Vector3(17f, 0f, 3.5f),
                new Vector3(-17f, 0f, 3.5f),
                new Vector3(-17f, 0f, -3.5f),
                new Vector3(3.5f, 0f, 17f),
                new Vector3(-3.5f, 0f, 17f),
                new Vector3(3.5f, 0f, -17f),
                new Vector3(-3.5f, 0f, -17f),
                new Vector3(0f, 0f, 19.5f),
                new Vector3(0f, 0f, -19.5f),
                new Vector3(19.5f, 0f, 0f),
                new Vector3(-19.5f, 0f, 0f),
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
            player.transform.position = new Vector3(0f, 0.4f, 0f);

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

            // Rifle model: receiver, rail, barrel and muzzle brake, open sights, angled
            // magazine and grip, stock with butt pad, plus an emissive charge strip.
            // Every piece is collider-free and on the Ignore Raycast layer so the weapon
            // can never eat its own shots.
            var model = new GameObject("Model");
            model.transform.SetParent(weaponObject.transform, false);

            CreatePrimitive("Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0.2f),
                new Vector3(0.1f, 0.12f, 0.4f), materials.Gun, model.transform);
            CreatePrimitive("Rail", PrimitiveType.Cube, new Vector3(0f, 0.074f, 0.24f),
                new Vector3(0.05f, 0.018f, 0.44f), materials.GunAccent, model.transform);
            CreatePrimitive("ChargingHandle", PrimitiveType.Cube, new Vector3(0.062f, 0.03f, 0.1f),
                new Vector3(0.02f, 0.03f, 0.07f), materials.GunAccent, model.transform);

            // Barrel: cylinder rotated so its axis runs down the barrel (Z).
            GameObject barrel = CreatePrimitive("Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0.53f),
                new Vector3(0.05f, 0.13f, 0.05f), materials.GunAccent, model.transform,
                Quaternion.Euler(90f, 0f, 0f));
            CreatePrimitive("MuzzleBrake", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.68f),
                new Vector3(0.065f, 0.065f, 0.07f), materials.Gun, model.transform);

            // Open sights at both ends of the rail.
            CreatePrimitive("FrontSight", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.62f),
                new Vector3(0.012f, 0.05f, 0.02f), materials.GunAccent, model.transform);
            CreatePrimitive("RearSight", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.06f),
                new Vector3(0.05f, 0.04f, 0.015f), materials.GunAccent, model.transform);

            // Magazine and grip, both raked for a natural silhouette.
            CreatePrimitive("Magazine", PrimitiveType.Cube, new Vector3(0f, -0.13f, 0.26f),
                new Vector3(0.07f, 0.2f, 0.1f), materials.Gun, model.transform,
                Quaternion.Euler(-8f, 0f, 0f));
            CreatePrimitive("MagazineBase", PrimitiveType.Cube, new Vector3(0f, -0.227f, 0.27f),
                new Vector3(0.075f, 0.02f, 0.105f), materials.GunAccent, model.transform,
                Quaternion.Euler(-8f, 0f, 0f));
            CreatePrimitive("Grip", PrimitiveType.Cube, new Vector3(0f, -0.13f, 0.04f),
                new Vector3(0.06f, 0.17f, 0.09f), materials.Gun, model.transform,
                Quaternion.Euler(14f, 0f, 0f));
            CreatePrimitive("TriggerGuard", PrimitiveType.Cube, new Vector3(0f, -0.075f, 0.14f),
                new Vector3(0.04f, 0.012f, 0.09f), materials.GunAccent, model.transform);

            // Stock reaching back with a butt pad.
            CreatePrimitive("Stock", PrimitiveType.Cube, new Vector3(0f, -0.005f, -0.11f),
                new Vector3(0.075f, 0.1f, 0.2f), materials.Gun, model.transform);
            CreatePrimitive("ButtPad", PrimitiveType.Cube, new Vector3(0f, -0.005f, -0.225f),
                new Vector3(0.085f, 0.12f, 0.03f), materials.GunAccent, model.transform);

            // Emissive charge strip on the left cheek: the weapon's one glowing detail.
            CreatePrimitive("StatusGlow", PrimitiveType.Cube, new Vector3(-0.053f, 0.015f, 0.26f),
                new Vector3(0.006f, 0.026f, 0.14f), materials.Ammo, model.transform);

            foreach (Transform piece in model.transform)
            {
                StripCollider(piece.gameObject);
                piece.gameObject.layer = 2; // Ignore Raycast, matching the player rig.
            }

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

            // All visuals live under one child so the CharacterController defines the physics
            // shape and the parts never contribute colliders. Roughly 0.06..1.77 in height.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            // Hips and lower torso.
            CreatePrimitive("Hips", PrimitiveType.Cube, new Vector3(0f, 0.72f, 0f),
                new Vector3(0.5f, 0.26f, 0.34f), materials.Enemy, model.transform);
            CreatePrimitive("Spine Pack", PrimitiveType.Cube, new Vector3(0f, 1.18f, -0.24f),
                new Vector3(0.4f, 0.5f, 0.18f), materials.GunAccent, model.transform);

            // Chest: tapered capsule with an armour plate over it.
            CreatePrimitive("Torso", PrimitiveType.Capsule, new Vector3(0f, 1.1f, 0f),
                new Vector3(0.72f, 0.32f, 0.5f), materials.Enemy, model.transform);
            CreatePrimitive("Chest Plate", PrimitiveType.Cube, new Vector3(0f, 1.12f, 0.24f),
                new Vector3(0.6f, 0.44f, 0.16f), materials.Enemy, model.transform);
            CreatePrimitive("Chest Core", PrimitiveType.Cube, new Vector3(0f, 1.1f, 0.33f),
                new Vector3(0.22f, 0.16f, 0.03f), materials.EnemyEye, model.transform);

            // Shoulder pads push the silhouette wider than the capsule collider.
            CreatePrimitive("Shoulder L", PrimitiveType.Cube, new Vector3(-0.5f, 1.32f, 0f),
                new Vector3(0.26f, 0.2f, 0.36f), materials.Enemy, model.transform,
                Quaternion.Euler(0f, 0f, 10f));
            CreatePrimitive("Shoulder R", PrimitiveType.Cube, new Vector3(0.5f, 1.32f, 0f),
                new Vector3(0.26f, 0.2f, 0.36f), materials.Enemy, model.transform,
                Quaternion.Euler(0f, 0f, -10f));

            // Arms hang and angle outward, ending in claw pieces.
            CreatePrimitive("Arm L", PrimitiveType.Capsule, new Vector3(-0.56f, 0.92f, 0.04f),
                new Vector3(0.24f, 0.28f, 0.24f), materials.Enemy, model.transform,
                Quaternion.Euler(6f, 0f, 10f));
            CreatePrimitive("Arm R", PrimitiveType.Capsule, new Vector3(0.56f, 0.92f, 0.04f),
                new Vector3(0.24f, 0.28f, 0.24f), materials.Enemy, model.transform,
                Quaternion.Euler(6f, 0f, -10f));
            CreatePrimitive("Claw L", PrimitiveType.Cube, new Vector3(-0.62f, 0.6f, 0.12f),
                new Vector3(0.16f, 0.2f, 0.2f), materials.GunAccent, model.transform,
                Quaternion.Euler(0f, 0f, 20f));
            CreatePrimitive("Claw R", PrimitiveType.Cube, new Vector3(0.62f, 0.6f, 0.12f),
                new Vector3(0.16f, 0.2f, 0.2f), materials.GunAccent, model.transform,
                Quaternion.Euler(0f, 0f, -20f));

            // Legs slightly apart, with forward-extending feet for a planted stance.
            CreatePrimitive("Leg L", PrimitiveType.Capsule, new Vector3(-0.19f, 0.42f, 0f),
                new Vector3(0.28f, 0.26f, 0.28f), materials.Enemy, model.transform);
            CreatePrimitive("Leg R", PrimitiveType.Capsule, new Vector3(0.19f, 0.42f, 0f),
                new Vector3(0.28f, 0.26f, 0.28f), materials.Enemy, model.transform);
            CreatePrimitive("Foot L", PrimitiveType.Cube, new Vector3(-0.2f, 0.06f, 0.06f),
                new Vector3(0.28f, 0.12f, 0.42f), materials.GunAccent, model.transform);
            CreatePrimitive("Foot R", PrimitiveType.Cube, new Vector3(0.2f, 0.06f, 0.06f),
                new Vector3(0.28f, 0.12f, 0.42f), materials.GunAccent, model.transform);

            // Helmet perched on a short neck, with an emissive visor strip.
            CreatePrimitive("Neck", PrimitiveType.Cube, new Vector3(0f, 1.44f, 0f),
                new Vector3(0.22f, 0.14f, 0.22f), materials.GunAccent, model.transform);
            CreatePrimitive("Head", PrimitiveType.Cube, new Vector3(0f, 1.61f, 0f),
                new Vector3(0.36f, 0.32f, 0.34f), materials.Enemy, model.transform);
            CreatePrimitive("EyeVisor", PrimitiveType.Cube, new Vector3(0f, 1.62f, 0.19f),
                new Vector3(0.34f, 0.1f, 0.05f), materials.EnemyEye, model.transform);
            CreatePrimitive("Crest", PrimitiveType.Cube, new Vector3(0f, 1.78f, -0.04f),
                new Vector3(0.08f, 0.1f, 0.3f), materials.Enemy, model.transform);

            foreach (Transform visual in model.transform)
            {
                StripCollider(visual.gameObject);
            }

            EnemyHealth enemyHealth = root.AddComponent<EnemyHealth>();
            enemyHealth.debrisMaterial = materials.EnemyDebris;
            root.AddComponent<EnemyAI>();

            string path = $"{PrefabsFolder}/Enemy.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] Enemy prefab saved to {path}.");
            return prefab;
        }

        /// <summary>
        /// Builds the ammo cache that elites drop. It is a one-shot pickup (it does not
        /// respawn) and carries more rounds than the fixed arena crates.
        /// </summary>
        private static GameObject CreateAmmoCachePrefab(ArenaMaterials materials)
        {
            var root = new GameObject("AmmoCache");

            GameObject crate = CreatePrimitive("Crate", PrimitiveType.Cube, new Vector3(0f, 0f, 0f),
                new Vector3(0.5f, 0.32f, 0.5f), materials.AmmoCache, root.transform);
            StripCollider(crate);

            GameObject lid = CreatePrimitive("Lid", PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f),
                new Vector3(0.34f, 0.3f, 0.34f), materials.AmmoCache, root.transform,
                Quaternion.Euler(0f, 45f, 0f));
            StripCollider(lid);

            GameObject beacon = CreatePrimitive("Beacon", PrimitiveType.Cube, new Vector3(0f, 0.44f, 0f),
                new Vector3(0.16f, 0.09f, 0.16f), materials.AmmoCache, root.transform);
            StripCollider(beacon);

            AmmoPickup pickup = root.AddComponent<AmmoPickup>();
            pickup.Configure(90, 1.9f, 0f, 90f, 0.12f);

            string path = $"{PrefabsFolder}/AmmoCache.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[Shooter] Ammo cache prefab saved to {path}.");
            return prefab;
        }

        // ---------------------------------------------------------------- managers
        private sealed class ManagersRig
        {
            public GameManager Game;
            public WaveSpawner Waves;
            public AudioManager Audio;
            public UpgradeManager Upgrades;
        }

        private static ManagersRig BuildManagers(GameObject enemyPrefab, GameObject ammoCachePrefab, PlayerRig rig,
            List<Transform> spawnPoints)
        {
            var managers = new GameObject("Managers");

            GameManager gameManager = managers.AddComponent<GameManager>();
            WaveSpawner waveSpawner = managers.AddComponent<WaveSpawner>();
            AudioManager audioManager = managers.AddComponent<AudioManager>();
            UpgradeManager upgradeManager = managers.AddComponent<UpgradeManager>();

            waveSpawner.enemyPrefab = enemyPrefab;
            waveSpawner.ammoCachePrefab = ammoCachePrefab;
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
                Upgrades = upgradeManager };
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
                "WASD move   ·   SHIFT sprint   ·   SPACE jump   ·   LMB fire   ·   RMB aim   ·   C slide   ·   R reload   ·   ESC free cursor",
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
            hud.crosshairBars = new[] { crosshairTop, crosshairBottom, crosshairLeft, crosshairRight };
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
            _ = healthLabel;
            _ = ammoLabel;
            _ = gameOverTitle;
            _ = gameOverHint;
            _ = healthBackground;

            return hud;
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

        /// <summary>
        /// Builds the wave-clear augment screen: a dim panel with four cards, each
        /// the colour of its rarity (Standard / Advanced / Master) rather than
        /// its slot. Cards keep a coloured background, header badge and label so
        /// value can be read at a glance. Input stays keyboard only.
        /// </summary>
        private static UpgradePanelRig BuildUpgradePanel(Transform canvas, Font font)
        {
            UpgradePanelRig result = new UpgradePanelRig
            {
                Roots = new GameObject[4],
                Backgrounds = new Image[4],
                Accents = new Image[4],
                Badges = new Image[4],
                Rarities = new Text[4],
                Titles = new Text[4],
                Descriptions = new Text[4],
            };

            Image panelImage = CreateFullScreenImage(canvas, "UpgradePanel", new Color(0.02f, 0.03f, 0.05f, 0.88f));
            result.Panel = panelImage.gameObject;

            Text title = CreateText(panelImage.transform, "UpgradeTitle", "WAVE CLEARED", 64,
                new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1200f, 80f), font, FontStyle.Bold);

            Text subtitle = CreateText(panelImage.transform, "UpgradeSubtitle", "CHOOSE AN AUGMENT", 26,
                new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 188f), new Vector2(1200f, 40f), font);

            result.Hint = CreateText(panelImage.transform, "ChoiceHint", "PRESS 1 / 2 / 3 / 4 TO PICK", 24,
                new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(1200f, 40f), font);

            Vector2[] offsets =
            {
                new Vector2(-645f, -40f), new Vector2(-215f, -40f),
                new Vector2(215f, -40f), new Vector2(645f, -40f),
            };

            for (int i = 0; i < 4; i++)
            {
                RectTransform card = CreateRect("Card" + (i + 1), panelImage.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), offsets[i], new Vector2(360f, 440f));
                result.Roots[i] = card.gameObject;

                Image cardBack = card.gameObject.AddComponent<Image>();
                cardBack.color = new Color(0.10f, 0.11f, 0.14f, 0.98f); // re-tinted to rarity at pick time.
                cardBack.raycastTarget = false;
                result.Backgrounds[i] = cardBack;

                // A thin accent strip along the top edge.
                RectTransform strip = CreateRect("AccentStrip", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, 5f), new Vector2(356f, 10f));
                Image accentStrip = AddImage(strip, Color.white);
                result.Accents[i] = accentStrip;

                // Colour carries the card's VALUE, not its slot: a Master card reads
                // gold wherever it was dealt; a Standard one reads slate.
                RectTransform badge = CreateRect("KeyBadge", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -34f), new Vector2(56f, 56f));
                Image badgeImage = badge.gameObject.AddComponent<Image>();
                badgeImage.color = Color.white;
                badgeImage.raycastTarget = false;
                result.Badges[i] = badgeImage;

                Text badgeText = CreateText(badge, "KeyText", (i + 1).ToString(), 28,
                    new Color(0.05f, 0.06f, 0.09f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f), font, FontStyle.Bold);

                result.Titles[i] = CreateText(card, "CardTitle", "AUGMENT", 28, Color.white,
                    TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 55f), new Vector2(360f, 44f), font, FontStyle.Bold);

                result.Rarities[i] = CreateText(card, "CardRarity", "STANDARD", 13, Color.white,
                    TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 20f), new Vector2(300f, 18f), font, FontStyle.Bold);

                Text description = CreateText(card, "CardDescription", "DESCRIBE THE CARD HERE", 20,
                    Color.white, TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -80f), new Vector2(320f, 250f), font);
                description.horizontalOverflow = HorizontalWrapMode.Wrap; // Descriptions must wrap.
                result.Descriptions[i] = description;

                _ = badgeText;
            }

            _ = title;
            _ = subtitle;

            foreach (GameObject root in result.Roots)
            {
                root.SetActive(false);
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
                new Vector3(8.2f, 0.55f, 0f),
                new Vector3(-8.2f, 0.55f, 0f),
                new Vector3(0f, 0.55f, 8.2f),
                new Vector3(0f, 0.55f, -8.2f),
                new Vector3(15.5f, 2.3f, 15.5f),
                new Vector3(-15.5f, 2.3f, -15.5f),
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

                // Emissive beacon cube so crates read at a distance even in fog.
                GameObject beacon = CreatePrimitive("Beacon", PrimitiveType.Cube, new Vector3(0f, 0.46f, 0f),
                    new Vector3(0.12f, 0.07f, 0.12f), materials.Ammo, crate.transform);
                StripCollider(beacon);

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
