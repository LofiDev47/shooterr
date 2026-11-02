# Shooter

A 3D first-person wave shooter built in Unity 6.6 (URP), generated entirely from C# —
the arena scene, materials, enemy prefab and HUD are produced by an Editor script, so the
project can be rebuilt from scratch on the command line.

![genre](https://img.shields.io/badge/genre-arena%20FPS-blue) ![engine](https://img.shields.io/badge/Unity-6000.6.7f1-black)

## Requirements

- **Unity 6000.6.7f1** with Linux build support (installed at
  `~/Unity/Hub/Editor/6000.6.7f1/`).
- A free Unity Personal license (already activated on this machine).
- Linux x86_64 to run the included build.
- No external assets or paid packages are used. Everything is built-in primitives
  (cubes, capsules, spheres) and generated materials.

## Open the project

**Unity Hub:** Add → select this folder (`/home/lofi/shooterr`) → open with Unity 6000.6.7f1.

**Command line (editor UI):**

```bash
~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity -projectPath "$(pwd)"
```

The playable scene is `Assets/Scenes/Arena.unity` and is first in Build Settings.

## Play the included build

```bash
./Builds/Linux/Shooter.x86_64
```

The game opens at 1600x900 in a window. The mouse is captured on start.

## Controls

| Input | Action |
|---|---|
| **W A S D** | Move |
| **Mouse** | Look |
| **Shift** (while moving forward) | Sprint |
| **Space** | Jump |
| **Left mouse** | Fire (automatic) |
| **R** | Reload |
| **Esc** | Release the mouse cursor |
| **Left click** (cursor released) | Capture the cursor again |
| **R / Enter** (on game over) | Restart the run |

## How the game works

- **Waves:** wave 1 starts after a short countdown with 3 enemies. Each wave adds 2 more
  (capped at 36), while health, speed and damage scale per wave. Spawning is throttled to
  14 enemies alive at once.
- **Enemies:** capsules that steer straight at you, avoid stacking on each other and
  sidestep when blocked by cover. They swipe for damage in melee range.
- **Weapon:** hitscan rifle, 30-round magazine with 150 reserve, ~9 rounds/second,
  1.5 s reload, slight spread, camera recoil, muzzle flash, tracer and bullet-hole decals.
  Firing with an empty magazine triggers a dry click and an automatic reload.
- **Player:** 100 HP. Damage flashes the screen red. At 0 HP the run ends, time freezes
  and the game-over screen appears.
- **Score:** 10 points per kill. The HUD shows health, ammo, wave, enemies remaining,
  countdown and score, plus a crosshair.
- **Audio:** no clips ship. `Assets/Scripts/Managers/AudioManager.cs` exposes named
  placeholders (`weaponFire`, `enemyDeath`, `waveStart`, …). Drop clips onto the
  **Managers** object in the scene and every existing hook starts playing them.

## Project layout

```
Assets/
├── Editor/
│   ├── ShooterSceneBuilder.cs   # builds arena, player, weapon, HUD, enemy prefab
│   ├── BuildScript.cs           # Linux player build entry point
│   ├── SelfTests.cs             # headless checks for health/ammo/reload logic
│   └── SceneValidator.cs        # checks the generated scene's wiring
├── Scripts/
│   ├── Combat/IDamageable.cs
│   ├── Player/PlayerController.cs, PlayerHealth.cs
│   ├── Weapons/Weapon.cs, WeaponState.cs
│   ├── Enemies/EnemyAI.cs, EnemyHealth.cs
│   ├── Managers/GameManager.cs, WaveSpawner.cs, AudioManager.cs
│   └── UI/HUDController.cs, DamageFlash.cs
├── Materials/                   # generated URP materials
├── Prefabs/Enemy.prefab         # generated enemy prefab
└── Scenes/Arena.unity           # generated playable scene
```

The scene is fully reproducible: delete `Assets/Scenes/Arena.unity`, rerun the builder,
and you get the same arena, wiring and HUD back.

## Rebuild from the command line

Set a convenient variable first:

```bash
UNITY=~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity
```

**1. Rebuild the arena scene** (regenerates materials, enemy prefab, scene and build list):

```bash
"$UNITY" -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod Shooter.EditorTools.ShooterSceneBuilder.Build \
  -logFile /tmp/shooter_scene.log
```

**2. Build the standalone player** into `Builds/Linux/`:

```bash
"$UNITY" -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod Shooter.EditorTools.ShooterBuild.BuildLinux \
  -logFile /tmp/shooter_build.log
```

**3. Run the logic self-tests** (ammo, fire rate, reload, health, death):

```bash
"$UNITY" -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod Shooter.EditorTools.SelfTests.RunAll \
  -logFile /tmp/shooter_tests.log
```

**4. Validate the scene wiring** (references, layers, build settings):

```bash
"$UNITY" -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod Shooter.EditorTools.SceneValidator.Validate \
  -logFile /tmp/shooter_validate.log
```

All four exit with a non-zero code on failure.

## Implementation notes

- **Input:** the project uses the **Input System** package (the template already had it
  installed and `activeInputHandler` is set to "Input System Package"). Controls are read
  directly from `Keyboard.current` / `Mouse.current`, so no `.inputactions` asset is needed.
- **Rendering:** URP 17.6. URP is assigned per quality level in
  `ProjectSettings/QualitySettings.asset`; the procedural skybox, fog, trilight ambient and
  one shadowed directional light are configured by the scene builder.
- **No NavMesh:** enemies use `CharacterController` steering with separation and stuck
  detection, so no bake step is required.
- **HUD:** uGUI (`UnityEngine.UI.Text`) with the built-in `LegacyRuntime.ttf` font, which
  avoids the TextMeshPro "import essentials" step that batch mode cannot perform.
- **Testing:** `SelfTests` and `SceneValidator` run in edit mode under `-batchmode`, so they
  work without a GPU. The shipped player was also smoke-tested headless for 30 s with no
  exceptions.
