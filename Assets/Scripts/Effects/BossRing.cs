using UnityEngine;

namespace Shooter.Effects
{
    /// <summary>
    /// Flat ground ring used for boss telegraphs and slam shockwaves. It is a
    /// <see cref="LineRenderer"/>, so it needs no mesh asset, and it fades by
    /// tapering its width rather than by alpha: the shared unlit shadow material
    /// is opaque, so colour alpha would never show.
    /// </summary>
    public sealed class BossRing : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private LineRenderer ring;
        private float elapsed;
        private float duration = 1f;
        private float startRadius;
        private float endRadius;
        private float width;
        private bool taperIn;

        /// <summary>
        /// Creates a ring at <paramref name="position"/> that grows from
        /// <paramref name="startRadius"/> to <paramref name="endRadius"/> over
        /// <paramref name="duration"/> seconds and then removes itself.
        /// </summary>
        public static BossRing Spawn(Vector3 position, float startRadius, float endRadius,
            float duration, float width, Color color, Material material, bool taperIn = false)
        {
            var gameObject = new GameObject("Boss Ring");
            gameObject.layer = 2; // Ignore Raycast: markers must never eat shots.

            var ring = gameObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 48;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.numCornerVertices = 2;
            ring.numCapVertices = 2;
            ring.textureMode = LineTextureMode.Stretch;

            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / ring.positionCount;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }

            if (material != null)
            {
                ring.sharedMaterial = material;
            }

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            ring.SetPropertyBlock(block);

            gameObject.transform.position = new Vector3(position.x, 0.06f, position.z);

            BossRing marker = gameObject.AddComponent<BossRing>();
            marker.ring = ring;
            marker.startRadius = Mathf.Max(0.05f, startRadius);
            marker.endRadius = Mathf.Max(0.05f, endRadius);
            marker.duration = Mathf.Max(0.05f, duration);
            marker.width = Mathf.Max(0.01f, width);
            marker.taperIn = taperIn;
            return marker;
        }

        private void Update()
        {
            if (ring == null)
            {
                Destroy(gameObject);
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Ease out so the ring slams outward and settles instead of drifting.
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(startRadius, endRadius, eased);
            transform.localScale = new Vector3(radius, 1f, radius);

            float widthT = taperIn ? Mathf.Clamp01(t / 0.2f) : Mathf.Clamp01((1f - t) / 0.35f);
            ring.widthMultiplier = width * widthT;

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
