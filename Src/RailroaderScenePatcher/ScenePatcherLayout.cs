// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;
using TMPro;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // Inspector mode of the Scene Patcher window: a command band, the outliner beside the inspector, four tabs and a
    // fixed footer. A control sits where its scope is: a tab's own actions in a pinned row at the bottom of its tab
    // body, the actions on the inspected object that are the same on every tab in the footer below the messages.
    // The tab body takes the height left by the fixed message area and the action rows.
    // Every region has a fixed height or scrolls inside its own rectangle, so showing or hiding text or feedback
    // moves no other control.
    public sealed partial class PatcherBehaviour {
        // An equipment status that fails is checked again at every redraw; each failure is one debug line.
        readonly LogOnce equipmentStatusFailures = new LogOnce();
        const string ObjectTab = "object", PlacementTab = "placement", ConnectionsTab = "connections", ChangesTab = "changes";
        static readonly string[] TabIds = { ObjectTab, PlacementTab, ConnectionsTab, ChangesTab };
        // Canvas units.
        // The outliner is never narrower than its four filter segments at their natural widths, 292.4 units
        // (CommandBand.FilterSegments), which is also more than its band needs: Refresh and both status lamps with
        // their words, 251.
        // The inspector's minimum holds the four footer slots, their spacing and the resize grip.
        const float OutlinerDefaultWidth = CommandBand.OutlinerWidth, OutlinerMinimumWidth = CommandBand.OutlinerWidth, InspectorMinimumWidth = 350, TabBodyMinimumHeight = 100,
            RowHeight = 24, LineHeight = 20, TabStripHeight = 30,
            // The message areas' fixed heights: 124 units in the inspector and 84 beside the file panel. The tab's own
            // row stands just above the footer's divider, and the footer's row at the window's lower edge.
            InspectorMessageHeight = 124, FilePanelMessageHeight = 84,
            // What a row adds to its name: the folder and child indent, the two markers and the scroll bar.
            RowDecoration = 80;
        // The changed state reads the same in the state line and on the Changes tab: the native map editor's badge.
        static readonly string ChangedBadge = CommandBand.Changed.Markup;
        // The left band's widths are CommandBand's, and the outliner's minimum holds them: 296 against the 251 they
        // need.
        // How far a lamp's dot stands above the middle of its row, half of this, so that it sits on its word's line.
        const float LampRaise = 4;
        // The native map editor's status colors (MapCompositionStyle and its inspectors), and the proposed-target blue of
        // the world markings.
        const string Red = "#FF8080", Amber = "#E8A33D", Green = "#7FB86B", Blue = "#4DCCFF";
        // The outliner width is stored where the native window stores this window's bounds: the game's preferences
        // file, under the window's own key. Unity's PlayerPrefs would write to the registry key that the user's normal
        // play shares.
        const string OutlinerWidthKey = "window." + ScenePatchWindow.WindowIdentifier + ".outliner-width";
        static float ReadOutlinerWidth() {
            try { var stored = Game.GlobalPreferences.GetFloat(OutlinerWidthKey, OutlinerDefaultWidth); return Single.IsNaN(stored) || stored <= 0 ? OutlinerDefaultWidth : stored; }
            catch { return OutlinerDefaultWidth; }
        }
        static void StoreOutlinerWidth(float width) { try { Game.GlobalPreferences.SetFloat(OutlinerWidthKey, width); } catch { } }
        static void ForgetOutlinerWidth() { try { Game.GlobalPreferences.DeleteKey(OutlinerWidthKey); } catch { } }
        // Why a tab's own controls cannot be used while a world choice is pending: the footer holds that choice.
        const string PendingChoice = "Confirm or cancel the pending world choice first.";
        string ChoiceBlock => selection?.Pending != null ? PendingChoice : null;
        // The tool the gizmo starts in, chosen with the Move and Rotate switch of the Gizmo mode.
        UI.Gizmos.TransformGizmoMode gizmoTool = UI.Gizmos.TransformGizmoMode.Move;
        // Whether the footer's message area shows the clicked candidate's details during a world choice.
        bool candidateDetails;
        static readonly string[] PlacementModes = { "Numeric", "Gizmo", "Snap", "Follow" };
        OutlinerFilter outlinerFilter;
        // Whether the Object tab's Dependencies and Runtime state show their long form instead of the short form.
        bool dependenciesLong, runtimeLong;
        float outlinerWidth = OutlinerDefaultWidth;
        int placementMode;
        object revealed;
        // The folders the revealed object stood in when it was revealed.
        string revealedIn;
        readonly UIState<string> inspectorTab = new UIState<string>(ObjectTab);
        readonly UIOutlineViewState inventoryOutline = new UIOutlineViewState();
        readonly UIOutlineViewState filteredOutline = new UIOutlineViewState(true);

        IConfigurableElement Button(UIPanelBuilder builder, string label, Action action) {
            var button = builder.AddButtonCompact(label, presentation.Action(action));
            foreach (var text in button.RectTransform.GetComponentsInChildren<TMP_Text>(true)) text.richText = false;
            return button;
        }
        void PlainLabel(UIPanelBuilder builder, string value) => builder.AddLabel(value, text => text.richText = false);
        // Scene and scene patch file text is shown literally inside the native text styles.
        static string Literal(string value) => (value ?? "").Replace("<", "<noparse><</noparse>");
        static string Tint(string color, string value) => "<color=" + color + ">" + Literal(value) + "</color>";
        // A named value: the native field label above the value, left-aligned. Every tab uses this one form, so the same
        // kind of element has the same size, weight and colour everywhere.
        RectTransform Field(UIPanelBuilder builder, string label, string value) => FieldMarkup(builder, label, Literal(value));
        RectTransform FieldMarkup(UIPanelBuilder builder, string label, string markup) { FieldLabel(builder, label); return builder.AddLabel(markup); }
        // A section heading: a rule, then the title in bold body text. The native section header has the small upper-case
        // letters of a field label, so "STATE" over "ENABLED" read as an empty section followed by a second heading.
        RectTransform Section(UIPanelBuilder builder, string title, bool divider = true) {
            if (divider) builder.AddHRule();
            return builder.AddLabel("<b>" + Literal(title) + "</b>");
        }
        // A section heading with the native disclosure triangle (NativePresentation.DisclosureTriangle) right after its
        // title. The triangle switches the section between its short form, collapsed, and its long form, expanded; the
        // section's lines stay in view either way.
        void DisclosureSection(UIPanelBuilder builder, string title, bool longForm, Action toggle) {
            builder.AddHRule();
            builder.HStack(row => {
                var heading = row.AddLabel("<b>" + Literal(title) + "</b>");
                var headingLayout = heading.GetComponent<LayoutElement>() ?? heading.gameObject.AddComponent<LayoutElement>();
                headingLayout.flexibleWidth = 0;
                foreach (var text in heading.GetComponentsInChildren<TMP_Text>(true)) text.textWrappingMode = TextWrappingModes.NoWrap;
                var headingText = heading.GetComponentInChildren<TMP_Text>(true);
                presentation.DisclosureTriangle(heading.parent, longForm, headingText != null ? headingText.color : Color.white, toggle, heading, new TooltipInfo(title, CommandBand.DisclosureTip(longForm)));
                row.Spacer();
            }).Height(NativePresentation.DisclosureHitSize);
        }
        // A line of the form "Marker ID=Mlm1; type=Generic" or "Graph registration: none" as named values. A line of
        // another form stays text.
        void BindingFields(UIPanelBuilder builder, string line) {
            var values = InventoryPresentationState.BindingValues(line);
            // A named location is its track location as the row's value, then each note as a line of its own.
            if (values == null && line.StartsWith(CommandBand.ListedTrackLocation, StringComparison.Ordinal)) {
                var named = CommandBand.NamedLocationRow(line);
                Field(builder, CommandBand.ListedTrackLocation.TrimEnd(':'), Breakable(named.Value));
                foreach (var note in named.Notes) PlainLabel(builder, Breakable(note));
                return;
            }
            if (values == null) {
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0) Field(builder, line.Substring(0, colon), Breakable(line.Substring(colon + 2))); else PlainLabel(builder, Breakable(line));
                return;
            }
            foreach (var pair in values.Value.Values) Field(builder, (values.Value.Group + " " + pair.Key).Trim(), Breakable(CommandBand.Shown(pair.Key, pair.Value)));
        }
        // A group of named values in the native inspector's form (DtoInspectorPanel.AddStackedField with AddAxisField):
        // the group's caption above the row, then each short label in front of its input box, the boxes sharing the
        // width. While the group cannot be edited it is one running line of text instead, as tall as the row of
        // boxes, so a group that becomes read-only moves nothing below it. Every group of values in the panel is built
        // here. Returns the text of the running line, for a caller that keeps it current.
        // accessory builds a control at the right end of the caption's row, as the native Move button stands beside
        // the Position caption.
        TMP_Text ValueRow(UIPanelBuilder builder, string caption, string unit, string[] labels, string[] values, int first, string block, string tip, Func<int, bool> numeric, Action<UIPanelBuilder> accessory = null, bool freeText = false) {
            if (accessory == null) FieldLabel(builder, CommandBand.GroupCaption(caption, unit, block == null));
            else builder.HStack(row => {
                row.AddLabel("<style=FieldLabel>" + Literal(CommandBand.GroupCaption(caption, unit, block == null)) + "</style>", text => text.verticalAlignment = VerticalAlignmentOptions.Bottom);
                row.Spacer();
                accessory(row);
            }).Height(RowHeight);
            if (block != null) {
                var line = Line(builder, Literal(CommandBand.RunningLine(labels, values.Skip(first).Take(labels.Length).ToArray(), unit)), caption, CommandBand.BlockedValueTip(block), 30);
                return line.GetComponentInChildren<TMP_Text>(true);
            }
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                for (var column = 0; column < labels.Length; column++) {
                    var index = first + column; var label = labels[column];
                    // The one value of a group has only the group's caption.
                    if (CommandBand.HasBoxLabels(labels)) row.AddLabel("<style=FieldLabel>" + Literal(label) + "</style>", text => text.verticalAlignment = VerticalAlignmentOptions.Middle).MinWidth(0f);
                    var input = row.AddInputField(values[index], value => values[index] = value);
                    // Every keystroke stores the value and lets the buttons that depend on it follow.
                    var field = input.GetComponent<TMP_InputField>();
                    if (freeText) FreeText(field);
                    field.onValueChanged.AddListener(value => { values[index] = value; FieldsChanged(); });
                    if (ReferenceEquals(values, placementFields)) field.onSelect.AddListener(_ => BeginNumericWorldEdit(field));
                    if (numeric(column)) AlignNumeric(input);
                    input.Tooltip(CommandBand.HasBoxLabels(labels) ? caption + " " + label : caption, tip);
                    var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
                    // The boxes share the row's width equally, whatever their content: the box's own preferred
                    // width, which follows its text, gives way to this element.
                    layout.minWidth = 0; layout.preferredWidth = 0; layout.flexibleWidth = 1; layout.layoutPriority = 2;
                }
            });
            return null;
        }
        // A field that keeps every character the modder types. The game's shared input field prefab dropped ':' from
        // a replacement ID, so a value that the mod's own rules check would otherwise never reach them as typed.
        static void FreeText(TMP_InputField field) {
            field.contentType = TMP_InputField.ContentType.Standard;
            field.characterValidation = TMP_InputField.CharacterValidation.None;
            field.inputValidator = null;
            field.onValidateInput = null;
        }
        // The label of an input field and the alignment of a number in it, as the native inspectors build them.
        RectTransform FieldLabel(UIPanelBuilder builder, string label) => builder.AddLabel("<style=FieldLabel>" + Literal(label) + "</style>");
        static void AlignNumeric(RectTransform input) {
            var text = input.GetComponent<TMP_InputField>()?.textComponent;
            if (text != null) text.alignment = TextAlignmentOptions.Right;
        }
        // Reserve a full native H3 line for the name. The tooltip gives the full text supplied by the caller.
        // With wrap, the text wraps at the heading's size and takes the height its lines need, so it is shown in full.
        RectTransform Heading(UIPanelBuilder builder, string markup, string title, string full, bool wrap = false) {
            var line = builder.AddLabel("<style=H3>" + markup + "</style>").TextWrap(wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis, wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap);
            if (!String.IsNullOrEmpty(full)) line.Tooltip(title, full);
            return line;
        }
        // Remeasure when the native layout gives this label a different width.
        static void FitEquipmentText(TMP_Text text, string family, string name, Func<string, string> format, Func<float> parentWidth = null) {
            var lastWidth = 0f;
            var shown = format(name);
            text.gameObject.AddComponent<TextUpdater>().Configure(() => {
                var width = (parentWidth == null ? text.rectTransform.rect.width : parentWidth()) - text.margin.x - text.margin.z - 1;
                if (width > 0 && width != lastWidth) {
                    lastWidth = width;
                    shown = format(InventoryPresentationState.NarrowName(family, name, width, value => text.GetPreferredValues(format(value)).x));
                }
                return shown;
            }, 0.1f);
        }
        // One line of fixed height. Text that does not fit ends in an ellipsis and is complete in the tooltip.
        RectTransform Line(UIPanelBuilder builder, string markup, string title, string full, float height = LineHeight) {
            var line = builder.AddLabel(markup).TextWrap(TextOverflowModes.Ellipsis, TextWrappingModes.NoWrap).Height(height);
            if (!String.IsNullOrEmpty(full)) line.Tooltip(title, full);
            return line;
        }
        const string GuideRoot = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/";

        void DrawWindow(UIPanelBuilder builder) {
            // The compact window draws its own content unless the file panel locks the window.
            if (presentation.Compact && !FileLocked) { DrawCompact(builder); return; }
            typed.Clear();
            builder.ConfigureLayout(4f);
            // The command band is split at the column divider: each column starts with its own band, so the left band is
            // as wide as the outliner and the right band as wide as the inspector, wherever the divider stands.
            var columns = builder.HStack(row => {
                // The first open of the window uses the default column width, as Reset layout does.
                if (presentation.FirstOpen && !firstOpenDone) { firstOpenDone = true; outlinerWidth = OutlinerDefaultWidth; ForgetOutlinerWidth(); }
                // The stored width is the user's; a window too narrow for it shows less without overwriting it.
                var width = Mathf.Clamp(outlinerWidth, OutlinerMinimumWidth, OutlinerMaximum());
                leftBandWidth = width;
                var outlinerRect = row.VStack(DrawOutliner);
                // While the file panel is open, the outliner waits for it; Refresh alone stays available.
                if (FileLocked) { DisableDuringRepair(outlinerRect); LockControls(outlinerRect, refreshButton); }
                // While the blank object window of Duplicate is open, the outliner waits for Place or Cancel; a click on
                // a row asks whether to cancel the duplicate (AskBeforeOutlinerSelection).
                else if (Duplicating) LockControls(outlinerRect, true);
                var outliner = outlinerRect.gameObject.AddComponent<LayoutElement>();
                outliner.minWidth = width; outliner.preferredWidth = width; outliner.flexibleWidth = 0; outliner.flexibleHeight = 10000;
                presentation.Divider(row, outliner, OutlinerMinimumWidth, OutlinerMaximum, () => outlinerWidth, width => { outlinerWidth = width; StoreOutlinerWidth(width); });
                // The explicit widths keep long inspector text from claiming the width of the outliner.
                var inspectorRect = row.VStack(DrawInspector);
                var inspector = inspectorRect.gameObject.AddComponent<LayoutElement>();
                inspector.minWidth = InspectorMinimumWidth; inspector.preferredWidth = InspectorMinimumWidth; inspector.flexibleWidth = 10000; inspector.flexibleHeight = 10000;
            }).gameObject.AddComponent<LayoutElement>();
            columns.flexibleWidth = 10000; columns.flexibleHeight = 10000;
        }
        bool firstOpenDone;
        RectTransform refreshButton;
        static void DisableDuringRepair(RectTransform rect) {
            var group = rect.GetComponent<CanvasGroup>() ?? rect.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
        }
        static void AllowDuringRepair(RectTransform rect) {
            var group = rect.GetComponent<CanvasGroup>() ?? rect.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
        }
        float leftBandWidth = OutlinerDefaultWidth;
        // What the window leaves for the outliner beside the divider, the row spacing and the inspector's minimum.
        float OutlinerMaximum() => Mathf.Max(OutlinerMinimumWidth, presentation.ContentWidth - NativePresentation.DividerWidth - InspectorMinimumWidth - 26);

        // The left band, over the outliner: Refresh and the two status lamps.
        void DrawLeftBand(UIPanelBuilder builder) {
            var stale = inventory == null || inventory.IsStale || definitionsStale;
            var unsaved = Unsaved(out var saveText);
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var refresh = Button(row, "Refresh", () => { CheckFileVersion(); if (!FileLocked) { OutlinerGrouping.InvalidatePlaces(); Inspect(); } });
                refreshButton = refresh.RectTransform;
                if (FileLocked) AllowDuringRepair(refresh.RectTransform);
                refresh.Tooltip("Refresh", "Read the scene, scene patch file, and supplied definitions again without applying changes.");
                var lamps = new[] { CommandBand.Inventory(stale, !FileLocked), CommandBand.Save(unsaved, attachment != null && attachment.Session.SaveRefusal == null) };
                var reasons = new[] { FileLocked ? FileLockReason : InventoryPresentationState.Notice(inventory, definitionsStale), saveText };
                var reserved = new[] { CommandBand.InventoryWord, CommandBand.SaveWord };
                // The decision is made from fixed widths before anything is built: a band that cannot hold both words
                // keeps each lamp's dot and leaves the word to the tooltip.
                var fit = CommandBand.WordsFit(leftBandWidth);
                for (var i = 0; i < 2; i++) {
                    var index = i;
                    var tooltip = CommandBand.LampTooltip(lamps[i], reasons[i]);
                    var lamp = row.HStack(parts => {
                        parts.ConfigureLayout(CommandBand.Gap);
                        if (!fit) return;
                        // The word has no frame and no action. Its rectangle is as wide as the lamp's longer word, so
                        // the other word takes its place without moving anything; a word never wraps or is cut off.
                        parts.AddLabel(Tint(lamps[index].Color, lamps[index].Word)).TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap)
                            .Width(reserved[index]).Height(RowHeight).Tooltip(tooltip.Title, tooltip.Body);
                    });
                    // The tooltip sits on the dot and on the word themselves, the objects the pointer rests on.
                    AddLampDot(lamp, lamps[i].Color).Tooltip(tooltip.Title, tooltip.Body);
                    lamp.ChildrenKeepHeight().Height(RowHeight);
                    // A lamp keeps its own width, so a row that is short of room cannot pull it towards Refresh.
                    if (fit) { var own = lamp.GetComponent<LayoutElement>() ?? lamp.gameObject.AddComponent<LayoutElement>(); own.minWidth = own.preferredWidth = index == 0 ? CommandBand.InventoryLamp : CommandBand.SaveLamp; own.flexibleWidth = 0; }
                }
                row.Spacer();
            }).Height(RowHeight);
        }
        // A lamp's dot: a round image in the lamp's colour, before its word. It is an image, not a button.
        static Sprite lampSprite;
        static RectTransform AddLampDot(RectTransform lamp, string color) {
            if (lampSprite == null) {
                // The row centres the dot's rectangle on its height, and the word's letters sit above that centre, so a
                // dot in the middle of the row stood below its word. The image is taller than the disc by LampRaise at
                // its lower edge: its rectangle stays centred and the disc stands that much higher, on the word's line.
                const int size = 32;
                var height = Mathf.RoundToInt(size * (CommandBand.Dot + LampRaise) / CommandBand.Dot);
                var texture = new Texture2D(size, height, TextureFormat.RGBA32, false) { name = "CrankyAnt Scene Patcher lamp", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * height];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < size; x++) {
                        // One pixel of soft edge around a filled disc in the upper part of the image; rows count from the bottom.
                        var distance = Mathf.Sqrt((x + 0.5f - size * 0.5f) * (x + 0.5f - size * 0.5f) + (y + 0.5f - (height - size * 0.5f)) * (y + 0.5f - (height - size * 0.5f)));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(size * 0.5f - 0.5f - distance)));
                    }
                texture.SetPixels32(pixels); texture.Apply();
                lampSprite = Sprite.Create(texture, new Rect(0, 0, size, height), new Vector2(0.5f, 0.5f), 100f);
                lampSprite.name = texture.name; lampSprite.hideFlags = HideFlags.DontSave;
            }
            // The game shows the tooltip of the first object under the pointer that is on the UI layer. A stack the builder
            // makes is on the default layer, so a dot that took its parent's layer was skipped and the window behind it,
            // which has no tooltip, answered instead.
            var ui = LayerMask.NameToLayer("UI");
            var dot = new GameObject("Lamp dot", typeof(RectTransform), typeof(Image)) { layer = ui >= 0 ? ui : lamp.gameObject.layer };
            dot.transform.SetParent(lamp, false);
            dot.transform.SetAsFirstSibling();
            var image = dot.GetComponent<Image>();
            image.sprite = lampSprite; image.preserveAspect = true; image.raycastTarget = true;
            image.color = ColorUtility.TryParseHtmlString(color, out var parsed) ? parsed : Color.white;
            var layout = dot.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = CommandBand.Dot; layout.minHeight = layout.preferredHeight = CommandBand.Dot + LampRaise; layout.flexibleWidth = 0; layout.flexibleHeight = 0;
            return (RectTransform)dot.transform;
        }
        // The right band, over the inspector: the commands for the inspection. A command that cannot run stays in
        // place, greyed out, with its reason. The session details and Reset layout are window commands and stand in
        // the title bar.
        RectTransform DrawRightBand(UIPanelBuilder builder) {
            return builder.HStack(row => {
                row.ConfigureLayout(4f);
                var pending = selection?.Pending != null;
                var select = CommandBand.SelectInWorld(selection != null, pending);
                Button(row, "Select in world", () => BeginSelection(SelectionKind.SceneObject, null)).Inactive(select != null)
                    .Tooltip("Select in world", CommandBand.Tooltip("Click a marking in the world to choose the object to inspect.", select));
                // A crossing node of the outliner is a complex: Focus goes to the crossing.
                var groupNode = selected == null ? crossingNode : null;
                var focus = groupNode != null ? (groupNode.Point == null ? "This crossing has no position." : null) : CommandBand.Focus(selected != null, FocusUnavailable());
                var starter = selected != null && selected.Family == StarterPlacements.Family;
                Button(row, "Focus", () => { if (selected != null) Focus(selected); else FocusCrossingNode(); }).Inactive(focus != null)
                    .Tooltip("Focus", CommandBand.Tooltip(groupNode != null ? "Move the camera to the selected crossing." : starter ? "Move the camera to the marker's track point, where the start setup places its cars." : "Move the camera to the inspected object.", focus));
                // Beside Focus, as the native map editor has Focus and Clear on its selection row.
                var keepDraft = guided.Enabled && (guided.Step == GuidedTaskStep.Values || guided.Step == GuidedTaskStep.Review);
                var clear = keepDraft && !pending ? "No world choice is pending." : CommandBand.Clear(selected != null, pending);
                Button(row, "Clear", ClearInspection).Inactive(clear != null)
                    .Tooltip("Clear", CommandBand.Tooltip(keepDraft ? "Cancel the world choice and keep the object and draft." : "End the inspection and cancel a pending world choice, gizmo, or snap. Applied changes stay.", clear));
                var copy = CommandBand.CopyId(selected != null, attachment?.Session.CopyIdRefusal);
                Button(row, "Copy ID", CopyIdentity).Inactive(copy != null)
                    .Tooltip("Copy ID", CommandBand.Tooltip("Copy the object's ID, placement, and the map ID needed by the scene patch file.", copy));
                row.Spacer();
                // The window's "?" belongs in the title bar; it stands here only when the title bar could not take it.
                // Like the title bar's, it stays available while the file panel locks the other controls.
                windowHelpButton = presentation.TitleHelp ? null : HelpButton(row, NativePresentation.WindowHelpRegion, "Window");
            }).Height(RowHeight);
        }
        // The same button opens and closes its region's help.
        RectTransform windowHelpButton;
        RectTransform HelpButton(UIPanelBuilder row, string region, string scope) {
            var open = presentation.HelpRegion == region;
            var button = Button(row, "?", () => presentation.ToggleHelp(region));
            if (FitButton(button, "?") == 0) button.Width(28);
            button.Tooltip(scope + " help", open ? "Close the " + scope.ToLowerInvariant() + " help." : "Explain the " + scope.ToLowerInvariant() + ".");
            return button.RectTransform;
        }
        bool Unsaved(out string text) {
            if (attachment == null) { text = String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : editingStatus; return false; }
            try {
                var outstanding = attachment.Session.Outstanding(attachment.NativeDirty);
                text = CommandBand.SaveNotice(outstanding, attachment.Session.VersionState, attachment.Session.VersionFileMissing);
                return outstanding.Count != 0;
            } catch (Exception e) { text = "Outstanding work unknown: " + e.GetBaseException().Message; return true; }
        }
        // The copy leaves no trace in the world, so the footer and the log say that it happened and for which object.
        void CopyIdentity() {
            if (selected == null) return;
            try {
                GUIUtility.systemCopyBuffer = Identity(selected);
                feedback.Report(EditFeedback.CopyIdPlace, "Copied the ID for " + selected.DisplayName + ".");
            } catch (Exception e) { feedback.Report(EditFeedback.CopyIdPlace, "Could not copy the ID: " + e.GetBaseException().Message); }
        }

        // A light, gate or bell the patcher added: its name is fitted to the width and stands for its path. Every added
        // object, this mod's mod objects and their parts included, takes "+" and + ADDED (IsAdded).
        bool IsAddedEquipment(SceneObservation entry) => CommandBand.IsAddedFamily(entry?.Family);
        // The edited mod's ID, or null while no mod is edited.
        string EditedModId { get { try { return attachment?.Session.ModId; } catch { return null; } } }
        // The key the edited mod's scene patch file is applied under, which scopes the lights, gates and bells it adds,
        // or null while no mod is edited.
        string EditedScenePatchFileKey { get { try { return attachment?.Session.ScenePatchFileKey; } catch { return null; } } }
        bool IsAdded(SceneObservation entry) => InventoryPresentationState.IsAdded(entry, EditedModId);
        bool IsChanged(SceneObservation entry) {
            try { return attachment != null && attachment.Session.IsChanged(entry.Identity); } catch { return false; }
        }
        // How this mod's modification of the object is not applied, or null: the object then shows that, not a change.
        FailureKind? FailureOf(SceneObservation entry) {
            try { return attachment == null || entry == null ? null : CommandBand.ShownFailure(attachment.Session.IsChanged(entry.Identity), attachment.Session.NotApplied(entry.Identity)?.Kind); } catch { return null; }
        }
        void DrawOutliner(UIPanelBuilder builder) {
            builder.ConfigureLayout(4f);
            DrawLeftBand(builder);
            UIPanel results = null;
            builder.HStack(row => {
                // The search field takes the width the two buttons leave, whatever it holds: its own least and
                // preferred widths follow its content, the clear button that comes with text included, and they
                // moved "?" and Fit and pushed Fit past the column's edge. This element decides instead.
                var search = row.AddFilterField(filter, value => { filter = value; results?.SetNeedsRebuild(); }, null, true);
                // A fixed title on the field itself, so its tooltip never shows the query or nothing.
                var searchInput = search.GetComponentInChildren<TMP_InputField>(true);
                var searchTip = (searchInput != null ? searchInput.gameObject : search.gameObject);
                var searchProvider = searchTip.GetComponent<UI.Tooltips.UITooltipProvider>() ?? searchTip.AddComponent<UI.Tooltips.UITooltipProvider>();
                searchProvider.TooltipInfo = new TooltipInfo(CommandBand.SearchTitle, CommandBand.SearchTip);
                var searchWidth = search.GetComponent<LayoutElement>() ?? search.gameObject.AddComponent<LayoutElement>();
                searchWidth.minWidth = 0; searchWidth.preferredWidth = 0; searchWidth.flexibleWidth = 1; searchWidth.layoutPriority = 2;
                HelpButton(row, "outliner", "Outliner");
                // Beside the divider, because it sets the column width. The native builder's icon buttons take a sprite
                // name from the game's assets and none of the known ones means width, so the control is a short text.
                IConfigurableElement fit = null;
                fit = Button(row, "Fit", () => FitOutliner(fit.RectTransform.GetComponentInChildren<TMP_Text>(true)));
                fit.Tooltip("Fit names", "Size this column to most names shown. Drag the divider for another width.");
            }).Height(30);
            builder.AddSegmentedControlCompact(segments => {
                // All first, then the rest by name.
                segments.AddSegment(CommandBand.AllFilter, outlinerFilter == OutlinerFilter.All, presentation.Action(() => outlinerFilter = OutlinerFilter.All)).Tooltip(CommandBand.AllFilter, "Show all objects.");
                segments.AddSegment(CommandBand.ChangedFilter, outlinerFilter == OutlinerFilter.Changed, presentation.Action(() => outlinerFilter = OutlinerFilter.Changed)).Tooltip(CommandBand.ChangedFilter, "Show the objects this mod changes.");
                segments.AddSegment(CommandBand.DisabledFilter, outlinerFilter == OutlinerFilter.Disabled, presentation.Action(() => outlinerFilter = OutlinerFilter.Disabled)).Tooltip(CommandBand.DisabledFilter, "Show the objects that are switched off, whatever their parents are.");
                segments.AddSegment(CommandBand.ProblemsFilter, outlinerFilter == OutlinerFilter.Problems, presentation.Action(() => outlinerFilter = OutlinerFilter.Problems)).Tooltip(CommandBand.ProblemsFilter, "Show the objects with a detected problem.");
            });
            // The object count line with the view switch stands outside the rebuildable list, so typing in Search
            // updates the count without rebuilding the switch.
            TMP_Text measure = null;
            builder.HStack(countRow => {
                countRow.ConfigureLayout(4f);
                var countLine = Line(countRow, "<style=Footnote></style>", "Objects", "Inactive loaded objects are included.", RowHeight);
                measure = countLine.GetComponent<TMP_Text>();
                if (measure != null) measure.verticalAlignment = VerticalAlignmentOptions.Middle;
                var grow = countLine.GetComponent<LayoutElement>() ?? countLine.gameObject.AddComponent<LayoutElement>();
                grow.flexibleWidth = 1; grow.minWidth = 0;
                DrawViewSwitch(countRow);
            }).ChildrenKeepHeight().Height(RowHeight);
            results = DrawOutlinerRows(builder, measure, summary => "<style=Footnote>" + Literal(summary) + "</style>");
            // The window help expands on these three reminders.
            builder.AddHRule();
            builder.Spacer(4f);
            Line(builder, "<style=Footnote>Save, Undo, Redo: Map Editor</style>", null, null);
            Line(builder, "<style=Footnote>Refresh after native edits</style>", null, null);
            Line(builder, "<style=Footnote>Reload clears Undo history</style>", null, null);
            builder.Spacer(4f);
        }
        // One tree across families: its top level is the categories or the places. Null when it couldn't be built;
        // shown holds the objects the search and filter let through.
        OutlinerGrouping.Result OutlinerTree(out IReadOnlyList<SceneObservation> shown) {
            shown = Array.Empty<SceneObservation>();
            var narrowed = !String.IsNullOrEmpty(filter) || outlinerFilter != OutlinerFilter.All;
            try {
                shown = OutlinerGrouping.Rows(inventory, filter, outlinerFilter, InventoryPresentationState.ChangedOrMade(IsChanged, EditedModId, EditedScenePatchFileKey), OutlinerTrackPoint);
                return OutlinerGrouping.Build(shown, inventory?.Entries, filter, narrowed, OutlinerTrackPoint);
            } catch (Exception e) when (!(e is OutOfMemoryException)) { PatcherLog.Error("The patcher couldn't build the scene object outliner. Report this to the patcher maintainer.", e); return null; }
        }
        // The object count line's text. The count is that of the tree: scene objects that only hold other groups and native track spans have no row.
        string OutlinerSummary(OutlinerGrouping.Result tree) =>
            inventory == null ? "Inspection is unavailable" : tree == null ? "The patcher couldn't build the outliner. Check the Player log for the cause." : OutlinerGrouping.Summary(tree, inventory.Entries);
        // The outliner's rows, in a section that rebuilds by itself. countLine shows the object count, as countMarkup
        // words it. The full window and the compact rail share it.
        UIPanel DrawOutlinerRows(UIPanelBuilder builder, TMP_Text countLine, Func<string, string> countMarkup) {
            return builder.AddRebuildableSection(items => {
                items.ConfigureLayout(4f);
                var rows = new List<UIOutlineItem>();
                var byKey = new Dictionary<string, SceneObservation>();
                // The added rows by their full text, for OutlineNameFit.
                var fittedRows = new Dictionary<string, InventoryPresentationState.FittedRow>(StringComparer.Ordinal);
                // The crossing nodes, by the key of their outline row.
                var crossingKeys = new Dictionary<string, string>();
                string selectedKey = null;
                var narrowed = !String.IsNullOrEmpty(filter) || outlinerFilter != OutlinerFilter.All;
                var state = narrowed ? filteredOutline : inventoryOutline;
                var indices = new Dictionary<SceneObservation, int>();
                if (inventory != null) for (var i = 0; i < inventory.Entries.Count; i++) indices[inventory.Entries[i]] = i;
                var tree = OutlinerTree(out var shown);
                var summary = OutlinerSummary(tree);
                if (countLine != null) countLine.text = countMarkup(summary);
                // The debug lines of the tree are written once the inventory holds objects, and again after a view switch.
                if (tree != null && OutlinerGrouping.LogPending && inventory != null && inventory.Entries.Count > 0) {
                    OutlinerGrouping.LogPending = false;
                    OutlinerGrouping.Log(tree, shown.Count, filter, outlinerFilter.ToString());
                }
                var view = OutlinerGrouping.ByPlace ? "place/" : "category/";
                // One row: label is the name the row shows, and above the keys of the folders and rows it stands in.
                // dimmed marks a group shown only to hold a matching part, which is no match itself.
                UIOutlineItem Row(SceneObservation item, string label, IEnumerable<string> above, bool dimmed) {
                    var key = indices[item].ToString(CultureInfo.InvariantCulture);
                    byKey[key] = item;
                    if (ReferenceEquals(selected, item)) {
                        selectedKey = key;
                        // A newly inspected object is revealed once; afterwards its folder stays as the user leaves it. An edit
                        // that moves it to another place reveals it there.
                        var folders = String.Join("\n", above);
                        if (!ReferenceEquals(revealed, item.Handle) || revealedIn != folders) { revealed = item.Handle; revealedIn = folders; foreach (var open in above) state.SetExpanded(open, true); }
                    }
                    var changed = IsChanged(item); var added = IsAdded(item); var failure = CommandBand.ShownFailure(changed, changed ? FailureOf(item) : null);
                    // The native outline appends " *" and " +" to a name. Here the markers stand before the name, so
                    // that they stay visible when the end of a long name is cut off; they take room only on a row
                    // that has them. "*" marks a changed object and never unsaved work. An object without a
                    // supported edit says so, and an inactive object is italic and says so.
                    var markers = CommandBand.RowMarkers(item.Problems.Count != 0, changed, added, failure);
                    var mark = InventoryPresentationState.RowMark(item);
                    var context = mark == null ? "" : "  <alpha=#99>" + mark + "<alpha=#FF>";
                    var prefix = markers.Replace("!", "<color=" + Red + "><b>!</b></color>").Replace("*", "<color=" + Amber + "><b>*</b></color>").Replace("+", "<color=" + Green + "><b>+</b></color>").Replace("×", "<color=" + Red + "><b>×</b></color>") + (markers.Length == 0 ? "" : " ");
                    string RowMarkup(string value) {
                        var text = item.Active ? Literal(value) : "<i>" + Literal(value) + "</i>";
                        if (dimmed) text = "<alpha=#99>" + text + "<alpha=#FF>";
                        return prefix + text + context + (item.Active ? "" : "  <alpha=#99>inactive<alpha=#FF>");
                    }
                    // The row shows its full name. Its cell fits an added name to the width it has (OutlineNameFit).
                    var fitted = new InventoryPresentationState.FittedRow(item.Family, label, RowMarkup);
                    if (CommandBand.IsAddedFamily(item.Family)) fittedRows[fitted.Full] = fitted;
                    return new UIOutlineItem(key, fitted.Full, item) { Tooltip = OutlinerGrouping.Tooltip(tree, item, changed, failure) };
                }
                // A folder shows its count first, so a narrow column cuts the name and never the count. A crossing
                // without a controller is a folder that can be selected.
                void Nest(UIOutlineItem holder, InventoryPresentationState.OutlineNode node, string[] above) {
                    UIOutlineItem made; string key;
                    if (node.Row == null) {
                        key = view + node.Key;
                        made = new UIOutlineItem(key, "<alpha=#99>(" + node.Count.ToString(CultureInfo.InvariantCulture) + ")<alpha=#FF> " + Literal(node.Label)) { Tooltip = OutlinerGrouping.FolderTooltip(node) };
                        if (tree.GroupNodes.ContainsKey(node.Key)) {
                            crossingKeys[key] = node.Key;
                            if (selected == null && crossingNodeKey == node.Key) { selectedKey = key; crossingNode = tree.GroupNodes[node.Key]; }
                        }
                    } else {
                        key = indices[node.Row].ToString(CultureInfo.InvariantCulture);
                        made = Row(node.Row, node.Label, above, node.Context);
                    }
                    if (holder == null) rows.Add(made); else holder.AddChild(made);
                    var below = above.Concat(new[] { key }).ToArray();
                    foreach (var child in node.Children) Nest(made, child, below);
                }
                // On a map where the patcher lists nothing because the game creates every checked scene object while it
                // runs, the area states that reason instead of an empty list.
                var emptyReason = InventoryPresentationState.EmptyListReason(inventory);
                if (emptyReason != null) {
                    var reasonLine = items.AddLabel(Literal(emptyReason)).TextWrap(TextOverflowModes.Overflow, TextWrappingModes.Normal);
                    var reasonLayout = reasonLine.GetComponent<LayoutElement>() ?? reasonLine.gameObject.AddComponent<LayoutElement>();
                    reasonLayout.minHeight = CommandBand.OutlinerLeastHeight; reasonLayout.flexibleHeight = 10000;
                    foreach (var text in reasonLine.GetComponentsInChildren<TMP_Text>(true)) text.verticalAlignment = VerticalAlignmentOptions.Top;
                    presentation.OutlinerView = null;
                    return;
                }
                if (tree != null) foreach (var top in tree.Tops) Nest(null, top, new string[0]);
                var outline = items.AddOutlineView(rows, state, new UIState<string>(selectedKey), key => {
                    if (!byKey.TryGetValue(key, out var observation)) {
                        // A click on a crossing node selects the crossing, so Focus goes to it.
                        if (tree != null && crossingKeys.TryGetValue(key, out var nodeKey) && tree.GroupNodes.TryGetValue(nodeKey, out var node))
                            presentation.Action(() => { if (!AskBeforeOutlinerSelection(node, node.Label, () => SelectCrossingNode(nodeKey, node))) SelectCrossingNode(nodeKey, node); })();
                        return;
                    }
                    // While a mod object is unplaced, the modder first confirms that the duplicate is cancelled.
                    presentation.Action(() => { if (!AskBeforeOutlinerSelection(observation, observation.DisplayName, () => SelectOutlinerRow(observation, true))) SelectOutlinerRow(observation, false); })();
                });
                // A row's tooltip waits as long as a button's, also while the pointer moves from row to row.
                if (outline.GetComponent<OutlineTooltipDelay>() == null) outline.gameObject.AddComponent<OutlineTooltipDelay>();
                (outline.GetComponent<OutlineNameFit>() ?? outline.gameObject.AddComponent<OutlineNameFit>()).Use(fittedRows);
                var listLayout = outline.GetComponent<LayoutElement>();
                listLayout.minHeight = CommandBand.OutlinerLeastHeight; listLayout.flexibleHeight = 10000;
                // The compact rail trims this view to whole rows.
                presentation.OutlinerView = outline.GetComponent<ScrollRect>();
            });
        }
        // A click on an object's row. After a cancelled duplicate the source is still inspected, and a click on its row
        // keeps it inspected instead of clearing the inspection.
        void SelectOutlinerRow(SceneObservation observation, bool afterDuplicate) {
            if (!GuidedSelect(observation)) return;
            crossingNodeKey = null; crossingNode = null;
            // A click on the row that is inspected already clears the inspection.
            if (CommandBand.ClearsInspection(selected, observation)) { if (!afterDuplicate) ClearInspection(); return; }
            if (!inventoryPresentation.CanSelect(inventory, observation)) return;
            CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; CloseEquipmentForm(); selected = inventoryPresentation.CurrentSelection(inventory, observation); ReadPlacement();
        }
        void FitOutliner(TMP_Text measure) {
            if (measure == null || inventory == null) return;
            // The row decoration covers one folder level; each deeper level of the tree adds its indent.
            var widths = OutlinerGrouping.FitLabels().Select(label => measure.GetPreferredValues(label.Key).x + Math.Max(0, label.Value - 1) * OutlineIndent).ToArray();
            var divider = NativePresentation.DividerWidth + 26;
            outlinerWidth = InventoryPresentationState.FitWidth(widths, RowDecoration, presentation.ContentWidth, OutlinerMinimumWidth, InspectorMinimumWidth, divider);
            StoreOutlinerWidth(outlinerWidth);
            // The same fit without a window limit tells whether the cap stopped it.
            var wanted = InventoryPresentationState.FitWidth(widths, RowDecoration, 100000, OutlinerMinimumWidth, InspectorMinimumWidth, divider);
            if (wanted > outlinerWidth + 0.5f) feedback.Report("Fit", "Most names need " + Mathf.RoundToInt(wanted) + "; the column stops at " + Mathf.RoundToInt(outlinerWidth) + ", 40 percent of the window. " + WindowGuidance.WidenWindow);
        }

        void DrawInspector(UIPanelBuilder builder) {
            builder.ConfigureLayout(4f);
            severalModifications = SeveralModifications(selected?.Identity);
            LockControls(DrawRightBand(builder), windowHelpButton);
            // The blank object window of Duplicate takes the inspector's place until the object is placed or Cancel ends it.
            if (Duplicating && !FileLocked) { DrawDuplicate(builder); DrawFooter(builder); return; }
            // While the file panel waits for a decision and nothing is inspected, its own heading is the first heading:
            // the empty inspector's heading and instruction would only stand above it.
            if (FileLocked && selected == null) { DrawFilePanel(builder); DrawFooter(builder); return; }
            RectTransform headingRow;
            if (selected == null) {
                // Keep the instruction across the full width while Guided is available without an inspected object.
                headingRow = builder.HStack(row => {
                    EmptyHeading(row).EqualFlexibleWidth(0);
                    DrawModeSwitch(row);
                }).ChildrenKeepHeight().Height(RowHeight);
            } else headingRow = ObjectHeading(builder).Height(RowHeight);
            LockControls(headingRow);
            var state = StateLine(out var states);
            // The instruction while nothing is inspected wraps at the heading's size and takes the height its lines need, so it
            // is shown in full at any inspector width: one H3 line holds only part of it at the default window width.
            if (selected == null) {
                var instruction = DuplicateState.EmptyInstruction(CrossingNodeLabel());
                if (instruction != null) Heading(builder, instruction, null, null, wrap: true);
                if (crossingNode?.Identity != null && !FileLocked && !guided.Enabled) DrawCrossingNode(builder);
            }
            else LockControls(builder.HStack(row => {
                StateLineRow(row, state, states).EqualFlexibleWidth(0);
                DrawModeSwitch(row);
            }).ChildrenKeepHeight().Height(RowHeight));
            // The file panel takes the place of the tabs and the tab body, and of the guided steps, until it is resolved.
            if (FileLocked) DrawFilePanel(builder);
            else if (guided.Enabled) DrawGuided(builder);
            else {
                builder.AddTabbedPanels(inspectorTab, tabs => {
                    tabs.AddTab("Object", ObjectTab, body => Tab(body, ObjectTab, DrawObject));
                    tabs.AddTab("Placement", PlacementTab, body => Tab(body, PlacementTab, DrawPlacement));
                    tabs.AddTab("Connections", ConnectionsTab, body => Tab(body, ConnectionsTab, DrawConnections));
                    tabs.AddTab("Changes", ChangesTab, body => Tab(body, ChangesTab, DrawChanges));
                });
            }
            // The native tab strip gives each tab the share of its label's width. In a narrow strip a title ends in an
            // ellipsis inside 8 units of padding; the four titles are never renamed.
            var tabView = headingRow.parent.GetComponentInChildren<UI.TabView.TabView>(true);
            if (tabView != null) {
                // Give the tab container a fixed preferred height so the body, through its flexible height,
                // takes the remainder after the pinned rows and fixed footer.
                var tabsLayout = tabView.GetComponent<LayoutElement>() ?? tabView.gameObject.AddComponent<LayoutElement>();
                tabsLayout.minHeight = tabsLayout.preferredHeight = TabStripHeight + TabBodyMinimumHeight + RowHeight;
                tabsLayout.flexibleHeight = 10000;
            }
            if (tabView != null) foreach (var toggle in tabView.GetComponentsInChildren<Toggle>(true)) {
                var strip = toggle.transform.parent as RectTransform;
                if (strip != null && strip.parent == tabView.transform) strip.Height(TabStripHeight);
                // Step titles show progress; Back and Next alone navigate the guided steps.
                // A disabled Toggle component ignores pointer and keyboard input without Selectable's disabled tint.
                // TabView has already set its selected title to alpha 1 and the others to alpha .6.
                if (guided.Enabled) toggle.enabled = false;
                var title = toggle.GetComponentInChildren<TMP_Text>(true);
                if (title == null) continue;
                title.overflowMode = TextOverflowModes.Ellipsis; title.textWrappingMode = TextWrappingModes.NoWrap;
                title.margin = new Vector4(8, title.margin.y, 8, title.margin.w);
            }
            DrawFooter(builder);
        }
        void DrawModeSwitch(UIPanelBuilder row) => row.AddSegmentedControlCompact(modes => {
            modes.AddSegment("Inspector", !guided.Enabled, presentation.Action(LeaveGuided))
                .Tooltip("Inspector", "Show the object's values and edits in tabs. An unfinished guided task ends without writing anything.");
            modes.AddSegment("Guided", guided.Enabled, presentation.Action(OpenGuided))
                .Tooltip("Guided", "Complete one edit step by step: Target, Values, Review, and Apply.");
        }, true);
        // One line under the name: the object's badges and problem count, then its family and states, in the native status
        // colors. The line ends in an ellipsis beside the Inspector and Guided switch, so what is cut off is the family's end,
        // never a badge (CommandBand.StateParts).
        // The inspected object's name as the heading, with its path in the tooltip; an added object's name is fitted to
        // the width. The full window and the compact rail share it.
        RectTransform ObjectHeading(UIPanelBuilder builder) {
            var name = OutlinerGrouping.InspectorName(selected, inventory, OutlinerTrackPoint);
            var added = IsAddedEquipment(selected);
            var heading = Heading(builder, Literal(name), "Object", added ? name : selected.Identity.Path);
            if (added) FitEquipmentText(heading.GetComponent<TMP_Text>(), selected.Family, name, value => "<style=H3>" + Literal(value) + "</style>");
            return heading;
        }
        // The state line of an inspected object, one row high, with the full states in its tooltip.
        RectTransform StateLineRow(UIPanelBuilder builder, string state, string states) => Line(builder, state, "State", states, RowHeight);
        string StateLine(out string plain) {
            if (selected == null) { plain = CommandBand.SelectInstruction; return plain; }
            var text = new List<string> { CommandBand.ShownFamily(selected) };
            if (!selected.Active) text.Add("Inactive");
            // The native map editor's badges, as its inspector shows them beside the row marker.
            var badges = CommandBand.Badges(IsChanged(selected), IsAdded(selected), FailureOf(selected));
            foreach (var badge in badges) text.Add(ReferenceEquals(badge, CommandBand.Changed) ? "Changed by this mod" : ReferenceEquals(badge, CommandBand.Refused) ? "Modification refused" : ReferenceEquals(badge, CommandBand.NotApplied) ? "Modification not applied" : "Added by this mod");
            if (selected.InspectionOnly != null) text.Add(CommandBand.InspectionOnlyLine(selected.InspectionOnly));
            var count = selected.Problems.Count == 0 ? null : selected.Problems.Count == 1 ? "1 problem" : selected.Problems.Count + " problems";
            if (count != null) text.Add(count);
            plain = String.Join("; ", text);
            if (!IsAddedEquipment(selected)) plain += "\n" + selected.Identity.Path;
            var parts = CommandBand.StateParts(Literal(CommandBand.ShownFamily(selected)), selected.Active ? null : "Inactive", badges.Select(badge => badge.Markup),
                selected.InspectionOnly == null ? null : "Inspection only", count == null ? null : "<color=" + Red + ">" + count + "</color>");
            return String.Join("   ", parts);
        }
        // A tab body scrolls inside its own rectangle. Its first row names what the tab is for and holds the tab's own
        // "?", so that help visibly belongs to the tab; the help opens in its own window. Below the scrolling part
        // the tab's own actions stand in a pinned row of fixed height, inside the tab, so that their place says they
        // belong to the tab; the row stands just above the footer's divider, which sets it apart from the footer, whose
        // row is the same on every tab. The native map editor puts a tool's actions directly under its fields; here they
        // stay in view while the fields scroll.
        void Tab(UIPanelBuilder builder, string id, Action<UIPanelBuilder> body) {
            var view = presentation.Scroll(builder, TabBodyMinimumHeight, content => {
                content.ConfigureLayout(4f);
                var help = Help[id];
                content.HStack(row => {
                    row.AddLabel("<style=Footnote>" + help.Intro + "</style>").TextWrap(TextOverflowModes.Ellipsis, TextWrappingModes.Normal).Height(32).FlexibleWidth();
                    HelpButton(row, id, help.Title);
                }).Height(32);
                if (selected == null) content.AddExpandingVerticalSpacer();
                else body(content);
            });
            // The body takes all free height: its least height shows one role of a coverage form under the tab's
            // first rows, and the window's least height leaves it that.
            var fill = view.GetComponent<LayoutElement>();
            if (fill != null) { fill.minHeight = fill.preferredHeight = TabBodyMinimumHeight; fill.flexibleHeight = 10000; }
            // The viewport clips its scrolling contents above the separate action row.
            var viewport = view.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
            ActionRow(builder, TabActs(id), ChoiceBlock);
        }

        // A rail bridge outside Ela or a road bridge, or a part of one: it takes no
        // track-following placement.
        BridgeKind InspectedBridge => selected == null || !SceneObjectRules.InFamilies(selected.Family) ? BridgeKind.None
            : selected.GroupPath == null ? selected.Bridge : InventoryPresentationState.GroupOf(inventory, selected)?.Bridge ?? BridgeKind.None;
        bool IsRailBridge => InspectedBridge == BridgeKind.Rail;
        bool IsRoadBridge => InspectedBridge == BridgeKind.Road;
        // An explicit disable holds the inspected object off against a switch of the game: its track construction,
        // progression or a map feature. The amber warning shows on the Object tab and in the footer; null otherwise.
        string HoldWarning => selected?.Handle is GameObject held ? GroupWorld.HoldWarning(held, FollowsGroup) : null;
        void DrawOrigin(UIPanelBuilder builder) {
            var origin = InventoryPresentationState.ObjectOrigin(selected);
            Line(builder, "<style=FieldLabel>" + Literal(origin.Label) + "</style> " + Literal(origin.Value), origin.Label, origin.Value, LineHeight);
        }
        // The teleport name field of a duplicated spawn point, with Set beside it, and the note while it is empty. The
        // field keeps what the modder typed until another object is inspected.
        string teleportNameEntry; TargetIdentity teleportNameFor;
        const string TeleportNamePlace = "Teleport name", SetTeleportNameAction = "Set teleport name";
        void DrawTeleportName(UIPanelBuilder builder) {
            var identity = selected.Identity;
            if (teleportNameFor == null || !TargetIdentity.SameObject(teleportNameFor, identity)) { teleportNameFor = identity; teleportNameEntry = TeleportNameField.Value(selected); }
            var block = attachment != null && !attachment.Session.IsModObject(identity) ? TeleportNameField.OtherMod : EditBlock();
            FieldLabel(builder, TeleportNameField.Label);
            builder.HStack(row => {
                if (block != null) Line(row, Literal(TeleportNameField.Value(selected)), TeleportNameField.Label, block, 30).FlexibleWidth();
                else {
                    var input = row.AddInputField(teleportNameEntry ?? "", value => teleportNameEntry = value);
                    var field = input.GetComponent<TMP_InputField>();
                    FreeText(field);
                    field.onValueChanged.AddListener(value => teleportNameEntry = value);
                    input.Tooltip(TeleportNameField.Label, TeleportNameField.Tip);
                    input.FlexibleWidth();
                }
                Button(row, TeleportNameField.SetLabel, () => {
                    var entered = teleportNameEntry;
                    var refusal = TeleportNameField.Refusal(entered);
                    if (refusal != null) { feedback.Refuse(TeleportNamePlace, SetTeleportNameAction, refusal); return; }
                    Edit(TeleportNamePlace, SetTeleportNameAction, (session, inspection) => session.SetTeleportName(identity, entered, inspection));
                }).Inactive(block != null).Tooltip(TeleportNameField.SetLabel, CommandBand.Tooltip(TeleportNameField.SetTip, block));
            });
            if (TeleportNameField.Note(selected) is string note) PlainLabel(builder, note);
        }
        // The Object tab: what the object is, whether it is enabled, and what was detected about it.
        void DrawObject(UIPanelBuilder builder) {
            DrawOrigin(builder);
            // A part names its group first, with the button that inspects the group directly after the name.
            var partOf = InventoryPresentationState.PartOf(selected);
            if (partOf != null) {
                builder.HStack(row => {
                    Line(row, Literal(partOf), "Group", selected.GroupPath, RowHeight).EqualFlexibleWidth(0);
                    ActButton(row, SelectGroupAct());
                }).ChildrenKeepHeight().Height(RowHeight);
            }
            // A detection span names the crossing controller that holds it first, with the button that inspects the
            // controller: crossing coverage, the span's supported edit, is on that controller's Connections tab.
            var heldBy = InventoryPresentationState.HeldBy(selected);
            if (heldBy != null) {
                builder.HStack(row => {
                    Line(row, Literal(heldBy), "Grade crossing controller", selected.HolderPath, RowHeight).EqualFlexibleWidth(0);
                    ActButton(row, SelectControllerAct());
                }).ChildrenKeepHeight().Height(RowHeight);
            }
            // An object of a kind no supported family covers says so first, with the reason it is not yet supported.
            if (selected.InspectionOnly != null) {
                Section(builder, "Inspection only");
                PlainLabel(builder, selected.InspectionOnly);
            }
            // Problems first: the red count in the state line points at text that is in view.
            Section(builder, "Detected problems");
            // A stale inventory keeps the problems of its last refresh, also those another object's edit has solved.
            if (inventory == null || inventory.IsStale || definitionsStale) builder.AddLabel(Tint(Amber, CommandBand.StaleProblems));
            // Beside Repair duplicate ID, an inactive holder says why it has no problem, in a sentence that wraps.
            if (selected.Problems.Count == 0) {
                var offersRepair = CommandBand.OffersDuplicateRepair(selected.Identity.Structure, selected.Dependencies);
                if (offersRepair) PlainLabel(builder, CommandBand.NoDetectedProblems(true)); else Line(builder, CommandBand.NoDetectedProblems(false), null, null);
            }
            // After a replacement ID, a track binding problem that remains says that it is another problem.
            var repaired = selected.Problems.Count != 0 ? RepairedMarkerId() : null;
            var boundSegment = CommandBand.BindingSlotSegments(selected.Bindings).FirstOrDefault();
            // A generic track marker's binding is optional (BindingRules.Need), the one need a repaired marker has.
            foreach (var problem in selected.Problems) builder.AddLabel(Tint(Red, CommandBand.ProblemText(problem, repaired, boundSegment, selected.Enabled, BindingNeed.Optional)));
            if (selected.Problems.Count != 0) DrawProblemTasks(builder);
            // An inactive holder of a duplicate marker ID has no problem, and its explanation still offers the repair here.
            DrawDuplicateIdTasks(builder);
            // What the inspection found that is not a problem here, such as the detection findings of a crossing controller
            // that fails its pattern check, with the reason.
            if (selected.Information.Count != 0) {
                Section(builder, "Information");
                foreach (var line in selected.Information) PlainLabel(builder, Breakable(line));
            }
            // What the inspection could not read or check: nothing the modder can repair here, so no problem.
            if (selected.Notes.Count != 0) {
                Section(builder, "Inspection notes");
                foreach (var note in selected.Notes) PlainLabel(builder, Breakable(note));
            }
            // A scene object of the Scene object family, and each child object of it, is decorative: the game reads nothing from
            // where it stands. An industry's buildings and storage display say that the industry works on its native
            // track spans, and a storage pile names the storage it shows.
            if (selected.InspectionOnly == null && SceneObjectRules.InFamilies(selected.Family)) {
                Section(builder, "Use in the game");
                PlainLabel(builder, CommandBand.UseInGame(selected, selected.GroupPath != null ? InventoryPresentationState.GroupOf(inventory, selected) : null));
            }
            // A duplicated spawn point has a teleport name of its own: the name players reach it by.
            if (TeleportNameField.Shows(selected)) DrawTeleportName(builder);
            // A spawn point names each use the game has for it: the default start, rescue, teleport,
            // and the start setups that name it.
            if (selected.Family == SpawnPoints.Family) {
                Section(builder, "Use in the game");
                // The game's order of active spawn points comes from the inventory.
                foreach (var use in SpawnPoints.Uses(selected.Bindings, SpawnPointWorld.NativeSpawnCount(), SpawnPoints.Rank(selected, inventory?.Entries))) PlainLabel(builder, use);
            }
            Section(builder, "State");
            if (HoldWarning is string warning) builder.AddLabel(Tint(Amber, warning));
            // A top scene object's own flag can be off while only its simple culler hides it; the observation reads the authored flag.
            Field(builder, "Enabled", EnabledText()).Tooltip("Enabled", "Whether this object is active in the scene.");
            if (FollowsGroup) Field(builder, "Track group", CommandBand.TrackGroup(Editable));
            // The edits that the controls offer and why each other one is not available, from the controls' own rules.
            FieldLabel(builder, "Supported edits");
            foreach (var line in SupportedEdits.Lines(InspectedEdits())) PlainLabel(builder, line);
            // A mast or bell that does not conform to its pattern links to the guide's refusal reasons.
            if (IsRefusedEquipment)
                Button(builder, CommandBand.EquipmentGuide[0], () => Application.OpenURL(GuideRoot + CommandBand.EquipmentGuide[1])).Tooltip(CommandBand.EquipmentGuide[0], "Open the guide's refusal reasons in your browser.");
            // A starter placement marker names every setup and car placement that reads it, with the cars and the wreck
            // setting of each.
            if (selected.Family == StarterPlacements.Family) {
                Section(builder, "Used by");
                var readers = selected.Bindings.Where(IsReader).ToArray();
                if (readers.Length == 0) Line(builder, "No active setup", null, null);
                foreach (var line in readers) PlainLabel(builder, line.Substring("Read by ".Length));
            }
            Section(builder, "Identity");
            Field(builder, "Scene", selected.Identity.Scene);
            Field(builder, "Component", selected.Identity.Component);
            Field(builder, "Structure", selected.Identity.Structure ?? "none");
            FieldLabel(builder, "Path");
            PlainLabel(builder, Breakable(selected.Identity.Path));
            // A crossbuck whose path other crossbucks share is named by its crossing's pad marker ID beside the path.
            if (selected.Identity.PadMarkerId != null)
                Field(builder, "Pad marker ID", selected.Identity.PadMarkerId)
                    .Tooltip("Pad marker ID", "The marker ID of this crossing's pad. Other crossbucks share this path, so the scene patch file names this crossbuck by its path and this ID.");
            // An object whose path and component another object shares is named by its key beside the path.
            if (selected.Identity.MarkerId != null)
                Field(builder, CommandBand.KeyMarkerIdField, selected.Identity.MarkerId).Tooltip(CommandBand.KeyMarkerIdField, CommandBand.KeyMarkerIdTooltip);
            if (selected.Identity.MapPosition != null)
                Field(builder, CommandBand.KeyPositionField, CommandBand.KeyPosition(selected.Identity.MapPosition)).Tooltip(CommandBand.KeyPositionField, CommandBand.KeyPositionTooltip);
            // Each section shows its short form, paths relative to this object and grouped per object, until its
            // triangle switches it to the long form.
            DisclosureSection(builder, "Dependencies", dependenciesLong, () => dependenciesLong = !dependenciesLong);
            if (selected.Dependencies.Count == 0) Line(builder, "None observed", null, null);
            foreach (var line in dependenciesLong ? selected.Dependencies : HierarchyDependencies.ShortForm(selected.Identity.Scene, selected.Identity.Path, selected.Dependencies)) PlainLabel(builder, Breakable(line));
            DisclosureSection(builder, "Runtime state", runtimeLong, () => runtimeLong = !runtimeLong);
            if (selected.RuntimeState.Count == 0) Line(builder, "None observed", null, null);
            foreach (var line in runtimeLong ? selected.RuntimeState : HierarchyDependencies.ShortForm(selected.Identity.Scene, selected.Identity.Path, selected.RuntimeState)) PlainLabel(builder, Breakable(line));
        }
        // Select group and Select controller: each inspects the object that holds the inspected one.
        Act SelectGroupAct() => Slot("Select group", CommandBand.SelectGroupTip,
            InventoryPresentationState.GroupOf(inventory, selected) == null ? "this part's group isn't in the inventory. Click Refresh" : null,
            () => InspectHolder(InventoryPresentationState.GroupOf(inventory, selected)));
        Act SelectControllerAct() => Slot("Select controller", "Inspect the crossing controller that holds this detection span. Set crossing coverage on its Connections tab.",
            InventoryPresentationState.HolderOf(inventory, selected) == null ? "this detection span's crossing controller isn't in the inventory. Click Refresh" : null,
            () => InspectHolder(InventoryPresentationState.HolderOf(inventory, selected)));
        void InspectHolder(SceneObservation entry) {
            if (entry == null || !inventoryPresentation.CanSelect(inventory, entry)) return;
            CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; CloseEquipmentForm(); selected = inventoryPresentation.CurrentSelection(inventory, entry); ReadPlacement();
        }
        // A button for one action slot outside an action row: inactive with its reason, which its tooltip gives.
        IConfigurableElement ActButton(UIPanelBuilder row, Act act) =>
            Button(row, act.Label, act.Run).Inactive(act.Reason != null).Tooltip(act.Title ?? act.Label, CommandBand.Tooltip(act.Tip, act.Reason));
        // The inspected object's Enabled value as the Object tab shows it.
        string EnabledText() {
            var own = OwnActivity(selected);
            var switchedOff = selected.Handle is GameObject shown && GroupWorld.OffThroughProgression(shown);
            return own ? (switchedOff ? CommandBand.SwitchedOffByGame : selected.Active ? "Yes" : "Yes; a parent is inactive") : "No";
        }
        static bool IsReader(string line) => line.StartsWith(StarterPlacements.ReaderPrefix, StringComparison.Ordinal);
        // A path wraps after a separator instead of in the middle of a name.
        static string Breakable(string path) => (path ?? "").Replace("/", "/\u200B");
        // Markup in which a file path or URL wraps only after its separators: each part between separators stays whole,
        // also a part with a hyphen, which the text would otherwise break at. A closing tag of the markup is no path.
        static readonly System.Text.RegularExpressions.Regex PathToken = new System.Text.RegularExpressions.Regex(@"(?<!<)[^\s<>""']*[\\/][^\s<>""']+");
        static string PathBreaks(string markup) => PathToken.Replace(markup ?? "", match => {
            var parts = System.Text.RegularExpressions.Regex.Split(match.Value, @"([\\/])");
            var result = new System.Text.StringBuilder();
            foreach (var part in parts) {
                if (part == "\\" || part == "/") result.Append(part).Append('\u200B');
                else if (part.Length != 0) result.Append("<nobr>").Append(part).Append("</nobr>");
            }
            return result.ToString();
        });
        bool FollowsGroup => SupportedEdits.FollowsGroup(selected);
        // The inspected object's edits and the reason of each one that is not available, which Supported edits lists and
        // every control of the Placement, Object and Connections tabs takes as its object reason. The loaded world adds what
        // the inventory entry does not hold: whether the object still stands, its own activity, the game's switches of
        // that activity, the marker adapter's refusal and the marker ID's sharers.
        IReadOnlyList<SupportedEdits.Edit> InspectedEdits() {
            if (selected == null) return Array.Empty<SupportedEdits.Edit>();
            var target = selected.Handle as GameObject;
            var editable = Editable;
            var live = new SupportedEdits.Live {
                Present = selected.Handle is GameObject, Enabled = OwnActivity(selected), Bridge = InspectedBridge, HoldsConstructionSegments = HoldsConstructionSegments,
                StartersError = NativeInventory.StartersError,
            };
            if (editable && target != null) {
                live.ActivitySwitched = selected.GroupPath != null || SceneObjectRules.InFamilies(selected.Family) ? GroupWorld.ActivityProblem(target) : IsEquipment ? CrossingDeviceWorld.ActivityProblem(target) : null;
                live.DisableProblem = GroupWorld.DisableProblem(target);
            }
            // A crossing controller moves its setup only where the crossing capability holds the whole move.
            if (CommandBand.EditsCoverage(selected.Identity) && crossingCoverageScene?.MovesSetup != true) live.WholeMoveReason = EditReasons.GameBuild;
            // A crossing whose lights, gates and bells the session cannot read offers no equipment edit, with that reason.
            if (CommandBand.EditsCoverage(selected.Identity) && attachment != null)
                try { attachment.Session.Equipment(selected.Identity); } catch (Exception e) { live.EquipmentReason = e.GetBaseException().Message; }
            if (IsGenericMarker) {
                var reading = ReadSharers();
                live.MarkerIdReason = CommandBand.MarkerIdUnavailable(selected.Family, true, reading.Underlying, reading.Sharers.Count, HasMarkerIdValue(selected.Identity));
            }
            return SupportedEdits.Of(selected, live);
        }
        // The reason of one edit of the inspected object, or null while its controls offer it.
        string EditUnavailable(string edit) => SupportedEdits.ReasonOf(InspectedEdits(), edit);

        // The Placement tab: every way to put the object somewhere. The tab's pinned row holds the actions of the chosen way.
        void DrawPlacement(UIPanelBuilder builder) {
            // A crossing pad has no placement of its own, and an object that cannot be edited has none to set: the tab
            // shows the placement, and no other mode is open.
            var edits = InspectedEdits();
            var moveBlock = SupportedEdits.ReasonOf(edits, SupportedEdits.Move);
            if (moveBlock != null) placementMode = CommandBand.NumericMode;
            // A new-game car placement puts its cars at the marker's track point, which the world marks in gold; the
            // object position below plays no part in it.
            if (selected.Family == StarterPlacements.Family) {
                var point = InspectedTrackPoint();
                Field(builder, "Track point", point == null ? "None: " + (FocusUnavailable() ?? StarterPlacements.NoTrackPoint) : String.Join(", ", point.Select(CommandBand.Number)))
                    .Tooltip("Track point", "Where the start setup places the front of this marker's car cut, in map coordinates. Focus goes here.");
            }
            var tips = new[] { "Enter a position in map coordinates and a rotation in degrees.", "Drag the native map editor's Move or Rotate gizmo in the world.",
                "Move the object once onto a chosen track or endpoint.", "Keep the object's placement relative to a chosen track or endpoint." };
            builder.AddSegmentedControlCompact(segments => {
                for (var i = 0; i < PlacementModes.Length; i++) {
                    var mode = i;
                    // A mode that cannot be used on the inspected object is inactive and says why, in the words of
                    // Supported edits. Numeric stays open to show the placement, its fields unavailable with the reason.
                    var padMode = SupportedEdits.ModeBlock(edits, i, !IsPad && !IsRefusedEquipment);
                    segments.AddSegment(PlacementModes[i], placementMode == i, presentation.Action(() => { if (padMode == null) ChangePlacementMode(mode); })).Inactive(padMode != null).Tooltip(PlacementModes[i], CommandBand.ModeTip(i, tips[i], padMode, moveBlock ?? EditBlock()));
                }
            }, true);
            var block = moveBlock ?? EditBlock();
            if (placementMode == 0) {
                for (var group = 0; group < 2; group++) {
                    if (group != 0) builder.Spacer(6f);
                    // A bell's rotation stays visible as a running line with the reason it does not apply.
                    ValueRow(builder, CommandBand.PlacementGroups[group], CommandBand.PlacementUnits[group], CommandBand.Axes, placementFields, group * 3, block ?? (group == 1 ? CommandBand.RotationBlock(selected?.Identity.Structure) : null), group == 0 ? "Map coordinates" : "Euler angles", column => true);
                }
            } else if (placementMode == 1) {
                // The tool is a choice, as in the native map editor: Move by default. The gizmo stands on the object as
                // soon as this mode is open and stays until the mode, the tab or the inspection ends; choosing the
                // other tool changes the gizmo at once. Each completed drag is one operation.
                // Rotation does not apply to a bell: its Rotate tool stays visible and inactive, and the gizmo stays on Move.
                var noRotation = CommandBand.RotationBlock(selected?.Identity.Structure);
                if (noRotation != null && gizmoTool != UI.Gizmos.TransformGizmoMode.Move) { gizmoTool = UI.Gizmos.TransformGizmoMode.Move; gizmo?.SetMode(gizmoTool); }
                builder.AddSegmentedControlCompact(tools => {
                    foreach (var tool in new[] { UI.Gizmos.TransformGizmoMode.Move, UI.Gizmos.TransformGizmoMode.Rotate }) {
                        var chosen = tool; var name = tool == UI.Gizmos.TransformGizmoMode.Move ? "Move" : "Rotate";
                        var toolBlock = ChoiceBlock ?? (tool == UI.Gizmos.TransformGizmoMode.Rotate ? noRotation : null);
                        tools.AddSegment(name, gizmoTool == tool, presentation.Action(() => {
                            if (selection?.Pending != null || toolBlock != null) return;
                            gizmoTool = chosen;
                            gizmo?.SetMode(chosen);
                        })).Inactive(toolBlock != null).Tooltip(name, CommandBand.Tooltip(tool == UI.Gizmos.TransformGizmoMode.Move ? "Drag the native map editor's Move gizmo in the world." : "Drag the native map editor's Rotate gizmo in the world.", toolBlock));
                    }
                }, true);
                // The object's placement as the running lines of Numeric: it follows the gizmo while it is dragged
                // (ShowLivePlacement sets the texts in place).
                var live = new TMP_Text[2];
                for (var group = 0; group < 2; group++) {
                    if (group != 0) builder.Spacer(6f);
                    live[group] = ValueRow(builder, CommandBand.PlacementGroups[group], CommandBand.PlacementUnits[group], CommandBand.Axes, placementFields, group * 3, "The object's placement now; it follows the gizmo while you drag.", null, column => true);
                }
                gizmoPlacement = live;
                Line(builder, "<style=Footnote>" + Literal(block ?? ChoiceBlock ?? (gizmo?.Shown == true ? gizmo.State : "The gizmo appears on the object in the world.")) + "</style>", null, null);
            } else if (placementMode == 2) {
                var offered = choosingSnap && selection?.Pending != null ? selection.Find(selection.Pending.Candidate) : null;
                Field(builder, "Target", preparedSnap != null ? preparedSnap.Identity.ToString() : offered != null ? "Candidate, not confirmed: " + offered.Choice.Identity : "None chosen");
                Field(builder, "Position", preparedSnap == null ? "None" : String.Join(", ", preparedSnap.Placement.Position.Select(CommandBand.Number)));
                // One line names the next step of the sequence: Track or Node, a click in the world, Confirm, Apply.
                Line(builder, "<style=Footnote>" + (preparedSnap != null ? "Next: Apply moves the object to this position."
                    : offered != null ? "Next: Confirm takes the candidate; Apply then moves the object."
                    : choosingSnap ? "Next: click a track or endpoint in the world."
                    : "Next: Track or Node, then click in the world, Confirm, and Apply.") + "</style>", null, null);
            } else {
                TrackFollowing following = null;
                if (block == null && trackPoses != null) try { following = attachment.Session.FollowingOf(selected.Identity); } catch { }
                var picked = FollowTarget();
                if (following != null) {
                    Field(builder, "Following", following.Anchor.Display);
                    Field(builder, "Offset", String.Join(", ", following.Offset.Select(CommandBand.Number)));
                }
                if (picked != null) Field(builder, "Picked", picked.Identity.ToString());
                if (following == null && picked == null) Field(builder, "Following", "Nothing");
                Line(builder, "<style=Footnote>" + (picked != null ? "Next: Start keeps the object's placement relative to the picked track."
                    : following != null ? "Stop ends following. Track or Node picks another track to follow."
                    : "Next: Track or Node, then click in the world, Confirm, and Start.") + "</style>", null, null);
            }
        }
        // The confirmed choice made for the inspected inventory entry, not for another entry of the same object.
        ConfirmedSelection ConfirmedForInspected => selection?.ConfirmedFor(selected) == true ? selection.Confirmed : null;
        // The confirmed track or endpoint that belongs to the inspected object, for following.
        ConfirmedSelection FollowTarget() {
            var confirmed = selection?.Confirmed;
            return confirmed != null && selected != null && confirmed.Identity.Kind != SelectionKind.SceneObject &&
                (confirmed.Source == null || confirmed.Source.Equals(SelectionIdentity.SceneObject(selected.Identity))) ? confirmed : null;
        }

        // The Connections tab: what the object is bound to. Track binding is its first section.
        // The crossing's detection coverage by circuit. A change is previewed for every circuit before it is applied; the
        // crossing object never moves, and proximity never assigns coverage. The tab's pinned row holds Preview, Detach,
        // Clear circuit and Apply for the circuit being edited, and Preview, Delete and Apply for a detection addition.
        void DrawCoverage(UIPanelBuilder builder) {
            var identity = selected.Identity;
            var block = EditBlock();
            // A button in the tab waits for a pending world choice; the fields stay as they are.
            var wait = ChoiceBlock ?? block;
            // The open form keeps the pickers of all three roles above the list of circuits: each role's From and To
            // ends, each with its own segment, segment end, distance and world pick, since the two ends may lie on
            // different segments.
            if (coverageCircuit != null) {
                Section(builder, coverageAddition == null ? "Circuit " + coverageCircuit : "Detection addition " + coverageAddition);
                var pickBlock = CoveragePickBlock;
                for (var role = 0; role < 3; role++) {
                    var roleIndex = role; var roleName = CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]);
                    foreach (var to in new[] { false, true }) {
                        var end = to;
                        ValueRow(builder, roleName + " " + CoverageFormFields.EndCaption(to), "m", CoverageFormFields.EndLabels, coverageFields, CoverageFormFields.First(role, to), block,
                            "End is a or b. Distance is in meters from that segment end. " + (to ? "To is the end away from the island for an approach, and the island's upper end for the island." : "From is the end next to the island for an approach, and the island's lower end for the island.") + " The two ends can lie on different segments.",
                            column => column == 2, row => ActButton(row, CoverageRoleAct(roleIndex, "Segment", pickBlock, end)));
                    }
                }
                Section(builder, "Preview");
                if (preparedCoverage == null || preparedCoverage.Circuit != coverageCircuit) Line(builder, "None", null, null);
                else if (preparedCoverage.Preview.Error != null) builder.AddLabel(Tint(Red, "Preview refused: " + preparedCoverage.Preview.Error));
                else foreach (var line in preparedCoverage.Preview.Lines) PlainLabel(builder, Breakable(line));
                    }
            Section(builder, "Crossing coverage");
            CrossingCoverage current = null;
            if (attachment != null) try { current = attachment.Session.Coverage(identity); }
                catch (Exception e) { builder.AddLabel(Tint(Red, "Coverage unavailable: " + e.GetBaseException().Message)); }
            if (current != null) {
                foreach (var circuit in current.Circuits) {
                    var key = circuit.Key; var value = circuit.Value;
                    // A native circuit is named by its island span, a detection addition by its ID; the button names it in
                    // running text, after Edit.
                    var addition = DetectionAddition.Id(key);
                    var name = addition == null ? "Circuit " + key : "Detection addition " + addition;
                    Field(builder, name, value.ToString());
                    var buttonName = addition == null ? "Edit circuit " + key : "Edit detection addition " + addition;
                    Button(builder, buttonName, () => { OpenedForm(null); CloseEquipmentForm(); coverageCircuit = key; coverageAddition = DetectionAddition.Id(key); preparedCoverage = null; ReadCoverage(value); }).Inactive(wait != null)
                        .Tooltip(buttonName, CommandBand.Tooltip(DetectionAddition.Id(key) == null ? "Select the tracks this circuit covers, or detach it. Circuits you do not edit keep their coverage." : "Change the tracks of this detection addition, or delete it.", wait));
                }
                if (current.Residue.Count > 0) builder.AddLabel(Tint(Amber, "Owned spans left by an incomplete cleanup: " + String.Join(", ", current.Residue) + ". Click Recheck."));
                // An addition starts from an ID the modder chooses and tracks the modder selects; nothing is taken from
                // nearby track.
                Section(builder, "Add detection");
                FieldLabel(builder, "Detection addition ID");
                var circuits = current;
                // The ID field and its button share a row, so the button's short label reads with the field beside it.
                builder.HStack(row => {
                    if (block != null) Line(row, Literal(newAddition ?? ""), "Detection addition ID", block, 30).FlexibleWidth();
                    else {
                        var idInput = row.AddInputField(newAddition ?? "", value => newAddition = value);
                        idInput.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => newAddition = value);
                        idInput.FlexibleWidth();
                    }
                    Button(row, "Add detection", () => {
                        var id = (newAddition ?? "").Trim();
                        if (id.Length == 0) { feedback.Refuse(CoveragePlace, "Detection addition", "the addition has no ID. " + WindowGuidance.GiveAdditionId); return; }
                        var existing = circuits.Circuits.FirstOrDefault(c => c.Value.Kind == CircuitKind.Added && DetectionAddition.Id(c.Key) == id);
                        CloseEquipmentForm(); coverageAddition = id; coverageCircuit = existing.Key ?? DetectionAddition.Key("", id); preparedCoverage = null; ReadCoverage(existing.Value);
                        OpenedForm("Detection addition " + id + " is open. " + WindowGuidance.SelectTracksThenPreview);
                    }).Inactive(wait != null).Tooltip("Add detection with this ID", CommandBand.Tooltip("Make the crossing detect an extra track: give the addition an ID that is unique in this mod's scene patch file, then select its tracks.", wait));
                });
            }
        }
        // Lights and gates added to the crossing: copies of eligible devices already loaded in the crossing's scene, which the
        // crossing controller drives. The open form stands first; the tab's pinned row holds Preview, Remove and Apply for it.
        // The open light or gate form stands first in the Connections tab, above the replacement targets it does not use,
        // so the scrolling tab body shows its name, ID and position.
        // The kind of the open equipment form, as its labels name it.
        string EquipmentKind => equipmentSource?.IsDefinition == true ? "bell" : "light or gate";
        static string AddedEquipmentFamily(EquipmentSource source) => source.IsDefinition ? CommandBand.AddedBellFamily : CommandBand.AddedEquipmentFamily;
        bool revealEquipmentForm;
        void DrawEquipmentForm(UIPanelBuilder builder) {
            var block = EditBlock();
            var bell = equipmentSource.IsDefinition;
            {
                var label = Section(builder, equipmentLabel);
                // The form was just opened, as by Use or Edit in the list below it: it scrolls into view once.
                if (revealEquipmentForm) presentation.Reveal(label, line => {
                    revealEquipmentForm = false;
                    PatcherLog.Debug(CommandBand.LogLine("Connections", selected?.Identity.Path, line));
                });
                if (equipmentEdited != null) {
                    label.Tooltip("Equipment", equipmentLabel);
                    FitEquipmentText(label.GetComponent<TMP_Text>(), AddedEquipmentFamily(equipmentSource), equipmentLabel, value => "<b>" + Literal(value) + "</b>");
                }
                FieldLabel(builder, "Equipment ID");
                if (block != null) Line(builder, Literal(equipmentFields[0] ?? ""), "Equipment ID", block, 30);
                else {
                    var idInput = builder.AddInputField(equipmentFields[0] ?? "", value => equipmentFields[0] = value);
                    idInput.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => { equipmentFields[0] = value; preparedEquipment = null; FieldsChanged(); });
                    idInput.Tooltip("Equipment ID", "A persistent ID for this " + EquipmentKind + " in this mod's scene patch file. Use letters, digits, '.', '_', or '-'.");
                }
                ValueRow(builder, "Position", "m", new[] { "X", "Y", "Z" }, equipmentFields, 1, block, bell ? "The bell's position in map coordinates. Its sound comes from this position." : "The copy's position in map coordinates.", column => true);
                ValueRow(builder, "Rotation", "°", new[] { "X", "Y", "Z" }, equipmentFields, 4, block, bell ? "The bell's rotation in degrees." : "The copy's rotation in degrees.", column => true);
                Section(builder, "Preview");
                if (preparedEquipment == null) Line(builder, "None", null, null);
                else if (preparedEquipment.Preview.Error != null) builder.AddLabel(Tint(Red, "Preview refused: " + CommandBand.Sentence(preparedEquipment.Preview.Error)));
                else foreach (var line in preparedEquipment.Preview.Lines) PlainLabel(builder, Breakable(line));
            }
        }
        // The crossing's lights, gates and bells and the sources offered for them, below its coverage.
        void DrawEquipment(UIPanelBuilder builder) {
            var identity = selected.Identity;
            var block = EditBlock();
            var wait = ChoiceBlock ?? block;
            Section(builder, "Lights, gates, and bells");
            CrossingEquipment current = null;
            IReadOnlyList<EquipmentEntry> mine = Array.Empty<EquipmentEntry>();
            if (attachment != null) try { current = attachment.Session.Equipment(identity); mine = attachment.Session.EquipmentEntries(identity); }
                catch (Exception e) { builder.AddLabel(Tint(Red, "Lights, gates, and bells unavailable: " + CommandBand.Sentence(e.GetBaseException().Message))); }
            IReadOnlyList<SourceCandidate> candidates = null;
            IReadOnlyList<SourceCandidate> choices = null;
            string sourcesUnavailable = null;
            if (attachment != null) try { choices = attachment.Session.EquipmentSources(identity, out candidates); }
                catch (Exception e) { sourcesUnavailable = e.GetBaseException().Message; }
            if (current != null) {
                var copies = EquipmentCopies();
                var others = current.Additions.Where(a => a.Scope != attachment.Session.ScenePatchFileKey).ToArray();
                if (mine.Count == 0 && others.Length == 0) Line(builder, "None added", null, null);
                // This mod's additions come from its scene patch file, so one whose build failed is listed with its reason and can
                // be edited or removed.
                foreach (var entry in mine) {
                    var item = entry.Built ?? entry.Addition;
                    var kind = InventoryPresentationState.EquipmentKind(item.Source).ToLowerInvariant();
                    var name = EquipmentEntryName(entry, copies, candidates);
                    string state = null;
                    if (entry.Built != null) {
                        try { state = attachment.Session.EquipmentStatus(identity, item.Id); }
                        catch (Exception e) {
                            state = "The equipment status could not be checked";
                            var failure = e;
                            equipmentStatusFailures.Debug(item.Id + "/" + e.GetBaseException().Message, () => "Equipment status check failed for " + item.Id + ": " + failure.GetBaseException());
                        }
                    }
                    EquipmentNameField(builder, item, name, entry.Built != null);
                    if (entry.Problem != null) builder.AddLabel(Tint(Red, "Not built: " + CommandBand.Sentence(entry.Problem)));
                    else if (state != null) builder.AddLabel(Literal(CommandBand.Sentence(state)));
                    var scenePatchFile = entry.Addition;
                    var edit = Button(builder, "Edit " + name, () => ReadEquipment(scenePatchFile, name)).Inactive(wait != null)
                        .Tooltip("Edit " + name, CommandBand.Tooltip(entry.Problem == null ? (item.Source.IsDefinition ? "Change this bell's placement or remove it." : "Change this copy's placement or remove it.") : "Correct this " + kind + " or remove it.", wait));
                    FitEquipmentText(edit.RectTransform.GetComponentInChildren<TMP_Text>(true), AddedEquipmentFamily(item.Source), name, value => "Edit " + value,
                        () => ((RectTransform)edit.RectTransform.parent).rect.width - 2 * CommandBand.ButtonPadding);
                }
                foreach (var item in others) {
                    EquipmentNameField(builder, item, EquipmentDisplayName(item, copies, candidates), true);
                    builder.AddLabel("Added by another mod. Read only.");
                }
            }
            Section(builder, "Add a light or gate");
            if (sourcesUnavailable != null) builder.AddLabel(Tint(Red, "Sources unavailable: " + CommandBand.Sentence(sourcesUnavailable)));
            if (choices == null) return;
            var devices = choices.Where(c => !c.Source.IsDefinition).ToArray();
            var bells = choices.Where(c => c.Source.IsDefinition).ToArray();
            // With the Edit form of a refused addition open, Use corrects that addition rather than adding a new one.
            var corrected = attachment == null ? null : CorrectedUse(attachment.Session);
            string UseTip(string added) => corrected == null ? added : "Correct " + corrected + " with this source, keeping its equipment ID and placement. Click Preview, then Apply.";
            if (devices.Length == 0) Line(builder, "No usable lights or gates.", null, null);
            foreach (var source in devices) {
                var name = InventoryPresentationState.EquipmentSourceName(source);
                Button(builder, "Use " + name, () => UseSource(attachment.Session, source)).Inactive(wait != null)
                    .Tooltip("Use " + name, CommandBand.Tooltip(UseTip("Copy this device to this crossing. The crossing controller drives its lamps and gates."), wait));
            }
            Section(builder, "Add a bell");
            if (bells.Length == 0) Line(builder, "No usable crossing bells.", null, null);
            foreach (var source in bells) {
                var name = InventoryPresentationState.EquipmentSourceName(source);
                Button(builder, "Use " + name, () => UseSource(attachment.Session, source)).Inactive(wait != null)
                    .Tooltip("Use " + name, CommandBand.Tooltip(UseTip("Add a bell to this crossing using this definition's sound. It rings when the crossing controller rings its own bell."), wait));
            }
        }
        void EquipmentNameField(UIPanelBuilder builder, EquipmentAddition item, string name, bool built) {
            var label = FieldLabel(builder, name);
            if (built) {
                label.Tooltip("Equipment", name);
                FitEquipmentText(label.GetComponent<TMP_Text>(), AddedEquipmentFamily(item.Source), name, value => "<style=FieldLabel>" + Literal(value) + "</style>");
            }
        }
        OwnedEquipment[] EquipmentCopies() => selected?.Handle is GameObject crossing && crossing != null
            ? OwnedEquipment.At(crossing).ToArray() : Array.Empty<OwnedEquipment>();

        static OwnedEquipment AddedWrapper(EquipmentAddition item, IEnumerable<OwnedEquipment> copies) =>
            copies.FirstOrDefault(copy => copy.Scope == item.Scope && copy.Id == item.Id && copy.SourceScene == item.Source.Scene
                && copy.SourcePath == item.Source.Path && copy.SourceDefinition == item.Source.Definition);
        static string EquipmentDisplayName(EquipmentAddition item, IEnumerable<OwnedEquipment> copies, IEnumerable<SourceCandidate> candidates) =>
            AddedWrapper(item, copies)?.DisplayName ?? InventoryPresentationState.EquipmentSourceName(item.Source, candidates);
        // The name of one of this mod's additions in its row and its open form: its wrapper's name while the wrapper stands,
        // also when its copy is gone; else, not built, its ID and source.
        static string EquipmentEntryName(EquipmentEntry entry, IEnumerable<OwnedEquipment> copies, IEnumerable<SourceCandidate> candidates) {
            var item = entry.Built ?? entry.Addition;
            return AddedWrapper(item, copies)?.DisplayName
                ?? (entry.Built == null ? InventoryPresentationState.UnbuiltEquipmentName(item, candidates) : InventoryPresentationState.EquipmentSourceName(item.Source, candidates));
        }
        // Why the inspected object's track binding cannot be set: for a track marker, milepost or crossing pad the patcher
        // refuses, its own reason, as Supported edits names it. An object that holds no track binding has no Track
        // binding section; only the guided task list, which keeps every task, still asks, and is told whose binding
        // the patcher edits. It is one plain sentence (CommandBand.Sentence).
        string BindingUnavailable() => EditUnavailable(SupportedEdits.Binding) ?? CommandBand.NoBindingReason(selected);
        // Whether the inspected object holds a track binding, and so has the Track binding section and its row.
        bool HoldsBinding => SupportedEdits.HoldsBinding(selected?.Family);
        // Why a role's Segment button cannot start a world choice now, or null.
        string CoveragePickBlock => ChoiceBlock ?? EditBlock() ?? (selection == null ? "World selection is unavailable." : null);
        // The Segment button of one end of a role of the open coverage form: a world choice that fills that end's
        // segment, segment end and distance from the click. Without to it fills the From end.
        Act CoverageRoleAct(int role, string label, string pickBlock, bool to = false) {
            var roleName = CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]);
            var end = CoverageFormFields.EndCaption(to);
            return Slot(label, "Click a track in the world and confirm it to fill the segment, segment end, and distance of this end.", pickBlock, () => BeginSelection(SelectionKind.TrackSegment, selected, role, coverageTo: to), "Choose a segment for the " + end + " end of " + roleName);
        }
        void CloseCoverageForm() { OpenedForm(null); coverageCircuit = coverageAddition = null; preparedCoverage = null; newAddition = ""; coverageForm.Clear(); }
        // The message that says a form is open. It leaves when the form closes or another one opens.
        string openedForm;
        void OpenedForm(string message) {
            if (openedForm != null) feedback.Withdraw(CoveragePlace, openedForm);
            openedForm = message;
            if (message != null) feedback.Report(CoveragePlace, message);
        }
        // The tab's order, as in every tab: what can be operated first, what is only read below. Replacement targets
        // and the fields of the binding come first; the current binding and the other observed bindings follow.
        void DrawConnections(UIPanelBuilder builder) {
            if (IsStarter) { DrawStarterLocation(builder); return; }
            if (HoldsConstructionSegments) { DrawConstructionSegments(builder); return; }
            var coverage = CommandBand.EditsCoverage(selected.Identity);
            // A crossing pad has the binding of its marker: setting it regenerates the pad along the track there.
            var pad = selected.Identity.Structure == CrossingPadScene.Structure;
            var marker = pad || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == MilepostScene.Structure;
            var block = marker ? EditBlock() : BindingUnavailable();
            if (coverage && coverageCircuit == null && equipmentSource != null) DrawEquipmentForm(builder);
            // Replacement targets serve a track binding or a crossing's coverage: an object with neither has no such
            // section, as it has no Track binding section. Its observed bindings stay below.
            if (HoldsBinding || coverage) {
                Section(builder, "Replacement targets");
                ReplacementRow(builder);
            }
            if (coverage) { DrawCoverage(builder); DrawEquipment(builder); }
            var current = HoldsBinding ? CommandBand.CurrentBindings(selected) : new string[0];
            // Only a track marker, milepost or crossing pad holds a track binding; one that the patcher refuses keeps its
            // fields, disabled with its own reason.
            if (HoldsBinding) {
                Section(builder, pad ? "Crossing pad binding" : "Track binding");
                ValueRow(builder, "Track location", "m", new[] { "Segment", "End", "Distance" }, binding, 0, block, pad ? "The track location this pad's marker is registered at. Setting it regenerates the pad along the track there; the pad object does not move. End is a or b. Distance is measured in meters from the End you entered, along the whole segment." : "The track location this marker is registered at. A binding edit does not move the object. End is a or b.", column => column == 2);
                FieldLabel(builder, "Current binding");
                if (current.Length == 0) Line(builder, "None", null, null);
                foreach (var line in current) BindingFields(builder, line);
            }
            if (marker) DrawMarkerId(builder);
            Section(builder, "Observed bindings");
            var others = selected.Bindings.Where(line => !current.Contains(line) && !IsReader(line)).ToArray();
            if (others.Length == 0) Line(builder, "None observed", null, null);
            foreach (var line in others) BindingFields(builder, line);
        }

        // The buttons that start a world choice for the Connections tab: the replacement targets of a binding or a
        // crossing's coverage, the segment of a rail bridge construction, or the track of a starter placement marker.
        Act[] ReplacementActs() {
            if (IsStarter) {
                var busy = EditBlock() ?? (selection == null ? "World selection is unavailable." : ChoiceBlock);
                return new[] { Slot("Segment", "Click the track where the front of the car cut belongs and confirm the segment. The fields take the segment and the distance of the click from end a.", busy, () => BeginSelection(SelectionKind.TrackSegment, selected), "Place on a segment") };
            }
            if (HoldsConstructionSegments) {
                var busy = EditBlock() ?? (selection == null ? "World selection is unavailable." : ChoiceBlock);
                return new[] { Slot("Segment", "Choose a track segment in the world and confirm its identity. Click Picked to add it to the construction segments.", busy, () => BeginSelection(SelectionKind.TrackSegment, selected), "Replacement segment") };
            }
            var coverage = CommandBand.EditsCoverage(selected.Identity);
            var marker = selected.Identity.Structure == CrossingPadScene.Structure || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == MilepostScene.Structure;
            // A starter placement marker gives its own reason: its track location belongs to the starter placement workflow.
            var reason = selected.Family == StarterPlacements.Family ? CommandBand.EditReason(selected.Operations)
                : !marker && !coverage ? (HoldsBinding ? BindingUnavailable() : "Replacement targets serve a track binding or a crossing's coverage.") : selection == null ? "World selection is unavailable." : ChoiceBlock;
            var acts = new List<Act> { Slot("Object", "Choose a replacement object in the world and confirm its identity.", reason, () => BeginSelection(SelectionKind.SceneObject, selected), "Replacement object") };
            if (!coverage) acts.Add(Slot("Segment", "Choose a track segment in the world and confirm its identity.", reason, () => BeginSelection(SelectionKind.TrackSegment, selected, bindingSlot: marker), "Replacement segment"));
            acts.Add(Slot("Node", "Choose a track node in the world and confirm its identity.", reason, () => BeginSelection(SelectionKind.TrackNode, selected), "Replacement node"));
            return acts.ToArray();
        }
        void ReplacementRow(UIPanelBuilder builder) => builder.HStack(row => { foreach (var act in ReplacementActs()) ActButton(row, act); }).Height(RowHeight);

        // The Connections tab of a rail bridge construction: the construction segments its controller reads, as one field of
        // segment ids. Segment chooses a track in the world and Picked adds it to the field; Set replaces the segments
        // with exactly those in the field. The observed bindings follow, with one row per current segment.
        void DrawConstructionSegments(UIPanelBuilder builder) {
            var block = EditBlock();
            Section(builder, "Replacement targets");
            ReplacementRow(builder);
            Section(builder, "Construction segments");
            ValueRow(builder, "Segments", null, new[] { "IDs" }, segmentFields, 0, block,
                "The construction state of these track segments determines which parts of this bridge the game shows. Separate the IDs with commas. Click Set to replace the construction's segments with exactly these segments. The bridge doesn't move.", column => false);
            Section(builder, "Observed bindings");
            var others = selected.Bindings.Where(line => !IsReader(line)).ToArray();
            if (others.Length == 0) Line(builder, "None observed", null, null);
            foreach (var line in others) BindingFields(builder, line);
        }
        void ApplySegments() {
            var identity = selected.Identity;
            var ids = CommandBand.SegmentIds(segmentFields[0]);
            if (ids.Count == 0) { feedback.Refuse(SegmentsPlace, "Construction segments", "the segment ID list is empty. " + WindowGuidance.EnterSegmentId); return; }
            Edit(SegmentsPlace, "Construction segments", (current, inspection) => current.SetConstructionSegments(identity, ids, inspection), true);
        }
        bool HasBindingValue(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasTrackBinding(identity); } catch { return false; }
        }
        bool HasSegmentValue(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasConstructionSegments(identity); } catch { return false; }
        }
        // The Connections tab of a starter placement marker: on-track placement of its track location, which the start setup
        // reads as the front of the car cut. Segment chooses the track in the world, and the click on it gives the
        // distance; the fields can also be typed. The current location follows, with the setups that read it.
        void DrawStarterLocation(UIPanelBuilder builder) {
            var block = EditBlock();
            Section(builder, "Place on the track");
            ReplacementRow(builder);
            Section(builder, "Track location");
            ValueRow(builder, "Track location", "m", new[] { "Segment", "End", "Distance" }, binding, 0, block,
                "Where the start setup places the front of this marker's car cut; the cut extends from there towards the end the distance counts from. Set checks the whole cut with the setup's other cuts by the game's placement rule. End is a or b.", column => column == 2);
            var current = CommandBand.CurrentBindings(selected);
            FieldLabel(builder, "Current track location");
            if (current.Length == 0) Line(builder, "None", null, null);
            foreach (var line in current) BindingFields(builder, line);
            DrawMarkerId(builder);
            var readers = selected.Bindings.Where(IsReader).ToArray();
            // The native checkbox shows the effective wreck setting of the one car placement that reads the marker and
            // sets this mod's value; the result appears in the footer.
            Section(builder, "Starting cars");
            var wreckBlock = EditUnavailable(SupportedEdits.Wreck) ?? block;
            var identity = selected.Identity;
            // A checkbox that cannot be used refuses the click and dims its label, as an inactive button does.
            var wreckRow = builder.HStack(row => {
                row.AddToggle(() => InspectedWreck() == true, value => Edit(WreckPlace, CommandBand.WreckAction(value), (current, inspection) => current.SetStarterWreck(identity, value, inspection)), wreckBlock == null);
                row.AddLabel("Start derailed");
            }).Height(RowHeight);
            if (wreckBlock != null) foreach (var text in wreckRow.GetComponentsInChildren<TMP_Text>(true)) text.alpha = CommandBand.InactiveAlpha;
            wreckRow.Tooltip("Start derailed", CommandBand.Tooltip("Start this car cut derailed, like the wrecks the game places itself. To remove the mod's value, choose the setting the car cut has without this mod.", wreckBlock));
            Section(builder, "Used by");
            if (readers.Length == 0) Line(builder, "No active setup", null, null);
            foreach (var line in readers) PlainLabel(builder, line.Substring("Read by ".Length));
        }
        // The Changes tab: what this mod changes about the inspected object, and what that change still awaits.
        void DrawChanges(UIPanelBuilder builder) {
            Section(builder, "This mod");
            var changed = IsChanged(selected);
            var shownFailure = FailureOf(selected);
            FieldMarkup(builder, "Object", shownFailure != null ? CommandBand.Badges(changed, false, shownFailure)[0].Markup : changed ? ChangedBadge : "Unchanged")
                .Tooltip("Changed", "Whether Data/scene-patches.json of the edited mod holds a modification of this object.");
            // One short line per changed setting, with the value the object has without this modification.
            if (changed && attachment != null) {
                IReadOnlyList<string> lines = null;
                try { lines = attachment.Session.Changes(selected.Identity); } catch { }
                if (lines != null) foreach (var line in lines) PlainLabel(builder, Breakable(line));
            }
            // Terrain output exists only for an object with a terrain mask: a location sign, and an object that
            // holds a scene ground mask or stands above one. The section is there only for those, in plain words.
            if (!HasTerrain) return;
            Section(builder, "Terrain");
            var text = TerrainLine(out var failed);
            if (failed) builder.AddLabel(Tint(Red, text)); else PlainLabel(builder, text);
        }
        // The Terrain line of the inspected object: the output of its own edits. A part whose own edits wrote none,
        // such as the part Mask after its group was disabled, shows its group's output with the group named: the
        // terrain is the part's mask, and the edit that changed it is the group's (CommandBand.GroupTerrain).
        readonly InspectedOutput inspectedGroupOutput = new InspectedOutput();
        string TerrainLine(out bool failed) {
            var problem = (string)null;
            var output = attachment == null || !Editable ? DependentOutput.None : inspectedOutput.Of(attachment.Session, selected, out problem);
            failed = problem != null;
            var own = CommandBand.Terrain(problem, output.Error, output.Settled, Join(output.Completed), Join(output.Outstanding), output.Completed.Count != 0);
            if (problem != null || output.Error != null || output.Completed.Count != 0 || output.Outstanding.Count != 0 || selected.GroupPath == null) return own;
            var group = InventoryPresentationState.GroupOf(inventory, selected);
            if (group == null || attachment == null) return own;
            var groupProblem = (string)null;
            var held = inspectedGroupOutput.Of(attachment.Session, group, out groupProblem);
            if (groupProblem != null || (held.Error == null && held.Completed.Count == 0 && held.Outstanding.Count == 0)) return own;
            var text = CommandBand.Terrain(null, held.Error, held.Settled, Join(held.Completed), Join(held.Outstanding), held.Completed.Count != 0);
            return CommandBand.GroupTerrain(group.DisplayName, text, held.Error != null || held.Outstanding.Count != 0);
        }
        // Whether the inspected object has terrain output at all.
        bool HasTerrain => selected != null && (selected.Identity.Structure == LocationSigns.Structure || CommandBand.HoldsGroundMask(selected.Dependencies));

        // The footer, the same on every tab: the message area, then one row with the actions on the inspected object
        // that belong to no tab, Remove modification and Recheck. A pending world choice or an active gizmo takes that
        // row over until it ends.
        // Title is the tooltip's title when the label is short for it. Other is the label the button changes to in
        // place, such as Enable for Disable: the button is as wide as the wider of the two. A button's width follows
        // its label by the one padding rule (FitButton); span and width are no longer used.
        // Live, when set, is the reason of a button whose state follows the values typed into fields: it is read
        // again on every keystroke.
        struct Act { internal string Label, Tip, Reason, Title, Other; internal Action Run; internal Func<string> Live; }
        static Act Typed(Act act, Func<string> live) { act.Live = live; act.Reason = live(); return act; }
        // The buttons of the panel as it is built now whose state follows typing; each entry sets its button's state
        // in place, because the panel does not rebuild while a field has the focus.
        readonly List<Action> typed = new List<Action>();
        void FieldsChanged() { foreach (var update in typed.ToArray()) { try { update(); } catch { } } }
        static Act Slot(string label, string tip, string reason, Action run, string title = null, int span = 1, float width = 0, string other = null) => new Act { Label = label, Tip = tip, Reason = reason, Run = run, Title = title, Other = other };
        static readonly Act Empty = new Act();
        GameObject gizmoDragMessage;
        // A held pointer prevents a rebuild. The footer's first row can follow the drag in place.
        void ShowLiveGizmoDrag() {
            var dragging = gizmo?.Active == true;
            if (gizmoDragMessage != null && gizmoDragMessage.activeSelf != dragging) gizmoDragMessage.SetActive(dragging);
        }
        void DrawFooter(UIPanelBuilder builder) {
            // Reserve room even when empty. The file panel uses a shorter viewport so its decisions have more room.
            DrawMessages(builder, FileLocked ? FilePanelMessageHeight : InspectorMessageHeight);
            // While the file panel is open, the footer's row waits for it with the panel's reason.
            // The row is the window's last: it stands at the lower edge, on the native padding below the last row. Its
            // buttons start at the left, so the native resize grip in the lower right corner covers only the row's
            // empty end.
            ActionRow(builder, guided.Enabled && !FileLocked && !Duplicating && selection?.Pending == null ? GuidedActs() : FooterActs(), WindowLockReason);
        }
        // The footer's rule and message area of messageHeight; returns the area's layout element.
        LayoutElement DrawMessages(UIPanelBuilder builder, float messageHeight) {
            builder.AddHRule();
            // The first slot says what the window is waiting for or why the open tab cannot edit now.
            var pending = selection?.Pending;
            string source = null, state = "";
            if (pending == null) candidateDetails = false;
            var session = attachment?.Session;
            var confirmation = selection?.Confirmed;
            var confirmedForInspected = selected != null && confirmation?.Source != null && confirmation.Source.Equals(SelectionIdentity.SceneObject(selected.Identity));
            // The message area holds no version choice: that stands in the file panel.
            var primary = feedback.Primary(session, Requests.Place != null, pending != null, confirmedForInspected);
            if (primary == EditFeedback.PrimaryMessage.Working) { source = Requests.Place; state = "Working..."; }
            else if (primary == EditFeedback.PrimaryMessage.WorldChoice) {
                var candidate = selection.Find(pending.Candidate);
                source = "World choice";
                // A short purpose with the count on the first line, the candidate on its own line.
                var purpose = choosingCoverageRole.HasValue ? pending.Purpose : choosingSnap ? "Choose the snap target" : pending.Kind == SelectionKind.SceneObject ? "Choose an object" : pending.Kind == SelectionKind.TrackSegment ? "Choose a track" : "Choose an endpoint";
                // The choice takes the world's clicks: the native map editor does not select or drag until it ends.
                state = (pending.ListOpen ? Tint(Blue, pending.OfferCountText + " ") + Literal(pending.OfferListInstruction) : Literal(purpose + " in the world.")) +
                    (pending.InspectOnly ? "" : Literal(" Map Editor selection is paused. Click Cancel to resume it.")) +
                    (pending.Problem == null || pending.Problem == PendingSelection.SeveralOffers ? "" : " " + (PendingSelection.IsInstruction(pending.Problem) ? Literal(pending.Problem) : Tint(Red, pending.Problem))) + (inputBlocker.WorldInputProblem == null ? "" : " " + Tint(Red, inputBlocker.WorldInputProblem)) +
                    (selectionProblem == null ? "" : " " + Tint(Red, selectionProblem)) +
                    (candidate == null ? "" : "\n" + Tint(Blue, CommandBand.ChoiceLine(pending.Kind == SelectionKind.SceneObject ? "Object" : "Candidate", pending.Number(candidate.Choice), pending.Offers.Count, candidate.Choice.Label, pending.Offers.Select(o => o.Label), candidate.Observation)) + Literal(choosingSnap ? guided.Enabled ? " Click Confirm to choose this target, then click Next to review the snap." : " Confirm chooses it. Apply moves the object." : pending.InspectOnly ? " Confirm inspects this object." : " Confirm chooses this target.")) +
                    // The footer's Details: the clicked candidate's identity and details, here in the message area.
                    (candidate == null || !candidateDetails ? "" : "\n" + Literal(candidate.Choice.Identity.ToString()) + (String.IsNullOrEmpty(candidate.Details) ? "" : "\n" + Literal(candidate.Details)));
            } else if (guided.Enabled && !String.IsNullOrEmpty(guided.Recovery)) { source = "Guided task"; state = Literal(guided.Recovery); }
            else if (primary == EditFeedback.PrimaryMessage.BackupDiagnosis) {
                source = RecoveryPlace; state = Literal(EditFeedback.BackupNotice(session));
            } else if (selectionProblem != null) { source = "World choice"; state = Tint(Red, selectionProblem); }
            else if (primary == EditFeedback.PrimaryMessage.Reported) { state = null; } // The newest result leads the list below.
            // A confirmed target belongs to the object it was chosen for; another object does not show it.
            else if (primary == EditFeedback.PrimaryMessage.ConfirmedChoice) {
                source = "World choice"; state = Tint(Green, "Confirmed choice: " + (selection.ConfirmedName ?? confirmation.Identity.ToString()));
            } else if (!guided.Enabled && selected != null && EditBlock() is string block) { source = "Editing"; state = Tint(Amber, CommandBand.EditingLine(block)); }
            else if (feedback.Recovery?.Kind != FailureKind.IncompleteCleanup && feedback.Recovery?.Kind != FailureKind.Blocked && CleanupState() is string cleanup) { source = "Editing"; state = Tint(Amber, cleanup); }
            else if (!guided.Enabled && HoldWarning is string warning) { source = ActivityPlace; state = Tint(Amber, warning); }
            // Entries in the area stack from the top without gaps:
            // the current state or newest result first, then the guided hint and the remaining results. Older results
            // are dimmed. Each names its action group. The area is a native scroll view: its content takes the height of its
            // entries, so an entry that wraps has its wrapped height, and its viewport cuts off what does not fit. A
            // stack of fixed height would instead squeeze its entries below their text, and the text would overprint.
            // What lies below the area's edge is reached by scrolling.
            var messages = feedback.Messages;
            var messageArea = presentation.Scroll(builder, messageHeight, lines => {
                gizmoDragMessage = MessageSlot(lines, GizmoPlace, Literal(GizmoController.DraggingState), false).gameObject;
                ShowLiveGizmoDrag();
                var firstReport = 0;
                if (!String.IsNullOrEmpty(state)) MessageSlot(lines, source, state, false);
                else if (messages.Count != 0) { ReportSlot(0); firstReport = 1; }
                if (preparedSnap != null && guided.Enabled && guided.Step == GuidedTaskStep.Values)
                    MessageSlot(lines, GuidedPlace, "Click Next to review the snap.", false);
                for (var i = firstReport; i < messages.Count; i++) ReportSlot(i);
                if ((primary == EditFeedback.PrimaryMessage.ConfirmedChoice || primary == EditFeedback.PrimaryMessage.Reported) && EditFeedback.HasBackupNotice(session))
                    MessageSlot(lines, RecoveryPlace, Literal(EditFeedback.BackupNotice(session)), true);

                void ReportSlot(int i) {
                    var message = messages[i]; var older = i > 0;
                    // A failed action's message takes its colour from its failure kind; any other result from its words.
                    var tone = message.Recovery != null ? (CommandBand.RecoveryWarning(message.Recovery.Kind) ? Amber : Red) : ToneOf(message.Text);
                    var text = CommandBand.WithoutSource(message.Source, message.Text);
                    MessageSlot(lines, message.Source, PathBreaks(tone == null ? Literal(text) : Tint(tone + (older ? "80" : ""), text)), older);
                }
            }, false);
            // Keep the scroll rectangle itself in place; its viewport takes the full height of the area in every state.
            var messageScroll = messageArea.GetComponent<ScrollRect>();
            // ExpandViewport drives the viewport's anchors and size during layout, undoing the reserved strip.
            // Preserve auto-hiding while leaving the viewport and scrollbar bounds under our control.
            if (messageScroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport)
                messageScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            if (messageScroll.horizontalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport)
                messageScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            var viewport = messageScroll.viewport;
            var scrollbar = messageScroll.verticalScrollbar == null ? null : messageScroll.verticalScrollbar.transform as RectTransform;
            // ExpandViewport normally establishes these anchors during its first layout pass. With that driver off,
            // stretch the viewport ourselves so the content and wrapped labels receive the available width.
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(scrollbar == null ? 0 : -Mathf.Max(0, scrollbar.rect.width + messageScroll.verticalScrollbarSpacing), 0);
            var area = messageArea.GetComponent<LayoutElement>();
            area.preferredHeight = messageHeight;
            area.flexibleHeight = 0;
            return area;
        }
        // One padding rule for every button of the panel (CommandBand.ButtonWidth): a button is as wide as its label
        // and the native compact padding on both sides. The native compact button does that by itself for one label.
        // A button whose label changes in place gets the width of its widest label here, so the change moves no
        // control. Returns the width, or 0 when the label could not be measured and the button keeps its own size.
        float FitButton(IConfigurableElement button, params string[] labels) {
            var text = button.RectTransform.GetComponentInChildren<TMP_Text>(true);
            if (text == null) return 0;
            var widths = labels.Where(label => !String.IsNullOrEmpty(label)).Select(label => text.GetPreferredValues(label).x).ToArray();
            if (widths.Length == 0 || widths.Any(width => !(width > 0))) return 0;
            var fit = CommandBand.ButtonWidth(widths);
            var fitter = button.RectTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            var layout = button.RectTransform.GetComponent<LayoutElement>() ?? button.RectTransform.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = fit; layout.flexibleWidth = 0;
            return fit;
        }
        // A row of actions, from the left: a tab's pinned row and the footer's row. Every button follows the one
        // padding rule, with the widest of its labels where the label changes in place. A row that would not fit
        // the inspector's minimum with its full labels takes the shorter label of each button that has one. blocked
        // disables every button of the row with one reason. A disabled button's tooltip gives only why it cannot be
        // used (CommandBand.Tooltip).
        RectTransform ActionRow(UIPanelBuilder builder, Act[] acts, string blocked, bool allowDuringRepair = false) {
            var actionRow = builder.HStack(row => {
                row.ConfigureLayout(CommandBand.ActionRowSpacing);
                var shown = acts.Where(act => act.Label != null).ToArray();
                var buttons = new List<IConfigurableElement>();
                var widths = new List<float>();
                foreach (var slot in shown) {
                    var act = slot;
                    var reason = blocked ?? act.Reason;
                    var button = Button(row, act.Label, act.Run);
                    if (act.Live == null) button.Inactive(reason != null).Tooltip(act.Title ?? act.Label, CommandBand.Tooltip(act.Tip, reason));
                    else {
                        // The state and the tooltip's reason are read from the fields as they are typed.
                        var live = act.Live; var title = act.Title ?? act.Label; var tip = act.Tip;
                        button.Inactive(reason != null);
                        var provider = button.RectTransform.GetComponent<UI.Tooltips.UITooltipProvider>() ?? button.RectTransform.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
                        provider.DynamicTooltipInfo = () => new TooltipInfo(title, CommandBand.Tooltip(tip, blocked ?? live()));
                        typed.Add(() => { if (button.RectTransform != null) button.Inactive((blocked ?? live()) != null); });
                    }
                    buttons.Add(button);
                    widths.Add(FitButton(button, act.Label, act.Other));
                }
                if (widths.Count != 0 && widths.All(width => width > 0) && !CommandBand.RowFits(widths))
                    for (var i = 0; i < shown.Length; i++) {
                        var shorter = CommandBand.ShortLabel(shown[i].Label);
                        if (shorter == shown[i].Label) continue;
                        foreach (var text in buttons[i].RectTransform.GetComponentsInChildren<TMP_Text>(true)) text.text = shorter;
                        FitButton(buttons[i], shorter);
                    }
                row.Spacer();
            });
            if (allowDuringRepair) AllowDuringRepair(actionRow);
            actionRow.ChildrenKeepHeight().Height(RowHeight);
            return actionRow;
        }
        // Each message takes its wrapped height inside the scrolling content.
        RectTransform MessageSlot(UIPanelBuilder builder, string source, string markup, bool older) {
            var label = builder.AddLabel((older ? "<alpha=#80>" : "") + (String.IsNullOrEmpty(source) ? "" : "<b>" + Literal(source) + "</b> ") + markup)
                .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.Normal);
            // Leave a small gap between the text and the viewport edge.
            foreach (var text in label.GetComponentsInChildren<TMP_Text>(true)) text.margin = new Vector4(0, 0, 4, 0);
            return label;
        }
        static string ToneOf(string text) {
            foreach (var word in new[] { " refused", " failed", "unavailable", "could not", " stopped", " blocked by" }) if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return Red;
            foreach (var word in new[] { " completed", " complete.", " copied", "requested again" }) if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return Green;
            foreach (var word in new[] { "Terrain updated" }) if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return Green;
            if (text.IndexOf("outstanding", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Terrain pending", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("the column stops", StringComparison.OrdinalIgnoreCase) >= 0) return Amber;
            return null;
        }
        // The end of a long text, with a leading ellipsis.
        static string Tail(string text, int length) => text == null || text.Length <= length ? text : "..." + text.Substring(text.Length - length);
        void CancelSelection() { selection.Cancel(); selectionProblem = null; ClearSnap(); }
        // The footer's row: the same on every tab. A pending world choice and an active gizmo take it over.
        Act[] FooterActs() {
            var pending = selection?.Pending;
            if (pending != null) {
                var candidate = selection.Find(pending.Candidate);
                var none = candidate == null ? "Click a marking or track in the world first." : null;
                return new[] {
                    Slot("Confirm", "Confirm the candidate's identity. Confirming changes nothing.", none, ConfirmSelection),
                    Slot("Cancel", "Cancel the world choice.", null, CancelSelection),
                    Slot("Focus", "Move the camera to the candidate.", none, () => {
                        try { if (candidate != null) JumpTo(candidate.Position); }
                        catch (Exception e) { selectionProblem = "Camera focus unavailable: " + e.GetBaseException().Message; }
                    }),
                    // Only the clicked candidate's details, in the message area; nothing to show until a click. The
                    // session details open from the band's Details alone.
                    Slot("Details", "Show or hide the clicked candidate's identity and details in the message area.", CommandBand.CandidateDetailsReason(pending.Offers.Count), () => candidateDetails = !candidateDetails, "Candidate details")
                };
            }
            // The gizmo owns the row only during a drag, when Escape cancels; while it stands, the row is the general one.
            if (gizmo?.Active == true) return new[] { Slot("Cancel", "Cancel the drag and restore the placement.", null, () => gizmo?.Cancel(), "Cancel gizmo drag") };
            return new[] { RemoveModificationAct(), RecheckAct(), GuideAct() };
        }
        // Why the inspected object's modification cannot be removed or rechecked now, or null. A starter placement
        // marker's modification is removed or rechecked from every tab.
        string ModificationBlock() => FileLocked ? FileLockReason : selected == null ? "Inspect an object first." : IsStarter ? StarterBlock() : ObjectEditBlock();
        // Remove modification, which meets the several-modifications refusal. The whole modification goes: the control
        // says so in full and takes two cells. It stands in the full window's footer only.
        Act RemoveModificationAct() {
            var identity = selected?.Identity;
            var unchanged = ModificationBlock() ?? (IsChanged(selected) ? null : "This mod does not change this object.") ?? severalModifications;
            var modObject = false;
            try { modObject = identity != null && attachment != null && attachment.Session.IsModObject(identity); } catch { }
            return Slot(CommandBand.RemoveModification, modObject ? DuplicateState.RemoveTip : selected != null && selected.Identity.Structure == CrossingCoverageScene.Structure ? "Remove this mod's modification. Each circuit returns to the coverage it would have without this modification. This mod's detection additions and added lights, gates, and bells are deleted." : "Remove this mod's modification. The object returns to its underlying state.", unchanged, () => Edit(ModificationPlace, "Removal", (current, inspection) => {
                var removed = current.Remove(identity, inspection);
                // Removal deletes the crossing's additions and returns its circuits: a form that was open for one closes.
                if (removed?.Applied == true) { CloseCoverageForm(); CloseEquipmentForm(); }
                return removed;
            }), null, 2);
        }
        // Recheck, which does not meet the several-modifications refusal.
        Act RecheckAct() {
            var identity = selected?.Identity;
            return Slot("Recheck", "Compare the object with the scene patch file after a failed edit. Apply the file's settings wherever they differ.", ModificationBlock(), () => {
                // Recheck finds and inspects the object again: the panel's own Refresh reads the scene first, so a failed
                // attempt that changed the object needs no separate Refresh before its Recheck.
                Inspect();
                Edit(ModificationPlace, "Recheck", (current, inspection) => current.Recheck(identity, inspection), guarded: false);
            });
        }
        // While the inspected object's cleanup is unverified: what remains and how the guard ends, or null. Read from the
        // session's record without searching the scene, so it can run with every rebuild.
        string CleanupState() {
            var session = attachment?.Session;
            if (session == null || selected == null || !session.CleanupUnverified(selected.Identity)) return null;
            var left = session.RemainingState(selected.Identity);
            return "Cleanup after a failed edit is incomplete. " + (left.Count == 0 ? "" : "What remains: " + String.Join("; ", left) + ". ") + "Click Recheck or reload the Map Editor.";
        }
        // Opens the guide of the operation whose failure the message area shows; without a failure there is nothing to open.
        Act GuideAct() {
            var recovery = feedback.Recovery;
            return Slot("Guide", recovery == null ? "Open the guide for the failed action." : "Open the guide for the failed action: " + recovery.GuideTitle + ".",
                recovery == null ? "There is no failed action to show a guide for." : null, () => { var shown = feedback.Recovery; if (shown != null) Application.OpenURL(GuideRoot + shown.Guide); }, "Guide for the failed action");
        }
        // The actions of one tab, for its pinned row. They belong to the open tab and mode only.
        Act[] TabActs(string id) {
            var block = selected == null ? "Inspect an object first." : EditBlock();
            var identity = selected?.Identity;
            switch (id) {
                case PlacementTab:
                    block = (selected == null ? null : EditUnavailable(SupportedEdits.Move)) ?? block;
                    // An object whose placement stands as running lines keeps Apply and Read, inactive with the reason.
                    if (placementMode == 0 || IsPad) {
                        var placementBlock = block;
                        return new[] {
                            Typed(Slot("Apply", "Apply the entered position and rotation. Placement does not change a track binding.", null, ApplyPlacement),
                                () => CommandBand.NumericActionReasons(placementBlock, placementBlock == null ? CommandBand.FieldReason(null, placementFields, CurrentPlacement(), CommandBand.SamePlacement) : null).Apply),
                            Slot("Read", "Fill the fields with the inspected values.", CommandBand.NumericActionReasons(placementBlock, null).Read, ReadNumericPlacement), Empty, Empty };
                    }
                    // The Gizmo mode has no action: the gizmo stands on the object while the mode is open.
                    if (placementMode == 1) return new Act[0];
                    if (placementMode == 2) {
                        var unavailable = block ?? (snap == null ? "Snapping is unavailable." : null);
                        var unprepared = unavailable ?? (preparedSnap == null ? "Choose a track or endpoint and confirm it first." : null);
                        return new[] {
                            Slot("Track", "Choose the track to snap to.", unavailable, () => BeginSnap(SelectionKind.TrackSegment)),
                            Slot("Node", "Choose the track node, an endpoint, to snap to.", unavailable, () => BeginSnap(SelectionKind.TrackNode)),
                            Slot("Apply", "Move the object once to the snap position. Rotation and binding stay.", unprepared, () => {
                                var request = preparedSnap;
                                Edit(SnapPlace, "Physical snap", (session, inspection) => snap.Apply(request), true);
                            }),
                            Slot(CommandBand.DiscardPrepared, "Discard the prepared snap. The object stays where it is.", unprepared, DiscardSnap, "Discard prepared snap") };
                    } else {
                        var unavailable = block ?? (trackPoses == null ? "Track-following placement is unavailable in this game build." : null);
                        var picked = FollowTarget();
                        TrackFollowing following = null;
                        if (unavailable == null) try { following = attachment.Session.FollowingOf(identity); } catch { }
                        return new[] {
                            Slot("Track", "Choose the track to follow.", unavailable, () => BeginSelection(SelectionKind.TrackSegment, selected)),
                            Slot("Node", "Choose the track node, an endpoint, to follow.", unavailable, () => BeginSelection(SelectionKind.TrackNode, selected)),
                            Slot("Start", "Start following: keep the current placement relative to the picked track or endpoint.", unavailable ?? (picked == null ? "Choose a track or endpoint and confirm it first." : null), () => {
                                var maps = picked.MapIds.ToArray();
                                Edit(FollowPlace, "Track following", (current, inspection) => {
                                    if (!maps.SequenceEqual(capabilities.MapIds)) throw new InvalidOperationException("The map context changed after the track was selected. " + WindowGuidance.SelectTrackAgain);
                                    var anchor = picked.Identity.Kind == SelectionKind.TrackNode ? TrackAnchor.Endpoint(picked.Identity.Identifier)
                                        : TrackAnchor.At(trackPoses.Nearest(picked.Identity.Identifier, ((GameObject)selected.Handle).transform.position));
                                    return current.Follow(identity, anchor, inspection);
                                }, true);
                            }, "Start following"),
                            picked != null ? Slot("Cancel", "Cancel the confirmed track or endpoint.", null, () => { selection.Consume(); EndWorldEditAction(); }) :
                            Slot("Stop", "Stop following and keep the current placement.", unavailable ?? (following == null ? "This object does not follow a track." : null),
                                () => Edit(FollowPlace, "Stop following", (current, inspection) => current.StopFollowing(identity, inspection)), "Stop following") };
                    }
                case ConnectionsTab: {
                    var structure = identity?.Structure;
                    // A crossing keeps its own row while an edit block holds; each control is inactive with the block.
                    if (attachment != null && structure == CrossingCoverageScene.Structure && coverageCircuit == null && equipmentSource != null) {
                        var session = attachment.Session;
                        var equipmentId = (equipmentFields[0] ?? "").Trim();
                        IReadOnlyList<EquipmentEntry> entries = Array.Empty<EquipmentEntry>();
                        try { entries = session.EquipmentEntries(identity); } catch { }
                        // Removal applies to an addition the scene patch file holds, whether or not it was built.
                        var inScenePatchFile = entries.Any(e => e.Addition.Id == equipmentId);
                        var prepared = preparedEquipment != null && preparedEquipment.Preview.Error == null ? preparedEquipment : null;
                        return new[] {
                            Slot("Preview", "Preview this " + EquipmentKind + ".", block, () => {
                                var value = ParseEquipment(out var problem);
                                if (value == null) feedback.Refuse(EquipmentPlace, "Equipment preview", problem); else PrepareEquipment(session, identity, value, null);
                            }),
                            Slot("Remove", "Preview removing this " + EquipmentKind + ".", block ?? (inScenePatchFile ? null : "This " + EquipmentKind + " is not in the scene patch file yet."), () => PrepareEquipment(session, identity, null, equipmentId)),
                            Empty,
                            Slot("Apply", "Apply the previewed change.", block ?? (prepared == null ? "Click Preview first." : null), () => Edit(EquipmentPlace, EquipmentPlace, (edit, inspection) => {
                                var done = prepared.Addition != null ? edit.AddEquipment(identity, prepared.Addition, inspection) : edit.RemoveEquipment(identity, prepared.RemoveId, inspection);
                                if (done?.Applied == true && prepared.Addition == null) CloseEquipmentForm();
                                // An applied addition is the one the open form now edits, so a later Undo or Redo refills the form from it.
                                else if (done?.Applied == true) equipmentEdited = prepared.Addition.Id;
                                return done;
                            }, true)) };
                    }
                    if (attachment != null && structure == CrossingCoverageScene.Structure) {
                        var session = attachment.Session;
                        var circuitKey = coverageCircuit; var additionId = coverageAddition;
                        var none = block ?? (circuitKey == null ? "Click Edit circuit or Add detection first." : null);
                        CrossingCoverage current = null;
                        try { current = session.Coverage(identity); } catch { }
                        // A crossing keeps at least one covered circuit; the preview and the edit refuse the last detach too.
                        var last = none == null && CrossingCoverage.LastCovered(current, circuitKey);
                        var prepared = preparedCoverage != null && preparedCoverage.Circuit == circuitKey && preparedCoverage.Preview.Error == null ? preparedCoverage : null;
                        Action apply = () => Edit(CoveragePlace, prepared?.Addition != null ? RecoveryGuidance.DetectionAddition : "Crossing coverage", (edit, inspection) => {
                            if (prepared.Addition == null) return edit.Cover(identity, prepared.Circuit, prepared.Value, inspection);
                            if (prepared.Value != null) return edit.AddDetection(identity, prepared.Addition, prepared.Value, inspection);
                            var deleted = edit.DeleteDetection(identity, prepared.Addition, inspection);
                            // The deleted addition's form closes with it.
                            if (deleted?.Applied == true) CloseCoverageForm();
                            return deleted;
                        }, true);
                        var preview = Slot("Preview", "Preview the selected tracks for every circuit.", none, () => {
                            var value = ParseCoverage(out var problem, additionId != null);
                            if (value == null) feedback.Refuse(CoveragePlace, "Coverage preview", problem); else PrepareCoverage(session, identity, circuitKey, value, additionId);
                        });
                        if (additionId != null) {
                            // Deleting applies to an addition the scene patch file holds; a crossing keeps at least one detection.
                            var inScenePatchFile = current != null && current.Circuits.Any(c => c.Value.Kind == CircuitKind.Added && DetectionAddition.Id(c.Key) == additionId);
                            return new[] { preview,
                                Slot("Delete", "Preview deleting this detection addition.", block ?? (!inScenePatchFile ? "This addition is not in the scene patch file yet." : last ? "This is the crossing's last detection; return a native circuit to its coverage first." : null), () => PrepareCoverage(session, identity, circuitKey, null, additionId)),
                                Empty,
                                Slot("Apply", "Apply the previewed coverage.", block ?? CommandBand.CoverageApplyBlock(prepared != null, prepared?.Preview.Unchanged == true), apply) };
                        }
                        return new[] { preview,
                            Slot("Detach", "Preview detaching this circuit.", none ?? (last ? "This is the crossing's last covered circuit; detaching it would leave the crossing without detection." : null), () => PrepareCoverage(session, identity, circuitKey, CircuitCoverage.Detached, null), null, 1, 72),
                            // Every Clear in a tab names what it clears.
                            Slot(CommandBand.ClearCircuit, "Preview clearing this mod's value for the circuit; it returns to its underlying coverage.", none, () => PrepareCoverage(session, identity, circuitKey, null, null), null, 1, 112),
                            Slot("Apply", "Apply the previewed coverage.", none ?? CommandBand.CoverageApplyBlock(prepared != null, prepared?.Preview.Unchanged == true), apply, null, 1, 62) };
                    }
                    // An object that holds no track binding has no track location row.
                    if (!IsStarter && !HoldsConstructionSegments && !HoldsBinding) return new Act[0];
                    var row = TrackLocationActs(block);
                    return new[] { row.Set, row.Detach, row.Clear, row.Pick };
                }
                case ChangesTab: {
                    // Retry belongs to terrain output: an object without it has no action on this tab.
                    if (!HasTerrain) return new Act[0];
                    var idle = block;
                    if (idle == null) { var output = inspectedOutput.Of(attachment.Session, selected, out _); if (output.Outstanding.Count == 0) idle = "No terrain is pending."; }
                    return new[] {
                        Slot("Retry", "Ask the game to rebuild the pending terrain again.", idle, () => {
                            var outcome = attachment.Session.RetryOutput(identity);
                            feedback.Report(TerrainPlace, outcome.Applied ? "Terrain requested again." : "Terrain retry refused: " + outcome.Error);
                        }, "Retry terrain output") };
                }
                default: {
                    var enabled = OwnActivity(selected);
                    // Why the object takes no Enabled setting, as Supported edits names it: such as a part that a culler of
                    // the game switches by camera distance, or a controller whose activity the game needs.
                    var activity = selected == null ? null : EditUnavailable(SupportedEdits.Activity);
                    return new[] {
                        Slot(enabled ? "Disable" : "Enable", enabled ? "Disable this object." : "Enable this object.", activity ?? block, () => { var enable = !enabled; Edit(ActivityPlace, enable ? "Enable" : "Disable", (current, inspection) => current.Edit(identity, null, enable, inspection)); }, null, 1, 0, enabled ? "Enable" : "Disable"),
                        // Named by what it takes back, over two cells; disabled while this mod sets no activity.
                        Slot(CommandBand.ClearActivity, "Clear this mod's value for the activity; the object returns to its underlying activity.", block ?? ControllerActivity ?? CommandBand.ClearActivityReason(HasActivityValue(identity)),
                            () => Edit(ActivityPlace, "Clearing the activity's mod value", (current, inspection) => current.ClearActivity(identity, inspection)), null, 2),
                        // Duplicate makes a new object: it is no edit of this one, so Supported edits does not list it.
                        DuplicateAct() };
                }
            }
        }
        // The four slots of the Connections tab's track location row for a starter placement marker, a rail bridge
        // construction's segments, or a binding: Set, Detach, Clear for the mod value, and Picked or Cancel. The compact
        // rail keeps Set and the last slot.
        struct TrackLocationRow {
            internal Act Set, Detach, Clear, Pick;
            internal TrackLocationRow(Act set, Act detach, Act clear, Act pick) { Set = set; Detach = detach; Clear = clear; Pick = pick; }
        }
        TrackLocationRow TrackLocationActs(string block) {
            var identity = selected?.Identity;
            var structure = identity?.Structure;
            if (structure == StarterPlacementScene.Structure) {
                var confirmedTrack = ConfirmedForInspected?.Identity;
                var picked = confirmedTrack != null && confirmedTrack.Kind == SelectionKind.TrackSegment;
                return new TrackLocationRow(
                    Typed(Slot("Set", "Place the front of the car cut at the entered track location. The whole cut is checked first; the object does not move.", null, ApplyStarterLocation, "Set track location", 1, 56),
                        () => CommandBand.FieldReason(block, binding, CurrentBinding(), CommandBand.SameLocation, CommandBand.BindingIds)),
                    Slot("Detach", "Detach the marker from its track.", block ?? StarterPlacements.NoDetach, () => { }, "Detach track location", 1, 72),
                    Slot(CommandBand.ClearLocation, "Clear this mod's value for the track location; the marker returns to its underlying track location.", block ?? CommandBand.ClearLocationReason(HasStarterTrack(identity)),
                        () => Edit(BindingPlace, "Clearing the track location's mod value", (current, inspection) => current.ClearStarterTrack(identity, inspection)), null, 1, 124),
                    HasPickedBinding ? Slot("Cancel", "Restore the current track location in the fields.", null, CancelPickedBinding) :
                    Slot("Picked", "Fill the segment field with the confirmed segment.", block ?? (picked ? null : "Choose a segment with Segment and confirm it first."), () => { binding[0] = confirmedTrack.Identifier; }, "Picked segment", 1, 70));
            }
            if (HoldsConstructionSegments) {
                var current = CurrentSegments();
                var chosen = ConfirmedForInspected?.Identity;
                var pickedSegment = chosen != null && chosen.Kind == SelectionKind.TrackSegment;
                return new TrackLocationRow(
                    Typed(Slot("Set", "Replace the construction segments with exactly the segments in the field. The bridge does not move.", null, ApplySegments, "Set construction segments", 1, 56),
                        () => CommandBand.SegmentsReason(block, segmentFields[0], current)),
                    Slot("Detach", "Detach the construction from its segments.", block ?? CommandBand.SegmentDetachReason, () => { }, "Detach construction segments", 1, 72),
                    Slot(CommandBand.ClearSegments, "Clear this mod's construction segments value to return the construction to its underlying segments.", block ?? CommandBand.ClearSegmentsReason(HasSegmentValue(identity)),
                        () => Edit(SegmentsPlace, "Clearing the construction segments value", (edit, inspection) => edit.SetConstructionSegments(identity, null, inspection)), null, 1, 124),
                    Slot("Picked", "Add the confirmed segment to the segments field and remove segments that no longer exist.", block ?? (pickedSegment ? null : "Click Segment to choose a segment, then confirm it."),
                        () => { segmentFields[0] = CommandBand.AddSegment(segmentFields[0], chosen.Identifier, CommandBand.MissingSegments(selected?.Bindings)); FieldsChanged(); }, "Picked segment", 1, 70));
            }
            var pad = structure == CrossingPadScene.Structure;
            // An object without a binding names that first; why it cannot be edited in general comes second.
            var unavailable = pad || structure == TrackMarkerScene.Structure || structure == MilepostScene.Structure ? block : selected == null ? block : BindingUnavailable();
            var confirmed = ConfirmedForInspected?.Identity;
            var segment = confirmed != null && confirmed.Kind == SelectionKind.TrackSegment;
            return new TrackLocationRow(
                Typed(Slot("Set", pad ? "Register the pad's marker at the entered track location and regenerate the pad there." : "Register the marker at the entered track location. The object does not move.", null, ApplyBinding, "Set binding", 1, 56), () => CommandBand.FieldReason(unavailable, binding, CurrentBinding(), CommandBand.SameBinding, CommandBand.BindingIds)),
                Slot("Detach", "Detach the marker from its track.", unavailable ?? (pad ? "A crossing pad cannot be detached. Disable it on the Object tab to hide it." : CurrentBinding()[0].Length == 0 ? CommandBand.AlreadyDetached : null), () => Edit(BindingPlace, "Track detachment", (current, inspection) => current.Bind(identity, MarkerBinding.Detached, inspection)), "Detach binding", 1, 72),
                Slot(CommandBand.ClearBinding, "Clear this mod's value for the binding; the marker returns to its underlying binding.", unavailable ?? CommandBand.ClearBindingReason(HasBindingValue(identity)), () => Edit(BindingPlace, "Clearing the binding's mod value", (current, inspection) => current.Bind(identity, null, inspection)), null, 1, 124),
                HasPickedBinding ? Slot("Cancel", "Restore the current binding in the fields.", null, CancelPickedBinding) :
                Slot("Picked", "Fill the segment field with the confirmed segment.", unavailable ?? (segment ? null : "Choose a replacement segment and confirm it first."), () => { binding[0] = confirmed.Identifier; }, "Picked segment", 1, 70));
        }
        // A construction controller on an object of its own takes no placement and no Enabled setting: its position has
        // no effect, and the construction's visibility control runs on it.
        bool IsController => selected?.Identity.Structure == ConstructionSceneryScene.Structure;
        string ControllerActivity => IsController ? CommandBand.ControllerNoActivity : CommandBand.EditsCoverage(selected?.Identity) ? SupportedEdits.CrossingNoActivity : null;
        bool HasStarterTrack(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasStarterTrack(identity); } catch { return false; }
        }
        static bool OwnActivity(SceneObservation observation) {
            var target = observation?.Handle as GameObject;
            // Own culling switches a moved mast by camera distance, as it does a moved part: the observation reads what the scene or the modder set.
            // An object a progression controller or a map feature switches reads whether no explicit disable holds it off.
            return target != null && (SceneObjectRules.InFamilies(observation.Family) || observation.Family == ExistingEquipmentScene.MastFamily ? observation.Enabled : GroupWorld.ProgressionEnabled(target) ?? target.activeSelf);
        }
        static double[] EnteredRotation(double x, double y, double z) {
            var rotation = Quaternion.Euler((float)x, (float)y, (float)z);
            return new double[] { rotation.x, rotation.y, rotation.z, rotation.w };
        }
        static Placement EnteredPlacement(string[] fields, double[] own, SceneObservation observation) =>
            EnteredEditValues.Placement(fields, own, observation?.Rotation, EnteredRotation);
        void ApplyStarterLocation() {
            var identity = selected.Identity;
            var location = EnteredEditValues.TrackLocation(binding);
            if (location == null) {
                feedback.Refuse(BindingPlace, "Track location", "the segment ID, end a or b, or numeric distance is missing or invalid. " + WindowGuidance.EnterTrackLocation);
                return;
            }
            Edit(BindingPlace, CommandBand.StarterAction(location.Segment, location.End, location.Distance), (current, inspection) => current.PlaceStarter(identity, location, inspection), true);
        }
        bool HasActivityValue(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasActivityValue(identity); } catch { return false; }
        }
        void ApplyPlacement() {
            var identity = selected.Identity;
            var placement = EnteredPlacement(placementFields, PlacementValues(), selected);
            if (placement == null) { feedback.Refuse(PlacementPlace, "Placement", "the six position and rotation values aren't valid numbers. " + WindowGuidance.EnterPlacement); return; }
            Edit(PlacementPlace, "Placement", (current, inspection) => current.Edit(identity, placement, null, inspection));
        }
        void ApplyBinding() {
            var identity = selected.Identity;
            var location = EnteredEditValues.TrackLocation(binding);
            if (location == null) {
                feedback.Refuse(BindingPlace, "Track binding", "the segment ID, end a or b, or numeric distance is missing or invalid. " + WindowGuidance.EnterTrackLocation);
                return;
            }
            var value = MarkerBinding.At(location);
            Edit(BindingPlace, CommandBand.BindingAction(location.Segment, location.End, location.Distance), (current, inspection) => current.Bind(identity, value, inspection), true);
        }

        // The World choice section of the session details window: what the choice is for, the candidate that was clicked, and
        // the other candidates at that point, each named by what tells it apart from the others.
        void DrawChoice(UIPanelBuilder builder) {
            var pending = selection?.Pending;
            if (pending == null) return;
            Section(builder, "World choice", divider: false);
            PlainLabel(builder, (choosingSnap ? "Select physical snap target" : pending.Purpose) + ".");
            if (pending.Source != null) PlainLabel(builder, "Object: " + pending.Source.Label);
            if (pending.Problem != null) builder.AddLabel(PendingSelection.IsInstruction(pending.Problem) ? Literal(pending.Problem) : Tint(Red, pending.Problem));
            var candidate = selection.Find(pending.Candidate);
            if (candidate == null && pending.Offers.Count == 0) Line(builder, "Nothing clicked yet. Click a marking or track in the world.", null, null);
            if (candidate != null) {
                builder.AddLabel(Tint(Blue, CommandBand.ChoiceTag("Candidate", pending.Number(candidate.Choice), pending.Offers.Count) + ": " + CommandBand.ChoiceLabel(candidate.Choice.Label, pending.Offers.Select(o => o.Label), 60, candidate.Observation)));
                PlainLabel(builder, candidate.Choice.Identity.ToString());
                PlainLabel(builder, candidate.Details);
                Button(builder, "Inspect candidate", () => {
                    if (candidate.Observation == null) return;
                    CancelGizmo(); InspectObservation(candidate.Observation); ReadPlacement();
                }).Inactive(candidate.Observation == null).Tooltip("Inspect candidate", CommandBand.Tooltip("Inspect the candidate without confirming it.", candidate.Observation == null ? "Only a scene object can be inspected." : null));
            }
            var labels = pending.Offers.Select(offer => offer.Label).ToArray();
            foreach (var offer in pending.Offers) {
                if (ReferenceEquals(offer, pending.Candidate)) continue;
                var choice = offer;
                // The label leads with the name that tells this candidate from the others, and is cut at its end.
                // and carries the number of the candidate's label in the world. With the pointer over the button the
                // world marks that candidate as it marks one under the pointer, so the name and the object meet.
                var button = Button(builder, CommandBand.ChoiceTag("Choose", pending.Number(choice), pending.Offers.Count) + ": " + CommandBand.ChoiceLabel(choice.Label, labels, observation: selection.Find(choice)?.Observation), () => pending.Choose(choice));
                button.Tooltip("Choose this candidate", choice.Label);
                button.RectTransform.gameObject.AddComponent<HoverReport>().Changed = over => {
                    if (over) pending.Highlight = choice; else if (ReferenceEquals(pending.Highlight, choice)) pending.Highlight = null;
                };
            }
        }
        const string ReloadTip = "Reload the Map Editor. If you have unsaved work, you can save or discard it first. Reloading clears the Undo and Redo history.";
        // The session details, in their own window: session state that the panel shows nowhere else. The messages are
        // in the footer and the reasons of the two lamps in their tooltips, so they are not repeated here.
        void DrawSession(UIPanelBuilder builder) {
            if (attachment?.Session.VersionState == FileVersionState.Repair) {
                Section(builder, "Scene patch file", divider: false);
                PlainLabel(builder, FullDiagnosis(attachment.Session));
                // A file that needs repair offers its format reference and recovery guide, as the recovery section does.
                ReferenceButtons(builder);
                Button(builder, "Copy to clipboard", () => GUIUtility.systemCopyBuffer = FullDiagnosis(attachment.Session))
                    .Tooltip("Copy to clipboard", "Copy the full diagnosis to share or save it.");
                Button(builder, "Reload Map Editor...", () => MapEditorReload.Request(attachment, MapEditorReload.ActiveMapId))
                    .Disable(MapEditorReload.Error != null).Tooltip("Reload Map Editor", CommandBand.Tooltip(ReloadTip, MapEditorReload.Error == null ? null : CommandBand.Sentence(MapEditorReload.Error)));
                if (MapEditorReload.Error != null) PlainLabel(builder, MapEditorReload.Error);
                return;
            }
            DrawChoice(builder);
            Section(builder, "Inventory", divider: selection?.Pending != null);
            PlainLabel(builder, status);
            if (scene != null) PlainLabel(builder, scene.Status);
            Section(builder, "Editing");
            // A message from the game or the file system can hold a full path: only its part below a known folder is shown.
            builder.AddLabel(PathBreaks(Literal(String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : PatcherLog.WithoutAbsolutePaths(editingStatus))));
            if (attachment != null) {
                var session = attachment.Session;
                if (reloadRequirement != null) PlainLabel(builder, "Reload required: " + reloadRequirement);
                Button(builder, "Reload Map Editor...", () => MapEditorReload.Request(attachment, MapEditorReload.ActiveMapId))
                    .Disable(MapEditorReload.Error != null).Tooltip("Reload Map Editor", CommandBand.Tooltip(ReloadTip, MapEditorReload.Error == null ? null : CommandBand.Sentence(MapEditorReload.Error)));
                if (MapEditorReload.Error != null) PlainLabel(builder, MapEditorReload.Error);
                if (session.LastSave != null && session.LastSave.Error != null) builder.AddLabel(Tint(Red, "Last Save failed: " + session.LastSave.Error + " Completed: " + (session.LastSave.Completed.Count == 0 ? "none" : String.Join(", ", session.LastSave.Completed.Select(PatchEditingSession.DisplayItem))) + ". Outstanding: " + String.Join(", ", session.LastSave.Outstanding.Select(PatchEditingSession.DisplayItem)) + ". Edits remain in memory; correct the cause and Save again."));
                if (session.IncompleteOperation != null) builder.AddLabel(Tint(Red, "Incomplete operation: " + session.IncompleteOperation));
                if (attachment.Fault.Text != null) builder.AddLabel(Tint(Red, attachment.Fault.Text));
            }
            DrawScenePatchFileRecovery(builder);
            Section(builder, "Supplied definitions");
            if (String.IsNullOrEmpty(scenePatchFileInspection)) Line(builder, definitionsStale ? "Not read yet. Click Refresh." : "None", null, null);
            else PlainLabel(builder, scenePatchFileInspection);
            Section(builder, "Diagnostics");
            if (diagnostics.Count == 0 && presentation.EntryProblem == null) Line(builder, "None", null, null);
            if (presentation.EntryProblem != null) builder.AddLabel(Tint(Amber, presentation.EntryProblem));
            foreach (var diagnostic in diagnostics) PlainLabel(builder, diagnostic);
        }

        // Context help of one region: an introduction, short terms and the guides. It opens in the help window beside
        // the region's "?" and replaces nothing in the panel.
        sealed class HelpText {
            internal string Title, Intro;
            internal string[][] Terms, Guides;
        }
        static readonly Dictionary<string, HelpText> Help = new Dictionary<string, HelpText> {
            ["main"] = new HelpText { Title = "Scene Patcher help", Intro = "Use Scene Patcher to edit scene objects, then click Save in the Map Editor.",
                Terms = new[] {
                    new[] { "Save", "Click Save in the Map Editor to write the map data, Data/scene-patches.json, and the patcher dependency in Info.json. Save never reloads the map." },
                    new[] { "Title bar", "Reset layout restores the window position and outliner width. Session details shows information about this editing session. Click Compact window to switch to the narrow side rail. Click Full window to switch back." },
                    new[] { "Clear", "Click Clear or select the inspected object's row again to end the inspection and cancel any pending world choice, gizmo, or snap. Applied changes stay. During the Values or Review step in Guided mode, either action is available only while a world choice is pending. It cancels the choice and keeps the target and draft. To cancel the task, return to Inspector. With the pointer over this window, press Escape to close an open choice list first. Press Escape again to cancel a drag, a pending world choice, a gizmo, or a snap, one action per press. Escape keeps the inspection and guided task." },
                    new[] { "Undo and Redo", "Every completed edit here is one Undo step. Use Undo and Redo in the Map Editor." },
                    new[] { "Refresh", "Click Refresh to read the scene again without changing it. Do this after an edit, Undo, or Redo in the Map Editor. The patcher refuses edits while the inventory is stale." },
                    new[] { "Map Editor reload", "Reloading the Map Editor clears the Undo and Redo history. Leaving the Map Editor discards unsaved work." },
                    new[] { "Current, Stale, Paused, Saved, Unsaved, Blocked", "The lamps above the outliner show whether the inventory is up to date and whether you need to save. If you see Paused, follow the instructions in Version choice, Repair, or Import before inspecting objects. Blocked means Save is unavailable. Hover over a lamp for details." },
                    new[] { "* and +", "* marks an object this mod changes. + marks an object this mod adds. The Unsaved lamp tells you when to save." },
                    new[] { "Footer", "In the full window, Remove modification, Recheck, and Guide appear below the messages on every tab. The compact window shows Recheck and Guide only on the Changes tab. Remove modification is available only in the full window. Each tab's own actions appear at the bottom of that tab." },
                    new[] { "Failed edit", "A refused edit changes nothing. If an edit fails after it starts, the patcher restores the object to its previous state. If the patcher can't verify the restoration, the message tells you what remains. You can edit again once Recheck succeeds or you reload the Map Editor. Click Guide to open the guide for the action that failed." } },
                Guides = new[] { new[] { "Modder manual", "modder-manual.md" }, new[] { "Guided tasks", "completing-guided-tasks.md" }, new[] { "Scene patch file reference", "scene-patch-file-v1.md" } } },
            ["outliner"] = new HelpText { Title = "Outliner", Intro = "Find an object in the outliner or select it in the world to inspect it.",
                Terms = new[] {
                    new[] { "Search", "Search by name, path, family, binding ID, dependency, problem, category, kind, place, or crossing." },
                    new[] { "By category", "The first view icon lists objects by category, then by kind and place. When objects in a category share a kind, they appear under that kind. For example, Bridge can list Rail bridges and Road bridges, and Sign can list Speed signs and Whistle signs." },
                    new[] { "By place", "The second view icon lists objects by where they stand, with the same categories and kinds under each place. With no search or filter, the outliner lists every place the game names, even places with no objects." },
                    new[] { "Place", "The outliner uses the place named in the object's name or hierarchy path. Otherwise, it uses the nearest place the game names." },
                    new[] { "Changed", "Lists objects this mod changes (*) or adds (+). If the patcher refuses a modification, the row shows × instead. Open the object's Changes tab for the reason." },
                    new[] { "Disabled", "Lists objects that are themselves switched off, whether or not their parents are active. Problems usually leaves them out. See the exception for starter placement markers below." },
                    new[] { "Problems", "Lists objects with detected problems you can repair here. Each of those objects has a ! marker. For example, the game may need a binding it can't use, a binding may refer to a segment that no longer exists, or the game may be unable to use a crossing detection span. Edit limits and things the inspection couldn't read are separate from detected problems. This filter leaves out objects that are themselves switched off. A starter placement marker can still appear here if the game uses it and its track location has a problem. Even without a detected problem, an object may be in the wrong place or use the wrong track." },
                    new[] { "Groups and crossings", "The outliner lists a group's parts and a crossing controller's lights, gates, bells, pads, and detection spans beneath their respective rows. For a crossing without a controller, one row holds its crossbucks and pads. Select that row and click Focus to go to the crossing." },
                    new[] { "Spawn point", "Players can start at a scene spawn point, teleport to it, or return to it after rescue. Edit native spawns in the Map Editor's Spawns category." },
                    new[] { StarterPlacements.Family, "An active new-game setup uses these markers' track locations to place its starting cars. The marker's own position has no effect. Click Focus to move the camera to where the cars stand." },
                    new[] { StationMapIconRules.Family, "These icons show stations on the map. After moving a station in the Map Editor, move its icon to the station's new location." },
                    new[] { "Inspection only", "The patcher has no edit for this kind of object yet, and the Map Editor doesn't edit it. The outliner lists it under its category with no problem marker. The Object tab explains why it isn't supported yet." },
                    new[] { InventoryPresentationState.NoEditableObjectsTerm, "The outliner shows this message when every scene object the patcher checks for editing is one the game creates while the map is running. The patcher can't edit any objects on this map." },
                    new[] { "Dimmed rows", "A group row is dimmed when its parts match the search but the group itself doesn't." },
                    new[] { "Focus", "Moves the camera to the inspected object, the starting point of a car placement, or the selected crossing." },
                    new[] { "Fit names", "Sets the column width to fit most names. Drag the divider to adjust the width." } },
                Guides = new[] { new[] { "Inspecting objects", "inspect-scene-objects.md" }, new[] { "Selecting in the world", "select-world-targets.md" } } },
            [ObjectTab] = new HelpText { Title = "Object", Intro = "Before you edit an object, check its state, problems, and dependencies.",
                Terms = new[] {
                    new[] { "Supported edits", "Available lists the edits you can use. Each bullet under Unavailable gives a reason and the edits it applies to. Hover over a disabled control to see the same reason. These reasons explain edit limits. Look under Detected problems for problems found on the object." },
                    new[] { "Placement limits", "Mileposts, crossing masts and bells, and bridges use fixed placement. A bell's sound has no direction. A crossing pad's track binding determines where the pad goes. Change the binding on the Connections tab. To reposition a bridge, first edit its track or road in the Map Editor, then move the bridge onto it. Moving a construction controller has no effect in the game. Move the bridge or its parts. A starter placement marker's track location determines where the cars start. Moving the marker has no effect on where they start. Set the track location to change where they start." },
                    new[] { "Activity set by the game", "The game switches these objects on or off through a track group, camera distance, progression, a map feature, or bridge construction. When available, Clear activity gives control back to the object's track group. To change a bridge's visibility, disable the bridge or its parts directly. Disabling its construction controller won't hide the bridge." },
                    new[] { "Edit elsewhere", "Edit added lights, gates, bells, and detection span tracks on the crossing controller's Connections tab. Edit a marker on its milepost or crossing pad. Edit construction segments on their group. Edit track markers and spawn points one at a time. Edit each map icon separately. Edit station click spots in the Map Editor. A group that holds a construction controller outside a rail bridge can't be edited as a whole. If the path names a group, edit the whole group. If an object only controls whether its parts are visible, edit the parts directly. If it only holds other groups, edit those groups directly." },
                    new[] { "Not supported", "Some objects and structures have no supported edits. The patcher also refuses an edit if it can't tell the object apart from others at the same hierarchy path. A replacement marker ID is available only for a Generic track marker that shares its ID with another object or already has a replacement ID. Start derailed is available only when one car placement uses the marker. You can edit track bindings for markers, mileposts, and crossing pads. Some game builds can't enable a bell again." },
                    new[] { "Blocked for now", "An edit may be blocked if the patcher can't read the information it needs, the scene isn't loaded, or the game doesn't provide the support the edit needs. A related object can block the edit too. For example, the object's parent may be inactive, an attached object may be out of date, or its crossing controller may be disabled." },
                    new[] { "Detected problems", "Lists the problems the inspection found that you can repair with the patcher. Each entry tells you how to fix the problem. A crossing controller's detection span can end outside its track segment or have no track route between its ends. Either problem prevents the game from detecting trains on that span." },
                    new[] { "Information", "Lists findings the patcher can't repair on this object, with the reason for each. One example is a detection finding on a crossing controller you can't edit." },
                    new[] { "Inspection notes", "Lists what the inspection couldn't read or check. The patcher can't repair these, so they don't appear under Detected problems." },
                    new[] { "Enabled", "Shows whether the object is active. Click Disable to keep it inactive. Click Clear activity to return it to its underlying activity." },
                    new[] { "Track group", "A track group switches some markers on or off. Click Disable to keep the marker inactive. Click Clear activity to let the track group control it again." },
                    new[] { "Dependencies", "Shows direct links to what uses this object or changes with it, such as track locations, terrain masks, and visibility. The short view shows the relative path of each object in the hierarchy and lists its components. Click the triangle beside the heading to see the full lines." },
                    new[] { "Runtime state", "Shows whether the game switches parts of the object on or off while it runs. These changes don't make the inventory stale. As with Dependencies, click the triangle beside the heading to switch between the short view and the full lines." },
                    new[] { "Copy ID", "Copies the object's ID, placement, and the map ID needed by the scene patch file." },
                    new[] { "Duplicate", "Creates a mod object from this object. Click Place to show a ghost that follows the pointer, then left-click the ground to place the mod object. To keep it unplaced, click Release or press Esc to hide the ghost. Click Cancel to discard it. If you select another object in the outliner before placing it, the window asks whether to cancel Duplicate. After placing it, you can edit it like any other object. Click Remove modification to remove it. The original stays as it is." } },
                Guides = new[] { new[] { "Inspecting objects", "inspect-scene-objects.md" }, new[] { "Following a track and group activity", "following-track.md" }, new[] { "Duplicating objects", "duplicating-scene-objects.md" } } },
            [PlacementTab] = new HelpText { Title = "Placement", Intro = "Move or rotate the object without changing its logical binding.",
                Terms = new[] {
                    new[] { "Numeric", "Enter the position in map coordinates and the rotation in degrees." },
                    new[] { "Gizmo", "In Gizmo mode, the Map Editor's gizmo appears on the object. Choose Move or Rotate, then drag a handle. The fields update to show the placement as you drag. Each release is one Undo step. Press Escape during a drag to restore the placement you started from." },
                    new[] { "Snap", "Moves the object onto a track or endpoint once. Click Track or Node, click in the world, then click Confirm and Apply. Click Discard to drop a prepared snap. The object keeps its rotation and binding. Save records fixed coordinates." },
                    new[] { "Follow", "Keeps the object's current placement relative to a track or endpoint. Click Track or Node, click in the world, then click Confirm and Start. The object moves with the track until you click Stop." },
                    new[] { "Bridges and location signs", "Moving a bridge moves the whole bridge. Its track scenery stays in place. Moving a location sign or an object that holds a scene ground mask regenerates its terrain output." },
                    new[] { "Groups and parts", "Moving a group also moves its parts. Each part keeps its own Enabled setting. A part with a fixed placement stays at its map coordinates when the group moves." },
                    new[] { "Spawn points", "The game uses a spawn point's current location whenever a player starts, is rescued, or teleports. The game offers a disabled spawn point only through a start setup that names it. A duplicated spawn point has lower priority than the map's spawn points. Enter a name in Teleport name on the Object tab and click Set so players can find the spawn point and teleport there." } },
                Guides = new[] { new[] { "Fixed placement", RecoveryGuidance.NumericPlacementGuide }, new[] { "Transform gizmos", "using-transform-gizmos.md" }, new[] { "Snapping", "snapping-to-track.md" },
                    new[] { "Following a track", "following-track.md" }, new[] { "Bridges", "placing-bridges.md" }, new[] { "Location signs", "moving-location-signs.md" }, new[] { "Groups and parts", "moving-scene-objects.md" }, new[] { "Spawn points", "moving-spawn-points.md" },
                    new[] { "Station map icons", "moving-station-map-icons.md" }, new[] { "Existing crossing equipment", "moving-crossing-equipment.md" } } },
            [ConnectionsTab] = new HelpText { Title = "Connections", Intro = "See what the object is connected to and which connections you can change.",
                Terms = new[] {
                    new[] { "Track binding", "The track location where the game registers a marker. Changing the binding leaves the object in place." },
                    new[] { "Replacement targets", "Select a target in the world, check its ID, then click Confirm. Confirm changes nothing. The connection changes only when you click Set or Apply." },
                    new[] { "Set, Detach, Clear binding", "Click Set to register the marker at the location you entered. Click Detach to remove it from the track. Click Clear binding to restore its underlying binding." },
                    new[] { "Crossing coverage", "Each circuit has an approach A, an island, and an approach B. Select which tracks the circuit covers, or detach it. A nearby track is covered only if it's part of a circuit. Click Preview to see the result for every circuit before you click Apply." },
                    new[] { "Detection addition", "To give an extra track its own circuit, enter an ID, click Add detection, and select the tracks for its three spans. Use Delete to remove the addition." },
                    new[] { "Lights and gates", "To add a copy of a light or gate already loaded in the crossing's scene, click its Use button. The crossing controller operates it and shows or hides it with its own equipment. The list shows only usable choices." },
                    new[] { "Bells", "Add a bell that sounds like one the game uses at grade crossings. It rings whenever the crossing controller rings its own bell. The list shows only usable choices." },
                    new[] { "Crossing pad", "A pad uses its marker's binding. Setting the binding rebuilds the pad along the track at that location and leaves the pad object in place. To hide the pad, disable it. Detach is unavailable." },
                    new[] { "Milepost", "Set a milepost's track binding to register its marker at that track location. The post stays in place and keeps its model and mile value." },
                    new[] { "Replacement marker ID", "The game uses this ID to register the marker's track location. If scene markers share an ID, changing one removes all their registrations. The patcher therefore refuses changes to Enabled, the binding, and Follow. Click Generate or enter an unused ID, then click Set replacement ID. This marker gets the new ID and the others keep their registrations. Click Clear replacement ID to restore the underlying ID." },
                    new[] { "Construction segments", "The game shows parts of a rail bridge according to the construction state of these track segments. After splitting or replacing the bridge's track in the Map Editor, select the new segments and click Set. Detach is unavailable. Click Clear segments to restore the underlying segments." } },
                Guides = new[] { new[] { "Track bindings", "repairing-track-bindings.md" }, new[] { "Marker IDs", "repairing-duplicate-marker-ids.md" }, new[] { "Mileposts", "repositioning-mileposts.md" }, new[] { "Crossing pads", "rebinding-crossing-pads.md" }, new[] { "Crossing coverage", "correcting-crossing-coverage.md" }, new[] { "Adding detection", "adding-crossing-detection.md" }, new[] { "Lights and gates", "adding-crossing-equipment.md" }, new[] { "Bells", "adding-crossing-bells.md" }, new[] { "Construction segments", "repairing-construction-segments.md" }, new[] { "Starter placement", "placing-starter-markers.md" }, new[] { "Selecting in the world", "select-world-targets.md" } } },
            [ChangesTab] = new HelpText { Title = "Changes", Intro = "See what this mod changes on the object and finish any work that's still pending.",
                Terms = new[] {
                    new[] { "Remove modification", "Removes this mod's modification and restores the object's underlying state. It doesn't restore an earlier patched position." },
                    new[] { "Recheck", "After a failed edit, click Recheck to compare the object with the scene patch file. Once they agree, you can edit the object again." },
                    new[] { "Terrain output", "The game regenerates terrain output after you move a board or a ground mask. Save doesn't write that output. Click Retry to ask the game to rebuild it." } },
                Guides = new[] { new[] { "Editing in the Map Editor", "editing-placement-in-the-map-editor.md" }, new[] { "Location signs", "moving-location-signs.md" }, new[] { "File recovery", "recover-scene-patch-files.md" }, new[] { "Scene patch file reference", "scene-patch-file-v1.md" } } },
        };
        void DrawHelp(UIPanelBuilder builder, string region) {
            if (!Help.TryGetValue(region, out var help)) return;
            builder.AddLabel("<style=H3>" + help.Title + "</style>");
            // The window commands stand in the title bar; where it could not take one, the window's help holds it.
            if (region == NativePresentation.WindowHelpRegion && (!presentation.TitleReset || !presentation.TitleDetails || !presentation.TitleCompact)) builder.HStack(row => {
                if (!presentation.TitleReset) Button(row, "Reset layout", ResetLayout).Tooltip("Reset layout", "Restore the default window position and outliner width. The help and session details windows move back beside Scene Patcher, and the compact window returns to the full window.");
                if (!presentation.TitleDetails) Button(row, "Session details", () => presentation.ToggleDetails()).Tooltip("Session details", "Open or close the separate session details window.");
                if (!presentation.TitleCompact) Button(row, "Compact window", () => presentation.ToggleCompact()).Tooltip("Compact window", "Switch to the narrow side rail to see more of the world.");
            }).Height(RowHeight);
            builder.AddLabel(help.Intro);
            // Each term is a caption above its explanation, as a named value is in the tabs.
            foreach (var term in help.Terms) { builder.Spacer(2f); FieldLabel(builder, term[0]); builder.AddLabel(term[1]); }
            Section(builder, "Guides");
            foreach (var guide in help.Guides) {
                var document = guide[1];
                Button(builder, guide[0], () => Application.OpenURL(GuideRoot + document)).Tooltip(guide[0], "Open this guide in the browser.");
            }
        }
    }
}
