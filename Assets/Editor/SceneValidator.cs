using Shooter.Enemies;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using Shooter.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shooter.EditorTools
{
    /// <summary>
    /// Structural checks for the generated scene. Logic tests cover gameplay rules;
    /// this covers the wiring that only exists in the serialized scene.
    /// Run with -executeMethod Shooter.EditorTools.SceneValidator.Validate
    /// </summary>
    public static class SceneValidator
    {
        private static int checks;
        private static int failures;

        public static void Validate()
        {
            checks = 0;
            failures = 0;

            if (!System.IO.File.Exists(ShooterSceneBuilder.ScenePath))
            {
                Debug.LogError("[Validate] Arena scene is missing.");
                Exit(1);
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ShooterSceneBuilder.ScenePath, OpenSceneMode.Single);

            GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
            WaveSpawner waveSpawner = Object.FindFirstObjectByType<WaveSpawner>();
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            PlayerHealth playerHealth = Object.FindFirstObjectByType<PlayerHealth>();
            Weapon weapon = Object.FindFirstObjectByType<Weapon>();
            HUDController hud = Object.FindFirstObjectByType<HUDController>();
            DamageFlash damageFlash = Object.FindFirstObjectByType<DamageFlash>();
            Camera camera = Object.FindFirstObjectByType<Camera>();

            Check(scene.IsValid(), "arena scene opens");
            Check(gameManager != null, "scene contains a GameManager");
            Check(waveSpawner != null, "scene contains a WaveSpawner");
            Check(player != null, "scene contains a PlayerController");
            Check(playerHealth != null, "scene contains a PlayerHealth");
            Check(weapon != null, "scene contains a Weapon");
            Check(hud != null, "scene contains the HUD controller");
            Check(damageFlash != null, "scene contains the damage flash");
            Check(camera != null, "scene contains a camera");

            if (gameManager != null)
            {
                Check(gameManager.playerHealth != null, "GameManager is wired to player health");
                Check(gameManager.playerController != null, "GameManager is wired to the player controller");
                Check(gameManager.waveSpawner != null, "GameManager is wired to the wave spawner");
            }

            if (waveSpawner != null)
            {
                Check(waveSpawner.enemyPrefab != null, "WaveSpawner has an enemy prefab");
                Check(waveSpawner.playerTarget != null, "WaveSpawner has a player target");
                Check(waveSpawner.spawnPoints != null && waveSpawner.spawnPoints.Length == 8,
                    "WaveSpawner has eight spawn points");
            }

            if (player != null)
            {
                Check(player.GetComponent<CharacterController>() != null, "player uses a CharacterController");
                Check(player.gameObject.layer == 2, "player is on the Ignore Raycast layer");
                Check(player.cameraPivot != null, "player camera pivot is wired");
                Check(player.playerCamera != null, "player camera is wired");
            }

            if (camera != null)
            {
                Check(camera.CompareTag("MainCamera"), "camera is tagged MainCamera");
                Check(camera.GetComponent<AudioListener>() != null, "camera has an AudioListener");
            }

            if (weapon != null)
            {
                Check(weapon.aimCamera != null, "weapon aim camera is wired");
                Check(weapon.player != null, "weapon player reference is wired");
                Check(weapon.muzzle != null, "weapon muzzle is wired");
                Check(weapon.muzzleFlash != null, "weapon muzzle flash is wired");
                Check(weapon.muzzleLight != null, "weapon muzzle light is wired");
                Check(weapon.tracer != null, "weapon tracer is wired");
                Check(weapon.audioSource != null, "weapon audio source is wired");
                Check(weapon.impactMaterial != null, "weapon impact material is wired");
            }

            if (hud != null)
            {
                Check(hud.playerHealth != null && hud.weapon != null && hud.gameManager != null &&
                      hud.waveSpawner != null, "HUD gameplay references are wired");
                Check(hud.healthFill != null && hud.healthText != null && hud.ammoText != null &&
                      hud.reloadText != null, "HUD health/ammo widgets are wired");
                Check(hud.waveText != null && hud.statusText != null && hud.scoreText != null,
                    "HUD wave/score widgets are wired");
                Check(hud.gameOverPanel != null && hud.gameOverScoreText != null && hud.hintText != null,
                    "HUD game-over widgets are wired");
            }

            if (damageFlash != null)
            {
                Check(damageFlash.playerHealth != null, "damage flash is wired to player health");
                Check(damageFlash.flashImage != null, "damage flash image is wired");
            }

            if (waveSpawner != null && waveSpawner.enemyPrefab != null)
            {
                GameObject prefab = waveSpawner.enemyPrefab;
                Check(prefab.GetComponent<EnemyAI>() != null, "enemy prefab has EnemyAI");
                Check(prefab.GetComponent<EnemyHealth>() != null, "enemy prefab has EnemyHealth");
                Check(prefab.GetComponent<CharacterController>() != null, "enemy prefab has a CharacterController");
            }

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            Check(buildScenes.Length > 0 && buildScenes[0].path == ShooterSceneBuilder.ScenePath,
                "arena is the first scene in build settings");

            Debug.Log($"[Validate] Finished: {checks - failures}/{checks} checks passed, {failures} failed.");
            Exit(failures == 0 ? 0 : 1);
        }

        private static void Check(bool condition, string description)
        {
            checks++;

            if (condition)
            {
                Debug.Log($"[Validate] PASS — {description}");
                return;
            }

            failures++;
            Debug.LogError($"[Validate] FAIL — {description}");
        }

        private static void Exit(int code)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
