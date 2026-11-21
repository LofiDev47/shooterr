using UnityEngine;

namespace Shooter.Effects
{
    public enum FoundryEffect
    {
        Impact, Explosion, ShockRing, Bolt, Tracer, Debris,
    }

    /// <summary>Scene-owned references for the baked effects used by existing combat emitters.</summary>
    public sealed class FoundryEffects : MonoBehaviour
    {
        [SerializeField] private GameObject[] prefabs;
        private static FoundryEffects active;

        public void Configure(GameObject[] assets) => prefabs = assets;
        private void Awake() => active = this;

        private void OnDestroy()
        {
            if (active == this)
                active = null;
        }

        public static GameObject Spawn(FoundryEffect kind, Vector3 position, Quaternion rotation,
            float scale, float duration, Transform parent = null, bool loop = false)
        {
            if (active == null || active.prefabs == null || (int)kind >= active.prefabs.Length)
                return null;

            GameObject source = active.prefabs[(int)kind];
            if (source == null)
                return null;

            GameObject instance = Instantiate(source, position, rotation, parent);
            instance.transform.localScale = Vector3.one * scale;
            Animation animation = instance.GetComponentInChildren<Animation>();
            Play(animation, duration, loop);
            if (parent == null)
                Destroy(instance, duration);
            return instance;
        }

        public static void Play(Animation animation, float duration, bool loop = false)
        {
            if (animation == null)
                return;

            AnimationState state = animation["Effect"];
            if (state == null)
                return;

            state.time = 0f;
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            state.speed = state.length / Mathf.Max(0.02f, duration);
            animation.Play("Effect");
        }
    }
}
