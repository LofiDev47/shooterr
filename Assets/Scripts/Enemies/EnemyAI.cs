using Shooter.Managers;
using Shooter.Player;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Steering AI. Melee troopers close in and swipe only after a visible wind-up,
    /// so backing out is a real dodge. Marksmen switch into ranged mode: they hold
    /// a firing lane and orbit while <see cref="EnemyRangedAttack"/> does the shooting.
    /// Uses a <see cref="CharacterController"/> so no baked NavMesh is required.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyAI : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3.6f;
        [SerializeField] private float acceleration = 14f;
        [SerializeField] private float turnSpeed = 720f;
        [SerializeField] private float gravity = -22f;

        [Header("Combat")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float attackCooldown = 1.2f;
        [SerializeField] private float attackDamage = 9f;

        [Header("Melee telegraph")]
        [Tooltip("Seconds between the swipe starting and the damage landing.")]
        [SerializeField] private float attackWindup = 0.34f;
        [Tooltip("Degrees the model leans back during the wind-up.")]
        [SerializeField] private float meleeLeanAngle = 14f;

        [Header("Crowd control")]
        [SerializeField] private float neighborRadius = 1.1f;
        [SerializeField] private float separationStrength = 2.5f;

        [Header("Ranged behaviour")]
        [Tooltip("Set by the spawner for marksmen. Zero keeps melee behaviour.")]
        [SerializeField] private float rangedHoldDistance;
        [SerializeField] private float rangedStrafeSpeed = 2.6f;

        private static readonly Collider[] Neighbors = new Collider[32];

        private CharacterController controller;
        private EnemyHealth health;
        private PlayerHealth target;
        private Transform model;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float nextAttackTime;
        private float attackStrikeTime = -1f;
        private float leanAngle;
        private float lungeTimer;
        private int strafeDirection = 1;
        private float strafeSwitchAt;
        private Vector3 lastPosition;
        private float stuckTimer;
        private float biasTimer;
        private float biasDirection = 1f;

        /// <summary>True while a melee swipe is winding up, exposed for tests.</summary>
        public bool IsAttacking => attackStrikeTime >= 0f;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<EnemyHealth>();
            model = transform.Find("Model");
            lastPosition = transform.position;
        }

        /// <summary>Called by the wave spawner to set the target and per-wave scaling.</summary>
        public void Initialize(PlayerHealth player, float speedBonus, float damageBonus)
        {
            target = player;
            moveSpeed += speedBonus;
            attackDamage += damageBonus;
            nextAttackTime = Time.time + 0.6f;
            attackStrikeTime = -1f;
            lastPosition = transform.position;
        }

        /// <summary>
        /// Switches this enemy into marksman behaviour: keep roughly
        /// <paramref name="holdDistance"/> metres away and strafe instead of meleeing.
        /// </summary>
        public void SetRanged(float holdDistance, float strafeSpeed = 2.6f)
        {
            rangedHoldDistance = Mathf.Max(0f, holdDistance);
            rangedStrafeSpeed = Mathf.Max(0f, strafeSpeed);
            strafeSwitchAt = 0f;
        }

        private void Update()
        {
            if (health != null && health.IsDead)
            {
                return;
            }

            if (target == null || target.IsDead)
            {
                ApplyGravityOnly();
                return;
            }

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 0.001f ? toTarget / distance : transform.forward;

            Face(direction, deltaTime);

            if (rangedHoldDistance > 0.01f)
            {
                UpdateRangedMovement(direction, distance, deltaTime);
            }
            else if (attackStrikeTime >= 0f)
            {
                // Mid-swing: plant the feet. The pause is the dodge window.
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);
            }
            else if (distance <= attackRange)
            {
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);
                BeginAttack();
            }
            else
            {
                Steer(direction, deltaTime);
            }

            UpdateMeleeSwing(deltaTime);
            Move(deltaTime);
            UpdateStuckDetection(deltaTime);
        }

        private void Steer(Vector3 direction, float deltaTime)
        {
            // When an enemy has been pushing against cover, sidestep for a moment.
            if (biasTimer > 0f)
            {
                biasTimer -= deltaTime;
                direction = Quaternion.Euler(0f, biasDirection * 55f, 0f) * direction;
            }

            Vector3 desired = direction * moveSpeed + ComputeSeparation();
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, acceleration * deltaTime);
        }

        /// <summary>
        /// Marksman movement: close the gap, back out of knife range, otherwise
        /// orbit so a ranged enemy is never a stationary turret.
        /// </summary>
        private void UpdateRangedMovement(Vector3 direction, float distance, float deltaTime)
        {
            if (distance > rangedHoldDistance * 1.15f)
            {
                Steer(direction, deltaTime);
                return;
            }

            if (distance < rangedHoldDistance * 0.7f)
            {
                Steer(-direction, deltaTime);
                return;
            }

            strafeSwitchAt -= deltaTime;

            if (strafeSwitchAt <= 0f)
            {
                strafeSwitchAt = Random.Range(1.1f, 2.2f);
                strafeDirection = Random.value < 0.5f ? -1 : 1;
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, direction) * strafeDirection;
            Vector3 desired = tangent * rangedStrafeSpeed + ComputeSeparation();
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, acceleration * deltaTime);
        }

        private Vector3 ComputeSeparation()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position + Vector3.up * 0.9f,
                neighborRadius,
                Neighbors,
                ~0,
                QueryTriggerInteraction.Ignore);

            Vector3 push = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                Collider other = Neighbors[i];

                if (other == null || other.transform == transform)
                {
                    continue;
                }

                if (other.GetComponentInParent<EnemyAI>() == null)
                {
                    continue;
                }

                Vector3 away = transform.position - other.transform.position;
                away.y = 0f;

                float distance = away.magnitude;

                if (distance < 0.01f)
                {
                    away = new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f);
                    distance = 0.01f;
                }

                push += away.normalized * (neighborRadius - distance);
            }

            return push * separationStrength;
        }

        /// <summary>
        /// Starts a telegraphed swipe. Damage is applied later in
        /// <see cref="UpdateMeleeSwing"/>, so stepping out of range is a dodge.
        /// </summary>
        private void BeginAttack()
        {
            if (Time.time < nextAttackTime)
            {
                return;
            }

            nextAttackTime = Time.time + attackCooldown;
            attackStrikeTime = Time.time + attackWindup;
            AudioManager.Instance?.PlayEnemyAttack(transform.position);
        }

        private void UpdateMeleeSwing(float deltaTime)
        {
            if (attackStrikeTime >= 0f && Time.time >= attackStrikeTime)
            {
                attackStrikeTime = -1f;
                lungeTimer = 0.16f;

                if (target != null && !target.IsDead)
                {
                    Vector3 toTarget = target.transform.position - transform.position;
                    toTarget.y = 0f;

                    if (toTarget.magnitude <= attackRange * 1.2f)
                    {
                        target.TakeDamage(attackDamage);

                        // Small forward jab so the hit reads visually.
                        controller.Move(transform.forward * 0.22f);
                    }
                }
            }

            UpdateLean(deltaTime);
        }

        private void UpdateLean(float deltaTime)
        {
            if (lungeTimer > 0f)
            {
                lungeTimer -= deltaTime;
            }

            float targetLean = lungeTimer > 0f
                ? meleeLeanAngle
                : attackStrikeTime >= 0f ? -meleeLeanAngle : 0f;

            leanAngle = Mathf.Lerp(leanAngle, targetLean, 1f - Mathf.Exp(-16f * deltaTime));

            if (model != null)
            {
                model.localRotation = Quaternion.Euler(leanAngle, 0f, 0f);
            }
        }

        private void Face(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion wanted = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * deltaTime);
        }

        private void Move(float deltaTime)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            verticalVelocity += gravity * deltaTime;

            Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * deltaTime);
        }

        private void ApplyGravityOnly()
        {
            planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * Time.deltaTime);
            Move(Time.deltaTime);
        }

        private void UpdateStuckDetection(float deltaTime)
        {
            float moved = (transform.position - lastPosition).magnitude;
            float expected = planarVelocity.magnitude * deltaTime;
            lastPosition = transform.position;

            if (expected > 0.001f && moved < expected * 0.35f)
            {
                stuckTimer += deltaTime;
            }
            else
            {
                stuckTimer = 0f;
            }

            if (stuckTimer > 0.4f)
            {
                stuckTimer = 0f;
                biasTimer = 1.2f;
                biasDirection = Random.value < 0.5f ? -1f : 1f;
            }
        }
    }
}
