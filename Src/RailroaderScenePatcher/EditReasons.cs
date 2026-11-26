// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;

namespace RailroaderScenePatcher {
    // Presentation of the inventory's pattern refusals. Runtime checks retain their diagnostic detail; the window
    // names the same short reason wherever it explains an unavailable edit.
    internal static class EditReasons {
        internal const string Structure = "Its structure isn't supported", State = "Its state can't be read",
            Scene = "Its scene isn't loaded", Members = "Required game support is unavailable",
            References = "Its marker references can't be read", Family = "This family has no supported edits",
            Fixed = "Uses fixed placement", Crossing = "Edit it on its crossing controller",
            Starter = "Has no effect on where cars start", Replacement = "Only the Generic track marker family",
            NoReader = "No new-game car placement uses it", SeveralReaders = "Several new-game car placements use it",
            Setups = "The new-game setups can't be read", CurrentId = "Its current ID can't be read",
            UniqueId = "No other object holds its marker ID", Camera = "The game switches it by camera distance";

        internal static string Short(string reason) => Describe(reason, Structure);
        // Other failures, such as an invalid entered value or incomplete cleanup, keep their own correction text.
        internal static string Message(string reason) => Describe(reason, reason);
        static string Describe(string reason, string fallback) {
            if (reason == null) return null;
            var text = CommandBand.Sentence(reason).TrimEnd('.');
            if (Known.Contains(text, StringComparer.Ordinal)) return text;
            bool Has(string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("objects have this hierarchy path") || Has("objects share this hierarchy path")) return CommandBand.SharedPathBlock;
            if (Has("marker reference scan failed") || Has("direct-reference scan is unavailable")) return References;
            if (Has("new-game setups could not be read")) return Setups;
            if (Has("new-game car placements read this marker")) return SeveralReaders;
            if (Has("No new-game car placement in an active setup")) return NoReader;
            if (Has("new-game car placement reads only this marker's track location")) return Starter;
            if (Has("Only objects in the Generic track marker family")) return Replacement;
            if (Has("marker ID could not be read")) return CurrentId;
            if (Has("Only this marker holds marker ID")) return UniqueId;
            if (Has("no implemented bridge-placement capability")) return Structure;
            if (Has("scene isn't loaded") || Has("scene is not loaded") || Has("This object is not loaded") || Has("no implemented") && Has("capability")) return Scene;
            if (Has("parents must be active")) return "Its parent object must be active";
            if (Has("attached objects out of date")) return "Its attached object is out of date";
            if (Has("crossing controller is disabled")) return "Its crossing controller is disabled";
            if (Has("cannot reconfigure the bell")) return "Not supported on this game build";
            if (Has("progression controller are unavailable")) return "Progression data can't be read";
            if (Has("map feature are unavailable")) return "Map feature data can't be read";
            if (Has("can't share control of this object's activity")) return "Progression or a map feature controls it";
            if (Has("culler") && (Has("camera distance") || Has("switches this part"))) return Camera;
            if (Has("track group controls this object's activity")) return CommandBand.GroupEnableReason;
            if (Has("pad's activity follows a track group")) return "Its track group controls its activity";
            if (Has("construction controller switches the bridge's parts on or off regardless")) return CommandBand.ControllerNoPlacement;
            if (Has("This controller switches the bridge's parts on or off as construction progresses")) return CommandBand.ControllerNoActivity;
            if (Has("fixed placement")) return Fixed;
            if (Has("A bell's sound has no direction")) return CommandBand.BellNoRotation;
            if (Has("A crossing pad follows its track")) return CommandBand.PadFollowsTrack;
            if (Has("contains track markers")) return "Edit its track markers one by one";
            if (Has("contains spawn points") || Has("groups that hold a spawn point")) return "Edit its spawn points one by one";
            if (Has("contains a map icon")) return "Edit its map icon separately";
            if (Has("contains a station click spot")) return "Station click spots are native map data";
            if (Has("contains a construction controller")) return "It holds a construction controller";
            if (Has("Use the milepost's own settings")) return "Edit it on its milepost";
            if (Has("Edit this marker through its crossing pad")) return "Edit it on its crossing pad";
            if (Has("set the construction segments on its group")) return "Edit it on its group";
            if (Has("This path names a group, not a part")) return "Edit the whole group instead";
            if (Has("only carries a simple culler")) return "Edit its parts directly";
            if (Has("only holds other groups")) return "Edit its groups instead";
            if (Has("crossing controller's Connections tab") || Has("span's track locations determine its position")) return Crossing;
            if (Has("Objects listed under") || Has("cannot be edited") && reason.StartsWith("Not yet supported: ", StringComparison.Ordinal)) return Family;
            if (Has("Map coordinates are unavailable") || Has("editing is unavailable") || Has("could not be read") || Has("couldn't be read")) return State;
            if (Has("members are unavailable") || Has("type is unavailable") || Has("types are missing") || Has("fields") && Has("changed")
                || Has("is missing") && (Has("Controller.") || Has("construction")) || Has("keys") && Has("are missing")
                || Has("equipment cannot be edited on this game build") || Has("Construction scenery entries unavailable") || Has("Construction scenery targets unavailable")
                || Has("Existing crossing bells are unavailable") || Has("construction controller is unavailable") || Has("log pond") && Has("members") || Has("storage pile") && Has("members")) return Members;
            if (reason.StartsWith("Not editable: ", StringComparison.Ordinal)
                || Has("script") && Has("missing") || Has("rigidbody") || Has("renderer culler must") || Has("enabled circle map mask")
                || Has("object holds no ") || Has("object holds several ") || Has("pad builder has no ")
                || Has("track-owned component") || Has("controls the bridge root's activity") || Has("bridge has no colliders")
                || Has("no text synchronizer") || Has("also has a crossing-pad builder") || Has("Generic track markers are supported")
                || Has("Generic pad markers are supported") || Has("needs a Generic track marker") || Has("not a listed group")
                || Has("not listed as a part") || Has("isn't a station map icon") || Has("has no scene spawn point")
                || Has("holds no circuit") || Has("has no circuits") || Has("holds an empty circuit") || Has("A circuit lacks")
                || Has("span of a circuit has no ID")) return Structure;
            return fallback;
        }

        // Every fixed reason in the ticket. No reason contains a runtime identifier or a family placeholder.
        internal static readonly string[] Known = {
            SupportedEdits.CrossingNoEdit, CommandBand.PadFollowsTrack, CommandBand.ControllerNoPlacement, CommandBand.ControllerNoActivity,
            Fixed, CommandBand.BellNoRotation, Starter, CommandBand.GroupEnableReason, Camera,
            "Progression or a map feature controls it", "Progression data can't be read", "Map feature data can't be read",
            Replacement, CommandBand.SharedPathBlock, CurrentId, UniqueId, Crossing, Scene, State,
            "Edit its track markers one by one", NoReader, SeveralReaders, Setups,
            "Its lights, gates and bells can't be read", CommandBand.NoTrackBinding, References, Members, Structure,
            "Edit its spawn points one by one", "Edit its map icon separately",
            "Station click spots are native map data", "It holds a construction controller", Family,
            "Edit the whole group instead", "Edit its parts directly", "Edit its groups instead",
            "Not supported on this game build", "Its parent object must be active", "Its attached object is out of date",
            "Its crossing controller is disabled", "Edit it on its milepost", "Edit it on its crossing pad",
            "Its track group controls its activity", "Edit it on its group"
        };
    }
}
