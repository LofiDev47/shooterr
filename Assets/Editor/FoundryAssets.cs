using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Shooter.EditorTools
{
    internal static class FoundryAssets
    {
        internal const string Folder = "Assets/Models/Foundry";
        internal const string Materials = "Assets/Materials/Foundry";

        internal static readonly string[] ActorNames = { "Idle", "Walk", "Slam", "Volley", "Roar", "Hit", "Death" };
        internal static readonly string[] PlayerNames =
            { "Idle", "Move", "Fire", "BreachFire", "Reload", "BreachReload", "Melee", "Hit", "Death" };

        private static readonly Vector2Int[] ActorFrames =
        {
            new(0, 60), new(62, 92), new(94, 153), new(155, 195), new(197, 251), new(253, 269), new(271, 397),
        };
        private static readonly Vector2Int[] PlayerFrames =
        {
            new(0, 60), new(62, 92), new(94, 103), new(105, 125), new(127, 172),
            new(174, 246), new(248, 260), new(262, 279), new(281, 341),
        };

        internal static GameObject LoadActor(string name) => Load(name, ActorNames, ActorFrames);
        internal static GameObject LoadPlayer() => Load("PlayerView", PlayerNames, PlayerFrames);
        internal static GameObject LoadPickup(string name) =>
            Load(name, new[] { "Idle" }, new[] { new Vector2Int(0, 72) });
        internal static GameObject LoadEffect(string name, int frames) =>
            Load(name, new[] { "Effect" }, new[] { new Vector2Int(0, frames) });
        internal static GameObject LoadProp(string name) => Load(name, null, null);

        private static GameObject Load(string name, string[] names, Vector2Int[] frames)
        {
            string path = $"{Folder}/{name}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                throw new System.InvalidOperationException($"Missing Foundry model at {path}.");

            bool animated = names != null;
            bool dirty = importer.importAnimation != animated ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard ||
                (animated && importer.animationType != ModelImporterAnimationType.Legacy);

            importer.importAnimation = animated;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            if (animated)
            {
                importer.animationType = ModelImporterAnimationType.Legacy;
                ModelImporterClipAnimation[] current = importer.clipAnimations;
                var slices = new ModelImporterClipAnimation[names.Length];
                for (int i = 0; i < slices.Length; i++)
                {
                    bool loop = names[i] is "Idle" or "Move" or "Walk";
                    slices[i] = new ModelImporterClipAnimation
                    {
                        takeName = "Scene", name = names[i], firstFrame = frames[i].x, lastFrame = frames[i].y,
                        loopTime = loop, wrapMode = loop ? WrapMode.Loop : WrapMode.Once,
                    };
                    dirty |= current.Length != names.Length || current[i].name != names[i] ||
                        current[i].firstFrame != frames[i].x || current[i].lastFrame != frames[i].y ||
                        current[i].wrapMode != slices[i].wrapMode ||
                        current[i].takeName != slices[i].takeName || current[i].loopTime != loop;
                }
                if (dirty)
                    importer.clipAnimations = slices;
            }
            if (dirty)
                importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        internal static AnimationClip Clip(GameObject source, string name)
        {
            string path = AssetDatabase.GetAssetPath(source);
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(item => item.name == name && item.legacy && !item.empty);
            if (clip == null)
                throw new System.InvalidOperationException($"Missing legacy clip {name} in {path}.");
            return clip;
        }

        internal static Animation Bind(GameObject instance, GameObject source, string[] names)
        {
            Animation animation = instance.GetComponent<Animation>() ?? instance.AddComponent<Animation>();
            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            AnimationUtility.SetAnimationClips(animation, names.Select(name => Clip(source, name)).ToArray());
            return animation;
        }

        internal static void Skin(GameObject model)
        {
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = RenderMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
        }

        private static Material RenderMaterial(Material source)
        {
            if (source == null)
                return null;
            string path = $"{Materials}/{source.name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color color = source.HasProperty("_Color") ? source.color : Color.gray;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.43f);
            bool luminous = source.name.EndsWith("Glow") || source.name.EndsWith("Signal") ||
                source.name == "FoundryWarning";
            if (luminous)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2f);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
