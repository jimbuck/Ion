"""Writes Assets/puppet.gltf: the skinned character of the Ion puppet sample.

A blocky figure of tubes and boxes (12 joints, T pose at bind time, facing +Z), each part weighted to its joints with
smooth blends at the waist, shoulders, elbows, hips and knees, and three looping clips: Idle, Walk and Wave. Run it
from this folder with any Python 3 (no packages): python make_puppet.py
"""

import base64
import json
import math
import os
import struct

# Joints: name, parent, model-space position at bind time.
JOINTS = [
    ("Hips", -1, (0.0, 1.0, 0.0)),
    ("Spine", 0, (0.0, 1.2, 0.0)),
    ("Chest", 1, (0.0, 1.42, 0.0)),
    ("Head", 2, (0.0, 1.66, 0.0)),
    ("UpperArm.L", 2, (0.2, 1.58, 0.0)),
    ("LowerArm.L", 4, (0.5, 1.58, 0.0)),
    ("UpperArm.R", 2, (-0.2, 1.58, 0.0)),
    ("LowerArm.R", 6, (-0.5, 1.58, 0.0)),
    ("UpperLeg.L", 0, (0.11, 0.94, 0.0)),
    ("LowerLeg.L", 8, (0.11, 0.5, 0.0)),
    ("UpperLeg.R", 0, (-0.11, 0.94, 0.0)),
    ("LowerLeg.R", 10, (-0.11, 0.5, 0.0)),
]
J = {name: i for i, (name, _, _) in enumerate(JOINTS)}

SHIRT = (0.10, 0.30, 0.75)
SKIN = (0.85, 0.55, 0.40)
TROUSERS = (0.12, 0.12, 0.16)
SHOES = (0.30, 0.16, 0.08)


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def blend(first, second, a, b):
    """Weights going from `first` to `second` between a and b along a part."""
    return lambda c: [(first, 1 - smooth(a, b, c)), (second, smooth(a, b, c))]


class Mesh:
    def __init__(self):
        self.positions, self.normals, self.colors, self.joints, self.weights, self.indices = [], [], [], [], [], []

    def vertex(self, p, n, color, influences):
        influences = [(j, w) for j, w in influences if w > 1e-4]
        influences.sort(key=lambda jw: -jw[1])
        influences = (influences + [(0, 0.0)] * 4)[:4]
        total = sum(w for _, w in influences)
        self.positions.append(p)
        self.normals.append(n)
        self.colors.append(color)
        self.joints.append(tuple(j for j, _ in influences))
        self.weights.append(tuple(w / total for _, w in influences))
        return len(self.positions) - 1

    def quad(self, a, b, c, d):
        # Counterclockwise from outside: a, b, c, d.
        self.indices += [a, b, c, a, c, d]

    def tube(self, axis, start, end, center, half, color, weights, rings=8):
        """A box along `axis` (0: x, 1: y) from start to end, cross-section half sizes `half`, split into rings."""
        coords = [start + (end - start) * i / rings for i in range(rings + 1)]
        other = [a for a in range(3) if a != axis]

        def point(c, s0, s1):
            p = list(center)
            p[axis] = c
            p[other[0]] += s0 * half[0]
            p[other[1]] += s1 * half[1]
            return tuple(p)

        # The four sides, each with its own (flat) normal.
        sides = [((1, -1), (1, 1), (1, 0)), ((1, 1), (-1, 1), (0, 1)), ((-1, 1), (-1, -1), (-1, 0)), ((-1, -1), (1, -1), (0, -1))]
        for (a0, a1), (b0, b1), (n0, n1) in sides:
            n = [0.0, 0.0, 0.0]
            n[other[0]] = float(n0)
            n[other[1]] = float(n1)
            rows = []
            for c in coords:
                rows.append((self.vertex(point(c, a0, a1), tuple(n), color, weights(c)), self.vertex(point(c, b0, b1), tuple(n), color, weights(c))))
            for i in range(rings):
                p, q = rows[i], rows[i + 1]
                if self.outward(p[0], p[1], q[1], tuple(n)):
                    self.quad(p[0], p[1], q[1], q[0])
                else:
                    self.quad(p[0], q[0], q[1], p[1])
        # The caps.
        for c, sign in ((start, -1 if end > start else 1), (end, 1 if end > start else -1)):
            n = [0.0, 0.0, 0.0]
            n[axis] = float(sign)
            corners = [self.vertex(point(c, s0, s1), tuple(n), color, weights(c)) for s0, s1 in ((1, -1), (1, 1), (-1, 1), (-1, -1))]
            if self.outward(corners[0], corners[1], corners[2], tuple(n)):
                self.quad(*corners)
            else:
                self.quad(corners[3], corners[2], corners[1], corners[0])

    def box(self, lo, hi, color, weights):
        """An axis-aligned box rigidly weighted (weights: a list of (joint, weight))."""
        self.tube(1, lo[1], hi[1], ((lo[0] + hi[0]) / 2, 0, (lo[2] + hi[2]) / 2), ((hi[0] - lo[0]) / 2, (hi[2] - lo[2]) / 2), color, lambda c: weights, rings=1)

    def outward(self, a, b, c, n):
        pa, pb, pc = self.positions[a], self.positions[b], self.positions[c]
        u = [pb[i] - pa[i] for i in range(3)]
        v = [pc[i] - pa[i] for i in range(3)]
        cross = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        return sum(cross[i] * n[i] for i in range(3)) > 0


def build_mesh():
    m = Mesh()
    # Torso: hips into spine at the waist, spine into chest below the shoulders.
    torso = lambda c: [(J["Hips"], 1 - smooth(1.08, 1.22, c)), (J["Spine"], smooth(1.08, 1.22, c) * (1 - smooth(1.34, 1.46, c))), (J["Chest"], smooth(1.34, 1.46, c))]
    m.tube(1, 0.9, 1.62, (0, 0, 0), (0.17, 0.1), SHIRT, torso, rings=9)
    # Neck and head.
    m.tube(1, 1.6, 1.7, (0, 0, 0), (0.05, 0.05), SKIN, blend(J["Chest"], J["Head"], 1.62, 1.68), rings=2)
    m.box((-0.12, 1.7, -0.12), (0.12, 1.96, 0.12), SKIN, [(J["Head"], 1.0)])
    for side, sign in (("L", 1), ("R", -1)):
        # Arms: a sleeve from the shoulder to the elbow, skin below; blends at the shoulder and the elbow.
        arm = lambda c, side=side, sign=sign: (
            [(J["Chest"], 1 - smooth(0.17, 0.26, c * sign)), (J["UpperArm." + side], smooth(0.17, 0.26, c * sign) * (1 - smooth(0.46, 0.54, c * sign))), (J["LowerArm." + side], smooth(0.46, 0.54, c * sign))])
        m.tube(0, 0.16 * sign, 0.5 * sign, (0, 1.58, 0), (0.06, 0.06), SHIRT, arm, rings=6)
        m.tube(0, 0.5 * sign, 0.8 * sign, (0, 1.58, 0), (0.05, 0.05), SKIN, arm, rings=4)
        # Legs: trousers from the hip to the ankle; blends at the hip and the knee; a shoe on the lower leg.
        leg = lambda c, side=side: (
            [(J["Hips"], 1 - smooth(0.82, 0.92, c)), (J["UpperLeg." + side], smooth(0.82, 0.92, c) * smooth(0.46, 0.54, c)), (J["LowerLeg." + side], 1 - smooth(0.46, 0.54, c))])
        m.tube(1, 0.96, 0.08, (0.11 * sign, 0, 0), (0.075, 0.075), TROUSERS, leg, rings=10)
        m.box((0.11 * sign - 0.07, 0.0, -0.08), (0.11 * sign + 0.07, 0.08, 0.16), SHOES, [(J["LowerLeg." + side], 1.0)])
    return m


# Quaternions (x, y, z, w).
def axis_angle(axis, degrees):
    h = math.radians(degrees) / 2
    s = math.sin(h)
    return (axis[0] * s, axis[1] * s, axis[2] * s, math.cos(h))


def mul(a, b):
    """a * b: b applied first."""
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def clip(duration, fps, channels):
    """channels: joint -> {"rotation": f(t) -> quat, "translation": f(t) -> vec3}"""
    keys = [duration * i / round(duration * fps) for i in range(round(duration * fps) + 1)]
    out = []
    for joint, paths in channels.items():
        for path, f in paths.items():
            out.append((J[joint], path, keys, [f(t) for t in keys]))
    return out


def arms_down(side, angle):
    # The left arm (along +X) comes down turning about -Z, the right one (along -X) about +Z.
    return axis_angle(Z, -angle if side == "L" else angle)


def idle():
    w = lambda t: 2 * math.pi * t / 2.0
    return clip(2.0, 15, {
        "Chest": {"rotation": lambda t: axis_angle(X, 2.5 * math.sin(w(t)))},
        "Head": {"rotation": lambda t: axis_angle(Y, 8 * math.sin(w(t) * 0.5 + 0.3))},
        "UpperArm.L": {"rotation": lambda t: arms_down("L", 72 + 3 * math.sin(w(t)))},
        "UpperArm.R": {"rotation": lambda t: arms_down("R", 72 + 3 * math.sin(w(t)))},
        "LowerArm.L": {"rotation": lambda t: axis_angle(Y, -10)},
        "LowerArm.R": {"rotation": lambda t: axis_angle(Y, 10)},
    })


def walk():
    w = lambda t: 2 * math.pi * t / 1.0
    # Legs: forward is a negative turn about X; the knees bend back (positive) as the leg swings back.
    leg = lambda phase: lambda t: axis_angle(X, -28 * math.sin(w(t) + phase))
    knee = lambda phase: lambda t: axis_angle(X, 8 + 40 * max(0.0, math.sin(w(t) + phase + math.pi * 0.6)))
    # Arms: down, swinging against the legs (about Y, after coming down).
    arm = lambda side, phase: lambda t: mul(axis_angle(Y, (-1 if side == "L" else 1) * 25 * math.sin(w(t) + phase)), arms_down(side, 75))
    return clip(1.0, 20, {
        "Hips": {"translation": lambda t: (0.0, 1.0 + 0.03 * math.cos(2 * w(t)), 0.0), "rotation": lambda t: axis_angle(Y, 6 * math.sin(w(t)))},
        "Chest": {"rotation": lambda t: axis_angle(Y, -9 * math.sin(w(t)))},
        "UpperLeg.L": {"rotation": leg(0.0)},
        "UpperLeg.R": {"rotation": leg(math.pi)},
        "LowerLeg.L": {"rotation": knee(0.0)},
        "LowerLeg.R": {"rotation": knee(math.pi)},
        "UpperArm.L": {"rotation": arm("L", math.pi)},
        "UpperArm.R": {"rotation": arm("R", 0.0)},
        "LowerArm.L": {"rotation": lambda t: axis_angle(Y, -20)},
        "LowerArm.R": {"rotation": lambda t: axis_angle(Y, 20)},
    })


def wave():
    w = lambda t: 2 * math.pi * t / 1.5
    return clip(1.5, 20, {
        "Chest": {"rotation": lambda t: axis_angle(Z, 3 * math.sin(w(t)))},
        "Head": {"rotation": lambda t: axis_angle(Z, -6 + 4 * math.sin(w(t)))},
        "UpperArm.L": {"rotation": lambda t: arms_down("L", 74)},
        # The right arm up and out, the forearm up and waving.
        "UpperArm.R": {"rotation": lambda t: arms_down("R", -35)},
        "LowerArm.R": {"rotation": lambda t: arms_down("R", -(70 + 25 * math.sin(2 * w(t))))},
    })


def main():
    mesh = build_mesh()
    blob = bytearray()
    views, accessors = [], []

    def add(data, fmt, count, kind, component, target=None, minmax=None):
        while len(blob) % 4:
            blob.append(0)
        offset = len(blob)
        for item in data:
            blob.extend(struct.pack(fmt, *item) if isinstance(item, tuple) else struct.pack(fmt, item))
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(blob) - offset}
        if target:
            view["target"] = target
        views.append(view)
        accessor = {"bufferView": len(views) - 1, "componentType": component, "count": count, "type": kind}
        if minmax:
            accessor["min"], accessor["max"] = minmax
        accessors.append(accessor)
        return len(accessors) - 1

    lo = [min(p[i] for p in mesh.positions) for i in range(3)]
    hi = [max(p[i] for p in mesh.positions) for i in range(3)]
    n = len(mesh.positions)
    position = add(mesh.positions, "<3f", n, "VEC3", 5126, 34962, (lo, hi))
    normal = add(mesh.normals, "<3f", n, "VEC3", 5126, 34962)
    color = add([c + (1.0,) for c in mesh.colors], "<4f", n, "VEC4", 5126, 34962)
    joints = add(mesh.joints, "<4H", n, "VEC4", 5123, 34962)
    weights = add(mesh.weights, "<4f", n, "VEC4", 5126, 34962)
    indices = add(mesh.indices, "<H", len(mesh.indices), "SCALAR", 5123, 34963)

    # Inverse bind matrices: the joints are only translated at bind time (column-major).
    ibm = [(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -p[0], -p[1], -p[2], 1) for _, _, p in JOINTS]
    inverse_bind = add(ibm, "<16f", len(ibm), "MAT4", 5126)

    # Nodes: 0 the mesh, 1 the armature, 2.. the joints (translations relative to their parents).
    nodes = [{"name": "Puppet", "mesh": 0, "skin": 0}, {"name": "Armature", "children": [2]}]
    for i, (name, parent, p) in enumerate(JOINTS):
        origin = JOINTS[parent][2] if parent >= 0 else (0, 0, 0)
        node = {"name": name, "translation": [round(p[k] - origin[k], 6) for k in range(3)]}
        children = [2 + c for c, (_, cp, _) in enumerate(JOINTS) if cp == i]
        if children:
            node["children"] = children
        nodes.append(node)

    animations = []
    for name, channels in (("Idle", idle()), ("Walk", walk()), ("Wave", wave())):
        samplers, targets = [], []
        for joint, path, keys, values in channels:
            time = add(keys, "<f", len(keys), "SCALAR", 5126, minmax=([keys[0]], [keys[-1]]))
            value = add(values, "<4f" if path == "rotation" else "<3f", len(values), "VEC4" if path == "rotation" else "VEC3", 5126)
            samplers.append({"input": time, "output": value, "interpolation": "LINEAR"})
            targets.append({"sampler": len(samplers) - 1, "target": {"node": 2 + joint, "path": path}})
        animations.append({"name": name, "samplers": samplers, "channels": targets})

    gltf = {
        "asset": {"version": "2.0", "generator": "Ion.Examples.Puppet/tools/make_puppet.py"},
        "scene": 0,
        "scenes": [{"nodes": [0, 1]}],
        "nodes": nodes,
        "meshes": [{"name": "Puppet", "primitives": [{"attributes": {"POSITION": position, "NORMAL": normal, "COLOR_0": color, "JOINTS_0": joints, "WEIGHTS_0": weights}, "indices": indices, "material": 0}]}],
        "materials": [{"name": "Puppet", "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "metallicFactor": 0.0, "roughnessFactor": 0.7}}],
        "skins": [{"name": "Puppet", "joints": [2 + i for i in range(len(JOINTS))], "inverseBindMatrices": inverse_bind, "skeleton": 2}],
        "animations": animations,
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(blob), "uri": "data:application/octet-stream;base64," + base64.b64encode(bytes(blob)).decode("ascii")}],
    }
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "puppet.gltf")
    with open(out, "w", newline="\n") as f:
        json.dump(gltf, f, separators=(",", ":"))
        f.write("\n")
    print(f"{out}: {n} vertices, {len(mesh.indices) // 3} triangles, {len(JOINTS)} joints, {len(animations)} clips, {len(blob)} bytes of buffer")


if __name__ == "__main__":
    main()
