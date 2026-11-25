using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Temporary A/B render harness: captures the Foundry arena with the current
    /// Foundry shaders and again with the same materials forced onto URP Lit, so
    /// the visual delta of the shader pass can be judged by eye.
    ///
    /// Needs a graphics device. Unity -projectPath . -executeMethod Shooter.EditorTools.FoundryShot.Run -- /tmp/opencode/foundry
    /// </summary>
    public static class FoundryShot
    {
        private static readonly Shader UrpLit =
            Shader.Find("Universal Render Pipeline/Lit");

        private static readonly (string label, Vector3 eye, Vector3 look, float fov)[] Shots =
        {
            ("spawn", new Vector3(0f, 1.7f, -14f), new Vector3(0f, 1.6f, 0f), 75f),
            ("floor", new Vector3(4f, 1.2f, -6f), new Vector3(2f, 0f, -2.5f), 60f),
            ("hall", new Vector3(-9f, 2.2f, -9f), new Vector3(0f, 2.4f, -2f), 70f),
        };

        public static void Run()
        {
            string output = ArgumentAfter("--") ?? "/tmp/opencode/foundry";
            Directory.CreateDirectory(output);

            EditorSceneManager.OpenScene(ShooterSceneBuilder.ScenePath, OpenSceneMode.Single);

            Camera camera = Camera.main;

            if (camera == null)
            {
                Debug.LogError("[FoundryShot] No main camera in the arena scene.");
                EditorApplication.Exit(1);
                return;
            }

            // Collect unique materials actually on renderers in the scene.
            var materials = new List<Material>();
            var seen = new HashSet<Material>();

            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && seen.Add(material))
                    {
                        materials.Add(material);
                    }
                }
            }

            var originalShaders = new Shader[materials.Count];
            var baseColors = new Color[materials.Count];
            var metallic = new float[materials.Count];
            var smoothness = new float[materials.Count];
            var emissionColors = new Color[materials.Count];
            var hasEmission = new bool[materials.Count];

            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                originalShaders[i] = material.shader;
                baseColors[i] = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor") : Color.white;
                metallic[i] = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
                smoothness[i] = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.2f;
                hasEmission[i] = material.HasProperty("_EmissionColor")
                    && material.GetColor("_EmissionColor").maxColorComponent > 0.01f;
                emissionColors[i] = hasEmission[i] ? material.GetColor("_EmissionColor") : Color.black;
            }

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            camera.targetTexture = rt;
            camera.allowHDR = true;

            // Warm up bloom/tonemap history (first URP frame is under-exposed).
            for (int i = 0; i < 4; i++)
            {
                camera.Render();
            }

            foreach ((string label, Vector3 eye, Vector3 look, float fov) shot in Shots)
            {
                camera.fieldOfView = shot.fov;
                camera.transform.position = shot.eye;
                camera.transform.LookAt(shot.look);

                foreach (Vector2 ndc in new[]
                {
                    new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.85f),
                })
                {
                    Ray probe = camera.ViewportPointToRay(new Vector3(ndc.x, ndc.y, 0f));

                    if (Physics.Raycast(probe, out RaycastHit hit, 300f))
                    {
                        var renderer = hit.collider.GetComponentInParent<Renderer>();
                        string hitMaterials = renderer == null ? "none"
                            : string.Join(",", System.Array.ConvertAll(renderer.sharedMaterials, m => m == null ? "null" : m.name));
                        Debug.Log($"[FoundryShot] {shot.label} ndc({ndc.x},{ndc.y}) -> '{hit.collider.name}' {hit.distance:F1}m materials={hitMaterials}");
                    }
                }

                for (int i = 0; i < materials.Count; i++)
                {
                    Material material = materials[i];

                    if (originalShaders[i] == UrpLit || originalShaders[i] == null)
                    {
                        continue;
                    }

                    material.shader = UrpLit;
                    material.SetColor("_BaseColor", baseColors[i]);
                    material.SetFloat("_Metallic", metallic[i]);
                    material.SetFloat("_Smoothness", smoothness[i]);

                    if (hasEmission[i])
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", emissionColors[i]);
                    }
                }

                Capture(camera, rt, Path.Combine(output, $"{shot.label}_before.png"));

                for (int i = 0; i < materials.Count; i++)
                {
                    materials[i].shader = originalShaders[i];
                }

                Capture(camera, rt, Path.Combine(output, $"{shot.label}_after.png"));
                Debug.Log($"[FoundryShot] {shot.label} before/after written");
            }

            camera.targetTexture = null;
            rt.Release();

            // Report which of the visible materials still use URP Lit.
            int litCount = 0;
            int foundryCount = 0;

            for (int i = 0; i < materials.Count; i++)
            {
                if (originalShaders[i] == UrpLit)
                {
                    litCount++;
                    Debug.Log($"[FoundryShot] still URP Lit: {materials[i].name}");
                }
                else
                {
                    foundryCount++;
                }
            }

            Debug.Log($"[FoundryShot] visible materials: {materials.Count}, Foundry={foundryCount}, URP_Lit={litCount}");
            EditorApplication.Exit(0);
        }

        private static void Capture(Camera camera, RenderTexture target, string path)
        {
            Texture2D texture = null;

            try
            {
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;

                texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                texture.Apply();

                RenderTexture.active = previous;
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        private static string ArgumentAfter(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
