// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

static class WorldStrokeGeometryTests {
    // capture-f297085c9edf42e1963739d5a18e1294-15654.jsonl, source a9,
    // SHA256 52E561D5CC280C901940B0C04293898AFADB6923496014A47DD279F367E4BBAC.
    // The old first wide draw used destination [596.0106,157.464859,436.648438,101.49192]
    // and beforeDraw matrix [0.661376536,-0.750054,611.207031; 0.750054,0.661376536,-853.4078].
    // Its rotation and destination dimensions support the expected quad below; GUIToScreenPoint
    // and unavailable clip internals do not establish a raster pixel mapping.
    static readonly ScreenRect Panel = new ScreenRect(1660,607,700,660);
    // JSON numbers were serialized from Unity floats; Rect right/bottom use float addition.
    static ScreenRect RecordedZone(double x, double y, double width, double height) {
        float left = (float)x, top = (float)y;
        float right = left + (float)width, bottom = top + (float)height;
        return ScreenRect.FromEdges(left,top,right,bottom);
    }
    static ScreenPoint RecordedPoint(double x, double y) => new ScreenPoint((float)x,(float)y);
    static readonly ScreenRect[] Zones = {
        RecordedZone(604,41.9998779,1956,565.0001), RecordedZone(604,0,1508,41.9998779),
        RecordedZone(0,1267,2560,131), RecordedZone(0,1410,2560,30),
        RecordedZone(0,1398,2440,12), RecordedZone(2530,1398,30,12),
        RecordedZone(0,666,1660,601), RecordedZone(604,607,1056,59),
        RecordedZone(2360,607,200,61), RecordedZone(2360,668,6,2),
        RecordedZone(2500.2002,668,59.7998047,2), RecordedZone(2360,702,200,565),
        RecordedZone(2360,700,6,2), RecordedZone(2500.2002,700,59.7998047,2),
        RecordedZone(2360,670,6,30), RecordedZone(2548,670,12,30)
    };
    static readonly ScreenPoint[] A = { RecordedPoint(1250.75659,250.2107), RecordedPoint(1472.42126,501.5962), RecordedPoint(1902.41382,989.187439), RecordedPoint(58880.59,65616.69) };
    static readonly ScreenPoint[] B = { RecordedPoint(1472.42126,501.5962), RecordedPoint(1902.41382,989.187439), RecordedPoint(2766.59131,1969.13342), RecordedPoint(126044.719,141783.813) };
    static readonly double[] Radii = { (float)50.74596, (float)101.866295, (float)204.492523, (float)123099.414 };
    static void Check(bool condition, string message) { if (!condition) throw new Exception("World stroke geometry: " + message); }
    static double Cross(ScreenPoint a, ScreenPoint b, ScreenPoint c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    static double Area(IReadOnlyList<ScreenTriangle> ts) => ts.Sum(t => Math.Abs(Cross(t.A,t.B,t.C))/2);
    static bool InRect(ScreenPoint p, ScreenRect r, double e = 1e-6) => p.X >= r.X-e && p.X <= r.Right+e && p.Y >= r.Y-e && p.Y <= r.Bottom+e;
    static bool Covers(IReadOnlyList<ScreenTriangle> ts, ScreenPoint p) => ts.Any(t => {
        double a = Cross(t.A,t.B,p), b = Cross(t.B,t.C,p), c = Cross(t.C,t.A,p);
        return (a >= -1e-6 && b >= -1e-6 && c >= -1e-6) || (a <= 1e-6 && b <= 1e-6 && c <= 1e-6);
    });
    static void Valid(IReadOnlyList<ScreenTriangle> ts, ScreenRect[] zones) {
        foreach (var t in ts) {
            var vertices = new[] { t.A,t.B,t.C };
            Check(vertices.All(p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y)), "finite vertices");
            Check(Math.Abs(Cross(t.A,t.B,t.C)) > 1e-9, "nondegenerate triangle");
            Check(zones.Any(z => vertices.All(p => InRect(p,z))), "entire triangle belongs to one visible zone");
            var submitted = vertices.Select(p => new ScreenPoint((float)p.X,(float)p.Y)).ToArray();
            Check(submitted.All(p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y)), "submitted float vertices are finite");
            Check(Math.Abs(Cross(submitted[0],submitted[1],submitted[2])) > 0, "submitted float triangle is nondegenerate");
            Check(zones.Any(z => submitted.All(p => InRect(p,z,0))), "submitted float triangle belongs to one visible zone");
        }
    }
    static void Near(double actual, double expected, double tolerance, string message) => Check(Math.Abs(actual-expected) <= tolerance, message + ": " + actual + " expected " + expected);
    static void HasVertex(IReadOnlyList<ScreenTriangle> ts, double x, double y) => Check(ts.Any(t => new[] { t.A,t.B,t.C }.Any(p => Math.Abs(p.X-x)<0.02 && Math.Abs(p.Y-y)<0.02)), "expected quad corner");
    public static void Run() {
        // Each zone is disjoint from the panel interior. Zone containment of all three
        // submitted vertices therefore excludes panel coverage by edges and faces too.
        foreach (var zone in Zones)
            Check(Math.Min(zone.Right,Panel.Right) <= Math.Max(zone.X,Panel.X) ||
                  Math.Min(zone.Bottom,Panel.Bottom) <= Math.Max(zone.Y,Panel.Y), "recorded zone excludes panel interior");
        // Captured destination length and rotation agree with the global endpoints and
        // the independently calculated quad corners below, not a raster mapping claim.
        Near(B[0].X-A[0].X,335.156525*0.661376536,0.02,"captured rotation x");
        Near(B[0].Y-A[0].Y,335.156525*0.750054,0.02,"captured rotation y");
        Near(436.648438*101.49192,44316.28894,0.01,"captured destination area");
        var all = new[] { new ScreenRect(-1000,-1000,5000,5000) };
        var wide = WorldStrokeGeometry.Triangulate(A[0],B[0],Radii[0],true,all);
        Valid(wide,all);
        // Globalize the recorded destination, then apply the captured beforeDraw matrix.
        // This is a geometry reference, not proof of Unity raster pixels or clip internals.
        double left = 596.0106+604, top = 157.464859+41.9998779;
        foreach (var corner in new[] {
            new[] { left, top }, new[] { left+436.648438, top },
            new[] { left+436.648438, top+101.49192 }, new[] { left, top+101.49192 }
        })
            HasVertex(wide,0.661376536*corner[0]-0.750054*corner[1]+611.207031,
                0.750054*corner[0]+0.661376536*corner[1]-853.4078);
        HasVertex(wide,1255.25661,178.58630); HasVertex(wide,1544.04566,506.09622);
        HasVertex(wide,1467.92124,573.22060); HasVertex(wide,1179.13219,245.71068);
        Near(Area(wide),44316.28894,0.1,"recorded wide quad area including end extensions");
        var center = WorldStrokeGeometry.Triangulate(A[0],B[0],1,false,all);
        // Below 2048, float rounding moves a vertex by at most sqrt(2)/16384 pixels.
        // The recorded perimeter is below 675 pixels, bounding area drift below 0.06.
        Valid(center,all); Near(Area(center),670.31305,0.06,"recorded centerline area without extensions");
        for (int i=0; i<A.Length; i++) {
            foreach (bool extended in new[] { true, false }) {
                var triangles = WorldStrokeGeometry.Triangulate(A[i],B[i],extended ? Radii[i] : 1,extended,Zones);
                Valid(triangles,Zones);
                // A triangle contained in one recorded zone cannot cover the panel interior.
                Check(!triangles.Any(t => InRect(t.A,Panel,-1e-6) || InRect(t.B,Panel,-1e-6) || InRect(t.C,Panel,-1e-6)), "no panel interior vertices");
                if (i < 3) Check(Area(triangles)>0, "recorded exposed world remains visible");
            }
        }
        var huge = WorldStrokeGeometry.Triangulate(A[3],B[3],Radii[3],true,Zones);
        foreach (var zone in Zones) Check(Covers(huge,new ScreenPoint(zone.X+zone.Width/2,zone.Y+zone.Height/2)), "huge recorded radius covers each visible zone");
        Near(Area(huge),Zones.Sum(z => z.Width*z.Height),0.1,"huge radius covers visible zone area");
        var z = new[] { new ScreenRect(0,0,10,10) };
        var crossing = WorldStrokeGeometry.Triangulate(new ScreenPoint(-5,5),new ScreenPoint(15,5),2,false,z);
        Valid(crossing,z); Near(Area(crossing),40,1e-6,"crossing retains full width");
        Check(Covers(crossing,new ScreenPoint(5,6.5)), "crossing retains off-center world");
        var reversed = WorldStrokeGeometry.Triangulate(new ScreenPoint(15,5),new ScreenPoint(-5,5),2,false,z);
        Valid(reversed,z); Near(Area(reversed),40,1e-6,"reversed endpoints");
        var cap = WorldStrokeGeometry.Triangulate(new ScreenPoint(2,5),new ScreenPoint(4,5),2,true,z);
        Valid(cap,z); Near(Area(cap),24,1e-6,"extended end caps"); Check(Covers(cap,new ScreenPoint(0.5,5)), "end cap survives");
        var square = WorldStrokeGeometry.Triangulate(new ScreenPoint(5,5),new ScreenPoint(5,5),2,true,z);
        Valid(square,z); Near(Area(square),16,1e-6,"zero-length wide stroke is a square");
        Check(WorldStrokeGeometry.Triangulate(new ScreenPoint(5,5),new ScreenPoint(5,5),1,false,z).Count == 0, "zero-length centerline is empty");
        Check(WorldStrokeGeometry.Triangulate(new ScreenPoint(-2,-2),new ScreenPoint(-2,0),2,false,z).Count == 0, "boundary contact has no area");
        Check(WorldStrokeGeometry.Triangulate(new ScreenPoint(-2,-2),new ScreenPoint(-2,-2),2,true,z).Count == 0, "corner contact has no area");
        Check(WorldStrokeGeometry.Triangulate(A[0],B[0],Radii[0],true,Array.Empty<ScreenRect>()).Count == 0, "empty zones");
        Console.WriteLine("PASS: world stroke geometry");
    }
}
