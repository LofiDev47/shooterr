"""Render transparent, neutral-white HUD sprites from editable Blender meshes."""
from math import cos, pi, sin
from pathlib import Path

import bpy

SOURCE = Path(__file__).resolve().parent
OUTPUT = SOURCE.parent / "Assets" / "Models" / "Foundry" / "HUD"
OUTPUT.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for collection in list(bpy.data.collections):
    bpy.data.collections.remove(collection)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.film_transparent = True
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"

bpy.ops.object.camera_add(location=(0, 0, 5))
camera = bpy.context.object
camera.data.type = "ORTHO"
camera.data.ortho_scale = 2
scene.camera = camera

ink = bpy.data.materials.new("Tintable white")
ink.use_nodes = True
nodes = ink.node_tree.nodes
emitter = nodes.new("ShaderNodeEmission")
ink.node_tree.links.new(emitter.outputs[0], nodes.get("Material Output").inputs[0])


def shape(label, pieces):
    collection = bpy.data.collections.new(label)
    scene.collection.children.link(collection)
    for index, polygon in enumerate(pieces):
        data = bpy.data.meshes.new(f"{label} piece {index + 1}")
        data.from_pydata([(x, y, 0) for x, y in polygon], [], [tuple(range(len(polygon)))])
        obj = bpy.data.objects.new(data.name, data)
        collection.objects.link(obj)
        data.materials.append(ink)
    return collection


def rotated(points, amount):
    for i in range(amount):
        angle = i * 2*pi / amount
        yield [(x*cos(angle) - y*sin(angle), x*sin(angle) + y*cos(angle)) for x, y in points]


reticle = shape("Reticle", [[(0, -0.80), (0.80, 0), (0, 0.80), (-0.80, 0)]])
bar_shape = [(-0.94, -0.58), (0.26, -0.79), (0.92, 0),
             (0.26, 0.79), (-0.94, 0.58)]
bar_h = shape("ReticleBarHorizontal", [bar_shape])
bar_v = shape("ReticleBarVertical", [[(-y, x) for x, y in bar_shape]])
hit = shape("HitMarker", list(rotated(
    [(0.23, 0.29), (0.60, 0.78), (0.78, 0.78), (0.31, 0.23)], 4)))
panel = shape("Plate", [[
    (-0.99, -0.63), (-0.78, -0.96), (0.80, -0.96), (0.99, -0.62),
    (0.99, 0.63), (0.78, 0.96), (-0.80, 0.96), (-0.99, 0.62),
]])
pip = shape("PhasePip", [[(0, -0.98), (0.91, -0.40), (0.91, 0.40),
                          (0, 0.98), (-0.91, 0.40), (-0.91, -0.40)]])

atlases = (reticle, bar_h, bar_v, hit, panel, pip)
for collection in atlases:
    for other in atlases:
        other.hide_render = other is not collection
        other.hide_viewport = other is not collection
    scene.render.resolution_x = scene.render.resolution_y = 128
    scene.render.filepath = str(OUTPUT / (collection.name + ".png"))
    bpy.ops.render.render(write_still=True)

reticle.hide_render = reticle.hide_viewport = False
pip.hide_render = pip.hide_viewport = True
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "HUD.blend"))
