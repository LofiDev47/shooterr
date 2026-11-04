using System.Collections.Generic;
using Shooter.Combat;
using Shooter.Effects;
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

        [Header("Aim down sights")]
        [Tooltip("Spread multiplier while aiming.")]
        [Range(0f, 1f)]
        [SerializeField] private float aimSpreadMultiplier = 0.2f;
        [Tooltip("Recoil multiplier while aiming.")]
        [Range(0f, 1f)]
        [SerializeField] private float aimRecoilMultiplier = 0.45f;
        [Tooltip("Local position the weapon moves to while aiming.")]
        [SerializeField] private Vector3 aimLocalPosition = new Vector3(0f, -0.125f, 0.28f);
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
        public LineRenderer tracer;
        public AudioSource audioSource;
        public HUDController hud;

        [Header("Effects (wired by the scene builder)")]
        public Material impactMaterial;
        public Material sparkMaterial;

        public WeaponState State { get; private set; }

        public int AmmoInMagazine => State != null ? State.AmmoInMagazine : 0;
        public int ReserveAmmo => State != null ? State.ReserveAmmo : 0;
        public bool IsReloading => State != null && State.IsReloading;
        public float ReloadProgress01 => State != null ? State.ReloadProgress(Time.time) : 0f;

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
        private Vector3 swayPosition;        // smoothed sway offset (local space)
        private Vector2 swayRotation;        // smoothed sway rotation in degrees (x, y)
        private float lastFireTime = -999f;
        private float aimBlend; // 0 = hip fire, 1 = fully aimed.
        private Renderer[] weaponRenderers;
        private bool renderersVisible = true;

        /// <summary>Seconds since the last shot; large when the gun has not fired yet.</summary>
        public float TimeSinceLastShot => Time.time - lastFireTime;

        private void Awake()
        {
            RebuildState();
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

            HideEffects();
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

            int magazine = Mathf.Max(1, Mathf.CeilToInt(magazineSize * (upgrades != null ? upgrades.MagazineMultiplier : 1f)));
            float interval = Mathf.Max(0.02f, fireInterval / (upgrades != null ? upgrades.EffectiveFireRateMultiplier : 1f));
            float reload = Mathf.Max(0.15f, reloadDuration / (upgrades != null ? upgrades.ReloadSpeedMultiplier : 1f));
            int effectiveMaxReserve = maxReserve + (upgrades != null ? upgrades.ReserveBonus : 0);
            int inMagazine = magazine;
            int reserve = reserveAmmo;

            if (State != null)
            {
                inMagazine = State.AmmoInMagazine;
                reserve = State.ReserveAmmo;
            }

            State = new WeaponState(magazine, reserve, interval, reload, effectiveMaxReserve);
            State.SetMagazineAmmo(inMagazine);
        }

        /// <summary>Called by the upgrade manager after an augment changes weapon stats.</summary>
        public void OnAugmentsChanged() => RebuildState();

        private void Update()
        {
            if (State == null)
            {
                return;
            }

            float now = Time.time;
            State.Tick(now);

            HandleReloadInput(now);
            HandleFireInput(now);
            UpdateEffects(now);
            UpdatePose(now);
            UpdateVisibility();
        }

        /// <summary>Starts a reload if possible (used by the R key and by auto-reload on empty).</summary>
        public bool TryReload()
        {
            if (State == null || !State.TryStartReload(Time.time))
            {
                return false;
            }

            if (audioSource != null)
            {
                AudioManager.Instance?.PlayWeaponReload(audioSource.transform.position, audioSource);
            }

            return true;
        }

        private void HandleReloadInput(float now)
        {
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
                // Empty magazine: play the dry click and start reloading automatically.
                if (!State.IsReloading && State.AmmoInMagazine == 0)
                {
                    AudioManager.Instance?.PlayWeaponEmpty(muzzle != null ? muzzle.position : transform.position);
                    TryReload();
                }

                return;
            }

            Fire(now);
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

            Vector3 endPoint = origin + direction * range;

            // Layer 2 ("Ignore Raycast") holds the player, so the ray cannot hit its owner.
            int mask = ~(1 << 2);
            int pierce = upgrades != null ? upgrades.BonusPierce : 0;

            // RaycastAll, sorted by distance: the shot damages the first few distinct
            // targets it meets (base target plus pierce budget) and stops on geometry.
            // With no pierce this behaves exactly like a plain Raycast.
            List<RaycastHit> hits = new List<RaycastHit>(
                Physics.RaycastAll(origin, direction, range, mask, QueryTriggerInteraction.Ignore));
            hits.Sort((a, b) => a.distance.CompareTo(b.distance));

            List<IDamageable> damaged = new List<IDamageable>(pierce + 1);

            foreach (RaycastHit hit in hits)
            {
                endPoint = hit.point;

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                if (target == null)
                {
                    // Solid geometry ends the shot wherever it is in the queue.
                    SpawnImpact(hit.point, hit.normal);

                    if (sparkMaterial != null)
                    {
                        // Ricochet sparks off geometry, thrown back along the reflected ray.
                        DebrisBurst.Spawn(hit.point, Vector3.Reflect(direction, hit.normal), sparkMaterial,
                            4, 3f, 0.035f, 0.6f);
                    }

                    break;
                }

                if (damaged.Contains(target))
                {
                    continue; // a second collider on the same enemy.
                }

                float appliedDamage = damage * (upgrades != null ? upgrades.EffectiveDamageMultiplier : 1f);

                appliedDamage *= 1f + (upgrades != null ? upgrades.SlideFirstHitDamageMultiplier : 0f);
                appliedDamage *= 1f + (upgrades != null ? upgrades.AirborneFirstHitDamageMultiplier : 0f);

                bool critical = upgrades != null && Random.value < upgrades.EffectiveCritChance;

                if (critical)
                {
                    appliedDamage *= upgrades.CritMultiplier;
                }


                damaged.Add(target);
                target.TakeDamage(appliedDamage, hit.point);
                hud?.ShowHitMarker();
                AudioManager.Instance?.PlayWeaponHit(hit.point);

                if (critical)
                {
                    upgrades.OnCrit();
                }

                if (upgrades != null)
                {
                    // Winded trade: every accepted hit costs a sliver of move speed.
                    upgrades.TakeSpeedPenalty();
                }

                if (damaged.Count > pierce)
                {
                    break; // base target plus the pierce budget is spent.
                }
            }

            PlayMuzzleFlash();
            PlayTracer(endPoint);
            ApplyRecoil();
            PlayFireAudio();

            lastFireTime = now;
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
            if (impactMaterial == null)
            {
                return;
            }

            // A thin cube reads as a bullet hole from any angle, unlike a one-sided quad.
            GameObject decal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            decal.name = "Impact";
            decal.transform.position = point + normal * 0.01f;
            decal.transform.rotation = Quaternion.LookRotation(normal);
            decal.transform.localScale = new Vector3(0.09f, 0.09f, 0.01f);
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
            }

            if (muzzleLight != null)
            {
                muzzleLight.enabled = true;
                muzzleLight.intensity = Random.Range(6f, 9f);
            }

            muzzleFlashUntil = Time.time + muzzleFlashTime;
        }

        private void PlayTracer(Vector3 endPoint)
        {
            if (tracer == null)
            {
                return;
            }

            Vector3 start = muzzle != null ? muzzle.position : transform.position;
            tracer.SetPosition(0, start);
            tracer.SetPosition(1, endPoint);
            tracer.enabled = true;
            tracerUntil = Time.time + tracerTime;
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

        private void PlayFireAudio()
        {
            Vector3 position = muzzle != null ? muzzle.position : transform.position;
            AudioManager.Instance?.PlayWeaponFire(position, audioSource);
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

            if (State.IsReloading)
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

            Vector3 targetPosition = hipPosition
                + new Vector3(0f, reloadDip, -kickback * 0.8f * Mathf.Lerp(1f, 0.45f, eased))
                + swayPosition;

            float kickAngle = kickback * 130f * Mathf.Lerp(1f, 0.4f, eased);
            Quaternion targetRotation = hipRotation
                * Quaternion.Euler(-kickAngle, 0f, reloadTilt)
                * Quaternion.Euler(swayRotation.x, swayRotation.y, 0f);

            float blend = 1f - Mathf.Exp(-20f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, blend);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, blend);
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

            if (tracer != null)
            {
                tracer.enabled = false;
            }
        }
    }
}
