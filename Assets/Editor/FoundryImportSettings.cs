using UnityEditor;
using UnityEngine;

namespace Shooter.EditorTools
{
    public static class FoundryImportSettings
    {
        [MenuItem("Shooter/Apply Foundry Import Settings")]
        public static void Apply()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models/Foundry" });
            int changed = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (path == FoundryAssets.Folder + "/PlayerView.fbx")
                    continue;

                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                bool modified = false;

                if (importer.meshCompression != ModelImporterMeshCompression.Medium)
                {
                    importer.meshCompression = ModelImporterMeshCompression.Medium;
                    modified = true;
                }

                if (!importer.optimizeGameObjects)
                {
                    importer.optimizeGameObjects = true;
                    modified = true;
                }

                if (!importer.removeConstantScaleCurves)
                {
                    importer.removeConstantScaleCurves = true;
                    modified = true;
                }

                if (modified)
                {
                    importer.SaveAndReimport();
                    changed++;
                }
            }

            Debug.Log($"[FoundryImportSettings] Updated {changed} FBX importers.");
        }
    }
}