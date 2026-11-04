"""Render a quick preview of the exported rifle for inspection."""

import math
import os

import bpy
from mathutils import Vector

ROOT = "/home/lofi/shooterr"
FBX = os.path.join(ROOT, "Assets", "Models", "ShooterRifle.fbx")
OUT = "/tmp/opencode/rifle.png"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

# Bounds of all mesh objects.
points = []
for obj in bpy.context.scene.objects:
    if obj.type == "MESH":
        for corner in obj.bound_box:
            points.append(obj.matrix_world @ Vector(corner))

minimum = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
maximum = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
center = (minimum + maximum) * 0.5
print("[render] bounds", minimum, maximum)

# Camera: front three-quarter from the muzzle side (-Y).
camera_data = bpy.data.cameras.new("Camera")
camera = bpy.data.objects.new("Camera", camera_data)
bpy.context.collection.objects.link(camera)
camera.location = center + Vector((1.15, -1.65, 0.75))
direction = center - camera.location
camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
camera_data.lens = 70
bpy.context.scene.camera = camera

# Lights.
key_data = bpy.data.lights.new("Key", "AREA")
key_data.energy = 500
key_data.size = 4.0
key = bpy.data.objects.new("Key", key_data)
bpy.context.collection.objects.link(key)
key.location = center + Vector((2.0, -2.0, 3.0))
key.rotation_euler = (center - key.location).to_track_quat("-Z", "Y").to_euler()

fill_data = bpy.data.lights.new("Fill", "AREA")
fill_data.energy = 180
fill_data.size = 5.0
fill = bpy.data.objects.new("Fill", fill_data)
bpy.context.collection.objects.link(fill)
fill.location = center + Vector((-2.5, 1.5, 2.0))
fill.rotation_euler = (center - fill.location).to_track_quat("-Z", "Y").to_euler()

# Dark backdrop plane.
bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0.0, 0.0, minimum.z - 0.02))
floor = bpy.context.object
floor_material = bpy.data.materials.new("Floor")
floor_material.use_nodes = True
floor_bsdf = floor_material.node_tree.nodes.get("Principled BSDF")
floor_bsdf.inputs["Base Color"].default_value = (0.035, 0.04, 0.05, 1.0)
floor_bsdf.inputs["Roughness"].default_value = 0.5
floor.data.materials.append(floor_material)

scene = bpy.context.scene
scene.world = bpy.data.worlds.new("World")
scene.world.use_nodes = True
background = scene.world.node_tree.nodes.get("Background")
background.inputs["Color"].default_value = (0.02, 0.025, 0.035, 1.0)
background.inputs["Strength"].default_value = 0.35

scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 960
scene.render.resolution_y = 640
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = OUT
scene.eevee.taa_render_samples = 32

bpy.ops.render.render(write_still=True)
print("[render] wrote", OUT)
