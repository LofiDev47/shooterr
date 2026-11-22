using Shooter.Effects;
using Shooter.Player;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Slow, visible marksman bolt. It travels in a straight line, explodes on
    /// geometry and only damages the player, so sidestepping or using cover
    /// actually avoids it. Colliders are stripped: travel is resolved with a
    /// linecast instead of Rigidbody physics, which keeps it deterministic.
    /// </summary>
    public sealed class EnemyProjectile : MonoBehaviour
    {
        [SerializeField] private float lifetime = 6f;

        private Vector3 direction;
        private float speed;
        private float damage;
        private float hitRadius;
        private PlayerHealth target;
        private Material sparkMaterial;

        /// <summary>
        /// Creates a bolt at <paramref name="origin"/> travelling along
        /// <paramref name="direction"/>. The player is damaged on proximity.
        /// </summary>
        public static void Spawn(
            Vector3 origin,
            Vector3 direction,
            float speed,
            float damage,
            float hitRadius,
            PlayerHealth target,
            Material boltMaterial,
            Material sparkMaterial)
        {
            GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bolt.name = "Marksman Bolt";
            bolt.transform.position = origin;
            bolt.transform.localScale = Vector3.one * 0.24f;

            Collider collider = bolt.GetComponent<Collider>();

            if (collider != null)
            {
                Destroy(collider);
            }

            MeshRenderer renderer = bolt.GetComponent<MeshRenderer>();
            if (FoundryEffects.Spawn(FoundryEffect.Bolt, origin, Quaternion.identity, 5f, 0.3f, bolt.transform, true) != null)
                renderer.enabled = false;

            if (renderer != null && boltMaterial != null)
            {
                renderer.sharedMaterial = boltMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var lightObject = new GameObject("Bolt Light");
            lightObject.transform.SetParent(bolt.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.35f, 0.16f);
            light.range = 3.2f;
            light.intensity = 3.2f;
            light.shadows = LightShadows.None;

            EnemyProjectile projectile = bolt.AddComponent<EnemyProjectile>();
            projectile.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            projectile.speed = Mathf.Max(1f, speed);
            projectile.damage = Mathf.Max(0f, damage);
            projectile.hitRadius = Mathf.Max(0.1f, hitRadius);
            projectile.target = target;
            projectile.sparkMaterial = sparkMaterial;
        }

        /// <summary>
        /// Swept proximity test: measures the closest approach of the player's
        /// chest to the travelled segment instead of just the endpoint, so a fast
        /// bolt cannot step over the player between two frames. Travel is already
        /// clamped to the geometry hit, so cover still blocks the bolt.
        /// </summary>
        public static bool SweptHit(Vector3 origin, Vector3 direction, float travel,
            Vector3 point, float radius, out float distance)
        {
            float t = Mathf.Clamp(Vector3.Dot(point - origin, direction), 0f, Mathf.Max(0f, travel));
            Vector3 closest = origin + direction * t;
            distance = t;
            return (point - closest).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// When geometry stopped the bolt, only a player whose closest approach
        /// happens BEFORE the wall may be hit — otherwise cover is decorative.
        /// </summary>
        public static bool CanHitPlayer(float hitDistance, float travel, bool geometryHit) =>
            !geometryHit || hitDistance < travel - 0.001f;

        private void Update()
        {
            float step = speed * Time.deltaTime;
            Vector3 origin = transform.position;
            Vector3 next = origin + direction * step;
            float travel = step;
            bool geometryHit = false;
            Vector3 impactPoint = next;

            // Layer 2 is Ignore Raycast and holds the player. Enemies share the
            // default layer, so filter them out of the geometry test: a bolt must
            // only detonate on world geometry, never on an ally.
            RaycastHit[] geometryHits = Physics.RaycastAll(origin, direction, step,
                ~(1 << 2), QueryTriggerInteraction.Ignore);
            System.Array.Sort(geometryHits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit candidate in geometryHits)
            {
                if (candidate.collider == null)
                {
                    continue;
                }

                if (candidate.collider.GetComponentInParent<EnemyAI>() != null ||
                    candidate.collider.GetComponentInParent<EnemyHealth>() != null)
                {
                    continue;
                }

                geometryHit = true;
                travel = Mathf.Min(step, candidate.distance);
                impactPoint = candidate.point;
                break;
            }

            if (target != null && !target.IsDead)
            {
                Vector3 chest = target.transform.position + Vector3.up * 1.1f;

                if (SweptHit(origin, direction, travel, chest, hitRadius, out float playerHitDistance) &&
                    CanHitPlayer(playerHitDistance, travel, geometryHit))
                {
                    transform.position = origin + direction * playerHitDistance;
                    target.TakeDamage(damage);
                    Explode();
                    return;
                }
            }

            if (geometryHit)
            {
                transform.position = impactPoint;
                Explode();
                return;
            }

            transform.position = next;

            lifetime -= Time.deltaTime;

            if (lifetime <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void Explode()
        {
            DebrisBurst.Spawn(transform.position, Random.onUnitSphere, sparkMaterial, 5, 3f, 0.035f, 0.5f);
            Destroy(gameObject);
        }
    }
}
