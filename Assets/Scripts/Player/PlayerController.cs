using Shooter.Managers;
using Shooter.Upgrades;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shooter.Player
{
    /// <summary>
    /// First-person controller built on <see cref="CharacterController"/>.
    /// Handles WASD movement, sprint, jump (coyote time + input buffer + jump cut),
    /// crouch-sliding, aim-down-sights, mouse look, recoil and camera feel (bob, FOV).
    /// Input comes from the Input System package.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Speeds")]
        [SerializeField] private float walkSpeed = 5.2f;
        [SerializeField] private float sprintSpeed = 8.8f;
        [Tooltip("Movement multiplier while aiming down sights.")]
        [SerializeField] private float aimSpeedMultiplier = 0.65f;

        [Header("Acceleration")]
        [SerializeField] private float groundAcceleration = 55f;
        [SerializeField] private float groundDeceleration = 70f;
        [SerializeField] private float airAcceleration = 14f;

        [Header("Jump")]
        [Tooltip("How high a jump reaches in metres. 2 m lets you land on the arena's platforms.")]
        [SerializeField] private float jumpHeight = 2f;
        [Tooltip("Downward acceleration. -30 is the snappy FPS feel (Source-like): ~0.73 s of air time.")]
        [SerializeField] private float gravity = -30f;
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires.")]
        [SerializeField] private float jumpBufferTime = 0.12f;
        [Tooltip("Vertical velocity multiplier when jump is released early.")]
        [Range(0f, 1f)]
        [SerializeField] private float jumpCutMultiplier = 0.45f;

        [Header("Wall movement")]
        [SerializeField] private float wallClimbSpeed = 5.2f;
        [SerializeField] private float wallClimbDuration = 0.68f;
        [SerializeField] private float wallJumpPush = 7f;
        [SerializeField] private float wallJumpSteerDelay = 0.14f;
        [SerializeField] private float wallReach = 0.20f;

        [Header("Slide")]
        [SerializeField] private float slideSpeedMultiplier = 1.4f;
        [SerializeField] private float slideFriction = 4.5f;
        [SerializeField] private float slideDuration = 1.1f;
        [SerializeField] private float slideCooldown = 0.6f;
        [Tooltip("How quickly a slide can be steered, in degrees per second.")]
        [SerializeField] private float slideSteerRate = 55f;
        [SerializeField] private float slideHeight = 1.1f;
        [SerializeField] private float standHeight = 1.8f;

        [Header("Mouse look")]
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float aimSensitivityMultiplier = 0.6f;
        [SerializeField] private float pitchLimit = 85f;

        [Header("Field of view")]
        [SerializeField] private float baseFov = 75f;
        [SerializeField] private float sprintFov = 80f;
        [SerializeField] private float aimFov = 55f;
        [SerializeField] private float fovBlendSpeed = 10f;

        [Header("Camera feel")]
        [SerializeField] private float bobFrequency = 2.1f;
        [SerializeField] private float bobAmplitude = 0.035f;
        [SerializeField] private float landingDip = 0.09f;

        [Header("Camera roll")]
        [Tooltip("Degrees the camera tilts into a strafe at full speed.")]
        [SerializeField] private float strafeRoll = 1.4f;
        [Tooltip("Degrees of roll per pixel of mouse turn, used for a subtle lean.")]
        [SerializeField] private float turnRollScale = 0.006f;
        [Tooltip("How quickly the roll settles, per second.")]
        [SerializeField] private float rollDamping = 9f;
        [SerializeField] private float maxRoll = 2.5f;

        [Header("Landing spring")]
        [SerializeField] private float landingStiffness = 140f;
        [SerializeField] private float landingSpringDamping = 12f;

        [Header("Recoil")]
        [Tooltip("Degrees per second the camera returns to its resting pitch after a shot.")]
        [SerializeField] private float recoilRecovery = 22f;

        [Header("Screen shake")]
        [Tooltip("Degrees per second the shake settles.")]
        [SerializeField] private float shakeDecay = 4f;
        [SerializeField] private float maxShake = 2.5f;
        [Tooltip("Damage needed to produce a full-strength shake.")]
        [SerializeField] private float damageShakeScale = 0.045f;

        [Header("References (wired by the scene builder)")]
        public Transform cameraPivot;
        public Camera playerCamera;

        /// <summary>When false the player cannot move, look, aim or shoot (used on game over).</summary>
        public bool ControlsEnabled { get; set; } = true;

        /// <summary>True while sprinting at full speed on the ground.</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>True while the right mouse button is held and aiming is allowed.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>True during a crouch-slide.</summary>
        public bool IsSliding => isSliding;

        /// <summary>True when the cursor is locked and shooting is allowed.</summary>
        public bool CanFireNow =>
            ControlsEnabled && cursorLocked && Time.time >= suppressFireUntil;

        private CharacterController controller;
        private PlayerHealth health;
        private float shakeAmount;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float yaw;
        private float pitch;
        private float recoilPitch;
        private float recoilYaw;
        private float lastGroundedTime = -999f;
        private float jumpBufferedUntil = -1f;
        private float suppressFireUntil;
        private bool cursorLocked;
        private bool wasGrounded = true;
        private float climbTime;
        private float wallSteerUntil;
        private Vector3 lastKickedWall;

        // Camera feel state.
        private float bobTime;
        private float bobBlend;          // damped 0..1 multiplier so bob fades in and out
        private float landingOffset;
        private float landingVelocity;   // spring velocity for the landing dip
        private float rollAmount;        // current camera roll in degrees
        private Vector2 smoothedLookDelta; // damped mouse delta, also feeds weapon sway

        // Slide state.
        private bool isSliding;
        private Vector3 slideDirection;
        private float slideSpeed;
        private float slideEndTime;
        private float lastSlideEndTime = -999f;

        /// <summary>Damped mouse movement in pixels per frame; the weapon sways with it.</summary>
        public Vector2 SmoothedLookDelta => smoothedLookDelta;

        /// <summary>Current planar velocity, exposed for weapon sway and HUD spread.</summary>
        public Vector3 Velocity => planarVelocity;

        /// <summary>Top sprint speed with augments applied, used to normalise speed-based effects.</summary>
        public float SprintSpeed => sprintSpeed *
            (UpgradeManager.Instance != null ? UpgradeManager.Instance.SpeedMultiplier : 1f);

        /// <summary>True when the capsule is resting on the ground (used by the air Hunter card).</summary>
        public bool IsGrounded => controller != null && controller.isGrounded;

        /// <summary>Clears the slide cooldown immediately (Chain Slide augment).</summary>
        public void ResetSlideCooldown()
        {
            lastSlideEndTime = -999f;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<PlayerHealth>();
            yaw = transform.eulerAngles.y;

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }

            if (cameraPivot == null && playerCamera != null)
            {
                cameraPivot = playerCamera.transform;
            }
        }

        private void Start()
        {
            // GameManager starts in Menu; only lock the cursor once the run begins.
            if (GameManager.Instance != null && GameManager.Instance.IsPlaying)
            {
                ControlsEnabled = true;
                LockCursor(true);
            }
            else if (GameManager.Instance != null)
            {
                ControlsEnabled = false;
                LockCursor(false);
            }
            else
            {
                ControlsEnabled = false;
                LockCursor(false);
            }
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Damaged += HandleDamaged;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
            }
        }

        private void HandleDamaged(float amount)
        {
            AddShake(Mathf.Clamp(amount * damageShakeScale, 0.15f, 1.1f));
        }

        /// <summary>Adds a short camera shake. Strength is in degrees.</summary>
        public void AddShake(float strength)
        {
            if (SettingsManager.Instance != null && !SettingsManager.Instance.ShakeEnabled)
            {
                return;
            }

            shakeAmount = Mathf.Min(maxShake, Mathf.Max(shakeAmount, strength));
        }

        /// <summary>
        /// Shoves the player a fixed distance without touching their input, used by
        /// the boss slam. Vertical motion is clamped upward so a slam can never
        /// drive the player through the floor.
        /// </summary>
        public void ApplyKnockback(Vector3 impulse)
        {
            if (controller == null || !controller.enabled)
            {
                return;
            }

            impulse.y = Mathf.Max(0f, impulse.y);
            controller.Move(impulse + Vector3.up * 0.15f);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (ControlsEnabled)
            {
                HandleCursor();
                ReadAim();
                ReadLook(deltaTime);
                ReadMovement(deltaTime);
            }
            else
            {
                IsAiming = false;
                IsSprinting = false;
                planarVelocity = Vector3.MoveTowards(planarVelocity, Vector3.zero, groundDeceleration * deltaTime);

                // Let the mouse sway settle when the controls are off.
                float settle = 1f - Mathf.Exp(-18f * deltaTime);
                smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, Vector2.zero, settle);
            }

            RecoverRecoil();
            UpdateCameraRig(deltaTime);
            ReportMovementToAugments();
        }

        /// <summary>
        /// Feeds actual horizontal speed to the movement-gated cards (Kinetic
        /// Rounds). It reads velocity, not the speed stats, so standing still is
        /// worth nothing no matter how many speed cards are owned.
        /// </summary>
        private void ReportMovementToAugments()
        {
            UpgradeManager upgrades = UpgradeManager.Instance;

            if (upgrades == null)
            {
                return;
            }

            float speed = new Vector2(planarVelocity.x, planarVelocity.z).magnitude;
            upgrades.SetMovementFactor(speed / Mathf.Max(0.01f, sprintSpeed));
        }

        /// <summary>Locks or releases the mouse cursor. Releasing also blocks firing briefly.</summary>
        public void LockCursor(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;

            if (locked)
            {
                // Ignore the click that re-locks the cursor so it does not fire a shot.
                suppressFireUntil = Time.time + 0.15f;
            }
        }

        /// <summary>Adds a camera kick. Positive pitch pushes the view upwards.</summary>
        public void AddRecoil(float pitchKick, float yawKick)
        {
            recoilPitch -= pitchKick;
            recoilYaw += yawKick;
        }

        private void HandleCursor()
        {
            bool playing = GameManager.Instance != null && GameManager.Instance.IsPlaying;

            if (!playing)
            {
                return;
            }

            // Escape now pauses via GameManager; do not just unlock the cursor here.
            if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame))
            {
                GameManager.Instance?.Pause();
            }
            else if (!cursorLocked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                LockCursor(true);
            }
        }

        private void ReadAim()
        {
            bool wantsAim = cursorLocked && Mouse.current != null && Mouse.current.rightButton.isPressed;
            IsAiming = wantsAim && !isSliding;

            if (IsAiming)
            {
                IsSprinting = false;
            }
        }

        private void ReadLook(float deltaTime)
        {
            Vector2 delta = Vector2.zero;

            if (Mouse.current != null && cursorLocked)
            {
                delta = Mouse.current.delta.ReadValue();
                float baseSensitivity = SettingsManager.Instance != null ? SettingsManager.Instance.Sensitivity : lookSensitivity;
                float sensitivity = baseSensitivity * (IsAiming ? aimSensitivityMultiplier : 1f);
                float pitchDelta = delta.y * sensitivity;

                if (SettingsManager.Instance != null && SettingsManager.Instance.InvertY)
                {
                    pitchDelta = -pitchDelta;
                }

                yaw += delta.x * sensitivity;
                pitch = Mathf.Clamp(pitch - pitchDelta, -pitchLimit, pitchLimit);

                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            // A damped copy of the raw delta: fast enough to never feel laggy, but with
            // enough softness that the weapon sway and camera roll breathe instead of
            // snapping. This decays to zero the moment the mouse stops.
            float blend = 1f - Mathf.Exp(-18f * deltaTime);
            smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, delta, blend);
        }

        private void ReadMovement(float deltaTime)
        {
            Vector2 input = Vector2.zero;
            bool sprintHeld = false;
            bool crouchHeld = false;
            bool jumpHeld = false;
            bool jumpPressed = false;

            SettingsManager settings = SettingsManager.Instance;

            if (Keyboard.current != null)
            {
                if (IsHeld(settings, SettingsManager.ActionForward)) input.y += 1f;
                if (IsHeld(settings, SettingsManager.ActionBackward)) input.y -= 1f;
                if (IsHeld(settings, SettingsManager.ActionStrafeRight)) input.x += 1f;
                if (IsHeld(settings, SettingsManager.ActionStrafeLeft)) input.x -= 1f;

                if (input.sqrMagnitude > 1f)
                {
                    input.Normalize();
                }

                sprintHeld = IsHeld(settings, SettingsManager.ActionSprint);
                crouchHeld = IsHeld(settings, SettingsManager.ActionSlide);
                jumpHeld = IsHeld(settings, SettingsManager.ActionJump);
                jumpPressed = WasPressed(settings, SettingsManager.ActionJump);

                if (jumpPressed)
                {
                    jumpBufferedUntil = Time.time + jumpBufferTime;
                }

                if (WasReleased(settings, SettingsManager.ActionJump) && verticalVelocity > 0f)
                {
                    verticalVelocity *= jumpCutMultiplier;
                }
            }

            IsSprinting = sprintHeld && input.y > 0.1f && !IsAiming && !isSliding && controller.isGrounded;

            TryStartSlide(input, crouchHeld);

            if (isSliding)
            {
                UpdateSlide(input, crouchHeld, deltaTime);
            }
            else if (Time.time >= wallSteerUntil)
            {
                UpdateWalk(input, deltaTime);
            }

            bool climbed = UpdateWallMovement(input, jumpPressed, jumpHeld, deltaTime);
            if (!climbed)
                UpdateJump();

            // Gravity and ground contact. Ground state here is from the PREVIOUS
            // Move, so it must not be used to re-arm the coyote window or reset
            // the airborne timer: on a take-off frame it is still true and would
            // hand out a free second jump while instantly cancelling Sky Hunter.
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f; // Keeps the controller glued to slopes.
            }

            float fallSpeed = verticalVelocity;
            if (!climbed)
                verticalVelocity += gravity * deltaTime;

            Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * deltaTime);

            bool groundedNow = controller.isGrounded;

            // Only the post-Move contact state is authoritative. Left-ground arms
            // the airborne timer exactly once; landing clears it and restores air
            // jumps. Air jumps keep wasGrounded == false, so they never re-arm it.
            if (groundedNow)
            {
                lastGroundedTime = Time.time;
                climbTime = 0f;
                wallSteerUntil = 0f;
                lastKickedWall = Vector3.zero;
                UpgradeManager.Instance?.ResetAirJumps();
            }

            if (JustLanded(wasGrounded, groundedNow))
            {
                UpgradeManager.Instance?.OnLanded();
            }
            else if (JustLeftGround(wasGrounded, groundedNow))
            {
                UpgradeManager.Instance?.OnJumpPressed();
            }

            if (groundedNow && !wasGrounded && fallSpeed < -6f)
            {
                // A spring instead of a fixed offset: the camera dips and settles with a
                // hint of bounce, scaled by how hard we hit the ground.
                landingVelocity = fallSpeed * 0.09f;
            }

            wasGrounded = groundedNow;
        }

        private void UpdateWalk(Vector2 input, float deltaTime)
        {
            float speedScale = UpgradeManager.Instance != null ? UpgradeManager.Instance.SpeedMultiplier : 1f;
            float targetSpeed = (IsAiming
                ? walkSpeed * aimSpeedMultiplier
                : (IsSprinting ? sprintSpeed : walkSpeed)) * speedScale;

            Vector3 wish = transform.right * input.x + transform.forward * input.y;

            if (wish.sqrMagnitude > 1f)
            {
                wish.Normalize();
            }

            Vector3 targetVelocity = wish * targetSpeed;

            float acceleration = controller.isGrounded
                ? (targetVelocity.sqrMagnitude >= planarVelocity.sqrMagnitude
                    ? groundAcceleration
                    : groundDeceleration)
                : airAcceleration;

            planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity, acceleration * deltaTime);
        }

        private void TryStartSlide(Vector2 input, bool crouchHeld)
        {
            if (isSliding || !crouchHeld || !controller.isGrounded || IsAiming)
            {
                return;
            }

            UpgradeManager upgrades = UpgradeManager.Instance;
            float cooldownScale = upgrades != null ? upgrades.SlideCooldownMultiplier : 1f;

            if (Time.time < lastSlideEndTime + slideCooldown * cooldownScale)
            {
                return;
            }

            // Only slide with real momentum: either sprinting, or already moving fast.
            bool hasMomentum = IsSprinting || planarVelocity.magnitude > walkSpeed * 1.05f;

            if (!hasMomentum)
            {
                return;
            }

            Vector3 direction = planarVelocity.sqrMagnitude > 0.01f
                ? planarVelocity
                : transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.01f)
            {
                direction = transform.forward;
            }

            slideDirection = direction.normalized;
            slideSpeed = Mathf.Max(planarVelocity.magnitude, sprintSpeed) * slideSpeedMultiplier *
                (upgrades != null ? upgrades.SlideSpeedMultiplier : 1f);
            slideEndTime = Time.time + slideDuration;
            isSliding = true;
            IsSprinting = false;

            _ = input; // Steering is read every slide frame, not just on start.
        }

        private void UpdateSlide(Vector2 input, bool crouchHeld, float deltaTime)
        {
            // Light steering keeps the slide committed but not rigid.
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 side = transform.right * input.x;
                Vector3 steerTarget = (slideDirection + side * 0.6f).normalized;
                float maxRadians = slideSteerRate * Mathf.Deg2Rad * deltaTime;
                slideDirection = Vector3.RotateTowards(slideDirection, steerTarget, maxRadians, 0f);
            }

            slideSpeed = Mathf.MoveTowards(slideSpeed, 0f, slideFriction * deltaTime);
            planarVelocity = slideDirection * slideSpeed;

            // Releasing crouch after a short commitment ends the slide early.
            bool releasedCrouch = !crouchHeld && Time.time > slideEndTime - slideDuration + 0.15f;
            bool spent = slideSpeed <= walkSpeed * 0.75f || Time.time >= slideEndTime;

            if (releasedCrouch || spent)
            {
                EndSlide();
            }
        }

        private void EndSlide()
        {
            if (!isSliding)
            {
                return;
            }

            isSliding = false;
            lastSlideEndTime = Time.time;
            // Momentum carries over: planarVelocity keeps the slide's direction and speed.
        }

        /// <summary>True on the frame ground contact is lost (jump or ledge drop).</summary>
        public static bool JustLeftGround(bool wasGrounded, bool groundedNow) => wasGrounded && !groundedNow;

        /// <summary>True on the frame the player touches down.</summary>
        public static bool JustLanded(bool wasGrounded, bool groundedNow) => !wasGrounded && groundedNow;

        /// <summary>
        /// Coyote check. A jump writes a negative sentinel to lastGroundedTime, so
        /// the window cannot be re-armed until a real post-Move landing occurs.
        /// </summary>
        public static bool CanGroundedJump(float now, float lastGroundedTime, float coyoteTime) =>
            lastGroundedTime > -1e8f && now - lastGroundedTime <= coyoteTime;

        private void UpdateJump()
        {
            if (jumpBufferedUntil < 0f || Time.time > jumpBufferedUntil)
            {
                return;
            }

            UpgradeManager upgrades = UpgradeManager.Instance;
            float heightScale = upgrades != null ? upgrades.JumpMultiplier : 1f;
            bool groundedJump = CanGroundedJump(Time.time, lastGroundedTime, coyoteTime);

            if (!groundedJump && !(upgrades != null && upgrades.TryConsumeAirJump()))
            {
                return; // No coyote window and no air jump available.
            }

            if (isSliding)
            {
                EndSlide(); // Slide-jumping keeps the slide momentum.
            }

            verticalVelocity = Mathf.Sqrt(jumpHeight * heightScale * -2f * gravity);
            lastGroundedTime = -999f;
            jumpBufferedUntil = -1f;
        }

        private bool UpdateWallMovement(Vector2 input, bool jumpPressed, bool jumpHeld, float deltaTime)
        {
            if (controller.isGrounded || isSliding || Time.time < wallSteerUntil ||
                (!jumpPressed && (!jumpHeld || input.y <= 0.1f)))
                return false;

            UpgradeManager upgrades = UpgradeManager.Instance;
            float climbLimit = wallClimbDuration * (upgrades != null ? upgrades.WallClimbMultiplier : 1f);
            if (!jumpPressed && climbTime >= climbLimit)
                return false;

            Vector3 forward = transform.forward;
            if (!ProbeWall(forward, out Vector3 normal) &&
                (!jumpPressed || (!ProbeWall(transform.right, out normal) &&
                    !ProbeWall(-transform.right, out normal) && !ProbeWall(-forward, out normal))))
                return false;

            bool sameWall = Vector3.Dot(lastKickedWall, normal) > 0.7f;
            if (jumpPressed && !sameWall)
            {
                float push = wallJumpPush * (upgrades != null ? upgrades.WallKickMultiplier : 1f);
                planarVelocity = Vector3.ProjectOnPlane(planarVelocity, normal) * 0.55f + normal * push;
                float heightScale = upgrades != null ? upgrades.JumpMultiplier : 1f;
                verticalVelocity = Mathf.Sqrt(jumpHeight * heightScale * -2f * gravity);
                wallSteerUntil = Time.time + wallJumpSteerDelay;
                lastKickedWall = normal;
                jumpBufferedUntil = -1f;
                lastGroundedTime = -999f;
                return false;
            }

            if (sameWall || !jumpHeld || climbTime >= climbLimit ||
                input.y <= 0.1f || Vector3.Dot(forward, normal) > -0.45f)
                return false;

            climbTime += deltaTime;
            verticalVelocity = wallClimbSpeed;
            planarVelocity = Vector3.ProjectOnPlane(planarVelocity, normal) * 0.4f - normal * 1.5f;
            return true;
        }

        private bool ProbeWall(Vector3 direction, out Vector3 normal)
        {
            Vector3 center = transform.TransformPoint(controller.center);
            const float probeRadius = 0.10f;
            float distance = controller.radius + wallReach - probeRadius;
            // At the lower probe, climbing continues until the feet reach the lip.
            Vector3 lower = center - Vector3.up * (controller.height * 0.5f - 0.20f);
            return TryWallCast(center, direction, probeRadius, distance, out normal) ||
                TryWallCast(lower, direction, probeRadius, distance, out normal);
        }

        private static bool TryWallCast(Vector3 center, Vector3 direction, float radius, float distance, out Vector3 normal)
        {
            const int ignoreRaycast = 1 << 2;
            if (Physics.SphereCast(center, radius, direction, out RaycastHit hit, distance,
                ~ignoreRaycast, QueryTriggerInteraction.Ignore) &&
                (hit.collider is BoxCollider || hit.collider is MeshCollider) && Mathf.Abs(hit.normal.y) < 0.25f)
            {
                normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                return true;
            }

            normal = Vector3.zero;
            return false;
        }

        private void RecoverRecoil()
        {
            float deltaTime = Time.deltaTime;

            // Exponential recovery: each frame the remaining recoil shrinks by a fixed
            // fraction, which settles smoothly instead of at a constant clip.
            float recovery = 1f - Mathf.Exp(-recoilRecovery * deltaTime);
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, recovery);
            recoilYaw = Mathf.Lerp(recoilYaw, 0f, recovery);

            float shakeRecovery = 1f - Mathf.Exp(-shakeDecay * 2f * Time.unscaledDeltaTime);
            shakeAmount = Mathf.Lerp(shakeAmount, 0f, shakeRecovery);

            UpdateRoll(deltaTime);
            ApplyCameraRotation();
        }

        /// <summary>
        /// Tilts the camera into strafes and fast turns. Exponentially damped so the
        /// lean eases in and out rather than snapping with velocity.
        /// </summary>
        private void UpdateRoll(float deltaTime)
        {
            float lateralSpeed = Vector3.Dot(planarVelocity, transform.right);
            float speedRatio = Mathf.Clamp01(Mathf.Abs(lateralSpeed) / sprintSpeed);

            float target = -Mathf.Sign(lateralSpeed) * speedRatio * strafeRoll * (IsAiming ? 0.4f : 1f);

            // A fast mouse flick leans the camera a touch further.
            target += Mathf.Clamp(-smoothedLookDelta.x * turnRollScale, -1.5f, 1.5f);
            target = Mathf.Clamp(target, -maxRoll, maxRoll);

            float blend = 1f - Mathf.Exp(-rollDamping * deltaTime);
            rollAmount = Mathf.Lerp(rollAmount, target, blend);
        }

        private void ApplyCameraRotation()
        {
            if (cameraPivot == null)
            {
                return;
            }

            float totalPitch = Mathf.Clamp(pitch + recoilPitch, -pitchLimit, pitchLimit);
            Quaternion look = Quaternion.Euler(totalPitch, recoilYaw, rollAmount);

            if (shakeAmount > 0.001f)
            {
                // Perlin noise gives a smoother shake than raw random offsets.
                float t = Time.unscaledTime * 24f;
                float offsetX = (Mathf.PerlinNoise(t, 0.37f) * 2f - 1f) * shakeAmount;
                float offsetY = (Mathf.PerlinNoise(t, 4.11f) * 2f - 1f) * shakeAmount;
                float offsetZ = (Mathf.PerlinNoise(t, 7.53f) * 2f - 1f) * shakeAmount * 0.5f;
                look *= Quaternion.Euler(offsetX, offsetY, offsetZ);
            }

            cameraPivot.localRotation = look;
        }

        private void UpdateCameraRig(float deltaTime)
        {
            // --- FOV: wider when sprinting or sliding, narrower when aiming.
            if (playerCamera != null)
            {
                float effectiveBaseFov = SettingsManager.Instance != null ? SettingsManager.Instance.FieldOfView : baseFov;
                float effectiveSprintFov = effectiveBaseFov + (sprintFov - baseFov);
                float effectiveAimFov = effectiveBaseFov + (aimFov - baseFov);
                float targetFov = effectiveBaseFov;

                if (IsAiming)
                {
                    targetFov = effectiveAimFov;
                }
                else if (isSliding)
                {
                    targetFov = effectiveSprintFov + 4f;
                }
                else if (IsSprinting)
                {
                    targetFov = effectiveSprintFov;
                }

                float blend = 1f - Mathf.Exp(-fovBlendSpeed * deltaTime);
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, blend);
            }

            // --- Crouch height while sliding.
            float targetHeight = isSliding ? slideHeight : standHeight;

            if (!Mathf.Approximately(controller.height, targetHeight))
            {
                controller.height = Mathf.MoveTowards(controller.height, targetHeight, 6f * deltaTime);
                controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
            }

            // --- Head bob: scaled by speed, damped while aiming or sliding.
            float horizontalSpeed = new Vector2(planarVelocity.x, planarVelocity.z).magnitude;
            float speedRatio = Mathf.Clamp01(horizontalSpeed / sprintSpeed);

            bool bobbing = controller.isGrounded && speedRatio > 0.1f;
            float damping = IsAiming ? 0.3f : (isSliding ? 0.4f : 1f);

            if (bobbing)
            {
                bobTime += deltaTime * bobFrequency * Mathf.PI * 2f * Mathf.Lerp(0.75f, 1.35f, speedRatio);
            }

            // Fade the bob in and out instead of switching it off: stopping used to snap
            // the camera back to centre in a single frame.
            float bobTarget = bobbing ? 1f : 0f;
            bobBlend = Mathf.Lerp(bobBlend, bobTarget, 1f - Mathf.Exp(-8f * deltaTime));

            float bobAmount = bobAmplitude * speedRatio * damping * bobBlend;
            float bobX = Mathf.Cos(bobTime) * bobAmount * 0.6f;
            float bobY = Mathf.Sin(bobTime * 2f) * bobAmount;

            // Landing dip: an underdamped spring, so the camera dips, recovers and
            // overshoots a hair before settling.
            float springAccel = -landingStiffness * landingOffset - landingSpringDamping * landingVelocity;
            landingVelocity += springAccel * deltaTime;
            landingOffset = Mathf.Clamp(landingOffset + landingVelocity * deltaTime, -0.02f, landingDip);

            if (cameraPivot != null)
            {
                float baseHeight = controller.height - 0.18f;
                cameraPivot.localPosition = new Vector3(bobX, baseHeight + bobY - landingOffset, 0f);
            }
        }

        private static bool IsHeld(SettingsManager settings, string action)
        {
            if (settings == null)
            {
                return false;
            }

            Key key = settings.GetKey(action);

            if (key == Key.None || Keyboard.current == null)
            {
                return false;
            }

            return Keyboard.current[key].isPressed;
        }

        private static bool WasPressed(SettingsManager settings, string action)
        {
            if (settings == null)
            {
                return false;
            }

            Key key = settings.GetKey(action);

            if (key == Key.None || Keyboard.current == null)
            {
                return false;
            }

            return Keyboard.current[key].wasPressedThisFrame;
        }

        private static bool WasReleased(SettingsManager settings, string action)
        {
            if (settings == null)
            {
                return false;
            }

            Key key = settings.GetKey(action);

            if (key == Key.None || Keyboard.current == null)
            {
                return false;
            }

            return Keyboard.current[key].wasReleasedThisFrame;
        }
    }
}
