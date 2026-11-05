using Shooter.Effects;
using Shooter.Managers;
using Shooter.Player;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Exploder (Boomer) variant. Runs at the player; if it gets close it starts a
    /// short fuse and detonates, and it *also* detonates when shot dead, so killing
    /// it at range does not make it safe. The blast hurts the player and nearby
    /// enemies, which turns a kill into a crowd-control decision. Stays inert until
    /// the wave spawner initialises it, mirroring <see cref="EnemyRangedAttack"/>.
    /// </summary>
    public sealed class EnemyExploder : MonoBehaviour
    {
        [Header("Detonation")]
        [Tooltip("How close the player must get before the fuse starts.")]
        [SerializeField] private float triggerDistance = 2.4f;
        [Tooltip("Seconds between the fuse starting and the blast.")]
        [SerializeField] private float fuseTime = 0.55f;
        [SerializeField] private float explosionRadius = 3.6f;
        [SerializeField] private float explosionDamage = 18f;

        [Header("Wiring (scene builder)")]
        public Material explosionMaterial;
        public Material sparkMaterial;

        private PlayerHealth target;
        private EnemyHealth health;
        private Transform model;
        private float fuseEndsAt = -1f;
        private bool active;
        private bool exploded;

        /// <summary>True when the spawner has initialised this instance as an exploder.</summary>
        public bool IsActive => active;

        /// <summary>True once the blast has gone off.</summary>
        public bool IsExploded => exploded;

        /// <summary>True while the proximity fuse is burning.</summary>
        public bool IsFusing => fuseEndsAt >= 0f;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            model = transform.Find("Model");

            if (health != null)
            {
                health.Died += HandleDied;
            }
        }

        /// <summary>Turns the exploder on for this instance and scales its blast.</summary>
        public void Initialize(PlayerHealth player, float damageBonus)
        {
            target = player;
            explosionDamage += Mathf.Max(0f, damageBonus);
            active = true;
            fuseEndsAt = -1f;
            exploded = false;
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Died -= HandleDied;
            }
        }

        private void HandleDied(EnemyHealth enemy)
        {
            // Shot dead: still goes off. That is the whole threat of the variant.
            Detonate();
        }

        private void Update()
        {
            if (!active || exploded)
            {
                return;
            }

            if (health != null && health.IsDead)
            {
                return; // Died already detonated; the corpse is shrinking away.
            }

            if (target == null || target.IsDead ||
                (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            {
                return;
            }

            Vector3 toPlayer = target.transform.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            if (fuseEndsAt >= 0f)
            {
                // Player broke clear of the blast radius: the fuse fizzles.
                if (distance > explosionRadius * 1.25f)
                {
                    fuseEndsAt = -1f;
                    return;
                }

                if (Time.time >= fuseEndsAt)
                {
                    Detonate();
                }
            }
            else if (distance <= triggerDistance)
            {
                fuseEndsAt = Time.time + fuseTime;
                AudioManager.Instance?.PlayEnemyCharge(transform.position);
            }

            UpdatePulse();
        }

        /// <summary>
        /// Flash-pulses the Model child (never the root, whose scale carries the
        /// CharacterController). Faster while the fuse burns.
        /// </summary>
        private void UpdatePulse()
        {
            if (model == null)
            {
                return;
            }

            float rate = IsFusing ? 14f : 5f;
            float pulse = 1f + Mathf.Sin(Time.time * rate) * (IsFusing ? 0.12f : 0.05f);
            model.localScale = Vector3.one * pulse;
        }

        /// <summary>
        /// The blast: falloff damage to the player, heavy damage to nearby friends,
        /// then the shared explosion visual. Runs exactly once.
        /// </summary>
        public void Detonate()
        {
            if (exploded)
            {
                return;
            }

            exploded = true;
            fuseEndsAt = -1f;

            Vector3 origin = transform.position + Vector3.up * 0.9f;
            float radius = explosionRadius;
            float damage = explosionDamage;

            SpawnExplosionEffect(origin, radius);
            AudioManager.Instance?.PlayExplosion(transform.position);

            if (target != null && !target.IsDead)
            {
                float distance = Vector3.Distance(origin, target.transform.position + Vector3.up * 1.1f);

                if (distance <= radius)
                {
                    float falloff = Mathf.Lerp(1f, 0.4f, distance / Mathf.Max(0.01f, radius));
                    target.TakeDamage(damage * falloff);
                }
            }

            // Friendly fire: the blast punches a hole in the pack, which is why
            // these enemies are worth killing with Chain Reaction / explosive rounds.
            foreach (EnemyHealth other in FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude))
            {
                if (other == null || other.IsDead || other == health)
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, other.transform.position);

                if (distance <= radius)
                {
                    float falloff = Mathf.Lerp(1f, 0.3f, distance / Mathf.Max(0.01f, radius));
                    other.TakeDamage(damage * 0.6f * falloff, other.transform.position);
                }
            }
        }

        private void SpawnExplosionEffect(Vector3 point, float radius)
        {
            float size = Mathf.Max(0.3f, radius * 0.5f);

            if (explosionMaterial != null)
            {
                GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flash.name = "Exploder Blast";
                flash.layer = 2;
                flash.transform.position = point;
                flash.transform.localScale = Vector3.one * size;

                Collider collider = flash.GetComponent<Collider>();

                if (collider != null)
                {
                    Destroy(collider);
                }

                MeshRenderer renderer = flash.GetComponent<MeshRenderer>();

                if (renderer != null)
                {
                    renderer.sharedMaterial = explosionMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Destroy(flash, 0.14f);
            }

            DebrisBurst.Spawn(point, Random.onUnitSphere, sparkMaterial, 8, 5f, 0.05f, 0.6f);

            var lightObject = new GameObject("Exploder Light");
            lightObject.transform.position = point;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.15f);
            light.range = radius * 2.5f;
            light.intensity = 9f;
            light.shadows = LightShadows.None;
            Destroy(lightObject, 0.16f);
        }
    }
}
