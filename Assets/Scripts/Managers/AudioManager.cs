using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shooter.Managers
{
    /// <summary>
    /// Central audio hub. Placeholders are intentionally kept assignable, but if a
    /// slot is left empty a code-generated fallback is created at startup. That keeps
    /// a fresh scene playable from the very first shot with no external audio assets.
    /// Clips set on a specific AudioSource play spatially; everything else uses a
    /// one-shot source created at the requested world position.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Override clips (optional)")]
        public AudioClip weaponFire;
        public AudioClip weaponEmpty;
        public AudioClip weaponReload;
        public AudioClip weaponHit;
        public AudioClip enemyHit;
        public AudioClip enemyDeath;
        public AudioClip enemyAttack;
        public AudioClip enemyCharge;
        public AudioClip enemyShot;
        public AudioClip playerHurt;
        public AudioClip ammoPickup;
        public AudioClip waveStart;
        public AudioClip waveComplete;
        public AudioClip gameOver;

        [Range(0f, 1f)]
        public float masterVolume = 0.8f;

        private readonly Dictionary<string, AudioClip> generated = new Dictionary<string, AudioClip>();

        private void Awake()
        {
            Instance = this;
            EnsureProceduralClips();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Creates in-memory PCM sounds when no clip was explicitly assigned. The clips
        /// are semantic, not random beeps: they carry attack noise, tonal bodies and
        /// proper release tails so the complete soundscape works out of the box.
        /// </summary>
        private void EnsureProceduralClips()
        {
            if (weaponFire == null) weaponFire = Generate("WeaponFire", 0.34f, WeaponFireSample);
            if (weaponEmpty == null) weaponEmpty = Generate("WeaponEmpty", 0.15f, EmptyClickSample);
            if (weaponReload == null) weaponReload = Generate("WeaponReload", 0.62f, ReloadSample);
            if (weaponHit == null) weaponHit = Generate("WeaponHit", 0.18f, MetalTickSample);
            if (enemyHit == null) enemyHit = Generate("EnemyHit", 0.26f, EnemyImpactSample);
            if (enemyDeath == null) enemyDeath = Generate("EnemyDeath", 0.58f, EnemyDeathSample);
            if (enemyAttack == null) enemyAttack = Generate("EnemyAttack", 0.34f, EnemyGrowlSample);
            if (enemyCharge == null) enemyCharge = Generate("EnemyCharge", 0.55f, EnemyChargeSample);
            if (enemyShot == null) enemyShot = Generate("EnemyShot", 0.3f, EnemyShotSample);
            if (playerHurt == null) playerHurt = Generate("PlayerHurt", 0.32f, PlayerHurtSample);
            if (ammoPickup == null) ammoPickup = Generate("AmmoPickup", 0.32f, PickupSample);
            if (waveStart == null) waveStart = Generate("WaveStart", 0.85f, WaveStartSample);
            if (waveComplete == null) waveComplete = Generate("WaveComplete", 0.62f, WaveClearSample);
            if (gameOver == null) gameOver = Generate("GameOver", 1.35f, GameOverSample);
        }

        // ---------------------------------------------------------------- procedural clips

        private AudioClip Generate(string name, float duration, Func<float, double, float> sample)
        {
            const int sampleRate = 44100;
            int samples = Mathf.CeilToInt(duration * sampleRate);
            float[] pcm = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                double time = i / (double)sampleRate;
                pcm[i] = Mathf.Clamp(sample((float)time, time), -0.98f, 0.98f);
            }

            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(pcm, 0);
            generated[name] = clip;
            return clip;
        }

        private double NoiseAt(double index)
        {
            double x = Math.Sin(index * 12.9898 + 78.233) * 43758.5453;
            return (x - Math.Floor(x)) * 2.0 - 1.0;
        }

        private float Decay(double time, double rate) => (float)Math.Exp(-time * rate);

        private float WaveSine(double t, double freq, double rate = 9.0)
            => (float)Math.Sin(Math.PI * 2.0 * freq * t) * Decay(t, rate);

        private float WeaponFireSample(float t, double time)
        {
            float body = (float)Math.Sin(Math.PI * 2.0 * 70.0 * time) * Decay(time, 21f);
            float bite = (float)Math.Sin(Math.PI * 2.0 * 480.0 * time) * Decay(time, 42f);
            float whip = (float)NoiseAt(time * 2200.0) * Decay(time, 35f);
            float click = (float)NoiseAt(time * 3900.0) * Decay(time, 75f);
            return body * 0.58f + bite * 0.22f + whip * 0.32f + click * 0.45f;
        }

        private float EmptyClickSample(float t, double time)
        {
            float tick = (float)NoiseAt(time * 5200.0) * Decay(time, 140f);
            float ping = WaveSine(time, 980.0, 90.0);
            return tick * 0.6f + ping * 0.18f;
        }

        private float ReloadSample(float t, double time)
        {
            float mag = System.Math.Abs(time - 0.16) < 0.004 ? 0.6f : 0f;
            float bolt = System.Math.Abs(time - 0.46) < 0.005 ? 0.85f : 0f;
            float rattle = (float)NoiseAt(time * 3000.0) * Decay(time, 42f);
            float clack = (float)Math.Sin(Math.PI * 2.0 * 190.0 * time) * Decay(time, 28f);
            return mag + bolt + rattle * 0.25f + clack * 0.55f;
        }

        private float MetalTickSample(float t, double time)
        {
            float clang = (float)Math.Sin(Math.PI * 2.0 * 620.0 * time) * Decay(time, 30.0);
            float sparks = (float)NoiseAt(time * 4700.0) * Decay(time, 52.0);
            return clang * 0.65f + sparks * 0.35f;
        }

        private float EnemyImpactSample(float t, double time)
        {
            float thud = WaveSine(time, 115.0, 30.0);
            float tear = (float)NoiseAt(time * 2600.0) * Decay(time, 34.0);
            float crunch = (float)NoiseAt(time * 900.0) * Decay(time, 18.0);
            return thud * 0.55f + tear * 0.48f + crunch * 0.38f;
        }

        private float EnemyDeathSample(float t, double time)
        {
            float low = WaveSine(time, 58.0, 6.5);
            float tear = (float)NoiseAt(time * 1500.0) * Decay(time, 8.0);
            float whine = (float)Math.Sin(Math.PI * 2.0 * (170.0 + time * 220.0) * time) * Decay(time, 7.0);
            return low * 0.8f + tear * 0.55f + whine * 0.22f;
        }

        private float EnemyGrowlSample(float t, double time)
        {
            double vibrato = Math.Sin(Math.PI * 2.0 * 23.0 * time) * 11.0;
            float growl = (float)Math.Sin(Math.PI * 2.0 * (78.0 + vibrato) * time) * Decay(time, 7.0);
            float rasp = (float)NoiseAt(time * 1700.0) * Decay(time, 9.0);
            return growl * 0.65f + rasp * 0.32f;
        }

        private float EnemyChargeSample(float t, double time)
        {
            double rising = 220.0 + time * 900.0;
            float whine = (float)Math.Sin(Math.PI * 2.0 * rising * time) * 0.42f;
            float buzz = (float)Math.Sin(Math.PI * 2.0 * 70.0 * time) * 0.24f;
            float air = (float)NoiseAt(time * 1200.0) * 0.14f;
            float envelope = Mathf.Clamp01((float)time / 0.12f) * (1f - 0.35f * (float)(time / 0.55));
            return (whine + buzz + air) * envelope;
        }

        private float EnemyShotSample(float t, double time)
        {
            float zap = (float)Math.Sin(Math.PI * 2.0 * (900.0 - time * 2200.0) * time) * Decay(time, 18.0);
            float crack = (float)NoiseAt(time * 5200.0) * Decay(time, 55.0);
            float low = (float)Math.Sin(Math.PI * 2.0 * 120.0 * time) * Decay(time, 16.0);
            return zap * 0.5f + crack * 0.42f + low * 0.34f;
        }

        private float PlayerHurtSample(float t, double time)
        {
            float thump = WaveSine(time, 96.0, 15.0);
            float pressure = (float)NoiseAt(time * 1400.0) * Decay(time, 12.0);
            return thump * 0.8f + pressure * 0.25f;
        }

        private float PickupSample(float t, double time)
        {
            float bright = WaveSine(time, 760.0, 20.0);
            float harmony = WaveSine(time, 1140.0, 24.0);
            return bright * 0.38f + harmony * 0.22f;
        }

        private float WaveStartSample(float t, double time)
        {
            float swell = (float)Math.Sin(Math.PI * Math.Min(1.0, time * 2.8));
            float low = (float)Math.Sin(Math.PI * 2.0 * 55.0 * time);
            float fifth = (float)Math.Sin(Math.PI * 2.0 * 82.4 * time);
            return (low + fifth * 0.6f) * swell * 0.7f * (1f - Mathf.Clamp01((float)time / 0.85f));
        }

        private float WaveClearSample(float t, double time)
        {
            float a = WaveSine(time, 440.0, 7.0);
            float d = WaveSine(time - 0.18, 587.0, 7.0);
            float sharp = WaveSine(time - 0.36, 740.0, 6.0);
            float chime = WaveSine(time - 0.42, 1480.0, 18.0);
            return a * 0.38f + d * 0.38f + sharp * 0.38f + chime * 0.18f;
        }

        private float GameOverSample(float t, double time)
        {
            float minor = (float)Math.Sin(Math.PI * 2.0 * (146.0 - time * 28.0) * time) * Decay(time, 1.8);
            float pressure = (float)NoiseAt(time * 900.0) * Decay(time, 2.4);
            float drone = (float)Math.Sin(Math.PI * 2.0 * 34.0 * time) * Decay(time, 1.4);
            return minor * 0.55f + pressure * 0.36f + drone * 0.35f;
        }

        public void PlayWeaponFire(Vector3 position, AudioSource source) =>
            Play(weaponFire, position, 0.65f, source);

        public void PlayWeaponEmpty(Vector3 position) =>
            Play(weaponEmpty, position, 0.5f);

        public void PlayWeaponHit(Vector3 position) =>
            Play(weaponHit, position, 0.5f);

        public void PlayWeaponReload(Vector3 position, AudioSource source) =>
            Play(weaponReload, position, 0.6f, source);

        public void PlayEnemyHit(Vector3 position) =>
            Play(enemyHit, position, 0.55f);

        public void PlayEnemyDeath(Vector3 position) =>
            Play(enemyDeath, position, 0.7f);

        public void PlayEnemyAttack(Vector3 position) =>
            Play(enemyAttack, position, 0.6f);

        public void PlayEnemyCharge(Vector3 position) =>
            Play(enemyCharge, position, 0.42f);

        public void PlayEnemyShot(Vector3 position) =>
            Play(enemyShot, position, 0.6f);

        public void PlayPlayerHurt(Vector3 position) =>
            Play(playerHurt, position, 0.75f);

        public void PlayAmmoPickup(Vector3 position) =>
            Play(ammoPickup, position, 0.7f);

        public void PlayWaveStart() =>
            Play(waveStart, Vector3.zero, 0.8f);

        public void PlayWaveComplete() =>
            Play(waveComplete, Vector3.zero, 0.8f);

        public void PlayGameOver() =>
            Play(gameOver, Vector3.zero, 0.9f);

        /// <summary>Null-safe playback used by every gameplay hook.</summary>
        private void Play(AudioClip clip, Vector3 position, float volume, AudioSource source = null)
        {
            if (clip == null)
            {
                return;
            }

            float finalVolume = Mathf.Clamp01(volume * masterVolume);

            if (source != null)
            {
                source.PlayOneShot(clip, finalVolume);
                return;
            }

            AudioSource.PlayClipAtPoint(clip, position, finalVolume);
        }
    }
}
