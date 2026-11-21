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
        [Tooltip("Fallback prefab; also used by older tests.")]
        public GameObject enemyPrefab;
        public GameObject trooperPrefab;
        public GameObject exploderPrefab;
        public GameObject runnerPrefab;
        public GameObject bulwarkPrefab;
        public GameObject marksmanPrefab;
        public GameObject elitePrefab;
        public GameObject bossPrefab;
        public GameObject ammoCachePrefab;
        public GameObject healthCachePrefab;
        public PlayerHealth playerTarget;
        public Transform[] spawnPoints;
        public HUDController hud;

        [Header("Elites")]
        [Tooltip("First wave that can contain elites.")]
        [SerializeField] private int eliteFromWave = 3;
        [Range(0f, 1f)]
        [SerializeField] private float eliteChance = 0.18f;
        [SerializeField] private float eliteHealthMultiplier = 2.6f;
        [SerializeField] private float eliteScale = 1.15f;
        [Tooltip("How much slower than a normal enemy an elite moves.")]
        [SerializeField] private float eliteSpeedPenalty = 1.1f;
        [SerializeField] private float eliteDamageBonus = 6f;
        [SerializeField] private int eliteScoreValue = 25;

        [Header("Variants")]
        [SerializeField] private int runnerFromWave = 2;
        [SerializeField] private int bulwarkFromWave = 3;
        [SerializeField] private float runnerChance = 0.30f;
        [SerializeField] private float bulwarkChance = 0.20f;
        [SerializeField] private float runnerHealthMultiplier = 0.62f;
        [SerializeField] private float runnerSpeedBonus = 1.8f;
        [SerializeField] private float runnerDamagePenalty = 0.55f;
        [SerializeField] private int runnerScoreValue = 15;
        [SerializeField] private float runnerScale = 0.80f;

        [Header("Marksman variant")]
        [SerializeField] private int marksmanFromWave = 2;
        [Range(0f, 1f)]
        [SerializeField] private float marksmanChance = 0.18f;
        [SerializeField] private float marksmanHealthMultiplier = 0.8f;
        [Tooltip("How much slower than a normal enemy a marksman moves.")]
        [SerializeField] private float marksmanSpeedPenalty = 0.5f;
        [SerializeField] private int marksmanScoreValue = 18;

        [Header("Marksman spacing")]
        [Tooltip("Preferred distance a ranged enemy keeps from the player.")]
        [SerializeField] private float marksmanHoldDistance = 11f;
        [SerializeField] private float marksmanStrafeSpeed = 2.4f;

        [SerializeField] private float bulwarkHealthMultiplier = 2.15f;
        [SerializeField] private float bulwarkSpeedPenalty = 1.4f;
        [SerializeField] private float bulwarkDamageBonus = 5f;
        [SerializeField] private int bulwarkScoreValue = 20;
        [SerializeField] private float bulwarkScale = 1.18f;

        [Header("Elite drops")]
        [Tooltip("Ammo cache dropped when an elite dies. Null disables the drop.")]
        [SerializeField] private int eliteDropAmmo = 90;

        [Header("Boss waves")]
        [Tooltip("Every 5th wave fields The Warden. Keep in sync with HUD countdown.")]
        [SerializeField] private float bossHealthMultiplier = 22f;
        [Tooltip("The authored boss model is already ~4.6 m tall, so it spawns at 1x. " +
                 "Raising this would also enlarge its CharacterController.")]
        [SerializeField] private float bossScale = 1f;
        [Tooltip("How much slower than a normal enemy the boss moves.")]
        [SerializeField] private float bossSpeedPenalty = 1.1f;
        [SerializeField] private float bossDamageBonus = 12f;
        [SerializeField] private int bossScoreValue = 150;
        [Tooltip("Health restored by the heal cache the boss drops.")]
        [SerializeField] private float bossDropHeal = 40f;

        [Header("Exploder variant")]
        [SerializeField] private int exploderFromWave = 3;
        [Range(0f, 1f)]
        [SerializeField] private float exploderChance = 0.14f;
        [SerializeField] private float exploderHealthMultiplier = 0.5f;
        [Tooltip("How much faster than a normal enemy an exploder moves.")]
        [SerializeField] private float exploderSpeedBonus = 1.2f;
        [SerializeField] private float exploderScale = 0.85f;
        [SerializeField] private int exploderScoreValue = 12;
        [SerializeField] private Color exploderTint = new Color(1.6f, 1.25f, 0.15f);

        [Header("Heal drops (heal on kills)")]
        [Tooltip("Chance for any kill to drop a small heal cache.")]
        [Range(0f, 1f)]
        [SerializeField] private float healDropChance = 0.10f;
        [SerializeField] private float healDropAmount = 15f;
        [Tooltip("Chance for an elite kill to drop a heal cache.")]
        [Range(0f, 1f)]
        [SerializeField] private float eliteHealDropChance = 0.35f;
        [SerializeField] private float eliteDropHeal = 25f;

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

        /// <summary>True when the current wave is a boss wave.</summary>
        public bool IsBossWave => IsBossWaveNumber(CurrentWave);

        /// <summary>Every 5th wave (5, 10, 15...) is a boss wave. Pure for tests.</summary>
        public static bool IsBossWaveNumber(int wave) => wave > 0 && wave % 5 == 0;

        private int spawnedThisWave;
        private float spawnTimer;
        private int lastSpawnIndex = -1;
        private bool bossSpawnedThisWave;

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
            bossSpawnedThisWave = false;
            Phase = WavePhase.Active;

            AudioManager.Instance?.PlayWaveStart();

            if (IsBossWave)
            {
                hud?.ShowToast($"BOSS WAVE {CurrentWave} INBOUND");
            }

            Debug.Log($"[Shooter] Wave {CurrentWave} started with {WaveSize} enemies." + (IsBossWave ? " (boss wave)" : ""));
        }

        private void SpawnEnemy()
        {
            if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
            {
                return;
            }

            Transform point = PickSpawnPoint();
            float waveIndex = CurrentWave - 1;

            bool boss = IsBossWave && !bossSpawnedThisWave;
            bool elite = !boss && CurrentWave >= eliteFromWave && Random.value < eliteChance;
            bool exploder = !boss && !elite && CurrentWave >= exploderFromWave && Random.value < exploderChance;
            bool marksman = !boss && !elite && !exploder && CurrentWave >= marksmanFromWave && Random.value < marksmanChance;
            bool runner = !boss && !elite && !exploder && !marksman && CurrentWave >= runnerFromWave && Random.value < runnerChance;
            bool bulwark = !boss && !elite && !exploder && !marksman && !runner && CurrentWave >= bulwarkFromWave && Random.value < bulwarkChance;

            // Pick the dedicated model for this variant. enemyPrefab stays as a
            // fallback for older scenes and tests.
            GameObject prefab = boss && bossPrefab != null ? bossPrefab
                : boss ? elitePrefab
                : elite ? elitePrefab
                : exploder && exploderPrefab != null ? exploderPrefab
                : marksman ? marksmanPrefab
                : runner ? runnerPrefab
                : bulwark ? bulwarkPrefab
                : trooperPrefab;

            if (prefab == null)
            {
                prefab = enemyPrefab;
            }

            GameObject instance = Instantiate(prefab, point.position, Quaternion.identity);
            float healthMultiplier = 1f + waveIndex * healthPerWave;
            float speedBonus = waveIndex * speedPerWave;
            float damageBonus = waveIndex * damagePerWave;
            float scale = 1f;
            Color? tint = null;
            int scoreValue = 10;

            if (boss)
            {
                // Boss rules: huge, tanky, slow, hits hard, and shoots as well as
                // charging. It rewards focus fire and punishes greed.
                healthMultiplier *= bossHealthMultiplier;
                speedBonus -= bossSpeedPenalty;
                damageBonus += bossDamageBonus;
                scale = bossScale;
                scoreValue = bossScoreValue;
                bossSpawnedThisWave = true;
            }
            else if (elite)
            {
                // Elites are bigger, tougher and hit harder, but slower.
                healthMultiplier *= eliteHealthMultiplier;
                speedBonus -= eliteSpeedPenalty;
                damageBonus += eliteDamageBonus;
                scale = eliteScale;
                scoreValue = eliteScoreValue;
            }
            else if (exploder)
            {
                // Exploders are fast, fragile, and turn a kill into a blast.
                // They force the player to kill at range or burn dash/slide.
                healthMultiplier *= exploderHealthMultiplier;
                speedBonus += exploderSpeedBonus;
                damageBonus += 4f;
                scale = exploderScale;
                tint = exploderTint;
                scoreValue = exploderScoreValue;
            }
            else if (marksman)
            {
                // Marksmen trade health for a telegraphed ranged attack. They force
                // the player to break line of sight instead of backpedalling forever.
                healthMultiplier *= marksmanHealthMultiplier;
                speedBonus -= marksmanSpeedPenalty;
                scoreValue = marksmanScoreValue;
            }
            else if (runner)
            {
                // Runners arrive fast and low-health. They punish greedy positioning.
                healthMultiplier *= runnerHealthMultiplier;
                speedBonus += runnerSpeedBonus;
                damageBonus *= runnerDamagePenalty;
                scale = runnerScale;
                scoreValue = runnerScoreValue;
            }
            else if (bulwark)
            {
                // Bulwarks are slow moving shields. They telegraph where the pressure will come from.
                healthMultiplier *= bulwarkHealthMultiplier;
                speedBonus -= bulwarkSpeedPenalty;
                damageBonus += bulwarkDamageBonus;
                scale = bulwarkScale;
                scoreValue = bulwarkScoreValue;
            }

            EnemyHealth health = instance.GetComponent<EnemyHealth>();
            EnemyAI ai = instance.GetComponent<EnemyAI>();
            EnemyRangedAttack ranged = instance.GetComponent<EnemyRangedAttack>();
            EnemyExploder exploderComp = instance.GetComponent<EnemyExploder>();
            BossController bossController = instance.GetComponent<BossController>();

            if (health != null)
            {
                health.Initialize(healthMultiplier, scale, tint, elite, boss);
                health.SetScoreValue(scoreValue);

                health.Died += HandleEnemyDied;
            }

            if (boss && bossController != null)
            {
                // The Warden runs its own movement, attacks and phases.
                bossController.Initialize(playerTarget, this, Mathf.RoundToInt(waveIndex), damageBonus, speedBonus);
            }

            if (ai != null)
            {
                ai.Initialize(playerTarget, speedBonus, damageBonus);
            }

            if ((marksman || boss) && ranged != null)
            {
                if (marksman)
                {
                    ai?.SetRanged(marksmanHoldDistance, marksmanStrafeSpeed);
                }

                // Boss keeps melee behaviour *and* shoots, so it stays a threat
                // at any range instead of turning into a stationary turret.
                ranged.Initialize(playerTarget, boss ? waveIndex * 0.6f + 4f : waveIndex * 0.4f);
            }

            if (exploder && exploderComp != null)
            {
                exploderComp.Initialize(playerTarget, damageBonus);
            }

            if (boss)
            {
                hud?.ShowToast("THE WARDEN INBOUND");
            }
            else if (elite)
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

        /// <summary>
        /// Registers an enemy summoned mid-wave by the boss. Summons join the wave
        /// so they count toward clearing it, award score and drop like any other kill.
        /// </summary>
        public void RegisterSummon(EnemyHealth enemy)
        {
            if (enemy == null || enemy.IsDead)
            {
                return;
            }

            enemy.Died -= HandleEnemyDied;
            enemy.Died += HandleEnemyDied;
            AliveCount++;
        }

        private void HandleEnemyDied(EnemyHealth enemy)
        {
            enemy.Died -= HandleEnemyDied;
            AliveCount = Mathf.Max(0, AliveCount - 1);

            if (enemy.IsBoss)
            {
                // Boss rules: both drops are guaranteed, so the reward is not RNG.
                DropAmmoCache(enemy.transform.position);
                DropHealthCache(enemy.transform.position, bossDropHeal);
                hud?.ShowToast("THE WARDEN FALLS  ·  +AMMO +HEAL");
                return;
            }

            if (enemy.IsElite)
            {
                DropAmmoCache(enemy.transform.position);

                if (Random.value < eliteHealDropChance)
                {
                    DropHealthCache(enemy.transform.position, eliteDropHeal);
                }
            }
            else if (Random.value < healDropChance)
            {
                // Heal on kills: aggression heals, so passive play starves.
                DropHealthCache(enemy.transform.position, healDropAmount);
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

        /// <summary>
        /// Spawns a medikit where the enemy fell. Offset a little from the ammo
        /// cache so they don't overlap visually. Stays until the player actually
        /// needs health.
        /// </summary>
        private void DropHealthCache(Vector3 position, float heal)
        {
            if (healthCachePrefab == null || heal <= 0f)
            {
                return;
            }

            Vector3 dropPosition = new Vector3(
                position.x + Random.Range(-0.6f, 0.6f), 0.55f, position.z + Random.Range(-0.6f, 0.6f));
            GameObject cache = Instantiate(healthCachePrefab, dropPosition, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            HealthPickup pickup = cache.GetComponent<HealthPickup>();

            if (pickup != null)
            {
                pickup.HealAmount = heal;
                pickup.playerHealth = null; // Resolved at runtime.
                pickup.player = null;
            }

            Debug.Log($"[Shooter] Dropped heal cache (+{heal} HP) at {dropPosition}.");
        }
    }
}
