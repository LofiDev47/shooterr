using System.Collections.Generic;
using System.IO;
using Shooter.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Renders The Warden in the real arena so the authored model, materials and
    /// clips can be checked by eye. Needs a graphics device, so it is run without
    /// -nographics: Unity -projectPath . -executeMethod ... -bossRender
    ///
    /// Writes one PNG per sampled pose to the output folder given after "--".
    /// </summary>
    public static class BossRender
    {
        private const string PrefabPath = "Assets/Prefabs/BossWarden.prefab";

        public static void Run()
        {
            string output = ArgumentAfter("--") ?? "/tmp/opencode/boss";
            Directory.CreateDirectory(output);

            EditorSceneManager.OpenScene(ShooterSceneBuilder.ScenePath, OpenSceneMode.Single);

            // The player camera drives itself; a dedicated camera keeps the shots stable.
            foreach (Camera existing in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                existing.enabled = false;
            }

            var cameraObject = new GameObject("BossRenderCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.06f, 0.075f);
            cameraObject.transform.position = new Vector3(0f, 2.7f, 10.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 2.3f, 0f));

            var rimObject = new GameObject("BossRimLight");
            Light rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1f, 0.62f, 0.42f);
            rim.intensity = 1.4f;
            rimObject.transform.rotation = Quaternion.Euler(28f, 165f, 0f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            if (prefab == null)
            {
                Debug.LogError($"[BossRender] Missing {PrefabPath}.");
                EditorApplication.Exit(1);
                return;
            }

            GameObject boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            boss.transform.position = Vector3.zero;
            // The rig's authored forward is +Z, which is what the controller uses
            // for facing and the muzzle offset, so no correction rotation is needed.
            boss.transform.rotation = Quaternion.identity;

            BossAnimator animator = boss.GetComponent<BossAnimator>();

            if (animator == null)
            {
                Debug.LogError("[BossRender] Prefab has no BossAnimator.");
                EditorApplication.Exit(1);
                return;
            }

            Animation animation = animator.GetComponentInChildren<Animation>();

            if (animation == null)
            {
                Debug.LogError("[BossRender] No Animation component on the rig.");
                EditorApplication.Exit(1);
                return;
            }

            var clips = new Dictionary<string, AnimationClip>
            {
                { "Idle", animator.idle },
                { "Walk", animator.walk },
                { "Slam", animator.slam },
                { "Volley", animator.volley },
                { "Roar", animator.roar },
                { "Hit", animator.hit },
                { "Death", animator.death },
            };

            foreach (KeyValuePair<string, AnimationClip> pair in clips)
            {
                if (pair.Value == null)
                {
                    Debug.LogWarning($"[BossRender] Clip {pair.Key} is missing.");
                    continue;
                }

                animation.AddClip(pair.Value, pair.Key);
            }

            // Pose samples: the moments that carry the readability of each action.
            var shots = new (string clip, float time, string label)[]
            {
                ("Idle", 0.0f, "idle_a"),
                ("Idle", 1.2f, "idle_b"),
                ("Walk", 0.35f, "walk_a"),
                ("Walk", 0.95f, "walk_b"),
                ("Slam", 0.45f, "slam_windup"),
                ("Slam", 0.86f, "slam_impact"),
                ("Slam", 1.5f, "slam_recover"),
                ("Volley", 0.5f, "volley_aim"),
                ("Volley", 0.9f, "volley_fire"),
                ("Roar", 0.55f, "roar"),
                ("Hit", 0.2f, "hit"),
                ("Death", 0.9f, "death_a"),
                ("Death", 2.2f, "death_b"),
            };

            var rt = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            camera.targetTexture = rt;

            // Bloom and tonemapping build history over frames; without warming the
            // target up, the first capture comes out several stops dark.
            for (int i = 0; i < 4; i++)
            {
                camera.Render();
            }

            foreach ((string clip, float time, string label) shot in shots)
            {
                if (!clips.TryGetValue(shot.clip, out AnimationClip asset) || asset == null)
                {
                    continue;
                }

                // SampleAnimation poses the hierarchy straight from the clip. It
                // bypasses the Animation state machine, so no weight, fade or
                // wrap-mode setting can silently leave the rig in its bind pose.
                asset.SampleAnimation(animation.gameObject,
                    Mathf.Clamp(shot.time, 0f, Mathf.Max(0f, asset.length - 0.01f)));

                string path = Path.Combine(output, $"boss_{shot.label}.png");
                Capture(camera, rt, path);
                Debug.Log($"[BossRender] {shot.label} -> {path}");
            }

            camera.targetTexture = null;
            rt.Release();

            // One clean three-quarter angle for scale and silhouette.
            cameraObject.transform.position = new Vector3(9.4f, 2.6f, 4.6f);
            cameraObject.transform.LookAt(new Vector3(0f, 2.2f, 0f));

            if (animator.idle != null)
            {
                animator.idle.SampleAnimation(animation.gameObject, 0.9f);
            }

            var sideRt = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32);
            sideRt.antiAliasing = 4;
            camera.targetTexture = sideRt;
            Capture(camera, sideRt, Path.Combine(output, "boss_side.png"));
            camera.targetTexture = null;
            sideRt.Release();

            Debug.Log("[BossRender] Done.");
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
