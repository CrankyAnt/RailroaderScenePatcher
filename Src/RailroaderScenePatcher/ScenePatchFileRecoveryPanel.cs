// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;
using UI.Builder;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The scene patch file section of the session details: what the last read found wrong in the edited mod's scene patch file
    // and, while that scene patch file is protected, the two recovery choices. Each choice is reviewed before it is confirmed.
    public sealed partial class PatcherBehaviour {
        enum RecoveryStep { None, ReviewReread, ReviewFreshStart }
        RecoveryStep recoveryStep;
        // The action group of the panel's recovery results in the message area.
        const string RecoveryPlace = "Scene patch file";
        // The result of the last fresh start while Save has not yet replaced the original; it goes once Save did.
        string freshStartMessage;
        // Whether a fresh start was seen waiting for Save, so the Save that replaced the original is noticed once.
        bool freshStartSeen;
        // The diagnostics of the edited mod's scene patch file from world load, or from the last recovery.
        readonly System.Collections.Generic.List<string> editedDiagnostics = new System.Collections.Generic.List<string>();
        static string FullDiagnosis(PatchEditingSession session) => session.ScenePatchFileDiagnostics.Count == 0 ? (session.VersionMessage ?? "") : String.Join("\n\n",
            session.ScenePatchFileDiagnostics.Select(problem => {
                var location = String.IsNullOrEmpty(problem.Path) ? "Scene patch file" : problem.Path;
                if (problem.StartLine > 0)
                    location += problem.StartLine == problem.EndLine ? ", line " + problem.StartLine : ", lines " + problem.StartLine + " to " + problem.EndLine;
                return location + ": " + CommandBand.Sentence(problem.Message)
                    + (String.IsNullOrEmpty(problem.Fragment) ? "" : "\nText there:\n" + problem.Fragment)
                    + (String.IsNullOrEmpty(problem.Expected) ? "" : "\nExpected: " + problem.Expected)
                    + (String.IsNullOrEmpty(problem.TechnicalDetail) ? "" : "\nTechnical detail: " + problem.TechnicalDetail);
            }));
        void ChooseFileVersion(bool useFile) {
            var session = attachment.Session;
            var result = useFile ? session.UseFileVersion() : session.KeepEditorVersion();
            feedback.ObserveFileVersion(session, RecoveryPlace);
            if (!result.Applied) { attachment.Fault.Refuse(result.Error); feedback.Refuse(RecoveryPlace, useFile ? "Use file version" : "Keep editor version", result.Error); }
            else {
                attachment.Fault.Clear();
                if (useFile) { ReplaceScenePatchFileDiagnostics(session.LastVersionApplication); Inspect(); }
                // The fields show the object as it now stands: a draft or a value from before the choice never stays
                // behind to be applied.
                ReadPlacement();
                if (useFile) ReplaceOpenEquipment();
                feedback.Report(RecoveryPlace, useFile ? FilePanelState.FileVersionApplied(session.LastVersionApplication) : "Editor version kept.");
            }
            // The choice may close the file panel: the window's controls and the title bar follow it.
            VersionChanged(); attachment.RefreshChrome();
        }
        // Once Save replaced the original after a fresh start, everything that described the old scene patch file is read again:
        // the message of the fresh start goes, the scene patch file's diagnostics are replaced and the supplied definitions and
        // the inventory are refreshed.
        void TrackRecovery() {
            var session = attachment?.Session;
            if (session == null) { freshStartSeen = false; return; }
            if (session.FreshStartPending) { freshStartSeen = true; return; }
            if (!freshStartSeen) return;
            freshStartSeen = false;
            if (freshStartMessage != null) { feedback.Withdraw(RecoveryPlace, freshStartMessage); freshStartMessage = null; }
            ReplaceScenePatchFileDiagnostics(null);
            Inspect();
            presentation?.RequestRefresh();
        }
        // Replaces the diagnostics of the edited mod's scene patch file by the failures of the scene patch file recovery applied.
        void ReplaceScenePatchFileDiagnostics(ScenePatchFileResult applied) {
            foreach (var message in editedDiagnostics) diagnostics.Remove(message);
            editedDiagnostics.Clear();
            if (applied == null) return;
            string mod;
            try { mod = ModScenePatchFiles.EditedModId(); } catch { mod = "Edited mod"; }
            var messages = applied.Error != null ? new[] { mod + " scene patch file not applied. " + CommandBand.Sentence(applied.Error) }
                : applied.Outcomes.Where(o => !o.Value.Applied).Select(o => mod + "/" + o.Key + " not applied. " + CommandBand.Sentence(o.Value.Error));
            foreach (var message in messages) { MapEditorDiagnostic(message); editedDiagnostics.Add(message); }
        }
        // What the panel's look depends on, for the rebuild key.
        string RecoverySnapshot() {
            var session = attachment?.Session;
            if (session == null) return "";
            return String.Join("|", recoveryStep, session.Blocked, session.LastBackup, session.FreshStartPending, session.CanStartFresh,
                String.Join("\n", session.ScenePatchFileDiagnostics.Select(d => d.Path + d.StartLine + d.Message + d.TechnicalDetail)));
        }
        void DrawScenePatchFileRecovery(UIPanelBuilder builder) {
            var session = attachment?.Session;
            if (session == null) return;
            var problems = session.ScenePatchFileDiagnostics;
            if (session.Blocked == null && problems.Count == 0 && session.LastBackup == null) return;
            Section(builder, "Scene patch file");
            if (session.VersionMessage != null) PlainLabel(builder, session.VersionMessage);
            if (session.Blocked != null) builder.AddLabel(PathBreaks(Tint(Amber, session.Blocked)));
            foreach (var problem in problems) {
                FieldLabel(builder, problem.Path.Length == 0 ? "Scene patch file" : problem.Path);
                builder.AddLabel(PathBreaks(Literal(problem.Message)));
                if (problem.StartLine > 0)
                    Field(builder, problem.StartLine == problem.EndLine ? "Line" : "Lines",
                        problem.StartLine == problem.EndLine ? problem.StartLine.ToString() : problem.StartLine + " to " + problem.EndLine);
                if (!String.IsNullOrEmpty(problem.Fragment)) Field(builder, "Text there", problem.Fragment);
                if (!String.IsNullOrEmpty(problem.Expected)) Field(builder, "Expected", problem.Expected);
                if (!String.IsNullOrEmpty(problem.TechnicalDetail)) Field(builder, "Technical detail", problem.TechnicalDetail);
            }
            if (problems.Count != 0 || session.Blocked != null) ReferenceButtons(builder);
            if (session.LastBackup != null)
                FieldMarkup(builder, "Backup of the original", PathBreaks(Literal(session.LastBackup + (session.FreshStartPending ? " (Save replaces the original with the new empty scene patch file)" : ""))));
            if (problems.Count != 0)
                Button(builder, "Copy to clipboard", () => GUIUtility.systemCopyBuffer = FullDiagnosis(session))
                    .Tooltip("Copy to clipboard", "Copy the full diagnosis to share or save it.");
            if (session.Blocked == null) { recoveryStep = RecoveryStep.None; return; }
            switch (recoveryStep) {
                case RecoveryStep.ReviewReread:
                    PlainLabel(builder, "The patcher reads the current file and applies its modifications to the objects immediately. You can then edit it in this session. The file is not written. Anything you deleted stays deleted, including content this patcher does not understand.");
                    Confirmation(builder, "Read again", "Click after you finish correcting the file.", () => Recover("Read again", session.Reread(true, attachment.NativeDirty)));
                    break;
                case RecoveryStep.ReviewFreshStart when session.CanStartFresh:
                    PlainLabel(builder, "Start fresh removes all content from the scene patch file, including content this patcher does not understand. The original is first copied to a backup beside it. Save then replaces the original with an empty scene patch file.");
                    Confirmation(builder, "Back up and start fresh", "Click only if you intend to replace the entire file.", () => Recover("Start fresh", session.FreshStart(true, attachment.NativeDirty)));
                    break;
                default:
                    builder.HStack(row => {
                        row.ConfigureLayout(4f);
                        Button(row, "Read again", () => recoveryStep = RecoveryStep.ReviewReread).Tooltip("Read again", "Review the option to read the scene patch file again after correcting it outside the Map Editor.");
                        // Start fresh replaces the scene patch file; it is offered only when that scene patch file is what blocks editing.
                        if (session.CanStartFresh)
                            Button(row, "Start fresh", () => recoveryStep = RecoveryStep.ReviewFreshStart).Tooltip("Start fresh", "Review the option to back up the original scene patch file and replace it with an empty one.");
                        row.Spacer();
                    }).Height(RowHeight);
                    break;
            }
        }
        // The format reference and the recovery guide, offered wherever a scene patch file's problems are shown.
        void ReferenceButtons(UIPanelBuilder builder) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                Button(row, "Format reference", () => Application.OpenURL(ScenePatchFileDiagnostic.FormatReference)).Tooltip("Format reference", "Open in your browser the scene patch file reference. It describes each field and its values.");
                Button(row, "Recovery guide", () => Application.OpenURL(ScenePatchFileDiagnostic.RecoveryReference)).Tooltip("Recovery guide", "Open in your browser the guide to correcting a protected scene patch file.");
                row.Spacer();
            }).Height(RowHeight);
        }
        void Confirmation(UIPanelBuilder builder, string label, string tooltip, Action confirm) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                Button(row, label, confirm).Tooltip(label, tooltip);
                Button(row, "Cancel", () => recoveryStep = RecoveryStep.None).Tooltip("Cancel", "Keep the scene patch file protected and change nothing.");
                row.Spacer();
            }).Height(RowHeight);
        }
        void Recover(string action, Outcome outcome) {
            recoveryStep = RecoveryStep.None;
            if (!outcome.Applied) { feedback.Refuse(RecoveryPlace, action, outcome.Error); return; }
            var session = attachment.Session;
            editingStatus = "Editing ready for " + CommandBand.ShownPath(attachment.PatchPath, GameFolder);
            if (session.FreshStartPending) {
                freshStartMessage = "Start fresh completed. The original is backed up at " + session.LastBackup + ". " + WindowGuidance.SaveReplacesWithEmpty;
                feedback.Report(RecoveryPlace, freshStartMessage);
            } else {
                var outcomes = session.LastReread?.Outcomes?.Values.ToArray() ?? new Outcome[0];
                var failed = session.LastReread?.Outcomes?.Where(o => !o.Value.Applied).Select(o => o.Key).ToArray() ?? new string[0];
                feedback.Report(RecoveryPlace, "Read again completed. " + (session.LastReread?.Error != null ? "The modifications were not applied. " + CommandBand.Sentence(session.LastReread.Error)
                    : (outcomes.Length - failed.Length) + " of " + outcomes.Length + " modifications applied" + (failed.Length == 0 ? "." : ". Not applied: " + String.Join(", ", failed) + ". " + WindowGuidance.SeeDiagnostics)));
                // The scene and the definitions changed: the scene patch file's diagnostics are replaced, and the inventory and
                // the supplied definitions are read again.
                ReplaceScenePatchFileDiagnostics(session.LastReread);
                Inspect();
            }
            try { attachment.RefreshChrome(); } catch { }
        }
    }
}
