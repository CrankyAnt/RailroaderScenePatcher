// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The teleport name field on the Object tab of a duplicated spawn point. The teleport name becomes the copy's object
    // name in the game, by which the console's teleport command and the quick search find it, as they find the map's own
    // spawn points by theirs. The field is empty after placing; while it stays empty, the tab says how hard the copy is
    // to reach.
    internal static class TeleportNameField {
        internal const string Label = "Teleport name";
        internal const string Tip = "Players teleport here by typing this name after /tp in the console, or find it in the quick search. In the console, a name with a space needs quotation marks.";
        internal const string SetLabel = "Set";
        internal const string SetTip = "Give this spawn point the name in the field. Clear the field to remove the teleport name.";
        internal const string OtherMod = "Another mod placed this spawn point, so only that mod can name it.";
        internal const string EmptyNote = "Without a teleport name, players can reach this spawn point only by typing its full object name, which is very hard.";
        // Whether the Object tab shows the field: for a duplicated spawn point.
        internal static bool Shows(SceneObservation selected) =>
            selected != null && selected.Family == SceneFamilies.SpawnPoint && ModObjects.IsPath(selected.Identity.Path);
        // The teleport name the copy has in the game, or empty while it has none.
        internal static string Value(SceneObservation selected) => SpawnPoints.TeleportName(selected) ?? "";
        // The note under the field while the teleport name is empty, or null.
        internal static string Note(SceneObservation selected) => Shows(selected) && SpawnPoints.TeleportName(selected) == null ? EmptyNote : null;
        // Why Set refuses the entered text, or null. Empty text clears the teleport name.
        internal static string Refusal(string entered) => ModObjects.TeleportNameProblem(entered?.Trim());
    }
}
