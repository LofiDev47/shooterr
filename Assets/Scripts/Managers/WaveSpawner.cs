using Shooter.Enemies;
using Shooter.Player;
using Shooter.UI;
using Shooter.Weapons;
using UnityEngine;

namespace Shooter.Managers
{
    public enum WavePhase
    {
        /// <summary>Waiting before the next wave starts.</summary>
        Countdown,

        /// <summary>Spawning and fighting the current wave.</summary>
        Active
    }

    /// <summary>
    /// Endless wave spawner. Each wave adds enemies and scales their health, speed and
    /// damage. Spawning is throttled so the arena never exceeds <see cref="maxAlive"/>.
    /// </summary>
    public sealed class WaveSpawner : MonoBehaviour
    {
        [Header("Wiring (scene builder)")]
        public GameObject enemyPrefab;
        public GameObject ammoCachePrefab;
        public PlayerHealth playerTarget;
        public Transform[] spawnPoints;
        public HUDController hud;

        [Header("Elites")]
        [Tooltip("First wave that can contain elites.")]
        [SerializeField] private int eliteFromWave = 3;
        [Range(0f, 1f)]
        [SerializeField] private float eliteChance = 0.18f;
        [SerializeField] private float eliteHealthMultiplier = 2.6f;
        [SerializeField] private float eliteScale = 1.35f;
        [Tooltip("How much slower than a normal enemy an elite moves.")]
        [SerializeField] private float eliteSpeedPenalty = 1.1f;
        [SerializeField] private float eliteDamageBonus = 6f;
        [SerializeField] private int eliteScoreValue = 25;
        [SerializeField] private Color eliteTint = new Color(1f, 0.5f, 0.12f);

        [Header("Variants")]
        [SerializeField] private int runnerFromWave = 2;
        [SerializeField] private int bulwarkFromWave = 3;
        [SerializeField] private float runnerChance = 0.30f;
        [SerializeField] private float bulwarkChance = 0.20f;
        [SerializeField] private float runnerHealthMultiplier = 0.62f;
        [SerializeField] private float runnerSpeedBonus = 1.8f;
        [SerializeField] private float runnerDamagePenalty = 0.55f;
        [SerializeField] private int runnerScoreValue = 15;
        [SerializeField] private float runnerScale = 0.72f;
        [SerializeField] private Color runnerTint = new Color(1.55f, 0.28f, 0.9f);

        [Header("Marksman variant")]
        [SerializeField] private int marksmanFromWave = 2;
        [Range(0f, 1f)]
        [SerializeField] private float marksmanChance = 0.18f;
        [SerializeField] private float marksmanHealthMultiplier = 0.8f;
        [Tooltip("How much slower than a normal enemy a marksman moves.")]
        [SerializeField] private float marksmanSpeedPenalty = 0.5f;
        [SerializeField] private int marksmanScoreValue = 18;
        [SerializeField] private Color marksmanTint = new Color(0.75f, 1.35f, 0.55f);

        [Header("Marksman spacing")]
        [Tooltip("Preferred distance a ranged enemy keeps from the player.")]
        [SerializeField] private float marksmanHoldDistance = 11f;
        [SerializeField] private float marksmanStrafeSpeed = 2.4f;

        [SerializeField] private float bulwarkHealthMultiplier = 2.15f;
        [SerializeField] private float bulwarkSpeedPenalty = 1.4f;
        [SerializeField] private float bulwarkDamageBonus = 5f;
        [SerializeField] private int bulwarkScoreValue = 20;
        [SerializeField] private float bulwarkScale = 1.28f;
        [SerializeField] private Color bulwarkTint = new Color(0.25f, 0.95f, 1.55f);

        [Header("Elite drops")]
        [Tooltip("Ammo cache dropped when an elite dies. Null disables the drop.")]
        [SerializeField] private int eliteDropAmmo = 90;

        [Header("Waves")]
        [SerializeField] private int startingEnemies = 4;
        [SerializeField] private int enemiesAddedPerWave = 2;
        [SerializeField] private int maxEnemiesPerWave = 36;
        [SerializeField] private int maxAlive = 14;
        [SerializeField] private float spawnInterval = 0.55f;
        [SerializeField] private float firstWaveDelay = 1.5f;
        [SerializeField] private float interWaveDelay = 3.5f;

        [Header("Difficulty scaling (per wave, wave 1 = 0)")]
        [SerializeField] private float healthPerWave = 0.18f;
        [SerializeField] private float speedPerWave = 0.12f;
        [SerializeField] private float damagePerWave = 0.6f;

        public WavePhase Phase { get; private set; } = WavePhase.Countdown;
        public int CurrentWave { get; private set; }
        public int WaveSize { get; private set; }
        public int AliveCount { get; private set; }
        public float CountdownRemaining { get; private set; }

        private int spawnedThisWave;
        private float spawnTimer;
        private int lastSpawnIndex = -1;

        private void Start()
        {
            CountdownRemaining = firstWaveDelay;
        }

        private void Update()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (Phase == WavePhase.Countdown)
            {
                CountdownRemaining -= deltaTime;

                if (CountdownRemaining <= 0f)
                {
                    BeginWave();
                }

                return;
            }

            if (spawnedThisWave < WaveSize)
            {
                spawnTimer -= deltaTime;

                if (spawnTimer <= 0f && AliveCount < maxAlive)
                {
                    SpawnEnemy();
                }
            }
            else if (AliveCount <= 0)
            {
                Phase = WavePhase.Countdown;
                CountdownRemaining = interWaveDelay;
                AudioManager.Instance?.PlayWaveComplete();

                // The cleared wave pays out an augment pick; the game pauses until
                // the player makes it (UpgradeManager freezes time itself).
                Upgrades.UpgradeManager.Instance?.OfferChoice();
            }
        }

        private void BeginWave()
        {
            CurrentWave++;
            WaveSize = Mathf.Min(maxEnemiesPerWave, startingEnemies + (CurrentWave - 1) * enemiesAddedPerWave);
            spawnedThisWave = 0;
            spawnTimer = 0f;
            Phase = WavePhase.Active;

            AudioManager.Instance?.PlayWaveStart();
            Debug.Log($"[Shooter] Wave {CurrentWave} started with {WaveSize} enemies.");
        }

        private void SpawnEnemy()
        {
            if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
            {
                return;
            }

            Transform point = PickSpawnPoint();
            GameObject instance = Instantiate(enemyPrefab, point.position, Quaternion.identity);

            float waveIndex = CurrentWave - 1;

            bool elite = CurrentWave >= eliteFromWave && Random.value < eliteChance;
            bool marksman = !elite && CurrentWave >= marksmanFromWave && Random.value < marksmanChance;
            bool runner = !elite && !marksman && CurrentWave >= runnerFromWave && Random.value < runnerChance;
            bool bulwark = !elite && !marksman && !runner && CurrentWave >= bulwarkFromWave && Random.value < bulwarkChance;
            float healthMultiplier = 1f + waveIndex * healthPerWave;
            float speedBonus = waveIndex * speedPerWave;
            float damageBonus = waveIndex * damagePerWave;
            float scale = 1f;
            Color? tint = null;
            int scoreValue = 10;

            if (elite)
            {
                // Elites are bigger, tougher and hit harder, but slower.
                healthMultiplier *= eliteHealthMultiplier;
                speedBonus -= eliteSpeedPenalty;
                damageBonus += eliteDamageBonus;
                scale = eliteScale;
                tint = eliteTint;
                scoreValue = eliteScoreValue;
            }
            else if (marksman)
            {
                // Marksmen trade health for a telegraphed ranged attack. They force
                // the player to break line of sight instead of backpedalling forever.
                healthMultiplier *= marksmanHealthMultiplier;
                speedBonus -= marksmanSpeedPenalty;
                tint = marksmanTint;
                scoreValue = marksmanScoreValue;
            }
            else if (runner)
            {
                // Runners arrive fast and low-health. They punish greedy positioning.
                healthMultiplier *= runnerHealthMultiplier;
                speedBonus += runnerSpeedBonus;
                damageBonus *= runnerDamagePenalty;
                scale = runnerScale;
                tint = runnerTint;
                scoreValue = runnerScoreValue;
            }
            else if (bulwark)
            {
                // Bulwarks are slow moving shields. They telegraph where the pressure will come from.
                healthMultiplier *= bulwarkHealthMultiplier;
                speedBonus -= bulwarkSpeedPenalty;
                damageBonus += bulwarkDamageBonus;
                scale = bulwarkScale;
                tint = bulwarkTint;
                scoreValue = bulwarkScoreValue;
            }

            EnemyHealth health = instance.GetComponent<EnemyHealth>();
            EnemyAI ai = instance.GetComponent<EnemyAI>();
            EnemyRangedAttack ranged = instance.GetComponent<EnemyRangedAttack>();

            if (health != null)
            {
                health.Initialize(healthMultiplier, scale, tint, elite);
                health.SetScoreValue(scoreValue);

                health.Died += HandleEnemyDied;
            }

            if (ai != null)
            {
                ai.Initialize(playerTarget, speedBonus, damageBonus);
            }

            if (marksman && ranged != null)
            {
                ai?.SetRanged(marksmanHoldDistance, marksmanStrafeSpeed);
                ranged.Initialize(playerTarget, waveIndex * 0.4f);
            }

            if (elite)
            {
                hud?.ShowToast("ELITE INBOUND");
            }

            AliveCount++;
            spawnedThisWave++;
            spawnTimer = spawnInterval;
        }

        private Transform PickSpawnPoint()
        {
            int index = Random.Range(0, spawnPoints.Length);

            // Try not to reuse the same point twice in a row, and avoid spawning on the player.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Transform candidate = spawnPoints[index];

                bool onPlayer = playerTarget != null &&
                                Vector3.Distance(candidate.position, playerTarget.transform.position) < 8f;

                if (!onPlayer && (index != lastSpawnIndex || spawnPoints.Length == 1))
                {
                    break;
                }

                index = Random.Range(0, spawnPoints.Length);
            }

            lastSpawnIndex = index;
            return spawnPoints[index];
        }

        private void HandleEnemyDied(EnemyHealth enemy)
        {
            enemy.Died -= HandleEnemyDied;
            AliveCount = Mathf.Max(0, AliveCount - 1);

            if (enemy.IsElite)
            {
                DropAmmoCache(enemy.transform.position);
            }
        }

        /// <summary>
        /// Spawns the elite's ammo cache where the elite fell. The cache is a one-shot
        /// pickup: it stays on the ground until the player actually needs ammo.
        /// </summary>
        private void DropAmmoCache(Vector3 position)
        {
            if (ammoCachePrefab == null)
            {
                return;
            }

            Vector3 dropPosition = new Vector3(position.x, 0.55f, position.z);
            GameObject cache = Instantiate(ammoCachePrefab, dropPosition, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            AmmoPickup pickup = cache.GetComponent<AmmoPickup>();

            if (pickup != null)
            {
                pickup.AmmoAmount = eliteDropAmmo;
                pickup.weapon = null; // Resolved at runtime: the cache is spawned mid-wave.
            }

            hud?.ShowToast("ELITE DOWN  ·  AMMO CACHE DROPPED");
            Debug.Log($"[Shooter] Elite dropped an ammo cache at {dropPosition}.");
        }
    }
}
