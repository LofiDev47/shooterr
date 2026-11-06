"""Build "The Warden" - the shooter's boss - with authored animation clips.

Run headless:
    blender -b -P Tools/Blender/build_boss.py

The grunt enemies are beveled cubes posed by a sine wave. This model is built
instead from lofted superellipse profiles (tapered, curved solids with layered
armour), and every clip is authored as real keyframes: anticipation, contact,
impact, recovery.

Hierarchy (joint coordinates are world-space; parenting preserves world):

    Model
      LegL -> KneeL -> BootL
      LegR -> KneeR -> BootR
      Torso
        Head (crown, optics)
        PauldronL/R (static armour)
        ArmL -> ElbowL -> blade
        ArmR -> ElbowR -> cannon
        ExhaustL/R, Banner

Each clip is exported as its own FBX into Assets/Models/Boss/.
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = "/home/lofi/shooterr"
OUT_DIR = os.path.join(ROOT, "Assets", "Models", "Boss")
os.makedirs(OUT_DIR, exist_ok=True)

FPS = 30
TAU = math.pi * 2.0


# ------------------------------------------------------------------- scene

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish(obj, bevel=0.02, segments=2, smooth_angle=34.0, recalc=True):
    if recalc:
        mesh = obj.data
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

    if bevel > 0.0:
        modifier = obj.modifiers.new("Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = math.radians(42.0)
        select_only(obj)
        bpy.ops.object.modifier_apply(modifier=modifier.name)

    select_only(obj)

    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    except Exception:
        bpy.ops.object.shade_smooth()


def parent_keep(obj, parent):
    if parent is None:
        return

    # obj.location was just assigned, so force a depsgraph evaluation before
    # reading the world matrix; otherwise Blender hands back a stale identity
    # and every child collapses onto its parent's origin.
    bpy.context.view_layer.update()
    world = obj.matrix_world.copy()

    obj.parent = parent
    obj.matrix_world = world
    bpy.context.view_layer.update()


def place(obj, joint, offset, rotation):
    """Rotate locally, shift the mesh off the pivot, then pin the origin."""
    if rotation != (0.0, 0.0, 0.0):
        obj.rotation_euler = rotation
        select_only(obj)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    obj.data.transform(Matrix.Translation(Vector(offset)))
    obj.location = joint


# --------------------------------------------------------------- materials

def lit(name, color, roughness=0.42, metallic=0.72):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")

    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness

    return material


def emissive(name, color, strength=7.0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")

    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = 0.1
        bsdf.inputs["Roughness"].default_value = 0.28

        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = strength

    return material


# ------------------------------------------------------------ mesh helpers

def superellipse(n, rx, ry, power):
    points = []

    for i in range(n):
        angle = TAU * i / n
        cos_a, sin_a = math.cos(angle), math.sin(angle)
        x = math.copysign(abs(cos_a) ** (2.0 / power), cos_a) * rx
        y = math.copysign(abs(sin_a) ** (2.0 / power), sin_a) * ry
        points.append((x, y))

    return points


def S(z, rx, ry, cx=0.0, cy=0.0, power=4.0):
    return {"z": z, "rx": rx, "ry": ry, "cx": cx, "cy": cy, "power": power}


def loft(name, sections, material, parent, joint=(0.0, 0.0, 0.0),
         offset=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0),
         n=14, cap=True, bevel=0.02, smooth=34.0, axis="Z"):
    """Bridges superellipse rings into one tapered solid.

    axis="Z": sections stack along Z, rings lie in XY.
    axis="Y": sections' z is the Y position, rings lie in XZ (cy shifts Z).
    """
    verts = []
    faces = []
    rings = []

    for section in sections:
        indices = []

        for x, y in superellipse(n, section["rx"], section["ry"], section["power"]):
            if axis == "Y":
                verts.append((x + section["cx"], section["z"], y + section["cy"]))
            else:
                verts.append((x + section["cx"], y + section["cy"], section["z"]))

            indices.append(len(verts) - 1)

        rings.append(indices)

    for lower, upper in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append((lower[i], lower[j], upper[j], upper[i]))

    if cap:
        faces.append(tuple(reversed(rings[0])))
        faces.append(tuple(rings[-1]))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)

    finish(obj, bevel=bevel, smooth_angle=smooth)
    place(obj, joint, offset, rotation)
    parent_keep(obj, parent)
    return obj


def slab(name, size, material, parent, joint, offset=(0.0, 0.0, 0.0),
         rotation=(0.0, 0.0, 0.0), taper=0.84, bevel=0.016):
    """A plate with tapered faces: reads as armour instead of a cube."""
    width, depth, height = size
    half = height * 0.5
    return loft(
        name,
        [
            S(-half, width * 0.5 * taper, depth * 0.5 * taper),
            S(-half * 0.15, width * 0.5, depth * 0.5),
            S(half * 0.72, width * 0.5 * 0.95, depth * 0.5 * 0.9),
            S(half, width * 0.5 * taper, depth * 0.5 * taper),
        ],
        material, parent, joint=joint, offset=offset, rotation=rotation,
        n=12, bevel=bevel, smooth=30.0,
    )


def spike(name, base_radius, length, material, parent, joint, offset,
          rotation=(0.0, 0.0, 0.0), vertices=7):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=base_radius,
                                    radius2=0.0, depth=length, location=(0, 0, 0))
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(material)
    finish(obj, bevel=0.0, smooth_angle=30.0)
    place(obj, joint, offset, rotation)
    parent_keep(obj, parent)
    return obj


def tube(name, profile, material, parent, joint, offset=(0.0, 0.0, 0.0),
         rotation=(0.0, 0.0, 0.0), axis="Z", n=16, bevel=0.012):
    """Round solid from (distance_along_axis, radius) pairs."""
    sections = [S(d, r, r, power=2.0) for d, r in profile]
    return loft(name, sections, material, parent, joint=joint, offset=offset,
                rotation=rotation, n=n, bevel=bevel, smooth=40.0, axis=axis)


# ------------------------------------------------------------------- model

PALETTE = {}


def build_boss():
    """Returns {name: joint object}. Face points toward -Y, feet at Z=0."""
    body = lit("BossBody", (0.120, 0.048, 0.058), roughness=0.36, metallic=0.80)
    dark = lit("BossDark", (0.020, 0.018, 0.022), roughness=0.46, metallic=0.66)
    trim = lit("BossTrim", (0.30, 0.125, 0.075), roughness=0.28, metallic=0.88)
    glow = emissive("BossGlow", (1.0, 0.42, 0.09), strength=8.0)
    eye = emissive("BossEye", (1.0, 0.74, 0.30), strength=12.0)

    PALETTE.update({"body": body, "dark": dark, "trim": trim, "glow": glow, "eye": eye})

    model = bpy.data.objects.new("Model", None)
    bpy.context.collection.objects.link(model)
    model.empty_display_type = "PLAIN_AXES"
    model.empty_display_size = 0.4

    joints = {"Model": model}

    # ------------------------------------------------------------- legs
    hip_x, hip_z = 0.62, 2.30
    knee_z, ankle_z = 1.30, 0.42

    for side, x in (("L", -hip_x), ("R", hip_x)):
        thigh = loft("Thigh" + side, [
            S(0.12, 0.30, 0.34),
            S(-0.26, 0.34, 0.39),
            S(-0.70, 0.27, 0.31),
            S(-1.00, 0.22, 0.25),
        ], body, model, joint=(x, 0.0, hip_z))
        joints["Leg" + side] = thigh

        slab("ThighPlate" + side, (0.30, 0.14, 0.54), dark, thigh,
             joint=(x, 0.0, hip_z), offset=(0.0, -0.30, -0.52),
             rotation=(math.radians(-4.0), 0.0, 0.0), taper=0.78)

        knee = loft("Knee" + side, [
            S(0.08, 0.24, 0.27),
            S(-0.24, 0.27, 0.31),
            S(-0.62, 0.21, 0.24),
            S(-0.86, 0.19, 0.22),
        ], body, thigh, joint=(x, 0.0, knee_z))
        joints["Knee" + side] = knee

        slab("KneeGuard" + side, (0.46, 0.32, 0.44), dark, knee,
             joint=(x, 0.0, knee_z), offset=(0.0, -0.26, 0.02), taper=0.66)
        slab("KneeTrim" + side, (0.28, 0.10, 0.14), trim, knee,
             joint=(x, 0.0, knee_z), offset=(0.0, -0.40, 0.04), taper=0.9)

        boot = loft("Boot" + side, [
            S(-0.42, 0.26, 0.32),
            S(-0.30, 0.30, 0.42, cy=-0.08),
            S(-0.12, 0.31, 0.52, cy=-0.16),
            S(0.00, 0.27, 0.50, cy=-0.18),
        ], body, knee, joint=(x, 0.0, ankle_z))
        joints["Boot" + side] = boot

        for i, toe_x in enumerate((-0.17, 0.0, 0.17)):
            spike("Claw" + side + str(i), 0.075, 0.30, dark, boot,
                  joint=(x, 0.0, ankle_z), offset=(toe_x, -0.66, -0.40),
                  rotation=(math.radians(76.0), 0.0, 0.0))

        slab("SoleTrim" + side, (0.52, 0.84, 0.10), trim, boot,
             joint=(x, 0.0, ankle_z), offset=(0.0, -0.14, -0.37), taper=0.92)

    # ----------------------------------------------------------- pelvis
    loft("Pelvis", [
        S(2.10, 0.54, 0.42),
        S(2.32, 0.74, 0.52),
        S(2.56, 0.66, 0.46),
    ], body, model, joint=(0.0, 0.0, 0.0), bevel=0.026)

    for side, x in (("L", -0.68), ("R", 0.68)):
        slab("Skirt" + side, (0.40, 0.64, 0.80), dark, model,
             joint=(0.0, 0.0, 2.34), offset=(x, -0.12, -0.10),
             rotation=(0.0, math.radians(-6.0 if x < 0 else 6.0), 0.0), taper=0.8)

    # ------------------------------------------------------------ torso
    torso = loft("Torso", [
        S(0.00, 0.56, 0.42, power=5.0),
        S(0.24, 0.80, 0.56, power=5.0),
        S(0.62, 1.04, 0.64, power=5.5),
        S(0.92, 1.08, 0.60, power=6.0),
        S(1.04, 0.90, 0.48, power=5.0),
    ], body, model, joint=(0.0, 0.0, 2.50), bevel=0.024, smooth=24.0)
    joints["Torso"] = torso

    # Layered chest armour.
    for side, x in (("L", -0.36), ("R", 0.36)):
        slab("ChestPlate" + side, (0.76, 0.30, 0.64), dark, torso,
             joint=(0.0, 0.0, 2.50), offset=(x, -0.42, 0.74),
             rotation=(math.radians(-6.0), 0.0, math.radians(4.0 if x > 0 else -4.0)),
             taper=0.7)
        slab("RibTrim" + side, (0.60, 0.10, 0.18), trim, torso,
             joint=(0.0, 0.0, 2.50), offset=(x * 1.12, -0.50, 0.46),
             rotation=(math.radians(-4.0), 0.0, math.radians(6.0 if x > 0 else -6.0)))

    # Furnace core.
    # Furnace core: discs facing -Y, not stacked rings (a Z-loft would read
    # as a flat rectangle from the front).
    loft("CoreHousing", [
        S(-0.40, 0.34, 0.34, power=2.0),
        S(-0.56, 0.40, 0.40, power=2.0),
        S(-0.64, 0.36, 0.36, power=2.0),
    ], dark, torso, joint=(0.0, 0.0, 3.12), axis="Y", n=22, bevel=0.012)

    loft("CoreGlow", [
        S(-0.74, 0.23, 0.23, power=2.0),
        S(-0.64, 0.21, 0.21, power=2.0),
    ], glow, torso, joint=(0.0, 0.0, 3.12), axis="Y", n=22, bevel=0.0)

    for i in range(3):
        slab("VentGlow" + str(i), (0.36, 0.06, 0.045), glow, torso,
             joint=(0.0, 0.0, 2.50), offset=(0.0, -0.60, 0.16 + i * 0.10))

    for i in range(4):
        slab("SpinePlate" + str(i), (0.34 - i * 0.03, 0.16, 0.12), dark, torso,
             joint=(0.0, 0.0, 2.50), offset=(0.0, 0.50, 0.94 - i * 0.17),
             rotation=(math.radians(8.0), 0.0, 0.0))
    slab("RuneSpine", (0.08, 0.05, 0.52), glow, torso,
         joint=(0.0, 0.0, 2.50), offset=(0.0, 0.58, 0.60))

    # Exhaust stacks.
    for side, x in (("L", -0.52), ("R", 0.52)):
        tube("Exhaust" + side, [(0.0, 0.15), (0.34, 0.13), (0.62, 0.11)],
             dark, torso, joint=(x, 0.40, 3.34),
             rotation=(math.radians(-12.0), 0.0, 0.0), axis="Z", n=12)
        tube("ExhaustGlow" + side, [(0.0, 0.095), (0.06, 0.07)],
             glow, torso, joint=(x, 0.53, 3.94),
             rotation=(math.radians(-12.0), 0.0, 0.0), axis="Z", n=12, bevel=0.0)

    slab("Banner", (0.54, 0.05, 1.34), dark, torso,
         joint=(0.0, 0.0, 2.50), offset=(0.0, 0.58, -0.28),
         rotation=(math.radians(-4.0), 0.0, 0.0), taper=0.9)

    # -------------------------------------------------------- pauldrons
    for side, sign in (("L", -1.0), ("R", 1.0)):
        x = sign * 1.20
        loft("Pauldron" + side, [
            S(-0.22, 0.32, 0.38, power=5.0),
            S(0.02, 0.44, 0.46, power=5.0),
            S(0.24, 0.34, 0.34, power=5.0),
        ], body, torso, joint=(x, 0.0, 3.44),
            rotation=(0.0, math.radians(sign * 12.0), 0.0), bevel=0.026, smooth=22.0)
        slab("PauldronTrim" + side, (0.60, 0.10, 0.15), trim, torso,
             joint=(0.0, 0.0, 2.50), offset=(x, -0.36, 1.12),
             rotation=(0.0, 0.0, math.radians(sign * 6.0)))

        spike("ShoulderSpike" + side + "A", 0.10, 0.54, dark, torso,
              joint=(x, 0.0, 3.54), offset=(sign * 0.40, 0.0, 0.24),
              rotation=(0.0, math.radians(sign * 58.0), 0.0))
        spike("ShoulderSpike" + side + "B", 0.085, 0.44, dark, torso,
              joint=(x, 0.0, 3.54), offset=(sign * 0.12, -0.18, 0.40),
              rotation=(0.0, math.radians(sign * 22.0), 0.0))
        spike("ShoulderSpike" + side + "C", 0.075, 0.36, dark, torso,
              joint=(x, 0.0, 3.54), offset=(sign * 0.22, 0.30, 0.28),
              rotation=(math.radians(-26.0), math.radians(sign * 38.0), 0.0))

    # -------------------------------------------------------------- head
    tube("Neck", [(0.0, 0.22), (0.10, 0.20)], dark, torso,
         joint=(0.0, 0.0, 3.56), axis="Z", n=12)

    head = loft("Head", [
        S(0.00, 0.32, 0.34, power=5.0),
        S(0.18, 0.42, 0.44, power=5.5),
        S(0.38, 0.34, 0.36, power=5.0),
        S(0.50, 0.22, 0.24, power=4.0),
    ], body, torso, joint=(0.0, 0.0, 3.72), bevel=0.022, smooth=24.0)
    joints["Head"] = head

    slab("Brow", (0.66, 0.24, 0.15), dark, head,
         joint=(0.0, 0.0, 3.72), offset=(0.0, -0.30, 0.35),
         rotation=(math.radians(-14.0), 0.0, 0.0), taper=0.8)
    slab("Jaw", (0.46, 0.26, 0.14), dark, head,
         joint=(0.0, 0.0, 3.72), offset=(0.0, -0.26, 0.03),
         rotation=(math.radians(10.0), 0.0, 0.0), taper=0.7)

    for i, (x, width) in enumerate(((-0.18, 0.11), (0.0, 0.17), (0.18, 0.11))):
        slab("Visor" + str(i), (width, 0.07, 0.075), eye, head,
             joint=(0.0, 0.0, 3.72), offset=(x, -0.42, 0.20), taper=0.9)

    for i, (x, length, tilt) in enumerate(
            [(-0.24, 0.36, 34.0), (-0.12, 0.48, 14.0), (0.0, 0.58, 0.0),
             (0.12, 0.48, -14.0), (0.24, 0.36, -34.0)]):
        spike("Crown" + str(i), 0.055, length, dark, head,
              joint=(0.0, 0.0, 3.72),
              offset=(x, 0.06, 0.44 + length * 0.32),
              rotation=(math.radians(-10.0), math.radians(tilt), 0.0))

    # -------------------------------------------------------------- arms
    for side, sign in (("L", -1.0), ("R", 1.0)):
        x = sign * 1.26
        arm = loft("Arm" + side, [
            S(0.06, 0.23, 0.25),
            S(-0.26, 0.25, 0.27),
            S(-0.60, 0.20, 0.22),
            S(-0.70, 0.19, 0.21),
        ], body, torso, joint=(x, 0.0, 3.40))
        joints["Arm" + side] = arm

        elbow = loft("Elbow" + side, [
            S(0.02, 0.20, 0.22),
            S(-0.26, 0.22, 0.24),
            S(-0.56, 0.17, 0.19),
        ], body, arm, joint=(x, 0.0, 2.72))
        joints["Elbow" + side] = elbow

        slab("ElbowGuard" + side, (0.36, 0.36, 0.28), dark, elbow,
             joint=(x, 0.0, 2.72), offset=(0.0, -0.12, 0.02), taper=0.7)

    # Right arm: siege cannon, barrel points forward (-Y).
    cannon_joint = (1.26, -0.30, 2.34)
    tube("CannonBarrel", [(0.0, 0.225), (-0.42, 0.19), (-0.74, 0.155)],
         dark, joints["ElbowR"], joint=cannon_joint, axis="Y", n=16)
    tube("CannonCollar", [(0.0, 0.27), (-0.14, 0.245)],
         trim, joints["ElbowR"], joint=cannon_joint, axis="Y", n=16)
    for i, y in enumerate((-0.46, -0.60)):
        tube("CannonBrake" + str(i), [(0.0, 0.20), (-0.05, 0.185)],
             trim, joints["ElbowR"], joint=(1.26, y, 2.34), axis="Y", n=16)
    tube("CannonGlow", [(0.0, 0.135), (-0.05, 0.10)],
         glow, joints["ElbowR"], joint=(1.26, -1.02, 2.34), axis="Y", n=16, bevel=0.0)

    for i in range(3):
        slab("CannonVent" + str(i), (0.30, 0.10, 0.045), glow, joints["ElbowR"],
             joint=(1.26, 0.0, 2.34), offset=(0.0, -0.52, 0.10 + i * 0.09))

    # Left arm: cleaver blade.
    blade_joint = (-1.26, 0.0, 2.72)
    slab("BladeSpine", (0.10, 0.30, 1.05), dark, joints["ElbowL"],
         joint=blade_joint, offset=(0.0, -0.16, -0.74),
         rotation=(math.radians(-6.0), 0.0, 0.0), taper=0.9)
    slab("BladeEdge", (0.045, 0.42, 0.95), trim, joints["ElbowL"],
         joint=blade_joint, offset=(0.0, -0.34, -0.76),
         rotation=(math.radians(-6.0), 0.0, 0.0), taper=0.94)
    slab("BladeGlow", (0.02, 0.10, 0.70), glow, joints["ElbowL"],
         joint=blade_joint, offset=(0.0, -0.40, -0.76),
         rotation=(math.radians(-6.0), 0.0, 0.0))
    slab("BladeGuard", (0.36, 0.20, 0.20), trim, joints["ElbowL"],
         joint=blade_joint, offset=(0.0, -0.14, -0.18), taper=0.8)

    return joints


# -------------------------------------------------------------- animation

def key(joint, frame, rx=0.0, ry=0.0, rz=0.0):
    joint.rotation_mode = "XYZ"
    joint.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    joint.keyframe_insert("rotation_euler", frame=frame)


def pose(joints, frame, root_z=0.0, **rotations):
    for name, rotation in rotations.items():
        if name not in joints:
            continue

        values = tuple(rotation) + (0.0, 0.0, 0.0)
        key(joints[name], frame, values[0], values[1], values[2])

    model = joints["Model"]
    model.location = (0.0, 0.0, root_z)
    model.keyframe_insert("location", frame=frame)


def anim_idle(joints):
    pose(joints, 0, root_z=0.0, Torso=(1.5, 0.0, 1.5), Head=(-2.0, 0.0, -4.0),
         ArmL=(2.0, 0.0, -2.0), ArmR=(-1.0, 0.0, 2.0),
         ElbowL=(-4.0, 0.0, 0.0), ElbowR=(-2.0, 0.0, 0.0),
         KneeL=(1.0, 0.0, 0.0), KneeR=(1.0, 0.0, 0.0))
    pose(joints, 30, root_z=-0.035, Torso=(3.0, 0.0, 0.0), Head=(1.0, 0.0, 3.0),
         ArmL=(4.0, 0.0, -1.0), ArmR=(-3.0, 0.0, 1.0),
         ElbowL=(-7.0, 0.0, 0.0), ElbowR=(-4.0, 0.0, 0.0),
         KneeL=(2.0, 0.0, 0.0), KneeR=(2.0, 0.0, 0.0))
    pose(joints, 60, root_z=0.0, Torso=(1.5, 0.0, -1.5), Head=(-2.0, 0.0, 5.0),
         ArmL=(2.0, 0.0, -2.0), ArmR=(-1.0, 0.0, 2.0),
         ElbowL=(-4.0, 0.0, 0.0), ElbowR=(-2.0, 0.0, 0.0),
         KneeL=(1.0, 0.0, 0.0), KneeR=(1.0, 0.0, 0.0))
    pose(joints, 90, root_z=-0.035, Torso=(3.0, 0.0, 0.0), Head=(1.0, 0.0, -2.0),
         ArmL=(4.0, 0.0, -1.0), ArmR=(-3.0, 0.0, 1.0),
         ElbowL=(-7.0, 0.0, 0.0), ElbowR=(-4.0, 0.0, 0.0),
         KneeL=(2.0, 0.0, 0.0), KneeR=(2.0, 0.0, 0.0))
    pose(joints, 120, root_z=0.0, Torso=(1.5, 0.0, 1.5), Head=(-2.0, 0.0, -4.0),
         ArmL=(2.0, 0.0, -2.0), ArmR=(-1.0, 0.0, 2.0),
         ElbowL=(-4.0, 0.0, 0.0), ElbowR=(-2.0, 0.0, 0.0),
         KneeL=(1.0, 0.0, 0.0), KneeR=(1.0, 0.0, 0.0))
    return 120


def anim_walk(joints):
    """Heavy 40-frame two-step: contact, weight drop, pass, knee lift."""
    pose(joints, 0, root_z=-0.05,
         Torso=(3.0, 0.0, 3.0), Head=(-1.0, 0.0, -3.0),
         LegL=(-16.0, 0.0, 0.0), KneeL=(6.0, 0.0, 0.0),
         LegR=(14.0, 0.0, 0.0), KneeR=(26.0, 0.0, 0.0),
         ArmL=(9.0, 0.0, -2.0), ArmR=(-9.0, 0.0, 2.0),
         ElbowL=(-6.0, 0.0, 0.0), ElbowR=(-4.0, 0.0, 0.0))
    pose(joints, 10, root_z=0.02,
         Torso=(1.0, 0.0, 0.0), Head=(-2.0, 0.0, 2.0),
         LegL=(-6.0, 0.0, 0.0), KneeL=(3.0, 0.0, 0.0),
         LegR=(-4.0, 0.0, 0.0), KneeR=(44.0, 0.0, 0.0),
         ArmL=(3.0, 0.0, -2.0), ArmR=(-3.0, 0.0, 2.0),
         ElbowL=(-8.0, 0.0, 0.0), ElbowR=(-5.0, 0.0, 0.0))
    pose(joints, 20, root_z=-0.05,
         Torso=(3.0, 0.0, -3.0), Head=(-1.0, 0.0, 3.0),
         LegR=(-16.0, 0.0, 0.0), KneeR=(6.0, 0.0, 0.0),
         LegL=(14.0, 0.0, 0.0), KneeL=(26.0, 0.0, 0.0),
         ArmR=(9.0, 0.0, 2.0), ArmL=(-9.0, 0.0, -2.0),
         ElbowR=(-6.0, 0.0, 0.0), ElbowL=(-4.0, 0.0, 0.0))
    pose(joints, 30, root_z=0.02,
         Torso=(1.0, 0.0, 0.0), Head=(-2.0, 0.0, -2.0),
         LegR=(-6.0, 0.0, 0.0), KneeR=(3.0, 0.0, 0.0),
         LegL=(-4.0, 0.0, 0.0), KneeL=(44.0, 0.0, 0.0),
         ArmR=(3.0, 0.0, 2.0), ArmL=(-3.0, 0.0, -2.0),
         ElbowR=(-8.0, 0.0, 0.0), ElbowL=(-5.0, 0.0, 0.0))
    pose(joints, 40, root_z=-0.05,
         Torso=(3.0, 0.0, 3.0), Head=(-1.0, 0.0, -3.0),
         LegL=(-16.0, 0.0, 0.0), KneeL=(6.0, 0.0, 0.0),
         LegR=(14.0, 0.0, 0.0), KneeR=(26.0, 0.0, 0.0),
         ArmL=(9.0, 0.0, -2.0), ArmR=(-9.0, 0.0, 2.0),
         ElbowL=(-6.0, 0.0, 0.0), ElbowR=(-4.0, 0.0, 0.0))
    return 40


def anim_slam(joints):
    pose(joints, 0, Torso=(2.0, 0.0, 0.0), Head=(-1.0, 0.0, 0.0),
         ArmL=(2.0, 0.0, -2.0), ArmR=(-1.0, 0.0, 2.0),
         ElbowL=(-4.0, 0.0, 0.0), ElbowR=(-3.0, 0.0, 0.0))
    pose(joints, 12, root_z=0.06,
         Torso=(18.0, 0.0, 0.0), Head=(10.0, 0.0, 0.0),
         ArmL=(-132.0, 0.0, -10.0), ArmR=(-138.0, 0.0, 10.0),
         ElbowL=(-26.0, 0.0, 0.0), ElbowR=(-22.0, 0.0, 0.0),
         LegL=(4.0, 0.0, 0.0), LegR=(4.0, 0.0, 0.0),
         KneeL=(10.0, 0.0, 0.0), KneeR=(10.0, 0.0, 0.0))
    pose(joints, 20, root_z=0.08,
         Torso=(22.0, 0.0, 0.0), Head=(13.0, 0.0, 0.0),
         ArmL=(-146.0, 0.0, -12.0), ArmR=(-150.0, 0.0, 12.0),
         ElbowL=(-30.0, 0.0, 0.0), ElbowR=(-26.0, 0.0, 0.0),
         LegL=(5.0, 0.0, 0.0), LegR=(5.0, 0.0, 0.0),
         KneeL=(12.0, 0.0, 0.0), KneeR=(12.0, 0.0, 0.0))
    pose(joints, 26, root_z=-0.22,
         Torso=(-24.0, 0.0, 0.0), Head=(-16.0, 0.0, 0.0),
         ArmL=(34.0, 0.0, -4.0), ArmR=(30.0, 0.0, 4.0),
         ElbowL=(10.0, 0.0, 0.0), ElbowR=(8.0, 0.0, 0.0),
         LegL=(-8.0, 0.0, 0.0), LegR=(-8.0, 0.0, 0.0),
         KneeL=(26.0, 0.0, 0.0), KneeR=(26.0, 0.0, 0.0))
    pose(joints, 32, root_z=-0.16,
         Torso=(-18.0, 0.0, 0.0), Head=(-10.0, 0.0, 0.0),
         ArmL=(26.0, 0.0, -4.0), ArmR=(22.0, 0.0, 4.0),
         ElbowL=(4.0, 0.0, 0.0), ElbowR=(2.0, 0.0, 0.0),
         LegL=(-6.0, 0.0, 0.0), LegR=(-6.0, 0.0, 0.0),
         KneeL=(22.0, 0.0, 0.0), KneeR=(22.0, 0.0, 0.0))
    pose(joints, 48, root_z=-0.02,
         Torso=(4.0, 0.0, 0.0), Head=(-2.0, 0.0, 0.0),
         ArmL=(4.0, 0.0, -2.0), ArmR=(-2.0, 0.0, 2.0),
         ElbowL=(-6.0, 0.0, 0.0), ElbowR=(-4.0, 0.0, 0.0),
         KneeL=(2.0, 0.0, 0.0), KneeR=(2.0, 0.0, 0.0))
    pose(joints, 60, Torso=(2.0, 0.0, 0.0), Head=(-1.0, 0.0, 0.0),
         ArmL=(2.0, 0.0, -2.0), ArmR=(-1.0, 0.0, 2.0),
         ElbowL=(-4.0, 0.0, 0.0), ElbowR=(-3.0, 0.0, 0.0))
    return 60


def anim_volley(joints):
    pose(joints, 0, Torso=(2.0, 0.0, 0.0), ArmR=(-2.0, 0.0, 2.0),
         ElbowR=(-4.0, 0.0, 0.0), ArmL=(2.0, 0.0, -2.0))
    pose(joints, 10, root_z=0.02,
         Torso=(-4.0, 0.0, -8.0), Head=(-4.0, 0.0, 6.0),
         ArmR=(-88.0, 0.0, 6.0), ElbowR=(-14.0, 0.0, 0.0),
         ArmL=(6.0, 0.0, -6.0), ElbowL=(-10.0, 0.0, 0.0))
    pose(joints, 18, root_z=0.02,
         Torso=(-6.0, 0.0, -10.0), Head=(-5.0, 0.0, 7.0),
         ArmR=(-92.0, 0.0, 5.0), ElbowR=(-10.0, 0.0, 0.0),
         ArmL=(6.0, 0.0, -6.0), ElbowL=(-10.0, 0.0, 0.0))
    pose(joints, 22, root_z=0.03,
         Torso=(6.0, 0.0, -6.0), Head=(4.0, 0.0, 4.0),
         ArmR=(-74.0, 0.0, 7.0), ElbowR=(-32.0, 0.0, 0.0),
         ArmL=(4.0, 0.0, -4.0), ElbowL=(-8.0, 0.0, 0.0))
    pose(joints, 30, root_z=0.0,
         Torso=(-2.0, 0.0, -4.0), Head=(-2.0, 0.0, 3.0),
         ArmR=(-88.0, 0.0, 6.0), ElbowR=(-16.0, 0.0, 0.0),
         ArmL=(4.0, 0.0, -4.0), ElbowL=(-8.0, 0.0, 0.0))
    pose(joints, 42, Torso=(2.0, 0.0, 0.0), ArmR=(-2.0, 0.0, 2.0),
         ElbowR=(-4.0, 0.0, 0.0), ArmL=(2.0, 0.0, -2.0))
    return 42


def anim_roar(joints):
    pose(joints, 0, Torso=(2.0, 0.0, 0.0), Head=(-1.0, 0.0, 0.0))
    pose(joints, 10, root_z=-0.05,
         Torso=(10.0, 0.0, 0.0), Head=(8.0, 0.0, 0.0),
         ArmL=(-20.0, 0.0, -30.0), ArmR=(-20.0, 0.0, 30.0),
         ElbowL=(-30.0, 0.0, 0.0), ElbowR=(-24.0, 0.0, 0.0),
         KneeL=(10.0, 0.0, 0.0), KneeR=(10.0, 0.0, 0.0))
    pose(joints, 18, root_z=0.04,
         Torso=(-16.0, 0.0, 0.0), Head=(-22.0, 0.0, 0.0),
         ArmL=(-40.0, 0.0, -62.0), ArmR=(-40.0, 0.0, 62.0),
         ElbowL=(-16.0, 0.0, 0.0), ElbowR=(-12.0, 0.0, 0.0),
         LegL=(-3.0, 0.0, 0.0), LegR=(-3.0, 0.0, 0.0),
         KneeL=(4.0, 0.0, 0.0), KneeR=(4.0, 0.0, 0.0))
    pose(joints, 26, root_z=-0.02,
         Torso=(-12.0, 0.0, 0.0), Head=(-18.0, 0.0, 0.0),
         ArmL=(-38.0, 0.0, -58.0), ArmR=(-38.0, 0.0, 58.0),
         ElbowL=(-18.0, 0.0, 0.0), ElbowR=(-14.0, 0.0, 0.0),
         KneeL=(6.0, 0.0, 0.0), KneeR=(6.0, 0.0, 0.0))
    pose(joints, 36, root_z=0.03,
         Torso=(-14.0, 0.0, 0.0), Head=(-20.0, 0.0, 0.0),
         ArmL=(-42.0, 0.0, -60.0), ArmR=(-42.0, 0.0, 60.0),
         ElbowL=(-14.0, 0.0, 0.0), ElbowR=(-10.0, 0.0, 0.0),
         KneeL=(4.0, 0.0, 0.0), KneeR=(4.0, 0.0, 0.0))
    pose(joints, 50, Torso=(2.0, 0.0, 0.0), Head=(-1.0, 0.0, 0.0),
         ArmL=(2.0, 0.0, -2.0), ArmR=(-1.0, 0.0, 2.0),
         ElbowL=(-4.0, 0.0, 0.0), ElbowR=(-3.0, 0.0, 0.0))
    return 50


def anim_hit(joints):
    pose(joints, 0)
    pose(joints, 4, root_z=-0.02,
         Torso=(7.0, 0.0, 3.0), Head=(6.0, 0.0, -4.0),
         ArmL=(5.0, 0.0, 4.0), ArmR=(4.0, 0.0, -4.0))
    pose(joints, 14)
    return 14


def anim_death(joints):
    pose(joints, 0)
    pose(joints, 10, root_z=0.03,
         Torso=(12.0, 0.0, -6.0), Head=(14.0, 0.0, 8.0),
         ArmL=(-14.0, 0.0, -14.0), ArmR=(-18.0, 0.0, 14.0),
         ElbowL=(-20.0, 0.0, 0.0), ElbowR=(-16.0, 0.0, 0.0),
         KneeL=(8.0, 0.0, 0.0), KneeR=(6.0, 0.0, 0.0))
    pose(joints, 26, root_z=-0.05,
         Torso=(16.0, 0.0, -4.0), Head=(10.0, 0.0, 6.0),
         ArmL=(-8.0, 0.0, -10.0), ArmR=(-10.0, 0.0, 10.0),
         ElbowL=(-16.0, 0.0, 0.0), ElbowR=(-12.0, 0.0, 0.0),
         LegL=(-6.0, 0.0, 0.0), LegR=(-4.0, 0.0, 0.0),
         KneeL=(30.0, 0.0, 0.0), KneeR=(26.0, 0.0, 0.0))
    pose(joints, 44, root_z=-0.42,
         Torso=(6.0, 0.0, -2.0), Head=(-6.0, 0.0, 3.0),
         ArmL=(10.0, 0.0, -8.0), ArmR=(8.0, 0.0, 8.0),
         ElbowL=(-10.0, 0.0, 0.0), ElbowR=(-8.0, 0.0, 0.0),
         LegL=(-10.0, 0.0, 0.0), LegR=(-8.0, 0.0, 0.0),
         KneeL=(58.0, 0.0, 0.0), KneeR=(54.0, 0.0, 0.0))
    pose(joints, 62, root_z=-0.70,
         Torso=(-54.0, 0.0, 0.0), Head=(-22.0, 0.0, 0.0),
         ArmL=(-30.0, 0.0, -6.0), ArmR=(-26.0, 0.0, 6.0),
         ElbowL=(-6.0, 0.0, 0.0), ElbowR=(-4.0, 0.0, 0.0),
         LegL=(-4.0, 0.0, 0.0), LegR=(-4.0, 0.0, 0.0),
         KneeL=(64.0, 0.0, 0.0), KneeR=(60.0, 0.0, 0.0))
    pose(joints, 78, root_z=-0.78,
         Torso=(-62.0, 0.0, 0.0), Head=(-18.0, 0.0, 0.0),
         ArmL=(-22.0, 0.0, -4.0), ArmR=(-20.0, 0.0, 4.0),
         LegL=(-3.0, 0.0, 0.0), LegR=(-3.0, 0.0, 0.0),
         KneeL=(66.0, 0.0, 0.0), KneeR=(62.0, 0.0, 0.0))
    return 78


CLIPS = [
    ("Idle", anim_idle),
    ("Walk", anim_walk),
    ("Slam", anim_slam),
    ("Volley", anim_volley),
    ("Roar", anim_roar),
    ("Hit", anim_hit),
    ("Death", anim_death),
]


def export_clip(name, builder):
    reset_scene()
    joints = build_boss()
    last_frame = builder(joints)

    scene = bpy.context.scene
    scene.frame_start = 0
    scene.frame_end = last_frame
    scene.frame_set(0)

    # Blender 5 moved fcurves onto action layers/slots; walk whatever exists.
    for joint in joints.values():
        action = joint.animation_data.action if joint.animation_data else None

        if action is None:
            continue

        curves = list(getattr(action, "fcurves", []) or [])

        for layer in getattr(action, "layers", []) or []:
            for strip in getattr(layer, "strips", []) or []:
                for bag in getattr(strip, "channelbags", []) or []:
                    curves.extend(bag.fcurves)

        for fcurve in curves:
            for point in fcurve.keyframe_points:
                point.interpolation = "BEZIER"
                point.handle_left_type = "AUTO_CLAMPED"
                point.handle_right_type = "AUTO_CLAMPED"

    bpy.ops.object.select_all(action="SELECT")

    path = os.path.join(OUT_DIR, f"BossWarden_{name}.fbx")
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
        bake_anim=True,
        bake_anim_use_all_bones=False,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
    )

    # Report the real extents so the Unity collider can be sized to match.
    bpy.context.view_layer.update()
    points = []
    depsgraph = bpy.context.evaluated_depsgraph_get()

    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            evaluated = obj.evaluated_get(depsgraph)

            for corner in evaluated.bound_box:
                points.append(evaluated.matrix_world @ Vector(corner))

    low = min(p.z for p in points)
    high = max(p.z for p in points)
    wide = max(abs(p.x) for p in points)
    deep = max(abs(p.y) for p in points)
    size = os.path.getsize(path)
    print(f"[boss] {name}: frames 0-{last_frame}, z {low:.2f}..{high:.2f}, "
          f"x±{wide:.2f}, y±{deep:.2f}, {size // 1024} KB")


for clip_name, clip_builder in CLIPS:
    export_clip(clip_name, clip_builder)

print("[boss] done")
