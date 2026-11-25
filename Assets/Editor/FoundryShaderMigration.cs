using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Moves the remaining flat URP materials onto the Foundry shader family so the
    /// arena, weapon and props share the procedural surface language (wear, tread,
    /// machined alloy, signal pulse) instead of plain lit colour.
    ///
    /// Idempotent: materials already on a Foundry shader are left untouched, so
    /// hand-tuned values survive a re-run.
    ///
    /// Unity -batchmode -quit -projectPath . -executeMethod Shooter.EditorTools.FoundryShaderMigration.Run
    /// </summary>
    public static class FoundryShaderMigration
    {
        private const string MaterialsFolder = "Assets/Materials";

        public const string ArmorShader = "Shooter/Foundry Armor";
        public const string AlloyShader = "Shooter/Foundry Alloy";
        public const string GlowShader = "Shooter/Foundry Glow";
        public const string SignalShader = "Shooter/Foundry Signal";
        public const string SurfaceShader = "Shooter/Foundry Surface";

        private enum Family
        {
            None,
            Armor,
            Alloy,
            Glow,
            Signal,
            Surface,
        }

        // Materials that were still on Universal Render Pipeline/Lit after the
        // first pass. Unlit effect materials (tracer, muzzle, spark, bolt, eye)
        // and the skybox are deliberately excluded: they must stay unlit/additive.
        private static readonly Dictionary<string, Family> Pending = new()
        {
            // Arena shell
            { "Stone", Family.Surface },
            { "Floor", Family.Surface },
            { "Pad", Family.Surface },
            { "Wall", Family.Armor },
            { "Cover", Family.Armor },
            { "CoverAlt", Family.Armor },
            { "DarkMetal", Family.Alloy },

            // Signal strips
            { "Accent", Family.Signal },
            { "Hazard", Family.Signal },

            // Weapon
            { "Gun", Family.Alloy },
            { "GunAccent", Family.Alloy },
            { "Brass", Family.Alloy },
            { "Grip", Family.Armor },
        };

        [MenuItem("Shooter/Migrate Materials To Foundry Shaders")]
        public static void Run()
        {
            int migrated = 0;
            int skipped = 0;
            var missing = new List<string>();

            foreach (KeyValuePair<string, Family> entry in Pending)
            {
                string path = $"{MaterialsFolder}/{entry.Key}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    missing.Add(entry.Key);
                    continue;
                }

                if (IsFoundry(material.shader))
                {
                    skipped++;
                    continue;
                }

                // Snapshot before the swap: assigning a shader can reset values
                // that the new shader does not share by name.
                Color baseColor = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material.HasProperty("_Color") ? material.color : Color.white;
                float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
                float smoothness = material.HasProperty("_Smoothness")
                    ? material.GetFloat("_Smoothness") : 0.45f;
                bool emissive = material.HasProperty("_EmissionColor")
                    && material.GetColor("_EmissionColor").maxColorComponent > 0.01f;
                Color emission = emissive ? material.GetColor("_EmissionColor") : Color.black;

                Shader target = Shader.Find(ShaderName(entry.Value));

                if (target == null)
                {
                    Debug.LogError($"[FoundryShader] Missing shader {ShaderName(entry.Value)}");
                    continue;
                }

                material.shader = target;
                material.SetColor("_BaseColor", baseColor);
                material.SetColor("_Color", baseColor);

                switch (entry.Value)
                {
                    case Family.Armor:
                        material.SetColor("_DarkColor", baseColor * 0.3f);
                        material.SetFloat("_Metallic", metallic);
                        material.SetFloat("_Smoothness", smoothness);
                        material.SetFloat("_ScratchStrength", 0.5f);
                        material.SetFloat("_EdgeHighlight", 0.6f);
                        material.SetFloat("_MicroDetail", 0.3f);
                        break;

                    case Family.Alloy:
                        material.SetFloat("_Metallic", Mathf.Max(metallic, 0.6f));
                        material.SetFloat("_Smoothness", Mathf.Max(smoothness, 0.5f));
                        material.SetFloat("_AnisotropyStrength", 0.3f);
                        material.SetColor("_FresnelTint", new Color(0.4f, 0.5f, 0.6f, 1f));
                        material.SetFloat("_FresnelPower", 3f);
                        material.SetFloat("_MachiningScale", 20f);
                        material.SetFloat("_MachiningStrength", 0.08f);
                        break;

                    case Family.Surface:
                        material.SetFloat("_Metallic", metallic);
                        material.SetFloat("_Smoothness", smoothness);
                        material.SetFloat("_WearStrength", 0.35f);
                        // Floors and pads get the raised tread; walls and props do not.
                        material.SetFloat("_TreadStrength",
                            entry.Key is "Stone" or "Floor" or "Pad" ? 0.8f : 0f);
                        break;

                    case Family.Signal:
                        material.SetColor("_EmissionColor", emissive ? emission : baseColor * 1.6f);
                        material.SetFloat("_PulseRate", 0.55f);
                        material.SetFloat("_PulseDepth", 0.16f);
                        break;

                    case Family.Glow:
                        material.SetColor("_EmissionColor", emissive ? emission : baseColor * 2f);
                        material.SetFloat("_PulseRate", 0f);
                        material.SetFloat("_PulseDepth", 0.3f);
                        material.SetFloat("_HeatIntensity", 0f);
                        material.SetFloat("_NoiseScale", 8f);
                        material.SetFloat("_NoiseStrength", 0.25f);
                        material.SetFloat("_FresnelBoost", 0.5f);
                        break;
                }

                EditorUtility.SetDirty(material);
                migrated++;
                Debug.Log($"[FoundryShader] {entry.Key}: {entry.Value}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (missing.Count > 0)
            {
                Debug.LogWarning($"[FoundryShader] Missing material assets: {string.Join(", ", missing)}");
            }

            Debug.Log($"[FoundryShader] Done. Migrated={migrated} already-on-foundry={skipped} missing={missing.Count}");
        }

        /// <summary>Shader for a material name, used when new material assets are created.</summary>
        public static string ShaderNameFor(string name)
        {
            if (Pending.TryGetValue(name, out Family family))
            {
                return ShaderName(family);
            }

            if (name.EndsWith("Glow"))
            {
                return GlowShader;
            }

            if (name.EndsWith("Signal") || name.Contains("Warning"))
            {
                return SignalShader;
            }

            if (name.StartsWith("Stone") || name.StartsWith("Floor") || name.StartsWith("Ceramic")
                || name.StartsWith("Frame") || name.StartsWith("Ground"))
            {
                return SurfaceShader;
            }

            if (name.StartsWith("Steel") || name.StartsWith("Alloy") || name.StartsWith("Graphite")
                || name.StartsWith("Copper") || name.StartsWith("Rifle") || name.StartsWith("WardenTrim"))
            {
                return AlloyShader;
            }

            return ArmorShader;
        }

        private static string ShaderName(Family family)
        {
            return family switch
            {
                Family.Armor => ArmorShader,
                Family.Alloy => AlloyShader,
                Family.Glow => GlowShader,
                Family.Signal => SignalShader,
                Family.Surface => SurfaceShader,
                _ => ArmorShader,
            };
        }

        private static bool IsFoundry(Shader shader)
        {
            return shader != null && shader.name.StartsWith("Shooter/Foundry");
        }
    }
}
