"""Generate sample DXF parts (inches) for the README Auto Nest screenshot.

Run: python test-data/readme-parts/generate.py
Writes one DXF per part next to this script. Outlines and holes are written
as plain LINE/ARC/CIRCLE entities so every importer reads them the same way.
"""

import math
from pathlib import Path

import ezdxf

OUT = Path(__file__).resolve().parent


def new_doc():
    doc = ezdxf.new("R2010")
    doc.header["$INSUNITS"] = 1  # inches
    doc.header["$MEASUREMENT"] = 0
    return doc


def add_loop(msp, points):
    """Closed loop from (x, y, bulge) vertices, exploded to LINE/ARC."""
    poly = msp.add_lwpolyline(points, format="xyb", close=True)
    for e in list(poly.virtual_entities()):
        if e.dxftype() == "LINE":
            msp.add_line(e.dxf.start, e.dxf.end)
        else:
            msp.add_arc(e.dxf.center, e.dxf.radius, e.dxf.start_angle, e.dxf.end_angle)
    msp.delete_entity(poly)


def bulge(angle_deg):
    return math.tan(math.radians(angle_deg) / 4)


def rounded_rect(x0, y0, w, h, r):
    b = bulge(90)
    return [
        (x0 + r, y0, 0), (x0 + w - r, y0, b),
        (x0 + w, y0 + r, 0), (x0 + w, y0 + h - r, b),
        (x0 + w - r, y0 + h, 0), (x0 + r, y0 + h, b),
        (x0, y0 + h - r, 0), (x0, y0 + r, b),
    ]


def slot(msp, cx, cy, length, width, vertical=False):
    r = width / 2
    half = length / 2 - r
    b = bulge(180)
    if vertical:
        pts = [(cx + r, cy - half, 0), (cx + r, cy + half, b),
               (cx - r, cy + half, 0), (cx - r, cy - half, b)]
    else:
        pts = [(cx - half, cy - r, 0), (cx + half, cy - r, b),
               (cx + half, cy + r, 0), (cx - half, cy + r, b)]
    add_loop(msp, pts)


def save(doc, name):
    doc.saveas(OUT / f"{name}.dxf")
    print(f"wrote {name}.dxf")


def base_plate():
    doc = new_doc(); msp = doc.modelspace()
    add_loop(msp, rounded_rect(0, 0, 10, 8, 0.75))
    for x, y in [(1, 1), (9, 1), (1, 7), (9, 7)]:
        msp.add_circle((x, y), 0.3125)
    slot(msp, 5, 4, 4, 1)
    save(doc, "Base Plate")


def angle_bracket():
    doc = new_doc(); msp = doc.modelspace()
    r = 1.0  # inside fillet
    b = bulge(90)
    pts = [
        (0, 0, 0), (9, 0, 0), (9, 2.5, 0),
        (2.5 + r, 2.5, -b), (2.5, 2.5 + r, 0),
        (2.5, 9, 0), (0, 9, 0),
    ]
    add_loop(msp, pts)
    for x in (5, 7.5):
        msp.add_circle((x, 1.25), 0.28125)
    for y in (5, 7.5):
        msp.add_circle((1.25, y), 0.28125)
    save(doc, "Angle Bracket")


def gusset():
    doc = new_doc(); msp = doc.modelspace()
    pts = [(0, 0, 0), (8, 0, 0), (8, 1, 0), (1, 8, 0), (0, 8, 0)]
    add_loop(msp, pts)
    msp.add_circle((2.6, 2.6), 1.1)
    save(doc, "Gusset")


def flange():
    doc = new_doc(); msp = doc.modelspace()
    msp.add_circle((0, 0), 4.0)
    msp.add_circle((0, 0), 2.0)
    for i in range(6):
        a = math.radians(i * 60 + 30)
        msp.add_circle((3.0 * math.cos(a), 3.0 * math.sin(a)), 0.25)
    save(doc, "Flange")


def link():
    doc = new_doc(); msp = doc.modelspace()
    b = bulge(180)
    r = 1.0
    pts = [(0, -r, 0), (6, -r, b), (6, r, 0), (0, r, b)]
    add_loop(msp, pts)
    msp.add_circle((0, 0), 0.40625)
    msp.add_circle((6, 0), 0.40625)
    save(doc, "Link")


def clamp_jaw():
    doc = new_doc(); msp = doc.modelspace()
    # 7 x 4.5 block with a semicircular notch on top and chamfered corners.
    b = bulge(180)
    pts = [
        (0.5, 0, 0), (6.5, 0, 0), (7, 0.5, 0), (7, 4.5, 0),
        (5, 4.5, -b), (2, 4.5, 0),
        (0, 4.5, 0), (0, 0.5, 0),
    ]
    add_loop(msp, pts)
    msp.add_circle((1, 1.25), 0.25)
    msp.add_circle((6, 1.25), 0.25)
    save(doc, "Clamp Jaw")


def hex_cover():
    doc = new_doc(); msp = doc.modelspace()
    r = 3.5
    pts = [(r * math.cos(math.radians(60 * i)), r * math.sin(math.radians(60 * i)), 0)
           for i in range(6)]
    add_loop(msp, pts)
    msp.add_circle((0, 0), 1.25)
    for i in range(6):
        a = math.radians(60 * i + 30)
        msp.add_circle((2.3 * math.cos(a), 2.3 * math.sin(a)), 0.1875)
    save(doc, "Hex Cover")


if __name__ == "__main__":
    for make in (base_plate, angle_bracket, gusset, flange, link, clamp_jaw, hex_cover):
        make()
