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
        [SerializeField] private float attackWindup = 0.1f;
        [Tooltip("Degrees the model leans back during the wind-up.")]
        [SerializeField] private float meleeLeanAngle = 14f;

        [Header("Crowd control")]
        [SerializeField] private float neighborRadius = 1.1f;
        [SerializeField] private float separationStrength = 2.5f;

        [Header("Impact response")]
        [Tooltip("Seconds a hit slows movement and knocks the enemy back.")]
        [SerializeField] private float flinchDuration = 0.09f;
        [Tooltip("Metres pushed away from the hit point per accepted shot.")]
        [SerializeField] private float flinchKnockback = 0.09f;

        [Header("Ranged behaviour")]
        [Tooltip("Set by the spawner for ranged enemies. Zero keeps melee behaviour.")]
        [SerializeField] private float rangedHoldDistance;
        [SerializeField] private float rangedStrafeSpeed = 2.6f;

        [Header("Flight")]
        [SerializeField] private float hoverHeight;
        private float hoverPhase;

        private const float LungeDistance = 0.4f;
        private const float StrikeRadius = 0.28f;

        private static readonly Collider[] StrikeContacts = new Collider[16];
        private static readonly Collider[] Neighbors = new Collider[128];

        private CharacterController controller;
        private EnemyHealth health;
        private EnemyAnimator animator;
        private EnemyExploder exploder;
        private PlayerHealth target;
        private Transform model;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private Vector3 pendingKnockback;
        private float nextAttackTime;
        private float attackStrikeTime = -1f;
        private float leanAngle;
        private float recoveryEndsAt;
        private float lungeProgress;
        private int strafeDirection = 1;
        private float strafeSwitchAt;
        private Vector3 lastPosition;
        private float flinchTimer;
        private float stuckTimer;
        private float biasTimer;
        private float biasDirection = 1f;

        /// <summary>True while a melee swipe is winding up, exposed for tests.</summary>
        public bool IsAttacking => attackStrikeTime >= 0f;

        /// <summary>True while an accepted hit is slowing this enemy, exposed for tests.</summary>
        public bool IsFlinching => flinchTimer > 0f;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<EnemyHealth>();
            animator = GetComponent<EnemyAnimator>();
            exploder = GetComponent<EnemyExploder>();
            model = transform.Find("Model");
            lastPosition = transform.position;

            if (health != null)
            {
                health.Damaged += HandleDamaged;
            }
        }

        /// <summary>Called by the wave spawner to set the target and per-wave scaling.</summary>
        public void Initialize(PlayerHealth player, float speedBonus, float damageBonus)
        {
            target = player;
            moveSpeed += speedBonus;
            attackDamage += damageBonus;
            nextAttackTime = Time.time + 0.6f;
            attackStrikeTime = -1f;
            recoveryEndsAt = 0f;
            lastPosition = transform.position;
            if (hoverHeight > 0f)
            {
                hoverPhase = Random.Range(0f, 2f * Mathf.PI);
            }
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

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
            }
        }

        private void HandleDamaged(float damage, Vector3 hitPoint)
        {
            // A gunshot should register in the enemy's body, not just on the HP bar.
            flinchTimer = Mathf.Max(flinchTimer, flinchDuration);
            animator?.TriggerHit();

            if (controller == null)
            {
                return;
            }

            Vector3 away = transform.position - hitPoint;
            away.y = 0f;

            if (away.sqrMagnitude > 0.0001f)
            {
                // Route the knockback through the normal movement integration so
                // gravity and grounding are not bypassed by a raw controller.Move.
                pendingKnockback += away.normalized * flinchKnockback;
            }
        }

        private void Update()
        {
            flinchTimer = Mathf.Max(0f, flinchTimer - Time.deltaTime);

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

            bool committed = IsAttacking || Time.time < recoveryEndsAt;
            if (!committed)
            {
                Face(direction, deltaTime);
            }

            if (rangedHoldDistance > 0.01f)
            {
                UpdateRangedMovement(direction, distance, deltaTime);
            }
            else if (committed)
            {
                planarVelocity = Vector3.zero;
            }
            else if (distance <= MeleeStartRange && (exploder == null || !exploder.IsActive))
            {
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);
                if (Vector3.Dot(transform.forward, direction) >= 0.98f)
                {
                    BeginAttack();
                }
            }
            else
            {
                Steer(direction, deltaTime);
            }

            UpdateLean(deltaTime);
            Move(deltaTime);
            UpdateMeleeSwing();
            UpdateStuckDetection(deltaTime);
            animator?.SetLocomotion(planarVelocity.magnitude, moveSpeed);
        }

        private void Steer(Vector3 direction, float deltaTime)
        {
            // When an enemy has been pushing against cover, sidestep for a moment.
            if (biasTimer > 0f)
            {
                biasTimer -= deltaTime;
                direction = Quaternion.Euler(0f, biasDirection * 55f, 0f) * direction;
            }

            Vector3 desired = direction * moveSpeed * ImpactSpeedScale() + ComputeSeparation();
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, acceleration * deltaTime);
        }

        /// <summary>How much of the normal speed is kept mid-flinch.</summary>
        private float ImpactSpeedScale() => flinchTimer > 0f ? 0.45f : 1f;

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
            Vector3 desired = tangent * rangedStrafeSpeed * ImpactSpeedScale() + ComputeSeparation();
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

        private float MeleeStartRange => Mathf.Max(StrikeRadius, attackRange - StrikeRadius);

        private void BeginAttack()
        {
            if (Time.time < nextAttackTime)
            {
                return;
            }

            float windup = Mathf.Max(EnemyAnimator.MeleeSwingDuration, attackWindup);
            nextAttackTime = Time.time + attackCooldown;
            attackStrikeTime = Time.time + windup;
            recoveryEndsAt = attackStrikeTime + EnemyAnimator.MeleeRecovery(windup);
            lungeProgress = 0f;
            planarVelocity = Vector3.zero;
            animator?.TriggerAttack(windup);
            AudioManager.Instance?.PlayEnemyAttack(transform.position);
        }

        private void UpdateMeleeSwing()
        {
            if (!IsAttacking)
            {
                return;
            }

            // Advance the jab even on a miss. The controller stops it at solid obstacles.
            float progress = Mathf.Clamp01(1f - (attackStrikeTime - Time.time) / EnemyAnimator.MeleeSwingDuration);
            if (progress > lungeProgress)
            {
                controller.Move(transform.forward * (LungeDistance * (progress - lungeProgress)));
                lungeProgress = progress;
            }

            if (Time.time < attackStrikeTime)
            {
                return;
            }

            attackStrikeTime = -1f;
            TryMeleeHit();
        }

        private void TryMeleeHit()
        {
            // The swing occupies a narrow volume in front of the enemy at chest height.
            Vector3 origin = transform.TransformPoint(controller.center + Vector3.up * 0.25f);
            float forwardReach = Mathf.Max(0f, MeleeStartRange - LungeDistance - StrikeRadius);
            Vector3 reach = origin + transform.forward * forwardReach;
            int count = Physics.OverlapCapsuleNonAlloc(origin, reach, StrikeRadius,
                StrikeContacts, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider contact = StrikeContacts[i];
                if (contact.GetComponentInParent<PlayerHealth>() != target)
                {
                    continue;
                }

                Vector3 contactPoint = contact.bounds.center;
                contactPoint.y = Mathf.Clamp(origin.y, contact.bounds.min.y, contact.bounds.max.y);
                if (Physics.Linecast(origin, contactPoint, out RaycastHit obstruction, ~0,
                    QueryTriggerInteraction.Ignore) &&
                    obstruction.collider.GetComponentInParent<PlayerHealth>() != target)
                {
                    continue;
                }

                target.TakeDamage(attackDamage);
                break;
            }
        }

        private void UpdateLean(float deltaTime)
        {
            float targetLean = IsAttacking
                ? Mathf.Lerp(-meleeLeanAngle, meleeLeanAngle, lungeProgress)
                : Time.time < recoveryEndsAt ? meleeLeanAngle * 0.4f : 0f;

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
            if (hoverHeight > 0f && target != null && !target.IsDead)
            {
                float altitude = Mathf.Clamp(target.transform.position.y + 3.4f, hoverHeight, hoverHeight + 1f);
                altitude += Mathf.Sin(Time.time * 2.2f + hoverPhase) * 0.32f;
                float climbSpeed = Mathf.Clamp((altitude - transform.position.y) * 3f, -3f, 3f);
                verticalVelocity = Mathf.MoveTowards(verticalVelocity, climbSpeed, acceleration * deltaTime);
            }
            else
            {
                if (controller.isGrounded && verticalVelocity < 0f)
                {
                    verticalVelocity = -2f;
                }

                verticalVelocity += gravity * deltaTime;
            }

            Vector3 motion = planarVelocity * (health != null ? health.MovementStatusMultiplier : 1f)
                + Vector3.up * verticalVelocity;

            if (pendingKnockback.sqrMagnitude > 0.0001f)
            {
                // Convert the displacement into a velocity for this frame so the
                // CharacterController still applies gravity and grounding.
                motion += pendingKnockback / Mathf.Max(deltaTime, 0.0001f);
                pendingKnockback = Vector3.zero;
            }

            controller.Move(motion * deltaTime);
        }

        private void ApplyGravityOnly()
        {
            planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * Time.deltaTime);
            Move(Time.deltaTime);
        }

        private void UpdateStuckDetection(float deltaTime)
        {
            Vector3 movement = transform.position - lastPosition;
            movement.y = 0f;
            float moved = movement.magnitude;
            float expected = planarVelocity.magnitude *
                (health != null ? health.MovementStatusMultiplier : 1f) * deltaTime;
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
