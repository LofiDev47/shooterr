using System;
using UnityEditor;
using UnityEngine;

namespace Shooter.EditorTools
{
    internal sealed class FoundryViewImport : AssetPostprocessor
    {
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (assetPath != FoundryAssets.Folder + "/PlayerView.fbx")
                return;

            // The Blender FBX contains constant samples for every transform.
            // These two controls are owned by the existing weapon timing code.
            foreach (EditorCurveBinding curve in AnimationUtility.GetCurveBindings(clip))
            {
                if (curve.path.EndsWith("/Slide", StringComparison.Ordinal) ||
                    curve.path.EndsWith("/Pump", StringComparison.Ordinal))
                    AnimationUtility.SetEditorCurve(clip, curve, null);
            }
        }
    }
}
