// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// The Saved rule, the canonical order a writing Save produces, the order in which the engine processes modifications, and
// the guard against a scene patch file changed outside scene patch editing.
static class CanonicalSaveTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c"), Q = new TargetIdentity("s", "q", "c");
    static Placement PlacementAt(double x, double y = 2, double z = 3) => new Placement(new[] { x, y, z }, new[] { 0.0, 0.0, 0.0, 1.0 });
    static string Mod(string id, string path, string position, string deps = "[]") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":" + position + ",\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + "}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    sealed class World {
        public Scene Scene = new Scene(); public Box Box = new Box { Position = new[] { -93.0, 8.0, 9.0 }, Rotation = new[] { 0.0, 1.0, 0.0, 0.0 } };
        public Box Other = new Box { Position = new[] { -50.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        public Box Third = new Box { Position = new[] { -20.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        public Store Store = new Store(); public NativeMapEditor MapEditor = new NativeMapEditor(); public PatchEditingSession Session;
        public ScenePatchFileResult Applied;
        public World(string patch) {
            Scene.Objects["p"] = new List<object> { Box }; Scene.Objects["q"] = new List<object> { Other }; Scene.Objects["r"] = new List<object> { Third };
            Store.Patch = patch;
            if (patch != null) Store.Info = Store.Info.Replace("\"rr/game >=0.6.2\"", "\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.5.0\"");
            var engine = new PatchEngine(Scene);
            if (patch != null) Applied = engine.Apply(patch, "author/pack");
            Session = new PatchEditingSession(engine, MapEditor, Store);
            MapEditor.Session = Session;
        }
        public bool PatchOutstanding => Session.Outstanding(MapEditor.NativeDirty).Contains(PatchEditingSession.PatchData);
    }
    public static void Run() {
        ReadWithoutEditIsSaved();
        EqualValueInAnotherLiteralIsSaved();
        EditAndInverseAreSavedAndAnEditIsOutstanding();
        WithoutScenePatchFileSavedWhileEmpty();
        SaveWritesCanonicalOrderByteForByte();
        EveryKnownObjectHasItsCanonicalOrder();
        UnchangedNumbersKeepTheirLiteralInsideAnEditedMember();
        EngineOrderIsIndependentOfTextOrder();
        ExternalChangeGuard();
        DependenciesCompareAsSortedLists();
        EqualContentOfTexts();
        SurrogatePairsSortByTheirCodeUnits();
        Console.WriteLine("PASS: Saved rule, canonical Save byte for byte, order-independent application and the external-change guard");
    }

    // A scene patch file in another member order and spacing than the canonical one, read and not edited, is Saved, and a Save writes nothing.
    static void ReadWithoutEditIsSaved() {
        var text = "{ \"modifications\" : {\n\t\"z\":{\"dependsOn\":[],  \"placement\":{\"rotation\":[0,0,0,1],\"position\":[160,2,3],\"mode\":\"fixed\"},\"target\":{\"component\":\"c\",\"path\":\"q\",\"scene\":\"s\"}},\n"
            + "\t\"a\":{\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[],\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"}} },\n \"sourceMap\":{\"mapId\":\"map\"}, \"formatVersion\":1 }";
        var w = new World(text);
        Assert(w.Session.Blocked == null && w.Box.X == 12 && w.Other.X == 60, "The reordered scene patch file applies: " + w.Session.Blocked);
        Assert(!w.PatchOutstanding && !w.MapEditor.IsDirty, "A reordered and re-spaced scene patch file that is read and not edited is Saved");
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 0 && ReferenceEquals(w.Store.Patch, text), "A Save with no outstanding patch change writes nothing and leaves the file's bytes unchanged");
    }

    // The file has 12; the edit writes 12.0. The values are equal, so the scene patch file stays Saved and a following Save writes nothing.
    static void EqualValueInAnotherLiteralIsSaved() {
        var text = Doc(Mod("move-p", "p", "[112,12,3]"));
        var w = new World(text);
        var same = w.Session.Edit(P, PlacementAt(112.0, 12.0, 3.0), null);
        Assert(same.Applied && !w.PatchOutstanding && w.Box.Position[1] == 12, "An edit that sets a number to the value it already has leaves the scene patch file Saved: " + same.Error);
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 0 && ReferenceEquals(w.Store.Patch, text), "The following Save writes nothing");
        // One changed coordinate is a change; the coordinates it leaves at their values keep their source literals.
        var literal = new World(Doc(Mod("move-p", "p", "[112,1.20E1,3.0]")));
        Assert(literal.Session.Edit(P, PlacementAt(130, 12, 3), null).Applied && literal.PatchOutstanding && literal.MapEditor.Save() == null && literal.Store.PatchWrites == 1, "A changed coordinate is outstanding and saved");
        var position = literal.Store.Patch.Substring(literal.Store.Patch.IndexOf("\"position\"", StringComparison.Ordinal));
        Assert(position.StartsWith("\"position\": [\n          130.0,\n          1.20E1,\n          3.0\n        ]", StringComparison.Ordinal), "Save keeps the source literal of every number the edit does not replace: " + position.Substring(0, 80));
    }

    static void EditAndInverseAreSavedAndAnEditIsOutstanding() {
        var text = Doc(Mod("move-p", "p", "[112,2,3]"));
        var w = new World(text);
        Assert(w.Session.Edit(P, PlacementAt(130), null).Applied && w.PatchOutstanding && w.MapEditor.IsDirty, "An edit is outstanding");
        Assert(w.Session.Edit(P, PlacementAt(112), null).Applied && !w.PatchOutstanding, "An edit followed by its inverse is Saved");
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 0 && ReferenceEquals(w.Store.Patch, text) && !w.MapEditor.IsDirty, "Saving after the inverse writes no scene patch file");
        Assert(w.Session.Edit(P, PlacementAt(140), null).Applied && w.PatchOutstanding && w.MapEditor.Save() == null && w.Store.PatchWrites == 1 && !w.PatchOutstanding && !w.MapEditor.IsDirty, "An outstanding edit is written by the Save, and the scene patch file is Saved again");
        // Removing a modification and adding it back with the same content is Saved too, wherever its key now stands.
        var two = new World(Doc(Mod("move-p", "p", "[112,2,3]") + "," + Mod("move-q", "q", "[160,2,3]")));
        Assert(two.Session.Remove(P).Applied && two.PatchOutstanding, "A removal is outstanding");
        two.MapEditor.Undo();
        Assert(!two.PatchOutstanding && two.Box.X == 12, "Undo of the removal restores the modification under its key, and the scene patch file is Saved");
    }

    static void WithoutScenePatchFileSavedWhileEmpty() {
        var w = new World(null);
        Assert(!w.PatchOutstanding && !w.MapEditor.IsDirty, "Without a scene patch file at load, the patch is Saved while it has no modification");
        Assert(w.Session.Edit(P, PlacementAt(112), null).Applied && w.PatchOutstanding, "The first modification is outstanding");
        Assert(w.Session.Remove(P).Applied && !w.PatchOutstanding && w.MapEditor.Save() == null && w.Store.Patch == null, "Without a modification it is Saved again and no file is written");
    }

    // A scene patch file written in reverse order, with a literal in an untouched member of the modification that is edited and a nested
    // unknown object. Reading does not reorder it; the Save that writes it produces the canonical order.
    static void SaveWritesCanonicalOrderByteForByte() {
        var source = """
            {
              "zeta": { "b": 1, "a": { "y": 2.0, "x": [ 3, { "q": 1e0, "p": 2 } ] } },
              "modifications": {
                "move-q": {
                  "dependsOn": [],
                  "placement": { "rotation": [0, 0, 0, 1], "position": [160, 2, 3], "mode": "fixed" },
                  "target": { "component": "c", "path": "q", "scene": "s" }
                },
                "move-p": {
                  "active": true,
                  "dependsOn": ["move-q", "Base"],
                  "placement": { "rotation": [0, 0, 0, 1], "position": [112, 12.50, 3], "mode": "fixed" },
                  "target": { "component": "c", "path": "p", "scene": "s" }
                },
                "Base": {
                  "placement": { "position": [80, 0, 0], "mode": "fixed", "rotation": [0, 0, 0, 1] },
                  "dependsOn": [],
                  "target": { "path": "r", "scene": "s", "component": "c" }
                }
              },
              "sourceMap": { "futureMap": true, "mapId": "map" },
              "formatVersion": 1
            }
            """.Replace("\r\n", "\n");
        var expected = """
            {
              "formatVersion": 1,
              "sourceMap": {
                "mapId": "map",
                "futureMap": true
              },
              "modifications": {
                "Base": {
                  "target": {
                    "scene": "s",
                    "path": "r",
                    "component": "c"
                  },
                  "placement": {
                    "mode": "fixed",
                    "position": [
                      80,
                      0,
                      0
                    ],
                    "rotation": [
                      0,
                      0,
                      0,
                      1
                    ]
                  },
                  "dependsOn": []
                },
                "move-p": {
                  "target": {
                    "scene": "s",
                    "path": "p",
                    "component": "c"
                  },
                  "placement": {
                    "mode": "fixed",
                    "position": [
                      112,
                      12.50,
                      3
                    ],
                    "rotation": [
                      0,
                      0,
                      0,
                      1
                    ]
                  },
                  "dependsOn": [
                    "Base",
                    "move-q"
                  ],
                  "active": false
                },
                "move-q": {
                  "target": {
                    "scene": "s",
                    "path": "q",
                    "component": "c"
                  },
                  "placement": {
                    "mode": "fixed",
                    "position": [
                      160,
                      2,
                      3
                    ],
                    "rotation": [
                      0,
                      0,
                      0,
                      1
                    ]
                  },
                  "dependsOn": []
                }
              },
              "zeta": {
                "a": {
                  "x": [
                    3,
                    {
                      "p": 2,
                      "q": 1e0
                    }
                  ],
                  "y": 2.0
                },
                "b": 1
              }
            }

            """.Replace("\r\n", "\n");
        var w = new World(source);
        Assert(w.Session.Blocked == null && w.Applied.Outcomes.Values.All(o => o.Applied) && w.Box.X == 12 && w.Other.X == 60 && w.Third.X == -20, "The scene patch file in reverse order applies: " + w.Session.Blocked);
        Assert(!w.PatchOutstanding && w.MapEditor.Save() == null && ReferenceEquals(w.Store.Patch, source), "Reading the scene patch file does not rewrite it in canonical order");
        var edit = w.Session.Edit(P, null, false);
        Assert(edit.Applied && !w.Box.Active && w.PatchOutstanding, "The modification is edited: " + edit.Error);
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 1, "The Save writes the scene patch file");
        Assert(w.Store.Patch == expected, "The saved file is the canonical text, byte for byte:\n" + w.Store.Patch);
        Assert(System.Text.Encoding.UTF8.GetBytes(w.Store.Patch).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(expected)), "The bytes are equal");
        // The canonical text is a fixed point: reading it, editing back and forth and saving again writes the same bytes.
        var again = new World(expected);
        Assert(again.Session.Edit(P, null, true).Applied && again.Session.Edit(P, null, false).Applied && !again.PatchOutstanding, "The canonical text read back and edited to the same content is Saved");
        Assert(again.Session.Edit(Q, PlacementAt(160, 2, 4), null).Applied && again.Session.Edit(Q, PlacementAt(160, 2, 3), null).Applied && again.MapEditor.Save() == null && again.Store.PatchWrites == 0, "No further write follows");
    }

    // Every object with a fixed member order, written in reverse. An unknown object inside a coverage entry is ordered as
    // unknown content, not as a span, and a dependsOn that is not an array of strings keeps its source form, items included.
    // The modification that holds them targets no loaded object and
    // names a relationship kind this version does not execute (crossingSignal), so it is preserved; the Save of an edit beside it orders it.
    static void EveryKnownObjectHasItsCanonicalOrder() {
        var source = """
            {
              "modifications": {
                "wide": {
                  "active": false,
                  "dependsOn": ["move-p"],
                  "placement": { "rotation": [0, 0, 0, 1], "offset": [1, 2, 3], "track": { "distance": 5.0, "end": "a", "segment": "S1" }, "mode": "trackFollowing" },
                  "relationships": {
                    "crossingSignal": { "z": 1 },
                    "crossingEquipment": { "gate-b": { "z": 1, "a": 2 }, "gate-a": {} },
                    "crossingCoverage": {
                      "additions": { "siding": { "island": {}, "approachB": {}, "approachA": { "to": { "distance": 2, "end": "a", "segment": "S" }, "from": { "distance": 1, "end": "a", "segment": "S" } } } },
                      "circuits": { "Pzz": { "detach": true }, "Paa": { "detach": true, "island": {}, "bell": { "to": 1, "from": 2, "a": 3 } } }
                    },
                    "trackMarker": { "distance": 1, "end": "b", "segment": "S2" }
                  },
                  "target": { "structure": "x-v1", "component": "c", "path": "absent", "scene": "s" }
                },
                "odd": { "dependsOn": [{ "z": 1, "a": 2 }, "wide", "move-p"], "target": { "scene": "s", "path": "absent", "component": "c" }, "active": true },
                "move-p": { "dependsOn": [], "placement": { "rotation": [0, 0, 0, 1], "position": [112, 2, 3], "mode": "fixed" }, "target": { "component": "c", "path": "p", "scene": "s" } }
              },
              "sourceMap": { "mapId": "map" },
              "formatVersion": 1
            }
            """.Replace("\r\n", "\n");
        var expected = """
            {
              "formatVersion": 1,
              "sourceMap": {
                "mapId": "map"
              },
              "modifications": {
                "move-p": {
                  "target": {
                    "scene": "s",
                    "path": "p",
                    "component": "c"
                  },
                  "placement": {
                    "mode": "fixed",
                    "position": [
                      130.0,
                      2,
                      3
                    ],
                    "rotation": [
                      0,
                      0,
                      0,
                      1
                    ]
                  },
                  "dependsOn": []
                },
                "odd": {
                  "target": {
                    "scene": "s",
                    "path": "absent",
                    "component": "c"
                  },
                  "dependsOn": [
                    {
                      "z": 1,
                      "a": 2
                    },
                    "wide",
                    "move-p"
                  ],
                  "active": true
                },
                "wide": {
                  "target": {
                    "scene": "s",
                    "path": "absent",
                    "component": "c",
                    "structure": "x-v1"
                  },
                  "relationships": {
                    "trackMarker": {
                      "segment": "S2",
                      "end": "b",
                      "distance": 1
                    },
                    "crossingCoverage": {
                      "circuits": {
                        "Paa": {
                          "island": {},
                          "detach": true,
                          "bell": {
                            "a": 3,
                            "from": 2,
                            "to": 1
                          }
                        },
                        "Pzz": {
                          "detach": true
                        }
                      },
                      "additions": {
                        "siding": {
                          "approachA": {
                            "from": {
                              "segment": "S",
                              "end": "a",
                              "distance": 1
                            },
                            "to": {
                              "segment": "S",
                              "end": "a",
                              "distance": 2
                            }
                          },
                          "island": {},
                          "approachB": {}
                        }
                      }
                    },
                    "crossingEquipment": {
                      "gate-a": {},
                      "gate-b": {
                        "a": 2,
                        "z": 1
                      }
                    },
                    "crossingSignal": {
                      "z": 1
                    }
                  },
                  "placement": {
                    "mode": "trackFollowing",
                    "track": {
                      "segment": "S1",
                      "end": "a",
                      "distance": 5.0
                    },
                    "offset": [
                      1,
                      2,
                      3
                    ],
                    "rotation": [
                      0,
                      0,
                      0,
                      1
                    ]
                  },
                  "dependsOn": [
                    "move-p"
                  ],
                  "active": false
                }
              }
            }

            """.Replace("\r\n", "\n");
        var w = new World(source);
        Assert(w.Session.Blocked == null && w.Applied.Outcomes["move-p"].Applied && !w.Applied.Outcomes["wide"].Applied && w.Applied.Outcomes["wide"].MapEditorOnly, "The unsupported modification is not executed: " + w.Applied.Outcomes["wide"].Error);
        Assert(w.Session.Edit(P, PlacementAt(130), null).Applied && w.MapEditor.Save() == null && w.Store.PatchWrites == 1, "The modification beside it is edited and saved");
        Assert(w.Store.Patch == expected, "Every known object is written in its canonical order, byte for byte:\n" + w.Store.Patch);
    }

    static void UnchangedNumbersKeepTheirLiteralInsideAnEditedMember() {
        // The dependencies are edited by hand between two Saves in another order and literal; an edit of another member keeps them.
        var w = new World(Doc(Mod("b", "q", "[1.600E2,2,3]") + "," + Mod("a", "p", "[112,2,3]", "[\"b\"]")));
        Assert(w.Session.Edit(P, PlacementAt(112, 2, 5), null).Applied && w.MapEditor.Save() == null, "An edit of one modification is saved");
        Assert(w.Store.Patch.Contains("1.600E2") && w.Store.Patch.IndexOf("\"a\":", StringComparison.Ordinal) < w.Store.Patch.IndexOf("\"b\":", StringComparison.Ordinal), "The untouched modification keeps its literal and the keys are written in ordinal order:\n" + w.Store.Patch);
    }

    // Modifications that dependsOn does not order run in ordinal key order, so the canonical form and a form in another key and
    // member order apply the same operations in the same order and reach the same scene state.
    static void EngineOrderIsIndependentOfTextOrder() {
        string Member(string name, string value) => "\"" + name + "\":" + value;
        string Item(string id, string path, double x, string deps, bool reversed) {
            var members = new[] { Member("target", reversed ? "{\"component\":\"c\",\"path\":\"" + path + "\",\"scene\":\"s\"}" : "{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"}"),
                Member("placement", reversed ? "{\"rotation\":[0,0,0,1],\"position\":[" + x + ",2,3],\"mode\":\"fixed\"}" : "{\"mode\":\"fixed\",\"position\":[" + x + ",2,3],\"rotation\":[0,0,0,1]}"),
                Member("dependsOn", deps) };
            return "\"" + id + "\":{" + String.Join(",", reversed ? members.Reverse() : members) + "}";
        }
        string ScenePatchFile(bool reversed) {
            // Two modifications of the same object p, one of q that depends on a later key, and one of r.
            var items = new[] { Item("B-upper", "r", 190, "[]", reversed), Item("a-pose", "p", 112, "[]", reversed), Item("b-pose", "p", 150, "[]", reversed),
                Item("c-dependent", "q", 160, reversed ? "[\"z-base\",\"a-pose\"]" : "[\"a-pose\",\"z-base\"]", reversed), Item("z-base", "r", 180, "[]", reversed) };
            var modifications = "\"modifications\":{" + String.Join(",", reversed ? items.Reverse() : items) + "}";
            return reversed ? "{" + modifications + ",\"sourceMap\":{\"mapId\":\"map\"},\"formatVersion\":1}" : "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"}," + modifications + "}";
        }
        var runs = new List<(string order, string state)>();
        foreach (var reversed in new[] { false, true }) {
            var w = new World(null);
            var result = new PatchEngine(w.Scene).Apply(ScenePatchFile(reversed));
            Assert(result.Error == null && result.Outcomes.Count == 5 && result.Outcomes.Values.All(o => o.Applied), "Every modification applies" + (reversed ? " in reverse text order: " : ": ") + result.Error);
            var order = String.Join(",", w.Scene.MutationLog.Select(t => ReferenceEquals(t, w.Box) ? "p" : ReferenceEquals(t, w.Other) ? "q" : "r"));
            runs.Add((order, w.Box.X + "|" + w.Other.X + "|" + w.Third.X));
        }
        // B-upper, a-pose, b-pose, then c-dependent after its dependencies a-pose (done) and z-base, which therefore runs before it.
        Assert(runs[0].order == "r,p,p,r,q" && runs[0].state == "50|60|80", "The canonical scene patch file applies in ordinal key order with dependencies first: " + runs[0].order + " " + runs[0].state);
        Assert(runs[1] == runs[0], "A scene patch file in another key and member order applies the same operations and produces the same scene state: " + runs[1].order + " " + runs[1].state);
    }

    static void ExternalChangeGuard() {
        var canonical = Doc(Mod("a", "p", "[112,2,3]") + "," + Mod("z", "q", "[160,2,3]"));
        var reordered = "{\n  \"modifications\": {\n    " + Mod("z", "q", "[1.6E2,2.0,3]") + ",\n    " + Mod("a", "p", "[112,2,3]") + "\n  },\n  \"formatVersion\": 1,\n  \"sourceMap\": { \"mapId\": \"map\" }\n}\n";
        // A reorder or re-spacing made outside scene patch editing is not an external change and does not block a Save.
        var w = new World(canonical);
        Assert(w.Session.Edit(P, PlacementAt(130), null).Applied, "An edit is pending");
        w.Store.Patch = reordered;
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 1 && (double)JObject.Parse(w.Store.Patch)["modifications"]["a"]["placement"]["position"][0] == 130 && !w.MapEditor.IsDirty, "A file reordered and re-spaced outside scene patch editing does not block the Save");
        // Each external change refuses the Save and leaves the file as it is.
        var cases = new (string scenario, string load, string outside)[] {
            ("a changed value", canonical, canonical.Replace("[160,2,3]", "[161,2,3]")),
            ("a removed modification", canonical, Doc(Mod("a", "p", "[112,2,3]"))),
            ("a file that cannot be parsed", canonical, canonical.Substring(0, canonical.Length - 3)),
            ("a file with a repeated property name", canonical, canonical.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1")),
            ("a file deleted since load", canonical, null),
            ("a file created after a load that found none", null, canonical),
        };
        foreach (var (scenario, load, outside) in cases) {
            var g = new World(load);
            Assert(g.Session.Edit(P, PlacementAt(130), null).Applied, scenario + ": an edit is pending");
            g.Store.Patch = outside;
            var message = g.MapEditor.Save();
            Assert(message != null && message.Contains("changed outside scene patch editing") && message.Contains("outstanding: ") && message.Contains(PatchEditingSession.PatchData), scenario + ": the Save refuses to overwrite the external change: " + message);
            Assert(ReferenceEquals(g.Store.Patch, outside) && g.Store.PatchWrites == 0 && g.MapEditor.IsDirty && g.Box.X == 30, scenario + ": the file stays as it is and the edit stays in memory");
        }
    }

    // Equal content of two texts: numbers by the value they denote as a double, members without regard to their order.
    static void EqualContentOfTexts() {
        string Text(string members) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}," + members + "}";
        bool Same(string a, string b) => ScenePatchFile.SameContent(Text(a), Text(b)) && ScenePatchFile.SameContent(Text(b), Text(a));
        Assert(Same("\"n\":1e2", "\"n\":100") && Same("\"n\":1E+2", "\"n\":100.0"), "1e2 equals 100");
        Assert(Same("\"n\":-0", "\"n\":0") && Same("\"n\":-0.0", "\"n\":0"), "-0 equals 0");
        Assert(Same("\"n\":9007199254740993", "\"n\":9007199254740992"), "Integers that differ only beyond double precision are equal");
        Assert(Same("\"n\":123456789012345678901234567890", "\"n\":123456789012345678901234567891") && Same("\"n\":123456789012345678901234567890", "\"n\":1.2345678901234568e29"), "So are integers beyond the 64-bit range, and the same value in exponent form");
        Assert(!Same("\"n\":9007199254740992", "\"n\":9007199254740994") && !Same("\"n\":100", "\"n\":101") && !Same("\"n\":1", "\"n\":\"1\"") && !Same("\"n\":0", "\"n\":false") && !Same("\"n\":0", "\"n\":null"), "Another value, a string, a boolean and null are other content");
        // A key outside the Basic Multilingual Plane and a key in U+E000 to U+FFFF, in either order.
        Assert(Same("\"\uD83D\uDE00\":1,\"\uE000\":2", "\"\uE000\":2,\"\uD83D\uDE00\":1") && !Same("\"\uD83D\uDE00\":1,\"\uE000\":2", "\"\uD83D\uDE00\":2,\"\uE000\":1"), "Member order does not count, whatever the keys; their values do");
        Assert(Same("\"list\":[1,2]", "\"list\":[1.0,2e0]") && !Same("\"list\":[1,2]", "\"list\":[2,1]") && !Same("\"dependsOn\":[\"a\",\"b\"]", "\"dependsOn\":[\"b\",\"a\"]"), "An array outside a modification's dependsOn matches in order");
        Assert(ScenePatchFile.SameContent(null, null) && !ScenePatchFile.SameContent(null, Text("\"n\":1")) && ScenePatchFile.SameContent("{broken", "{broken") && !ScenePatchFile.SameContent("{broken", "{ broken"), "A missing text equals a missing text, and an unparseable text only itself");
    }

    // Ordinal order compares UTF-16 code units: a character outside the Basic Multilingual Plane sorts by its surrogates, before U+E000 to U+FFFF.
    static void SurrogatePairsSortByTheirCodeUnits() {
        var w = new World("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"\uFFFD\":3,\"\uE000\":2,\"\uD83D\uDE00\":1,\"_\":0,\"Z\":0,\"a\":0,\"B\":0,\"ab\":0,\"modifications\":{" + Mod("move-p", "p", "[112,2,3]") + "}}");
        Assert(w.Session.Blocked == null && w.Session.Edit(P, PlacementAt(130), null).Applied && w.MapEditor.Save() == null && w.Store.PatchWrites == 1, "The scene patch file with unknown root members is edited and saved: " + w.Session.Blocked);
        var names = JObject.Parse(w.Store.Patch).Properties().Select(p => p.Name).ToArray();
        Assert(names.SequenceEqual(new[] { "formatVersion", "sourceMap", "modifications", "B", "Z", "_", "a", "ab", "\uD83D\uDE00", "\uE000", "\uFFFD" }), "Unknown members are written in ordinal order: B before Z before _ before a, a prefix first, a surrogate pair before U+E000: " + String.Join(" ", names.Select(n => String.Join("", n.Select(c => c < 128 ? c.ToString() : "\\u" + ((int)c).ToString("X4"))))));
    }

    // dependsOn values have no order, and duplicates count: ["a","b"] equals ["b","a"], ["a","a"] does not equal ["a"].
    static void DependenciesCompareAsSortedLists() {
        string With(string deps) => Doc(Mod("a", "p", "[112,2,3]") + "," + Mod("b", "q", "[160,2,3]") + "," + Mod("c", "r", "[80,2,3]", deps));
        var w = new World(With("[\"a\",\"b\"]"));
        Assert(w.Session.Edit(P, PlacementAt(130), null).Applied, "An edit is pending");
        w.Store.Patch = With("[\"b\",\"a\"]");
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 1, "Dependencies in another order are equal content, so the Save is not blocked");
        var g = new World(With("[\"a\",\"a\"]"));
        Assert(g.Third.X == -20 && g.Applied.Outcomes["c"].Applied, "A repeated dependency counts as a single one for the engine");
        Assert(g.Session.Edit(P, PlacementAt(130), null).Applied, "An edit is pending");
        var single = With("[\"a\"]"); g.Store.Patch = single;
        var message = g.MapEditor.Save();
        Assert(message != null && message.Contains("changed outside scene patch editing") && ReferenceEquals(g.Store.Patch, single), "A dependency list without the duplicate is other content: " + message);
        // A writing Save orders the values and keeps the duplicate.
        var kept = new World(With("[\"b\",\"a\",\"b\"]"));
        Assert(kept.Session.Edit(P, PlacementAt(130), null).Applied && kept.MapEditor.Save() == null, "The scene patch file is saved");
        Assert(String.Join(",", JObject.Parse(kept.Store.Patch)["modifications"]["c"]["dependsOn"].Select(d => (string)d)) == "a,b,b", "A writing Save writes the dependencies in ordinal order with duplicates kept: " + kept.Store.Patch);
    }
}
