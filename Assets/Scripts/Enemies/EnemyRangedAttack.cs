using Shooter.Managers;
using Shooter.Player;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Marksman ranged attack. Before every shot the enemy paints a laser line at
    /// the player for a readable wind-up, then launches a slow <see cref="EnemyProjectile"/>
    /// along that line. Breaking line of sight cancels the shot, so cover matters.
    /// The component stays inert until the wave spawner initialises it.
    /// </summary>
    public sealed class EnemyRangedAttack : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField] private float engageRange = 32f;
        [Tooltip("Seconds the laser paints before the bolt is launched.")]
        [SerializeField] private float windUp = 0.7f;
        [SerializeField] private float cooldown = 2.8f;

        [Header("Aim")]
        [SerializeField] private float eyeHeight = 1.4f;
        [SerializeField] private float muzzleForward = 0.55f;
        [SerializeField] private float aimHeight = 1.1f;

        [Header("Bolt")]
        [SerializeField] private float projectileSpeed = 22f;
        [SerializeField] private float damage = 9f;
        [SerializeField] private float hitRadius = 0.55f;

        [Header("Wiring (scene builder)")]
        public Material projectileMaterial;
        public Material telegraphMaterial;
        public Material sparkMaterial;

        private static readonly Color ChargeStart = new Color(1f, 0.78f, 0.25f);
        private static readonly Color ChargeEnd = new Color(1f, 0.18f, 0.08f);

        private PlayerHealth target;
        private EnemyHealth health;
        private EnemyAnimator animator;
        private LineRenderer telegraph;
        private float nextShotAt;
        private float chargeStartedAt;
        private float windUpEndsAt = -1f;
        private bool charging;
        private bool active;
        private Vector3 aimPoint;

        /// <summary>True while the warning laser is live.</summary>
        public bool IsCharging => charging;

        /// <summary>True when the spawner has initialised this instance as a marksman.</summary>
        public bool IsActive => active;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            animator = GetComponent<EnemyAnimator>();
            BuildTelegraph();
            active = false;
        }

        /// <summary>Turns the marksman on for this instance and scales its shot damage.</summary>
        public void Initialize(PlayerHealth player, float damageBonus)
        {
            target = player;
            damage += Mathf.Max(0f, damageBonus);
            nextShotAt = Time.time + Random.Range(1.0f, 2.0f);
            active = true;
        }

        private void Update()
        {
            if (!active)
            {
                return;
            }

            if (health != null && health.IsDead)
            {
                CancelCharge();
                return;
            }

            if (target == null || target.IsDead ||
                (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            {
                CancelCharge();
                return;
            }

            animator?.SetAiming(charging);

            if (charging)
            {
                UpdateCharge();
                return;
            }

            if (Time.time >= nextShotAt &&
                Vector3.Distance(transform.position, target.transform.position) <= engageRange &&
                HasLineOfSight())
            {
                BeginCharge();
            }
        }

        private void BeginCharge()
        {
            charging = true;
            chargeStartedAt = Time.time;
            windUpEndsAt = Time.time + windUp;
            aimPoint = TargetPoint();

            if (telegraph != null)
            {
                telegraph.enabled = true;
                UpdateTelegraph();
            }

            AudioManager.Instance?.PlayEnemyCharge(MuzzlePosition());
        }

        private void UpdateCharge()
        {
            aimPoint = TargetPoint();
            UpdateTelegraph();

            // The player stepped behind cover: drop the shot instead of firing
            // through the wall.
            if (!HasLineOfSight())
            {
                CancelCharge();
                nextShotAt = Time.time + 0.4f;
                return;
            }

            if (Time.time >= windUpEndsAt)
            {
                Fire();
            }
        }

        private void Fire()
        {
            Vector3 muzzle = MuzzlePosition();
            Vector3 direction = (aimPoint - muzzle).normalized;

            EnemyProjectile.Spawn(muzzle, direction, projectileSpeed, damage, hitRadius,
                target, projectileMaterial, sparkMaterial);

            AudioManager.Instance?.PlayEnemyShot(muzzle);

            CancelCharge();
            nextShotAt = Time.time + cooldown;
        }

        private void CancelCharge()
        {
            charging = false;
            windUpEndsAt = -1f;
            animator?.SetAiming(false);

            if (telegraph != null)
            {
                telegraph.enabled = false;
            }
        }

        private void UpdateTelegraph()
        {
            if (telegraph == null)
            {
                return;
            }

            float progress = windUp > 0f
                ? Mathf.Clamp01((Time.time - chargeStartedAt) / windUp)
                : 1f;

            Color color = Color.Lerp(ChargeStart, ChargeEnd, progress);
            telegraph.startColor = color;
            telegraph.endColor = color;
            telegraph.SetPosition(0, MuzzlePosition());
            telegraph.SetPosition(1, aimPoint);
        }

        private bool HasLineOfSight()
        {
            Vector3 origin = MuzzlePosition();
            Vector3 destination = TargetPoint();
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;

            if (distance < 0.01f)
            {
                return true;
            }

            // Layer 2 (Ignore Raycast) holds the player, so this only tests world
            // geometry and other enemies.
            return !Physics.Raycast(origin, delta / distance, distance - 0.15f,
                ~(1 << 2), QueryTriggerInteraction.Ignore);
        }

        private Vector3 MuzzlePosition()
        {
            float scale = transform.localScale.y;
            return transform.position + Vector3.up * (eyeHeight * scale) + transform.forward * (muzzleForward * scale);
        }

        private Vector3 TargetPoint() => target.transform.position + Vector3.up * aimHeight;

        private void BuildTelegraph()
        {
            var holder = new GameObject("Ranged Telegraph");
            holder.transform.SetParent(transform, false);

            telegraph = holder.AddComponent<LineRenderer>();
            telegraph.useWorldSpace = true;
            telegraph.positionCount = 2;
            telegraph.widthMultiplier = 0.035f;
            telegraph.numCapVertices = 2;
            telegraph.alignment = LineAlignment.View;
            telegraph.textureMode = LineTextureMode.Stretch;
            telegraph.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            telegraph.receiveShadows = false;
            telegraph.sharedMaterial = telegraphMaterial;
            telegraph.startColor = ChargeStart;
            telegraph.endColor = ChargeStart;
            telegraph.enabled = false;
        }
    }
}
