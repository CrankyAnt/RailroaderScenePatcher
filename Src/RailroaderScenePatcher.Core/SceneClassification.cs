// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RailroaderScenePatcher.Core {
    // The category and kind the outliner lists an object under. Recognised is false when no rule recognised the object
    // and it took the category of last resort.
    public sealed class SceneClass {
        internal SceneClass(string category, string kind, bool recognised = true) { Category = category; Kind = kind; Recognised = recognised; }
        public string Category { get; }
        // The kind inside the category, or null for a category without kinds.
        public string Kind { get; }
        public bool Recognised { get; }
        public override string ToString() => Kind == null ? Category : Category + " / " + Kind;
    }

    // The families the inventory lists objects in, by their names: one place for the code that asks which family an
    // object is in. A family is the object's technical kind, which its edits follow.
    public static class SceneFamilies {
        public const string SceneObject = SceneObjectRules.Family, ChildObject = SceneObjectRules.ChildFamily, TrackMarker = "Track marker", Milepost = "Milepost", LocationSign = "Location sign",
            CrossingController = ObjectMarkingGeometry.ControllerFamily, CrossingPad = "Crossing pad marker", CrossingMast = ExistingEquipmentScene.MastFamily, CrossingBell = ExistingEquipmentScene.BellFamily,
            TrackSpan = FocusTarget.Span, AddedEquipment = "Added light or gate", AddedBell = "Added bell", Bridge = SceneObjectCategories.BridgeFamily, BridgeConstruction = SceneObjectCategories.ConstructionFamily,
            SpawnPoint = SpawnPoints.Family, StarterPlacement = StarterPlacements.Family, StationMapIcon = StationMapIconRules.Family;
        // The families of a crossing's own objects: its controller, its equipment and its detection spans.
        public static readonly IReadOnlyList<string> Crossing = Array.AsReadOnly(new[] { CrossingController, CrossingMast, CrossingBell, CrossingPad, TrackSpan, AddedEquipment, AddedBell });
    }

    // What an object is, decided from what it provides: its components, the components and names below it, its own
    // name and its hierarchy path. No rule names one object: a rule names a component the game gives a kind of object,
    // or a word the game's names use for it, so an object that a later game version or a mod adds in the same
    // construction is recognised without a change here.
    public static class SceneClassification {
        // The categories of the families whose objects are a kind of their own: each is named as its family, except
        // Starter placement, whose family is Starter placement marker.
        public const string TrackMarker = SceneFamilies.TrackMarker, SpawnPoint = SceneFamilies.SpawnPoint, StarterPlacement = "Starter placement", StationMapIcon = SceneFamilies.StationMapIcon;
        public const string LocationSigns = "Location signs", LocationMarkers = "Location markers", Mileposts = "Mileposts", OtherSigns = "Other signs", SetupMarkers = "Setup markers";
        public const string ProtectedCrossings = "Crossings with signals", UnprotectedCrossings = "Crossings without signals";
        public const string LakeSurfaces = "Lake surfaces", StoragePiles = "Storage piles", LogPonds = "Log ponds";
        public const string LocPrefix = "Loc:";
        const string World = "World/", LakeSurface = "LakePolygon", MapIcon = "UI.Map.MapIcon", SpawnPointComponent = "Character.SpawnPoint", MilepostComponent = "Helpers.MilePost",
            CarLoader = "RollingStock.CarContentSwapper", StorageSynchronizer = "Scenery.BulkIndustryStorageSynchronizer", Light = "UnityEngine.Light";

        // One word rule: a word the game's names use for a kind of object, the category it stands for, and the kind
        // inside that category. A rule without a kind takes the object's type name (TypeName) as its kind.
        sealed class NameRule {
            internal NameRule(string pattern, string category, string kind = null) { Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); Category = category; Kind = kind; }
            internal readonly Regex Pattern; internal readonly string Category, Kind;
        }
        // In order: the first rule whose word the name holds decides. A crossing and a sign come first, because their
        // names also hold the word of what they stand at, as "Speed - Ela Bridge" and "Sign-Station" do.
        static readonly NameRule[] NameRules = {
            new NameRule(@"\bcrossing", SceneObjectCategories.GradeCrossing),
            new NameRule(@"\bmile\s*posts?\b", SceneObjectCategories.Sign, Mileposts),
            new NameRule(@"\b(speed(\s*sign)?|speedlimit)\b", SceneObjectCategories.Sign, "Speed signs"),
            new NameRule(@"\byard\s+limit\b", SceneObjectCategories.Sign, "Yard limits"),
            new NameRule(@"\b(station\s+signs?|sign[-_\s]*station)\b", SceneObjectCategories.Sign, "Station signs"),
            new NameRule(@"\bwhistle\b", SceneObjectCategories.Sign, "Whistle signs"),
            new NameRule(@"\bctc\b|\bcircuit\b.*\bsigns?\b|\bsigns?\b.*\bcircuit\b", SceneObjectCategories.Sign, "CTC signs"),
            new NameRule(@"\bsigns?\b", SceneObjectCategories.Sign, OtherSigns),
            new NameRule(@"\broad\s+bridge\b", SceneObjectCategories.Bridge, SceneObjectCategories.RoadBridges),
            new NameRule(@"\b(bridge|trestle|viaduct)\b", SceneObjectCategories.Bridge, SceneObjectCategories.RailBridges),
            new NameRule(@"\b(piles?|log\s+pond)\b", SceneObjectCategories.StorageDisplay),
            new NameRule(@"flag\s*stop\s*station\b", SceneObjectCategories.StationBuilding, "Flag stop stations"),
            new NameRule(@"\bdepot\b", SceneObjectCategories.StationBuilding, "Depots"),
            new NameRule(@"\bstation\b", SceneObjectCategories.StationBuilding, "Stations"),
            new NameRule(@"\bplatforms?\b", SceneObjectCategories.Platform),
            new NameRule(@"\b(roundhouse|turntable|tower|section\s+house|tool\s+house|engine\s+house|sand\s+house|water\s+tank|coaling)\b", SceneObjectCategories.RailroadServiceBuilding),
            new NameRule(@"\b(shade|spotlight|spot\s+light|lamps?|lanterns?|lights?)\b", SceneObjectCategories.Lights),
            new NameRule(@"\b(mine|mill|elevator|dealer|oil|coal|loader|quarry|tannery|lumber|factory|plant|warehouse|pavilions?|shed|supply|works)\b", SceneObjectCategories.IndustryBuilding),
        };
        static readonly Regex Ordinal = new Regex(@"(\s*\(\d+\)|(?<![xX])\s+\d+)\z"), Culled = new Regex(@"\s+culled\z", RegexOptions.IgnoreCase), Edge = new Regex(@"\A[\s\-:,&]+|[\s\-:,&]+\z"),
            Crossing = new Regex(@"\bcrossing", RegexOptions.IgnoreCase), Spaces = new Regex(@"\s{2,}");

        // A category and kind decided elsewhere, such as the category the inventory gave a top scene object.
        public static SceneClass Class(string category, string kind) => new SceneClass(category, kind, category != SceneObjectCategories.Unclassified);
        static string Last(string path) => path == null ? null : path.Substring(path.LastIndexOf('/') + 1);
        static bool Has(IEnumerable<string> types, string type) => types != null && types.Contains(type);
        static bool HasPrefix(IEnumerable<string> types, string prefix) => types != null && types.Any(t => t != null && t.StartsWith(prefix, StringComparison.Ordinal));

        // The name of the kind of thing an object is, as the game names it: its own name without the number that tells
        // copies apart, without the mark of a culling group, and without the place it stands at. stripPlaces takes the
        // place names out of a name (ScenePlaces.StripPlaces); without it the place stays.
        public static string TypeName(string name, Func<string, string> stripPlaces = null) {
            if (String.IsNullOrWhiteSpace(name)) return null;
            var text = Culled.Replace(Ordinal.Replace(name.Trim(), ""), "");
            var stripped = stripPlaces == null ? text : stripPlaces(text);
            // A name that is only a place keeps it: it has no other word to go by.
            if (!String.IsNullOrWhiteSpace(stripped)) text = stripped;
            text = Spaces.Replace(Edge.Replace(Edge.Replace(text, ""), ""), " ");
            return text.Length == 0 ? null : text;
        }

        // What an object's own name says it is, or null when its name holds none of the words.
        public static SceneClass ByName(string name, Func<string, string> stripPlaces = null) {
            if (String.IsNullOrEmpty(name)) return null;
            foreach (var rule in NameRules) if (rule.Pattern.IsMatch(name)) return new SceneClass(rule.Category, rule.Kind ?? KindOf(rule.Category, name, stripPlaces));
            return null;
        }
        // A grade crossing has no kinds by name: its kind comes from what it holds (signals or none).
        static string KindOf(string category, string name, Func<string, string> stripPlaces) =>
            category == SceneObjectCategories.GradeCrossing ? null : TypeName(name, stripPlaces);

        // What the components on an object itself, and at or below it, say it is, or null. A lake surface, a storage
        // pile or log pond, a crossing controller and a loader that fills cars each have a
        // component of their own.
        public static SceneClass ByComponents(IEnumerable<string> own, IEnumerable<string> atOrBelow) {
            if (Has(atOrBelow, SceneObjectRules.LogPond)) return new SceneClass(SceneObjectCategories.StorageDisplay, LogPonds);
            if (Has(atOrBelow, SceneObjectRules.StoragePile) || Has(atOrBelow, StorageSynchronizer)) return new SceneClass(SceneObjectCategories.StorageDisplay, StoragePiles);
            if (Has(own, LakeSurface)) return new SceneClass(SceneObjectCategories.Water, LakeSurfaces);
            if (Has(own, SceneObjectRules.CrossingController)) return new SceneClass(SceneObjectCategories.GradeCrossing, null);
            if (Has(own, CarLoader)) return new SceneClass(SceneObjectCategories.IndustryBuilding, null);
            return null;
        }

        // What the objects below an object say it is, or null: the category most of their names stand for, with the
        // type name most of those objects share as the kind; without such names, an object that holds lights is a
        // light. So a group named after its place alone, as Sylva Culled is, is what it holds.
        public static SceneClass ByContent(IEnumerable<string> namesBelow, IEnumerable<string> atOrBelow, Func<string, string> stripPlaces = null) {
            var votes = (namesBelow ?? Enumerable.Empty<string>()).Select(name => new { Name = name, Class = ByName(name) })
                .Where(v => v.Class != null && v.Class.Category != SceneObjectCategories.GradeCrossing).ToArray();
            // A lamp or a sign is a fixture of what it stands on: it counts only where nothing else is named.
            var structures = votes.Where(v => v.Class.Category != SceneObjectCategories.Lights && v.Class.Category != SceneObjectCategories.Sign).ToArray();
            if (structures.Length != 0) votes = structures;
            if (votes.Length != 0) {
                var category = votes.GroupBy(v => v.Class.Category).OrderByDescending(g => g.Count()).ThenBy(g => Array.FindIndex(NameRules, r => r.Category == g.Key)).First();
                var kind = category.GroupBy(v => v.Class.Kind != null && NameRules.Any(r => r.Kind == v.Class.Kind) ? v.Class.Kind : TypeName(v.Name, stripPlaces))
                    .Where(g => g.Key != null).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.Key).FirstOrDefault();
                return new SceneClass(category.Key, kind);
            }
            return Has(atOrBelow, Light) ? new SceneClass(SceneObjectCategories.Lights, null) : null;
        }

        // The category of last resort, from the game's own container below World: what stands along a road is road
        // scenery and what stands with the rivers is water. Anything else is not recognised.
        static SceneClass ByContainer(string path, string name, Func<string, string> stripPlaces) {
            var container = path != null && path.StartsWith(World, StringComparison.Ordinal) ? path.Substring(World.Length).Split('/')[0] : "";
            if (container.StartsWith("Roads", StringComparison.OrdinalIgnoreCase)) return new SceneClass(SceneObjectCategories.RoadScenery, TypeName(name, stripPlaces));
            if (container.StartsWith("Rivers", StringComparison.OrdinalIgnoreCase) || container.StartsWith("Lakes", StringComparison.OrdinalIgnoreCase)) return new SceneClass(SceneObjectCategories.Water, TypeName(name, stripPlaces));
            return new SceneClass(SceneObjectCategories.Unclassified, null, false);
        }

        // A top scene object of the Scene object family: its components first, then its own name, then what it
        // holds, then the game's container it stands in. A kind taken from the name loses the place in it; a group
        // whose category has kinds and whose name gives none takes its type name.
        public static SceneClass OfGroup(string path, IEnumerable<string> own, IEnumerable<string> atOrBelow, IEnumerable<string> namesBelow, Func<string, string> stripPlaces = null) {
            var name = Last(path);
            var found = ByComponents(own, atOrBelow) ?? ByName(name, stripPlaces) ?? ByContent(namesBelow, atOrBelow, stripPlaces) ?? ByContainer(path, name, stripPlaces);
            // A crossing has no kind of its own here: the outliner tells crossings with signals from those without.
            if (found.Category == SceneObjectCategories.GradeCrossing) return new SceneClass(found.Category, null);
            // A container of signs says only that it holds signs: which signs, its contents say.
            if (found.Category == SceneObjectCategories.Sign && found.Kind == OtherSigns && ByContent(namesBelow, atOrBelow, stripPlaces) is SceneClass held && held.Category == SceneObjectCategories.Sign && held.Kind != null) return held;
            // A name that says no more than its category, as Platform does, leaves the kind to what the group holds.
            if ((found.Kind == null || String.Equals(found.Kind, found.Category, StringComparison.OrdinalIgnoreCase)) && found.Recognised
                && ByContent(namesBelow, atOrBelow, stripPlaces) is SceneClass content && content.Category == found.Category && content.Kind != null) return content;
            return found.Kind != null || !found.Recognised ? found : new SceneClass(found.Category, TypeName(name, stripPlaces));
        }

        // Any listed object. family is the technical family the inventory lists it in, which its edits follow; the
        // category does not follow the family where the object itself says more.
        public static SceneClass Of(string family, string path, IEnumerable<string> own, IEnumerable<string> atOrBelow, IEnumerable<string> namesBelow, Func<string, string> stripPlaces = null) {
            var name = Last(path) ?? "";
            atOrBelow = atOrBelow ?? own;
            // A Loc: object is a location board when it has something to show, and an invisible marker otherwise.
            if (name.StartsWith(LocPrefix, StringComparison.Ordinal))
                return Shows(atOrBelow) ? new SceneClass(SceneObjectCategories.Sign, LocationSigns) : new SceneClass(TrackMarker, LocationMarkers);
            if (Has(own, MapIcon) || family == SceneFamilies.StationMapIcon) return new SceneClass(StationMapIcon, null);
            if (Has(own, SpawnPointComponent) || family == SceneFamilies.SpawnPoint) return new SceneClass(SpawnPoint, null);
            if (family == SceneFamilies.StarterPlacement) return new SceneClass(StarterPlacement, null);
            if (Has(own, MilepostComponent)) return new SceneClass(SceneObjectCategories.Sign, Mileposts);
            // An object outside World is game setup, not scenery: its name says nothing about what stands in the world.
            if (path == null || !path.StartsWith(World, StringComparison.Ordinal))
                return SceneFamilies.Crossing.Contains(family) ? new SceneClass(SceneObjectCategories.GradeCrossing, null) : new SceneClass(family ?? TrackMarker, family == SceneFamilies.TrackMarker ? SetupMarkers : null, family != null);
            if (SceneObjectRules.InFamilies(family)) return OfGroup(path, own, atOrBelow, namesBelow, stripPlaces);
            var found = ByComponents(own, null) ?? ByName(name, stripPlaces);
            // What stands below a crossing belongs to it: its masts, its bell, its pads and its detection spans.
            if (found == null && (Crossing.IsMatch(Parent(path) ?? "") || SceneFamilies.Crossing.Contains(family))) found = new SceneClass(SceneObjectCategories.GradeCrossing, null);
            if (found == null) found = OfFamily(family) ?? ByContainer(path, name, stripPlaces);
            if (found.Category == SceneObjectCategories.GradeCrossing) return new SceneClass(found.Category, null);
            return found;
        }
        static string Parent(string path) { var at = path.LastIndexOf('/'); return at < 0 ? null : path.Substring(0, at); }
        // What the technical family says when the object itself says nothing more.
        static SceneClass OfFamily(string family) {
            switch (family) {
                case SceneFamilies.Milepost: return new SceneClass(SceneObjectCategories.Sign, Mileposts);
                case SceneFamilies.LocationSign: return new SceneClass(SceneObjectCategories.Sign, OtherSigns);
                // The bridge and the construction controller of a rail bridge construction stand with the other rail bridges.
                case SceneFamilies.Bridge: case SceneFamilies.BridgeConstruction: return new SceneClass(SceneObjectCategories.Bridge, SceneObjectCategories.RailBridges);
                case SceneFamilies.TrackMarker: return new SceneClass(TrackMarker, null);
                default: return null;
            }
        }
        // Something to show: a renderer, or the culler of a prefab that switches its renderers by camera distance.
        static bool Shows(IEnumerable<string> atOrBelow) =>
            Has(atOrBelow, SceneObjectRules.RendererCuller) || (atOrBelow ?? Enumerable.Empty<string>()).Any(t => t != null && t.EndsWith("Renderer", StringComparison.Ordinal) && !SceneObjectRules.EffectRenderers.Contains(t));

        // An object below World/Large Scenery that is named as a structure, such as Almond Bridge or Alarka Signs: it
        // is no place of its own, though its name may hold one.
        public static bool NamesStructure(string name) => ByName(name) != null;
    }
}
