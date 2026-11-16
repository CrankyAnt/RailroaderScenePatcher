// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Storage that can secure the exact bytes of the edited mod's patch document before scene patch editing replaces it.
    // Fresh start needs it; a storage without it leaves a protected document protected.
    public interface IRecoveryStorage {
        // Copies the patch document's bytes beside it, verifies the copy and returns its path.
        string BackupPatchDocument();
        // Whether the backup and the patch document both still hold the bytes the backup secured.
        bool MatchesPatchBackup(string backup);
    }
    // The recovery of a protected patch document: its diagnostics, confirmed re-reading after an outside correction and
    // confirmed fresh start behind a verified backup. Neither route clears history or changes the scene.
    public sealed partial class PatchEditingSession {
        const string ProtectedSuffix = ". The patch document is protected from overwrite. Correct it outside scene patch editing and read it again, or start fresh behind a backup of the original.";
        const string MetadataSuffix = ". Scene patch editing and Save of the patch document are unavailable. Correct Info.json outside scene patch editing and read it again.";
        // Whether the session is blocked by its patch document itself, rather than by the mod's metadata; only then can a
        // fresh start replace what blocks it.
        bool documentProblem;
        // Whether Start fresh can replace the protected document: the document itself blocks the session and the storage
        // can secure a backup of it.
        public bool CanStartFresh => Blocked != null && document == null && documentProblem && storage is IRecoveryStorage;
        // The backup a confirmed fresh start secured, until Save replaced the original with the fresh document.
        string replacementBackup;
        // What the last read of the patch document found wrong, with the place, the text there and what the format expects.
        public IReadOnlyList<DocumentDiagnostic> DocumentDiagnostics { get; private set; } = Array.Empty<DocumentDiagnostic>();
        // How applying the document that Read again adopted went: an outcome per modification. Null before Read again.
        public DocumentResult LastReread { get; private set; }
        // The path of the backup the last fresh start secured, or null.
        public string LastBackup { get; private set; }
        // Whether a fresh start waits for Save to replace the original document.
        public bool FreshStartPending => replacementBackup != null;
        // Why a read document cannot be edited in this session, or null; sets the diagnostics of that read.
        string Refusal(PatchDocument loaded, string text) {
            DocumentDiagnostics = loaded.Diagnostics;
            if (!loaded.CanWrite) return loaded.Error;
            if (loaded.EditError != null) return loaded.EditError;
            var map = loaded.Root["sourceMap"]["mapId"];
            if (scene.MapIds.Contains((string)map, StringComparer.Ordinal)) return null;
            const string message = "Target map does not match the loaded map context";
            DocumentDiagnostics = loaded.Diagnostics.Concat(new[] { DocumentDiagnostic.Create(text, "sourceMap.mapId", message,
                "The ID of the edited map or one of its map layers, which are " + String.Join(", ", scene.MapIds), map) }).ToArray();
            return message;
        }
        // Recovery replaces a protected session's document only before anything else happened in it: no unsaved native
        // work, no history change since the session started and no unrecovered native history.
        void CheckRecoverable(bool nativeDirty) {
            if (Blocked == null || document != null) throw new InvalidOperationException("The patch document is not protected; recovery is only for a protected document");
            if (nativeDirty || identity.Current != savedIdentity || unrecovered != null || uncertain)
                throw new InvalidOperationException("Save the native map work and reopen the map editor before recovery, so no history is replaced");
        }
        // Reads the edited mod's patch document again after the modder corrected it outside scene patch editing, and makes
        // it the session's document when it reads as a document of this session. The file is not written. The document's
        // modifications are applied at once, as a world load applies them, so every object shows the document's state and
        // an edit of one field keeps the document's values of the others. LastReread holds the result of that application.
        public Outcome Reread(bool confirmed, bool nativeDirty = false) {
            var outcome = new Outcome();
            try {
                if (!confirmed) throw new InvalidOperationException("Confirm the outside correction before reading the document again");
                CheckRecoverable(nativeDirty);
                string text;
                try { text = storage.ReadPatchDocument(); }
                catch (ArgumentException e) { throw new InvalidOperationException("The patch document is not UTF-8 text. " + PatchDocument.Sentence(e.Message) + ". The document stays protected"); }
                if (text == null) throw new InvalidOperationException("The patch document no longer exists; restore the file and read it again, or reopen the map editor to start a new one");
                var loaded = PatchDocument.Load(text);
                var refusal = Refusal(loaded, text);
                if (refusal != null) { documentProblem = true; throw new InvalidOperationException(refusal + ". The document stays protected"); }
                string metadata;
                try { metadata = storage.ReadModInfo(); }
                catch (Exception e) { documentProblem = false; throw new InvalidOperationException("The edited mod's Info.json cannot be read. " + PatchDocument.Sentence(e.Message) + ". Scene patch editing stays unavailable"); }
                document = loaded; diskText = text; info = metadata; saved = PreservingJson.Parse(text).Root; comparedVersion = -1;
                Blocked = null;
                LastReread = engine.Apply(text, documentKey);
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        // Replaces the protected document with an empty document of the current format once Save runs. The original's
        // bytes are copied to a verified backup first; without that backup nothing changes.
        public Outcome FreshStart(bool confirmed, bool nativeDirty = false) {
            var outcome = new Outcome();
            try {
                if (!confirmed) throw new InvalidOperationException("Confirm that fresh start removes all content of the patch document");
                CheckRecoverable(nativeDirty);
                if (!documentProblem) throw new InvalidOperationException("The patch document is not what blocks scene patch editing; correct Info.json and read it again");
                if (!(storage is IRecoveryStorage recovery)) throw new InvalidOperationException("This storage cannot secure a backup of the original document");
                if (!PatchDocumentExists()) throw new InvalidOperationException("The patch document no longer exists; reopen the map editor to start a new one");
                var backup = recovery.BackupPatchDocument();
                if (String.IsNullOrWhiteSpace(backup) || !recovery.MatchesPatchBackup(backup)) throw new InvalidOperationException("The backup of the original document could not be verified; the original stays protected");
                document = PatchDocument.Create(TargetMapFor(scene.MapIds));
                replacementBackup = backup; LastBackup = backup; saved = null; comparedVersion = -1;
                DocumentDiagnostics = Array.Empty<DocumentDiagnostic>();
                Blocked = null;
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        // A document whose bytes are not UTF-8 text exists although it cannot be read as text.
        bool PatchDocumentExists() {
            try { return storage.ReadPatchDocument() != null; }
            catch (ArgumentException) { return true; }
        }
        // The guard of a Save that replaces the original: the original and its backup must still hold the secured bytes.
        void CheckReplacement() {
            if (!((IRecoveryStorage)storage).MatchesPatchBackup(replacementBackup))
                throw new InvalidOperationException("the original document or its backup changed after fresh start. Both are kept; reopen the native map editor to read the document again");
        }
    }
}
