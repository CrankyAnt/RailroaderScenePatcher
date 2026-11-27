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
            UniqueId = "No other object holds its marker ID", Camera = "The game switches it by camera distance",
            ParentInactive = "Its parent object must be active", AttachedOutOfDate = "Its attached object is out of date",
            ControllerDisabled = "Its crossing controller is disabled", GameBuild = "Not supported on this game build",
            ProgressionUnread = "Progression data can't be read", MapFeatureUnread = "Map feature data can't be read",
            SharedActivity = "Progression or a map feature controls it", TrackGroupActivity = "Its track group controls its activity",
            TrackMarkers = "Edit its track markers one by one", SpawnPoints = "Edit its spawn points one by one",
            MapIcon = "Edit its map icon separately", StationClickSpots = "Station click spots are native map data",
            ConstructionController = "It holds a construction controller", OnMilepost = "Edit it on its milepost",
            OnCrossingPad = "Edit it on its crossing pad", OnGroup = "Edit it on its group",
            WholeGroup = "Edit the whole group instead", Parts = "Edit its parts directly", Groups = "Edit its groups instead",
            EquipmentUnread = "Its lights, gates and bells can't be read";

        internal static string Short(string reason) => Describe(reason, Structure);
        // Other failures, such as an invalid entered value or incomplete cleanup, keep their own correction text.
        internal static string Message(string reason) => Describe(reason, reason);
        static string Describe(string reason, string fallback) {
            if (reason == null) return null;
            // A refusal of an object the inventory does not edit, whose detail no rule names, is a structure refusal.
            return Matched(reason) ?? (reason.StartsWith(NotEditable, StringComparison.Ordinal) ? Structure : fallback);
        }
        const string NotEditable = "Not editable: ";

        // The short reason a rule names for a refusal text, or null when no rule names one. A structure refusal that a
        // rule names returns Structure; the fallbacks of Short and Message are not rules.
        internal static string Matched(string reason) {
            if (reason == null) return null;
            var text = CommandBand.Sentence(reason).TrimEnd('.');
            if (Known.Contains(text, StringComparer.Ordinal)) return text;
            bool Has(string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("objects have this hierarchy path") || Has("objects share this hierarchy path")) return CommandBand.SharedPathBlock;
            if (Has("marker reference scan failed") || Has("direct-reference scan is unavailable") || Has("direct-reference scan could not read")) return References;
            if (Has("new-game setups could not be read")) return Setups;
            if (Has("new-game car placements read this marker")) return SeveralReaders;
            if (Has("No new-game car placement in an active setup")) return NoReader;
            if (Has("new-game car placement reads only this marker's track location")) return Starter;
            if (Has("Only objects in the Generic track marker family")) return Replacement;
            if (Has("marker ID could not be read")) return CurrentId;
            if (Has("Only this marker holds marker ID")) return UniqueId;
            if (Has("no implemented bridge-placement capability")) return Structure;
            if (Has("scene isn't loaded") || Has("scene is not loaded") || Has("This object is not loaded")) return Scene;
            if (Has("parents must be active")) return ParentInactive;
            if (Has("attached objects out of date")) return AttachedOutOfDate;
            if (Has("crossing controller is disabled")) return ControllerDisabled;
            if (Has("cannot reconfigure the bell")) return GameBuild;
            if (Has("progression controller are unavailable")) return ProgressionUnread;
            if (Has("map feature are unavailable")) return MapFeatureUnread;
            if (Has("can't share control of this object's activity")) return SharedActivity;
            if (Has("culler") && (Has("camera distance") || Has("switches this part"))) return Camera;
            if (Has("track group controls this object's activity")) return CommandBand.GroupEnableReason;
            if (Has("activity follows a track group")) return TrackGroupActivity;
            if (Has("construction controller switches the bridge's parts on or off regardless")) return CommandBand.ControllerNoPlacement;
            if (Has("This controller switches the bridge's parts on or off as construction progresses")) return CommandBand.ControllerNoActivity;
            if (Has("fixed placement")) return Fixed;
            if (Has("A bell's sound has no direction")) return CommandBand.BellNoRotation;
            if (Has("A crossing pad follows its track")) return CommandBand.PadFollowsTrack;
            if (Has("contains track markers")) return TrackMarkers;
            if (Has("contains spawn points") || Has("groups that hold a spawn point")) return SpawnPoints;
            if (Has("contains a map icon")) return MapIcon;
            if (Has("contains a station click spot")) return StationClickSpots;
            if (Has("contains a construction controller")) return ConstructionController;
            if (Has("Use the milepost's own settings")) return OnMilepost;
            if (Has("Edit this marker through its crossing pad")) return OnCrossingPad;
            if (Has("set the construction segments on its group")) return OnGroup;
            if (Has("This path names a group, not a part")) return WholeGroup;
            if (Has("only carries a simple culler")) return Parts;
            if (Has("only holds other groups")) return Groups;
            if (Has("crossing controller's Connections tab") || Has("span's track locations determine its position")) return Crossing;
            if (Has("Objects listed under") || Has("cannot be edited") && reason.StartsWith("Not yet supported: ", StringComparison.Ordinal)) return Family;
            if (Has("Map coordinates are unavailable") || Has("editing is unavailable") || Has("could not be read") || Has("couldn't be read")) return State;
            if (Has("members are unavailable") || Has("type is unavailable") || Has("types are missing") || Has("fields") && Has("changed")
                || Has("is missing") && (Has("Controller.") || Has("construction")) || Has("keys") && Has("are missing")
                || Has("equipment cannot be edited on this game build") || Has("Construction scenery entries unavailable") || Has("Construction scenery targets unavailable")
                || Has("Existing crossing bells are unavailable") || Has("construction controller is unavailable") || Has("log pond") && Has("members") || Has("storage pile") && Has("members")) return Members;
            if (Has("script") && Has("missing") || Has("rigidbody") || Has("renderer culler must") || Has("enabled circle map mask")
                || Has("object holds no ") || Has("object holds several ") || Has("pad builder has no ")
                || Has("track-owned component") || Has("controls the bridge root's activity") || Has("bridge has no colliders")
                || Has("no text synchronizer") || Has("also has a crossing-pad builder") || Has("Generic track markers are supported")
                || Has("Generic pad markers are supported") || Has("needs a Generic track marker") || Has("not a listed group")
                || Has("not listed as a part") || Has("isn't a station map icon") || Has("has no scene spawn point")
                || Has("holds no circuit") || Has("has no circuits") || Has("holds an empty circuit") || Has("A circuit lacks")
                || Has("span of a circuit has no ID") || Has("milepost component") || Has("milepost has no track marker")
                || Has("more than one track marker") || Has("marker has no ID") || Has("consumer is not supported")
                || Has("dependent state is not supported") || Has("can't move that culler") || Has("group of this part is no longer listed") || Has("not part of a mast")) return Structure;
            return null;
        }

        // Every fixed reason in the ticket. No reason contains a runtime identifier or a family placeholder.
        internal static readonly string[] Known = {
            SupportedEdits.CrossingNoActivity, CommandBand.PadFollowsTrack, CommandBand.ControllerNoPlacement, CommandBand.ControllerNoActivity,
            Fixed, CommandBand.BellNoRotation, Starter, CommandBand.GroupEnableReason, Camera,
            SharedActivity, ProgressionUnread, MapFeatureUnread,
            Replacement, CommandBand.SharedPathBlock, CurrentId, UniqueId, Crossing, Scene, State,
            TrackMarkers, NoReader, SeveralReaders, Setups,
            EquipmentUnread, CommandBand.NoTrackBinding, References, Members, Structure,
            SpawnPoints, MapIcon, StationClickSpots, ConstructionController, Family,
            WholeGroup, Parts, Groups,
            GameBuild, ParentInactive, AttachedOutOfDate,
            ControllerDisabled, OnMilepost, OnCrossingPad,
            TrackGroupActivity, OnGroup
        };
    }
}
