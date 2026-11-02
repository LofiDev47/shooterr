using Shooter.Managers;
using Shooter.Player;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Simple steering AI: walk straight at the player, avoid crowding other enemies,
    /// and swipe when close enough. Uses a <see cref="CharacterController"/> for collision,
    /// so no baked NavMesh is required and the arena can be rebuilt entirely from code.
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

        [Header("Crowd control")]
        [SerializeField] private float neighborRadius = 1.1f;
        [SerializeField] private float separationStrength = 2.5f;

        private static readonly Collider[] Neighbors = new Collider[32];

        private CharacterController controller;
        private EnemyHealth health;
        private PlayerHealth target;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float nextAttackTime;
        private Vector3 lastPosition;
        private float stuckTimer;
        private float biasTimer;
        private float biasDirection = 1f;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<EnemyHealth>();
            lastPosition = transform.position;
        }

        /// <summary>Called by the wave spawner to set the target and per-wave scaling.</summary>
        public void Initialize(PlayerHealth player, float speedBonus, float damageBonus)
        {
            target = player;
            moveSpeed += speedBonus;
            attackDamage += damageBonus;
            nextAttackTime = Time.time + 0.6f;
            lastPosition = transform.position;
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

            if (distance <= attackRange)
            {
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);
                TryAttack();
            }
            else
            {
                Steer(direction, deltaTime);
            }

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

        private void TryAttack()
        {
            if (Time.time < nextAttackTime)
            {
                return;
            }

            nextAttackTime = Time.time + attackCooldown;
            target.TakeDamage(attackDamage);

            // Small forward jab so the hit reads visually.
            controller.Move(transform.forward * 0.12f);
            AudioManager.Instance?.PlayEnemyAttack(transform.position);
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
