using UnityEngine;

namespace Shooter.Enemies
{
    /// <summary>
    /// Plays authored clips on the shared enemy rig. Original prefabs use their
    /// procedural joint animation when they have no imported takes.
    /// </summary>
    public sealed class EnemyAnimator : MonoBehaviour
    {
        [Header("Rig (found by name)")]
        public Transform model;
        public Transform torso;
        public Transform head;
        public Transform armL;
        public Transform armR;
        public Transform legL;
        public Transform legR;
        public Animation authored;

        [Header("Tuning")]
        [SerializeField] private float walkCycleSpeed = 8f;
        [SerializeField] private float legSwing = 28f;
        [SerializeField] private float armSwing = 18f;
        [SerializeField] private float bobHeight = 0.03f;
        [SerializeField] private float attackSwing = 55f;
        [SerializeField] private float aimArmAngle = -78f;
        [SerializeField] private float hitRecoil = 10f;

        private Quaternion torsoRest = Quaternion.identity;
        private Quaternion headRest = Quaternion.identity;
        private Quaternion armLRest = Quaternion.identity;
        private Quaternion armRRest = Quaternion.identity;
        private Quaternion legLRest = Quaternion.identity;
        private Quaternion legRRest = Quaternion.identity;
        private Vector3 modelRestPosition;

        private float walkPhase;
        private float moveBlend;
        private float targetMove;
        private float attackTimer;
        private float attackDuration;
        private float hitTimer;
        private float aimBlend;
        private float aimTarget;
        private float authoredUntil;
        private string authoredLoop;
        private bool dying;

        /// <summary>True when all six animated joints were found on the model.</summary>
        public bool HasRig =>
            torso != null && head != null && armL != null && armR != null &&
            legL != null && legR != null;

        public float MoveBlend => moveBlend;
        public bool IsAttacking => attackTimer > 0f;

        private void Awake()
        {
            if (model == null)
            {
                model = transform.Find("Model");
            }

            Transform searchRoot = model != null ? model : transform;
            torso = FindJoint(searchRoot, "Torso");
            head = FindJoint(searchRoot, "Head");
            armL = FindJoint(searchRoot, "ArmL");
            armR = FindJoint(searchRoot, "ArmR");
            legL = FindJoint(searchRoot, "LegL");
            legR = FindJoint(searchRoot, "LegR");

            torsoRest = torso != null ? torso.localRotation : Quaternion.identity;
            headRest = head != null ? head.localRotation : Quaternion.identity;
            armLRest = armL != null ? armL.localRotation : Quaternion.identity;
            armRRest = armR != null ? armR.localRotation : Quaternion.identity;
            legLRest = legL != null ? legL.localRotation : Quaternion.identity;
            legRRest = legR != null ? legR.localRotation : Quaternion.identity;
            modelRestPosition = model != null ? model.localPosition : Vector3.zero;
        }

        /// <summary>Called by EnemyAI every frame with the current planar speed.</summary>
        public void SetLocomotion(float planarSpeed, float maxSpeed)
        {
            targetMove = Mathf.Clamp01(planarSpeed / Mathf.Max(0.1f, maxSpeed));
        }

        /// <summary>Starts the arm-swing portion of a telegraphed melee attack.</summary>
        public void TriggerAttack(float windup)
        {
            attackDuration = Mathf.Max(0.05f, windup);
            attackTimer = attackDuration;
            PlayAuthored("Slam", attackDuration / (26f / 30f));
        }

        /// <summary>Short recoil on the torso and head when a shot lands.</summary>
        public void TriggerHit()
        {
            hitTimer = 0.16f;
            if (Time.time >= authoredUntil)
                PlayAuthored("Hit");
        }

        /// <summary>Marksmen raise the cannon arm while charging a shot.</summary>
        public void SetAiming(bool aiming, float windup = 0f)
        {
            if (aiming && aimTarget == 0f)
                PlayAuthored("Volley", windup / (19f / 40f));
            aimTarget = aiming ? 1f : 0f;
        }

        public void PlayCharge(float duration) => PlayAuthored("Roar", duration);

        public void PlayDeath(float lifetime)
        {
            PlayAuthored("Death", lifetime * 2.5f);
            dying = true;
        }

        private void PlayAuthored(string clip, float seconds = 0f)
        {
            if (authored == null || dying)
                return;

            AnimationState state = authored[clip];
            if (state == null)
                return;

            state.speed = seconds > 0f ? state.length / seconds : 1f;
            state.time = 0f;
            authored.Play(clip);
            authoredUntil = Time.time + state.length / state.speed;
            authoredLoop = null;
        }

        private void Update()
        {
            if (!HasRig)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            moveBlend = Mathf.Lerp(moveBlend, targetMove, 1f - Mathf.Exp(-12f * deltaTime));
            if (authored != null)
            {
                attackTimer = Mathf.Max(0f, attackTimer - deltaTime);
                if (!dying && Time.time >= authoredUntil)
                {
                    string loop = moveBlend > 0.08f ? "Walk" : "Idle";
                    if (loop != authoredLoop)
                    {
                        authored.CrossFade(loop, 0.1f);
                        authoredLoop = loop;
                    }
                }
                return;
            }
            aimBlend = Mathf.Lerp(aimBlend, aimTarget, 1f - Mathf.Exp(-8f * deltaTime));

            if (moveBlend > 0.02f)
            {
                walkPhase += walkCycleSpeed * (0.6f + moveBlend) * deltaTime;
            }

            float walk = Mathf.Sin(walkPhase) * moveBlend;
            float breathe = Mathf.Sin(Time.time * 1.6f) * 0.8f;

            float attack = 0f;

            if (attackTimer > 0f)
            {
                attackTimer = Mathf.Max(0f, attackTimer - deltaTime);
                float progress = 1f - attackTimer / attackDuration;
                attack = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            }

            if (hitTimer > 0f)
            {
                hitTimer = Mathf.Max(0f, hitTimer - deltaTime);
            }

            float hit = Mathf.Sin(hitTimer / 0.16f * Mathf.PI);

            // Legs: alternating forward/back swing around the hip pivots.
            legL.localRotation = legLRest * Quaternion.Euler(walk * legSwing, 0f, 0f);
            legR.localRotation = legRRest * Quaternion.Euler(-walk * legSwing, 0f, 0f);

            // Arms: counter-swing to the legs, plus attack raise and hit recoil.
            float armWalk = -walk * armSwing;
            float attackRaise = -attackSwing * attack;
            float aimRaise = aimArmAngle * aimBlend;

            armL.localRotation = armLRest *
                Quaternion.Euler(armWalk + attackRaise - hit * hitRecoil, 0f, 0f);
            armR.localRotation = armRRest *
                Quaternion.Euler(-armWalk + attackRaise + aimRaise - hit * hitRecoil, 0f, 0f);

            // Torso: idle breathing, a small counter-roll, and hit recoil.
            torso.localRotation = torsoRest *
                Quaternion.Euler(breathe + hit * hitRecoil * 0.6f, 0f, walk * 2.5f);

            // Head: slight nod with the walk and a snap back on hits.
            head.localRotation = headRest *
                Quaternion.Euler(Mathf.Sin(walkPhase * 0.5f) * 2f * moveBlend - hit * hitRecoil,
                    0f, -walk * 1.5f);

            // Vertical bob reads as weight without lifting the collider.
            model.localPosition = modelRestPosition +
                Vector3.up * (Mathf.Sin(walkPhase * 2f) * bobHeight * moveBlend);
        }

        private static Transform FindJoint(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
