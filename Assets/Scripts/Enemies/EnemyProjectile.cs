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

        private void Update()
        {
            float step = speed * Time.deltaTime;
            Vector3 next = transform.position + direction * step;

            // Layer 2 is Ignore Raycast and holds the player, so the sweep only
            // tests world geometry and enemies.
            if (Physics.Linecast(transform.position, next, out RaycastHit hit,
                    ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                Explode();
                return;
            }

            transform.position = next;

            if (target != null && !target.IsDead)
            {
                Vector3 chest = target.transform.position + Vector3.up * 1.1f;

                if ((chest - transform.position).sqrMagnitude <= hitRadius * hitRadius)
                {
                    target.TakeDamage(damage);
                    Explode();
                    return;
                }
            }

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
