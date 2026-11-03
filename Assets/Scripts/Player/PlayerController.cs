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
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires.")]
        [SerializeField] private float jumpBufferTime = 0.12f;
        [Tooltip("Vertical velocity multiplier when jump is released early.")]
        [Range(0f, 1f)]
        [SerializeField] private float jumpCutMultiplier = 0.45f;

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

        /// <summary>Top sprint speed, used to normalise speed-based effects.</summary>
        public float SprintSpeed => sprintSpeed;

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
            LockCursor(true);
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
            shakeAmount = Mathf.Min(maxShake, Mathf.Max(shakeAmount, strength));
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
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                LockCursor(false);
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
                float sensitivity = lookSensitivity * (IsAiming ? aimSensitivityMultiplier : 1f);

                yaw += delta.x * sensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * sensitivity, -pitchLimit, pitchLimit);

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

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed) input.y -= 1f;
                if (Keyboard.current.dKey.isPressed) input.x += 1f;
                if (Keyboard.current.aKey.isPressed) input.x -= 1f;

                if (input.sqrMagnitude > 1f)
                {
                    input.Normalize();
                }

                sprintHeld = Keyboard.current.leftShiftKey.isPressed;
                crouchHeld = Keyboard.current.cKey.isPressed;

                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    jumpBufferedUntil = Time.time + jumpBufferTime;
                }

                if (Keyboard.current.spaceKey.wasReleasedThisFrame && verticalVelocity > 0f)
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
            else
            {
                UpdateWalk(input, deltaTime);
            }

            UpdateJump();

            // Gravity and ground contact.
            if (controller.isGrounded)
            {
                lastGroundedTime = Time.time;

                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2f; // Keeps the controller glued to slopes.
                }
            }

            float fallSpeed = verticalVelocity;
            verticalVelocity += gravity * deltaTime;

            Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * deltaTime);

            bool groundedNow = controller.isGrounded;

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
            float targetSpeed = IsAiming
                ? walkSpeed * aimSpeedMultiplier
                : (IsSprinting ? sprintSpeed : walkSpeed);

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

            if (Time.time < lastSlideEndTime + slideCooldown)
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
            slideSpeed = Mathf.Max(planarVelocity.magnitude, sprintSpeed) * slideSpeedMultiplier;
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

        private void UpdateJump()
        {
            if (jumpBufferedUntil < 0f || Time.time > jumpBufferedUntil)
            {
                return;
            }

            bool canJump = Time.time - lastGroundedTime <= coyoteTime;

            if (!canJump)
            {
                return;
            }

            if (isSliding)
            {
                EndSlide(); // Slide-jumping keeps the slide momentum.
            }

            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastGroundedTime = -999f;
            jumpBufferedUntil = -1f;
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
                float targetFov = baseFov;

                if (IsAiming)
                {
                    targetFov = aimFov;
                }
                else if (isSliding)
                {
                    targetFov = sprintFov + 4f;
                }
                else if (IsSprinting)
                {
                    targetFov = sprintFov;
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
    }
}
