"""Render the four Flowbar carousel effects with Blender's background Python."""

import argparse
import math
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector

TAU = math.tau

PALETTES = {
    "wind": ((0.025, 0.31, 0.18), (0.35, 0.95, 0.74), (0.01, 0.35, 0.22)),
    "petals": ((0.44, 0.025, 0.15), (1.0, 0.54, 0.72), (0.77, 0.045, 0.33)),
    "fire": ((0.46, 0.03, 0.21), (1.0, 0.55, 0.24), (1.0, 0.08, 0.018)),
    "sun": ((0.52, 0.22, 0.02), (1.0, 0.72, 0.20), (1.0, 0.55, 0.045)),
}


def emission(name, color, power=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    light = nodes.new("ShaderNodeEmission")
    light.inputs["Color"].default_value = (*color, 1.0)
    light.inputs["Strength"].default_value = power
    out = nodes.new("ShaderNodeOutputMaterial")
    mat.node_tree.links.new(light.outputs[0], out.inputs["Surface"])
    return mat


def glow_material(name, color, edge, strength):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    facing = nodes.new("ShaderNodeLayerWeight")
    facing.inputs["Blend"].default_value = 0.36
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*color, 1)
    ramp.color_ramp.elements[1].color = (*edge, 1)
    ramp.color_ramp.elements[1].position = 0.78
    links.new(facing.outputs["Facing"], ramp.inputs[0])
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    links.new(ramp.outputs[0], shader.inputs["Base Color"])
    links.new(ramp.outputs[0], shader.inputs["Emission Color"])
    shader.inputs["Emission Strength"].default_value = strength
    shader.inputs["Roughness"].default_value = 0.27
    shader.inputs["Metallic"].default_value = 0.27
    out = nodes.new("ShaderNodeOutputMaterial")
    links.new(shader.outputs[0], out.inputs["Surface"])
    return mat


def flame_material(phase):
    mat = bpy.data.materials.new("Translucent turbulent flame")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    coord = nodes.new("ShaderNodeTexCoord")
    scale = nodes.new("ShaderNodeVectorMath")
    scale.operation = "MULTIPLY"
    scale.inputs[1].default_value = (1.3, 3.0, 0.8)
    links.new(coord.outputs["Generated"], scale.inputs[0])
    noise = nodes.new("ShaderNodeTexNoise")
    noise.noise_dimensions = "4D"
    noise.inputs["Scale"].default_value = 2.1
    noise.inputs["Detail"].default_value = 4.0
    noise.inputs["Roughness"].default_value = 0.68
    noise.inputs["W"].default_value = 0.4 * math.sin(phase)
    links.new(scale.outputs["Vector"], noise.inputs["Vector"])
    heat = nodes.new("ShaderNodeValToRGB")
    gradient = heat.color_ramp
    gradient.elements[0].position = 0.26
    gradient.elements[0].color = (0.16, 0.001, 0.002, 1)
    gradient.elements[1].position = 0.77
    gradient.elements[1].color = (1, 0.91, 0.59, 1)
    gradient.elements.new(0.44).color = (1, 0.017, 0.003, 1)
    gradient.elements.new(0.62).color = (1, 0.23, 0.025, 1)
    links.new(noise.outputs["Fac"], heat.inputs[0])
    mask = nodes.new("ShaderNodeMapRange")
    mask.clamp = True
    mask.inputs["From Min"].default_value = 0.36
    mask.inputs["From Max"].default_value = 0.72
    links.new(noise.outputs["Fac"], mask.inputs["Value"])
    emission_node = nodes.new("ShaderNodeEmission")
    emission_node.inputs["Strength"].default_value = 2.7
    links.new(heat.outputs["Color"], emission_node.inputs["Color"])
    mix = nodes.new("ShaderNodeMixShader")
    links.new(mask.outputs[0], mix.inputs[0])
    links.new(nodes.new("ShaderNodeBsdfTransparent").outputs[0], mix.inputs[1])
    links.new(emission_node.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], nodes.new("ShaderNodeOutputMaterial").inputs["Surface"])
    return mat


def glass_material(kind, base, high):
    mat = glow_material("Translucent illuminated capsule", base, high, 0.55)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
    ramp = next(node for node in nodes if node.type == "VALTORGB")
    texture = nodes.new("ShaderNodeTexCoord")
    separate = nodes.new("ShaderNodeSeparateXYZ")
    links.new(texture.outputs["Generated"], separate.inputs[0])
    internal = nodes.new("ShaderNodeValToRGB")
    internal.color_ramp.elements[0].color = (*base, 1)
    internal.color_ramp.elements[1].color = (*high, 1)
    internal.color_ramp.elements.new(0.48).color = (*base, 1)
    links.new(separate.outputs["Y"], internal.inputs[0])
    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "SCREEN"
    mix.inputs[0].default_value = 0.55
    links.new(internal.outputs[0], mix.inputs[1])
    links.new(ramp.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], shader.inputs["Base Color"])
    links.new(mix.outputs[0], shader.inputs["Emission Color"])
    shader.inputs["Coat Weight"].default_value = 0.62
    shader.inputs["Coat Roughness"].default_value = 0.16
    return mat


def mesh(name, vertices, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    for face in data.polygons:
        face.use_smooth = True
    return obj


def capsule_xy(theta, width=0.68, half_height=1.16):
    s, c = math.sin(theta), math.cos(theta)
    return width * c, width * s + (half_height - width) * (1 if s >= 0 else -1)


def capsule(material):
    # The rings create a domed front and an accurately rounded pill silhouette.
    rings = [(1.0, -0.19), (1.0, 0.02), (0.986, 0.09), (0.94, 0.16),
             (0.86, 0.22), (0.73, 0.27), (0.54, 0.30), (0.30, 0.32), (0.03, 0.33)]
    vertices, faces = [], []
    steps = 132
    for scale, z in rings:
        for i in range(steps):
            x, y = capsule_xy(TAU * i / steps)
            vertices.append((x * scale, y * scale, z))
    for r in range(len(rings) - 1):
        for i in range(steps):
            j = (i + 1) % steps
            faces.append((r * steps + i, r * steps + j, (r + 1) * steps + j, (r + 1) * steps + i))
    faces.append(tuple(range((len(rings) - 1) * steps, len(rings) * steps)))
    mesh("Domed glass capsule", vertices, faces, material)


def line(name, points, radius, mat, cyclic=False):
    data = bpy.data.curves.new(name, "CURVE")
    data.dimensions = "3D"
    data.bevel_depth = radius
    data.bevel_resolution = 2
    spline = data.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for index, (point, xyz) in enumerate(zip(spline.points, points)):
        point.co = (*xyz, 1)
        if not cyclic:
            point.radius = 0.16 + 0.84 * math.sin(math.pi * index / max(1, len(points) - 1)) ** 0.65
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def blade(name, start, end, width, bend, mat, edge_mat, phase=0):
    axis = Vector(end) - Vector(start)
    lateral = Vector((-axis.y, axis.x, 0)).normalized()
    vertices, faces = [], []
    count, across = 30, 9
    for i in range(count):
        t = i / (count - 1)
        section = Vector(start).lerp(Vector(end), t)
        section += lateral * (bend * math.sin(math.pi * t) + 0.045 * math.sin(phase + 7 * t) * t)
        w = width * (math.sin(math.pi * t) ** 0.82) + 0.006
        for j in range(across):
            q = j * 2 / (across - 1) - 1
            p = section + lateral * (q * w)
            p.z += 0.16 * math.sin(math.pi * t) * (1 - q * q) + 0.11 * q * t
            vertices.append(tuple(p))
    for i in range(count - 1):
        for j in range(across - 1):
            k = i * across + j
            faces.append((k, k + 1, k + across + 1, k + across))
    mesh(name, vertices, faces, mat)
    if edge_mat:
        for index in (0, across - 1):
            line(name + " rim", vertices[index::across], 0.016, edge_mat)


def specks(kind, rng, phase, materials, count):
    for i in range(count):
        a = rng.uniform(0, TAU)
        r = rng.uniform(0.78, 1.6)
        x, y = math.cos(a) * r, math.sin(a) * r
        if kind == "fire":
            x = rng.uniform(-0.85, 0.85)
            y = rng.uniform(0.9, 2.0)
        else:
            y *= 1.13
        x += 0.065 * math.sin(phase + i)
        y += 0.1 * math.cos(phase + i * 0.71)
        radius = rng.uniform(0.007, 0.025)
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=radius, location=(x, y, -0.04))
        bpy.context.object.data.materials.append(materials[i % len(materials)])


def effects(kind, base, high, accent, phase):
    rng = random.Random(893 + tuple(PALETTES).index(kind))
    pale = emission("White-hot edge", high, 2.1)
    glow = emission("Colored threads", accent, 1.45)
    faint = emission("Dim colored threads", accent, 0.43)
    curved = glow_material("Curved elemental material", accent, high, 0.67)
    if kind == "petals":
        for i, (x, y, width) in enumerate([
            (-1.38, -0.35, 0.31), (1.38, -0.35, 0.31), (-1.33, 0.45, 0.29),
            (1.33, 0.45, 0.29), (-0.94, 1.02, 0.28), (0.94, 1.02, 0.28), (0.10, 1.43, 0.31),
        ]):
            blade("Outer pink petal", (0.0, -0.98, -0.06), (x, y + 0.08 * math.sin(phase + i), -0.16),
                  width, 0.13 * (1 if x > 0 else -1), curved, pale, phase)
        for side in (-1, 1):
            blade("Lower pink petal", (0, -1.04, 0.20), (side * 1.39, -0.66, 0.32),
                  0.20, side * 0.14, curved, pale, phase)
    elif kind == "fire":
        flame = flame_material(phase)
        for i in range(19):
            x = rng.uniform(-0.65, 0.65)
            y = rng.uniform(0.91, 1.91)
            sway = math.sin(phase * 2 + i * 1.73)
            blade("Hot flame tongue", (x * 0.82, 0.54 + 0.12 * math.cos(i), -0.12),
                  (x * 1.3 + 0.11 * sway, y + 0.10 * sway, 0.04),
                  rng.uniform(0.09, 0.21), 0.10 * sway, flame, None, phase + i)
        for side in (-1, 1):
            points = [(side * (0.64 + 0.10 * math.sin(t * 7 + phase)), -0.9 + 2.05 * t, 0.02)
                      for t in (i / 55 for i in range(56))]
            line("Fire licking the rim", points, 0.024, glow)
    elif kind == "wind":
        for i in range(30):
            start = rng.uniform(0, TAU) + 0.19 * math.sin(phase + i * 0.17)
            span = rng.uniform(0.65, 3.2)
            spread = 0.08 + rng.random() * 0.67
            points = []
            for j in range(77):
                t = j / 76
                angle = start + t * span
                x, y = capsule_xy(angle, 0.76 + spread * (0.28 + t), 1.25 + spread * 0.45)
                # Smooth the capsule's straight sections into the stream lines.
                y = (1.21 + spread * 0.45) * math.sin(angle) + 0.12 * math.sin(angle * 2 + phase)
                x += 0.12 * math.sin(angle * 3 + phase + i * 0.2) * t
                points.append((x, y, -0.17 + 0.15 * math.cos(angle + i)))
            line("Air stream", points, rng.uniform(0.008, 0.031), pale if i % 7 == 0 else glow if i % 3 else faint)
    else:
        for i in range(76):
            angle = TAU * i / 76 + 0.027 * math.sin(phase)
            c, s = math.cos(angle), math.sin(angle)
            x, y = capsule_xy(angle)
            length = rng.uniform(0.23, 1.12) * (0.89 + 0.11 * math.sin(phase + i * 0.77))
            line("Sunlight ray", [(x + c * length * t, y + s * length * t, -0.08) for t in (0.0, 0.36, 0.75, 1.0)],
                 rng.uniform(0.006, 0.02), pale if i % 9 == 0 else glow if i % 3 else faint)
    count = 48 if kind == "fire" else 27
    specks(kind, rng, phase, (pale, glow, faint), count)
    points = [(*capsule_xy(TAU * i / 198), 0.095) for i in range(198)]
    line("Illuminated glass rim", points, 0.026 if kind == "sun" else 0.015, pale if kind != "wind" else glow, True)


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 288
    scene.render.resolution_y = 288
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("Studio")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.16
    scene.world = world
    camera = bpy.data.cameras.new("Front")
    camera.type = "ORTHO"
    camera.ortho_scale = 4.65
    cam = bpy.data.objects.new("Front", camera)
    scene.collection.objects.link(cam)
    cam.location = (0, 0.24, 7)
    scene.camera = cam
    for name, position, watts, color, size in [
        ("Wide softbox", (-3, 3.1, 4.6), 370, (0.8, 0.86, 1), 4.0),
        ("Warm edge", (2.6, 2.3, 3), 160, (1, 0.68, 0.38), 2.3),
    ]:
        light = bpy.data.lights.new(name, "AREA")
        light.energy, light.color, light.shape, light.size = watts, color, "DISK", size
        obj = bpy.data.objects.new(name, light)
        scene.collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (Vector((0, 0, 0)) - obj.location).to_track_quat("-Z", "Y").to_euler()
    return scene


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--frames", type=int, default=8)
    parser.add_argument("--only", choices=PALETTES)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    args.output.mkdir(parents=True, exist_ok=True)
    for kind, (base, high, accent) in PALETTES.items():
        if args.only and args.only != kind:
            continue
        for index in range(args.frames):
            scene = setup()
            capsule(glass_material(kind, base, high))
            effects(kind, base, high, accent, TAU * index / args.frames)
            scene.render.filepath = str(args.output / f"{kind}-{index:02}.png")
            bpy.ops.render.render(write_still=True)


main()
