using System;
using UnityEditor;
using UnityEngine;

namespace Shooter.EditorTools
{
    internal sealed class FoundryViewImport : AssetPostprocessor
    {
        private void OnPostprocessModel(GameObject root)
        {
            if (assetPath != FoundryAssets.Folder + "/PlayerView.fbx")
                return;

            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                part.gameObject.layer = 2;

            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (assetPath != FoundryAssets.Folder + "/PlayerView.fbx")
                return;

            // The Blender FBX contains constant samples for every transform.
            // Gameplay drives the slide during firing and the pump during cycling.
            foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
            {
                if ((clip.name != "Reload" && curve.path.EndsWith("/Slide", StringComparison.Ordinal)) ||
                    curve.path.EndsWith("/Pump", StringComparison.Ordinal))
                    AnimationUtility.SetEditorCurve(clip, curve, null);
            }
        }
    }
}
