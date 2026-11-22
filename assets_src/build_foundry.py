"""Author the Signal Foundry art set in Blender 5.3.

Run with Blender in background or execute this file through Blender MCP.
The original Unity assets are kept alongside these replacements.
"""

from math import acos, atan2, cos, degrees, hypot, pi, radians, sin
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT.parent / "Assets" / "Models" / "Foundry"
OUTPUT.mkdir(parents=True, exist_ok=True)


def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.data.orphans_purge(do_recursive=True)
    bpy.context.scene.render.fps = 30


def material(name, color, metal=0.25, emission=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = mat.diffuse_color
    shader.inputs["Metallic"].default_value = metal
    shader.inputs["Roughness"].default_value = 0.37
    shader.inputs["Emission Color"].default_value = mat.diffuse_color
    shader.inputs["Emission Strength"].default_value = emission
    return mat


def pivot(name, location=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.location = location
    obj.empty_display_size = 0.08
    return obj


def mesh(name, vertices, faces, location, mat, parent, bevel):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.location = location
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new("Machined edges", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.harden_normals = True
        obj.modifiers.new("Panel normals", "WEIGHTED_NORMAL")
    return obj


def loft(name, location, profiles, mat, parent, bevel=0.012):
    """Build an octagonal section with independently tapered ends."""
    vertices = []
    for z, width, depth, offset in profiles:
        x, y = width / 2, depth / 2
        vertices.extend((a, b + offset, z) for a, b in (
            (-0.68*x, -y), (0.68*x, -y), (x, -0.58*y), (x, 0.58*y),
            (0.68*x, y), (-0.68*x, y), (-x, 0.58*y), (-x, -0.58*y)))
    faces = [tuple(reversed(range(8)))]
    for lower in range(0, len(vertices) - 8, 8):
        for side in range(8):
            a, b = lower + side, lower + (side + 1) % 8
            faces.append((a, b, b + 8, a + 8))
    faces.append(tuple(range(len(vertices) - 8, len(vertices))))
    return mesh(name, vertices, faces, location, mat, parent, bevel)


def panel(name, location, size, mat, parent, taper=1, bevel=0.008):
    width, depth, height = size
    return loft(name, location, [
        (-height / 2, width, depth, 0),
        (height / 2, width * taper, depth * taper, 0),
    ], mat, parent, bevel)


def combine_panels(parts, name, mat, parent):
    """Batch unrotated, unbeveled panels on the same rigid joint."""
    vertices, faces = [], []
    for part in parts:
        base = len(vertices)
        vertices.extend(vertex.co + part.location for vertex in part.data.vertices)
        faces.extend(tuple(base + index for index in face.vertices) for face in part.data.polygons)
        bpy.data.objects.remove(part, do_unlink=True)
    return mesh(name, vertices, faces, (0, 0, 0), mat, parent, 0)


def ring(name, location, radius, thickness, height, mat, parent, segments=12):
    vertices = [(r*cos(2*pi*i/segments), r*sin(2*pi*i/segments), z)
                for r, z in ((radius, -height/2), (radius, height/2),
                             (radius-thickness, height/2), (radius-thickness, -height/2))
                for i in range(segments)]
    faces = []
    for row in range(4):
        for i in range(segments):
            a, b = row*segments, ((row+1) % 4)*segments
            j = (i+1) % segments
            faces.append((a+i, a+j, b+j, b+i))
    return mesh(name, vertices, faces, location, mat, parent, 0)


def save(name):
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / (name + ".blend")))


def export(name, animated=False):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT / (name + ".fbx")), use_selection=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", object_types={"EMPTY", "MESH"},
        add_leaf_bones=False, use_custom_props=False, bake_anim=animated,
        bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0,
    )


def pose(joints, frame, angles, travel=(0, 0, 0)):
    for name, (joint, rest) in joints.items():
        joint.rotation_euler = tuple(radians(value) for value in angles.get(name, (0, 0, 0)))
        joint.keyframe_insert("rotation_euler", frame=frame)
        if name == "Model":
            joint.location = rest + Vector(travel)
            joint.keyframe_insert("location", frame=frame)


def actor(name, width, height, reach, armor_color, glow_color):
    scale = height / 1.9
    armor = material(name + "Body", armor_color)
    frame = material(name + "Dark", (0.027, 0.041, 0.050), 0.7)
    glow = material(name + "Glow", glow_color, 0.15, 3)
    joints = {}

    def joint(label, location, parent=None):
        obj = pivot(label, tuple(value * scale for value in location), parent)
        joints[label] = (obj, obj.location.copy())
        return obj

    def plate(label, location, size, mat, parent, taper=1):
        return panel(label, tuple(x*scale for x in location),
                     tuple(x*scale for x in size), mat, parent, taper, 0.012*scale)

    root = joint("Model", (0, 0, 0))
    pivot("StrideEnd", (0, -4 * stride_reach(name) * scale, 0), root)
    torso = joint("Torso", (0, 0, 0.89), root)
    head = joint("Head", (0, -0.025, 0.67), torso)
    plate("PelvisFrame", (0, 0, -0.035), (width*0.82, 0.29, 0.21), frame, torso, 1.15)
    plate("Thorax", (0, 0.015, 0.34), (width*0.83, 0.35, 0.57), frame, torso, 1.19)
    plate("Backpack", (0, 0.22, 0.35), (width*0.65, 0.20, 0.40), armor, torso, 0.85)
    plate("Crest", (0, 0.02, 0.195), (width*0.38, 0.32, 0.26), armor, head, 0.78)
    plate("FaceFrame", (0, -0.105, 0.121), (width*0.31, 0.16, 0.15), frame, head, 1.18)
    plate("VisorGlow", (0, -0.195, 0.144), (width*0.30, 0.020, 0.041), glow, head, 1.12)
    plate("ChestCoreGlow", (0, -0.21, 0.34), (width*0.16, 0.045, 0.16), glow, torso, 1.28)
    plate("HelmetBrow", (0, -0.16, 0.197), (width*0.43, 0.10, 0.048), armor, head, 0.78)
    plate("Breather", (0, -0.189, 0.074), (width*0.25, 0.052, 0.072), frame, head, 1.15)
    plate("BeltBuckle", (0, -0.167, -0.018), (width*0.29, 0.06, 0.105), armor, torso, 0.84)

    detail_groups = {}

    def detail(label, location, size, mat, parent, taper=1):
        part = panel(label, tuple(x*scale for x in location),
                     tuple(x*scale for x in size), mat, parent, taper, 0)
        detail_groups.setdefault((parent, mat), []).append(part)

    for side, sign in (("L", -1), ("R", 1)):
        detail("OpticTemple" + side, (sign*width*0.184, -0.066, 0.152),
               (0.032, 0.115, 0.11), frame, head, 0.78)
        chest = plate("ChestPlate" + side, (sign*width*0.26, -0.18, 0.37),
                      (width*0.41, 0.115, 0.43), armor, torso, 1.22)
        chest.rotation_euler.y = sign * radians(13)
        detail("ChestInset", (0, -0.059, 0.08), (width*0.26, 0.010, 0.145), frame, chest, 1.16)
        detail("ChestSignalGlow", (0, -0.066, 0.11), (width*0.18, 0.010, 0.025), glow, chest, 0.82)
        for index in range(3):
            detail("ChestVent", (0, -0.058, -0.113 + index*0.031),
                   (width*0.23, 0.012, 0.012), frame, chest, 0.72)
        for index in range(3):
            plate(f"BackVent{side}{index}", (sign*width*0.15, 0.333, 0.26 + index*0.075),
                  (width*0.19, 0.024, 0.020), glow, torso, 0.86)

        arm = joint("Arm" + side, (sign*(width*0.57+0.05), 0, 0.54), torso)
        elbow = joint("Elbow" + side, (sign*0.035, 0, -0.29*reach), arm)
        plate("Pauldron" + side, (0, 0, 0.01), (width*0.47, 0.35, 0.20), armor, arm, 0.65)
        plate("UpperLink" + side, (0, 0, -0.14*reach), (0.13, 0.15, 0.25*reach), frame, arm)
        plate("Gauntlet" + side, (0, -0.025, -0.15*reach), (width*0.32, 0.25, 0.27*reach),
              armor, elbow, 0.8)
        plate("WristGlow" + side, (0, -0.154, -0.16*reach), (width*0.19, 0.015, 0.055),
              glow, elbow)
        plate("Claw" + side, (0, -0.036, -0.32*reach), (0.13, 0.18, 0.13), frame, elbow, 1.1)
        detail("ShoulderCap", (sign*0.018, -0.078, 0.082),
               (width*0.35, 0.225, 0.07), frame, arm, 0.73)
        detail("ForearmRail", (sign*width*0.167, -0.038, -0.15*reach),
               (0.036, 0.121, 0.21*reach), frame, elbow, 0.71)
        for finger in (-1, 0, 1):
            detail("Knuckle", (finger*0.037, -0.105, -0.316*reach),
                   (0.029, 0.052, 0.055), armor, elbow, 0.75)

        leg = joint("Leg" + side, (sign*width*0.24, 0, 0.89), root)
        knee = joint("Knee" + side, (0, -0.016, -0.415), leg)
        boot = joint("Boot" + side, (0, 0.016, -0.36), knee)
        plate("ThighPlate" + side, (0, -0.02, -0.20), (width*0.34, 0.23, 0.34), armor, leg, 1.19)
        plate("KneeGuard" + side, (0, -0.11, -0.036), (width*0.31, 0.13, 0.16), frame, knee, 1.2)
        plate("ShinPlate" + side, (0, 0, -0.215), (width*0.38, 0.21, 0.24), armor, knee, 0.73)
        plate("BootShell" + side, (0, -0.055, -0.041), (width*0.43, 0.32, 0.14), frame, boot, 0.87)
        detail("ToePlate", (0, -0.171, -0.034), (width*0.38, 0.11, 0.11), armor, boot, 0.75)
        detail("ShinFin", (sign*width*0.14, -0.066, -0.21),
               (0.039, 0.105, 0.22), frame, knee, 0.69)
        detail("KneeSignalGlow", (0, -0.18, -0.04), (width*0.19, 0.015, 0.033), glow, knee, 0.82)

    if name in {"Bulwark", "Warden"}:
        arm = joints["ElbowL"][0]
        plate("ShieldPlate", (-0.06, -0.21, -0.12), (width*0.85, 0.14, 0.93), frame, arm, 1.23)
        plate("ShieldTrim", (-0.06, -0.286, -0.12), (width*0.68, 0.055, 0.78), armor, arm, 1.19)
        plate("ShieldGlow", (-0.06, -0.322, -0.12), (width*0.17, 0.025, 0.58), glow, arm, 1.41)
    if name in {"Marksman", "Warden"}:
        arm = joints["ElbowR"][0]
        plate("EmitterShroud", (0.11, -0.10, -0.25), (width*0.43, 0.44, 0.24), frame, arm, 1.2)
        plate("EmitterGlow", (0.11, -0.326, -0.25), (width*0.28, 0.036, 0.13), glow, arm)
    if name in {"Runner", "Elite", "Warden"}:
        for sign in (-1, 1):
            fin = plate("CrownFin", (sign*width*0.29, 0.055, 0.25),
                        (0.12, 0.23, 0.26), armor, head, 0.22)
            fin.rotation_euler.y = sign*radians(23)
    if name == "Exploder":
        for sign in (-1, 1):
            plate("WarningCellGlow", (sign*width*0.45, 0.12, 0.40),
                  (0.14, 0.29, 0.46), glow, torso, 0.80)
    if name == "Warden":
        trim = material("WardenTrim", (0.52, 0.25, 0.08), 0.68)
        collar = ring("ReactorCollar", (0, -0.255*scale, 0.34*scale),
                      0.129*scale, 0.024*scale, 0.034*scale, trim, torso, 8)
        collar.rotation_euler.x = pi/2
        for side, sign in (("L", -1), ("R", 1)):
            arm = joints["Arm" + side][0]
            plate("Mantle" + side, (sign*0.10, 0.026, 0.095),
                  (0.37, 0.37, 0.11), trim, arm, 0.66)
            plate("MantleSignalGlow" + side, (sign*0.075, -0.161, 0.087),
                  (0.24, 0.028, 0.042), glow, arm, 0.85)
            plate("HipGuard" + side, (sign*0.27, -0.087, -0.12),
                  (0.19, 0.31, 0.37), armor, torso, 1.30).rotation_euler.y = sign*radians(18)
        shield = joints["ElbowL"][0]
        for i in range(3):
            plate("ShieldRib", (-0.06, -0.327, -0.39 + i*0.27),
                  (0.45, 0.046, 0.041), trim, shield, 0.87)
        plate("CrownSigilGlow", (0, -0.123, 0.237), (0.05, 0.026, 0.10),
              glow, head, 0.44)
    for (parent, mat), parts in detail_groups.items():
        combine_panels(parts, parent.name + "Detail", mat, parent)
    return joints


# Each take shares a neutral frame at its start unless the opening silhouette
# is part of a loop. Unity slices the baked FBX take at these frame numbers.
def timeline(joints, clips):
    scene = bpy.context.scene
    scene.frame_start = 0
    scene.frame_end = max(end for _, end, _ in clips)
    for start, end, keys in clips:
        pose(joints, start, {})
        pose(joints, end, {})
        for frame, angles, *travel in keys:
            pose(joints, start + frame, angles, *travel)
    scene.frame_set(0)


def position_keys(obj, start, keys):
    rest = obj.location.copy()
    for frame, offset in keys:
        obj.location = rest + Vector(offset)
        obj.keyframe_insert("location", frame=start + frame)
    bpy.context.scene.frame_set(0)


def stride_reach(name):
    return {"Runner": 0.31, "Bulwark": 0.245, "Warden": 0.27}.get(name, 0.28)


def locomotion(name, height, length):
    """Bake a symmetric walk from planted-foot targets and two-bone leg IK."""
    reach = stride_reach(name)
    scale = height / 1.9
    upper = hypot(0.415, 0.016)
    lower = hypot(0.36, 0.016)
    lift = {"Runner": 0.17, "Bulwark": 0.09, "Warden": 0.10}.get(name, 0.13)
    arm_swing = {"Runner": 26, "Bulwark": 12, "Warden": 14}.get(name, 19)

    for frame in range(length + 1):
        phase = (frame % length) / length
        weight = sin(2*pi*phase)
        counter = cos(2*pi*phase)
        pelvis = -0.076 + 0.031 * sin(2*pi*phase - 0.22)**2
        # Offset the chest and head so the upper body follows the hips instead
        # of all joints reversing together on the contact frame.
        shoulder_swing = cos(2*pi*phase - 0.28)
        head_follow = cos(2*pi*phase - 0.57)
        angles = {
            "Torso": (5 + (4 if name == "Runner" else 2)*sin(4*pi*phase - 0.25),
                      3.4*weight, 6*shoulder_swing),
            "Head": (-3 - 2*sin(4*pi*phase - 0.55), -2.5*weight, -4.8*head_follow),
        }
        for side, offset, sign in (("L", 0, -1), ("R", 0.5, 1)):
            cycle = (phase + offset) % 1
            swing = max(0, cycle * 2 - 1)
            if cycle < 0.5:
                forward = reach * (4*cycle - 1)
            else:
                forward = reach * cos(pi*swing)
            raised = lift * sin(pi*swing)**2
            drop = 0.779 + pelvis - raised
            distance = min(upper + lower - 0.0001, hypot(forward, drop))
            hip = atan2(forward, drop) - acos((upper**2 + distance**2 - lower**2)/(2*upper*distance))
            bend = pi - acos((upper**2 + lower**2 - distance**2)/(2*upper*lower))
            leg_angle = hip - atan2(-0.016, 0.415)
            knee_angle = hip + bend - leg_angle - atan2(0.016, 0.36)
            roll = 18 * sin(2*pi*swing) * sin(pi*swing)
            angles["Leg" + side] = (degrees(leg_angle), 0, 0)
            angles["Knee" + side] = (degrees(knee_angle), 0, 0)
            angles["Boot" + side] = (roll - degrees(leg_angle + knee_angle), 0, 0)
            angles["Arm" + side] = (-sign*shoulder_swing*arm_swing, sign*4*weight, sign*(8+2*counter))
            angles["Elbow" + side] = (-20 + 11*sign*cos(2*pi*phase - 0.52), 0, 0)
        yield (frame, angles, (0, 0, pelvis*scale))


def actor_clips(name, height):
    attack = {"Bulwark": "ArmL", "Warden": "ArmL"}.get(name, "ArmR")
    ranged = name in {"Marksman", "Warden"}
    return [
        (0, 60, [(12, {"Torso": (2, -2, -3), "ArmL": (-4, 0, -2), "ElbowR": (-7, 0, 0)},
                  (0, 0, 0.009)),
                 (23, {"Torso": (2, -1, -2), "Head": (-4, -2, 7), "ElbowL": (-5, 0, 0)}),
                 (39, {"Torso": (-2, 2, 3), "Head": (2, 1, 1), "ArmR": (-5, 0, 2)}),
                 (51, {"Torso": (-1, 1, 2), "Head": (1, 0, -6), "ElbowR": (-3, 0, 0)})]),
        (62, 92, list(locomotion(name, height, 30))),
        (94, 153, [(9, {"Torso": (-12, 0, -13), "Head": (7, 0, 6), attack: (38, -14, 14)}),
                    (23, {"Torso": (-21, 0, -9), attack: (66, -12, 16),
                          "ArmL": (62, 0, -14), "ArmR": (61, 0, 14)} if name == "Warden"
                     else {"Torso": (-16, 0, -16), attack: (62, 0, 16)}),
                    (26, {"Torso": (29, 0, 12), attack: (-103, 0, -6),
                          "KneeL": (26, 0, 0), "KneeR": (26, 0, 0)}, (0, 0, -0.08*height)),
                    (31, {"Torso": (23, 0, 16), "Head": (-11, 0, 8), attack: (-88, 0, -15)}),
                    (41, {"Torso": (12, 0, 5), "Head": (-8, 0, 4), attack: (-48, 0, 0)}),
                    (49, {"Torso": (-3, 0, -2), attack: (8, 0, 4)})]),
        (155, 195, [(10, {"Torso": (-8, 0, -14), "ArmR": (-81, 0, 7),
                          "ElbowR": (-9, 0, 0), "Head": (4, 0, -5)}),
                    (18, {"ArmR": (-86, 0, 4), "Torso": (-4, 0, -13)}),
                    (19, {"ArmR": (-103, 0, 8), "Torso": (-13, 0, -15)}),
                    (25, {"ArmR": (-85, 0, 4), "Torso": (-4, 0, -12)})] if ranged else
                   [(18, {"Torso": (9, 0, -12), "ArmL": (-53, 0, -10), "ArmR": (-47, 0, 10)})]),
        (197, 251, [(13, {"Torso": (18, 0, 0), "Head": (12, 0, 0),
                          "ArmL": (-31, 0, -12), "ArmR": (-31, 0, 12)}),
                    (21, {"Torso": (-23, 0, 0), "Head": (-26, 0, 0),
                          "ArmL": (-70, 0, -52), "ArmR": (-70, 0, 52)}),
                    (42, {"Torso": (-13, 0, 0), "Head": (-15, 0, 0),
                          "ArmL": (-52, 0, -40), "ArmR": (-52, 0, 40)})]),
        (253, 269, [(3, {"Torso": (-20, -8, 11), "Head": (-17, 7, 12),
                         "ArmL": (21, 0, -12), "ArmR": (17, 0, 10)}),
                    (9, {"Torso": (7, 3, -5), "Head": (4, -6, -8)})]),
        (271, 397, [(7, {"Torso": (-22, 0, 12), "Head": (-18, 0, 9),
                         "ArmL": (24, 0, -28), "ArmR": (13, 0, 27)}),
                    (24, {"Torso": (25, 0, -9), "KneeL": (49, 0, 0), "KneeR": (31, 0, 0)},
                     (0, 0, -0.17*height)),
                    (43, {"Model": (84, -4, 2), "Torso": (11, 0, -7),
                          "ArmL": (-32, 0, -26), "ArmR": (-15, 0, 15)}, (0, -0.09*height, 0.11*height)),
                    (397-271, {"Model": (87, -4, 2), "Torso": (5, 0, -7),
                               "ArmL": (-26, 0, -29), "ArmR": (-14, 0, 13)},
                     (0, -0.09*height, 0.09*height))]),
    ]


ACTORS = [
    ("Trooper", 0.53, 1.90, 0.93, (0.63, 0.22, 0.12), (1.0, 0.38, 0.10)),
    ("Runner", 0.37, 1.63, 1.18, (0.46, 0.23, 0.64), (1.0, 0.20, 0.66)),
    ("Bulwark", 0.77, 1.92, 0.90, (0.17, 0.33, 0.39), (0.12, 0.85, 1.0)),
    ("Marksman", 0.43, 1.91, 0.98, (0.24, 0.48, 0.35), (0.34, 1.0, 0.55)),
    ("Elite", 0.63, 2.02, 1.05, (0.74, 0.53, 0.22), (1.0, 0.64, 0.11)),
    ("Exploder", 0.51, 1.82, 0.91, (0.76, 0.45, 0.11), (1.0, 0.80, 0.11)),
    ("Warden", 0.78, 3.48, 1.08, (0.70, 0.24, 0.16), (1.0, 0.46, 0.13)),
]


def build_actors():
    for spec in ACTORS:
        reset()
        name = spec[0]
        filename = "BossWarden" if name == "Warden" else "Enemy" + name
        joints = actor(*spec)
        timeline(joints, actor_clips(name, spec[2]))
        save(filename)
        export(filename, True)


def build_viewmodel():
    reset()
    original = ROOT.parent / "Assets" / "Models" / "ShooterRifle.fbx"
    bpy.ops.import_scene.fbx(filepath=str(original))
    root = bpy.data.objects["RifleRoot"]
    source_parts = tuple(root.children)
    handling = pivot("Handling", parent=root)
    ceramic = material("ServiceSteel", (0.17, 0.19, 0.21), 0.65)
    suit = material("ServiceFabric", (0.055, 0.071, 0.074), 0.06)
    finishes = {
        "GunBody": material("ServiceGraphite", (0.053, 0.061, 0.072), 0.65),
        "GunMetal": ceramic,
        "GunRubber": material("ServicePolymer", (0.025, 0.028, 0.032), 0.06),
        "GunGlow": material("ServiceIndicatorGlow", (0.14, 0.62, 0.54), 0.2, 1.3),
        "GunAmber": material("ServiceOpticGlow", (0.98, 0.59, 0.20), 0.2, 1.7),
    }
    mag_mesh = bpy.data.objects["Magazine"]
    mag_rest = tuple(mag_mesh.location)
    mag_mesh.name = "MagazineBody"
    magazine = pivot("Magazine", mag_rest, handling)
    replacement = pivot("ReplacementMagazine", mag_rest, handling)
    for part in source_parts:
        part.parent = handling
        if part.type == "MESH":
            for index, slot in enumerate(part.data.materials):
                part.data.materials[index] = finishes.get(slot.name, ceramic)

    # The imported parts share the rifle's original local coordinate space.
    # Attach them to their animated controls without changing their rest pose.
    for part in (mag_mesh, bpy.data.objects["MagazineBase"], bpy.data.objects["MagazineRib"]):
        part.parent = magazine
        part.location -= Vector(mag_rest)
        duplicate = part.copy()
        duplicate.data = part.data.copy()
        duplicate.parent = replacement
        bpy.context.collection.objects.link(duplicate)

    slide = bpy.data.objects["Slide"]
    slide_handle = bpy.data.objects["SlideHandle"]
    slide_handle.location = slide.matrix_basis.inverted() @ slide_handle.location
    slide_handle.parent = slide

    dot = bpy.data.objects["OpticDot"]
    dot.scale *= 0.55
    # The old front post sat in the optic's opening; keep the base and use the
    # illuminated optic dot as the single sight reference.
    bpy.data.objects.remove(bpy.data.objects["FrontSightPost"], do_unlink=True)
    bpy.data.objects.remove(bpy.data.objects["OpticDotTick"], do_unlink=True)

    for side, wrist_pos, sleeve_pos in (
        ("R", (-0.024, 0.116, -0.167), (-0.085, 0.33, -0.225)),
        ("L", (0.038, -0.257, -0.112), (0.088, 0.007, -0.237)),
    ):
        hand = pivot("Hand" + side, wrist_pos, handling)
        panel("Glove" + side, (0, -0.025, 0), (0.068, 0.101, 0.068), suit, hand, 0.92)
        panel("Knuckle" + side, (0, -0.039, 0.033), (0.057, 0.063, 0.021), ceramic, hand)
        sleeve_vec = Vector(sleeve_pos)-Vector(wrist_pos)
        if side == "L":
            sleeve_vec *= 0.65
        sleeve_width, sleeve_depth = (0.074, 0.068) if side == "L" else (0.102, 0.094)
        sleeve = panel("Sleeve" + side, sleeve_vec/2, (sleeve_width, sleeve_depth, sleeve_vec.length),
                       suit, hand, 0.74)
        sleeve.rotation_euler = sleeve_vec.to_track_quat("Z", "Y").to_euler()
    hand_l = bpy.data.objects["HandL"]
    joints = {obj.name: (obj, obj.location.copy()) for obj in (handling, magazine, hand_l)}
    joints["Model"] = joints.pop("Handling")
    timeline(joints, [
        (0, 60, [(15, {"Model": (0.40, -0.35, 0.25)}, (0.002, 0, 0.002)),
                 (45, {"Model": (-0.40, 0.35, -0.25)}, (-0.002, 0, -0.002))]),
        (62, 92, [(7, {"Model": (-1.8, 1.1, 1.6)}, (0.008, 0, -0.014)),
                  (22, {"Model": (1.6, -0.8, -1.1)}, (-0.008, 0, -0.011))]),
        (94, 103, [(1, {"Model": (4, 0, 1)}, (0, 0.024, 0.004)),
                   (4, {"Model": (-1, 0, 0)}, (0, 0.008, -0.003))]),
        (105, 125, [(2, {"Model": (8, -1, 3)}, (0.004, 0.055, 0.012)),
                    (8, {"Model": (2, 0, -2)}, (0, 0.018, -0.009))]),
        (127, 172, [(9, {"Model": (-3, -16, -8), "HandL": (12, -5, 8)}, (0.040, -0.080, 0.090)),
                    (16, {"Model": (-2, -23, -11), "HandL": (15, -7, 12)}, (0.060, -0.14, 0.16)),
                    (26, {"Model": (-2, -22, -10), "HandL": (15, -7, 11)}, (0.063, -0.145, 0.16)),
                    (35, {"Model": (1, -19, -8), "HandL": (11, -4, 8)}, (0.050, -0.10, 0.14)),
                    (39, {"Model": (2, -11, -5), "HandL": (7, -2, 4)}, (0.028, -0.055, 0.070))]),
        (174, 246, [(10, {"Model": (-10, -4, 10)}, (-0.02, 0.04, -0.06)),
                    (46, {"Model": (-17, 2, 15), "HandL": (30, 8, -19)}, (-0.02, 0.05, -0.07)),
                    (64, {"Model": (6, 0, -4)}, (0, 0.02, -0.03))]),
        (248, 260, [(2, {"Model": (-19, 0, 20)}, (0.06, 0.06, -0.02)),
                    (5, {"Model": (12, 4, -31)}, (-0.13, -0.08, 0.03))]),
        (262, 279, [(3, {"Model": (7, 0, -7)}, (-0.018, 0.046, -0.027)),
                    (9, {"Model": (-3, 0, 3)}, (0.008, 0.020, -0.012))]),
        (281, 341, [(12, {"Model": (-14, -10, 17)}, (0.03, 0.14, -0.10)),
                    (60, {"Model": (-53, -20, 33)}, (0.05, 0.17, -0.32))]),
        (343, 409, [(13, {"Model": (-4, -22, -14)}, (0.09, -0.13, 0.08)),
                    (27, {"Model": (-4, -39, -24)}, (0.14, -0.19, 0.105)),
                    (43, {"Model": (1, -31, -19)}, (0.12, -0.17, 0.095)),
                    (55, {"Model": (1, -14, -8)}, (0.06, -0.085, 0.050))]),
    ])
    stow = (-0.07, 0.115, -0.35)
    empty_path = [(0, (0, 0, 0)), (10, (0, 0, 0)), (15, (0.013, 0.020, -0.088)),
                  (19, (0.022, 0.032, -0.15)), (22, stow), (23, stow)]
    fresh_path = [(23, stow), (28, (0.022, 0.041, -0.14)), (32, (0.008, 0.018, -0.062)),
                  (35, (0, 0, 0)), (38, (0, 0, 0))]
    position_keys(magazine, 127, [*empty_path, (38, (0, 0, 0))])
    position_keys(replacement, 127, fresh_path)
    # The supporting hand follows the visible magazine through the exchange.
    grasp = Vector(mag_rest) + Vector((0.048, 0.010, -0.060)) - hand_l.location
    hand_path = [(0, (0, 0, 0)), (5, (0.021, 0.08, -0.033))]
    hand_path.extend((time, tuple(grasp + Vector(offset)))
                     for time, offset in [*empty_path[1:-1], *fresh_path])
    position_keys(hand_l, 127, [*hand_path, (45, (0, 0, 0))])
    for mag, is_original in ((magazine, True), (replacement, False)):
        for frame in (0, 149, 150, 165, 166, 341):
            showing_replacement = 150 <= frame <= 165
            mag.scale = (1 if is_original != showing_replacement else 0.001,) * 3
            mag.keyframe_insert("scale", frame=frame)
    bpy.context.scene.frame_set(0)
    save("PlayerView")
    export("PlayerView", True)



def build_arena_kit():
    reset()
    frame = material("FoundryFrame", (0.048, 0.073, 0.088), 0.65)
    ceramic = material("FoundryCeramic", (0.44, 0.50, 0.50))
    floor = material("FoundryFloor", (0.17, 0.22, 0.24))
    floor_alt = material("FoundryFloorAlt", (0.148, 0.196, 0.217))
    signal = material("FoundrySignal", (0.07, 0.77, 0.72), 0.2, 3)
    warning = material("FoundryWarning", (0.96, 0.35, 0.07), 0.2, 1.5)
    roots = {name: pivot(name) for name in
             ("Ground", "Wall", "Cover", "Pillar", "Platform", "Ramp", "Pad", "Trim")}
    for name, obj in roots.items():
        if name == "Ramp":
            # The scene builder already applies the slope to the collider.
            panel("RampDeck", (0, 0, -0.060), (0.98, 0.98, 0.88), floor, obj, 0.98)
            for index in range(7):
                panel("RampGrip", (0, index/8 - 0.375, 0.397), (0.80, 0.025, 0.027),
                      frame, obj, bevel=0.002)
            continue
        if name == "Trim":
            panel("TrimSeat", (0, 0, 0), (0.99, 0.98, 0.92), frame, obj)
            panel("TrimSignal", (0, 0, 0.47), (0.92, 0.55, 0.06), signal, obj)
            continue
        deck = name in {"Ground", "Platform", "Pad"}
        frame_height = 0.94 if deck else 1
        panel(name + "Frame", (0, 0, (frame_height - 1)/2),
              (1, 1, frame_height), frame, obj, 0.99, 0.009)
        if name == "Ground":
            tile_count = 8
            tile_span = 1 / tile_count
            groups = {floor: [], floor_alt: []}
            for x in range(tile_count):
                for y in range(tile_count):
                    mat = floor_alt if (x + 2*y) % 5 == 0 else floor
                    center = ((x+0.5)*tile_span - 0.5, (y+0.5)*tile_span - 0.5, 0.467)
                    size = (tile_span - 0.0014, tile_span - 0.0014, 0.066)
                    groups[mat].append(panel("Tile", center, size, mat, obj, bevel=0))
            for mat, parts in groups.items():
                combine_panels(parts, mat.name + "Panels", mat, obj)
        elif deck:
            for x in (-1, 1):
                for y in (-1, 1):
                    panel("DeckTile", (0.25*x, 0.25*y, 0.467), (0.487, 0.487, 0.066),
                          ceramic, obj, bevel=0.002)
        else:
            for sign in (-1, 1):
                panel("Armor", (0, sign*0.47, 0.02), (0.91, 0.051, 0.85),
                      ceramic, obj, 0.87)
                for i in range(3):
                    slash = panel("Signal", ((i-1)*0.18, sign*0.499, -0.21),
                                  (0.065, 0.018, 0.27), warning if name == "Cover" else signal, obj)
                    slash.rotation_euler.y = radians(-23)
    variants = list(roots.items())
    for i, (_, obj) in enumerate(variants):
        obj.location.x = i*1.35
    save("ArenaKit")
    bpy.ops.object.select_all(action="DESELECT")
    for name, obj in variants:
        obj.location.x = 0
        for part in (obj, *obj.children_recursive):
            part.select_set(True)
        bpy.ops.export_scene.fbx(
            filepath=str(OUTPUT / (name + ".fbx")), use_selection=True,
            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_anim=False)
        for part in (obj, *obj.children_recursive):
            part.select_set(False)


def build_pickup(name, color, is_health):
    reset()
    shell = material("FoundryFrame", (0.045, 0.064, 0.078), 0.5)
    glow = material(name + "Signal", color, emission=3)
    obj = pivot("Pickup")
    panel("Shell", (0, 0, 0), (0.46, 0.36, 0.32), shell, obj, 0.76)
    ring("SignalRim", (0, 0, 0.16), 0.185, 0.030, 0.026, glow, obj, 8)
    for sign in (-1, 1):
        if is_health:
            panel("CrossStem", (0, sign*0.18, 0), (0.05, 0.019, 0.17), glow, obj)
            panel("CrossBeam", (0, sign*0.18, 0), (0.18, 0.019, 0.05), glow, obj)
        else:
            for index in (-1, 0, 1):
                panel("ChargeCell", (index*0.075, sign*0.18, 0), (0.036, 0.018, 0.14),
                      glow, obj, 0.72)
    joints = {"Model": (obj, obj.location.copy())}
    timeline(joints, [(0, 72, [(0, {"Model": (0, 0, -180)}),
                                (18, {"Model": (0, 0, -90)}, (0, 0, 0.075)),
                                (36, {"Model": (0, 0, 0)}, (0, 0, 0.12)),
                                (54, {"Model": (0, 0, 90)}, (0, 0, 0.075)),
                                (72, {"Model": (0, 0, 180)})])])
    save(name)
    export(name, True)


def build_effect(name, color, radius, duration, rays):
    reset()
    glow = material(name + "Glow", color, emission=5)
    root = pivot("Effect")
    if name in {"ShockRing", "Explosion"}:
        ring("Wavefront", (0, 0, 0.02), radius, radius*0.09, 0.025, glow, root, 32)
    for i in range(rays):
        angle = 2*pi*i/rays
        group = pivot("Ray%02d" % i, parent=root)
        group.rotation_euler.z = angle
        length = radius*(0.46 + 0.14*(i % 3))
        blade = loft("Shard", (length*0.40, 0, 0.025),
                     [(-length/2, radius*0.095, radius*0.085, 0),
                      (-length/4, radius*0.14, radius*0.11, 0),
                      (length/2, radius*0.014, radius*0.011, 0)],
                     glow, group, 0)
        blade.rotation_euler.y = radians(62 if name != "ChargeFlash" else 42)
        for t, x in ((0, 0), (max(1, duration//4), radius*0.19), (duration, radius*0.85)):
            group.location = (x*cos(angle), x*sin(angle), x*0.28)
            group.keyframe_insert("location", frame=t)
    for frame, scale in ((0, 0.8), (1, 1.0), (duration-1, 1.3), (duration, 0.001)):
        root.scale = (scale,)*3
        root.keyframe_insert("scale", frame=frame)
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = duration
    bpy.context.scene.frame_set(0)
    save(name)
    export(name, True)


def build_effects():
    for spec in (
        ("ChargeFlash", (0.26, 1.0, 0.78), 0.13, 5, 6),
        ("BreachFlash", (1.0, 0.68, 0.23), 0.19, 7, 8),
        ("ImpactSpark", (1.0, 0.84, 0.39), 0.25, 11, 7),
        ("Explosion", (1.0, 0.36, 0.11), 1.0, 19, 9),
        ("ShockRing", (1.0, 0.49, 0.15), 1.0, 24, 0),
        ("EnemyBolt", (1.0, 0.21, 0.08), 0.10, 9, 3),
        ("Tracer", (0.30, 0.96, 0.85), 0.16, 5, 4),
        ("DebrisShard", (0.57, 0.34, 0.19), 0.14, 18, 3),
    ):
        build_effect(*spec)


def build_secondary_models():
    for name in ("Conduit", "Hardware", "Drum", "SpentCell", "ImpactMark"):
        reset()
        finish = material("FoundryFrame", (0.038, 0.057, 0.067), 0.7)
        if name == "Conduit":
            loft(name, (0, 0, 0), [(-1, 0.84, 0.84, 0), (-0.83, 1, 1, 0),
                                   (0.83, 1, 1, 0), (1, 0.84, 0.84, 0)], finish, None)
        elif name == "Drum":
            loft(name, (0, 0, 0), [(-1, 0.91, 0.91, 0), (-0.90, 1, 1, 0),
                                   (-0.72, 0.87, 0.87, 0), (0.70, 0.87, 0.87, 0),
                                   (0.80, 1, 1, 0), (1, 0.91, 0.91, 0)], finish, None)
        elif name == "SpentCell":
            loft(name, (0, 0, 0), [(-0.50, 0.67, 0.67, 0), (-0.29, 1, 1, 0),
                                   (0.17, 1, 1, 0), (0.50, 0.71, 0.71, 0)], finish, None)
        elif name == "ImpactMark":
            ring(name, (0, 0, 0), 0.5, 0.13, 0.045, finish, None, 8)
        else:
            loft(name, (0, 0, 0), [(-0.5, 0.82, 0.82, 0), (-0.34, 1, 1, 0),
                                   (0.27, 1, 1, 0), (0.50, 0.75, 0.84, 0)], finish, None)
        save(name)
        export(name)


if __name__ == "__main__":
    build_actors()
    build_viewmodel()
    build_arena_kit()
    build_pickup("AmmoCache", (1.0, 0.62, 0.13), False)
    build_pickup("HealthCache", (0.26, 1.0, 0.54), True)
    build_effects()
    build_secondary_models()
