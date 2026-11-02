using UnityEngine;

namespace Shooter.Managers
{
    /// <summary>
    /// Central audio hub. Every clip is intentionally empty: this is the wiring point
    /// where real sounds can be dropped in later without touching gameplay code.
    /// Clips set on a specific AudioSource play spatially; everything else uses a
    /// one-shot source created at the requested world position.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Placeholder clips — assign later")]
        public AudioClip weaponFire;
        public AudioClip weaponEmpty;
        public AudioClip weaponReload;
        public AudioClip enemyHit;
        public AudioClip enemyDeath;
        public AudioClip enemyAttack;
        public AudioClip playerHurt;
        public AudioClip ammoPickup;
        public AudioClip waveStart;
        public AudioClip waveComplete;
        public AudioClip gameOver;

        [Range(0f, 1f)]
        public float masterVolume = 0.8f;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void PlayWeaponFire(Vector3 position, AudioSource source) =>
            Play(weaponFire, position, 0.65f, source);

        public void PlayWeaponEmpty(Vector3 position) =>
            Play(weaponEmpty, position, 0.5f);

        public void PlayWeaponReload(Vector3 position, AudioSource source) =>
            Play(weaponReload, position, 0.6f, source);

        public void PlayEnemyHit(Vector3 position) =>
            Play(enemyHit, position, 0.55f);

        public void PlayEnemyDeath(Vector3 position) =>
            Play(enemyDeath, position, 0.7f);

        public void PlayEnemyAttack(Vector3 position) =>
            Play(enemyAttack, position, 0.6f);

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
