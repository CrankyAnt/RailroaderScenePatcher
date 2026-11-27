// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The place rule with synthetic places, positions and paths: where a place stands by the order of its sources, the
// 500 metre boundary of a moved object, and the choice by path and distance (1500 and 3000 metres) of any other object.
static class ScenePlaceTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Scene places: " + message); }
    static PlaceSource Source(PlaceSourceKind kind, string name, double x, double z) => new PlaceSource(kind, name, new[] { x, z });
    static PlaceSource Area(string name, double x, double z) => Source(PlaceSourceKind.MapDataArea, name, x, z);
    static string Points(ScenePlace place) => place == null ? "no place" : String.Join(" ", place.Points.Select(p => p[0] + "," + p[1]));

    public static void Run() {
        PositionsComeFromTheMostReliableSource();
        NamesKeepTheirTrust();
        AMovedObjectTakesTheNearestPlace();
        AnUnmovedObjectTakesThePlaceItsPathNames();
        Console.WriteLine("PASS: scene places");
    }

    // Map labels first, then the map data's areas, its passenger stops, the Loc: boards, station map icons, the
    // objects of a place's folder and those of its Roads folder. Each step drops the most reliable source left.
    static void PositionsComeFromTheMostReliableSource() {
        var sources = new List<PlaceSource> {
            Source(PlaceSourceKind.RoadsFolder, "Ashby", 70, 70),
            Source(PlaceSourceKind.SceneFolder, "Ashby", 60, 60), Source(PlaceSourceKind.SceneFolder, "Ashby", 62, 62), Source(PlaceSourceKind.SceneFolder, "Ashby", 66, 66),
            Source(PlaceSourceKind.StationMapIcon, "Ashby", 50, 50),
            Source(PlaceSourceKind.LocBoard, "Ashby", 40, 40), Source(PlaceSourceKind.LocBoard, "Ashby", 41, 41),
            Source(PlaceSourceKind.MapDataPassengerStop, "Ashby", 30, 30),
            Area("Ashby", 20, 20),
            Source(PlaceSourceKind.MapLabel, "Ashby", 10, 10),
        };
        var expected = new[] {
            (PlaceSourceKind.MapLabel, "10,10"), (PlaceSourceKind.MapDataArea, "20,20"), (PlaceSourceKind.MapDataPassengerStop, "30,30"), (PlaceSourceKind.LocBoard, "40,40 41,41"),
            (PlaceSourceKind.StationMapIcon, "50,50"), (PlaceSourceKind.SceneFolder, "62,62"), (PlaceSourceKind.RoadsFolder, "70,70") };
        foreach (var (kind, points) in expected) {
            var place = ScenePlaces.From(sources).All.Single();
            Check(place.PointsFrom == kind && Points(place) == points, "with " + kind + " as the most reliable source left, Ashby stands at " + points + " (got " + place.PointsFrom + " " + Points(place) + ")");
            sources.RemoveAll(s => s.Kind == kind);
        }
        // A source without a position gives none: the next one does.
        var unplaced = ScenePlaces.From(new[] { new PlaceSource(PlaceSourceKind.MapLabel, "Ashby"), Area("Ashby", 20, 20) }).All.Single();
        Check(unplaced.PointsFrom == PlaceSourceKind.MapDataArea && Points(unplaced) == "20,20", "a map label without a position leaves the area's position (got " + Points(unplaced) + ")");
        // The middle of the map data's positions stays available, whatever the place's position comes from.
        var both = ScenePlaces.From(new[] { Area("Ashby", 20, 20), Source(PlaceSourceKind.MapDataPassengerStop, "Ashby", 30, 40), Source(PlaceSourceKind.MapLabel, "Ashby", 10, 10) }).All.Single();
        Check(both.MapDataPoint != null && both.MapDataPoint[0] == 25 && both.MapDataPoint[1] == 30, "the map data point is the middle of the area and the passenger stop (got " + (both.MapDataPoint == null ? "none" : both.MapDataPoint[0] + "," + both.MapDataPoint[1]) + ")");
    }

    // Splitting the map data into areas and passenger stops changes no name: they name places as one source, so a
    // passenger stop never falls together with an area by holding its words, and a map label still does.
    static void NamesKeepTheirTrust() {
        var places = ScenePlaces.From(new[] {
            Area("Alarka", 0, 0), Source(PlaceSourceKind.MapDataPassengerStop, "Alarka", 10, 0),
            Area("Alarka Jct", 5000, 0), Source(PlaceSourceKind.MapDataPassengerStop, "Ashby Siding", 9000, 0), Area("Ashby", 9100, 0),
            Source(PlaceSourceKind.MapLabel, "Connelly", 20000, 0), Area("Connelly Creek", 20100, 0) });
        var names = String.Join(", ", places.All.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Check(names == "Alarka, Alarka Jct, Ashby, Ashby Siding, Connelly Creek", "areas and passenger stops of other names stay apart and a map label falls in with the area it names (got " + names + ")");
        Check(places.Named("Alarka").From == PlaceSourceKind.MapDataArea && places.Named("Connelly").Name == "Connelly Creek", "a place takes its name from its area");
        Check(Points(places.Named("Connelly")) == "20000,0", "Connelly Creek stands at its map label (got " + Points(places.Named("Connelly")) + ")");
    }

    // Beyond 500 metres from where the map placed it, an object is listed under the nearest place, whatever its path
    // names. Exactly 500 metres keeps the place from its path.
    static void AMovedObjectTakesTheNearestPlace() {
        var places = ScenePlaces.From(new[] { Area("Ashby", 0, 0), Area("Birch", 800, 0) });
        const string shed = "World/Large Scenery/Ashby/Shed";
        var placed = new[] { 0.0, 0 };
        string At(double x, double z) => places.Of(shed, new[] { x, z }, placed)?.Name;
        Check(At(500, 0) == "Ashby", "500 metres from where the map placed it, the shed keeps the place its path names (got " + At(500, 0) + ")");
        Check(At(500.01, 0) == "Birch", "just over 500 metres from where the map placed it, the shed is listed under the nearest place (got " + At(500.01, 0) + ")");
        Check(At(0, 600) == "Ashby", "moved more than 500 metres, the shed takes the nearest place, which can be the one its path names (got " + At(0, 600) + ")");
        Check(places.Of(shed, new[] { 500.01, 0 }) ?.Name == "Ashby", "without where the map placed it, the shed takes the place its path names");
        Check(!ScenePlaces.IsMoved(new[] { 500.0, 0 }, placed) && ScenePlaces.IsMoved(new[] { 500.01, 0 }, placed) && !ScenePlaces.IsMoved(new[] { 900.0, 0 }, null), "an object counts as moved only beyond 500 metres from a known placement");
        Check(ScenePlaces.Moved == 500, "the boundary is 500 metres");
    }

    // Within 500 metres of where the map placed it, an object takes the most specific place its path names within
    // 1500 metres, else the place its path names that it stands nearest to within 3000 metres, else the nearest place.
    static void AnUnmovedObjectTakesThePlaceItsPathNames() {
        var places = ScenePlaces.From(new[] { Area("Ashby", 0, 0), Area("Birch", 4000, 0), Area("Cedar", 2700, 0), Area("Dale", -6000, 0) });
        const string birchShed = "World/Large Scenery/Ashby/Birch Shed", ashbyShed = "World/Large Scenery/Ashby/Shed";
        string Of(string path, double x) { var at = new[] { x, 0 }; return places.Of(path, at, at)?.Name; }
        Check(Of(birchShed, 2600) == "Birch", "1400 metres from Birch, which its own name names, the shed is listed at Birch, though Cedar is nearer (got " + Of(birchShed, 2600) + ")");
        Check(Of(birchShed, 2500) == "Birch", "1500 metres from Birch is close (got " + Of(birchShed, 2500) + ")");
        Check(Of(birchShed, 1700) == "Ashby", "farther than 1500 metres from both, the shed takes the named place it stands nearest to within 3000 metres, though Cedar is nearer (got " + Of(birchShed, 1700) + ")");
        Check(Of(ashbyShed, -3000) == "Ashby", "3000 metres from Ashby is within reach (got " + Of(ashbyShed, -3000) + ")");
        Check(Of(ashbyShed, -3500) == "Dale", "farther than 3000 metres from every place its path names, the shed takes the nearest place (got " + Of(ashbyShed, -3500) + ")");
        Check(Of("World/Signs/Speed 25", 2650) == "Cedar", "an object whose path names no place takes the nearest place (got " + Of("World/Signs/Speed 25", 2650) + ")");
    }
}
