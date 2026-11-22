using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shooter.EditorTools
{
    internal static class FoundryHudSkin
    {
        internal static Sprite Plate => Load("Plate");
        internal static Sprite Emblem => Load("FoundryEmblem");
        internal static Sprite Indicator => Load("Indicator");

        internal static void Apply(GameObject root)
        {
            Transform hud = root.transform;
            Sprite horizontal = Load("ReticleBarHorizontal");
            Sprite vertical = Load("ReticleBarVertical");

            Transform crosshair = hud.Find("Crosshair");
            if (crosshair != null)
            {
                Bar(crosshair, "Left", horizontal, 180f);
                Bar(crosshair, "Right", horizontal, 0f);
                Bar(crosshair, "Bottom", vertical, 180f);
                Bar(crosshair, "Top", vertical, 0f);

                Transform topBar = crosshair.Find("Top");
                Color diamondColor = Color.white;
                if (horizontal != null && topBar != null)
                {
                    Image topImage = topBar.GetComponent<Image>();
                    if (topImage != null)
                    {
                        diamondColor = topImage.color;
                    }
                }

                AddImage(crosshair, "CenterDiamond", Load("Reticle"), new Vector2(4f, 4f), diamondColor);
            }

            Transform hitMarker = hud.Find("HitMarker");
            if (hitMarker != null)
            {
                Image[] ticks = hitMarker.GetComponentsInChildren<Image>(true);
                Color tickColor = Color.white;
                if (ticks != null && ticks.Length > 0 && ticks[0] != null)
                {
                    tickColor = ticks[0].color;
                }

                AddImage(hitMarker, "FoundryHitMarker", Load("HitMarker"), new Vector2(29f, 29f), tickColor);

                if (ticks != null)
                {
                    foreach (Image tick in ticks)
                    {
                        if (tick != null)
                        {
                            tick.enabled = false;
                        }
                    }
                }
            }

            Sprite pip = Load("PhasePip");
            Sprite plate = Plate;
            Transform bossTransform = hud.Find("BossBar");
            if (bossTransform is RectTransform boss)
            {
                AddImage(boss, "InstrumentPlate", plate, boss.sizeDelta + new Vector2(34f, 18f),
                    new Color(0.025f, 0.046f, 0.060f, 0.92f));

                Transform plateTransform = boss.Find("InstrumentPlate");
                if (plateTransform != null)
                {
                    plateTransform.SetAsFirstSibling();
                }
            }
            foreach (Image image in hud.GetComponentsInChildren<Image>(true))
            {
                if (image.name.StartsWith("BossPhasePip", System.StringComparison.Ordinal))
                    image.sprite = pip;
                if (image.name == "GameOverPanel")
                    image.sprite = plate;
            }
        }

        private static void Bar(Transform crosshair, string name, Sprite sprite, float angle)
        {
            if (crosshair == null)
            {
                return;
            }

            Transform bar = crosshair.Find(name);
            if (bar == null)
            {
                return;
            }

            Image image = bar.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

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
