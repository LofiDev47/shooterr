using Shooter.Effects;
using Shooter.Managers;
using Shooter.Player;
using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// The Warden, the five-wave boss. It is a real fight rather than a scaled-up
    /// grunt: three health phases, two telegraphed attacks, summoned adds and a
    /// weak-point window.
    /// </summary>
    /// <remarks>
    /// <para><b>Slam</b> — a wide ground AoE. The authored clip coils for ~0.85 s
    /// before impact, a ring marks the radius the whole time, and the shockwave
    /// knocks the player back instead of only damaging them.</para>
    /// <para><b>Volley</b> — the siege cannon fires a fan of five bolts. It covers
    /// the range the slam cannot reach, so backing off is not a solution.</para>
    /// <para><b>Phases</b> — at 66% and 33% it roars, ignores damage, calls in
    /// adds and comes back faster and harder.</para>
    /// <para><b>Exposed core</b> — a slam leaves the furnace core open for a short
    /// window; hits there deal double damage. That is the reward for baiting a
    /// slam and surviving it.</para>
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class BossController : MonoBehaviour
    {
        private const string DisplayName = "THE WARDEN";

        /// <summary>Health fraction below which the boss enters its second phase.</summary>
        public const float PhaseTwoThreshold = 0.66f;

        /// <summary>Health fraction below which the boss enters its third phase.</summary>
        public const float PhaseThreeThreshold = 0.33f;

        /// <summary>Longest a boss corpse is held on screen after death.</summary>
        public const float DeathHoldSeconds = 4.2f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 2.5f;
        [SerializeField] private float acceleration = 5f;
        [SerializeField] private float turnSpeed = 150f;
        [SerializeField] private float gravity = -22f;
        [Tooltip("The boss keeps walking until this close, then commits to an attack.")]
        [SerializeField] private float engageRange = 3.2f;

        [Header("Slam")]
        [SerializeField] private float slamRange = 9.5f;
        [SerializeField] private float slamRadius = 7f;
        [SerializeField] private float slamDamage = 24f;
        [SerializeField] private float slamKnockback = 3.4f;
        [Tooltip("Seconds into the slam clip when the shockwave lands.")]
        [SerializeField] private float slamImpactDelay = 0.86f;
        [Tooltip("Seconds from impact until the boss can move again.")]
        [SerializeField] private float slamRecover = 1.1f;
        [SerializeField] private float slamCooldown = 5.2f;
        [SerializeField] private float coreExposedSeconds = 2.8f;
        [SerializeField] private float coreDamageMultiplier = 2f;

        [Header("Volley")]
        [SerializeField] private float volleyRange = 36f;
        [SerializeField] private float volleyDamage = 10f;
        [SerializeField] private float volleyCooldown = 4.4f;
        [SerializeField] private int volleyBolts = 5;
        [Tooltip("Degrees between adjacent bolts in the fan.")]
        [SerializeField] private float volleySpread = 8f;
        [SerializeField] private float volleySpeed = 20f;
        [Tooltip("Seconds into the volley clip when the first bolt leaves.")]
        [SerializeField] private float volleyWindup = 0.62f;

        [Header("Obstacles")]
        [Tooltip("How far ahead the boss looks for cover, crates and pillars.")]
        [SerializeField] private float avoidanceProbe = 3.4f;
        [Tooltip("Probe radius; matches the CharacterController so it fits through what it clears.")]
        [SerializeField] private float avoidanceRadius = 0.95f;

        [Header("Phases")]
        [SerializeField] private float roarInvulnerability = 1.8f;
        [SerializeField] private float phaseSpeedGain = 0.5f;
        [SerializeField] private float phaseDamageGain = 5f;
        [SerializeField] private int phaseTwoSummons = 2;
        [SerializeField] private int phaseThreeSummons = 3;

        [Header("Wiring (scene builder)")]
        public Material ringMaterial;
        public Material boltMaterial;
        public Material sparkMaterial;
        [Tooltip("Adds called in on phase changes; the spawner counts them as wave enemies.")]
        public GameObject[] summonPrefabs;

        private enum BossState
        {
            Idle,
            SlamWindup,
            SlamRecover,
            Volley,
            Roar,
            Dying,
        }

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color CoreHot = new Color(1f, 0.52f, 0.12f);

        /// <summary>Everything except the Ignore Raycast layer, which holds both rigs.</summary>
        private static readonly int AvoidMask = ~(1 << 2);

        /// <summary>The live boss, so the HUD can find it without extra wiring.</summary>
        public static BossController Current { get; private set; }

        private CharacterController controller;
        private EnemyHealth health;
        private BossAnimator animator;
        private PlayerHealth target;
        private PlayerController targetController;
        private WaveSpawner spawner;
        private Renderer coreRenderer;
        private MaterialPropertyBlock coreBlock;

        private Vector3 planarVelocity;
        private float verticalVelocity;
        private BossState state = BossState.Idle;
        private float nextSlamAt;
        private float nextVolleyAt;
        private float stateUntil;
        private float boltsAt;
        private int boltsLeft;
        private float coreExposedUntil = -1f;
        private int waveIndex;
        private bool dying;

        // Obstacle steering state: which way the boss is currently going around
        // something (-1 | +1, 0 when clear) and how long it stays committed.
        private float avoidanceSide;
        private float avoidanceUntil;

        /// <summary>Current phase, 1 to 3.</summary>
        public int Phase { get; private set; } = 1;

        /// <summary>True while the core takes double damage.</summary>
        public bool CoreExposed => Time.time < coreExposedUntil;

        /// <summary>True while a phase roar makes the boss immune.</summary>
        public bool Invulnerable => state == BossState.Roar;

        /// <summary>True once the boss is dead; the HUD hides its bar.</summary>
        public bool IsDefeated => health != null && health.IsDead;

        /// <summary>Health fraction in 0..1 for the boss bar.</summary>
        public float HealthRatio => health != null && health.MaxHealth > 0.01f
            ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth)
            : 0f;

        /// <summary>Bar label: the phase, or the weak-point window when it is open.</summary>
        public string PhaseLabel => CoreExposed ? "CORE EXPOSED" : Phase switch
        {
            2 => "PHASE II",
            3 => "PHASE III",
            _ => "PHASE I",
        };

        /// <summary>Display name shown above the boss bar.</summary>
        public string BossName => DisplayName;

        /// <summary>
        /// Which phase a health fraction belongs to. Pure, so the thresholds can be
        /// tested without spawning anything.
        /// </summary>
        public static int PhaseFor(float healthRatio)
        {
            if (healthRatio > PhaseTwoThreshold)
            {
                return 1;
            }

            return healthRatio > PhaseThreeThreshold ? 2 : 3;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<EnemyHealth>();
            animator = GetComponent<BossAnimator>();
            Current = this;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name == "CoreGlow")
                {
                    coreRenderer = renderer;
                    break;
                }
            }

            coreBlock ??= new MaterialPropertyBlock();
            if (health != null)
            {
                health.SetDeathHold(DeathHoldSeconds);
            }
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>Called by the wave spawner once health has been initialised.</summary>
        public void Initialize(PlayerHealth player, WaveSpawner owner, int bossWaveIndex,
            float damageBonus, float speedBonus)
        {
            target = player;
            targetController = player != null ? player.GetComponent<PlayerController>() : null;
            spawner = owner;
            waveIndex = Mathf.Max(0, bossWaveIndex);

            moveSpeed += speedBonus;
            slamDamage += damageBonus;
            volleyDamage += damageBonus * 0.5f;

            Phase = 1;
            dying = false;
            coreExposedUntil = -1f;
            stateUntil = 0f;
            nextSlamAt = Time.time + 2.6f;
            nextVolleyAt = Time.time + 4.2f;
            Current = this;

            if (health != null)
            {
                health.IncomingDamageScale = 1f;
                health.SetDeathHold(DeathHoldSeconds);
            }
        }

        private void Update()
        {
            if (health == null)
            {
                return;
            }

            if (health.IsDead)
            {
                EnterDying();
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

            Face(direction, deltaTime, state == BossState.SlamWindup ? 0.3f : 1f);
            CheckPhaseChange();

            switch (state)
            {
                case BossState.Idle:
                    UpdateIdle(direction, distance, deltaTime);
                    break;

                case BossState.SlamWindup:
                    planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);

                    if (Time.time >= stateUntil)
                    {
                        ImpactGround();
                        state = BossState.SlamRecover;
                        stateUntil = Time.time + slamRecover;
                    }

                    break;

                case BossState.SlamRecover:
                    planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);

                    if (Time.time >= stateUntil)
                    {
                        state = BossState.Idle;
                    }

                    break;

                case BossState.Volley:
                    planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);
                    UpdateVolley();
                    break;

                case BossState.Roar:
                    planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);

                    if (Time.time >= stateUntil)
                    {
                        FinishRoar();
                    }

                    break;
            }

            UpdateCoreVisual();
            Move(deltaTime);
            animator?.SetLocomotion(planarVelocity.magnitude, moveSpeed, controller.isGrounded);
        }

        // ------------------------------------------------------------- movement

        private void UpdateIdle(Vector3 direction, float distance, float deltaTime)
        {
            if (distance <= slamRange && Time.time >= nextSlamAt)
            {
                BeginSlam();
                return;
            }

            if (distance > slamRange * 0.85f && distance <= volleyRange && Time.time >= nextVolleyAt)
            {
                BeginVolley();
                return;
            }

            if (distance > engageRange)
            {
                Vector3 desired = SteerAround(direction, distance) * moveSpeed;
                planarVelocity = Vector3.MoveTowards(planarVelocity, desired, acceleration * deltaTime);
            }
            else
            {
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * deltaTime);
            }
        }

        /// <summary>
        /// Steers the boss around cover instead of into it. The Warden is far too
        /// big to squeeze past a container the way a grunt does: without this it
        /// walks into the first crate between it and the player and grinds there
        /// forever, which reads as a broken boss rather than a fight.
        ///
        /// A capsule cast looks straight ahead; when it is blocked, the boss slides
        /// along the obstacle toward whichever side has more room. The chosen side
        /// is held for a moment so it cannot dither in a corner.
        /// </summary>
        private Vector3 SteerAround(Vector3 direction, float distance)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);

            if (flat.sqrMagnitude < 0.0001f)
            {
                return direction;
            }

            flat.Normalize();

            // Never probe past the player: they are the destination, not an obstacle.
            float probe = Mathf.Min(avoidanceProbe, distance - engageRange - 0.6f);

            if (probe < 0.8f)
            {
                avoidanceSide = 0f;
                return flat;
            }

            Vector3 origin = transform.position + Vector3.up * 1.6f;
            bool blocked = Physics.SphereCast(origin, avoidanceRadius, flat, out _, probe,
                AvoidMask, QueryTriggerInteraction.Ignore);

            if (!blocked)
            {
                if (Time.time >= avoidanceUntil)
                {
                    avoidanceSide = 0f;
                }

                return flat;
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, flat);

            if (avoidanceSide == 0f || Time.time >= avoidanceUntil)
            {
                // Prefer the side with a clear exit; when both are blocked, keep
                // circling one way rather than swapping and standing still.
                bool leftClear = !Physics.SphereCast(origin, avoidanceRadius,
                    (flat - tangent).normalized, out _, probe, AvoidMask, QueryTriggerInteraction.Ignore);
                bool rightClear = !Physics.SphereCast(origin, avoidanceRadius,
                    (flat + tangent).normalized, out _, probe, AvoidMask, QueryTriggerInteraction.Ignore);

                avoidanceSide = leftClear || !rightClear ? -1f : 1f;
                avoidanceUntil = Time.time + 0.9f;
            }

            return (flat + tangent * (avoidanceSide * 1.35f)).normalized;
        }

        private void Face(Vector3 direction, float deltaTime, float scale)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion wanted = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * scale * deltaTime);
        }

        private void Move(float deltaTime)
        {
            if (controller == null || !controller.enabled)
            {
                return;
            }

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            verticalVelocity += gravity * deltaTime;
            controller.Move((planarVelocity + Vector3.up * verticalVelocity) * deltaTime);
        }

        private void ApplyGravityOnly()
        {
            planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, acceleration * Time.deltaTime);
            verticalVelocity += gravity * Time.deltaTime;
            Move(Time.deltaTime);
        }

        // -------------------------------------------------------------- attacks

        private void BeginSlam()
        {
            state = BossState.SlamWindup;
            stateUntil = Time.time + slamImpactDelay;
            nextSlamAt = Time.time + slamCooldown;
            animator?.PlaySlam();
            AudioManager.Instance?.PlayEnemyAttack(transform.position);

            // The ring is the mechanical tell; the clip's own anticipation sells it.
            BossRing.Spawn(transform.position, slamRadius, slamRadius, slamImpactDelay, 0.09f,
                new Color(1f, 0.42f, 0.10f), ringMaterial, taperIn: true);
        }

        private void ImpactGround()
        {
            BossRing.Spawn(transform.position, slamRadius * 0.35f, slamRadius * 1.08f, 0.42f, 0.22f,
                new Color(1f, 0.62f, 0.18f), ringMaterial);
            DebrisBurst.Spawn(transform.position + Vector3.up * 0.4f, Vector3.up, sparkMaterial,
                16, 6.5f, 0.13f, 1.1f);
            AudioManager.Instance?.PlayExplosion(transform.position);

            if (target != null && !target.IsDead)
            {
                Vector3 toPlayer = target.transform.position - transform.position;
                toPlayer.y = 0f;
                float distance = toPlayer.magnitude;

                if (distance <= slamRadius)
                {
                    target.TakeDamage(slamDamage);

                    if (distance > 0.01f)
                    {
                        targetController?.ApplyKnockback(toPlayer.normalized * slamKnockback);
                    }

                    targetController?.AddShake(0.75f);
                }
            }

            // The slam overextends the boss: the core is open to counter-fire.
            coreExposedUntil = Time.time + coreExposedSeconds;
            if (health != null)
            {
                health.IncomingDamageScale = coreDamageMultiplier;
            }
        }

        private void BeginVolley()
        {
            state = BossState.Volley;
            boltsLeft = volleyBolts;
            boltsAt = Time.time + volleyWindup;
            stateUntil = boltsAt + 0.05f * volleyBolts + 0.7f;
            nextVolleyAt = Time.time + volleyCooldown;
            animator?.PlayVolley();
            AudioManager.Instance?.PlayEnemyCharge(transform.position);
        }

        private void UpdateVolley()
        {
            if (boltsLeft > 0 && Time.time >= boltsAt)
            {
                FireBolt();
                boltsLeft--;
                boltsAt = Time.time + 0.055f;
            }

            if (Time.time >= stateUntil)
            {
                state = BossState.Idle;
            }
        }

        private void FireBolt()
        {
            if (target == null || target.IsDead)
            {
                return;
            }

            Vector3 origin = transform.position + Vector3.up * 2.55f + transform.forward * 1.35f +
                transform.right * 0.95f;
            Vector3 aim = target.transform.position + Vector3.up * 1.15f;
            Vector3 direction = (aim - origin).normalized;

            // Fan the bolts around the aim line so the player must move, not stand.
            int fired = volleyBolts - boltsLeft;
            float offset = fired - (volleyBolts - 1) * 0.5f;
            direction = Quaternion.Euler(0f, offset * volleySpread, 0f) * direction;

            EnemyProjectile.Spawn(origin, direction, volleySpeed, volleyDamage, 0.9f, target,
                boltMaterial, sparkMaterial);
            AudioManager.Instance?.PlayEnemyShot(origin);
        }

        // --------------------------------------------------------------- phases

        private void CheckPhaseChange()
        {
            if (state == BossState.Roar || state == BossState.Dying)
            {
                return;
            }

            int wanted = PhaseFor(HealthRatio);

            if (wanted > Phase)
            {
                BeginPhase(wanted);
            }
        }

        private void BeginPhase(int phase)
        {
            Phase = phase;
            state = BossState.Roar;
            stateUntil = Time.time + roarInvulnerability;

            if (health != null)
            {
                health.IncomingDamageScale = 0f;
            }

            animator?.PlayRoar();
            BossRing.Spawn(transform.position, 1.5f, slamRadius * 1.3f, 0.7f, 0.18f,
                new Color(1f, 0.25f, 0.08f), ringMaterial);
            AudioManager.Instance?.PlayEnemyAttack(transform.position);
            spawner?.hud?.ShowToast($"THE WARDEN · PHASE {(phase == 2 ? "II" : "III")}");
        }

        private void FinishRoar()
        {
            state = BossState.Idle;
            coreExposedUntil = Time.time;
            moveSpeed += phaseSpeedGain;
            slamDamage += phaseDamageGain;
            volleyDamage += phaseDamageGain * 0.5f;
            nextSlamAt = Time.time + 1.1f;
            nextVolleyAt = Time.time + 1.6f;

            if (health != null)
            {
                health.IncomingDamageScale = 1f;
            }

            SummonAdds(Phase >= 3 ? phaseThreeSummons : phaseTwoSummons);
        }

        private void SummonAdds(int count)
        {
            if (summonPrefabs == null || summonPrefabs.Length == 0 || count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                GameObject prefab = summonPrefabs[i % summonPrefabs.Length];

                if (prefab == null)
                {
                    continue;
                }

                float angle = Mathf.PI * 2f * i / count + Random.Range(-0.25f, 0.25f);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3.4f;
                Vector3 position = transform.position + offset;
                position.y = transform.position.y;

                GameObject instance = Instantiate(prefab, position, Quaternion.identity);
                EnemyHealth addHealth = instance.GetComponent<EnemyHealth>();
                EnemyAI addAI = instance.GetComponent<EnemyAI>();

                if (addHealth != null)
                {
                    // The adds scale with the boss wave so late fights stay honest.
                    addHealth.Initialize(1f + waveIndex * 0.18f, 1f, null, false, false);
                    addHealth.SetScoreValue(10);
                }

                addAI?.Initialize(target, 0f, 0f);
                spawner?.RegisterSummon(addHealth);
                DebrisBurst.Spawn(position, Vector3.up, sparkMaterial, 6, 3f, 0.08f, 0.6f);
            }
        }

        // ---------------------------------------------------------------- death

        private void EnterDying()
        {
            if (dying)
            {
                return;
            }

            dying = true;
            state = BossState.Dying;

            // The corpse must not keep blocking shots or the player's movement.
            if (controller != null)
            {
                controller.enabled = false;
            }

            animator?.PlayDeath();

            if (health != null)
            {
                // Death hold was set at spawn; the object removes itself once the
                // authored death clip has finished.
                health.SetDeathHold(DeathHoldSeconds);
            }
        }

        // --------------------------------------------------------------- visuals

        private void UpdateCoreVisual()
        {
            if (coreRenderer == null)
            {
                return;
            }

            coreBlock ??= new MaterialPropertyBlock();

            float strength;

            if (CoreExposed)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 11f);
                strength = Mathf.Lerp(5f, 13f, pulse);
            }
            else
            {
                // Phase three burns brighter: the tell that it is nearly over.
                strength = Phase >= 3 ? 3.4f : 2.1f;
            }

            coreBlock.SetColor(EmissionColorId, CoreHot * strength);
            coreRenderer.SetPropertyBlock(coreBlock);
        }

        /// <summary>Health scale the boss will apply to the next hit, exposed for tests.</summary>
        public float CurrentDamageScale => health != null ? health.IncomingDamageScale : 1f;
    }
}
