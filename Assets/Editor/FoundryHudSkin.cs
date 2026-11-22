using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shooter.EditorTools
{
    internal static class FoundryHudSkin
    {
        internal static Sprite Plate => Load("Plate");
        internal static Sprite Emblem => Load("FoundryEmblem");

        internal static void Apply(GameObject root)
        {
            Transform hud = root.transform;
            Transform crosshair = hud.Find("Crosshair");
            Sprite horizontal = Load("ReticleBarHorizontal");
            Sprite vertical = Load("ReticleBarVertical");
            Bar(crosshair, "Left", horizontal, 180f);
            Bar(crosshair, "Right", horizontal, 0f);
            Bar(crosshair, "Bottom", vertical, 180f);
            Bar(crosshair, "Top", vertical, 0f);
            AddImage(crosshair, "CenterDiamond", Load("Reticle"), new Vector2(4f, 4f), horizontal != null
                ? crosshair.Find("Top").GetComponent<Image>().color : Color.white);

            Transform hitMarker = hud.Find("HitMarker");
            Image[] ticks = hitMarker.GetComponentsInChildren<Image>(true);
            AddImage(hitMarker, "FoundryHitMarker", Load("HitMarker"), new Vector2(29f, 29f), ticks[0].color);
            foreach (Image tick in ticks)
                tick.enabled = false;

            Sprite pip = Load("PhasePip");
            Sprite plate = Plate;
            foreach (string name in new[] { "HealthPanel", "AmmoPanel", "BossBar" })
            {
                Transform target = hud.Find(name);
                var bounds = (RectTransform)target;
                AddImage(target, "InstrumentPlate", plate, bounds.sizeDelta + new Vector2(34f, 18f),
                    new Color(0.025f, 0.046f, 0.060f, 0.92f));
                target.Find("InstrumentPlate").SetAsFirstSibling();
            }
            foreach (Image image in hud.GetComponentsInChildren<Image>(true))
            {
                if (image.name.StartsWith("BossPhasePip", System.StringComparison.Ordinal))
                    image.sprite = pip;
                if (image.name == "GameOverPanel" || image.name == "UpgradePanel")
                    image.sprite = plate;
            }
        }

        private static void Bar(Transform crosshair, string name, Sprite sprite, float angle)
        {
            Image image = crosshair.Find(name).GetComponent<Image>();
            image.sprite = sprite;
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private static void AddImage(Transform parent, string name, Sprite sprite, Vector2 size, Color color)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            Image image = child.GetComponent<Image>();
            image.rectTransform.sizeDelta = size;
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        private static Sprite Load(string name)
        {
            string path = $"{FoundryAssets.Folder}/HUD/{name}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                throw new System.InvalidOperationException($"Missing Foundry HUD sprite at {path}.");

            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                !importer.alphaIsTransparency || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
