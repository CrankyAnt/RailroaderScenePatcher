// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

static class WorldRibbonGeometryTests {
    // Actual offered ca9713f054c6487ebd6ef142a01a8eb1 and chosen
    // 7d2bec53f87a45768fc413576b68af42, source62fb2d1, frames12085/16857.
    // Camera, controls and strokes are identical. The old fourth S8f6 wide call
    // actually submitted every visible zone; these are current producer data.
    static readonly WorldPoint[] Controls = {
        new WorldPoint(25234.986328125,623.9530029296875,-703.7760009765625),
        new WorldPoint(25220.74609375,623.9530029296875,-745.3043823242188),
        new WorldPoint(25208.5390625,623.9530029296875,-780.900634765625),
        new WorldPoint(25194.298828125,623.9530029296875,-822.4290161132812)
    };
    static ClipPoint Camera(WorldPoint p) {
        double x=p.X-25000,y=p.Y,z=p.Z+1000;
        double cx=.9011029005050659*x-.4336053133010864*z-85.02886199951172;
        double cy=.14830175042152405*x+.9396926164627075*y+.3081953525543213*z-684.7352294921875;
        double cz=-.4074556827545166*x+.3420201539993286*y-.8467597365379333*z+16.396995544433594;
        return new ClipPoint(1.5454559326171875*cx,2.7474772930145264*cy,-1.0001332759857178*cz-.20001332461833954,-cz);
    }
    static readonly ScreenRect[] Zones = {
        new ScreenRect(604,41.9998779296875f,1956,565.0001220703125f),new ScreenRect(604,0,1508,41.9998779296875f),
        new ScreenRect(0,1267,2560,131),new ScreenRect(0,1410,2560,30),new ScreenRect(0,1398,2440,12),new ScreenRect(2530,1398,30,12),
        new ScreenRect(0,666,1660,601),new ScreenRect(604,607,1056,59),new ScreenRect(2360,607,200,61),new ScreenRect(2360,668,6,2),
        new ScreenRect(2500.2001953125f,668,59.7998046875f,2),new ScreenRect(2360,702,200,565),new ScreenRect(2360,700,6,2),
        new ScreenRect(2500.2001953125f,700,59.7998046875f,2),new ScreenRect(2360,670,6,30),new ScreenRect(2548,670,12,30)
    };
    static void Check(bool value,string message) { if(!value) throw new Exception("World ribbon: "+message); }
    static bool Inside(ScreenPoint p,ScreenRect r) => p.X>=r.X && p.X<=r.Right && p.Y>=r.Y && p.Y<=r.Bottom;
    static void Valid(IReadOnlyList<ScreenTriangle> triangles,ScreenRect[] zones) {
        foreach(var t in triangles) {
            var points=new[]{t.A,t.B,t.C};
            Check(points.All(p=>!double.IsNaN(p.X)&&!double.IsNaN(p.Y)&&!double.IsInfinity(p.X)&&!double.IsInfinity(p.Y)),"finite submitted geometry");
            Check(zones.Any(r=>points.All(p=>Inside(p,r))),"full submitted triangle belongs to one visible zone");
        }
    }
    static WorldPoint[] Line(WorldPoint a,WorldPoint d) => new[]{a,
        new WorldPoint((2*a.X+d.X)/3,(2*a.Y+d.Y)/3,(2*a.Z+d.Z)/3),
        new WorldPoint((a.X+2*d.X)/3,(a.Y+2*d.Y)/3,(a.Z+2*d.Z)/3),d};
    public static void Run() {
        var view=new ScreenRect(0,0,2560,1440);
        var actual=WorldRibbonGeometry.ProjectCurve(Controls,2,Camera,view);
        Check(actual.Ribbon.Count>0 && actual.Centerline.Count>0,"captured exposed track remains marked");
        Valid(actual.Ribbon,new[]{view}); Valid(actual.Centerline,new[]{view});
        Check(actual.Contains(new ScreenPoint(1472.42126,501.5962)),"captured track center is pickable");
        // Exposed-world witnesses away from this track in the actual image. The
        // captured old fourth stroke covered all of them, for both role colors.
        foreach(var p in new[]{new ScreenPoint(800,1000),new ScreenPoint(1280,100),new ScreenPoint(2500,100)})
            Check(!actual.Contains(p),"off-track world is neither painted nor pickable");
        var clipped=WorldStrokeGeometry.ClipTriangles(actual.Ribbon,Zones);
        Check(clipped.Count>0,"UI exclusion retains exposed ribbon"); Valid(clipped,Zones);
        Valid(WorldStrokeGeometry.ClipTriangles(actual.Centerline,Zones),Zones);
        // The same actual offered frame contains two other fullscreen producers.
        // Retain their independent controls, rather than testing only the named S8f6.
        foreach(var controls in new[] {
            new[]{new WorldPoint(25663.5234375,626.6749877929688,-1049.4720458984375),
                new WorldPoint(25723.9921875,626.6749877929688,-1078.923095703125),
                new WorldPoint(25784.951171875,628.3670043945312,-1102.001220703125),
                new WorldPoint(25859.703125,628.3670043945312,-1140.81298828125)},
            new[]{new WorldPoint(25158.923828125,623.2440185546875,-818.9929809570312),
                new WorldPoint(25164.33203125,623.2965087890625,-809.9920043945312),
                new WorldPoint(25168.01953125,623.260009765625,-801.7535400390625),
                new WorldPoint(25171.353515625,623.260009765625,-792.02197265625)}
        }) {
            var other=WorldRibbonGeometry.ProjectCurve(controls,2,Camera,view);
            Check(!other.Contains(new ScreenPoint(1280,100)),"other captured offscreen-width producer cannot tint unrelated upper world");
            Valid(other.Ribbon,new[]{view}); Valid(WorldStrokeGeometry.ClipTriangles(other.Ribbon,Zones),Zones);
        }

        // Worked orthographic rectangle: x=-4..4, radius2, square ends x=-6/6.
        // Screen support is x20..80,y40..60, including full width and end caps.
        var orthoView=new ScreenRect(0,0,100,100);
        Func<WorldPoint,ClipPoint> ortho=p=>new ClipPoint(p.X/10,p.Z/10,-p.Y,1);
        var broad=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(-4,0,0),new WorldPoint(4,0,0)),2,ortho,orthoView,1);
        Check(broad.Contains(new ScreenPoint(50,59)),"full broad width survives");
        Check(broad.Contains(new ScreenPoint(21,50)),"projected square end extension survives");
        Check(!broad.Contains(new ScreenPoint(19,50))&&!broad.Contains(new ScreenPoint(50,61)),"extension and width have actual edges");
        Valid(broad.Ribbon,new[]{orthoView});
        // This diagonal cap reaches the viewport although every center control and
        // its radius-only AABB are outside. Production uses the same cap-aware cull.
        var edge=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(12.5,0,0),new WorldPoint(16.5,0,4)),2,ortho,orthoView,1);
        Check(edge.Contains(new ScreenPoint(99.5,50)),"diagonal end cap at screen edge is not culled");
        Valid(edge.Ribbon,new[]{orthoView});

        var perspectiveView=new ScreenRect(0,0,200,200);
        Func<WorldPoint,ClipPoint> perspective=p=>new ClipPoint(p.X,p.Y,2001.0/1999*p.Z-4000.0/1999,p.Z);
        var near=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(0,-2,-2),new WorldPoint(0,-2,10)),2,perspective,perspectiveView);
        Check(near.Contains(new ScreenPoint(100,150)),"visible rail after near-plane crossing remains pickable");
        Check(!near.Contains(new ScreenPoint(100,20)),"near crossing cannot paint unrelated sky");
        Valid(near.Ribbon,new[]{perspectiveView}); Valid(near.Centerline,new[]{perspectiveView});
        var behind=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(0,-2,-20),new WorldPoint(0,-2,-10)),2,perspective,perspectiveView);
        Check(behind.Ribbon.Count==0&&behind.Centerline.Count==0,"fully behind-camera geometry is absent");
        var far=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(0,-2,3000),new WorldPoint(0,-2,3010)),2,perspective,perspectiveView);
        Check(far.Ribbon.Count==0,"beyond-far geometry is absent");
        var offscreen=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(100,-2,5),new WorldPoint(100,-2,10)),2,perspective,perspectiveView);
        Check(offscreen.Ribbon.Count==0,"offscreen corridor cannot create an oversized cap");
        var minimum=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(-5,0,100),new WorldPoint(5,0,100)),2,perspective,perspectiveView);
        Check(minimum.Contains(new ScreenPoint(100,109)),"visible distant track retains ten-pixel minimum width");
        Check(!minimum.Contains(new ScreenPoint(100,111)),"minimum width has no invisible expansion");
        var point=WorldRibbonGeometry.ProjectCurve(Line(new WorldPoint(0,0,10),new WorldPoint(0,0,10)),2,perspective,perspectiveView);
        Check(point.Contains(new ScreenPoint(100,100)),"degenerate visible curve retains drawn minimum marker");
        Valid(point.Ribbon,new[]{perspectiveView});
        Console.WriteLine("PASS: clipped world ribbon projection and shared picking");
    }
}
