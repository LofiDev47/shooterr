using Shooter.Combat;
using Shooter.Effects;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
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
        private float kickback;
        private float aimBlend; // 0 = hip fire, 1 = fully aimed.
        private Renderer[] weaponRenderers;
        private bool renderersVisible = true;

        private void Awake()
        {
            State = new WeaponState(magazineSize, reserveAmmo, fireInterval, reloadDuration, maxReserve);
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

            bool aiming = player != null && player.IsAiming;
            Vector3 origin = aimCamera != null ? aimCamera.transform.position : transform.position;
            Vector3 direction = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            float currentSpread = spreadDegrees * (aiming ? aimSpreadMultiplier : 1f);
            direction = ApplySpread(direction, currentSpread);

            Vector3 endPoint = origin + direction * range;

            // Layer 2 ("Ignore Raycast") holds the player, so the ray cannot hit its owner.
            int mask = ~(1 << 2);

            if (Physics.Raycast(origin, direction, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
                SpawnImpact(hit.point, hit.normal);

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                if (target != null)
                {
                    target.TakeDamage(damage, hit.point);
                    hud?.ShowHitMarker();
                    AudioManager.Instance?.PlayWeaponHit(hit.point);
                }
                else if (sparkMaterial != null)
                {
                    // Ricochet sparks off geometry, thrown back along the reflected ray.
                    DebrisBurst.Spawn(hit.point, Vector3.Reflect(direction, hit.normal), sparkMaterial,
                        4, 3f, 0.035f, 0.6f);
                }
            }

            PlayMuzzleFlash();
            PlayTracer(endPoint);
            ApplyRecoil();
            PlayFireAudio();
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
            kickback = 0.07f;

            if (player != null)
            {
                float multiplier = player.IsAiming ? aimRecoilMultiplier : 1f;
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
            // Blend between hip-fire and aim-down-sights positions.
            bool wantsAim = player != null && player.IsAiming;
            float aimTarget = wantsAim ? 1f : 0f;
            aimBlend = Mathf.MoveTowards(aimBlend, aimTarget, sightBlendSpeed * Time.deltaTime);
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

            kickback = Mathf.MoveTowards(kickback, 0f, 0.6f * Time.deltaTime);

            Vector3 targetPosition = hipPosition + new Vector3(0f, reloadDip, -kickback * Mathf.Lerp(1f, 0.45f, eased));
            Quaternion targetRotation = hipRotation * Quaternion.Euler(-kickback * 180f * Mathf.Lerp(1f, 0.4f, eased), 0f, reloadTilt);

            float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, blend);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, blend);
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
