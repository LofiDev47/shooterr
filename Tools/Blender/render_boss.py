"""Render the boss for inspection.

    blender -b -P Tools/Blender/render_boss.py -- <clip> <frame> <angle> <out.png>

angle is degrees around Z (0 = looking at the front, i.e. -Y face).
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = "/home/lofi/shooterr"
OUT_DIR = os.path.join(ROOT, "Assets", "Models", "Boss")

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
clip = args[0] if len(args) > 0 else "Idle"
frame = int(args[1]) if len(args) > 1 else 0
angle = float(args[2]) if len(args) > 2 else 32.0
out = args[3] if len(args) > 3 else "/tmp/opencode/boss.png"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(OUT_DIR, f"BossWarden_{clip}.fbx"))

scene = bpy.context.scene
scene.frame_set(frame)
bpy.context.view_layer.update()

depsgraph = bpy.context.evaluated_depsgraph_get()
points = []
for obj in scene.objects:
    if obj.type == "MESH":
        evaluated = obj.evaluated_get(depsgraph)
        for corner in evaluated.bound_box:
            points.append(evaluated.matrix_world @ Vector(corner))

mn = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
mx = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
center = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, (mn.z + mx.z) * 0.5))
size = mx - mn
print("[render]", clip, frame, "bounds", mn, mx)

rad = math.radians(angle)
distance = max(size.x, size.z) * 1.75
cam_data = bpy.data.cameras.new("Camera")
cam = bpy.data.objects.new("Camera", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = center + Vector((math.sin(rad) * distance,
                                -math.cos(rad) * distance,
                                size.z * 0.12))
cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
cam_data.lens = 62
scene.camera = cam

key = bpy.data.lights.new("Key", "AREA"); key.energy = 1400; key.size = 8
ko = bpy.data.objects.new("Key", key); bpy.context.collection.objects.link(ko)
ko.location = center + Vector((3.0, -4.0, 5.5))
ko.rotation_euler = (center - ko.location).to_track_quat("-Z", "Y").to_euler()

fill = bpy.data.lights.new("Fill", "AREA"); fill.energy = 420; fill.size = 9
fo = bpy.data.objects.new("Fill", fill); bpy.context.collection.objects.link(fo)
fo.location = center + Vector((-5.0, -2.5, 2.0))
fo.rotation_euler = (center - fo.location).to_track_quat("-Z", "Y").to_euler()

rim = bpy.data.lights.new("Rim", "AREA"); rim.energy = 1100; rim.size = 6
ro = bpy.data.objects.new("Rim", rim); bpy.context.collection.objects.link(ro)
ro.location = center + Vector((-1.0, 5.0, 4.0))
ro.rotation_euler = (center - ro.location).to_track_quat("-Z", "Y").to_euler()

bpy.ops.mesh.primitive_plane_add(size=60.0, location=(0.0, 0.0, -0.02))
floor = bpy.context.object
fm = bpy.data.materials.new("Floor"); fm.use_nodes = True
bsdf = fm.node_tree.nodes.get("Principled BSDF")
bsdf.inputs["Base Color"].default_value = (0.045, 0.05, 0.06, 1)
bsdf.inputs["Roughness"].default_value = 0.55
floor.data.materials.append(fm)

scene.world = bpy.data.worlds.new("W"); scene.world.use_nodes = True
bg = scene.world.node_tree.nodes.get("Background")
bg.inputs["Color"].default_value = (0.02, 0.024, 0.032, 1)
bg.inputs["Strength"].default_value = 0.45

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 760
scene.render.resolution_y = 900
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = out
scene.eevee.taa_render_samples = 32
bpy.ops.render.render(write_still=True)
print("[render] wrote", out)
