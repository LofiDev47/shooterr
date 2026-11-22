using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Plays The Warden's authored clips. Unlike the grunts (six joints posed by
    /// sines in <see cref="EnemyAnimator"/>), the boss is keyframed in Blender:
    /// contact, anticipation, impact and recovery poses, exported as one legacy
    /// clip per action. This component adds those clips to the imported rig's
    /// <see cref="Animation"/> and crossfades between locomotion and actions.
    /// </summary>
    /// <remarks>
    /// The <see cref="Animation"/> component lives on the imported FBX root, because
    /// legacy clips bind by path relative to that root ("Torso/ArmL/ElbowL").
    /// </remarks>
    public sealed class BossAnimator : MonoBehaviour
    {
        public const string IdleClip = "Idle";
        public const string WalkClip = "Walk";
        public const string SlamClip = "Slam";
        public const string VolleyClip = "Volley";
        public const string RoarClip = "Roar";
        public const string HitClip = "Hit";
        public const string DeathClip = "Death";

        [Header("Clips (wired by the scene builder)")]
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip slam;
        public AnimationClip volley;
        public AnimationClip roar;
        public AnimationClip hit;
        public AnimationClip death;

        [Header("Blending")]
        [Tooltip("Crossfade seconds between idle and walk.")]
        [SerializeField] private float locomotionFade = 0.35f;
        [Tooltip("Crossfade seconds into an action clip.")]
        [SerializeField] private float actionFade = 0.12f;
        [Tooltip("Crossfade seconds out of an action clip.")]
        [SerializeField] private float recoverFade = 0.22f;

        private Animation animation;
        private string locomotionClip = IdleClip;
        private string activeAction;
        private bool resumeLocomotion;
        private float actionEndsAt = -1f;
        private float planarSpeed;
        private float referenceSpeed = 2.4f;
        private float walkStride;
        private AnimationState walkState;
        private bool grounded = true;

        /// <summary>True while a one-shot action clip is playing.</summary>
        public bool IsActionPlaying => activeAction != null && Time.time < actionEndsAt;

        /// <summary>Name of the clip currently driving the rig, exposed for tests.</summary>
        public string CurrentClip => activeAction ?? locomotionClip;

        /// <summary>
        /// True when the Animation component is actually playing the locomotion clip,
        /// not merely holding it as the intended one. PlayAction fades with StopAll, so
        /// after an action the locomotion state stays disabled until something fades it
        /// back in — CurrentClip cannot see that.
        /// </summary>
        public bool IsLocomotionPlaying
        {
            get
            {
                if (animation == null || locomotionClip == null)
                {
                    return false;
                }

                return animation.IsPlaying(locomotionClip);
            }
        }

        /// <summary>True when all seven clips were found and added to the Animation.</summary>
        public bool HasClips => animation != null && animation.GetClipCount() >= 7;

        private void Awake()
        {
            animation = GetComponentInChildren<Animation>();

            if (animation == null)
            {
                Debug.LogWarning($"[Boss] {name} has no Animation component on its rig; clips cannot play.");
                return;
            }

            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;

            Register(IdleClip, idle, true);
            Register(WalkClip, walk, true);
            Register(SlamClip, slam, false);
            Register(VolleyClip, volley, false);
            Register(RoarClip, roar, false);
            Register(HitClip, hit, false);
            Register(DeathClip, death, false);
            walkStride = EnemyAnimator.ReadStride(animation.transform, transform);
            walkState = animation[WalkClip];

            if (animation[IdleClip] != null)
            {
                animation.clip = idle;
                animation.Play(IdleClip, PlayMode.StopAll);
            }
        }

        private void Register(string name, AnimationClip clip, bool loop)
        {
            if (clip == null)
            {
                return;
            }

            // The prefab ships with its clips already attached (the scene builder
            // bakes them in); this only fills in a rig that was assembled by hand.
            if (animation[name] == null)
            {
                animation.AddClip(clip, name);
            }

            AnimationState state = animation[name];

            if (state != null)
            {
                state.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
                state.speed = 1f;
                state.weight = 1f;
            }
        }

        /// <summary>Called every frame by the controller with the current planar speed.</summary>
        public void SetLocomotion(float speed, float maxSpeed, bool onGround)
        {
            planarSpeed = speed;
            referenceSpeed = Mathf.Max(0.1f, maxSpeed);
            grounded = onGround;
        }

        public void PlaySlam() => PlayAction(SlamClip);

        public void PlayVolley() => PlayAction(VolleyClip);

        public void PlayRoar() => PlayAction(RoarClip);

        /// <summary>
        /// Short recoil. Ignored mid-action so a scripted attack is never cut off;
        /// damage feedback still reads through the health flash.
        /// </summary>
        public void PlayHit()
        {
            if (IsActionPlaying)
            {
                return;
            }

            PlayAction(HitClip);
        }

        public void PlayDeath()
        {
            if (animation == null || animation[DeathClip] == null)
            {
                return;
            }

            activeAction = DeathClip;
            actionEndsAt = float.PositiveInfinity;
            animation.CrossFade(DeathClip, actionFade, PlayMode.StopAll);
        }

        private void PlayAction(string name)
        {
            if (animation == null || animation[name] == null)
            {
                return;
            }

            AnimationState state = animation[name];
            activeAction = name;

            // Hold the last pose until the controller asks for locomotion again,
            // so a timed attack and its clip can never disagree by a frame.
            actionEndsAt = Time.time + state.length / Mathf.Max(0.05f, state.speed);
            animation.CrossFade(name, actionFade, PlayMode.StopAll);
        }

        private void Update()
        {
            if (animation == null)
            {
                return;
            }

            if (activeAction != null)
            {
                if (Time.time < actionEndsAt)
                {
                    return;
                }

                // PlayAction crossfaded with StopAll, so the locomotion loop is
                // stopped and the rig is holding the action clip's final pose.
                // Force one fade back, even when the wanted clip is unchanged.
                activeAction = null;
                resumeLocomotion = true;
            }

            // Locomotion: walk when actually covering ground, idle otherwise.
            // The threshold is low because the boss lumbers rather than jogs.
            string wanted = planarSpeed > 0.25f && grounded ? WalkClip : IdleClip;
            float fade = resumeLocomotion ? recoverFade : locomotionFade;

            if (resumeLocomotion || wanted != locomotionClip)
            {
                resumeLocomotion = false;
                locomotionClip = wanted;
                animation.CrossFade(wanted, fade, PlayMode.StopAll);
                return;
            }

            // Speed-matched playback keeps a slow walk from looking like a slide.
            if (wanted == WalkClip && walkState != null)
            {
                float pace = walkStride > 0f
                    ? EnemyAnimator.StridePlaybackSpeed(walkState, planarSpeed, walkStride * transform.lossyScale.y)
                    : Mathf.Clamp(planarSpeed / referenceSpeed, 0.55f, 1.35f);
                walkState.speed = Mathf.Lerp(walkState.speed, pace, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
        }
    }
}
