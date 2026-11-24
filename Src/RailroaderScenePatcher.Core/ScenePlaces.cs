// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RailroaderScenePatcher.Core {
    // Where the game names a place, most trusted first: its map data (the areas and passenger stops of the
    // operations data), a station map icon, a folder of a place below World/Large Scenery, a Roads folder, a map label
    // and a Loc: board.
    public enum PlaceSourceKind { MapData, StationMapIcon, SceneFolder, RoadsFolder, MapLabel, LocBoard }

    // One naming of a place by the game, with where it is in map x/z, or null.
    public sealed class PlaceSource {
        public PlaceSource(PlaceSourceKind kind, string name, double[] at = null) { Kind = kind; Name = name; At = at; }
        public PlaceSourceKind Kind { get; }
        public string Name { get; }
        public double[] At { get; }
    }

    public sealed class ScenePlace {
        internal ScenePlace(string name, PlaceSourceKind from) { Name = name; From = from; }
        // The game's own name of the place: the name of its most trusted source.
        public string Name { get; }
        public PlaceSourceKind From { get; }
        // Every name the game uses for the place, the place's own name first.
        public IReadOnlyList<string> Names => names;
        // Where the game puts the place: one point per source that has one.
        public IReadOnlyList<double[]> Points => points;
        internal readonly List<string> names = new List<string>();
        internal readonly List<double[]> points = new List<double[]>();
        internal readonly List<string[]> tokens = new List<string[]>();
    }

    // The places of a map as the game itself names them, and the place of an object. Nothing here names a place: the
    // places come from the sources, and two names fall together by rule.
    //
    // Names fall together when they are the same words, or when a less trusted name holds all the words of a more
    // trusted place or the other way round: Connelly (a Loc: board) is Connelly Creek (map data), Sylva Int is Sylva.
    // Two names of one kind of source never fall together that way: the map data lists Alarka and Alarka Jct side by
    // side, so they are two places.
    //
    // An object's place is the place its own name or hierarchy path names. Where the path names several places, as
    // World/Large Scenery/Whittier/Connelly does, the most specific name counts when the object stands close to that
    // place, and otherwise the named place it stands nearest to. An object whose path names no place, or that stands
    // far from every place its path names, takes the nearest place.
    public sealed class ScenePlaces {
        // Beyond this distance, in metres, from every point of a place its path names, that name does not count: a
        // name the game copied from elsewhere, such as Whittier Haywood Road Bridge at Nantahala.
        public const double NameReach = 3000;
        // Within this distance of a place its path names, an object belongs there, whatever else its path names.
        public const double Close = 1500;
        const string Scenery = "World/Large Scenery/", Roads = "World/Roads ", MapLabels = "World/Map Labels/", StationIcons = "World/Map Labels/Station Icons/";
        static readonly Regex Word = new Regex(@"[A-Za-z0-9]+"), StationSuffix = new Regex(@"\s+(Station(\s+Icon)?|Depot)\s*\z", RegexOptions.IgnoreCase), Whitespace = new Regex(@"\s+");
        readonly List<ScenePlace> places = new List<ScenePlace>();
        public IReadOnlyList<ScenePlace> All => places;

        // The words of a name, lower case, without apostrophes: Barker's and Barkers are the same word.
        public static string[] Tokens(string text) =>
            text == null ? new string[0] : Word.Matches(text.Replace("'", "").Replace("’", "")).Cast<Match>().Select(m => m.Value.ToLowerInvariant()).ToArray();
        static bool Finite(double[] at) => at != null && at.Length >= 2 && !Double.IsNaN(at[0]) && !Double.IsNaN(at[1]) && !Double.IsInfinity(at[0]) && !Double.IsInfinity(at[1]);

        public static ScenePlaces From(IEnumerable<PlaceSource> sources) {
            var found = new ScenePlaces();
            // A folder or a board names its place once per object in it; the place stands at the middle of them.
            var named = (sources ?? Enumerable.Empty<PlaceSource>()).Where(s => s != null && Tokens(s.Name).Length != 0)
                .GroupBy(s => new { s.Kind, Key = String.Join(" ", Tokens(s.Name)) })
                .Select(g => new { g.Key.Kind, Name = Whitespace.Replace(g.First().Name, " ").Trim(), Points = g.Where(s => Finite(s.At)).Select(s => s.At).ToArray() })
                .OrderBy(g => g.Kind).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var source in named) {
                var tokens = Tokens(source.Name);
                var place = found.places.FirstOrDefault(p => p.tokens.Any(t => t.SequenceEqual(tokens))) ?? found.Holder(tokens, source.Kind);
                if (place == null) found.places.Add(place = new ScenePlace(source.Name, source.Kind));
                if (!place.tokens.Any(t => t.SequenceEqual(tokens))) { place.tokens.Add(tokens); place.names.Add(source.Name); }
                // A Loc: board stands at each end of its place, so each board is a point; every other source is one point.
                if (source.Kind == PlaceSourceKind.LocBoard) place.points.AddRange(source.Points);
                else if (source.Points.Length != 0) place.points.Add(new[] { Median(source.Points.Select(p => p[0])), Median(source.Points.Select(p => p[1])) });
            }
            return found;
        }
        // The more trusted place whose words a name holds, or that holds the words of the name: the one that shares
        // the most words, or none when two share as many.
        ScenePlace Holder(string[] tokens, PlaceSourceKind kind) {
            var candidates = places.Where(p => p.From < kind).Select(p => new { Place = p, Shared = Contains(tokens, p.tokens[0]) ? p.tokens[0].Length : Contains(p.tokens[0], tokens) ? tokens.Length : 0 })
                .Where(c => c.Shared != 0).OrderByDescending(c => c.Shared).Take(2).ToArray();
            return candidates.Length == 0 || (candidates.Length == 2 && candidates[0].Shared == candidates[1].Shared) ? null : candidates[0].Place;
        }
        static bool Contains(string[] all, string[] some) => some.Length != 0 && some.All(all.Contains);
        static double Median(IEnumerable<double> values) {
            var s = values.OrderBy(v => v).ToArray();
            return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
        }

        // The place a text names: the place whose name, as whole words in a row, is the longest one in the text.
        public ScenePlace Named(string text) => Match(Tokens(text), out _, out _);
        ScenePlace Match(string[] words, out int at, out int length) {
            ScenePlace best = null; at = -1; length = 0;
            foreach (var place in places)
                foreach (var name in place.tokens) {
                    if (name.Length <= length || name.Length > words.Length) continue;
                    for (var i = 0; i + name.Length <= words.Length; i++) {
                        var k = 0;
                        while (k < name.Length && words[i + k] == name[k]) k++;
                        if (k == name.Length) { best = place; at = i; length = name.Length; break; }
                    }
                }
            return best;
        }
        // A name without the places in it, for the name of a kind: Bryson Coal Dealer is a Coal Dealer.
        public string StripPlaces(string name) {
            if (String.IsNullOrEmpty(name)) return name;
            for (var guard = 0; guard < 4; guard++) {
                // Apostrophes are kept out of the words: match on the text without them, cut in the text with them.
                var plain = name.Replace("'", "").Replace("’", "");
                var words = Word.Matches(plain).Cast<Match>().ToArray();
                if (Match(words.Select(m => m.Value.ToLowerInvariant()).ToArray(), out var at, out var length) == null) return name;
                name = (plain.Substring(0, words[at].Index) + plain.Substring(words[at + length - 1].Index + words[at + length - 1].Length)).Trim();
            }
            return name;
        }

        public double Distance(ScenePlace place, double[] at) =>
            place == null || !Finite(at) || place.points.Count == 0 ? Double.PositiveInfinity : place.points.Min(p => Math.Sqrt((p[0] - at[0]) * (p[0] - at[0]) + (p[1] - at[1]) * (p[1] - at[1])));
        public ScenePlace Nearest(double[] at) {
            if (!Finite(at)) return null;
            ScenePlace best = null; var distance = Double.PositiveInfinity;
            foreach (var place in places) { var d = Distance(place, at); if (d < distance) { distance = d; best = place; } }
            return best;
        }
        // The places the hierarchy path names, the object's own name first and then each object above it.
        public IReadOnlyList<ScenePlace> NamedInPath(string path) {
            var found = new List<ScenePlace>();
            if (path == null) return found;
            var segments = path.Split('/');
            for (var i = segments.Length - 1; i >= 0; i--) { var place = Named(segments[i]); if (place != null && !found.Contains(place)) found.Add(place); }
            return found;
        }
        // The place of an object at a hierarchy path that stands at map x/z, or null without a place and a position:
        // of the places its path names, the most specific one it stands close to, else the one it stands nearest to
        // within reach; else the nearest place of all. Without a position, or for a named place without one, the
        // most specific name decides.
        public ScenePlace Of(string path, double[] at) {
            var named = NamedInPath(path);
            if (named.Count != 0 && (!Finite(at) || named[0].points.Count == 0)) return named[0];
            var near = named.FirstOrDefault(p => Distance(p, at) <= Close) ?? named.Where(p => Distance(p, at) <= NameReach).OrderBy(p => Distance(p, at)).FirstOrDefault();
            return near ?? Nearest(at) ?? named.FirstOrDefault();
        }

        // The places one scene object names by where it stands in the hierarchy and by its own name: a folder of a
        // place below World/Large Scenery (a folder named as a structure, such as Almond Bridge or Alarka Signs, is
        // none), a Roads folder, a station map icon, a map label and a Loc: board.
        public static IEnumerable<PlaceSource> SourcesOf(string path, double[] at) {
            if (path == null) yield break;
            var name = path.Substring(path.LastIndexOf('/') + 1);
            if (name.StartsWith(SceneClassification.LocPrefix, StringComparison.Ordinal)) { yield return new PlaceSource(PlaceSourceKind.LocBoard, name.Substring(SceneClassification.LocPrefix.Length).Trim(), at); yield break; }
            if (path.StartsWith(StationIcons, StringComparison.Ordinal)) {
                if (path.IndexOf('/', StationIcons.Length) < 0) yield return new PlaceSource(PlaceSourceKind.StationMapIcon, StationSuffix.Replace(name, ""), at);
            } else if (path.StartsWith(MapLabels, StringComparison.Ordinal)) { if (!(path + "/").StartsWith(StationIcons, StringComparison.Ordinal)) yield return new PlaceSource(PlaceSourceKind.MapLabel, First(path, MapLabels.Length), at); }
            else if (path.StartsWith(Scenery, StringComparison.Ordinal)) {
                var folder = First(path, Scenery.Length);
                if (path.Length > Scenery.Length + folder.Length && !SceneClassification.NamesStructure(folder)) yield return new PlaceSource(PlaceSourceKind.SceneFolder, folder, at);
            } else if (path.StartsWith(Roads, StringComparison.Ordinal)) yield return new PlaceSource(PlaceSourceKind.RoadsFolder, First(path, Roads.Length), at);
        }
        static string First(string path, int from) { var slash = path.IndexOf('/', from); return slash < 0 ? path.Substring(from) : path.Substring(from, slash - from); }
    }
}
