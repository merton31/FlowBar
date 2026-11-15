"""Create the editable recording orb and render its seamless transparent loop."""
import math
import random
import sys
from pathlib import Path

import bpy


FRAMES = 36
POSES = 12
SIZE = 144
SEED = 34271


def emission(name, color, strength):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1)
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    light = tree.nodes.new("ShaderNodeEmission")
    light.inputs["Color"].default_value = (*color, 1)
    light.inputs["Strength"].default_value = strength
    tree.links.new(light.outputs[0], output.inputs["Surface"])
    return material


def halo(name, size, edge_colors, z):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(-size, -size, z), (size, -size, z), (size, size, z), (-size, size, z)], [], [(0, 1, 2, 3)])
    uv = mesh.uv_layers.new()
    for loop, coordinate in zip(uv.data, [(0, 0), (1, 0), (1, 1), (0, 1)]):
        loop.uv = coordinate
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.surface_render_method = "BLENDED"
    tree = mat.node_tree
    tree.nodes.clear()
    ramp = tree.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "EASE"
    for index, (position, opacity) in enumerate(edge_colors):
        node = ramp.color_ramp.elements[index] if index < 2 else ramp.color_ramp.elements.new(position)
        node.position = position
        node.color = (opacity, opacity, opacity, 1)
    coord = tree.nodes.new("ShaderNodeTexCoord")
    length = tree.nodes.new("ShaderNodeVectorMath")
    length.operation = "DISTANCE"
    length.inputs[1].default_value = (0.5, 0.5, 0)
    tree.links.new(coord.outputs["UV"], length.inputs[0])
    tree.links.new(length.outputs["Value"], ramp.inputs[0])
    light = tree.nodes.new("ShaderNodeEmission")
    light.inputs["Color"].default_value = (0.24, 0.53, 1, 1)
    light.inputs["Strength"].default_value = 1.6
    transparent = tree.nodes.new("ShaderNodeBsdfTransparent")
    mix = tree.nodes.new("ShaderNodeMixShader")
    tree.links.new(ramp.outputs["Color"], mix.inputs[0])
    tree.links.new(transparent.outputs[0], mix.inputs[1])
    tree.links.new(light.outputs[0], mix.inputs[2])
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(mix.outputs[0], output.inputs["Surface"])
    mesh.materials.append(mat)


def dust():
    rng = random.Random(SEED)
    palette = [
        ((0.18, 0.39, 0.80), 0.75),
        ((0.26, 0.53, 1.00), 1.15),
        ((0.38, 0.69, 1.00), 1.65),
        ((0.60, 0.82, 1.00), 2.5),
        ((0.78, 0.93, 1.00), 3.7),
        ((0.96, 0.99, 1.00), 5.3),
    ]
    particles = []
    for count, low, high, geometric, width in [
        (405, .014, .33, 1.26, .0047),
        (225, .24, .73, .90, .0036),
        (630, .65, .98, .58, .0038),
    ]:
        for _ in range(count):
            r = low + (high - low) * rng.random() ** geometric
            angle = rng.uniform(0, math.tau)
            phase = rng.uniform(0, math.tau)
            spread = 1.10 + 4.2 * rng.random() ** 2
            size = width * rng.uniform(.35, 1.7)
            material = rng.choices(range(len(palette)), [8, 22, 25, 24, 15, 6])[0]
            if high < .4 and r < .18:
                material = max(3, material)
            particles.append((r, angle, phase, spread, size, material, rng.uniform(-.13, .13)))

    def vertices(t):
        for r, angle, phase, spread, size, _, tilt in particles:
            theta = angle + (.018 + .037 * (1 - r)) * math.sin(t + phase) + .021 * math.sin(2 * t + angle * 3)
            radius = r + (.019 + .011 * r) * math.sin(2 * t + phase) + .012 * math.sin(t + angle * 2)
            x, y = radius * math.cos(theta), radius * math.sin(theta)
            twinkle = .32 + .68 * (0.5 + .5 * math.sin(3 * t + phase)) ** 2
            sx = size * (.73 + .55 * twinkle)
            sy = sx * spread * (.70 + .80 * twinkle)
            dx, dy = math.cos(theta + tilt), math.sin(theta + tilt)
            # A diamond gives each elongated particle a bright centre and pointed ends.
            yield (x + sy * dx, y + sy * dy, 0)
            yield (x - sx * dy, y + sx * dx, 0)
            yield (x - sy * dx, y - sy * dy, 0)
            yield (x + sx * dy, y - sx * dx, 0)

    mesh = bpy.data.meshes.new("Particle diamonds")
    mesh.from_pydata(list(vertices(0)), [], [tuple(range(i * 4, i * 4 + 4)) for i in range(len(particles))])
    mesh.update()
    for i, (color, power) in enumerate(palette):
        mesh.materials.append(emission("Ice dust %02d" % i, color, power))
    for polygon, particle in zip(mesh.polygons, particles):
        polygon.material_index = particle[-2]
    obj = bpy.data.objects.new("Animated particle sphere", mesh)
    bpy.context.collection.objects.link(obj)
    obj.shape_key_add(name="Base")
    step = FRAMES // POSES
    for i in range(POSES):
        key = obj.shape_key_add(name="Loop pose %02d" % (i + 1))
        for point, coordinate in zip(key.data, vertices(i / POSES * math.tau)):
            point.co = coordinate
        for mark in range(POSES + 1):
            key.value = float(mark % POSES == i)
            key.keyframe_insert("value", frame=1 + mark * step)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)


def rounded_panel(scene, name, bounds, radius, z, color):
    left, bottom, right, top = bounds
    corners = [(right - radius, top - radius), (left + radius, top - radius),
               (left + radius, bottom + radius), (right - radius, bottom + radius)]
    points = []
    for corner, (x, y) in enumerate(corners):
        for step in range(13):
            angle = (corner + step / 12) * math.pi / 2
            points.append((x + radius * math.cos(angle), y + radius * math.sin(angle), z))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(points, [], [tuple(range(len(points)))])
    mesh.materials.append(emission(name, color, 1))
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)


def button_preview(source, destination):
    preview = bpy.data.scenes.new("FlowBar recording button")
    preview.render.engine = "BLENDER_EEVEE"
    preview.render.resolution_x, preview.render.resolution_y = 660, 310
    preview.render.resolution_percentage = 100
    preview.render.fps = source.render.fps
    preview.frame_start, preview.frame_end = source.frame_start, source.frame_end
    preview.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Dark interface")
    world.use_nodes = True
    world.node_tree.nodes.get("Background").inputs["Color"].default_value = (.0031, .0043, .006, 1)
    preview.world = world
    for obj in source.objects:
        if obj.type == "MESH":
            preview.collection.objects.link(obj)
    camera_data = bpy.data.cameras.new("Button preview camera")
    camera_data.type, camera_data.ortho_scale = "ORTHO", 7.30
    camera = bpy.data.objects.new("Button preview camera", camera_data)
    camera.location = (1.71, 0, 5)
    preview.collection.objects.link(camera)
    preview.camera = camera
    rounded_panel(preview, "Fine blue edge", (-1.46, -1.23, 4.91, 1.23), .46, -.36, (.053, .078, .12))
    rounded_panel(preview, "Dark button surface", (-1.42, -1.19, 4.87, 1.19), .42, -.35, (.0048, .007, .012))
    text_data = bpy.data.curves.new("Record label", "FONT")
    text_data.body = "Записать"
    text_data.align_y = "CENTER"
    text_data.size = .55
    font_path = Path("C:/Windows/Fonts/segoeuib.ttf")
    if font_path.exists():
        text_data.font = bpy.data.fonts.load(str(font_path))
        text_data.font.pack()
    text_data.materials.append(emission("Cool white text", (.80, .89, 1), 1))
    text = bpy.data.objects.new("Записать", text_data)
    text.location = (1.25, 0, -.08)
    preview.collection.objects.link(text)
    preview.render.filepath = str(destination / "button-preview.png")
    return preview


def main():
    destination = Path(sys.argv[sys.argv.index("--") + 1])
    renders = destination / "frames"
    renders.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.name = "FlowBar recording orb"
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Standard"
    scene.render.fps = 24
    scene.frame_start, scene.frame_end = 1, FRAMES
    scene.render.filepath = str(renders / "orb-")

    camera_data = bpy.data.cameras.new("Orthographic button camera")
    camera = bpy.data.objects.new("Camera", camera_data)
    bpy.context.collection.objects.link(camera)
    camera.location = (0, 0, 5)
    camera_data.type, camera_data.ortho_scale = "ORTHO", 2.65
    scene.camera = camera
    dust()
    halo("Soft icy halo", 1.325, [(0, .16), (.18, .043), (.32, .025), (.49, 0)], -.16)
    halo("Bright blue core", .43, [(0, .89), (.095, .64), (.24, .14), (.49, 0)], -.11)
    preview = button_preview(scene, destination)
    bpy.context.window.scene = preview
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type != "VIEW_3D":
                continue
            space = area.spaces.active
            space.region_3d.view_perspective = "CAMERA"
            space.shading.type = "RENDERED"
            space.overlay.show_overlays = False
    scene.frame_set(1)
    preview.frame_set(1)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(destination / "FlowBar Voice Orb.blend"))
    bpy.ops.render.render(write_still=True, scene=preview.name)
    bpy.context.window.scene = scene
    bpy.ops.render.render(animation=True, scene=scene.name)


if __name__ == "__main__":
    main()
