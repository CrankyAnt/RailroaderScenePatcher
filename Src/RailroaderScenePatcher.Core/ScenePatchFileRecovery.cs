// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Storage that can secure the exact bytes of the edited mod's scene patch file before scene patch editing replaces it.
    // Fresh start needs it; a storage without it leaves a protected scene patch file protected.
    public interface IRecoveryStorage {
        // Copies the scene patch file's bytes beside it, verifies the copy and returns its path.
        string BackupScenePatchFile();
        // Whether the backup and the scene patch file both still hold the bytes the backup secured.
        bool MatchesPatchBackup(string backup);
    }
    // The recovery of a protected scene patch file: its diagnostics, confirmed re-reading after an outside correction and
    // confirmed fresh start behind a verified backup. Neither route clears history or changes the scene.
    public sealed partial class PatchEditingSession {
        const string ProtectedSuffix = ". The scene patch file is protected from overwrite. Correct it outside scene patch editing and read it again, or start fresh behind a backup of the original.";
        const string MetadataSuffix = ". Scene patch editing and Save of the scene patch file are unavailable. Correct Info.json outside scene patch editing and read it again.";
        // Whether the session is blocked by its scene patch file itself, rather than by the mod's metadata; only then can a
        // fresh start replace what blocks it.
        bool scenePatchFileProblem;
        // Whether Start fresh can replace the protected scene patch file: the scene patch file itself blocks the session and the storage
        // can secure a backup of it.
        public bool CanStartFresh => Blocked != null && scenePatchFile == null && scenePatchFileProblem && storage is IRecoveryStorage;
        // The backup a confirmed fresh start secured, until Save replaced the original with the fresh scene patch file.
        string replacementBackup;
        // What the last read of the scene patch file found wrong, with the place, the text there and what the format expects.
        public IReadOnlyList<ScenePatchFileDiagnostic> ScenePatchFileDiagnostics { get; private set; } = Array.Empty<ScenePatchFileDiagnostic>();
        // How applying the scene patch file that Read again adopted went: an outcome per modification. Null before Read again.
        public ScenePatchFileResult LastReread { get; private set; }
        // The path of the backup the last fresh start secured, or null.
        public string LastBackup { get; private set; }
        // Whether a fresh start waits for Save to replace the original scene patch file.
        public bool FreshStartPending => replacementBackup != null;
        // Why a read scene patch file cannot be edited in this session, or null; sets the diagnostics of that read.
        string Refusal(ScenePatchFile loaded, string text) {
            ScenePatchFileDiagnostics = loaded.Diagnostics;
            if (!loaded.CanWrite) return loaded.Error;
            if (loaded.EditError != null) return loaded.EditError;
            var map = loaded.Root["sourceMap"]["mapId"];
            var message = SceneTargetMapCheck.Problem(scene, (string)map);
            if (message == null) return null;
            var expected = message == SceneTargetMapCheck.Mismatch ? "The ID of the edited map or one of its map layers, excluding any map patch. Valid IDs: " + String.Join(", ", SceneTargetMapCheck.Valid(scene))
                : "The bottom map layer's ID: " + SceneTargetMapFor(scene.MapIds);
            ScenePatchFileDiagnostics = loaded.Diagnostics.Concat(new[] { ScenePatchFileDiagnostic.Create(text, "sourceMap.mapId", message, expected, map) }).ToArray();
            return message;
        }
        // Recovery replaces a protected session's scene patch file only before anything else happened in it: no unsaved native
        // work, no history change since the session started and no unrecovered native history.
        void CheckRecoverable(bool nativeDirty) {
            if (Blocked == null || scenePatchFile != null) throw new InvalidOperationException("The scene patch file is not protected; recovery is only for a protected scene patch file");
            if (nativeDirty || identity.Current != savedIdentity || unrecovered != null || uncertain)
                throw new InvalidOperationException("Save the native map work and reopen the native map editor before recovery, so no history is replaced");
        }
        // Reads the edited mod's scene patch file again after the modder corrected it outside scene patch editing, and makes
        // it the session's scene patch file when it reads as a scene patch file of this session. The file is not written. The scene patch file's
        // modifications are applied at once, as a world load applies them, so every object shows the scene patch file's state and
        // an edit of one field keeps the scene patch file's values of the others. LastReread holds the result of that application.
        public Outcome Reread(bool confirmed, bool nativeDirty = false) {
            var outcome = new Outcome();
            try {
                if (!confirmed) throw new InvalidOperationException("Confirm the outside correction before reading the scene patch file again");
                CheckRecoverable(nativeDirty);
                string text;
                try { text = storage.ReadScenePatchFile(); }
                catch (ArgumentException e) { throw new InvalidOperationException("The scene patch file is not UTF-8 text. " + ScenePatchFile.Sentence(e.Message) + ". The scene patch file stays protected"); }
                if (text == null) throw new InvalidOperationException("The scene patch file no longer exists; restore the file and read it again, or reopen the native map editor to start a new one");
                var loaded = ScenePatchFile.Load(text);
                var refusal = Refusal(loaded, text);
                if (refusal != null) { scenePatchFileProblem = true; throw new InvalidOperationException(refusal + ". The scene patch file stays protected"); }
                string metadata;
                try { metadata = storage.ReadModInfo(); }
                catch (Exception e) { scenePatchFileProblem = false; throw new InvalidOperationException("The edited mod's Info.json cannot be read. " + ScenePatchFile.Sentence(e.Message) + ". Scene patch editing stays unavailable"); }
                scenePatchFile = loaded; diskText = text; info = metadata; saved = PreservingJson.Parse(text).Root; comparedVersion = -1;
                Blocked = null;
                LastReread = engine.Apply(text, scenePatchFileKey);
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        // Replaces the protected scene patch file with an empty scene patch file of the current format once Save runs. The original's
        // bytes are copied to a verified backup first; without that backup nothing changes.
        public Outcome FreshStart(bool confirmed, bool nativeDirty = false) {
            var outcome = new Outcome();
            try {
                if (!confirmed) throw new InvalidOperationException("Confirm that fresh start removes all content of the scene patch file");
                CheckRecoverable(nativeDirty);
                if (!scenePatchFileProblem) throw new InvalidOperationException("The scene patch file is not what blocks scene patch editing; correct Info.json and read it again");
                if (!(storage is IRecoveryStorage recovery)) throw new InvalidOperationException("This storage cannot secure a backup of the original scene patch file");
                if (!ScenePatchFileExists()) throw new InvalidOperationException("The scene patch file no longer exists; reopen the native map editor to start a new one");
                var backup = recovery.BackupScenePatchFile();
                if (String.IsNullOrWhiteSpace(backup) || !recovery.MatchesPatchBackup(backup)) throw new InvalidOperationException("The backup of the original scene patch file could not be verified; the original stays protected");
                scenePatchFile = ScenePatchFile.Create(SceneTargetMapFor(scene.MapIds));
                replacementBackup = backup; LastBackup = backup; saved = null; comparedVersion = -1;
                ScenePatchFileDiagnostics = Array.Empty<ScenePatchFileDiagnostic>();
                Blocked = null;
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        // A scene patch file whose bytes are not UTF-8 text exists although it cannot be read as text.
        bool ScenePatchFileExists() {
            try { return storage.ReadScenePatchFile() != null; }
            catch (ArgumentException) { return true; }
        }
        // A version choice checks the original against the secured bytes. Legacy fresh start
        // also verifies its backup; a version-choice backup is left alone after initial verification.
        void CheckReplacement() {
            if (versionBackup) {
                if (((IVersionBackupStorage)storage).VersionFingerprint() != backedVersionFingerprint)
                    throw new InvalidOperationException("the file changed after its backup was written. Review the new file version before saving");
                return;
            }
            if (!((IRecoveryStorage)storage).MatchesPatchBackup(replacementBackup))
                throw new InvalidOperationException("the original scene patch file or its backup changed after fresh start. Both are kept; reopen the native map editor to read the scene patch file again");
        }
    }
}
