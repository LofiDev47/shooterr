"""Procedural sci-fi rifle for the Shooter project.

Run headless:
    blender -b -P Tools/Blender/build_rifle.py

The model is authored with the muzzle toward Blender -Y so the standard FBX
axis conversion can be inspected in Unity. Every visible part is a separate
named object; Unity reassigns the project's URP materials by name, and the
"Slide" object is animated procedurally by Weapon.cs.

Coordinates are metres. The rifle is roughly 1.1 m from butt pad to muzzle.
"""

import math
import os

import bpy

ROOT = "/home/lofi/shooterr"
OUT_DIR = os.path.join(ROOT, "Assets", "Models")
OUT_FILE = os.path.join(OUT_DIR, "ShooterRifle.fbx")

os.makedirs(OUT_DIR, exist_ok=True)


# ----------------------------------------------------------------- helpers

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def make_material(name, color, metallic, roughness, emission=None, emission_strength=0.0):
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


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_bevel(obj, width=0.0035, segments=2):
    if width <= 0.0:
        return

    modifier = obj.modifiers.new("Bevel", "BEVEL")
    modifier.width = width
    modifier.segments = segments
    modifier.limit_method = "ANGLE"
    modifier.angle_limit = math.radians(40.0)

    select_only(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def add_box(name, location, size, material, rotation=(0.0, 0.0, 0.0), bevel=0.0035, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = size
    obj.rotation_euler = rotation

    select_only(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    apply_bevel(obj, bevel)
    obj.data.materials.append(material)
    obj.parent = parent
    return obj


def add_cylinder(name, location, radius, depth, material, axis="Y", vertices=24,
                 rotation_extra=(0.0, 0.0, 0.0), parent=None, smooth=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location)
    obj = bpy.context.object
    obj.name = name

    if axis == "Y":
        obj.rotation_euler = (math.pi * 0.5, 0.0, 0.0)
    elif axis == "X":
        obj.rotation_euler = (0.0, math.pi * 0.5, 0.0)
    else:
        obj.rotation_euler = (0.0, 0.0, 0.0)

    select_only(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    if smooth:
        for polygon in obj.data.polygons:
            polygon.use_smooth = True

    obj.data.materials.append(material)
    obj.parent = parent
    return obj


def add_torus(name, location, major_radius, minor_radius, material, rotation=(0.0, 0.0, 0.0), parent=None):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=24,
        minor_segments=8,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name

    select_only(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    for polygon in obj.data.polygons:
        polygon.use_smooth = True

    obj.data.materials.append(material)
    obj.parent = parent
    return obj


def add_empty(name, location, parent=None):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.03
    obj.parent = parent
    return obj


# ----------------------------------------------------------------- build

reset_scene()

body = make_material("GunBody", (0.045, 0.05, 0.06), 0.85, 0.32)
metal = make_material("GunMetal", (0.14, 0.15, 0.17), 0.9, 0.25)
rubber = make_material("GunRubber", (0.02, 0.022, 0.025), 0.15, 0.75)
glow = make_material("GunGlow", (0.05, 0.18, 0.22), 0.2, 0.3,
                     emission=(0.1, 0.85, 1.0), emission_strength=6.0)
amber = make_material("GunAmber", (0.3, 0.18, 0.04), 0.3, 0.4,
                      emission=(1.0, 0.6, 0.15), emission_strength=2.5)

root = bpy.data.objects.new("RifleRoot", None)
bpy.context.collection.objects.link(root)
root.empty_display_type = "PLAIN_AXES"
root.empty_display_size = 0.1

# ---- receiver and lower

add_box("Receiver", (0.0, 0.045, 0.0), (0.095, 0.37, 0.115), body, parent=root, bevel=0.006)
add_box("LowerReceiver", (0.0, 0.08, -0.062), (0.082, 0.25, 0.05), body, parent=root, bevel=0.005)
add_box("ReceiverTop", (0.0, 0.045, 0.062), (0.085, 0.33, 0.018), metal, parent=root, bevel=0.004)

# Side panels break up the slab-sided receiver.
add_box("PanelL", (-0.0505, 0.04, -0.012), (0.007, 0.22, 0.07), metal, parent=root, bevel=0.003)
add_box("PanelR", (0.0505, 0.04, -0.012), (0.007, 0.22, 0.07), metal, parent=root, bevel=0.003)

# ---- handguard and barrel

add_box("Handguard", (0.0, -0.28, 0.002), (0.086, 0.27, 0.098), body, parent=root, bevel=0.006)
add_box("HandguardTop", (0.0, -0.28, 0.058), (0.052, 0.27, 0.018), metal, parent=root, bevel=0.004)
add_box("HandguardBottom", (0.0, -0.28, -0.052), (0.06, 0.25, 0.014), metal, parent=root, bevel=0.004)

# Vent slots: small dark insets along both sides.
for index in range(5):
    y = -0.37 + index * 0.045
    add_box(f"VentL{index}", (-0.0455, y, 0.005), (0.004, 0.022, 0.055), rubber, parent=root, bevel=0.0015)
    add_box(f"VentR{index}", (0.0455, y, 0.005), (0.004, 0.022, 0.055), rubber, parent=root, bevel=0.0015)

add_cylinder("Barrel", (0.0, -0.50, 0.015), 0.017, 0.20, metal, axis="Y", parent=root)
add_cylinder("BarrelShroud", (0.0, -0.43, 0.015), 0.028, 0.07, metal, axis="Y", parent=root)
add_box("GasBlock", (0.0, -0.405, 0.05), (0.05, 0.05, 0.055), body, parent=root, bevel=0.004)

# Muzzle brake: a cylindrical brake with rings and three vent cuts on top.
add_cylinder("MuzzleBrake", (0.0, -0.625, 0.015), 0.037, 0.075, body, axis="Y", parent=root)
add_cylinder("BrakeRingFront", (0.0, -0.660, 0.015), 0.043, 0.014, metal, axis="Y", parent=root)
add_cylinder("BrakeRingRear", (0.0, -0.590, 0.015), 0.043, 0.014, metal, axis="Y", parent=root)
for index in range(3):
    add_box(f"BrakeVent{index}", (0.0, -0.645 + index * 0.02, 0.048), (0.062, 0.008, 0.012), rubber, parent=root, bevel=0.001)

# ---- rail and optic

add_box("TopRail", (0.0, 0.07, 0.075), (0.05, 0.36, 0.018), metal, parent=root, bevel=0.003)
for index in range(9):
    y = -0.08 + index * 0.04
    add_box(f"RailTooth{index}", (0.0, y, 0.088), (0.052, 0.014, 0.012), metal, parent=root, bevel=0.0015)

add_box("OpticMount", (0.0, 0.03, 0.10), (0.052, 0.10, 0.03), body, parent=root, bevel=0.004)

# Open holographic sight. There is deliberately no tube or lens: the camera
# looks through the aperture in ADS, so a solid cylinder would read as a
# black ball. Four thin bars frame a floating amber dot.
add_box("OpticFrameTop", (0.0, 0.03, 0.170), (0.060, 0.020, 0.009), metal, parent=root, bevel=0.0015)
add_box("OpticFrameBottom", (0.0, 0.03, 0.100), (0.060, 0.020, 0.009), metal, parent=root, bevel=0.0015)
add_box("OpticFrameLeft", (-0.0285, 0.03, 0.135), (0.009, 0.020, 0.079), metal, parent=root, bevel=0.0015)
add_box("OpticFrameRight", (0.0285, 0.03, 0.135), (0.009, 0.020, 0.079), metal, parent=root, bevel=0.0015)
add_box("OpticDot", (0.0, 0.018, 0.135), (0.007, 0.002, 0.007), amber, parent=root, bevel=0.0)
add_box("OpticDotTick", (0.0, 0.018, 0.117), (0.002, 0.002, 0.014), amber, parent=root, bevel=0.0)

# Iron sights: a front post and a rear notch made from two ears.
add_box("FrontSightBase", (0.0, -0.385, 0.095), (0.05, 0.03, 0.018), metal, parent=root, bevel=0.002)
add_box("FrontSightPost", (0.0, -0.385, 0.118), (0.008, 0.012, 0.03), metal, parent=root, bevel=0.001)
add_box("RearSightBase", (0.0, 0.235, 0.09), (0.05, 0.024, 0.016), metal, parent=root, bevel=0.002)
add_box("RearSightL", (-0.018, 0.235, 0.108), (0.01, 0.012, 0.022), metal, parent=root, bevel=0.001)
add_box("RearSightR", (0.018, 0.235, 0.108), (0.01, 0.012, 0.022), metal, parent=root, bevel=0.001)

# ---- magazine, grip, trigger

add_box("Magazine", (0.0, -0.012, -0.155), (0.062, 0.125, 0.22), body,
        rotation=(math.radians(-11.0), 0.0, 0.0), parent=root, bevel=0.005)
add_box("MagazineBase", (0.0, -0.030, -0.262), (0.07, 0.135, 0.022), metal,
        rotation=(math.radians(-11.0), 0.0, 0.0), parent=root, bevel=0.003)
add_box("MagazineRib", (0.0, -0.012, -0.145), (0.066, 0.10, 0.012), metal,
        rotation=(math.radians(-11.0), 0.0, 0.0), parent=root, bevel=0.002)

add_box("Grip", (0.0, 0.115, -0.14), (0.054, 0.075, 0.19), rubber,
        rotation=(math.radians(17.0), 0.0, 0.0), parent=root, bevel=0.007)
add_box("GripCap", (0.0, 0.083, -0.232), (0.05, 0.07, 0.018), metal,
        rotation=(math.radians(17.0), 0.0, 0.0), parent=root, bevel=0.003)

add_torus("TriggerGuard", (0.0, 0.055, -0.085), 0.045, 0.006, metal,
          rotation=(0.0, math.pi * 0.5, 0.0), parent=root)
add_box("Trigger", (0.0, 0.055, -0.083), (0.008, 0.012, 0.032), metal, parent=root, bevel=0.0015)

# ---- stock

add_box("StockArm", (0.0, 0.255, 0.012), (0.058, 0.14, 0.055), body, parent=root, bevel=0.005)
# Skeleton stock: top and bottom struts leave an open channel so it does not
# read as a solid block behind the receiver.
add_box("StockTop", (0.0, 0.36, 0.050), (0.062, 0.17, 0.026), body, parent=root, bevel=0.004)
add_box("StockBottom", (0.0, 0.36, -0.045), (0.062, 0.17, 0.024), body, parent=root, bevel=0.004)
add_box("StockSpine", (0.0, 0.435, 0.002), (0.066, 0.032, 0.12), body, parent=root, bevel=0.004)
add_box("CheekRiser", (0.0, 0.36, 0.072), (0.056, 0.13, 0.020), rubber, parent=root, bevel=0.004)
add_box("ButtPad", (0.0, 0.458, 0.0), (0.078, 0.028, 0.135), rubber, parent=root, bevel=0.005)

# ---- moving bolt / charging handle (animated by Weapon.cs)

add_box("Slide", (0.055, 0.145, 0.04), (0.028, 0.12, 0.032), metal, parent=root, bevel=0.003)
add_cylinder("SlideHandle", (0.078, 0.155, 0.04), 0.011, 0.026, body, axis="X", parent=root)
add_box("EjectionPort", (0.051, 0.075, 0.015), (0.006, 0.085, 0.032), rubber, parent=root, bevel=0.001)

# ---- emissive details

add_box("StatusGlow", (0.0545, 0.02, 0.012), (0.006, 0.15, 0.018), glow, parent=root, bevel=0.001)
add_box("SideAccent", (-0.0545, 0.02, 0.012), (0.005, 0.10, 0.008), amber, parent=root, bevel=0.001)

# ---- anchor points for Unity

add_empty("MuzzlePoint", (0.0, -0.680, 0.015), parent=root)
add_empty("EjectPoint", (0.060, 0.105, 0.03), parent=root)

# ----------------------------------------------------------------- export

bpy.ops.object.select_all(action="SELECT")

bpy.ops.export_scene.fbx(
    filepath=OUT_FILE,
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

print(f"[rifle] exported {OUT_FILE}")
