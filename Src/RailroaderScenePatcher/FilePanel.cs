// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;
using TMPro;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // The file panel in the inspector (FilePanelState): Version choice, Repair or Import. It stands where the tabs and the tab
    // body stand, at their heights, so the footer stays in place. The choices are buttons in the panel's pinned row; the
    // results go to the footer's message area.
    public sealed partial class PatcherBehaviour {
        const string ImportPlace = "Import";
        FilePanelState.Kind FilePanel => FilePanelState.Of(attachment?.Session);
        bool FileLocked => FilePanel != FilePanelState.Kind.None;
        string FileLockReason => FilePanelState.Reason(FilePanel);
        void DrawFilePanel(UIPanelBuilder builder) {
            var session = attachment.Session;
            var kind = FilePanel;
            // The heading takes the place of the tab strip, at its height.
            Heading(builder, Literal(FilePanelState.Heading(kind)), null, null).Height(30);
            var view = presentation.Scroll(builder, TabBodyMinimumHeight, content => {
                content.ConfigureLayout(4f);
                switch (kind) {
                    case FilePanelState.Kind.VersionChoice: DrawVersionChoice(content, session); break;
                    case FilePanelState.Kind.Repair: DrawRepair(content, session); break;
                    case FilePanelState.Kind.Import: DrawImport(content, session); break;
                }
            });
            var fill = view.GetComponent<LayoutElement>();
            if (fill != null) { fill.minHeight = fill.preferredHeight = TabBodyMinimumHeight; fill.flexibleHeight = 10000; }
            var viewport = view.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
            ActionRow(builder, FilePanelActs(kind, session), null);
        }
        void DrawVersionChoice(UIPanelBuilder builder, PatchEditingSession session) {
            PlainLabel(builder, CommandBand.Sentence(session.VersionMessage));
            Field(builder, FilePanelState.UseFile, FilePanelState.UseFileEffect);
            Field(builder, FilePanelState.KeepEditor, FilePanelState.KeepEditorEffect);
            Field(builder, FilePanelState.Import, FilePanelState.ImportEffect);
        }
        void DrawRepair(UIPanelBuilder builder, PatchEditingSession session) {
            PlainLabel(builder, CommandBand.Sentence(session.VersionMessage));
            foreach (var problem in session.ScenePatchFileDiagnostics.Where(problem => problem.StartLine > 0))
                Field(builder, problem.StartLine == problem.EndLine ? "Line" : "Lines",
                    problem.StartLine == problem.EndLine ? problem.StartLine.ToString() : problem.StartLine + " to " + problem.EndLine);
            FieldMarkup(builder, FilePanelState.KeepEditor, PathBreaks(Literal(FilePanelState.RepairKeepEffect(session))));
        }
        void DrawImport(UIPanelBuilder builder, PatchEditingSession session) {
            var import = session.Import;
            PlainLabel(builder, FilePanelState.ImportIntro);
            // As #73 orders the overview: the changes that apply without conflict, then each conflict with what the editor
            // version and the file version hold, followed by its choice.
            Section(builder, "Changes (" + import.Changes.Count + ")");
            if (import.Changes.Count == 0) PlainLabel(builder, "There are no changes without conflicts.");
            foreach (var change in import.Changes)
                builder.AddLabel("<b>" + FilePanelState.KindLabel(change.Kind) + "</b> " + PathBreaks(Literal(change.Description)));
            Section(builder, "Conflicts (" + import.Conflicts.Count + ")");
            if (import.Conflicts.Count == 0) PlainLabel(builder, "No conflicts.");
            foreach (var conflict in import.Conflicts) {
                var number = conflict.Number;
                builder.AddLabel(PathBreaks(Literal(conflict.Description)));
                PlainLabel(builder, conflict.EditorSide);
                PlainLabel(builder, conflict.FileSide);
                builder.AddSegmentedControlCompact(segments => {
                    segments.AddSegment(FilePanelState.ChoiceLabel(false), conflict.UseFile == false, presentation.Action(() => ChooseImportVersion(number, false)))
                        .Tooltip(FilePanelState.ChoiceLabel(false), "Keep your edits for this conflict.");
                    segments.AddSegment(FilePanelState.ChoiceLabel(true), conflict.UseFile == true, presentation.Action(() => ChooseImportVersion(number, true)))
                        .Tooltip(FilePanelState.ChoiceLabel(true), "Use the file version for this conflict and discard your edits for it.");
                });
            }
        }
        Act[] FilePanelActs(FilePanelState.Kind kind, PatchEditingSession session) {
            // A choice reaches the session only while the panel it was drawn for still stands (FilePanelState.Late).
            var version = session.ObservedVersion;
            Action Current(Action run) => () => { if (!FilePanelState.Late(kind, version, attachment?.Session)) run(); };
            switch (kind) {
                case FilePanelState.Kind.VersionChoice: return new[] {
                    Slot(FilePanelState.UseFile, "Use the changes from the file on disk.", null, Current(() => ChooseFileVersion(true))),
                    Slot(FilePanelState.KeepEditor, "Continue editing with your current version.", null, Current(() => ChooseFileVersion(false))),
                    Slot(FilePanelState.Import, "Review which changes to bring into the editor.", null, Current(OpenImport)) };
                case FilePanelState.Kind.Repair: return new[] {
                    Slot(FilePanelState.KeepEditor, "Continue editing with your current version.", !session.VersionFileMissing && session.BackupPreview == null ? "The backup path is unavailable." : null, Current(() => ChooseFileVersion(false))),
                    Slot(FilePanelState.Details, "Open Details to read the full diagnosis for this session.", null, () => presentation.ToggleDetails()),
                    Slot(FilePanelState.Copy, "Copy the full diagnosis.", null, () => { GUIUtility.systemCopyBuffer = FullDiagnosis(session); feedback.Report(RecoveryPlace, "Diagnosis copied to the clipboard."); }) };
                case FilePanelState.Kind.Import: return new[] {
                    Slot(FilePanelState.Import, "Apply all changes and your choices for all conflicts in this overview together.", session.Import?.ApplyRefusal, Current(ApplyImport)),
                    Slot(FilePanelState.Cancel, "Close the overview and discard your conflict choices. Nothing changes. You still need to choose a version.", null, Current(CancelImport)) };
                default: return new Act[0];
            }
        }
        void OpenImport() {
            var session = attachment.Session;
            var result = session.OpenImport();
            feedback.ObserveFileVersion(session, RecoveryPlace);
            if (!result.Applied) feedback.Refuse(ImportPlace, "Import", result.Error);
            else feedback.ClearPlace(ImportPlace);
            VersionChanged();
        }
        void ChooseImportVersion(int conflict, bool useFile) {
            try { attachment.Session.ChooseImportVersion(conflict, useFile); }
            catch (Exception e) { feedback.Refuse(ImportPlace, "Conflict choice", e.Message); }
            presentation?.RequestRefresh();
        }
        void ApplyImport() {
            var session = attachment.Session;
            var result = session.ApplyImport();
            feedback.ObserveFileVersion(session, RecoveryPlace);
            if (!result.Applied) { feedback.Report(ImportPlace, "Import failed. " + CommandBand.Sentence(result.Error)); }
            else {
                attachment.Fault = null;
                Inspect();
                // The fields show the imported placement; the session value from before Import never stays behind.
                ReadPlacement();
                feedback.Report(ImportPlace, FilePanelState.Completed(session.LastImport, session.Outstanding(false).Contains(PatchEditingSession.PatchData)));
            }
            VersionChanged();
        }
        void CancelImport() {
            attachment.Session.CancelImport();
            feedback.Report(ImportPlace, "Import canceled. Nothing changed. Choose a version to continue.");
            VersionChanged();
        }
        // What the file panel's look depends on beyond the version state, for the rebuild key.
        string ImportSnapshot() {
            var import = attachment?.Session.Import;
            return import == null ? "" : import.GetHashCode() + ":" + String.Join(",", import.Conflicts.Select(c => c.UseFile));
        }
        // Makes every control below the rectangle unavailable with the file panel's reason, also controls the native
        // builders add later, such as the rows of an outline that is scrolled or rebuilt.
        void LockControls(RectTransform rect, params RectTransform[] except) {
            var reason = FileLockReason;
            if (reason == null || rect == null) return;
            var locked = rect.GetComponent<UnavailableControls>() ?? rect.gameObject.AddComponent<UnavailableControls>();
            locked.Reason = () => FileLockReason; locked.Except = except.Where(e => e != null).ToArray();
            locked.Apply();
        }
    }
    // Keeps the controls of a region unavailable while Reason gives a reason: not interactable, dimmed and with a tooltip
    // that gives only the reason. The controls in Except stay available. Once Reason gives none, every control gets back
    // its own state, text alpha and tooltip, whether or not the region is rebuilt.
    internal sealed class UnavailableControls : MonoBehaviour {
        internal Func<string> Reason = () => null;
        internal RectTransform[] Except = new RectTransform[0];
        int nextFrame;
        void LateUpdate() {
            string reason = null;
            try { reason = Reason(); } catch { }
            if (reason == null) { Release(); return; }
            // Rows are locked in every frame: the native list makes and reuses its rows after the window is built, after a
            // resize or a divider drag, and resets their look when it binds them, so no row is ever clickable or bright
            // while locked. The other controls are checked now and then.
            LockRows();
            if (Time.frameCount >= nextFrame) Apply();
        }
        // The title of a row and of its disclosure while locked: the first line of the row's own tooltip, its object's
        // name, or else the row's label, such as a folder's count and name.
        static string RowTitle(OutlineRowClickHandler row) {
            var provider = row.GetComponent<UI.Tooltips.UITooltipProvider>();
            string tooltip = null;
            try { tooltip = provider?.Title; } catch { }
            var label = row.GetComponentsInChildren<TMP_Text>(true).Select(t => System.Text.RegularExpressions.Regex.Replace(t.text ?? "", "<[^>]*>", "").Trim()).FirstOrDefault(t => t.Length != 0);
            return FilePanelState.RowTitle(tooltip, label);
        }
        static readonly int UiLayer = LayerMask.NameToLayer("UI");
        void LockRows() {
            foreach (var clicks in GetComponentsInChildren<OutlineRowClickHandler>(true)) {
                if (Except.Any(e => e != null && clicks.transform.IsChildOf(e))) continue;
                var saved = clicks.GetComponent<UnavailableRow>();
                if (saved == null) {
                    saved = clicks.gameObject.AddComponent<UnavailableRow>();
                    saved.Alphas = clicks.GetComponentsInChildren<TMP_Text>(true).Select(t => Tuple.Create(t, t.alpha)).ToArray();
                    var row = clicks; var source = Reason;
                    var provider = clicks.GetComponent<UI.Tooltips.UITooltipProvider>();
                    if (provider != null) provider.DynamicTooltipInfo = () => new TooltipInfo(RowTitle(row), CommandBand.Tooltip(null, source()));
                }
                if (clicks.enabled) clicks.enabled = false;
                foreach (var text in clicks.GetComponentsInChildren<TMP_Text>(true)) if (text.alpha != CommandBand.InactiveAlpha) text.alpha = CommandBand.InactiveAlpha;
                // The game shows the tooltip of a raycast hit on the UI layer only. The native list makes a row's disclosure
                // on the default layer, so its tooltip never showed; while locked it stands on the UI layer, and its own
                // layer comes back on release.
                foreach (var disclosure in clicks.GetComponentsInChildren<Button>(true)) {
                    if (UiLayer < 0 || disclosure.gameObject.layer == UiLayer) continue;
                    saved.Layers.Add(Tuple.Create(disclosure.gameObject, disclosure.gameObject.layer));
                    disclosure.gameObject.layer = UiLayer;
                }
            }
        }
        internal void Apply() {
            nextFrame = Time.frameCount + 15;
            foreach (var selectable in GetComponentsInChildren<Selectable>(true)) {
                if (Except.Any(e => e != null && selectable.transform.IsChildOf(e))) continue;
                if (selectable is Scrollbar) continue;
                var saved = selectable.GetComponent<UnavailableTooltip>();
                if (saved == null) {
                    var provider = selectable.GetComponent<UI.Tooltips.UITooltipProvider>();
                    string title = null;
                    try { title = provider?.Title; } catch { }
                    if (String.IsNullOrEmpty(title)) title = selectable.GetComponentsInChildren<TMP_Text>(true).Select(t => System.Text.RegularExpressions.Regex.Replace(t.text ?? "", "<[^>]*>", "").Trim()).FirstOrDefault(t => t.Length != 0) ?? "Unavailable";
                    saved = selectable.gameObject.AddComponent<UnavailableTooltip>();
                    saved.Title = title; saved.Interactable = selectable.interactable; saved.HadProvider = provider != null;
                    saved.Dynamic = provider?.DynamicTooltipInfo;
                    saved.Alphas = selectable.GetComponentsInChildren<TMP_Text>(true).Select(t => Tuple.Create(t, t.alpha)).ToArray();
                }
                selectable.interactable = false;
                foreach (var text in selectable.GetComponentsInChildren<TMP_Text>(true)) text.alpha = CommandBand.InactiveAlpha;
                var tooltip = selectable.GetComponent<UI.Tooltips.UITooltipProvider>() ?? selectable.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
                var shown = saved.Title; var source = Reason;
                // A row's disclosure takes the row's title, which follows the row as the native list reuses it.
                var owner = selectable.GetComponentInParent<OutlineRowClickHandler>(true);
                tooltip.DynamicTooltipInfo = owner != null ? (Func<TooltipInfo>)(() => new TooltipInfo(RowTitle(owner), CommandBand.Tooltip(null, source())))
                    : () => new TooltipInfo(shown, CommandBand.Tooltip(null, source()));
            }
            LockRows();
        }
        // Gives every control its own state back and removes this lock.
        internal void Release() {
            foreach (var saved in GetComponentsInChildren<UnavailableTooltip>(true)) {
                var selectable = saved.GetComponent<Selectable>();
                if (selectable != null) selectable.interactable = saved.Interactable;
                foreach (var pair in saved.Alphas) if (pair.Item1 != null) pair.Item1.alpha = pair.Item2;
                var tooltip = saved.GetComponent<UI.Tooltips.UITooltipProvider>();
                if (tooltip != null) {
                    if (saved.HadProvider) tooltip.DynamicTooltipInfo = saved.Dynamic;
                    else Destroy(tooltip);
                }
                Destroy(saved);
            }
            foreach (var row in GetComponentsInChildren<UnavailableRow>(true)) {
                var clicks = row.GetComponent<OutlineRowClickHandler>();
                if (clicks != null) clicks.enabled = true;
                foreach (var pair in row.Alphas) if (pair.Item1 != null) pair.Item1.alpha = pair.Item2;
                foreach (var pair in row.Layers) if (pair.Item1 != null) pair.Item1.layer = pair.Item2;
                var tooltip = row.GetComponent<UI.Tooltips.UITooltipProvider>();
                if (tooltip != null) tooltip.DynamicTooltipInfo = null;
                Destroy(row);
            }
            var group = GetComponent<CanvasGroup>();
            if (group != null) group.interactable = true;
            Destroy(this);
        }
    }
    // An outliner row UnavailableControls locked: its native click handler and tooltip get their own behaviour back on release.
    internal sealed class UnavailableRow : MonoBehaviour {
        internal Tuple<TMP_Text, float>[] Alphas = new Tuple<TMP_Text, float>[0];
        // The disclosures moved to the UI layer for their tooltip, with the layer each had.
        internal readonly System.Collections.Generic.List<Tuple<GameObject, int>> Layers = new System.Collections.Generic.List<Tuple<GameObject, int>>();
    }
    // A control whose state UnavailableControls replaced: what it had before, for the release.
    internal sealed class UnavailableTooltip : MonoBehaviour {
        internal string Title;
        internal bool Interactable, HadProvider;
        internal Func<TooltipInfo> Dynamic;
        internal Tuple<TMP_Text, float>[] Alphas = new Tuple<TMP_Text, float>[0];
    }
}
