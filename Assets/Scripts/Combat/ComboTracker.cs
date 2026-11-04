using UnityEngine;

namespace Shooter.Combat
{
    /// <summary>
    /// Pure kill-chain bookkeeping: consecutive kills inside a short window raise a
    /// score multiplier, and the chain lapses back to one when the window expires.
    /// Kept free of Unity scene state so the rules can be tested headlessly.
    /// </summary>
    public sealed class ComboTracker
    {
        private readonly float window;
        private readonly float step;
        private readonly float maxMultiplier;

        /// <summary>Length of the current chain.</summary>
        public int Count { get; private set; }

        /// <summary>Longest chain reached this run.</summary>
        public int Best { get; private set; }

        /// <summary>Score multiplier applied to the next kill. Starts at 1.</summary>
        public float Multiplier { get; private set; } = 1f;

        /// <summary>Time at which the current chain lapses.</summary>
        public float ExpiresAt { get; private set; }

        public ComboTracker(float window = 3.5f, float step = 0.25f, float maxMultiplier = 4f)
        {
            this.window = Mathf.Max(0.1f, window);
            this.step = Mathf.Max(0f, step);
            this.maxMultiplier = Mathf.Max(1f, maxMultiplier);
        }

        /// <summary>
        /// Registers a kill at <paramref name="now"/> and returns the score it pays.
        /// A kill inside the live window extends the chain; a late one starts over.
        /// </summary>
        public int RegisterKill(float now, int baseScore)
        {
            Count = Count > 0 && now <= ExpiresAt ? Count + 1 : 1;
            ExpiresAt = now + window;
            Multiplier = Mathf.Min(maxMultiplier, 1f + (Count - 1) * step);
            Best = Mathf.Max(Best, Count);

            return Mathf.RoundToInt(Mathf.Max(0, baseScore) * Multiplier);
        }

        /// <summary>Lapses the chain once its window has passed.</summary>
        public void Tick(float now)
        {
            if (Count > 0 && now > ExpiresAt)
            {
                Reset();
            }
        }

        /// <summary>Seconds left before the chain lapses; zero when idle.</summary>
        public float Remaining(float now) => Count > 0 ? Mathf.Max(0f, ExpiresAt - now) : 0f;

        public void Reset()
        {
            Count = 0;
            Multiplier = 1f;
            ExpiresAt = 0f;
        }
    }
}
