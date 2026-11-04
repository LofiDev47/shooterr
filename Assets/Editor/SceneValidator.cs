using Shooter.Enemies;
using Shooter.Managers;
using Shooter.Player;
using Shooter.UI;
using Shooter.Upgrades;
using Shooter.Weapons;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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

            GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
            WaveSpawner waveSpawner = Object.FindAnyObjectByType<WaveSpawner>();
            PlayerController player = Object.FindAnyObjectByType<PlayerController>();
            PlayerHealth playerHealth = Object.FindAnyObjectByType<PlayerHealth>();
            Weapon weapon = Object.FindAnyObjectByType<Weapon>();
            HUDController hud = Object.FindAnyObjectByType<HUDController>();
            DamageFlash damageFlash = Object.FindAnyObjectByType<DamageFlash>();
            UpgradeManager upgradeManager = Object.FindAnyObjectByType<UpgradeManager>();
            Camera camera = Object.FindAnyObjectByType<Camera>();

            Check(scene.IsValid(), "arena scene opens");
            Check(gameManager != null, "scene contains a GameManager");
            Check(waveSpawner != null, "scene contains a WaveSpawner");
            Check(player != null, "scene contains a PlayerController");
            Check(playerHealth != null, "scene contains a PlayerHealth");
            Check(weapon != null, "scene contains a Weapon");
            Check(hud != null, "scene contains the HUD controller");
            Check(damageFlash != null, "scene contains the damage flash");
            Check(upgradeManager != null, "scene contains an UpgradeManager");
            Check(camera != null, "scene contains a camera");

            if (upgradeManager != null)
            {
                Check(upgradeManager.player != null, "UpgradeManager is wired to the player controller");
                Check(upgradeManager.playerHealth != null, "UpgradeManager is wired to player health");
                Check(upgradeManager.hud != null, "UpgradeManager is wired to the HUD");
                Check(upgradeManager.choicePanel != null, "UpgradeManager has a choice panel");
                Check(!upgradeManager.choicePanel.activeSelf, "the augment choice panel starts hidden");

                bool cardsWired = upgradeManager.cardTitles != null && upgradeManager.cardDescriptions != null &&
                    upgradeManager.cardTitles.Length == 3 && upgradeManager.cardDescriptions.Length == 3;

                if (cardsWired)
                {
                    cardsWired = upgradeManager.cardTitles.All(card => card != null) &&
                        upgradeManager.cardDescriptions.All(card => card != null);
                }

                Check(cardsWired, "the three augment cards are wired with title and description texts");
                Check(upgradeManager.choiceHintText != null, "the augment choice hint text is wired");
            }

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
                Check(waveSpawner.spawnPoints != null && waveSpawner.spawnPoints.Length >= 10,
                    "WaveSpawner has at least ten spawn points");
            }

            if (player != null)
            {
                Check(player.GetComponent<CharacterController>() != null, "player uses a CharacterController");
                Check(player.gameObject.layer == 2, "player is on the Ignore Raycast layer");
                Check(player.cameraPivot != null, "player camera pivot is wired");
                Check(player.playerCamera != null, "player camera is wired");
                Check(!player.IsSliding, "player starts not sliding");
                Check(!player.IsAiming, "player starts not aiming");
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
                Check(hud.toastText != null, "HUD toast text is wired");
            }

            AmmoPickup[] pickups = Object.FindObjectsByType<AmmoPickup>(FindObjectsInactive.Exclude);
            Check(pickups.Length >= 4, "arena contains ammo crates");

            bool cratesWired = pickups.Length > 0;

            foreach (AmmoPickup pickup in pickups)
            {
                if (pickup.weapon == null || pickup.player == null || pickup.hud == null)
                {
                    cratesWired = false;
                    break;
                }
            }

            Check(cratesWired, "every ammo crate is wired to weapon, player and HUD");

            if (damageFlash != null)
            {
                Check(damageFlash.playerHealth != null, "damage flash is wired to player health");
                Check(damageFlash.flashImage != null, "damage flash image is wired");
            }

            if (hud != null)
            {
                Check(hud.hitMarkerGroup != null, "HUD hit marker is wired");
                Check(hud.player != null, "HUD is wired to the player controller");

                bool crosshairWired = hud.crosshairBars != null && hud.crosshairBars.Length == 4;

                if (crosshairWired)
                {
                    foreach (RectTransform bar in hud.crosshairBars)
                    {
                        if (bar == null)
                        {
                            crosshairWired = false;
                        }
                    }
                }

                Check(crosshairWired, "HUD crosshair bars are wired for spread animation");
            }

            Volume volume = Object.FindAnyObjectByType<Volume>();
            Check(volume != null && volume.sharedProfile != null, "post-processing volume has a profile");

            Camera playerCamera = Object.FindAnyObjectByType<Camera>();

            if (playerCamera != null)
            {
                Check(playerCamera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing,
                    "player camera renders post-processing");
            }

            Debug.Log("[Validate] cooler-pass checks done.");

            if (waveSpawner != null && waveSpawner.enemyPrefab != null)
            {
                GameObject prefab = waveSpawner.enemyPrefab;
                Check(prefab.GetComponent<EnemyAI>() != null, "enemy prefab has EnemyAI");
                Check(prefab.GetComponent<EnemyHealth>() != null, "enemy prefab has EnemyHealth");
                Check(prefab.GetComponent<CharacterController>() != null, "enemy prefab has a CharacterController");

                Check(waveSpawner.ammoCachePrefab != null, "wave spawner has an elite ammo cache prefab");

                if (waveSpawner.ammoCachePrefab != null)
                {
                    AmmoPickup cache = waveSpawner.ammoCachePrefab.GetComponent<AmmoPickup>();
                    Check(cache != null, "ammo cache prefab has an AmmoPickup");
                    Check(cache != null && cache.AmmoAmount > 0, "ammo cache prefab carries ammo");
                }

                Renderer[] prefabRenderers = prefab.GetComponentsInChildren<Renderer>();
                Check(prefabRenderers.Length >= 8, "enemy prefab has a detailed multi-part model");

                // Visual pieces must not carry colliders; only the CharacterController should.
                bool visualColliders = prefab.GetComponentsInChildren<Collider>()
                    .Any(collider => !ReferenceEquals(collider, prefab.GetComponent<CharacterController>()));
                Check(!visualColliders, "enemy visual pieces are collider-free");
            }

            Weapon sceneWeapon = Object.FindAnyObjectByType<Weapon>();

            if (sceneWeapon != null)
            {
                Transform weaponModel = sceneWeapon.transform.Find("Model");
                Check(weaponModel != null, "weapon has a detailed model group");
                Check(weaponModel != null && weaponModel.transform.Find("MuzzleBrake") != null,
                    "weapon model has muzzle detail");
                Check(weaponModel != null && weaponModel.transform.Find("StatusGlow") != null,
                    "weapon model has a charge strip");
            }

            // --- arena layout ------------------------------------------------------
            GameObject[] platforms = FindArenaObjects("Platform ", false);
            GameObject[] ramps = FindArenaObjects("Ramp ", false);
            GameObject[] pad = FindArenaObjects("Central Pad", true);
            GameObject[] pillars = FindArenaObjects("Pillar", true);
            GameObject[] walls = FindArenaObjects("Wall ", false);

            Check(platforms.Length == 4, "arena has four raised corner platforms");
            Check(ramps.Length == 4, "arena has four platform ramps");
            Check(pad.Length == 1, "arena has a central pad");
            Check(pillars.Length == 4, "arena has four inner pillars");
            Check(walls.Length == 4, "arena has four perimeter walls");

            // Spawn points must be in open ground: not inside a wall or a platform.
            if (waveSpawner != null && waveSpawner.spawnPoints != null)
            {
                Check(waveSpawner.spawnPoints.Length >= 10, "arena has at least ten spawn points");

                bool allClear = true;

                foreach (Transform point in waveSpawner.spawnPoints)
                {
                    if (point == null)
                    {
                        allClear = false;
                        continue;
                    }

                    foreach (GameObject platform in platforms)
                    {
                        Vector3 delta = point.position - platform.transform.position;

                        if (Mathf.Abs(delta.x) < 4.2f && Mathf.Abs(delta.z) < 4.2f)
                        {
                            allClear = false;
                        }
                    }

                    if (Mathf.Abs(point.position.x) > 21f || Mathf.Abs(point.position.z) > 21f)
                    {
                        allClear = false;
                    }
                }

                Check(allClear, "every spawn point sits in open ground");
            }

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            Check(buildScenes.Length > 0 && buildScenes[0].path == ShooterSceneBuilder.ScenePath,
                "arena is the first scene in build settings");

            Debug.Log($"[Validate] Finished: {checks - failures}/{checks} checks passed, {failures} failed.");
            Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>
        /// Finds scene objects by exact name or by name prefix.
        /// </summary>
        private static GameObject[] FindArenaObjects(string nameOrPrefix, bool exact)
        {
            return Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include)
                .Where(gameObject => gameObject != null &&
                    (exact ? gameObject.name == nameOrPrefix : gameObject.name.StartsWith(nameOrPrefix)))
                .ToArray();
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
