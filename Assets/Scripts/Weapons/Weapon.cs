using System.Collections.Generic;
using Shooter.Combat;
using Shooter.Effects;
using Shooter.Enemies;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using Shooter.Upgrades;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shooter.Weapons
{
    /// <summary>
    /// Hitscan weapon: left click fires a ray from the camera, applying damage to
    /// <see cref="IDamageable"/> targets. Handles ammo, reload, fire rate, spread,
    /// muzzle flash, tracer, recoil and reload pose. All effects use built-in primitives.
    /// </summary>
    public sealed class Weapon : MonoBehaviour
    {
        [Header("Ammo")]
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private int reserveAmmo = 150;
        [SerializeField] private int maxReserve = 300;

        [Header("Firing")]
        [Tooltip("Seconds between shots. 0.11 is roughly 9 rounds per second.")]
        [SerializeField] private float fireInterval = 0.11f;
        [SerializeField] private float reloadDuration = 1.5f;
        [SerializeField] private float damage = 24f;
        [SerializeField] private float range = 150f;
        [Tooltip("Cone half-angle in degrees applied to each shot.")]
        [SerializeField] private float spreadDegrees = 0.9f;

        [Header("Precision")]
        [Tooltip("Damage multiplier when a shot lands in the top of an enemy's body.")]
        [SerializeField] private float headshotMultiplier = 2f;

        [Header("Underbarrel shotgun")]
        [Tooltip("Press F to fire the breach shotgun.")]
        [SerializeField] private int shotgunCapacity = 4;
        [SerializeField] private int shotgunPellets = 8;
        [SerializeField] private float shotgunDamage = 10f;
        [SerializeField] private float shotgunSpreadDegrees = 6f;
        [SerializeField] private float shotgunRange = 45f;
        [Tooltip("Damage multiplier at maximum range.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float shotgunFalloff = 0.35f;
        [SerializeField] private float shotgunCooldown = 0.9f;
        [SerializeField] private float shotgunReloadDuration = 2.4f;
        [SerializeField] private float shotgunRecoilPitch = 3.2f;
        [SerializeField] private float pumpTravel = 0.06f;
        [SerializeField] private float pumpCycleDuration = 0.35f;

        [Header("Ballistics")]
        [Tooltip("Rifle damage is full inside this distance, then falls off.")]
        [SerializeField] private float falloffStart = 18f;
        [SerializeField] private float falloffEnd = 60f;
        [Range(0.1f, 1f)]
        [SerializeField] private float falloffMinimum = 0.65f;
        [Tooltip("Damage multiplier for a ricocheted hit.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float ricochetDamageMultiplier = 0.7f;

        [Header("Melee")]
        [SerializeField] private float meleeRange = 2.6f;
        [SerializeField] private float meleeDamage = 55f;
        [SerializeField] private float meleeCooldown = 0.9f;
        [Tooltip("Dot-product threshold for the melee cone (1 = dead ahead).")]
        [Range(-1f, 1f)]
        [SerializeField] private float meleeArcDot = 0.3f;
        [SerializeField] private float meleeSwingDuration = 0.24f;

        [Header("Aim down sights")]
        [Tooltip("Spread multiplier while aiming.")]
        [Range(0f, 1f)]
        [SerializeField] private float aimSpreadMultiplier = 0.2f;
        [Tooltip("Recoil multiplier while aiming.")]
        [Range(0f, 1f)]
        [SerializeField] private float aimRecoilMultiplier = 0.45f;
        [Tooltip("Local position the weapon moves to while aiming. Y centres the optic dot; Z clears the rear sight.")]
        [SerializeField] private Vector3 aimLocalPosition = new Vector3(0f, -0.135f, 0.44f);
        [SerializeField] private float hideWeaponBelowFov = 45f;
        [SerializeField] private float sightBlendSpeed = 14f;

        [Header("Feel")]
        [SerializeField] private float recoilPitch = 1.1f;
        [SerializeField] private float recoilYaw = 0.25f;
        [SerializeField] private float muzzleFlashTime = 0.045f;
        [SerializeField] private float tracerTime = 0.03f;
        [SerializeField] private float reloadDrop = 0.12f;

        [Header("Kickback spring")]
        [SerializeField] private float kickbackStiffness = 90f;
        [SerializeField] private float kickbackSpringDamping = 11f;

        [Header("Firing animation")]
        [Tooltip("Bolt/slide transform from the rifle model. Moves back on every shot.")]
        public Transform slide;
        [Tooltip("Where spent cases are thrown from.")]
        public Transform shellEjectPoint;
        public Material shellMaterial;
        [SerializeField] private float slideTravel = 0.045f;
        [Tooltip("Seconds for the bolt to travel back and return. Matches the base fire interval.")]
        [SerializeField] private float slideCycleDuration = 0.11f;
        [Tooltip("Multiplier applied to the casing's initial velocity.")]
        [SerializeField] private float shellEjectSpeed = 1.6f;
        [SerializeField] private float shellLifetime = 2.5f;

        [Header("Sway")]
        [Tooltip("Metres the weapon drifts per pixel of mouse movement.")]
        [SerializeField] private float swayPositionScale = 0.0012f;
        [Tooltip("Degrees the weapon turns per pixel of mouse movement.")]
        [SerializeField] private float swayRotationScale = 0.055f;
        [Tooltip("How quickly sway settles, per second.")]
        [SerializeField] private float swayDamping = 9f;
        [SerializeField] private float swayMaxPosition = 0.05f;
        [SerializeField] private float swayMaxRotation = 3.5f;
        [Tooltip("Sideways weapon drift at full strafe speed, in metres.")]
        [SerializeField] private float strafeSway = 0.028f;
        [Tooltip("Metres of idle breathing sway.")]
        [SerializeField] private float idleBreath = 0.004f;
        [SerializeField] private float breathSpeed = 1.4f;

        [Header("References (wired by the scene builder)")]
        public Camera aimCamera;
        public PlayerController player;
        public Transform muzzle;
        public GameObject muzzleFlash;
        public Light muzzleLight;
        public Transform shotgunMuzzle;
        public GameObject shotgunFlash;
        public Light shotgunLight;
        public Transform shotgunEjectPoint;
        public Transform pump;
        public Animation viewAnimation;
        public Animation muzzleBurst;
        public Animation breachBurst;
        public GameObject spentCell;
        public GameObject impactMark;
        public LineRenderer tracer;
        public AudioSource audioSource;
        public HUDController hud;

        [Header("Effects (wired by the scene builder)")]
        public Material impactMaterial;
        public Material sparkMaterial;
        public Material explosionMaterial;

        public WeaponState State { get; private set; }

        public int AmmoInMagazine => State != null ? State.AmmoInMagazine : 0;
        public int ReserveAmmo => State != null ? State.ReserveAmmo : 0;
        public bool IsReloading => State != null && State.IsReloading;
        public float ReloadProgress01 => State != null ? State.ReloadProgress(Time.time) : 0f;

        /// <summary>Loaded shells in the underbarrel shotgun.</summary>
        public int ShotgunShells => shotgunState != null ? shotgunState.Shells : 0;

        public int ShotgunCapacity => shotgunState != null ? shotgunState.Capacity : 0;

        public bool IsShotgunReloading => shotgunState != null && shotgunState.IsReloading;

        public float ShotgunReloadProgress => shotgunState != null ? shotgunState.ReloadProgress(Time.time) : 1f;

        /// <summary>Current pump travel in metres, exposed for play-mode tests.</summary>
        public float PumpOffset => pumpOffset;

        public bool PumpWired => pump != null;

        /// <summary>True while the melee bash animation is playing.</summary>
        public bool MeleeActive => Time.time - meleeSwingStartedAt < meleeSwingDuration;

        public float MeleeCooldownRemaining => Mathf.Max(0f, meleeReadyAt - Time.time);

        /// <summary>Number of points in the last tracer, exposed for ricochet tests.</summary>
        public int LastTracerPointCount { get; private set; }

        /// <summary>False when the reserve is already full, so pickups are not wasted.</summary>
        public bool CanReceiveAmmo => State != null && State.ReserveAmmo < State.MaxReserve;

        /// <summary>
        /// Adds reserve ammo, e.g. from an ammo crate.
        /// Returns how much was actually stored (zero if already full).
        /// </summary>
        public int AddAmmo(int amount)
        {
            if (State == null || amount <= 0)
            {
                return 0;
            }

            int before = State.ReserveAmmo;
            State.AddReserveAmmo(amount);
            return State.ReserveAmmo - before;
        }

        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private float muzzleFlashUntil;
        private float tracerUntil;
        private float kickback;              // spring offset: 0 at rest, pushed by each shot
        private float kickbackVelocity;
        private Vector3 slideRestLocalPosition;
        private Vector3 slideBackAxis = Vector3.back;
        private float slideOffset;
        private float slideCycleStartedAt = -999f;
        private ShotgunState shotgunState;
        private readonly DryClickGate dryClickGate = new DryClickGate();
        private float appliedFireRateMultiplier = 1f;
        private Vector3 pumpRestLocalPosition;
        private Vector3 pumpBackAxis = Vector3.back;
        private float pumpOffset;
        private float pumpCycleStartedAt = -999f;
        private float shotgunFlashUntil;
        private float meleeReadyAt;
        private float meleeSwingStartedAt = -999f;
        private Vector3 swayPosition;        // smoothed sway offset (local space)
        private Vector2 swayRotation;        // smoothed sway rotation in degrees (x, y)
        private float lastFireTime = -999f;
        private float aimBlend; // 0 = hip fire, 1 = fully aimed.
        private Renderer[] weaponRenderers;
        private bool renderersVisible = true;

        /// <summary>Seconds since the last shot; large when the gun has not fired yet.</summary>
        public float TimeSinceLastShot => Time.time - lastFireTime;

        /// <summary>Current bolt travel in metres, exposed for play-mode tests.</summary>
        public float SlideOffset => slideOffset;

        /// <summary>True when the imported rifle supplied a moving bolt.</summary>
        public bool SlideWired => slide != null;

        private PlayerHealth viewHealth;
        private string viewLoop;
        private float viewActionUntil;
        private bool viewDying;

        private void Awake()
        {
            RebuildState();
            shotgunState = new ShotgunState(shotgunCapacity);
            restLocalPosition = transform.localPosition;
            restLocalRotation = transform.localRotation;

            if (aimCamera == null)
            {
                aimCamera = GetComponentInParent<Camera>();
            }

            if (player == null)
            {
                player = GetComponentInParent<PlayerController>();
            }

            weaponRenderers = GetComponentsInChildren<Renderer>(true);
            if (viewAnimation != null)
            {
                viewHealth = GetComponentInParent<PlayerHealth>();
                if (viewHealth != null)
                {
                    viewHealth.Damaged += PlayViewHit;
                    viewHealth.Died += PlayViewDeath;
                }
            }

            if (slide != null)
            {
                slideRestLocalPosition = slide.localPosition;

                // The imported FBX root is rotated, so derive the local axis that
                // points toward the stock instead of assuming local -Z.
                slideBackAxis = slide.parent.InverseTransformDirection(-transform.forward).normalized;

                if (slideBackAxis.sqrMagnitude < 0.001f)
                {
                    slideBackAxis = Vector3.up;
                }
            }

            if (pump != null)
            {
                pumpRestLocalPosition = pump.localPosition;
                pumpBackAxis = pump.parent.InverseTransformDirection(-transform.forward).normalized;

                if (pumpBackAxis.sqrMagnitude < 0.001f)
                {
                    pumpBackAxis = Vector3.up;
                }
            }

            HideEffects();
        }

        private void OnDestroy()
        {
            if (viewHealth != null)
            {
                viewHealth.Damaged -= PlayViewHit;
                viewHealth.Died -= PlayViewDeath;
            }
        }

        private void PlayViewHit(float _)
        {
            if (Time.time >= viewActionUntil)
                PlayViewAction("Hit");
        }

        private void PlayViewDeath()
        {
            PlayViewAction("Death");
            viewDying = true;
            // The game-over screen pauses gameplay. Sample the final view pose
            // before that pause so the model does not freeze in its idle pose.
            AnimationState state = viewAnimation["Death"];
            if (state != null)
            {
                state.wrapMode = WrapMode.ClampForever;
                state.time = state.length;
                viewAnimation.Sample();
            }
        }

        private void PlayViewAction(string clip, float duration = 0f, float progress = 0f)
        {
            if (viewAnimation == null || viewDying)
                return;

            AnimationState state = viewAnimation[clip];
            if (state == null)
                return;

            float seek = state.length * Mathf.Clamp(progress, 0f, 0.999f);
            float remaining = state.length - seek;
            state.speed = duration > 0f ? remaining / duration : 1f;
            viewAnimation.Play(clip);
            state.time = seek;
            viewActionUntil = Time.time + remaining / state.speed;
            viewLoop = null;
        }

        private void UpdateViewAnimation()
        {
            if (viewAnimation == null || viewDying)
                return;

            float now = Time.time;
            if (now < viewActionUntil)
                return;

            if (State.IsReloading)
            {
                float progress = State.ReloadProgress(now);
                PlayViewAction("Reload", State.ReloadDuration * (1f - progress), progress);
                return;
            }

            if (shotgunState.IsReloading)
            {
                PlayViewAction("BreachReload", shotgunState.ReloadEndsAt - now, shotgunState.ReloadProgress(now));
                return;
            }

            string loop = player != null && player.Velocity.sqrMagnitude > 0.16f ? "Move" : "Idle";
            if (loop != viewLoop)
            {
                viewAnimation.CrossFade(loop, 0.08f);
                viewLoop = loop;
            }
        }

        /// <summary>
        /// Rebuilds the pure weapon state with augment-modified stats, carrying the
        /// current magazine and reserve counts across so picking a card mid-fight
        /// does not magically refill the gun. Multipliers are read live from
        /// <see cref="UpgradeManager"/>; this is only needed when magazine size,
        /// fire interval, reload duration or the reserve cap change.
        /// </summary>
        public void RebuildState()
        {
            UpgradeManager upgrades = UpgradeManager.Instance;
            WeaponState previous = State;

            int magazine = Mathf.Max(1, Mathf.CeilToInt(magazineSize * (upgrades != null ? upgrades.MagazineMultiplier : 1f)));
            appliedFireRateMultiplier = Mathf.Max(0.1f, upgrades != null ? upgrades.EffectiveFireRateMultiplier : 1f);
            float interval = Mathf.Max(0.02f, fireInterval / appliedFireRateMultiplier);
            float reload = Mathf.Max(0.15f, reloadDuration / (upgrades != null ? upgrades.ReloadSpeedMultiplier : 1f));
            int effectiveMaxReserve = maxReserve + (upgrades != null ? upgrades.ReserveBonus : 0);
            int inMagazine = magazine;
            int reserve = reserveAmmo;

            if (previous != null)
            {
                inMagazine = previous.AmmoInMagazine;
                reserve = previous.ReserveAmmo;
            }

            State = new WeaponState(magazine, reserve, interval, reload, effectiveMaxReserve);
            State.SetMagazineAmmo(inMagazine);

            // Fire cooldown and reload progress survive the rebuild: a card pick
            // must not hand out a free shot or refund an in-flight reload.
            State.CarryTimingFrom(previous);
        }

        /// <summary>Called by the upgrade manager after an augment changes weapon stats.</summary>
        public void OnAugmentsChanged() => RebuildState();

        /// <summary>
        /// Applies timed fire-rate effects (Crit Combo's 2 s window) to the rifle.
        /// The interval is baked into WeaponState, so the change is detected here
        /// instead of rebuilding on every crit. CarryTimingFrom keeps the shot
        /// cooldown intact when the rapid window opens and closes.
        /// </summary>
        public void ApplyLiveFireRate()
        {
            UpgradeManager upgrades = UpgradeManager.Instance;
            float rate = Mathf.Max(0.1f, upgrades != null ? upgrades.EffectiveFireRateMultiplier : 1f);

            if (Mathf.Abs(rate - appliedFireRateMultiplier) <= 0.0001f)
            {
                return;
            }

            appliedFireRateMultiplier = rate;
            RebuildState();
        }

        private void Update()
        {
            if (State == null)
            {
                return;
            }

            float now = Time.time;
            ApplyLiveFireRate();
            State.Tick(now);
            shotgunState ??= new ShotgunState(shotgunCapacity);
            shotgunState.Tick(now);

            HandleReloadInput(now);
            HandleFireInput(now);
            HandleShotgunInput();
            HandleMeleeInput();
            UpdateEffects(now);
            UpdatePose(now);
            UpdateViewAnimation();
            UpdateVisibility();
        }

        /// <summary>Starts a reload if possible (used by the R key and by auto-reload on empty).</summary>
        public bool TryReload()
        {
            if (State == null || !State.TryStartReload(Time.time))
            {
                return false;
            }

            PlayViewAction("Reload", State.ReloadDuration);

            if (audioSource != null)
            {
                AudioManager.Instance?.PlayWeaponReload(audioSource.transform.position, audioSource);
            }

            return true;
        }

        private void HandleReloadInput(float now)
        {
            SettingsManager settings = SettingsManager.Instance;

            if (settings != null && settings.WasPressed(SettingsManager.ActionReload))
            {
                TryReload();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                TryReload();
            }
        }

        private void HandleFireInput(float now)
        {
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
            {
                return;
            }

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            if (player != null && !player.CanFireNow)
            {
                return;
            }

            if (!State.CanFire(now))
            {
                // Empty magazine: one click per interval, and a reload attempt.
                if (dryClickGate.ShouldPlay(now, State.IsReloading, State.AmmoInMagazine))
                {
                    AudioManager.Instance?.PlayWeaponEmpty(muzzle != null ? muzzle.position : transform.position);
                    TryReload();
                }

                return;
            }

            Fire(now);
        }

        private void HandleShotgunInput()
        {
            SettingsManager settings = SettingsManager.Instance;
            bool pressed = settings != null && settings.WasPressed(SettingsManager.ActionShotgun);

            if (!pressed && (Keyboard.current == null || !Keyboard.current.fKey.wasPressedThisFrame))
            {
                return;
            }

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            if (player != null && !player.CanFireNow)
            {
                return;
            }

            TryFireShotgun();
        }

        private void HandleMeleeInput()
        {
            SettingsManager settings = SettingsManager.Instance;
            bool pressed = settings != null && settings.WasPressed(SettingsManager.ActionMelee);

            if (!pressed && (Keyboard.current == null || !Keyboard.current.vKey.wasPressedThisFrame))
            {
                return;
            }

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            if (player != null && !player.CanFireNow)
            {
                return;
            }

            TryMelee();
        }

        /// <summary>
        /// Short-range bash. Hits every enemy in a forward cone, so it doubles as
        /// crowd control when a runner closes the gap. Includes a real cooldown so
        /// it cannot replace the gun.
        /// </summary>
        public bool TryMelee()
        {
            float now = Time.time;

            if (now < meleeReadyAt ||
                (GameManager.Instance != null && !GameManager.Instance.IsPlaying))
            {
                return false;
            }

            meleeReadyAt = now + meleeCooldown;
            meleeSwingStartedAt = now;
            PlayViewAction("Melee", meleeSwingDuration);
            AudioManager.Instance?.PlayMeleeSwing(transform.position);

            Vector3 origin = aimCamera != null ? aimCamera.transform.position : transform.position;
            Vector3 forward = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            UpgradeManager upgrades = UpgradeManager.Instance;
            float appliedDamage = meleeDamage *
                (upgrades != null ? upgrades.EffectiveDamageMultiplier : 1f);

            Collider[] candidates = Physics.OverlapSphere(
                origin + forward * (meleeRange * 0.5f),
                meleeRange * 0.7f,
                ~(1 << 2),
                QueryTriggerInteraction.Ignore);

            var hitTargets = new HashSet<IDamageable>();

            foreach (Collider candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                IDamageable target = candidate.GetComponentInParent<IDamageable>();

                if (target == null || !hitTargets.Add(target))
                {
                    continue;
                }

                Vector3 targetPosition = candidate.transform.position;

                if (!IsInMeleeArc(origin, forward, targetPosition, meleeRange, meleeArcDot))
                {
                    continue;
                }

                if (IsImmune(target))
                {
                    continue;
                }

                Vector3 hitPoint = candidate.ClosestPoint(origin + forward * (meleeRange * 0.5f));
                target.TakeDamage(appliedDamage, hitPoint);
                hud?.ShowHitMarker(false);
                hud?.ShowDamageNumber(hitPoint, ReportedDamage(target, appliedDamage), false, false);
                AudioManager.Instance?.PlayEnemyHit(hitPoint);
            }

            return true;
        }

        private void Fire(float now)
        {
            State.TryFire(now);

            UpgradeManager upgrades = UpgradeManager.Instance;

            bool aiming = player != null && player.IsAiming;
            Vector3 origin = aimCamera != null ? aimCamera.transform.position : transform.position;
            Vector3 direction = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            float currentSpread = spreadDegrees * (aiming ? aimSpreadMultiplier : 1f) *
                (upgrades != null ? upgrades.SpreadMultiplier : 1f);
            direction = ApplySpread(direction, currentSpread);

            float velocity = upgrades != null ? upgrades.BulletVelocityMultiplier : 1f;
            float effectiveRange = range * velocity;
            int mask = ~(1 << 2);
            int pierce = upgrades != null ? upgrades.BonusPierce : 0;
            int maxBounces = upgrades != null ? upgrades.RicochetBounces : 0;

            var damaged = new HashSet<IDamageable>();
            var tracerPoints = new List<Vector3> { muzzle != null ? muzzle.position : transform.position };

            Vector3 segmentOrigin = origin;
            Vector3 segmentDirection = direction;
            float remainingRange = effectiveRange;
            int bounceIndex = 0;
            bool continueShot = true;

            while (continueShot)
            {
                continueShot = false;

                List<RaycastHit> hits = new List<RaycastHit>(
                    Physics.RaycastAll(segmentOrigin, segmentDirection, remainingRange, mask, QueryTriggerInteraction.Ignore));
                hits.Sort((a, b) => a.distance.CompareTo(b.distance));

                Vector3 segmentEnd = segmentOrigin + segmentDirection * remainingRange;

                foreach (RaycastHit hit in hits)
                {
                    segmentEnd = hit.point;
                    IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                    if (target == null)
                    {
                        SpawnImpact(hit.point, hit.normal);

                        if (sparkMaterial != null)
                        {
                            DebrisBurst.Spawn(hit.point, Vector3.Reflect(segmentDirection, hit.normal), sparkMaterial,
                                4, 3f, 0.035f, 0.6f);
                        }

                        if (upgrades != null && upgrades.ExplosionRadius > 0f)
                        {
                            ExplodeBulletAt(hit.point, null,
                                BulletExplosionBaseDamage(hit.distance, upgrades), upgrades);
                        }

                        // Ricochet off world geometry once per owned stack. A wall hit
                        // with no bounces left ends the shot exactly as before.
                        if (bounceIndex < maxBounces && remainingRange - hit.distance > 0.2f)
                        {
                            segmentOrigin = hit.point + hit.normal * 0.02f;
                            segmentDirection = Vector3.Reflect(segmentDirection, hit.normal);
                            remainingRange -= hit.distance;
                            bounceIndex++;
                            continueShot = true;
                        }

                        break;
                    }

                    if (!damaged.Add(target))
                    {
                        continue; // a second collider on the same enemy.
                    }

                    if (IsImmune(target))
                    {
                        // Telegraphed immunity: the shot stops on the body but nothing
                        // is recorded, so no marker, number, crit or Winded penalty.
                        break;
                    }

                    float appliedDamage = damage * (upgrades != null ? upgrades.EffectiveDamageMultiplier : 1f);
                    appliedDamage *= RangeDamageMultiplier(hit.distance, velocity,
                        falloffStart, falloffEnd, falloffMinimum);

                    bool headshot = target is EnemyHealth && IsHeadshot(hit.collider.bounds, hit.point);

                    if (headshot)
                    {
                        appliedDamage *= Mathf.Max(1f, headshotMultiplier);
                    }

                    appliedDamage *= 1f + (upgrades != null ? upgrades.SlideFirstHitDamageMultiplier : 0f);
                    appliedDamage *= 1f + (upgrades != null ? upgrades.AirborneFirstHitDamageMultiplier : 0f);

                    if (bounceIndex > 0)
                    {
                        appliedDamage *= ricochetDamageMultiplier;
                    }

                    bool critical = upgrades != null && Random.value < upgrades.EffectiveCritChance;

                    if (critical)
                    {
                        appliedDamage *= upgrades.CriticalDamageMultiplier;
                    }

                    target.TakeDamage(appliedDamage, hit.point);
                    hud?.ShowHitMarker(headshot);
                    hud?.ShowDamageNumber(hit.point, ReportedDamage(target, appliedDamage), headshot, critical);
                    AudioManager.Instance?.PlayWeaponHit(hit.point);

                    if (upgrades != null && upgrades.ExplosionRadius > 0f)
                    {
                        ExplodeBulletAt(hit.point, target, appliedDamage, upgrades);
                    }

                    if (critical)
                    {
                        upgrades.OnCrit();
                    }

                    if (upgrades != null)
                    {
                        // Winded trade: every accepted hit costs a sliver of move speed.
                        upgrades.TakeSpeedPenalty();
                    }

                    // Piercing only applies to the straight ray; a ricochet hits one target.
                    if (bounceIndex > 0 || damaged.Count > pierce)
                    {
                        break;
                    }
                }

                tracerPoints.Add(segmentEnd);
            }

            PlayMuzzleFlash();
            PlayTracer(tracerPoints);
            ApplyRecoil();
            PlayViewAction("Fire");
            CycleAction();
            PlayFireAudio();

            lastFireTime = now;
        }

        /// <summary>
        /// Damage retention over distance. High-Velocity Rounds multiply both
        /// distances, so the full-damage window and the falloff tail stretch out.
        /// </summary>
        public static float RangeDamageMultiplier(float distance, float velocityMultiplier,
            float start, float end, float minimum)
        {
            float full = start * Mathf.Max(0.1f, velocityMultiplier);
            float tail = end * Mathf.Max(0.1f, velocityMultiplier);

            if (distance <= full)
            {
                return 1f;
            }

            float t = Mathf.Clamp01((distance - full) / Mathf.Max(0.01f, tail - full));
            return Mathf.Lerp(1f, Mathf.Clamp01(minimum), t);
        }

        /// <summary>
        /// True when the target is inside the melee cone. Exposed for tests.
        /// </summary>
        public static bool IsInMeleeArc(Vector3 origin, Vector3 forward, Vector3 target,
            float range, float minimumDot)
        {
            Vector3 toTarget = target - origin;
            toTarget.y = 0f;
            forward.y = 0f;

            if (toTarget.sqrMagnitude > range * range)
            {
                return false;
            }

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            return Vector3.Dot(forward.normalized, toTarget.normalized) >= minimumDot;
        }

        /// <summary>
        /// The damage the target will actually lose, so floated numbers stay
        /// honest against armoured or exposed targets (see <see cref="IDamageScale"/>).
        /// </summary>
        private static float ReportedDamage(IDamageable target, float amount)
        {
            return target is IDamageScale scaled ? amount * scaled.IncomingDamageScale : amount;
        }

        /// <summary>
        /// True when the target is currently immune (a boss mid-roar). Damage is
        /// discarded inside TakeDamage, so every piece of firing feedback has to be
        /// skipped as well or the player is told they hit for full damage for nothing.
        /// </summary>
        private static bool IsImmune(IDamageable target) =>
            target is EnemyHealth health && health.IsInvulnerable;

        private float BulletExplosionBaseDamage(float distance, UpgradeManager upgrades)
        {
            float velocity = upgrades != null ? upgrades.BulletVelocityMultiplier : 1f;
            return damage * (upgrades != null ? upgrades.EffectiveDamageMultiplier : 1f) *
                RangeDamageMultiplier(distance, velocity, falloffStart, falloffEnd, falloffMinimum);
        }

        /// <summary>
        /// Explosive Rounds: splash damage around a bullet impact. The direct target
        /// is excluded because it already took the full hit.
        /// </summary>
        private void ExplodeBulletAt(Vector3 point, IDamageable directTarget, float sourceDamage,
            UpgradeManager upgrades)
        {
            if (upgrades == null || upgrades.ExplosionRadius <= 0f || upgrades.ExplosionDamageFraction <= 0f ||
                sourceDamage <= 0f)
            {
                return;
            }

            float radius = upgrades.ExplosionRadius;
            float fraction = upgrades.ExplosionDamageFraction;
            SpawnExplosionEffect(point, radius);

            foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude))
            {
                if (enemy == null || enemy.IsDead)
                {
                    continue;
                }

                if (directTarget is EnemyHealth directEnemy && enemy == directEnemy)
                {
                    continue;
                }

                float distance = Vector3.Distance(point, enemy.transform.position);

                if (distance > radius)
                {
                    continue;
                }

                float falloff = Mathf.Lerp(1f, 0.4f, distance / Mathf.Max(0.01f, radius));
                enemy.TakeDamage(sourceDamage * fraction * falloff, point);
            }
        }

        private void SpawnExplosionEffect(Vector3 point, float radius)
        {
            float size = Mathf.Max(0.25f, radius * 0.45f);

            if (FoundryEffects.Spawn(FoundryEffect.Explosion, point, Quaternion.identity, size, 0.25f) == null &&
                explosionMaterial != null)
            {
                GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flash.name = "Bullet Explosion";
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

                Destroy(flash, 0.12f);
            }

            DebrisBurst.Spawn(point, Random.onUnitSphere, sparkMaterial, 6, 4f, 0.04f, 0.5f);

            var lightObject = new GameObject("Explosion Light");
            lightObject.transform.position = point;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.15f);
            light.range = radius * 2.5f;
            light.intensity = 8f;
            light.shadows = LightShadows.None;
            Destroy(lightObject, 0.15f);
        }

        /// <summary>
        /// Fires one shell from the underbarrel shotgun. Returns false when the
        /// tube is empty or still cooling down.
        /// </summary>
        public bool TryFireShotgun()
        {
            shotgunState ??= new ShotgunState(shotgunCapacity);

            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return false;
            }

            UpgradeManager upgrades = UpgradeManager.Instance;

            // The underbarrel is an augment, not starting equipment.
            if (upgrades != null && !upgrades.ShotgunUnlocked)
            {
                return false;
            }
            float fireRate = upgrades != null ? Mathf.Max(0.1f, upgrades.EffectiveFireRateMultiplier) : 1f;
            float reloadSpeed = upgrades != null ? Mathf.Max(0.1f, upgrades.ReloadSpeedMultiplier) : 1f;
            float now = Time.time;

            if (!shotgunState.TryFire(now, shotgunCooldown / fireRate, shotgunReloadDuration / reloadSpeed))
            {
                return false;
            }

            FireShotgun(now);
            return true;
        }

        /// <summary>
        /// Eight independent pellets with range falloff. Damage is aggregated per
        /// enemy so one blast produces one hit marker and one damage number
        /// instead of eight overlapping ones.
        /// </summary>
        private void FireShotgun(float now)
        {
            UpgradeManager upgrades = UpgradeManager.Instance;
            bool aiming = player != null && player.IsAiming;
            Vector3 origin = aimCamera != null ? aimCamera.transform.position : transform.position;
            Vector3 forward = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            float spread = shotgunSpreadDegrees * (aiming ? aimSpreadMultiplier : 1f) *
                (upgrades != null ? upgrades.SpreadMultiplier : 1f);
            int mask = ~(1 << 2);

            var damageByTarget = new Dictionary<IDamageable, float>();
            var pointByTarget = new Dictionary<IDamageable, Vector3>();
            var headshotTargets = new HashSet<IDamageable>();
            bool anyCrit = false;
            Vector3 tracerEnd = origin + forward * shotgunRange;

            for (int pellet = 0; pellet < shotgunPellets; pellet++)
            {
                Vector3 direction = ApplySpread(forward, spread);
                List<RaycastHit> hits = new List<RaycastHit>(
                    Physics.RaycastAll(origin, direction, shotgunRange, mask, QueryTriggerInteraction.Ignore));
                hits.Sort((a, b) => a.distance.CompareTo(b.distance));

                foreach (RaycastHit hit in hits)
                {
                    IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                    if (target == null)
                    {
                        // Only a few decals per blast; eight holes per shell is noise.
                        if (pellet < 3)
                        {
                            SpawnImpact(hit.point, hit.normal);
                        }

                        tracerEnd = hit.point;
                        break;
                    }

                    float falloff = Mathf.Lerp(1f, shotgunFalloff, Mathf.Clamp01(hit.distance / shotgunRange));
                    float appliedDamage = shotgunDamage * falloff *
                        (upgrades != null ? upgrades.EffectiveDamageMultiplier : 1f);

                    if (IsImmune(target))
                    {
                        tracerEnd = hit.point;
                        break; // immune body stops the pellet; nothing is recorded
                    }

                    bool headshot = target is EnemyHealth && IsHeadshot(hit.collider.bounds, hit.point);

                    if (headshot)
                    {
                        appliedDamage *= Mathf.Max(1f, headshotMultiplier);
                    }

                    if (upgrades != null && Random.value < upgrades.EffectiveCritChance)
                    {
                        appliedDamage *= upgrades.CriticalDamageMultiplier;
                        anyCrit = true;
                    }

                    damageByTarget[target] = damageByTarget.TryGetValue(target, out float existing)
                        ? existing + appliedDamage
                        : appliedDamage;

                    if (!pointByTarget.ContainsKey(target))
                    {
                        pointByTarget[target] = hit.point;
                    }

                    if (headshot)
                    {
                        headshotTargets.Add(target);
                    }

                    tracerEnd = hit.point;
                    break; // this pellet stops on the first damageable it meets
                }
            }

            foreach (KeyValuePair<IDamageable, float> pair in damageByTarget)
            {
                Vector3 point = pointByTarget[pair.Key];
                bool headshot = headshotTargets.Contains(pair.Key);

                pair.Key.TakeDamage(pair.Value, point);
                hud?.ShowHitMarker(headshot);
                hud?.ShowDamageNumber(point, ReportedDamage(pair.Key, pair.Value), headshot, anyCrit);
                upgrades?.TakeSpeedPenalty();
            }

            if (anyCrit)
            {
                upgrades?.OnCrit();
            }

            Vector3 muzzleStart = shotgunMuzzle != null
                ? shotgunMuzzle.position
                : muzzle != null ? muzzle.position : transform.position;

            PlayShotgunFlash();
            PlayTracer(muzzleStart, tracerEnd);
            ApplyShotgunRecoil();
            PlayViewAction("BreachFire");
            CyclePump();
            EjectShell(shotgunEjectPoint != null ? shotgunEjectPoint : shellEjectPoint, 0.05f);
            PlayShotgunAudio();

            lastFireTime = now;
        }

        /// <summary>
        /// True when <paramref name="point"/> is inside the top 20% of
        /// <paramref name="bounds"/>. Scale-agnostic, so elites and runners use
        /// the same rule. Exposed for headless tests.
        /// </summary>
        public static bool IsHeadshot(Bounds bounds, Vector3 point)
        {
            if (bounds.size.y <= 0.001f)
            {
                return false;
            }

            return point.y >= bounds.max.y - bounds.size.y * 0.2f;
        }

        private static Vector3 ApplySpread(Vector3 forward, float degrees)
        {
            if (degrees <= 0.001f)
            {
                return forward;
            }

            float radius = Mathf.Tan(degrees * Mathf.Deg2Rad);
            Vector2 offset = Random.insideUnitCircle * radius;

            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(right, forward).normalized;

            return (forward + right * offset.x + up * offset.y).normalized;
        }

        private void SpawnImpact(Vector3 point, Vector3 normal)
        {
            FoundryEffects.Spawn(FoundryEffect.Impact, point, Quaternion.LookRotation(normal), 0.38f, 0.16f);
            if (impactMaterial == null)
            {
                return;
            }

            // A thin cube reads as a bullet hole from any angle, unlike a one-sided quad.
            GameObject decal = impactMark != null ? Instantiate(impactMark) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            decal.name = "Impact";
            decal.transform.position = point + normal * 0.01f;
            decal.transform.rotation = Quaternion.LookRotation(normal);
            decal.transform.localScale = new Vector3(0.09f, 0.09f, 0.01f);
            if (impactMark == null)
                decal.GetComponent<MeshRenderer>().sharedMaterial = impactMaterial;

            Collider decalCollider = decal.GetComponent<Collider>();

            if (decalCollider != null)
            {
                Destroy(decalCollider);
            }

            Destroy(decal, 8f);
        }

        private void PlayMuzzleFlash()
        {
            if (muzzleFlash != null)
            {
                muzzleFlash.SetActive(true);
                muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                FoundryEffects.Play(muzzleBurst, muzzleFlashTime);
            }

            if (muzzleLight != null)
            {
                muzzleLight.enabled = true;
                muzzleLight.intensity = Random.Range(6f, 9f);
            }

            muzzleFlashUntil = Time.time + muzzleFlashTime;
        }

        private void PlayShotgunFlash()
        {
            if (shotgunFlash != null)
            {
                shotgunFlash.SetActive(true);
                shotgunFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                FoundryEffects.Play(breachBurst, muzzleFlashTime * 1.4f);
            }

            if (shotgunLight != null)
            {
                shotgunLight.enabled = true;
                shotgunLight.intensity = Random.Range(8f, 12f);
            }

            shotgunFlashUntil = Time.time + muzzleFlashTime * 1.4f;
        }

        private void PlayTracer(Vector3 endPoint)
        {
            PlayTracer(muzzle != null ? muzzle.position : transform.position, endPoint);
        }

        private void PlayTracer(List<Vector3> points)
        {
            if (tracer == null || points == null || points.Count < 2)
            {
                return;
            }

            tracer.positionCount = points.Count;
            LastTracerPointCount = points.Count;

            for (int i = 0; i < points.Count; i++)
            {
                tracer.SetPosition(i, points[i]);
            }

            PlayTracerPulse(points[0], points[1]);
            tracer.enabled = true;
            tracerUntil = Time.time + tracerTime;
        }

        private void PlayTracer(Vector3 start, Vector3 endPoint)
        {
            if (tracer == null)
            {
                return;
            }

            tracer.positionCount = 2;
            LastTracerPointCount = 2;
            tracer.SetPosition(0, start);
            tracer.SetPosition(1, endPoint);
            PlayTracerPulse(start, endPoint);
            tracer.enabled = true;
            tracerUntil = Time.time + tracerTime;
        }

        private void PlayTracerPulse(Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;
            if (direction.sqrMagnitude > 0.01f)
                FoundryEffects.Spawn(FoundryEffect.Tracer, start + direction.normalized * 0.25f,
                    Quaternion.LookRotation(direction), 0.8f, tracerTime);
        }

        private void ApplyRecoil()
        {
            // Spring impulse: the kick strength comes from the velocity we inject.
            kickbackVelocity += 0.9f;

            if (player != null)
            {
                float multiplier = player.IsAiming ? aimRecoilMultiplier : 1f;

                if (UpgradeManager.Instance != null)
                {
                    multiplier *= UpgradeManager.Instance.RecoilMultiplier;
                }

                float yawKick = Random.Range(-recoilYaw, recoilYaw) * multiplier;
                player.AddRecoil(recoilPitch * multiplier, yawKick);
                player.AddShake(0.3f * multiplier);
            }
        }

        private void ApplyShotgunRecoil()
        {
            kickbackVelocity += 1.6f;

            if (player != null)
            {
                float multiplier = player.IsAiming ? aimRecoilMultiplier : 1f;

                if (UpgradeManager.Instance != null)
                {
                    multiplier *= UpgradeManager.Instance.RecoilMultiplier;
                }

                player.AddRecoil(shotgunRecoilPitch * multiplier,
                    Random.Range(-1.4f, 1.4f) * multiplier);
                player.AddShake(1.1f * multiplier);
            }
        }

        private void PlayFireAudio()
        {
            Vector3 position = muzzle != null ? muzzle.position : transform.position;
            AudioManager.Instance?.PlayWeaponFire(position, audioSource);
        }

        private void PlayShotgunAudio()
        {
            Vector3 position = shotgunMuzzle != null ? shotgunMuzzle.position : transform.position;
            AudioManager.Instance?.PlayShotgunFire(position, audioSource);
        }

        private void UpdateEffects(float now)
        {
            if (muzzleFlash != null && muzzleFlash.activeSelf && now >= muzzleFlashUntil)
            {
                muzzleFlash.SetActive(false);
            }

            if (muzzleLight != null && muzzleLight.enabled && now >= muzzleFlashUntil)
            {
                muzzleLight.enabled = false;
            }

            if (shotgunFlash != null && shotgunFlash.activeSelf && now >= shotgunFlashUntil)
            {
                shotgunFlash.SetActive(false);
            }

            if (shotgunLight != null && shotgunLight.enabled && now >= shotgunFlashUntil)
            {
                shotgunLight.enabled = false;
            }

            if (tracer != null && tracer.enabled && now >= tracerUntil)
            {
                tracer.enabled = false;
            }
        }

        private void UpdatePose(float now)
        {
            // Blend between hip-fire and aim-down-sights positions. Exponential blend so
            // the transition eases out instead of arriving at a constant clip.
            bool wantsAim = player != null && player.IsAiming;
            float aimTarget = wantsAim ? 1f : 0f;
            aimBlend = Mathf.Lerp(aimBlend, aimTarget, 1f - Mathf.Exp(-sightBlendSpeed * Time.deltaTime));
            float eased = Mathf.SmoothStep(0f, 1f, aimBlend);

            Vector3 hipPosition = Vector3.Lerp(restLocalPosition, aimLocalPosition, eased);
            Quaternion hipRotation = Quaternion.Slerp(restLocalRotation, Quaternion.identity, eased);

            // Simple reload dip + per-shot kickback, adapted so aiming still reads.
            float reloadDip = 0f;
            float reloadTilt = 0f;

            if (viewAnimation == null && State.IsReloading)
            {
                float progress = State.ReloadProgress(now);
                float curve = Mathf.Sin(progress * Mathf.PI); // 0 -> 1 -> 0 across the reload.
                reloadDip = -reloadDrop * curve;
                reloadTilt = 25f * curve;
            }

            // Kickback spring: the shot pushes the gun back and the spring pulls it home,
            // with a slight overshoot that reads as recoil settling.
            float springAccel = -kickbackStiffness * kickback - kickbackSpringDamping * kickbackVelocity;
            kickbackVelocity += springAccel * Time.deltaTime;
            kickback = Mathf.Clamp(kickback + kickbackVelocity * Time.deltaTime, -0.2f, 0.8f);

            UpdateSway(eased);
            UpdateSlide();
            UpdatePump();

            float meleeProgress = viewAnimation == null && meleeSwingDuration > 0.001f
                ? (Time.time - meleeSwingStartedAt) / meleeSwingDuration
                : 1f;
            float meleeWeight = meleeProgress >= 0f && meleeProgress < 1f
                ? Mathf.Sin(meleeProgress * Mathf.PI)
                : 0f;

            // Recoil now lifts and rolls the gun a touch, so the animation has
            // more than one axis instead of a straight back-and-forth slide.
            Vector3 targetPosition = hipPosition
                + new Vector3(0f, reloadDip + kickback * 0.014f, -kickback * 0.8f * Mathf.Lerp(1f, 0.45f, eased))
                + new Vector3(-0.16f, -0.05f, 0.18f) * meleeWeight
                + swayPosition;

            float kickAngle = kickback * 130f * Mathf.Lerp(1f, 0.4f, eased);
            float kickRoll = kickback * 16f * Mathf.Lerp(1f, 0.35f, eased);
            Quaternion targetRotation = hipRotation
                * Quaternion.Euler(-kickAngle, 0f, reloadTilt - kickRoll)
                * Quaternion.Euler(swayRotation.x, swayRotation.y, 0f)
                * Quaternion.Euler(-8f * meleeWeight, -38f * meleeWeight, 22f * meleeWeight);

            float blend = 1f - Mathf.Exp(-20f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, blend);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, blend);
        }

        /// <summary>
        /// Starts the cyclic action: the bolt is kicked backward and a spent case
        /// is thrown from the ejection port. Both are pure code; no animation clip.
        /// </summary>
        private void CycleAction()
        {
            // Frame-rate independent: the bolt snaps fully open on the shot and
            // closes over a fixed duration, so slow frames cannot skip the cycle.
            slideOffset = slideTravel;
            slideCycleStartedAt = Time.time;
            EjectShell();
        }

        private void UpdateSlide()
        {
            if (slide == null)
            {
                return;
            }

            float progress = slideCycleDuration > 0.001f
                ? (Time.time - slideCycleStartedAt) / slideCycleDuration
                : 1f;

            slideOffset = progress >= 0f && progress < 1f
                ? slideTravel * (1f - Mathf.SmoothStep(0f, 1f, progress))
                : 0f;

            slide.localPosition = slideRestLocalPosition + slideBackAxis * slideOffset;
        }

        /// <summary>Cycles the underbarrel pump fore-end after a shell.</summary>
        private void CyclePump()
        {
            pumpOffset = pumpTravel;
            pumpCycleStartedAt = Time.time;
        }

        private void UpdatePump()
        {
            if (pump == null)
            {
                return;
            }

            float progress = pumpCycleDuration > 0.001f
                ? (Time.time - pumpCycleStartedAt) / pumpCycleDuration
                : 1f;

            pumpOffset = progress >= 0f && progress < 1f
                ? pumpTravel * (1f - Mathf.SmoothStep(0f, 1f, progress))
                : 0f;

            pump.localPosition = pumpRestLocalPosition + pumpBackAxis * pumpOffset;
        }

        private void EjectShell() => EjectShell(shellEjectPoint, 0.034f);

        private void EjectShell(Transform origin, float casingLength)
        {
            if (shellMaterial == null || origin == null)
            {
                return;
            }

            GameObject casing = spentCell != null ? Instantiate(spentCell) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            casing.name = "Shell Casing";
            casing.layer = 2; // Ignore Raycast, matching every other effect piece.
            casing.transform.position = origin.position;
            casing.transform.rotation = Random.rotation;
            casing.transform.localScale = new Vector3(0.014f, 0.014f, Mathf.Max(0.02f, casingLength));

            Collider collider = casing.GetComponent<Collider>();

            if (collider != null)
            {
                Destroy(collider);
            }

            MeshRenderer renderer = casing.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = shellMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            Rigidbody body = casing.AddComponent<Rigidbody>();
            body.mass = 0.015f;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.1f;

            // The port sits on the visible (left) side of the rifle, so throw the
            // casing out of that side rather than through the receiver.
            Vector3 velocity = -transform.right * Random.Range(1.2f, 2.0f)
                + transform.up * Random.Range(0.7f, 1.3f)
                - transform.forward * 0.15f;

            body.AddForce(velocity * shellEjectSpeed, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * 10f, ForceMode.VelocityChange);

            Destroy(casing, shellLifetime);
        }

        /// <summary>
        /// Weapon lag: the gun trails the mouse a touch, drifts against strafes and
        /// breathes when idle. All offsets are smoothed, so they never snap.
        /// </summary>
        private void UpdateSway(float aimEased)
        {
            Vector2 look = player != null ? player.SmoothedLookDelta : Vector2.zero;
            Vector3 velocity = player != null ? player.Velocity : Vector3.zero;

            Vector3 targetPosition = new Vector3(
                Mathf.Clamp(-look.x * swayPositionScale, -swayMaxPosition, swayMaxPosition),
                Mathf.Clamp(look.y * swayPositionScale * 0.6f, -swayMaxPosition, swayMaxPosition),
                0f);

            // Strafing drags the weapon slightly against the motion.
            float lateralSpeed = Vector3.Dot(velocity, transform.right);
            float referenceSpeed = player != null ? Mathf.Max(1f, player.SprintSpeed) : 8.8f;
            targetPosition.x -= lateralSpeed * (strafeSway / referenceSpeed) * (1f - aimEased * 0.7f);

            // Idle breathing, mostly gone while aiming.
            float breathing = Mathf.Sin(Time.time * breathSpeed) * idleBreath * (1f - aimEased * 0.75f);
            targetPosition.y += breathing;

            Vector2 targetRotation = new Vector2(
                Mathf.Clamp(-look.y * swayRotationScale, -swayMaxRotation, swayMaxRotation),
                Mathf.Clamp(-look.x * swayRotationScale, -swayMaxRotation, swayMaxRotation));

            float blend = 1f - Mathf.Exp(-swayDamping * Time.deltaTime);
            swayPosition = Vector3.Lerp(swayPosition, targetPosition, blend);
            swayRotation = Vector2.Lerp(swayRotation, targetRotation, blend);
        }

        /// <summary>
        /// Hides the weapon model once the FOV is narrow enough that the gun would
        /// block the view through the sights. Only toggles renderers on state change.
        /// </summary>
        private void UpdateVisibility()
        {
            if (aimCamera == null || weaponRenderers == null)
            {
                return;
            }

            bool shouldShow = aimCamera.fieldOfView > hideWeaponBelowFov;

            if (shouldShow == renderersVisible)
            {
                return;
            }

            renderersVisible = shouldShow;

            foreach (Renderer renderer in weaponRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = shouldShow;
                }
            }
        }

        private void HideEffects()
        {
            if (muzzleFlash != null)
            {
                muzzleFlash.SetActive(false);
            }

            if (muzzleLight != null)
            {
                muzzleLight.enabled = false;
            }

            if (shotgunFlash != null)
            {
                shotgunFlash.SetActive(false);
            }

            if (shotgunLight != null)
            {
                shotgunLight.enabled = false;
            }

            if (tracer != null)
            {
                tracer.enabled = false;
            }
        }
    }
}
