"""Build five distinct enemy models for the Shooter project.

Run headless:
    blender -b -P Tools/Blender/build_enemies.py

Each variant is exported as its own FBX. Models are authored with the face
toward Blender -Y (Unity +Z after the standard FBX axis conversion), feet at
Z=0, and a named joint hierarchy:

    Model
      Torso          (pivot at the waist)
        Head         (pivot at the neck)
        ArmL/ArmR    (pivots at the shoulders)
      LegL/LegR      (pivots at the hips)

EnemyAnimator.cs animates those pivots. Part origins are placed at the joints
so rotating a transform swings the limb naturally.
"""

import math
import os

import bpy
from mathutils import Matrix, Vector

ROOT = "/home/lofi/shooterr"
OUT_DIR = os.path.join(ROOT, "Assets", "Models", "Enemies")
os.makedirs(OUT_DIR, exist_ok=True)


# ----------------------------------------------------------------- helpers

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_bevel(obj, width=0.02, segments=2):
    if width <= 0.0:
        return

    modifier = obj.modifiers.new("Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    modifier.angle_limit = math.radians(40.0)

    select_only(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def parent_object(obj, parent):
    if parent is None:
        return

    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_world = world


def make_material(name, color, metallic=0.6, roughness=0.4,
                  emission=None, emission_strength=0.0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")

    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness

        if emission is not None:
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = emission_strength

    return material


def make_palette(prefix, body_color, glow_color):
    body = make_material(prefix + "Body", body_color, 0.75, 0.34)
    dark = make_material(prefix + "Dark", (0.022, 0.025, 0.030), 0.72, 0.42)
    glow = make_material(prefix + "Glow", glow_color, 0.2, 0.25,
                         emission=glow_color, emission_strength=5.5)
    return body, dark, glow


def new_model():
    model = bpy.data.objects.new("Model", None)
    bpy.context.collection.objects.link(model)
    model.empty_display_type = "PLAIN_AXES"
    model.empty_display_size = 0.25
    return model


def add_box(name, joint, offset, size, material, parent,
            rotation=(0.0, 0.0, 0.0), bevel=0.018):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=joint)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    obj.rotation_euler = rotation

    select_only(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Shift the mesh away from the joint; the object origin stays at the joint.
    obj.data.transform(Matrix.Translation(Vector(offset)))

    apply_bevel(obj, bevel)
    obj.data.materials.append(material)
    parent_object(obj, parent)
    return obj


def add_cylinder(name, joint, offset, radius, depth, material, parent,
                 axis="Z", vertices=16, bevel=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius,
                                        depth=depth, location=joint)
    obj = bpy.context.object
    obj.name = name

    if axis == "Y":
        obj.rotation_euler = (math.pi * 0.5, 0.0, 0.0)
    elif axis == "X":
        obj.rotation_euler = (0.0, math.pi * 0.5, 0.0)

    select_only(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    obj.data.transform(Matrix.Translation(Vector(offset)))

    if bevel > 0.0:
        apply_bevel(obj, bevel, 2)

    for polygon in obj.data.polygons:
        polygon.use_smooth = True

    obj.data.materials.append(material)
    parent_object(obj, parent)
    return obj


def add_cone(name, joint, offset, radius, depth, material, parent,
             rotation=(0.0, 0.0, 0.0), vertices=8):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=0.0,
                                    depth=depth, location=joint)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = rotation

    select_only(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    obj.data.transform(Matrix.Translation(Vector(offset)))
    obj.data.materials.append(material)
    parent_object(obj, parent)
    return obj


# ----------------------------------------------------------------- variants

def build_trooper():
    body, dark, glow = make_palette("Trooper", (0.42, 0.075, 0.085), (1.0, 0.55, 0.18))
    model = new_model()

    waist = (0.0, 0.0, 0.95)
    torso = add_box("Torso", waist, (0.0, 0.0, 0.30), (0.52, 0.32, 0.62), body, model)
    add_box("ChestPlate", waist, (0.0, -0.18, 0.33), (0.44, 0.10, 0.34), dark, torso)
    add_box("ChestCore", waist, (0.0, -0.24, 0.33), (0.12, 0.03, 0.12), glow, torso, bevel=0.0)
    add_box("Backpack", waist, (0.0, 0.24, 0.30), (0.34, 0.16, 0.38), dark, torso)
    add_box("PauldronL", (-0.34, 0.0, 1.47), (0.0, 0.0, 0.0), (0.28, 0.28, 0.18), body, torso, rotation=(0, 0, 8))
    add_box("PauldronR", (0.34, 0.0, 1.47), (0.0, 0.0, 0.0), (0.28, 0.28, 0.18), body, torso, rotation=(0, 0, -8))

    head = add_box("Head", (0.0, 0.0, 1.60), (0.0, 0.0, 0.16), (0.32, 0.30, 0.32), body, torso)
    add_box("Visor", (0.0, 0.0, 1.60), (0.0, -0.16, 0.17), (0.24, 0.035, 0.075), glow, head, bevel=0.0)
    add_box("Crest", (0.0, 0.0, 1.60), (0.0, 0.04, 0.34), (0.08, 0.22, 0.10), dark, head, bevel=0.01)

    arm_l = add_box("ArmL", (-0.35, 0.0, 1.45), (-0.03, 0.0, -0.29), (0.16, 0.18, 0.58), body, torso, rotation=(0, 0, -5))
    arm_r = add_box("ArmR", (0.35, 0.0, 1.45), (0.03, 0.0, -0.29), (0.16, 0.18, 0.58), body, torso, rotation=(0, 0, 5))
    add_box("ClawL", (-0.35, 0.0, 1.45), (-0.04, -0.03, -0.63), (0.17, 0.22, 0.16), dark, arm_l)
    add_box("ClawR", (0.35, 0.0, 1.45), (0.04, -0.03, -0.63), (0.17, 0.22, 0.16), dark, arm_r)

    leg_l = add_box("LegL", (-0.17, 0.0, 0.95), (0.0, 0.0, -0.45), (0.20, 0.22, 0.86), body, model)
    leg_r = add_box("LegR", (0.17, 0.0, 0.95), (0.0, 0.0, -0.45), (0.20, 0.22, 0.86), body, model)
    add_box("BootL", (-0.17, 0.0, 0.95), (0.0, -0.06, -0.86), (0.22, 0.30, 0.16), dark, leg_l)
    add_box("BootR", (0.17, 0.0, 0.95), (0.0, -0.06, -0.86), (0.22, 0.30, 0.16), dark, leg_r)
    add_box("KneeL", (-0.17, 0.0, 0.95), (0.0, -0.12, -0.42), (0.18, 0.05, 0.16), dark, leg_l)
    add_box("KneeR", (0.17, 0.0, 0.95), (0.0, -0.12, -0.42), (0.18, 0.05, 0.16), dark, leg_r)

    return model


def build_runner():
    body, dark, glow = make_palette("Runner", (0.42, 0.085, 0.52), (1.0, 0.26, 0.82))
    model = new_model()

    waist = (0.0, -0.05, 0.82)
    torso = add_box("Torso", waist, (0.0, 0.0, 0.24), (0.38, 0.26, 0.48), body, model, rotation=(math.radians(9), 0, 0))
    add_box("ChestPlate", waist, (0.0, -0.14, 0.28), (0.32, 0.07, 0.26), dark, torso)
    add_box("ChestCore", waist, (0.0, -0.19, 0.26), (0.07, 0.02, 0.07), glow, torso, bevel=0.0)
    add_box("SpineSpike", waist, (0.0, 0.15, 0.30), (0.06, 0.08, 0.32), dark, torso, rotation=(math.radians(-25), 0, 0))
    add_box("PauldronL", (-0.24, 0.0, 1.26), (0.0, 0.0, 0.0), (0.18, 0.18, 0.13), dark, torso)
    add_box("PauldronR", (0.24, 0.0, 1.26), (0.0, 0.0, 0.0), (0.18, 0.18, 0.13), dark, torso)

    head = add_box("Head", (0.0, -0.10, 1.36), (0.0, 0.0, 0.12), (0.24, 0.22, 0.26), body, torso)
    add_box("Visor", (0.0, -0.10, 1.36), (0.0, -0.12, 0.13), (0.18, 0.03, 0.055), glow, head, bevel=0.0)
    add_cone("HornL", (0.0, -0.10, 1.36), (-0.10, 0.04, 0.24), 0.035, 0.20, dark, head, rotation=(math.radians(20), 0, math.radians(20)))
    add_cone("HornR", (0.0, -0.10, 1.36), (0.10, 0.04, 0.24), 0.035, 0.20, dark, head, rotation=(math.radians(20), 0, math.radians(-20)))

    arm_l = add_box("ArmL", (-0.26, 0.0, 1.24), (-0.02, 0.0, -0.36), (0.13, 0.15, 0.70), body, torso, rotation=(0, 0, -4))
    arm_r = add_box("ArmR", (0.26, 0.0, 1.24), (0.02, 0.0, -0.36), (0.13, 0.15, 0.70), body, torso, rotation=(0, 0, 4))
    add_box("ClawL", (-0.26, 0.0, 1.24), (-0.02, -0.02, -0.76), (0.15, 0.20, 0.14), dark, arm_l)
    add_box("ClawR", (0.26, 0.0, 1.24), (0.02, -0.02, -0.76), (0.15, 0.20, 0.14), dark, arm_r)

    leg_l = add_box("LegL", (-0.13, 0.0, 0.82), (0.0, 0.0, -0.40), (0.15, 0.17, 0.80), body, model)
    leg_r = add_box("LegR", (0.13, 0.0, 0.82), (0.0, 0.0, -0.40), (0.15, 0.17, 0.80), body, model)
    add_box("BootL", (-0.13, 0.0, 0.82), (0.0, -0.08, -0.78), (0.17, 0.28, 0.11), dark, leg_l)
    add_box("BootR", (0.13, 0.0, 0.82), (0.0, -0.08, -0.78), (0.17, 0.28, 0.11), dark, leg_r)

    return model


def build_bulwark():
    body, dark, glow = make_palette("Bulwark", (0.16, 0.31, 0.37), (0.16, 0.85, 1.0))
    model = new_model()

    waist = (0.0, 0.0, 0.92)
    torso = add_box("Torso", waist, (0.0, 0.0, 0.34), (0.76, 0.44, 0.70), body, model)
    add_box("ChestPlate", waist, (0.0, -0.24, 0.36), (0.64, 0.12, 0.44), dark, torso)
    add_box("ChestCore", waist, (0.0, -0.31, 0.36), (0.16, 0.03, 0.16), glow, torso, bevel=0.0)
    add_box("Backpack", waist, (0.0, 0.30, 0.34), (0.52, 0.18, 0.50), dark, torso)
    add_box("PauldronL", (-0.46, 0.0, 1.55), (0.0, 0.0, 0.0), (0.36, 0.36, 0.24), body, torso, rotation=(0, 0, 6))
    add_box("PauldronR", (0.46, 0.0, 1.55), (0.0, 0.0, 0.0), (0.36, 0.36, 0.24), body, torso, rotation=(0, 0, -6))

    head = add_box("Head", (0.0, 0.0, 1.64), (0.0, 0.0, 0.13), (0.28, 0.26, 0.28), body, torso)
    add_box("Visor", (0.0, 0.0, 1.64), (0.0, -0.14, 0.14), (0.22, 0.03, 0.07), glow, head, bevel=0.0)

    arm_l = add_box("ArmL", (-0.48, 0.0, 1.49), (-0.02, 0.0, -0.29), (0.22, 0.24, 0.58), body, torso)
    arm_r = add_box("ArmR", (0.48, 0.0, 1.49), (0.02, 0.0, -0.29), (0.22, 0.24, 0.58), body, torso)
    add_box("ShieldPlate", (-0.48, 0.0, 1.49), (-0.12, -0.36, -0.30), (0.62, 0.08, 0.92), body, arm_l, bevel=0.025)
    add_box("ShieldTrim", (-0.48, 0.0, 1.49), (-0.12, -0.41, -0.30), (0.52, 0.03, 0.76), dark, arm_l, bevel=0.01)
    add_box("ShieldGlow", (-0.48, 0.0, 1.49), (-0.12, -0.43, -0.30), (0.30, 0.02, 0.06), glow, arm_l, bevel=0.0)

    leg_l = add_box("LegL", (-0.24, 0.0, 0.92), (0.0, 0.0, -0.44), (0.26, 0.30, 0.84), body, model)
    leg_r = add_box("LegR", (0.24, 0.0, 0.92), (0.0, 0.0, -0.44), (0.26, 0.30, 0.84), body, model)
    add_box("BootL", (-0.24, 0.0, 0.92), (0.0, -0.08, -0.84), (0.30, 0.38, 0.18), dark, leg_l)
    add_box("BootR", (0.24, 0.0, 0.92), (0.0, -0.08, -0.84), (0.30, 0.38, 0.18), dark, leg_r)

    return model


def build_marksman():
    body, dark, glow = make_palette("Marksman", (0.07, 0.34, 0.26), (0.30, 1.0, 0.52))
    model = new_model()

    waist = (0.0, 0.0, 0.95)
    torso = add_box("Torso", waist, (0.0, 0.0, 0.32), (0.38, 0.28, 0.64), body, model)
    add_box("ChestPlate", waist, (0.0, -0.16, 0.34), (0.32, 0.08, 0.36), dark, torso)
    add_box("ChestCore", waist, (0.0, -0.21, 0.32), (0.08, 0.02, 0.08), glow, torso, bevel=0.0)
    add_box("Backpack", waist, (0.0, 0.21, 0.32), (0.26, 0.14, 0.42), dark, torso)
    add_cylinder("Antenna", waist, (0.12, 0.22, 0.66), 0.012, 0.36, dark, torso, axis="Z", vertices=8)
    add_box("PauldronL", (-0.25, 0.0, 1.48), (0.0, 0.0, 0.0), (0.20, 0.20, 0.13), body, torso)
    add_box("PauldronR", (0.25, 0.0, 1.48), (0.0, 0.0, 0.0), (0.20, 0.20, 0.13), body, torso)

    head = add_box("Head", (0.0, 0.0, 1.64), (0.0, 0.0, 0.18), (0.24, 0.24, 0.30), body, torso)
    add_box("Visor", (0.0, 0.0, 1.64), (0.0, -0.13, 0.18), (0.18, 0.03, 0.08), glow, head, bevel=0.0)
    add_cylinder("Lens", (0.0, 0.0, 1.64), (0.0, -0.16, 0.20), 0.035, 0.055, glow, head, axis="Y", vertices=12)
    add_cone("HeadSpike", (0.0, 0.0, 1.64), (0.0, 0.06, 0.34), 0.03, 0.18, dark, head, rotation=(math.radians(-15), 0, 0))

    arm_l = add_box("ArmL", (-0.27, 0.0, 1.46), (-0.01, 0.0, -0.30), (0.13, 0.14, 0.60), body, torso)
    arm_r = add_box("ArmR", (0.27, 0.0, 1.46), (0.01, 0.0, -0.30), (0.14, 0.15, 0.60), body, torso)
    add_cylinder("Cannon", (0.27, 0.0, 1.46), (0.02, -0.10, -0.52), 0.07, 0.44, body, arm_r, axis="Z", vertices=14)
    add_cylinder("CannonGlow", (0.27, 0.0, 1.46), (0.02, -0.10, -0.75), 0.045, 0.03, glow, arm_r, axis="Z", vertices=12)
    add_box("ClawL", (-0.27, 0.0, 1.46), (-0.02, -0.02, -0.60), (0.15, 0.18, 0.13), dark, arm_l)

    leg_l = add_box("LegL", (-0.14, 0.0, 0.95), (0.0, 0.0, -0.45), (0.15, 0.17, 0.86), body, model)
    leg_r = add_box("LegR", (0.14, 0.0, 0.95), (0.0, 0.0, -0.45), (0.15, 0.17, 0.86), body, model)
    add_box("BootL", (-0.14, 0.0, 0.95), (0.0, -0.08, -0.84), (0.17, 0.30, 0.12), dark, leg_l)
    add_box("BootR", (0.14, 0.0, 0.95), (0.0, -0.08, -0.84), (0.17, 0.30, 0.12), dark, leg_r)

    return model


def build_elite():
    body, dark, glow = make_palette("Elite", (0.48, 0.27, 0.055), (1.0, 0.55, 0.10))
    model = new_model()

    waist = (0.0, 0.0, 1.0)
    torso = add_box("Torso", waist, (0.0, 0.0, 0.34), (0.58, 0.36, 0.70), body, model)
    add_box("ChestPlate", waist, (0.0, -0.20, 0.36), (0.50, 0.10, 0.42), dark, torso)
    add_box("ChestCore", waist, (0.0, -0.26, 0.36), (0.13, 0.03, 0.13), glow, torso, bevel=0.0)
    add_box("Backpack", waist, (0.0, 0.26, 0.34), (0.40, 0.16, 0.44), dark, torso)
    add_box("Cape", waist, (0.0, 0.36, 0.02), (0.54, 0.04, 1.02), dark, torso, rotation=(math.radians(-8), 0, 0), bevel=0.01)
    add_box("PauldronL", (-0.38, 0.0, 1.54), (0.0, 0.0, 0.0), (0.32, 0.32, 0.20), body, torso, rotation=(0, 0, 8))
    add_box("PauldronR", (0.38, 0.0, 1.54), (0.0, 0.0, 0.0), (0.32, 0.32, 0.20), body, torso, rotation=(0, 0, -8))
    add_cone("ShoulderSpikeL", (-0.46, 0.0, 1.58), (-0.08, 0.0, 0.16), 0.055, 0.28, dark, torso, rotation=(0, math.radians(-25), 0))
    add_cone("ShoulderSpikeR", (0.46, 0.0, 1.58), (0.08, 0.0, 0.16), 0.055, 0.28, dark, torso, rotation=(0, math.radians(25), 0))

    head = add_box("Head", (0.0, 0.0, 1.72), (0.0, 0.0, 0.16), (0.30, 0.28, 0.30), body, torso)
    add_box("Visor", (0.0, 0.0, 1.72), (0.0, -0.15, 0.18), (0.22, 0.03, 0.08), glow, head, bevel=0.0)
    add_cone("HornL", (0.0, 0.0, 1.72), (-0.15, 0.02, 0.30), 0.055, 0.42, dark, head, rotation=(math.radians(-15), 0, math.radians(28)))
    add_cone("HornR", (0.0, 0.0, 1.72), (0.15, 0.02, 0.30), 0.055, 0.42, dark, head, rotation=(math.radians(-15), 0, math.radians(-28)))

    arm_l = add_box("ArmL", (-0.40, 0.0, 1.52), (-0.03, 0.0, -0.32), (0.19, 0.21, 0.64), body, torso)
    arm_r = add_box("ArmR", (0.40, 0.0, 1.52), (0.03, 0.0, -0.32), (0.19, 0.21, 0.64), body, torso)
    add_box("ClawL", (-0.40, 0.0, 1.52), (-0.03, -0.03, -0.68), (0.20, 0.24, 0.16), dark, arm_l)
    add_box("ClawR", (0.40, 0.0, 1.52), (0.03, -0.03, -0.68), (0.20, 0.24, 0.16), dark, arm_r)

    leg_l = add_box("LegL", (-0.20, 0.0, 1.0), (0.0, 0.0, -0.48), (0.23, 0.25, 0.92), body, model)
    leg_r = add_box("LegR", (0.20, 0.0, 1.0), (0.0, 0.0, -0.48), (0.23, 0.25, 0.92), body, model)
    add_box("BootL", (-0.20, 0.0, 1.0), (0.0, -0.07, -0.93), (0.26, 0.34, 0.16), dark, leg_l)
    add_box("BootR", (0.20, 0.0, 1.0), (0.0, -0.07, -0.93), (0.26, 0.34, 0.16), dark, leg_r)

    return model


# ----------------------------------------------------------------- export

VARIANTS = [
    ("Trooper", build_trooper),
    ("Runner", build_runner),
    ("Bulwark", build_bulwark),
    ("Marksman", build_marksman),
    ("Elite", build_elite),
]

for name, builder in VARIANTS:
    reset_scene()
    builder()
    bpy.ops.object.select_all(action="SELECT")

    path = os.path.join(OUT_DIR, f"Enemy{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        object_types={"EMPTY", "MESH"},
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
    )
    print(f"[enemy] exported {path}")
