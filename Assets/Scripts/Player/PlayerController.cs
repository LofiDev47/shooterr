using UnityEngine;
using UnityEngine.InputSystem;

namespace Shooter.Player
{
    /// <summary>
    /// First-person controller built on <see cref="CharacterController"/>.
    /// Handles WASD movement, sprint, jump, mouse look and camera recoil.
    /// Input comes from the Input System package (the project is configured for it).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8.5f;
        [SerializeField] private float groundAcceleration = 20f;
        [SerializeField] private float airAcceleration = 6f;
        [SerializeField] private float jumpHeight = 1.15f;
        [SerializeField] private float gravity = -22f;
        [SerializeField] private float coyoteTime = 0.12f;

        [Header("Mouse look")]
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float pitchLimit = 85f;

        [Header("Recoil")]
        [Tooltip("Degrees per second the camera returns to its resting pitch after a shot.")]
        [SerializeField] private float recoilRecovery = 22f;

        [Header("References (wired by the scene builder)")]
        public Transform cameraPivot;
        public Camera playerCamera;

        /// <summary>When false the player cannot move, look or shoot (used on game over).</summary>
        public bool ControlsEnabled { get; set; } = true;

        /// <summary>True while the player holds sprint and is moving forward.</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>True when the cursor is locked and shooting is allowed.</summary>
        public bool CanFireNow =>
            ControlsEnabled && cursorLocked && Time.time >= suppressFireUntil;

        private CharacterController controller;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float yaw;
        private float pitch;
        private float recoilPitch;
        private float recoilYaw;
        private float lastGroundedTime = -999f;
        private float suppressFireUntil;
        private bool cursorLocked;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
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

        private void Update()
        {
            if (ControlsEnabled)
            {
                HandleCursor();
                ReadLook();
                ReadMovement();
            }

            RecoverRecoil();
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

        private void ReadLook()
        {
            if (Mouse.current == null || !cursorLocked)
            {
                return;
            }

            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            ApplyCameraRotation();
        }

        private void ReadMovement()
        {
            Vector2 input = Vector2.zero;

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

                IsSprinting = Keyboard.current.leftShiftKey.isPressed && input.y > 0.1f;

                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    TryJump();
                }
            }

            float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 wishDirection = transform.right * input.x + transform.forward * input.y;
            Vector3 targetVelocity = wishDirection * targetSpeed;

            float acceleration = controller.isGrounded ? groundAcceleration : airAcceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity, acceleration * Time.deltaTime);

            if (controller.isGrounded)
            {
                lastGroundedTime = Time.time;

                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2f; // Small downward push keeps the controller glued to slopes.
                }
            }

            verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * Time.deltaTime);
        }

        private void TryJump()
        {
            if (Time.time - lastGroundedTime > coyoteTime)
            {
                return;
            }

            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            lastGroundedTime = -999f; // Prevent a second jump from the same ground contact.
        }

        private void RecoverRecoil()
        {
            recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, recoilRecovery * Time.deltaTime);
            recoilYaw = Mathf.MoveTowards(recoilYaw, 0f, recoilRecovery * Time.deltaTime);
            ApplyCameraRotation();
        }

        private void ApplyCameraRotation()
        {
            if (cameraPivot == null)
            {
                return;
            }

            float totalPitch = Mathf.Clamp(pitch + recoilPitch, -pitchLimit, pitchLimit);
            cameraPivot.localRotation = Quaternion.Euler(totalPitch, recoilYaw, 0f);
        }
    }
}
