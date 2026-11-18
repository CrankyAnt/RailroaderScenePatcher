// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    public interface IVersionBackupStorage : IRecoveryStorage {
        string ReserveVersionBackup();
        string RelativeVersionBackup(string path);
        string BackupScenePatchFile(string path);
        void RemoveVersionBackupIfUnchanged(string path);
        string VersionFingerprint();
        bool VersionBackupExists(string path);
    }
    public enum FileVersionState { None, Changed, Repair }
    public sealed partial class PatchEditingSession {
        int versionGeneration;
        bool acceptedVersion;
        bool versionBackup;
        bool lastVersionBackup;
        string backedVersionFingerprint;
        JToken acceptedContent;
        string backupPreview;
        string observedVersion;
        bool observedMissing;
        public FileVersionState VersionState { get; private set; }
        public string VersionMessage { get; private set; }
        public bool VersionFileMissing => VersionState == FileVersionState.Repair && observedMissing;
        public ScenePatchFileResult LastVersionApplication { get; private set; }
        public bool CanSaveForReload => VersionState == FileVersionState.None;
        public string SaveRefusal => CanSaveForReload ? null : VersionMessage;
        public string BackupPreview => storage is IVersionBackupStorage backup && backupPreview != null ? backup.RelativeVersionBackup(backupPreview) : null;
        void ClearVersionAcceptance() { acceptedVersion = false; acceptedContent = null; backupPreview = null; }
        public void CheckFileVersion() {
            if (scenePatchFile == null) return;
            if (lastVersionBackup && LastBackup != null && !((IVersionBackupStorage)storage).VersionBackupExists(LastBackup)) {
                LastBackup = null; VersionMessage = null; lastVersionBackup = false;
            }
            if (replacementBackup != null) {
                if (!versionBackup) return;
                try { if (((IVersionBackupStorage)storage).VersionFingerprint() == backedVersionFingerprint) return; } catch { }
                replacementBackup = null; versionBackup = false; backedVersionFingerprint = null; ClearVersionAcceptance();
            }
            var previous = VersionState;
            observedMissing = false;
            try {
                var fingerprint = (storage as IVersionBackupStorage)?.VersionFingerprint();
                if (storage is IVersionBackupStorage) observedVersion = fingerprint;
                var text = storage.ReadScenePatchFile();
                if (storage is IVersionBackupStorage checkedStorage && checkedStorage.VersionFingerprint() != fingerprint) {
                    observedVersion = null;
                    throw new InvalidOperationException("The file changed while it was being read. Refresh to check it again");
                }
                if (!(storage is IVersionBackupStorage)) observedVersion = text;
                observedMissing = text == null && fingerprint == null;
                if (acceptedVersion && text == null && acceptedContent == null) return;
                if (acceptedVersion && text != null) {
                    try { if (PreservingJson.SameContent(PreservingJson.Parse(text).Root, acceptedContent)) return; }
                    catch { /* A damaged replacement goes through the diagnostic read below. */ }
                }
                if (!ChangedOutside(text)) { VersionState = FileVersionState.None; VersionMessage = null; ClearVersionAcceptance(); Import = null; return; }
                var loaded = text == null ? null : ScenePatchFile.Load(text);
                var refusal = loaded == null ? "The file no longer exists" : Refusal(loaded, text);
                VersionState = refusal == null ? FileVersionState.Changed : FileVersionState.Repair;
                VersionMessage = refusal == null ? (previous == FileVersionState.Repair ? "The file is readable again. " : "") + "The scene patch file changed outside scene patch editing. Save won't overwrite it until you choose a version." : "File needs repair: " + refusal;
                if (VersionState == FileVersionState.Repair && backupPreview == null && storage is IVersionBackupStorage backup) backupPreview = backup.ReserveVersionBackup();
                // A file that needs repair offers no Import; an overview of an earlier version of the file closes.
                if (VersionState == FileVersionState.Repair) Import = null;
                else if (Import != null && observedVersion != Import.Fingerprint) { Import = null; ImportOutdated = true; }
            } catch (Exception e) {
                VersionState = FileVersionState.Repair; Import = null;
                VersionMessage = "File needs repair: " + e.Message;
                ScenePatchFileDiagnostics = new[] { ScenePatchFileDiagnostic.Create(null, "", e.Message, "A readable scene patch file") };
                if (backupPreview == null && storage is IVersionBackupStorage backup) backupPreview = backup.ReserveVersionBackup();
            }
        }
        public Outcome KeepEditorVersion() {
            var outcome = new Outcome();
            try {
                var shown = observedVersion;
                CheckFileVersion();
                if (shown != observedVersion) throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                if (VersionState == FileVersionState.None) throw new InvalidOperationException("There is no file version to choose");
                if (VersionState == FileVersionState.Repair) {
                    if (shown == null && observedMissing) {
                        acceptedVersion = true; acceptedContent = null; backupPreview = null;
                        VersionMessage = "There is no file to back up. The next Save writes the editor version.";
                        VersionState = FileVersionState.None; outcome.Applied = true;
                        return outcome;
                    }
                    if (!(storage is IVersionBackupStorage backup) || backupPreview == null) throw new InvalidOperationException("This storage cannot create a backup of the file");
                    string path = null;
                    try {
                        path = backup.BackupScenePatchFile(backupPreview);
                        if (!backup.MatchesPatchBackup(path)) throw new InvalidOperationException("The backup could not be verified");
                        if (backup.VersionFingerprint() != shown) throw new InvalidOperationException("The file changed while the backup was being written. Review the updated diagnosis before continuing");
                    } catch (Exception error) {
                        Exception cleanupError = null;
                        if (path != null) { try { backup.RemoveVersionBackupIfUnchanged(path); } catch (Exception e) { cleanupError = e; } }
                        backupPreview = backup.ReserveVersionBackup();
                        if (cleanupError != null) throw new InvalidOperationException(error.Message + " The backup may remain at " + path + "; delete it when no longer needed.", error);
                        throw;
                    }
                    replacementBackup = path; versionBackup = true; backedVersionFingerprint = shown; LastBackup = path; lastVersionBackup = true;
                    VersionMessage = "Scene Patcher will not change the backup. Delete it when you no longer need it. It will be published with the mod while it remains there.";
                } else {
                    var text = storage.ReadScenePatchFile();
                    if ((storage is IVersionBackupStorage versionStorage ? versionStorage.VersionFingerprint() : text) != shown) throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                    acceptedContent = PreservingJson.Parse(text).Root; acceptedVersion = true;
                    VersionMessage = null;
                }
                VersionState = FileVersionState.None; Import = null; outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        public Outcome UseFileVersion() {
            var outcome = new Outcome();
            var snapshots = new List<Tuple<object, object>>();
            var original = scenePatchFile == null ? null : ScenePatchFile.Load(scenePatchFile.Serialize());
            var mutationStarted = false;
            try {
                var shown = observedVersion;
                CheckFileVersion();
                if (shown != observedVersion) throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                if (VersionState != FileVersionState.Changed) throw new InvalidOperationException("There is no readable file version to use");
                var text = storage.ReadScenePatchFile();
                if ((storage is IVersionBackupStorage versionStorage ? versionStorage.VersionFingerprint() : text) != shown) throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                var loaded = ScenePatchFile.Load(text);
                var refusal = Refusal(loaded, text);
                if (refusal != null) throw new InvalidOperationException(refusal);
                var context = scene.MapIds.ToArray();
                foreach (var item in scenePatchFile.Items) {
                    try { PatchEngine.Parse(item, scenePatchFileKey); } catch (InvalidOperationException) { continue; }
                    TargetIdentity target; object resolved;
                    try { target = PatchEngine.ParseTarget(item["target"] as JObject); resolved = engine.ResolveUnique(target); }
                    catch (InvalidOperationException) { continue; }
                    if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Cleanup for this object could not be verified. Select Recheck or reload the Map Editor before using the file version");
                    scene.Preflight(resolved);
                    engine.GuardMutation(target, resolved, context, null);
                    if (!snapshots.Any(pair => ReferenceEquals(pair.Item1, resolved))) snapshots.Add(Tuple.Create(resolved, scene.Capture(resolved)));
                }
                // An unavailable target in the incoming file is an isolated application failure, as at load.
                foreach (var item in loaded.Items) {
                    try {
                        var target = PatchEngine.ParseTarget(item["target"] as JObject);
                        var resolved = engine.ResolveUnique(target);
                        if (engine.IsUnsafe(resolved)) continue;
                        scene.Preflight(resolved);
                        engine.GuardMutation(target, resolved, context, null);
                        if (!snapshots.Any(pair => ReferenceEquals(pair.Item1, resolved))) snapshots.Add(Tuple.Create(resolved, scene.Capture(resolved)));
                    } catch (Exception) { }
                }
                // Restore the current modifications before applying the new file, through the same guarded edit path.
                foreach (var property in scenePatchFile.Modifications.Properties().ToArray()) {
                    var item = property.Value as JObject;
                    if (item == null) continue;
                    try { PatchEngine.Parse(item, scenePatchFileKey); } catch (InvalidOperationException) { continue; }
                    TargetIdentity target; object resolved;
                    try { target = PatchEngine.ParseTarget(item["target"] as JObject); resolved = engine.ResolveUnique(target); }
                    catch (InvalidOperationException) { continue; }
                    if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Cleanup for this object could not be verified. Select Recheck or reload the Map Editor before using the file version");
                    scene.Preflight(resolved);
                    mutationStarted = true;
                    Commit(target, resolved, null, property.Name, item, null);
                }
                if ((storage is IVersionBackupStorage finalStorage ? finalStorage.VersionFingerprint() : storage.ReadScenePatchFile()) != shown)
                    throw new InvalidOperationException("The file changed while the choice was being applied. Review the updated choice before continuing");
                scenePatchFile = loaded; diskText = text; saved = PreservingJson.Parse(text).Root; comparedVersion = -1;
                ResetStartState(); stepKeys.Clear(); Import = null;
                ClearVersionAcceptance(); VersionState = FileVersionState.None; VersionMessage = null;
                LastVersionApplication = engine.ApplyFileVersion(text, scenePatchFileKey);
                versionGeneration++;
                outcome.Applied = true;
            } catch (Exception e) {
                scenePatchFile = original;
                var unverified = false;
                foreach (var snapshot in mutationStarted ? snapshots : Enumerable.Empty<Tuple<object, object>>()) {
                    var restored = false;
                    try { restored = scene.Restore(snapshot.Item1, snapshot.Item2); } catch { }
                    if (!restored) { engine.SetUnsafe(snapshot.Item1, true); unverified = true; }
                }
                outcome.Error = e.Message + (unverified ? ". Could not verify cleanup. Affected targets remain guarded" : "");
            }
            return outcome;
        }
    }
}
