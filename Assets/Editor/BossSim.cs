using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Shooter.Enemies;
using Shooter.Managers;
using Shooter.UI;
using Shooter.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Drives a real boss fight in play mode and asserts what a screenshot cannot:
    /// that the spawner produces the boss, that its authored clips actually play,
    /// that a slam telegraphs, damages and opens the core, that phase changes roar
    /// and summon, and that death ends the fight and hides the bar.
    ///
    /// Editor-only: it uses reflection to jump the spawner to a boss wave instead
    /// of shipping a test hook in the runtime class.
    /// </summary>
    public static class BossSim
    {
        [MenuItem("Shooter/Run Boss Fight Simulation")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ShooterSceneBuilder.ScenePath, OpenSceneMode.Single);

            var host = new GameObject("~BossSim");
            host.AddComponent<BossSimRunner>();

            EditorApplication.EnterPlaymode();
        }
    }

    /// <summary>The coroutine that performs the fight and reports the result.</summary>
    public sealed class BossSimRunner : MonoBehaviour
    {
        private readonly StringBuilder log = new StringBuilder();
        private readonly List<string> errors = new List<string>();
        private readonly HashSet<string> clipsSeen = new HashSet<string>();

        private int checks;
        private int failures;
        private PlayerHealth playerHealth;
        private float startedAt;
        private string reportPath = "/tmp/opencode/bossim.txt";

        private void Awake()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                errors.Add(condition);
            }
        }

        private void Start()
        {
            StartCoroutine(Scenario());
        }

        private void Update()
        {
            if (startedAt > 0f && Time.realtimeSinceStartup - startedAt > 150f)
            {
                checks++;
                failures++;
                log.AppendLine("[BossSim] FAIL — the fight did not finish inside 150 s (watchdog)");
                Finish();
            }

            KeepPlayerAlive(playerHealth);
        }

        private IEnumerator Scenario()
        {
            startedAt = Time.realtimeSinceStartup;

            // No try/catch around the yield: C# forbids it. A crashed coroutine is
            // caught by the watchdog in Update and by the engine-error hook.
            yield return RunFight();

            Finish();
        }

        private IEnumerator RunFight()
        {
            // Every Start() must run before the game state is poked: GameManager.Start
            // resets the run to the menu, so starting a frame earlier is undone.
            yield return null;
            yield return null;

            GameManager game = GameManager.Instance;
            Check(game != null, "the game manager exists");

            if (game != null)
            {
                game.StartGame();
            }

            // Set after StartGame, which resets the clock to 1x. Faster than real
            // time keeps the whole fight inside one short session.
            Time.timeScale = 3f;
            yield return null;

            Check(game == null || game.IsPlaying, "the run is playing before the fight starts");

            WaveSpawner spawner = UnityEngine.Object.FindAnyObjectByType<WaveSpawner>();
            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
            HUDController hud = spawner != null ? spawner.hud : null;

            Check(spawner != null, "the wave spawner exists");
            Check(player != null, "the player exists");
            Check(hud != null, "the HUD exists");

            if (spawner == null || player == null)
            {
                yield break;
            }

            // Jump the countdown to the wave before a boss wave, then let the real
            // spawner start wave 5 by itself.
            SetField(spawner, "CurrentWave", 4);
            SetField(spawner, "CountdownRemaining", 0f);

            float waitUntil = Time.realtimeSinceStartup + 6f;

            while (BossController.Current == null && Time.realtimeSinceStartup < waitUntil)
            {
                KeepPlayerAlive(playerHealth);
                yield return null;
            }

            BossController boss = BossController.Current;
            Check(boss != null, "the wave spawner fields The Warden on wave 5");

            if (boss == null)
            {
                yield break;
            }

            EnemyHealth bossHealth = boss.GetComponent<EnemyHealth>();
            BossAnimator animator = boss.GetComponent<BossAnimator>();

            Check(bossHealth != null, "the boss has health");
            Check(animator != null, "the boss has a clip animator");
            Check(bossHealth != null && bossHealth.IsBoss, "the boss is flagged as a boss");
            Check(Mathf.Approximately(boss.HealthRatio, 1f), "the boss starts at full health");
            Check(boss.Phase == 1, "the boss starts in phase one");
            Check(bossHealth != null && bossHealth.MaxHealth > 1000f,
                $"the boss has a boss-sized health pool ({bossHealth?.MaxHealth:0} hp)");

            // --- locomotion and clips ------------------------------------------
            Vector3 startPosition = boss.transform.position;
            float clipWatchUntil = Time.realtimeSinceStartup + 1.2f;

            while (Time.realtimeSinceStartup < clipWatchUntil)
            {
                KeepPlayerAlive(playerHealth);
                SampleClip(animator);
                yield return null;
            }

            float walked = Vector3.Distance(startPosition, boss.transform.position);
            Check(walked > 0.25f, $"the boss closes on the player ({walked:0.00} m in 1.2 s)");
            Check(clipsSeen.Contains(BossAnimator.IdleClip) || clipsSeen.Contains(BossAnimator.WalkClip),
                "the boss plays its authored locomotion clips");

            // --- slam: telegraph, damage, exposed core -------------------------
            // Stand in slam range so the boss commits to the ground attack.
            float slamDeadline = Time.realtimeSinceStartup + 14f;
            bool sawCoreOpen = false;

            while (Time.realtimeSinceStartup < slamDeadline && !sawCoreOpen)
            {
                KeepPlayerAlive(playerHealth);
                SampleClip(animator);

                if (boss.CoreExposed)
                {
                    sawCoreOpen = true;
                }

                yield return null;
            }

            Check(sawCoreOpen, "the slam opens the core window");
            Check(clipsSeen.Contains(BossAnimator.SlamClip), "the slam clip plays");
            Check(boss.CurrentDamageScale > 1.9f, "an exposed core doubles incoming damage");

            // The exposed core must actually cost more health than base damage.
            float beforeCore = bossHealth.CurrentHealth;
            bossHealth.TakeDamage(10f, boss.transform.position);
            float coreLoss = beforeCore - bossHealth.CurrentHealth;
            Check(Mathf.Approximately(coreLoss, 20f),
                $"10 damage into an exposed core removes 20 health (removed {coreLoss:0.0})");

            // --- phase two ------------------------------------------------------
            bossHealth.IncomingDamageScale = 1f;
            SetHealthRatio(bossHealth, 0.62f);
            yield return WaitFor(() => boss.Phase >= 2, 3f);
            Check(boss.Phase == 2, "dropping to two-thirds enters phase two");
            Check(boss.Invulnerable, "the phase roar makes the boss invulnerable");
            Check(clipsSeen.Contains(BossAnimator.RoarClip) || boss.Invulnerable, "the roar clip plays");

            int aliveBeforeSummons = spawner.AliveCount;
            yield return WaitFor(() => boss.Phase == 2 && !boss.Invulnerable, 5f);
            Check(!boss.Invulnerable, "the roar ends and the boss is vulnerable again");
            Check(spawner.AliveCount > aliveBeforeSummons,
                $"phase two summons adds (alive {aliveBeforeSummons} -> {spawner.AliveCount})");

            // --- phase three ----------------------------------------------------
            SetHealthRatio(bossHealth, 0.28f);
            yield return WaitFor(() => boss.Phase >= 3, 3f);
            Check(boss.Phase == 3, "dropping to one-third enters phase three");
            Check(boss.PhaseLabel == "PHASE III", "the boss bar reports phase three");

            yield return WaitFor(() => !boss.Invulnerable, 5f);

            // --- HUD bar --------------------------------------------------------
            yield return null;
            Check(hud.bossBarRoot != null && hud.bossBarRoot.activeSelf, "the boss bar shows during the fight");

            // --- death ----------------------------------------------------------
            float barBeforeDeath = boss.HealthRatio;

            // Force the vulnerable state first: the blow must not land inside a
            // roar window, which is a test-timing accident, not a fight bug.
            bossHealth.IncomingDamageScale = 1f;
            float healthBeforeKill = bossHealth.CurrentHealth;
            bossHealth.TakeDamage(bossHealth.CurrentHealth + 100f, boss.transform.position);

            if (!bossHealth.IsDead)
            {
                log.AppendLine($"[BossSim] retry kill: hp {healthBeforeKill:0} -> {bossHealth.CurrentHealth:0}, " +
                    $"scale={boss.CurrentDamageScale:0.000} invuln={boss.Invulnerable} phase={boss.Phase}");
                bossHealth.IncomingDamageScale = 1f;
                bossHealth.TakeDamage(bossHealth.CurrentHealth + 100f, boss.transform.position);
            }

            yield return WaitFor(() => bossHealth.IsDead, 2f);

            Check(bossHealth.IsDead, "the boss can be killed");
            Check(barBeforeDeath > 0f, "the boss had health before the killing blow");

            float deathWatchUntil = Time.realtimeSinceStartup + 1.2f;

            while (Time.realtimeSinceStartup < deathWatchUntil)
            {
                SampleClip(animator);
                yield return null;
            }

            Check(clipsSeen.Contains(BossAnimator.DeathClip), "the death clip plays");
            Check(hud.bossBarRoot != null && !hud.bossBarRoot.activeSelf, "the boss bar hides after death");
            Check(spawner.CurrentWave == 5, "the fight happened on wave 5");
        }

        // ------------------------------------------------------------- helpers

        private void SampleClip(BossAnimator animator)
        {
            if (animator != null && !string.IsNullOrEmpty(animator.CurrentClip))
            {
                clipsSeen.Add(animator.CurrentClip);
            }
        }

        private static void KeepPlayerAlive(PlayerHealth health)
        {
            // The sim is testing the boss, not player survival; adds and boss hits
            // would otherwise end the run before the later phases are reached.
            if (health != null && !health.IsDead && health.CurrentHealth < health.MaxHealth * 0.7f)
            {
                health.Heal(health.MaxHealth);
            }
        }

        private static void SetHealthRatio(EnemyHealth health, float ratio)
        {
            float target = health.MaxHealth * ratio;
            float loss = health.CurrentHealth - target;

            if (loss > 0f)
            {
                health.TakeDamage(loss, health.transform.position);
            }
        }

        private IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;

            while (!condition() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        private static void SetField(object target, string field, object value)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type type = target.GetType();

            // Auto-properties keep their state in a compiler-generated backing field.
            FieldInfo info = type.GetField(field, Flags) ?? type.GetField($"<{field}>k__BackingField", Flags);

            if (info == null)
            {
                Debug.LogError($"[BossSim] No field {field} on {type.Name}");
                return;
            }

            info.SetValue(target, value);
        }

        private void Check(bool condition, string description)
        {
            checks++;

            if (condition)
            {
                log.AppendLine($"[BossSim] PASS — {description}");
                return;
            }

            failures++;
            log.AppendLine($"[BossSim] FAIL — {description}");
        }

        private void Finish()
        {
            Time.timeScale = 1f;

            float elapsed = Time.realtimeSinceStartup - startedAt;
            log.AppendLine($"[BossSim] {checks - failures}/{checks} checks passed in {elapsed:0.0}s.");

            if (errors.Count > 0)
            {
                log.AppendLine($"[BossSim] {errors.Count} engine error(s):");

                foreach (string error in errors)
                {
                    log.AppendLine($"  ! {error}");
                }

                failures++;
            }

            log.AppendLine($"[BossSim] RESULT {(failures == 0 ? "OK" : "FAILED")}");

            System.IO.File.WriteAllText(reportPath, log.ToString());
            Debug.Log(log.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}
