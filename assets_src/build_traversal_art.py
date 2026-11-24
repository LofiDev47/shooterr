"""Build the Foundry catwalk kit and drone.

Run from the project root with Blender in background:
    blender -b --factory-startup --python assets_src/build_traversal_art.py
"""

from math import radians
from pathlib import Path
import sys

import bpy
from mathutils import Vector

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))

from build_foundry import OUTPUT, export, loft, material, mesh, panel, pivot, reset, ring, save


def block(name, location, size, mat, parent, bevel=0.012):
    x, y, z = (dimension / 2 for dimension in size)
    vertices = [(-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
                (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z)]
    faces = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5),
             (2, 3, 7, 6), (3, 0, 4, 7)]
    return mesh(name, vertices, faces, location, mat, parent, bevel)


def strut(name, start, end, width, depth, mat, parent):
    start, end = Vector(start), Vector(end)
    axis = end - start
    part = block(name, (start + end) / 2, (width, depth, axis.length), mat, parent, 0.009)
    part.rotation_euler = axis.to_track_quat("Z", "Y").to_euler()


def batch_meshes(root):
    """Bake the machined edges and batch static pieces by finish."""
    groups = {}
    for part in root.children:
        groups.setdefault(part.data.materials[0], []).append(part)

    for finish, parts in groups.items():
        bpy.ops.object.select_all(action="DESELECT")
        for part in parts:
            part.select_set(True)
        bpy.context.view_layer.objects.active = parts[0]
        bpy.ops.object.convert(target="MESH")
        bpy.ops.object.join()
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        bpy.context.object.name = finish.name + " Mesh"


def export_modules(modules):
    bpy.ops.object.select_all(action="DESELECT")
    for root in modules:
        authored_location = root.location.copy()
        root.location = (0, 0, 0)
        for part in (root, *root.children):
            part.select_set(True)
        bpy.ops.export_scene.fbx(
            filepath=str(OUTPUT / (root.name + ".fbx")), use_selection=True,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", object_types={"EMPTY", "MESH"},
            add_leaf_bones=False, bake_anim=False,
        )
        for part in (root, *root.children):
            part.select_set(False)
        root.location = authored_location


def build_catwalk():
    reset()
    frame = material("FoundryFrame", (0.048, 0.073, 0.088), 0.65)
    ceramic = material("FoundryCeramic", (0.44, 0.50, 0.50))
    floor = material("FoundryFloor", (0.17, 0.22, 0.24))
    grip = material("FoundryFloorAlt", (0.148, 0.196, 0.217))
    signal = material("CatwalkMarkerGlow", (0.09, 0.56, 0.53), 0.2, 2)
    safety = material("CatwalkSafetyPaint", (0.58, 0.27, 0.065), 0.12)

    # Physical dimensions. Origin is on the walking surface, at midspan.
    span = pivot("CatwalkSpan")
    block("Deck chassis", (0, 0, -0.135), (1.94, 3.895, 0.21), frame, span, 0.02)
    for longitudinal in (-1.461, -0.487, 0.487, 1.461):
        for side in (-1, 1):
            block("Pressed steel tread", (side * 0.505, longitudinal, -0.036),
                  (0.993, 0.953, 0.072), floor, span, 0.012)
            for offset in (-0.29, 0, 0.29):
                block("Recessed grip bar", (side * 0.505, longitudinal + offset, 0.001),
                      (0.76, 0.027, 0.009), grip, span, 0.003)

    for side in (-1, 1):
        x = side * 1.09
        block("Folded edge", (x, 0, -0.043), (0.21, 3.895, 0.098), ceramic, span, 0.014)
        block("Lower truss chord", (x, 0, -0.34), (0.18, 3.87, 0.10), frame, span, 0.014)
        for index in range(4):
            start = index * 0.96 - 1.92
            end = start + 0.96
            z0, z1 = (-0.14, -0.33) if index % 2 == 0 else (-0.33, -0.14)
            strut("Diagonal web", (x, start, z0), (x, end, z1), 0.105, 0.115, ceramic, span)
        for y in (-1.89, 0, 1.89):
            block("Splice plate", (side * 1.16, y, -0.185), (0.07, 0.105, 0.285),
                  frame, span)
        for y in (-0.96, 0.96):
            block("Recessed marker housing", (side * 1.16, y, -0.122), (0.08, 0.61, 0.095),
                  frame, span)
            block("Cyan position marker", (side * 1.203, y, -0.122), (0.014, 0.41, 0.029),
                  signal, span, 0.004)
        for y in (-1.52, 1.52):
            block("Edge hazard marking", (x, y, 0.008), (0.13, 0.24, 0.008), safety, span, 0.002)

    # x = across the deck, -Y = away from the high end toward the corner platform.
    landing = pivot("CatwalkLanding")
    for y, height in ((-0.27, 0.4), (-0.83, 0.2)):
        block("Stair tread", (0, y, height / 2), (2.38, 0.56, height), frame, landing)
        block("Pressed steel tread cap", (0, y + 0.009, height - 0.025),
              (2.34, 0.526, 0.05), floor, landing, 0.009)
        block("Safety nosing", (0, y - 0.266, height - 0.025), (2.08, 0.032, 0.038),
              safety, landing, 0.004)
        for side in (-1, 1):
            block("Folded stair cheek", (side * 1.12, y, height / 2), (0.14, 0.54, height - 0.025),
                  ceramic, landing)

    # One column assembly joins the deck to the floor at the existing cover location.
    pier = pivot("CatwalkPier")
    block("Anchor shoe", (0, 0, 0.09), (0.77, 1.81, 0.18), frame, pier, 0.025)
    block("Structural core", (0, 0, 1.22), (0.66, 1.70, 2.18), frame, pier, 0.04)
    block("Saddle cap", (0, 0, 2.27), (0.87, 1.82, 0.26), frame, pier, 0.02)
    for side in (-1, 1):
        for y in (-0.61, 0.61):
            block("Ceramic column guard", (0.328 * side, y, 1.13), (0.09, 0.40, 1.83),
                  ceramic, pier, 0.026)
        block("Recessed signal housing", (0.34 * side, 0, 1.15), (0.045, 0.38, 1.24), grip, pier)
        block("Warning stripe", (0.365 * side, 0, 0.53), (0.012, 0.28, 0.089), safety, pier, 0.003)

    modules = (span, landing, pier)
    for module, position in zip(modules, ((0, 0, 0.45), (3.4, 0, 0), (5.5, 0, 0))):
        module.location = position
    save("CatwalkKit")
    for module in modules:
        batch_meshes(module)
    export_modules(modules)


def build_drone():
    reset()
    armor = material("DroneArmor", (0.31, 0.42, 0.47), 0.5)
    frame = material("DroneGraphite", (0.040, 0.055, 0.070), 0.65)
    trim = material("DroneAlloy", (0.40, 0.44, 0.44), 0.55)
    copper = material("DroneCopper", (0.49, 0.20, 0.073), 0.5)
    optic = material("DroneOpticGlow", (1.0, 0.24, 0.052), 0.2, 3)
    exhaust = material("DroneThrustGlow", (0.19, 0.70, 1.0), 0.2, 2.4)

    hull = pivot("DroneHull")
    loft("Ventral chassis", (0, 0, 0), [
        (0.22, 0.31, 0.47, 0.08), (0.35, 0.62, 0.83, 0.02),
        (0.56, 0.66, 0.91, 0.03), (0.66, 0.43, 0.76, 0.07),
    ], frame, hull, 0.017)
    loft("Dorsal armor", (0, 0, 0), [
        (0.54, 0.68, 0.84, 0.05), (0.64, 0.69, 0.84, 0.06),
        (0.79, 0.46, 0.62, 0.09), (0.82, 0.29, 0.48, 0.12),
    ], armor, hull, 0.015)
    panel("Dorsal spine", (0, 0.14, 0.817), (0.13, 0.41, 0.055),
          trim, hull, 0.78, 0.009)
    for y in (0.12, 0.22, 0.32):
        block("Rear heat sink", (0, y, 0.798), (0.29, 0.032, 0.034), frame, hull, 0.004)

    panel("Sensor bezel", (0, -0.417, 0.58), (0.50, 0.15, 0.155), frame, hull, 0.94)
    lens = panel("Wide sensor lens", (0, -0.507, 0.574), (0.39, 0.038, 0.069), optic, hull, 0.9, 0.005)
    lens.rotation_euler.x = radians(12)
    for x in (-0.122, 0.122):
        divider = block("Lens divider", (x, -0.525, 0.574), (0.016, 0.012, 0.061), frame, hull, 0.003)
        divider.rotation_euler.x = lens.rotation_euler.x
    brow = panel("Sensor hood", (0, -0.422, 0.657), (0.52, 0.20, 0.083),
                 armor, hull, 0.85)
    brow.rotation_euler.x = radians(-10)
    panel("Barrel housing", (0, -0.405, 0.438), (0.25, 0.35, 0.17), trim, hull, 0.86)
    bore = ring("Muzzle shroud", (0, -0.584, 0.47), 0.084, 0.022, 0.084, frame, hull, 12)
    bore.rotation_euler.x = radians(90)
    lens = ring("Muzzle charge ring", (0, -0.630, 0.47), 0.058, 0.009, 0.008, optic, hull, 12)
    lens.rotation_euler.x = radians(90)

    for side in (-1, 1):
        # Ducted lift pods are tucked against the hull so the silhouette reads from below.
        x, y = 0.415 * side, 0.148
        wing = panel("Swept shoulder armor", (0.33 * side, 0.068, 0.579),
                     (0.44, 0.52, 0.17), armor, hull, 0.85)
        wing.rotation_euler.z = radians(side * 23)
        loft("Lift duct", (x, y, 0), [
            (0.25, 0.30, 0.31, 0), (0.35, 0.42, 0.47, 0),
            (0.52, 0.40, 0.49, 0), (0.60, 0.28, 0.39, 0.02),
        ], frame, hull, 0.013)
        panel("Outer pod armor", (side * 0.54, y + 0.032, 0.48), (0.16, 0.40, 0.16),
              armor, hull, 0.83)
        cheek = panel("Replaceable pod guard", (side * 0.422, y - 0.216, 0.416),
                      (0.235, 0.078, 0.13), trim, hull, 0.80)
        cheek.rotation_euler.x = radians(16)
        for dy in (-0.12, 0.0, 0.12):
            fin = panel("Copper cooling fin", (side * 0.58, y + dy, 0.50),
                        (0.105, 0.045, 0.067), copper, hull, 0.8, 0.005)
            fin.rotation_euler.z = radians(side * 24)
        ring("Exhaust bell", (x, y, 0.275), 0.165, 0.029, 0.095, trim, hull, 12)
        ring("Open thrust ring", (x, y, 0.258), 0.129, 0.019, 0.018, exhaust, hull, 12)
        panel("Recessed exhaust core", (x, y, 0.303), (0.14, 0.14, 0.023),
              exhaust, hull, 0.85, 0.004)
        for dx in (-0.045, 0.045):
            block("Exhaust vane", (x + dx, y, 0.247), (0.019, 0.18, 0.023), frame, hull, 0.003)

    panel("Belly armor", (0, -0.001, 0.241), (0.44, 0.62, 0.107), armor, hull, 1.18)
    for x in (-0.14, 0.14):
        panel("Ventral marker", (x, 0.03, 0.181), (0.025, 0.35, 0.015), optic, hull, 1, 0.003)

    # Preserve the individual parts in the editable file, bake exports for the game.
    save("EnemyDrone")
    batch_meshes(hull)
    export("EnemyDrone")


if __name__ == "__main__":
    build_catwalk()
    build_drone()
