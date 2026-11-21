# Changelog

All notable changes to **Signal Foundry**, the Shooter visual overhaul.

## [Unreleased] — Signal Foundry visual overhaul

### Added
- 26 original, editable Blender FBX exports covering the player view, actors,
  arena, pickups and FX.
- Six HUD PNG sprites authored by the lead (reticle, horizontal and vertical
  reticle bars, hit marker, plate and phase pip), rendered from the editable
  `assets_src/HUD.blend` via `assets_src/build_hud.py`.

### Verified
- Blender import and pose QA passed on the exported models.

### Not complete
- Unity gameplay integration, build and play remain unfinished. Both attempts
  were stopped when the DeepSeek OpenCode free quota ran out.
- The HUD is not wired into gameplay yet.
