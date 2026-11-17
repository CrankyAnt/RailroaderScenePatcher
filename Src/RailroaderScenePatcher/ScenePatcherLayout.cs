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
    // Inspector mode of the Scene patches window: a command band, the outliner beside the inspector, four tabs and a
    // fixed footer. A control sits where its scope is: a tab's own actions in a pinned row at the bottom of its tab
    // body, the actions on the inspected object that are the same on every tab in the footer below the messages. Every
    // region has a fixed height or scrolls inside its own rectangle, so showing or hiding text or feedback moves no
    // other control.
    public sealed partial class PatcherBehaviour {
        const string ObjectTab = "object", PlacementTab = "placement", ConnectionsTab = "connections", ChangesTab = "changes";
        static readonly string[] TabIds = { ObjectTab, PlacementTab, ConnectionsTab, ChangesTab };
        // Canvas units.
        // The outliner is never narrower than its four filter segments at their natural widths, 292.4 units
        // (CommandBand.FilterSegments), which is also more than its band needs: Refresh and both status lamps with
        // their words, 251.
        // The inspector's minimum holds the four footer slots, their spacing and the resize grip.
        const float OutlinerDefaultWidth = CommandBand.OutlinerWidth, OutlinerMinimumWidth = CommandBand.OutlinerWidth, InspectorMinimumWidth = 350, TabBodyMinimumHeight = 180, TabGap = 8,
            RowHeight = 24, LineHeight = 20, MessageHeight = 40,
            // What a row adds to its name: the folder and child indent, the two markers and the scroll bar.
            RowDecoration = 80;
        // The changed state reads the same in the state line and on the Changes tab: the native Map Editor's badge.
        static readonly string ChangedBadge = CommandBand.Changed.Markup;
        // The left band's widths are CommandBand's, and the outliner's minimum holds them: 296 against the 251 they
        // need.
        // How far a lamp's dot stands above the middle of its row, half of this, so that it sits on its word's line.
        const float LampRaise = 4;
        // The native Map Editor's status colors (MapCompositionStyle and its inspectors), and the proposed-target blue of
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
        float outlinerWidth = OutlinerDefaultWidth;
        int placementMode;
        object revealed;
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
        void Section(UIPanelBuilder builder, string title) { builder.AddHRule(); builder.AddLabel("<b>" + Literal(title) + "</b>"); }
        // A line of the form "Marker id=Mlm1; type=Generic" or "Graph registration: none" as named values. A line of
        // another form stays text.
        void BindingFields(UIPanelBuilder builder, string line) {
            var first = line.IndexOf('=');
            if (first < 0) {
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0) Field(builder, line.Substring(0, colon), Breakable(line.Substring(colon + 2))); else PlainLabel(builder, Breakable(line));
                return;
            }
            // The words before the first key name the group: "Marker", "Track-relative", "Circuit 0".
            var space = line.LastIndexOf(' ', first);
            var group = space < 0 ? "" : line.Substring(0, space);
            string key = null, value = null;
            foreach (var part in line.Substring(space + 1).Split(new[] { "; " }, StringSplitOptions.None)) {
                var equals = part.IndexOf('=');
                // A part without a key continues the value before it.
                if (equals <= 0) { if (key != null) value += "; " + part; continue; }
                if (key != null) Field(builder, (group + " " + key).Trim(), Breakable(CommandBand.Shown(key, value)));
                key = part.Substring(0, equals); value = part.Substring(equals + 1);
            }
            if (key != null) Field(builder, (group + " " + key).Trim(), Breakable(CommandBand.Shown(key, value)));
        }
        // A group of named values in the native inspector's form (DtoInspectorPanel.AddStackedField with AddAxisField):
        // the group's caption above the row, then each short label in front of its input box, the boxes sharing the
        // width. While the group cannot be edited it is one running line of text instead, as tall as the row of
        // boxes, so a group that becomes read-only moves nothing below it. Every group of values in the panel is built
        // here. Returns the text of the running line, for a caller that keeps it current.
        // accessory builds a control at the right end of the caption's row, as the native Move button stands beside
        // the Position caption.
        TMP_Text ValueRow(UIPanelBuilder builder, string caption, string unit, string[] labels, string[] values, int first, string block, string tip, Func<int, bool> numeric, Action<UIPanelBuilder> accessory = null) {
            if (accessory == null) FieldLabel(builder, CommandBand.GroupCaption(caption, unit, block == null));
            else builder.HStack(row => {
                row.AddLabel("<style=FieldLabel>" + Literal(CommandBand.GroupCaption(caption, unit, block == null)) + "</style>", text => text.verticalAlignment = VerticalAlignmentOptions.Bottom);
                row.Spacer();
                accessory(row);
            }).Height(RowHeight);
            if (block != null) {
                var line = Line(builder, Literal(CommandBand.RunningLine(labels, values.Skip(first).Take(labels.Length).ToArray(), unit)), caption, CommandBand.Sentence(block), 30);
                return line.GetComponentInChildren<TMP_Text>(true);
            }
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                for (var column = 0; column < labels.Length; column++) {
                    var index = first + column; var label = labels[column];
                    row.AddLabel("<style=FieldLabel>" + Literal(label) + "</style>", text => text.verticalAlignment = VerticalAlignmentOptions.Middle).MinWidth(0f);
                    var input = row.AddInputField(values[index], value => values[index] = value);
                    // Every keystroke stores the value and lets the buttons that depend on it follow.
                    input.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => { values[index] = value; FieldsChanged(); });
                    if (numeric(column)) AlignNumeric(input);
                    input.Tooltip(caption + " " + label, tip);
                    var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
                    // The boxes share the row's width equally, whatever their content: the box's own preferred
                    // width, which follows its text, gives way to this element.
                    layout.minWidth = 0; layout.preferredWidth = 0; layout.flexibleWidth = 1; layout.layoutPriority = 2;
                }
            });
            return null;
        }
        // The label of an input field and the alignment of a number in it, as the native inspectors build them.
        RectTransform FieldLabel(UIPanelBuilder builder, string label) => builder.AddLabel("<style=FieldLabel>" + Literal(label) + "</style>");
        static void AlignNumeric(RectTransform input) {
            var text = input.GetComponent<TMP_InputField>()?.textComponent;
            if (text != null) text.alignment = TextAlignmentOptions.Right;
        }
        // A heading in the native H3 style on one line, as high as the text itself makes it: 23.2, like a native H3. No
        // height is set. A line with an ellipsis draws nothing at all when its rectangle is lower than its text line by
        // any fraction, which a height of 23.2 set from outside was. The line is one line whatever the text, so its
        // height is the same for every object and nothing below it moves.
        RectTransform Heading(UIPanelBuilder builder, string markup, string title, string full) {
            var line = builder.AddLabel("<style=H3>" + markup + "</style>").TextWrap(TextOverflowModes.Ellipsis, TextWrappingModes.NoWrap);
            if (!String.IsNullOrEmpty(full)) line.Tooltip(title, full);
            return line;
        }
        // One line of fixed height. Text that does not fit ends in an ellipsis and is complete in the tooltip.
        RectTransform Line(UIPanelBuilder builder, string markup, string title, string full, float height = LineHeight) {
            var line = builder.AddLabel(markup).TextWrap(TextOverflowModes.Ellipsis, TextWrappingModes.NoWrap).Height(height);
            if (!String.IsNullOrEmpty(full)) line.Tooltip(title, full);
            return line;
        }
        const string GuideRoot = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/";

        void DrawWindow(UIPanelBuilder builder) {
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
                var outliner = row.VStack(DrawOutliner).gameObject.AddComponent<LayoutElement>();
                outliner.minWidth = width; outliner.preferredWidth = width; outliner.flexibleWidth = 0; outliner.flexibleHeight = 10000;
                presentation.Divider(row, outliner, OutlinerMinimumWidth, OutlinerMaximum, () => outlinerWidth, width => { outlinerWidth = width; StoreOutlinerWidth(width); });
                // The explicit widths keep long inspector text from claiming the width of the outliner.
                var inspector = row.VStack(DrawInspector).gameObject.AddComponent<LayoutElement>();
                inspector.minWidth = InspectorMinimumWidth; inspector.preferredWidth = InspectorMinimumWidth; inspector.flexibleWidth = 10000; inspector.flexibleHeight = 10000;
            }).gameObject.AddComponent<LayoutElement>();
            columns.flexibleWidth = 10000; columns.flexibleHeight = 10000;
        }
        bool firstOpenDone;
        float leftBandWidth = OutlinerDefaultWidth;
        // What the window leaves for the outliner beside the divider, the row spacing and the inspector's minimum.
        float OutlinerMaximum() => Mathf.Max(OutlinerMinimumWidth, presentation.ContentWidth - NativePresentation.DividerWidth - InspectorMinimumWidth - 26);

        // The left band, over the outliner: Refresh and the two status lamps.
        void DrawLeftBand(UIPanelBuilder builder) {
            var stale = inventory == null || inventory.IsStale || definitionsStale;
            var unsaved = Unsaved(out var saveText);
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                Button(row, "Refresh", Inspect).Tooltip("Refresh", "Read the scene and the supplied definitions again. Refresh applies nothing.");
                var lamps = new[] { CommandBand.Inventory(stale), CommandBand.Save(unsaved) };
                var reasons = new[] { InventoryPresentationState.Notice(inventory, definitionsStale), saveText };
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
                            .Width(reserved[index]).Height(RowHeight).Tooltip(lamps[index].Word, tooltip);
                    });
                    // The tooltip sits on the dot and on the word themselves, the objects the pointer rests on.
                    AddLampDot(lamp, lamps[i].Color).Tooltip(lamps[i].Word, tooltip);
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
        void DrawRightBand(UIPanelBuilder builder) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var pending = selection?.Pending != null;
                var select = CommandBand.SelectInWorld(selection != null, pending);
                Button(row, "Select in world", () => BeginSelection(SelectionKind.SceneObject, null)).Inactive(select != null)
                    .Tooltip("Select in world", CommandBand.Tooltip("Click a marking in the world to choose the object to inspect.", select));
                var focus = CommandBand.Focus(selected != null, FocusUnavailable());
                var starter = selected != null && selected.Family == StarterPlacements.Family;
                Button(row, "Focus", () => { if (selected != null) Focus(selected); }).Inactive(focus != null)
                    .Tooltip("Focus", CommandBand.Tooltip(starter ? "Move the camera to the marker's track point, where the start setup places its cars. Focus selects nothing." : "Move the camera to the inspected object. Focus selects nothing.", focus));
                // Beside Focus, as the native Map Editor has Focus and Clear on its selection row.
                var clear = CommandBand.Clear(selected != null, pending);
                Button(row, "Clear", ClearInspection).Inactive(clear != null)
                    .Tooltip("Clear", CommandBand.Tooltip("End the inspection and cancel a pending world choice, gizmo or snap. Changes that were made stay.", clear));
                var copy = CommandBand.CopyId(selected != null, attachment?.Session.CopyIdRefusal);
                Button(row, "Copy ID", CopyIdentity).Inactive(copy != null)
                    .Tooltip("Copy ID", CommandBand.Tooltip("Copy the inspected object's target map, target identity and placement as scene patch file JSON.", copy));
                row.Spacer();
                // The window's "?" belongs in the title bar; it stands here only when the title bar could not take it.
                if (!presentation.TitleHelp) HelpButton(row, NativePresentation.WindowHelpRegion, "Window");
            }).Height(RowHeight);
        }
        // A "?" names its scope in its tooltip and shows "x" while its help is open. It opens its region's help in
        // the help window beside itself, and closes it again.
        void HelpButton(UIPanelBuilder row, string region, string scope) {
            var open = presentation.HelpRegion == region;
            IConfigurableElement button = null;
            button = Button(row, open ? "x" : "?", () => presentation.ToggleHelp(region, button.RectTransform));
            // "?" and "x" change in place: the button has the width of the wider one.
            if (FitButton(button, "?", "x") == 0) button.Width(28);
            button.Tooltip(scope + " help", open ? "Close the " + scope.ToLowerInvariant() + " help." : "Explain the " + scope.ToLowerInvariant() + " in a window beside this button.");
        }
        bool Unsaved(out string text) {
            if (attachment == null) { text = String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : editingStatus; return false; }
            try {
                var outstanding = attachment.Session.Outstanding(attachment.NativeDirty);
                text = outstanding.Count == 0 ? "All required writes are saved." : "Unsaved: " + String.Join(", ", outstanding) + ". Use the Map Editor's Save button.";
                return outstanding.Count != 0;
            } catch (Exception e) { text = "Outstanding work unknown: " + e.GetBaseException().Message; return true; }
        }
        // The copy leaves no trace in the world, so the footer and the log say that it happened and for which object.
        void CopyIdentity() {
            if (selected == null) return;
            try {
                GUIUtility.systemCopyBuffer = Identity(selected);
                feedback.Report("Copy ID", "Identity of " + selected.Identity.Path + " copied to the clipboard.");
            } catch (Exception e) { feedback.Report("Copy ID", "Copy identity failed: " + e.GetBaseException().Message); }
        }

        // A light or gate the patcher added is an added object: the outliner marks it with "+" and the state line with
        // + ADDED. A detection addition adds a circuit to an existing crossing and is not an added object.
        bool IsAdded(SceneObservation entry) => entry?.Family == NativeInventory.AddedEquipmentFamily;
        bool IsChanged(SceneObservation entry) {
            try { return attachment != null && attachment.Session.IsChanged(entry.Identity); } catch { return false; }
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
                segments.AddSegment(CommandBand.AllFilter, outlinerFilter == OutlinerFilter.All, presentation.Action(() => outlinerFilter = OutlinerFilter.All)).Tooltip(CommandBand.AllFilter, "Show every observed object.");
                segments.AddSegment(CommandBand.ChangedFilter, outlinerFilter == OutlinerFilter.Changed, presentation.Action(() => outlinerFilter = OutlinerFilter.Changed)).Tooltip(CommandBand.ChangedFilter, "Show the objects this mod changes.");
                segments.AddSegment(CommandBand.DisabledFilter, outlinerFilter == OutlinerFilter.Disabled, presentation.Action(() => outlinerFilter = OutlinerFilter.Disabled)).Tooltip(CommandBand.DisabledFilter, "Show the objects that are switched off themselves. A disabled object is never listed as a problem.");
                segments.AddSegment(CommandBand.ProblemsFilter, outlinerFilter == OutlinerFilter.Problems, presentation.Action(() => outlinerFilter = OutlinerFilter.Problems)).Tooltip(CommandBand.ProblemsFilter, "Show the objects with a detected problem.");
            });
            results = builder.AddRebuildableSection(items => {
                items.ConfigureLayout(4f);
                var shown = InventoryPresentationState.Rows(inventory, filter, outlinerFilter, IsChanged);
                var summary = inventory == null ? "Inspection is unavailable" : InventoryPresentationState.Summary(inventory, filter, outlinerFilter, IsChanged);
                Line(items, "<style=Footnote>" + Literal(summary) + "</style>", "Objects", summary + ". Inactive loaded objects are included.");
                var rows = new List<UIOutlineItem>();
                var byKey = new Dictionary<string, SceneObservation>();
                string selectedKey = null;
                var narrowed = !String.IsNullOrEmpty(filter) || outlinerFilter != OutlinerFilter.All;
                var state = narrowed ? filteredOutline : inventoryOutline;
                var indices = new Dictionary<SceneObservation, int>();
                if (inventory != null) for (var i = 0; i < inventory.Entries.Count; i++) indices[inventory.Entries[i]] = i;
                foreach (var group in InventoryPresentationState.Groups(shown)) {
                    // The Inspection only group stands after the families and has a key no family can have.
                    var groupKey = (ReferenceEquals(group.Key, InventoryPresentationState.InspectionOnlyGroup) ? "group:" : "family:") + group.Key;
                    // The count stands first, so a narrow column cuts the family name and never the count; the name
                    // follows it directly.
                    var parent = new UIOutlineItem(groupKey, "<alpha=#99>" + group.Value.Count.ToString(CultureInfo.InvariantCulture) + "<alpha=#FF> " + Literal(group.Key));
                    var repeated = new HashSet<string>(group.Value.GroupBy(InventoryPresentationState.Name).Where(names => names.Count() > 1).Select(names => names.Key));
                    // A family whose objects the game itself tells apart shows them under subgroups; a family group
                    // only, never the Inspection only group.
                    var subgroups = groupKey.StartsWith("family:", StringComparison.Ordinal) ? InventoryPresentationState.Subgroups(group.Value) : null;
                    var folders = new Dictionary<SceneObservation, UIOutlineItem>(); var folderKeys = new Dictionary<SceneObservation, string>();
                    if (subgroups != null) foreach (var subgroup in subgroups) {
                        var subKey = groupKey + "/" + subgroup.Key;
                        var folder = new UIOutlineItem(subKey, "<alpha=#99>" + subgroup.Value.Count.ToString(CultureInfo.InvariantCulture) + "<alpha=#FF> " + Literal(subgroup.Key));
                        parent.AddChild(folder);
                        foreach (var row in subgroup.Value) { folders[row] = folder; folderKeys[row] = subKey; }
                    }
                    foreach (var item in group.Value) {
                        var holder = folders.TryGetValue(item, out var inFolder) ? inFolder : parent;
                        var key = indices[item].ToString(CultureInfo.InvariantCulture);
                        byKey[key] = item;
                        if (ReferenceEquals(selected, item)) {
                            selectedKey = key;
                            // A newly inspected object is revealed once; afterwards its folder stays as the user leaves it.
                            if (!ReferenceEquals(revealed, item.Handle)) { revealed = item.Handle; state.SetExpanded(groupKey, true); if (folderKeys.TryGetValue(item, out var folderKey)) state.SetExpanded(folderKey, true); }
                        }
                        var name = InventoryPresentationState.Name(item);
                        var changed = IsChanged(item); var added = IsAdded(item);
                        // The native outline appends " *" and " +" to a name. Here the markers stand before the name, so
                        // that they stay visible when the end of a long name is cut off; they take room only on a row
                        // that has them. "*" marks a changed object and never unsaved work. A name that repeats in its
                        // folder carries its parent, dimmed; an inactive object is italic and says so.
                        var markers = CommandBand.RowMarkers(item.Problems.Count != 0, changed, added);
                        var segments = item.Identity.Path.Split('/');
                        var context = repeated.Contains(name) && segments.Length > 1 ? "  <alpha=#99>" + Literal(segments[segments.Length - 2]) + "<alpha=#FF>" : "";
                        // A row of the Inspection only group names its family, which its folder does not.
                        if (item.InspectionOnly != null) context += "  <alpha=#99>" + Literal(item.Family) + "<alpha=#FF>";
                        holder.AddChild(new UIOutlineItem(key, markers.Replace("!", "<color=" + Red + "><b>!</b></color>").Replace("*", "<color=" + Amber + "><b>*</b></color>").Replace("+", "<color=" + Green + "><b>+</b></color>") + (markers.Length == 0 ? "" : " ") +
                            (item.Active ? Literal(name) : "<i>" + Literal(name) + "</i>") + context + (item.Active ? "" : "  <alpha=#99>inactive<alpha=#FF>"), item) {
                            Tooltip = (name + "\n" + item.Identity.Path + "\n" + item.Family + (item.Active ? "" : "\nInactive") + (item.InspectionOnly == null ? "" : "\n" + CommandBand.InspectionOnlyLine(item.InspectionOnly)) + (changed ? "\nChanged by this mod" : "") + (added ? "\nAdded by this mod" : "") +
                                (item.Problems.Count == 0 ? "" : "\n" + String.Join("\n", item.Problems))).Replace("<", "\\u003c").Replace(">", "\\u003e")
                        });
                    }
                    rows.Add(parent);
                }
                var outline = items.AddOutlineView(rows, state, new UIState<string>(selectedKey), key => {
                    if (!byKey.TryGetValue(key, out var observation)) return;
                    presentation.Action(() => {
                        // A click on the row that is inspected already clears the inspection; so does Escape over the panel.
                        if (CommandBand.ClearsInspection(selected, observation)) { ClearInspection(); return; }
                        if (!inventoryPresentation.CanSelect(inventory, observation)) return;
                        CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; CloseEquipmentForm(); selected = inventoryPresentation.CurrentSelection(inventory, observation); ReadPlacement();
                    })();
                });
                var listLayout = outline.GetComponent<LayoutElement>();
                listLayout.minHeight = 120; listLayout.flexibleHeight = 10000;
            });
            // Three anchors for the facts a modder needs before the first edit. They sit at the bottom edge of the window,
            // so their tooltips are one line; the window help holds the full explanation.
            builder.AddHRule();
            builder.Spacer(4f);
            Line(builder, "<style=Footnote>Save, Undo, Redo: Map Editor</style>", "Save, Undo and Redo", "Use the Map Editor's own Save, Undo and Redo.");
            Line(builder, "<style=Footnote>Refresh after native edits</style>", "Refresh", "Refresh after Undo, Redo or a native edit.");
            Line(builder, "<style=Footnote>Reload clears Undo history</style>", "Map Editor reload", "Reloading ends the Undo history.");
            builder.Spacer(4f);
        }
        void FitOutliner(TMP_Text measure) {
            if (measure == null || inventory == null) return;
            var widths = InventoryPresentationState.Rows(inventory, filter, outlinerFilter, IsChanged).Select(row => measure.GetPreferredValues(InventoryPresentationState.Name(row)).x).ToArray();
            var divider = NativePresentation.DividerWidth + 26;
            outlinerWidth = InventoryPresentationState.FitWidth(widths, RowDecoration, presentation.ContentWidth, OutlinerMinimumWidth, InspectorMinimumWidth, divider);
            StoreOutlinerWidth(outlinerWidth);
            // The same fit without a window limit tells whether the cap stopped it.
            var wanted = InventoryPresentationState.FitWidth(widths, RowDecoration, 100000, OutlinerMinimumWidth, InspectorMinimumWidth, divider);
            if (wanted > outlinerWidth + 0.5f) feedback.Report("Fit", "Most names need " + Mathf.RoundToInt(wanted) + "; the column stops at " + Mathf.RoundToInt(outlinerWidth) + ", 40 percent of the window. Widen the window for more.");
        }

        void DrawInspector(UIPanelBuilder builder) {
            builder.ConfigureLayout(4f);
            DrawRightBand(builder);
            var name = selected == null ? "No Selection" : InventoryPresentationState.Name(selected);
            // The name takes the whole row and is cut only when the row itself is too short; its tooltip holds the
            // full path. The right end of this row is where the Inspector and Guided switch will stand: no control
            // stands there, and the name gives way to the switch when it exists.
            builder.HStack(row => {
                Heading(row, Literal(name), "Object", selected == null ? null : selected.Identity.Path).FlexibleWidth();
            }).ChildrenKeepHeight();
            var stateLine = Line(builder, StateLine(out var states), "State", states);
            builder.AddTabbedPanels(inspectorTab, tabs => {
                tabs.AddTab("Object", ObjectTab, body => Tab(body, ObjectTab, DrawObject));
                tabs.AddTab("Placement", PlacementTab, body => Tab(body, PlacementTab, DrawPlacement));
                tabs.AddTab("Connections", ConnectionsTab, body => Tab(body, ConnectionsTab, DrawConnections));
                tabs.AddTab("Changes", ChangesTab, body => Tab(body, ChangesTab, DrawChanges));
            });
            // The native tab strip gives each tab the share of its label's width. In a narrow strip a title ends in an
            // ellipsis inside 8 units of padding; the four titles are never renamed.
            var tabView = stateLine.parent == null ? null : stateLine.parent.GetComponentInChildren<UI.TabView.TabView>(true);
            if (tabView != null) foreach (var toggle in tabView.GetComponentsInChildren<Toggle>(true)) {
                var title = toggle.GetComponentInChildren<TMP_Text>(true);
                if (title == null) continue;
                title.overflowMode = TextOverflowModes.Ellipsis; title.textWrappingMode = TextWrappingModes.NoWrap;
                title.margin = new Vector4(8, title.margin.y, 8, title.margin.w);
            }
            DrawFooter(builder);
        }
        // One line under the name: the family and the object's states, in the native status colors.
        string StateLine(out string plain) {
            if (selected == null) { plain = null; return "Select a row in the outliner, or use Select in world."; }
            var parts = new List<string> { Literal(selected.Family) };
            var text = new List<string> { selected.Family };
            if (!selected.Active) { parts.Add("Inactive"); text.Add("Inactive"); }
            // The native Map Editor's badges, as its inspector shows them beside the row marker.
            foreach (var badge in CommandBand.Badges(IsChanged(selected), IsAdded(selected))) {
                parts.Add(badge.Markup); text.Add(ReferenceEquals(badge, CommandBand.Changed) ? "Changed by this mod" : "Added by this mod");
            }
            if (selected.InspectionOnly != null) { parts.Add("Inspection only"); text.Add(CommandBand.InspectionOnlyLine(selected.InspectionOnly)); }
            if (selected.Problems.Count != 0) {
                var count = selected.Problems.Count == 1 ? "1 problem" : selected.Problems.Count + " problems";
                parts.Add("<color=" + Red + ">" + count + "</color>"); text.Add(count);
            }
            plain = String.Join("; ", text) + "\n" + selected.Identity.Path;
            return String.Join("   ", parts);
        }
        // A tab body scrolls inside its own rectangle. Its first row names what the tab is for and holds the tab's own
        // "?", so that help visibly belongs to the tab; the help opens in its own window. Below the scrolling part
        // the tab's own actions stand in a pinned row of fixed height, inside the tab, so that their place says they
        // belong to the tab; a gap sets the row apart from the footer, whose row is the same on every tab. The
        // native map editor puts a tool's actions directly under its fields; here they stay in view while the fields scroll.
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
            if (fill != null) { fill.preferredHeight = TabBodyMinimumHeight; fill.flexibleHeight = 10000; }
            ActionRow(builder, TabActs(id), ChoiceBlock);
            builder.Spacer(TabGap);
        }

        // The Object tab: what the object is, whether it is enabled, and what was detected about it.
        void DrawObject(UIPanelBuilder builder) {
            // An object of a kind no supported family covers says so first, with the reason it is not yet supported.
            if (selected.InspectionOnly != null) {
                Section(builder, "Inspection only");
                PlainLabel(builder, selected.InspectionOnly);
            }
            // Problems first: the red count in the state line points at text that is in view.
            Section(builder, "Detected problems");
            if (selected.Problems.Count == 0) Line(builder, "None detected", null, null);
            foreach (var problem in selected.Problems) builder.AddLabel(Tint(Red, problem));
            // What the inspection could not read or check: nothing the modder can repair here, so no problem.
            if (selected.Notes.Count != 0) {
                Section(builder, "Inspection notes");
                foreach (var note in selected.Notes) PlainLabel(builder, Breakable(note));
            }
            // A group of the Baked scenery group family is decorative: the game reads nothing from where it stands.
            if (selected.InspectionOnly == null && selected.Family == BakedSceneryRules.Family) {
                Section(builder, "Use in the game");
                PlainLabel(builder, CommandBand.Decorative);
            }
            Section(builder, "State");
            var target = selected.Handle as GameObject;
            // A group root's own flag can be off while only its simple culler hides it; the observation reads the authored flag.
            var own = target != null && (selected.Family == BakedSceneryRules.Family ? selected.Enabled : target.activeSelf);
            Field(builder, "Enabled", own ? (selected.Active ? "Yes" : "Yes; a parent is inactive") : "No")
                .Tooltip("Enabled", "Whether this object is active in the scene.");
            if (FollowsGroup) Field(builder, "Track group", CommandBand.TrackGroup(Editable));
            FieldLabel(builder, "Supported edits");
            PlainLabel(builder, selected.Operations);
            // A starter placement marker names every setup and car placement that reads it, with the cars and the wreck
            // setting of each.
            if (selected.Family == StarterPlacements.Family) {
                Section(builder, "Read by");
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
            Section(builder, "Dependencies");
            if (selected.Dependencies.Count == 0) Line(builder, "None observed", null, null);
            foreach (var line in selected.Dependencies) PlainLabel(builder, Breakable(line));
            Section(builder, "Runtime state");
            if (selected.RuntimeState.Count == 0) Line(builder, "None observed", null, null);
            foreach (var line in selected.RuntimeState) PlainLabel(builder, Breakable(line));
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
        bool FollowsGroup => selected != null && selected.Bindings.Any(b => b.IndexOf("follows track group", StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf("targetActiveFollowsTrackGroup=True", StringComparison.OrdinalIgnoreCase) >= 0);

        // The Placement tab: every way to put the object somewhere. The tab's pinned row holds the actions of the chosen way.
        void DrawPlacement(UIPanelBuilder builder) {
            // A crossing pad has no placement of its own, and an object that cannot be edited has none to set: the tab
            // shows the placement, and no other mode is open.
            if (IsPad || !Editable) placementMode = CommandBand.NumericMode;
            // A new-game car placement puts its cars at the marker's track point, which the world marks in gold; the
            // object position below plays no part in it.
            if (selected.Family == StarterPlacements.Family) {
                var point = InspectedTrackPoint();
                Field(builder, "Track point", point == null ? "None: " + (FocusUnavailable() ?? StarterPlacements.NoTrackPoint) : String.Join(", ", point.Select(CommandBand.Number)))
                    .Tooltip("Track point", "Where the start setup places the front of this marker's car cut, in map coordinates. Focus goes here.");
            }
            var tips = new[] { "Enter a position in map coordinates and a rotation in degrees.", "Drag the native Move or Rotate gizmo in the world.",
                "Move the object once onto a chosen track or endpoint.", "Keep the object's placement relative to a chosen track or endpoint." };
            builder.AddSegmentedControlCompact(segments => {
                for (var i = 0; i < PlacementModes.Length; i++) {
                    var mode = i;
                    // A mode that cannot be used on the inspected object is inactive and says why.
                    // A starter placement marker's object does not change where its cars start: every mode but Numeric,
                    // which shows the object's placement, says so.
                    var padMode = CommandBand.ModeBlock(i, IsPad, Editable, IsStarter ? CommandBand.NotEditable(StarterPlacements.ObjectSettings) : selected?.Operations, selected?.Identity.Structure == MilepostScene.Structure);
                    segments.AddSegment(PlacementModes[i], placementMode == i, presentation.Action(() => { if (padMode == null) placementMode = mode; })).Inactive(padMode != null).Tooltip(PlacementModes[i], CommandBand.Tooltip(tips[i], padMode));
                }
            }, true);
            var block = EditBlock() ?? PadBlock;
            if (placementMode == 0) {
                for (var group = 0; group < 2; group++) {
                    if (group != 0) builder.Spacer(6f);
                    ValueRow(builder, CommandBand.PlacementGroups[group], CommandBand.PlacementUnits[group], CommandBand.Axes, placementFields, group * 3, block, group == 0 ? "Map coordinates, in meters" : "Euler angles, degrees", column => true);
                }
            } else if (placementMode == 1) {
                // The tool is a choice, as in the native map editor: Move by default. The gizmo stands on the object as
                // soon as this mode is open and stays until the mode, the tab or the inspection ends; choosing the
                // other tool changes the gizmo at once. Each completed drag is one operation.
                builder.AddSegmentedControlCompact(tools => {
                    foreach (var tool in new[] { UI.Gizmos.TransformGizmoMode.Move, UI.Gizmos.TransformGizmoMode.Rotate }) {
                        var chosen = tool; var name = tool == UI.Gizmos.TransformGizmoMode.Move ? "Move" : "Rotate";
                        tools.AddSegment(name, gizmoTool == tool, presentation.Action(() => {
                            if (selection?.Pending != null) return;
                            gizmoTool = chosen;
                            gizmo?.SetMode(chosen);
                        })).Inactive(ChoiceBlock != null).Tooltip(name, CommandBand.Tooltip(tool == UI.Gizmos.TransformGizmoMode.Move ? "Drag the native Move gizmo in the world." : "Drag the native Rotate gizmo in the world.", ChoiceBlock));
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
                    : "Next: Track or Node, then click in the world, Confirm and Apply.") + "</style>", null, null);
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
                    : "Next: Track or Node, then click in the world, Confirm and Start.") + "</style>", null, null);
            }
        }
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
            // The open form stands first, directly under the replacement targets it takes its segments from, so its
            // three roles are in reach without scrolling past the list of circuits.
            if (coverageCircuit != null) {
                Section(builder, coverageAddition == null ? "Circuit " + coverageCircuit : "Detection addition " + coverageAddition);
                var confirmed = selection?.Confirmed?.Identity;
                var picked = confirmed != null && confirmed.Kind == SelectionKind.TrackSegment;
                var labels = new[] { "Segment", "End", "From", "To" };
                for (var role = 0; role < 3; role++) {
                    var first = role * 4; var roleName = CommandBand.RoleCaption(CircuitCoverage.Roles[role]);
                    // The role's Picked button stands in its caption row, so a role takes two rows and all three fit the tab.
                    ValueRow(builder, roleName, "m", labels, coverageFields, first, block, "End is a or b. Distances are metres from the chosen end. From is the end next to the island for an approach.", column => column >= 2, row =>
                        Button(row, "Picked", () => { coverageFields[first] = confirmed.Identifier; preparedCoverage = null; }).Inactive(!picked || wait != null)
                            .Tooltip("Picked segment for " + roleName, CommandBand.Tooltip("Fill this role's segment with the confirmed segment.", wait ?? (picked ? null : "Choose a segment under Replacement targets and confirm it first."))));
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
                    // A native circuit is named by its island span, a detection addition by its ID.
                    var name = DetectionAddition.Id(key) == null ? "Circuit " + key : "Detection addition " + DetectionAddition.Id(key);
                    Field(builder, name, value.ToString());
                    Button(builder, "Edit " + name, () => { OpenedForm(null); CloseEquipmentForm(); coverageCircuit = key; coverageAddition = DetectionAddition.Id(key); preparedCoverage = null; ReadCoverage(value); }).Inactive(wait != null)
                        .Tooltip("Edit " + name, CommandBand.Tooltip(DetectionAddition.Id(key) == null ? "Select the tracks this circuit covers, or detach it. Circuits you do not edit keep their coverage." : "Change the tracks of this detection addition, or delete it.", wait));
                }
                if (current.Residue.Count > 0) builder.AddLabel(Tint(Amber, "Owned spans left by an incomplete cleanup: " + String.Join(", ", current.Residue) + ". Use Recheck in the footer."));
                // An addition starts from an ID the modder chooses and tracks the modder selects; nothing is taken from
                // nearby track.
                Section(builder, "Add detection");
                FieldLabel(builder, "Addition ID");
                var circuits = current;
                // The ID field and its button share a row, so the button's short label reads with the field beside it.
                builder.HStack(row => {
                    if (block != null) Line(row, Literal(newAddition ?? ""), "Addition ID", block, 30).FlexibleWidth();
                    else {
                        var idInput = row.AddInputField(newAddition ?? "", value => newAddition = value);
                        idInput.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => newAddition = value);
                        idInput.FlexibleWidth();
                    }
                    Button(row, "Add detection", () => {
                        var id = (newAddition ?? "").Trim();
                        if (id.Length == 0) { feedback.Refuse(CoveragePlace, "Detection addition", "give the addition an ID first"); return; }
                        var existing = circuits.Circuits.FirstOrDefault(c => c.Value.Kind == CircuitKind.Added && DetectionAddition.Id(c.Key) == id);
                        CloseEquipmentForm(); coverageAddition = id; coverageCircuit = existing.Key ?? DetectionAddition.Key("", id); preparedCoverage = null; ReadCoverage(existing.Value);
                        OpenedForm("Detection addition " + id + " is open at the top of this tab: select its tracks, then Preview.");
                    }).Inactive(wait != null).Tooltip("Add detection with this ID", CommandBand.Tooltip("Make the crossing detect an extra track: give the addition an ID that is unique in this mod's scene patch file, then select its tracks.", wait));
                });
            }
        }
        // Lights and gates added to the crossing: copies of eligible devices already loaded in the crossing's scene, which the
        // crossing controller drives. The open form stands first; the tab's pinned row holds Preview, Remove and Apply for it.
        // Every device scene patch editing considers is listed: an eligible one with its Use button, a rejected one with its reason.
        // The open light or gate form stands first in the Connections tab, above the replacement targets it does not use,
        // so the scrolling tab body shows its ID and position at its least height; its source is one line with the full
        // path in the tooltip.
        void DrawEquipmentForm(UIPanelBuilder builder) {
            var block = EditBlock();
            {
                Section(builder, "Light or gate " + (equipmentFields[0] ?? ""));
                Line(builder, Literal("Source " + equipmentSource), "Source", equipmentSource.ToString());
                FieldLabel(builder, "Equipment ID");
                if (block != null) Line(builder, Literal(equipmentFields[0] ?? ""), "Equipment ID", block, 30);
                else {
                    var idInput = builder.AddInputField(equipmentFields[0] ?? "", value => equipmentFields[0] = value);
                    idInput.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => { equipmentFields[0] = value; preparedEquipment = null; FieldsChanged(); });
                    idInput.Tooltip("Equipment ID", "The persistent ID of this light or gate in this mod's scene patch file: letters, digits, '.', '_' or '-'.");
                }
                ValueRow(builder, "Position", "m", new[] { "X", "Y", "Z" }, equipmentFields, 1, block, "The copy's position in map coordinates.", column => true);
                ValueRow(builder, "Rotation", "°", new[] { "X", "Y", "Z" }, equipmentFields, 4, block, "The copy's rotation in degrees.", column => true);
                Section(builder, "Preview");
                if (preparedEquipment == null) Line(builder, "None", null, null);
                else if (preparedEquipment.Preview.Error != null) builder.AddLabel(Tint(Red, "Preview refused: " + CommandBand.Sentence(preparedEquipment.Preview.Error)));
                else foreach (var line in preparedEquipment.Preview.Lines) PlainLabel(builder, Breakable(line));
            }
        }
        // The crossing's lights and gates and the devices offered as sources, below its coverage.
        void DrawEquipment(UIPanelBuilder builder) {
            var identity = selected.Identity;
            var block = EditBlock();
            var wait = ChoiceBlock ?? block;
            Section(builder, "Lights and gates");
            CrossingEquipment current = null;
            IReadOnlyList<EquipmentEntry> mine = Array.Empty<EquipmentEntry>();
            if (attachment != null) try { current = attachment.Session.Equipment(identity); mine = attachment.Session.EquipmentEntries(identity); }
                catch (Exception e) { builder.AddLabel(Tint(Red, "Lights and gates unavailable: " + CommandBand.Sentence(e.GetBaseException().Message))); }
            if (current != null) {
                var others = current.Additions.Where(a => a.Scope != attachment.Session.ScenePatchFileKey).ToArray();
                if (mine.Count == 0 && others.Length == 0) Line(builder, "None added", null, null);
                // This mod's additions come from its scene patch file, so one whose build failed is listed with its reason and can
                // be edited or removed.
                foreach (var entry in mine) {
                    var item = entry.Built ?? entry.Addition;
                    string state = null;
                    if (entry.Built != null) try { state = attachment.Session.EquipmentStatus(identity, item.Id); } catch (Exception e) { state = "state unknown: " + e.GetBaseException().Message; }
                    if (entry.Problem != null) FieldMarkup(builder, "Light or gate " + item.Id, Literal(Breakable(item.ToString()) + ".") + " " + Tint(Red, "Not built: " + CommandBand.Sentence(entry.Problem)));
                    else Field(builder, "Light or gate " + item.Id, Breakable(item.ToString()) + (state == null ? "" : ". " + CommandBand.Sentence(state)));
                    var scenePatchFile = entry.Addition;
                    Button(builder, "Edit " + item.Id, () => ReadEquipment(scenePatchFile)).Inactive(wait != null)
                        .Tooltip("Edit light or gate " + item.Id, CommandBand.Tooltip(entry.Problem == null ? "Change this copy's placement, or remove it." : "Correct this light or gate, or remove it.", wait));
                }
                foreach (var item in others)
                    Field(builder, "Light or gate " + item.Id, Breakable(item.ToString()) + " (another mod's addition; read only)");
            }
            Section(builder, "Add a light or gate");
            IReadOnlyList<SourceCandidate> candidates = null;
            if (attachment != null) try { candidates = attachment.Session.EquipmentSources(identity); }
                catch (Exception e) { builder.AddLabel(Tint(Red, "Sources unavailable: " + CommandBand.Sentence(e.GetBaseException().Message))); }
            if (candidates == null) return;
            Line(builder, candidates.Count(c => c.Eligible) + " eligible and " + candidates.Count(c => !c.Eligible) + " rejected devices in the loaded map", null, null);
            foreach (var candidate in candidates) {
                var source = candidate;
                if (source.Eligible) Button(builder, "Use " + source.Label, () => UseSource(attachment.Session, source)).Inactive(wait != null)
                    .Tooltip("Use " + source.Source.Path, CommandBand.Tooltip("Copy this device to this crossing. Its lamps and gates work through the crossing controller's lights and gates keys.", wait));
                else Field(builder, "Rejected " + source.Label, Breakable(source.Source.Path) + ": " + CommandBand.Sentence(source.Problem));
            }
            if (candidates.Any(c => !c.Eligible))
                Button(builder, "Why a device is rejected", () => Application.OpenURL(GuideRoot + "adding-crossing-equipment.md#rejected-sources")).Tooltip("Why a device is rejected", "Open the rejection reasons in the guide in the browser.");
        }
        // Why the inspected object has no editable track binding: its own detected reason when it holds a marker the
        // adapter refuses, or that the operation is for markers, mileposts and pads. It is one plain sentence
        // (CommandBand.Sentence): the tooltip's Unavailable: and the section it stands in name what is unavailable.
        string BindingUnavailable() {
            // A marker or pad the adapter refuses says why under Supported edits; so does a starter placement marker.
            var family = selected?.Family;
            if ((family == StarterPlacements.Family || family == "Track marker" || family == "Crossing pad marker" || family == "Milepost") && CommandBand.Refusal(selected.Operations) is string reason)
                return CommandBand.Sentence(reason);
            // Another object that holds a marker: ask the marker adapter why it refuses this object.
            try {
                if (selected?.Handle is GameObject target && target != null && target.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == TrackMarkerWorld.MarkerType)) {
                    var refusal = TrackMarkerWorld.Problem(target, NativeInventory.LastReferrers, null);
                    if (refusal != null) return CommandBand.Sentence(refusal);
                }
            } catch { }
            return "Only Generic track markers, mileposts and crossing pads have editable track bindings.";
        }
        void CloseCoverageForm() { OpenedForm(null); coverageCircuit = coverageAddition = null; preparedCoverage = null; newAddition = ""; for (var i = 0; i < coverageFields.Length; i++) coverageFields[i] = ""; }
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
            var coverage = selected.Identity.Structure == CrossingCoverageScene.Structure;
            // A crossing pad has the binding of its marker: setting it regenerates the pad along the track there.
            var pad = selected.Identity.Structure == CrossingPadScene.Structure;
            var marker = pad || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == MilepostScene.Structure;
            var block = marker ? EditBlock() : BindingUnavailable();
            if (coverage && coverageCircuit == null && equipmentSource != null) DrawEquipmentForm(builder);
            Section(builder, "Replacement targets");
            // A starter placement marker gives its own reason: its track location belongs to the starter placement workflow.
            var busy = selected.Family == StarterPlacements.Family ? CommandBand.EditReason(selected.Operations)
                : !marker && !coverage ? "Replacement targets serve a track binding or a crossing's coverage." : selection == null ? "World selection is unavailable." : ChoiceBlock;
            builder.HStack(row => {
                Button(row, "Object", () => BeginSelection(SelectionKind.SceneObject, selected)).Inactive(busy != null).Tooltip("Replacement object", CommandBand.Tooltip("Choose a replacement object in the world and confirm its identity.", busy));
                Button(row, "Segment", () => BeginSelection(SelectionKind.TrackSegment, selected)).Inactive(busy != null).Tooltip("Replacement segment", CommandBand.Tooltip("Choose a track segment in the world and confirm its identity.", busy));
                Button(row, "Node", () => BeginSelection(SelectionKind.TrackNode, selected)).Inactive(busy != null).Tooltip("Replacement node", CommandBand.Tooltip("Choose a track node in the world and confirm its identity.", busy));
            }).Height(RowHeight);
            if (coverage) { DrawCoverage(builder); DrawEquipment(builder); }
            Section(builder, pad ? "Crossing pad binding" : "Track binding");
            ValueRow(builder, "Track location", "m", new[] { "Segment", "End", "Distance" }, binding, 0, block, (pad ? "The track location this pad's marker is registered at. Setting it regenerates the pad along the track there; the pad object does not move." : "The track location this marker is registered at. A binding edit does not move the object.") + " End is a or b.", column => column == 2);
            var current = selected.Bindings.Where(b => b.StartsWith("Marker", StringComparison.Ordinal) || b.StartsWith("Graph registration", StringComparison.Ordinal)).ToArray();
            FieldLabel(builder, "Current binding");
            if (current.Length == 0) Line(builder, "None", null, null);
            foreach (var line in current) BindingFields(builder, line);
            Section(builder, "Observed bindings");
            var others = selected.Bindings.Where(line => !current.Contains(line) && !IsReader(line)).ToArray();
            if (others.Length == 0) Line(builder, "None observed", null, null);
            foreach (var line in others) BindingFields(builder, line);
        }

        // The Connections tab of a starter placement marker: on-track placement of its track location, which the start setup
        // reads as the front of the car cut. Segment chooses the track in the world, and the click on it gives the
        // distance; the fields can also be typed. The current location follows, with the setups that read it.
        void DrawStarterLocation(UIPanelBuilder builder) {
            var block = EditBlock();
            Section(builder, "Place on the track");
            var busy = block ?? (selection == null ? "World selection is unavailable." : ChoiceBlock);
            builder.HStack(row => {
                Button(row, "Segment", () => BeginSelection(SelectionKind.TrackSegment, selected)).Inactive(busy != null)
                    .Tooltip("Place on a segment", CommandBand.Tooltip("Click the track where the front of the car cut belongs and confirm the segment. The fields take the segment and the distance of the click from end a.", busy));
            }).Height(RowHeight);
            Section(builder, "Track location");
            ValueRow(builder, "Track location", "m", new[] { "Segment", "End", "Distance" }, binding, 0, block,
                "Where the start setup places the front of this marker's car cut; the cut extends from there towards the end the distance counts from. Set checks the whole cut with the setup's other cuts by the game's placement rule. End is a or b.", column => column == 2);
            var current = selected.Bindings.Where(b => b.StartsWith("Marker", StringComparison.Ordinal) || b.StartsWith("Graph registration", StringComparison.Ordinal)).ToArray();
            FieldLabel(builder, "Current track location");
            if (current.Length == 0) Line(builder, "None", null, null);
            foreach (var line in current) BindingFields(builder, line);
            Section(builder, "Read by");
            var readers = selected.Bindings.Where(IsReader).ToArray();
            if (readers.Length == 0) Line(builder, "No active setup", null, null);
            foreach (var line in readers) PlainLabel(builder, line.Substring("Read by ".Length));
        }
        // The Changes tab: what this mod changes about the inspected object, and what that change still awaits.
        void DrawChanges(UIPanelBuilder builder) {
            Section(builder, "This mod");
            var changed = IsChanged(selected);
            FieldMarkup(builder, "Object", changed ? ChangedBadge : "Unchanged")
                .Tooltip("Changed", "Whether Data/scene-patches.json of the edited mod holds a modification of this object.");
            // One short line per changed setting, with the value the object has without this modification.
            if (changed && attachment != null) {
                IReadOnlyList<string> lines = null;
                try { lines = attachment.Session.Changes(selected.Identity); } catch { }
                if (lines != null) foreach (var line in lines) PlainLabel(builder, Breakable(line));
            }
            // Terrain output exists only for an object with a terrain mask, an independent board: the section is
            // there only for those, in plain words.
            if (!HasTerrain) return;
            Section(builder, "Terrain");
            var problem = (string)null;
            var output = attachment == null || !Editable ? DependentOutput.None : inspectedOutput.Of(attachment.Session, selected, out problem);
            var text = CommandBand.Terrain(problem, output.Error, output.Settled, Join(output.Completed), Join(output.Outstanding), output.Completed.Count != 0);
            if (problem != null) builder.AddLabel(Tint(Red, text)); else PlainLabel(builder, text);
        }
        // Whether the inspected object has terrain output at all.
        bool HasTerrain => selected != null && selected.Identity.Structure == IndependentBoards.Structure;

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
        void DrawFooter(UIPanelBuilder builder) {
            builder.AddHRule();
            // The first slot says what the window is waiting for or why the open tab cannot edit now.
            var pending = selection?.Pending;
            string source = null, state = "";
            if (pending == null) candidateDetails = false;
            if (Requests.Place != null) { source = Requests.Place; state = "Working..."; }
            else if (pending != null) {
                var candidate = selection.Find(pending.Candidate);
                source = "World choice";
                // A short purpose with the count on the first line, the candidate on its own line.
                var purpose = choosingSnap ? "Pick the snap target" : pending.Kind == SelectionKind.SceneObject ? "Pick an object" : pending.Kind == SelectionKind.TrackSegment ? "Pick a track" : "Pick an endpoint";
                // The choice takes the world's clicks: the native map editor does not select or drag until it ends.
                state = (pending.Offers.Count > 1 ? Tint(Blue, pending.Offers.Count + " candidates") + " " : "") + Literal(purpose + " in the world. Native world clicks are suspended; Cancel restores them.") +
                    (pending.Problem == null ? "" : " " + (PendingSelection.IsInstruction(pending.Problem) ? Literal(pending.Problem) : Tint(Red, pending.Problem))) + (inputBlocker.WorldInputProblem == null ? "" : " " + Tint(Red, inputBlocker.WorldInputProblem)) +
                    (selectionProblem == null ? "" : " " + Tint(Red, selectionProblem)) +
                    (candidate == null ? "" : "\n" + Tint(Blue, CommandBand.ChoiceTag("Candidate", pending.Number(candidate.Choice), pending.Offers.Count) + ": " + CommandBand.ChoiceLabel(candidate.Choice.Label, pending.Offers.Select(o => o.Label), 60)) + Literal(choosingSnap ? " Confirm takes it; Apply then moves the object." : " Confirm takes it.")) +
                    // The footer's Details: the clicked candidate's identity and details, here in the message area.
                    (candidate == null || !candidateDetails ? "" : "\n" + Literal(candidate.Choice.Identity.ToString()) + (String.IsNullOrEmpty(candidate.Details) ? "" : "\n" + Literal(candidate.Details)));
            } else if (gizmo?.Active == true) { source = GizmoPlace; state = Literal(gizmo.State); }
            else if (selectionProblem != null) { source = "World choice"; state = Tint(Red, selectionProblem); }
            else if (preparedSnap != null) { source = SnapPlace; state = Tint(Blue, "Snap target: " + preparedSnap.Identity) + Literal(" Apply moves the object."); }
            // A confirmed target belongs to the object it was chosen for; another object does not show it.
            else if (selected != null && selection?.Confirmed is ConfirmedSelection confirmed && confirmed.Source != null && confirmed.Source.Equals(SelectionIdentity.SceneObject(selected.Identity))) {
                source = "World choice"; state = Tint(Green, "Confirmed: " + confirmed.Identity + " in " + confirmed.MapIds[0]);
            } else if (selected != null && EditBlock() is string block) { source = "Editing"; state = Tint(Amber, block); }
            // The area has a fixed height, so nothing outside it moves, and its entries stack from the top without gaps:
            // first what the window waits for now, then the messages, newest first, the older ones dimmed. Each names
            // the action group it came from. The area is a native scroll view: its content takes the height of its
            // entries, so an entry that wraps has its wrapped height, and its viewport cuts off what does not fit. A
            // stack of fixed height would instead squeeze its entries below their text, and the text would overprint.
            // What lies below the area's edge is reached by scrolling.
            var messages = feedback.Messages;
            var messageArea = presentation.Scroll(builder, MessageHeight, lines => {
                if (!String.IsNullOrEmpty(state)) MessageSlot(lines, source, state, false);
                for (var i = 0; i < messages.Count; i++) {
                    var message = messages[i]; var older = i > 0;
                    var tone = ToneOf(message.Text);
                    var text = WithoutSource(message);
                    MessageSlot(lines, message.Source, PathBreaks(tone == null ? Literal(text) : Tint(tone + (older ? "80" : ""), text)), older);
                }
            }, false);
            // The area is three messages high and gives way before the tab body does: in a low window it keeps one
            // message in view and scrolls, so the body keeps the height a form needs.
            var area = messageArea.GetComponent<LayoutElement>();
            area.minHeight = MessageHeight; area.preferredHeight = 3 * MessageHeight; area.flexibleHeight = 0;
            ActionRow(builder, FooterActs(), null);
            // The native resize grip takes the window's lower right corner, 22 units high, and the row would reach 12
            // units into its height: the row stands that much higher, so the footer and the grip do not overlap.
            builder.Spacer(CommandBand.GripClearance(GripHeight, RowPadding));
        }
        // The native resize grip's height, and the window's padding below its last row, in canvas units.
        const float GripHeight = 22, RowPadding = 10;
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
        // disables every button of the row with one reason. A disabled button's tooltip says what the control does
        // first, then why it cannot be used.
        void ActionRow(UIPanelBuilder builder, Act[] acts, string blocked) {
            builder.HStack(row => {
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
            }).Height(RowHeight);
        }
        // One message slot of fixed height: the source in bold, then the text, wrapped over two lines. A longer text
        // ends in an ellipsis; the session details hold every message in full.
        void MessageSlot(UIPanelBuilder builder, string source, string markup, bool older) =>
            builder.AddLabel((older ? "<alpha=#80>" : "") + (String.IsNullOrEmpty(source) ? "" : "<b>" + Literal(source) + "</b> ") + markup)
                .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.Normal).GetComponentsInChildren<TMP_Text>(true).ToList()
                // The native scroll view lets its viewport run 3 units under the scroll bar; the text stops short of it.
                .ForEach(text => text.margin = new Vector4(0, 0, 4, 0));
        // A result that begins with its own source does not say it twice: "Placement  completed.".
        static string WithoutSource(EditFeedback.Message message) =>
            message.Text.StartsWith(message.Source + " ", StringComparison.OrdinalIgnoreCase) ? message.Text.Substring(message.Source.Length + 1) : message.Text;
        static string ToneOf(string text) {
            foreach (var word in new[] { " refused", " failed", "unavailable", "could not", " stopped" }) if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return Red;
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
            // A starter placement marker's modification is removed or rechecked from every tab.
            var block = selected == null ? "Inspect an object first." : IsStarter ? StarterBlock() : EditBlock();
            var identity = selected?.Identity;
            var unchanged = block ?? (IsChanged(selected) ? null : "This mod does not change this object.");
            return new[] {
                // The whole modification goes: the control says so in full and takes two cells.
                Slot(CommandBand.RemoveModification, selected != null && selected.Identity.Structure == CrossingCoverageScene.Structure ? "Remove this mod's modification. Every circuit returns to its coverage without it, and its detection additions and lights and gates are deleted." : "Remove this mod's modification. The object returns to its underlying state.", unchanged, () => Edit(ModificationPlace, "Removal", (current, inspection) => {
                    var removed = current.Remove(identity, inspection);
                    // Removal deletes the crossing's additions and returns its circuits: a form that was open for one closes.
                    if (removed?.Applied == true) { CloseCoverageForm(); CloseEquipmentForm(); }
                    return removed;
                }), null, 2),
                Slot("Recheck", "Recheck the object after a failed edit.", block, () => Edit(ModificationPlace, "Recheck", (current, inspection) => current.Recheck(identity, inspection))),
                Empty };
        }
        // The actions of one tab, for its pinned row. They belong to the open tab and mode only.
        Act[] TabActs(string id) {
            var block = selected == null ? "Inspect an object first." : EditBlock();
            var identity = selected?.Identity;
            switch (id) {
                case PlacementTab:
                    block = block ?? PadBlock;
                    // Without fields there is nothing to apply or to read: an object whose placement stands as running
                    // lines has no value controls.
                    if ((placementMode == 0 || IsPad) && block != null) return new Act[0];
                    if (placementMode == 0 || IsPad) return new[] {
                        Typed(Slot("Apply", "Apply the entered position and rotation. Placement does not change a track binding.", null, ApplyPlacement), () => CommandBand.FieldReason(block, placementFields, CurrentPlacement(), CommandBand.SamePlacement)),
                        Slot("Read", "Fill the fields with the inspected values.", block, ReadPlacement), Empty, Empty };
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
                            Slot(CommandBand.DiscardPrepared, "Discard the prepared snap. The object stays where it is.", unprepared, ClearSnap, "Discard prepared snap") };
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
                                    if (!maps.SequenceEqual(capabilities.MapIds)) throw new InvalidOperationException("Map context changed; select the track again");
                                    var anchor = picked.Identity.Kind == SelectionKind.TrackNode ? TrackAnchor.Endpoint(picked.Identity.Identifier)
                                        : TrackAnchor.At(trackPoses.Nearest(picked.Identity.Identifier, ((GameObject)selected.Handle).transform.position));
                                    return current.Follow(identity, anchor, inspection);
                                }, true);
                            }, "Start following"),
                            Slot("Stop", "Stop following and keep the current placement.", unavailable ?? (following == null ? "This object does not follow a track." : null),
                                () => Edit(FollowPlace, "Stop following", (current, inspection) => current.StopFollowing(identity, inspection)), "Stop following") };
                    }
                case ConnectionsTab: {
                    var structure = identity?.Structure;
                    if (block == null && structure == CrossingCoverageScene.Structure && coverageCircuit == null && equipmentSource != null) {
                        var session = attachment.Session;
                        var equipmentId = (equipmentFields[0] ?? "").Trim();
                        IReadOnlyList<EquipmentEntry> entries = Array.Empty<EquipmentEntry>();
                        try { entries = session.EquipmentEntries(identity); } catch { }
                        // Removal applies to an addition the scene patch file holds, whether or not it was built.
                        var inScenePatchFile = entries.Any(e => e.Addition.Id == equipmentId);
                        var prepared = preparedEquipment != null && preparedEquipment.Preview.Error == null ? preparedEquipment : null;
                        return new[] {
                            Slot("Preview", "Preview this light or gate.", null, () => {
                                var value = ParseEquipment(out var problem);
                                if (value == null) feedback.Refuse(EquipmentPlace, "Equipment preview", problem); else PrepareEquipment(session, identity, value, null);
                            }),
                            Slot("Remove", "Preview removing this light or gate.", inScenePatchFile ? null : "This light or gate is not in the scene patch file yet.", () => PrepareEquipment(session, identity, null, equipmentId)),
                            Empty,
                            Slot("Apply", "Apply the previewed change.", prepared == null ? "Preview a change first." : null, () => Edit(EquipmentPlace, "Lights and gates", (edit, inspection) => {
                                var done = prepared.Addition != null ? edit.AddEquipment(identity, prepared.Addition, inspection) : edit.RemoveEquipment(identity, prepared.RemoveId, inspection);
                                if (done?.Applied == true && prepared.Addition == null) CloseEquipmentForm();
                                return done;
                            }, true)) };
                    }
                    if (block == null && structure == CrossingCoverageScene.Structure) {
                        var session = attachment.Session;
                        var circuitKey = coverageCircuit; var additionId = coverageAddition;
                        var none = circuitKey == null ? "Select Edit circuit or Add detection first." : null;
                        CrossingCoverage current = null;
                        try { current = session.Coverage(identity); } catch { }
                        // A crossing keeps at least one covered circuit; the preview and the edit refuse the last detach too.
                        var last = none == null && CrossingCoverage.LastCovered(current, circuitKey);
                        var prepared = preparedCoverage != null && preparedCoverage.Circuit == circuitKey && preparedCoverage.Preview.Error == null ? preparedCoverage : null;
                        Action apply = () => Edit(CoveragePlace, "Crossing coverage", (edit, inspection) => {
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
                                Slot("Delete", "Preview deleting this detection addition.", !inScenePatchFile ? "This addition is not in the scene patch file yet." : last ? "This is the crossing's last detection; return a native circuit to its coverage first." : null, () => PrepareCoverage(session, identity, circuitKey, null, additionId)),
                                Empty,
                                Slot("Apply", "Apply the previewed coverage.", prepared == null ? "Preview a change first." : null, apply) };
                        }
                        return new[] { preview,
                            Slot("Detach", "Preview detaching this circuit.", none ?? (last ? "This is the crossing's last covered circuit; detaching it would leave the crossing without detection." : null), () => PrepareCoverage(session, identity, circuitKey, CircuitCoverage.Detached, null), null, 1, 72),
                            // Every Clear in a tab names what it clears.
                            Slot(CommandBand.ClearCircuit, "Preview clearing this mod's value for the circuit; it returns to its underlying coverage.", none, () => PrepareCoverage(session, identity, circuitKey, null, null), null, 1, 112),
                            Slot("Apply", "Apply the previewed coverage.", none ?? (prepared == null ? "Preview a change first." : null), apply, null, 1, 62) };
                    }
                    if (structure == StarterPlacementScene.Structure) {
                        var confirmedTrack = selection?.Confirmed?.Identity;
                        var picked = confirmedTrack != null && confirmedTrack.Kind == SelectionKind.TrackSegment;
                        return new[] {
                            Typed(Slot("Set", "Place the front of the car cut at the entered track location. The whole cut is checked first; the object does not move.", null, ApplyStarterLocation, "Set track location", 1, 56),
                                () => CommandBand.FieldReason(block, binding, CurrentBinding(), CommandBand.SameLocation, CommandBand.BindingIds)),
                            Slot("Detach", "Detach the marker from its track.", block ?? StarterPlacements.NoDetach, () => { }, "Detach track location", 1, 72),
                            Slot(CommandBand.ClearLocation, "Clear this mod's value for the track location; the marker returns to its underlying track location.", block ?? CommandBand.ClearLocationReason(HasStarterTrack(identity)),
                                () => Edit(BindingPlace, "Clearing the track location's mod value", (current, inspection) => current.ClearStarterTrack(identity, inspection)), null, 1, 124),
                            Slot("Picked", "Fill the segment field with the confirmed segment.", block ?? (picked ? null : "Choose a segment with Segment and confirm it first."), () => { binding[0] = confirmedTrack.Identifier; }, "Picked segment", 1, 70) };
                    }
                    var pad = structure == CrossingPadScene.Structure;
                    // An object without a binding names that first; why it cannot be edited in general comes second.
                    var unavailable = pad || structure == TrackMarkerScene.Structure || structure == MilepostScene.Structure ? block : selected == null ? block : BindingUnavailable();
                    var confirmed = selection?.Confirmed?.Identity;
                    var segment = confirmed != null && confirmed.Kind == SelectionKind.TrackSegment;
                    return new[] {
                        Typed(Slot("Set", pad ? "Register the pad's marker at the entered track location and regenerate the pad there." : "Register the marker at the entered track location. The object does not move.", null, ApplyBinding, "Set binding", 1, 56), () => CommandBand.FieldReason(unavailable, binding, CurrentBinding(), CommandBand.SameBinding, CommandBand.BindingIds)),
                        Slot("Detach", "Detach the marker from its track.", unavailable ?? (pad ? "A crossing pad cannot be detached. Disable it on the Object tab to hide it." : CurrentBinding()[0].Length == 0 ? CommandBand.AlreadyDetached : null), () => Edit(BindingPlace, "Track detachment", (current, inspection) => current.Bind(identity, MarkerBinding.Detached, inspection)), "Detach binding", 1, 72),
                        Slot(CommandBand.ClearBinding, "Clear this mod's value for the binding; the marker returns to its underlying binding.", unavailable, () => Edit(BindingPlace, "Clearing the binding's mod value", (current, inspection) => current.Bind(identity, null, inspection)), null, 1, 124),
                        Slot("Picked", "Fill the segment field with the confirmed segment.", unavailable ?? (segment ? null : "Choose a replacement segment and confirm it first."), () => { binding[0] = confirmed.Identifier; }, "Picked segment", 1, 70) };
                }
                case ChangesTab: {
                    // Retry belongs to terrain output: an object without it has no action on this tab.
                    if (selected != null && !HasTerrain) return new Act[0];
                    var idle = block;
                    if (idle == null) { var output = inspectedOutput.Of(attachment.Session, selected, out _); if (output.Outstanding.Count == 0) idle = "No terrain is pending."; }
                    return new[] {
                        Slot("Retry", "Ask the game to rebuild the pending terrain again.", idle, () => {
                            var outcome = attachment.Session.RetryOutput(identity);
                            feedback.Report(TerrainPlace, outcome.Applied ? "Terrain requested again." : "Terrain retry refused: " + outcome.Error);
                        }, "Retry terrain output") };
                }
                default: {
                    var target = selected?.Handle as GameObject;
                    var enabled = target != null && (selected.Family == BakedSceneryRules.Family ? selected.Enabled : target.activeSelf);
                    return new[] {
                        Slot(enabled ? "Disable" : "Enable", enabled ? "Disable this object." : "Enable this object.", block, () => { var enable = !enabled; Edit(ActivityPlace, enable ? "Enable" : "Disable", (current, inspection) => current.Edit(identity, null, enable, inspection)); }, null, 1, 0, enabled ? "Enable" : "Disable"),
                        // Named by what it takes back, over two cells; disabled while this mod sets no activity.
                        Slot(CommandBand.ClearActivity, "Clear this mod's value for the activity; the object returns to its underlying activity.", block ?? CommandBand.ClearActivityReason(HasActivityValue(identity)),
                            () => Edit(ActivityPlace, "Clearing the activity's mod value", (current, inspection) => current.ClearActivity(identity, inspection)), null, 2) };
                }
            }
        }
        // Why no placement mode edits the inspected object when it is a crossing pad; null for any other object.
        string PadBlock => IsPad ? CommandBand.PadFollowsTrack : null;
        bool HasStarterTrack(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasStarterTrack(identity); } catch { return false; }
        }
        void ApplyStarterLocation() {
            var identity = selected.Identity;
            var end = binding[1].Trim().ToLowerInvariant();
            if (String.IsNullOrWhiteSpace(binding[0]) || (end != "a" && end != "b") || !Double.TryParse(binding[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)) {
                feedback.Refuse(BindingPlace, "Track location", "enter a segment ID, end a or b, and a distance with a decimal point");
                return;
            }
            var location = new TrackLocation(binding[0].Trim(), end, distance);
            Edit(BindingPlace, CommandBand.StarterAction(location.Segment, end, distance), (current, inspection) => current.PlaceStarter(identity, location, inspection), true);
        }
        bool HasActivityValue(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasActivityValue(identity); } catch { return false; }
        }
        void ApplyPlacement() {
            var identity = selected.Identity;
            // A field that still shows the object's value stands for that exact value, so its rounding moves
            // nothing; a rotation whose three fields were not touched stays the object's own rotation.
            var own = PlacementValues();
            var values = new double[6]; var valid = own != null;
            for (var i = 0; valid && i < 6; i++) valid &= CommandBand.Entered(placementFields[i], own[i], out values[i]);
            if (!valid) { feedback.Refuse(PlacementPlace, "Placement", "enter six numbers with a decimal point"); return; }
            var turned = values[3] != own[3] || values[4] != own[4] || values[5] != own[5];
            var rotation = Quaternion.Euler((float)values[3], (float)values[4], (float)values[5]);
            var placement = new Placement(new[] { values[0], values[1], values[2] }, turned ? new double[] { rotation.x, rotation.y, rotation.z, rotation.w } : selected.Rotation.ToArray());
            Edit(PlacementPlace, "Placement", (current, inspection) => current.Edit(identity, placement, null, inspection));
        }
        void ApplyBinding() {
            var identity = selected.Identity;
            var end = binding[1].Trim().ToLowerInvariant();
            if (String.IsNullOrWhiteSpace(binding[0]) || (end != "a" && end != "b") || !Double.TryParse(binding[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)) {
                feedback.Refuse(BindingPlace, "Track binding", "enter a segment ID, end a or b, and a distance with a decimal point");
                return;
            }
            var value = MarkerBinding.At(new TrackLocation(binding[0].Trim(), end, distance));
            Edit(BindingPlace, CommandBand.BindingAction(binding[0].Trim(), end, distance), (current, inspection) => current.Bind(identity, value, inspection), true);
        }

        // The World choice section of the session details window: what the choice is for, the candidate that was clicked, and
        // the other candidates at that point, each named by what tells it apart from the others.
        void DrawChoice(UIPanelBuilder builder) {
            var pending = selection?.Pending;
            if (pending == null) return;
            Section(builder, "World choice");
            PlainLabel(builder, (choosingSnap ? "Select physical snap target" : pending.Purpose) + ".");
            if (pending.Source != null) PlainLabel(builder, "Object: " + pending.Source.Label);
            if (pending.Problem != null) builder.AddLabel(PendingSelection.IsInstruction(pending.Problem) ? Literal(pending.Problem) : Tint(Red, pending.Problem));
            var candidate = selection.Find(pending.Candidate);
            if (candidate == null && pending.Offers.Count == 0) Line(builder, "Nothing clicked yet. Click a marking or track in the world.", null, null);
            if (candidate != null) {
                builder.AddLabel(Tint(Blue, CommandBand.ChoiceTag("Candidate", pending.Number(candidate.Choice), pending.Offers.Count) + ": " + candidate.Choice.Label));
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
                var button = Button(builder, CommandBand.ChoiceTag("Choose", pending.Number(choice), pending.Offers.Count) + ": " + CommandBand.ChoiceLabel(choice.Label, labels), () => pending.Choose(choice));
                button.Tooltip("Choose this candidate", choice.Label);
                button.RectTransform.gameObject.AddComponent<HoverReport>().Changed = over => {
                    if (over) pending.Highlight = choice; else if (ReferenceEquals(pending.Highlight, choice)) pending.Highlight = null;
                };
            }
        }
        // The session details, in their own window: session state that the panel shows nowhere else. The messages are
        // in the footer and the reasons of the two lamps in their tooltips, so they are not repeated here.
        void DrawSession(UIPanelBuilder builder) {
            DrawChoice(builder);
            Section(builder, "Inventory");
            PlainLabel(builder, status);
            if (scene != null) PlainLabel(builder, scene.Status);
            Section(builder, "Editing");
            builder.AddLabel(PathBreaks(Literal(String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : editingStatus)));
            if (attachment != null) {
                var session = attachment.Session;
                if (session.LastSave != null && session.LastSave.Error != null) builder.AddLabel(Tint(Red, "Last Save failed: " + session.LastSave.Error + " Completed: " + (session.LastSave.Completed.Count == 0 ? "none" : String.Join(", ", session.LastSave.Completed)) + ". Outstanding: " + String.Join(", ", session.LastSave.Outstanding) + ". Edits remain in memory; correct the cause and Save again."));
                if (session.IncompleteOperation != null) builder.AddLabel(Tint(Red, "Incomplete operation: " + session.IncompleteOperation));
                if (attachment.Fault != null) builder.AddLabel(Tint(Red, attachment.Fault));
            }
            DrawScenePatchFileRecovery(builder);
            Section(builder, "Supplied definitions");
            if (String.IsNullOrEmpty(scenePatchFileInspection)) Line(builder, definitionsStale ? "Not read yet; select Refresh" : "None", null, null);
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
            ["main"] = new HelpText { Title = "Scene patches", Intro = "This help applies to the whole Scene patches window. Edit scene objects of the edited mod here, then save through the Map Editor.",
                Terms = new[] {
                    new[] { "Save", "The Map Editor's Save button writes map data, Data/scene-patches.json and the patcher dependency in Info.json. Save never reloads." },
                    new[] { "Title bar", "The ? opens this help. The round arrow beside it puts the window at its default place and restores the outliner width. The page beside that opens the session details in their own window." },
                    new[] { "Help", "Every ? opens its help in a window beside it and shows x while that help is open. Close the help with the x or with its own close button." },
                    new[] { "Clear", "Ends the inspection and cancels a pending world choice, gizmo or snap. A click on the inspected row, or Escape with the pointer over this window, does the same. With the pointer over the world, Escape cancels only a pending world choice or the gizmo. Changes that were made stay." },
                    new[] { "Undo and Redo", "Every completed edit here is one step in the Map Editor's own history: use the Map Editor's Undo and Redo." },
                    new[] { "Refresh", "Reads the scene again and applies nothing. Refresh after a native edit, Undo or Redo; a stale inventory refuses edits." },
                    new[] { "Map Editor reload", "Reloading the Map Editor ends the Undo and Redo history. Leaving the Map Editor discards unsaved work." },
                    new[] { "Current, Stale, Saved, Unsaved", "The two lamps above the outliner show the inventory and save state: green when good, amber otherwise. They are not buttons; their tooltips give the reasons." },
                    new[] { "* and +", "Before a name, * marks an object this mod changes and + an object it adds; the state line shows the same as CHANGED and ADDED. Neither means unsaved: only the Unsaved lamp does." },
                    new[] { "Footer", "Below the messages, Remove modification and Recheck are the same on every tab. A tab's own actions stand in the row at the bottom of the tab." },
                    new[] { "Failed edit", "A failed edit restores the object. If that cannot be verified, the object is blocked until Recheck succeeds or the Map Editor reloads." } },
                Guides = new[] { new[] { "Editing in the Map Editor", "editing-placement-in-the-map-editor.md" }, new[] { "Scene patch file reference", "scene-patch-file-v1.md" } } },
            ["outliner"] = new HelpText { Title = "Outliner", Intro = "Find an object and inspect it here, or select it in the world.",
                Terms = new[] {
                    new[] { "Search", "Matches names, paths, families, binding identifiers, dependencies and problems." },
                    new[] { "Changed", "Objects this mod changes, marked *." },
                    new[] { "Disabled", "Objects that are switched off themselves, whatever their parents are. They are never listed under Problems; find them here." },
                    new[] { "Problems", "Objects with a detected problem, marked !: a binding the game needs and cannot use, or a stored reference to a segment that no longer exists: a defect you can repair here. Why an object cannot be edited, and what the inspection could not read, are not problems. An object that is switched off itself is never one. No detected problem does not prove correct placement or the intended track." },
                    new[] { "Inspection only", "The last group, shown only while it holds objects: objects of a kind the patcher has no edit for yet and the Map Editor does not edit. They have no problem marker and no edit; the Object tab gives the reason." },
                    new[] { "Dimmed rows", "Loaded objects that are inactive." },
                    new[] { "Focus", "Moves the camera. It selects nothing." },
                    new[] { "Fit names", "Sizes the column to most names. Drag the divider for another width." } },
                Guides = new[] { new[] { "Inspecting objects", "inspect-scene-objects.md" }, new[] { "Selecting in the world", "select-world-targets.md" } } },
            [ObjectTab] = new HelpText { Title = "Object", Intro = "Check the object's state, problems and dependencies before choosing an edit.",
                Terms = new[] {
                    new[] { "Supported edits", "What this patcher can change on this object, or why it cannot be edited. That reason is not a problem." },
                    new[] { "Inspection notes", "What the inspection could not read or check. Nothing here can be repaired with the patcher, so it is not listed as a problem." },
                    new[] { "Enabled", "Whether the object is active. Disable holds it inactive; Clear activity returns it to its underlying activity." },
                    new[] { "Track group", "Some markers switch with their track group. For those, an explicit enable is refused." },
                    new[] { "Dependencies", "Culling, masks, graph registration and other state that follows an edit. Indirect references are not inspected." },
                    new[] { "Runtime state", "Enabled flags the game switches while it runs. They are shown and do not make the inventory stale." },
                    new[] { "Copy ID", "Copies the identity with the target map as scene patch file JSON." } },
                Guides = new[] { new[] { "Inspecting objects", "inspect-scene-objects.md" }, new[] { "Following a track and group activity", "following-track.md" } } },
            [PlacementTab] = new HelpText { Title = "Placement", Intro = "Move or rotate the object without changing its logical binding.",
                Terms = new[] {
                    new[] { "Numeric", "Position in map coordinates, rotation in degrees." },
                    new[] { "Gizmo", "The native gizmo stands on the object while this mode is open. Choose Move or Rotate and drag a handle; each release is one Undo step, and Escape during a drag restores. The fields show the placement and follow the drag." },
                    new[] { "Snap", "Moves the object once onto a chosen track or endpoint: Track or Node, a click in the world, Confirm, then Apply. Discard drops a prepared snap. Rotation and binding stay; Save records fixed coordinates." },
                    new[] { "Follow", "Keeps the current placement relative to a chosen track or endpoint, and moves the object when that track moves: Track or Node, a click in the world, Confirm, then Start. Stop ends it." },
                    new[] { "Bridges and boards", "A bridge moves with its colliders; track-owned scenery stays. A board's terrain output regenerates after the move." },
                    new[] { "Baked scenery groups", "A group moves as one, with its culling and colliders; its parts keep their own Enabled setting." } },
                Guides = new[] { new[] { "Fixed placement", "writing-fixed-placement.md" }, new[] { "Transform gizmos", "using-transform-gizmos.md" }, new[] { "Snapping", "snapping-to-track.md" },
                    new[] { "Following a track", "following-track.md" }, new[] { "Bridges", "placing-bridges.md" }, new[] { "Boards", "moving-boards.md" }, new[] { "Baked scenery groups", "moving-baked-scenery-groups.md" } } },
            [ConnectionsTab] = new HelpText { Title = "Connections", Intro = "Choose which track the object uses, then set the connection.",
                Terms = new[] {
                    new[] { "Track binding", "The track location a marker is registered at. Changing it does not move the object." },
                    new[] { "Replacement targets", "Choose a target in the world, check its ID and confirm it. Confirming changes nothing." },
                    new[] { "Set, Detach, Clear binding", "Set registers the marker at the entered location. Detach removes it from the track. Clear binding returns it to its underlying binding." },
                    new[] { "Crossing coverage", "Each circuit has an approach A, an island and an approach B. Select the tracks a circuit covers, or detach it; proximity never assigns coverage. Preview shows the result for every circuit before Apply." },
                    new[] { "Detection addition", "Makes the crossing detect an extra track: a circuit of its own with three spans on tracks you select, under an ID you give it. Delete removes it again." },
                    new[] { "Lights and gates", "Adds a copy of a light or gate already loaded in the crossing's scene. The crossing controller drives it and culls it with its own equipment. A rejected device names why it does not qualify." },
                    new[] { "Crossing pad", "A pad has the binding of its marker. Setting it regenerates the pad geometry and its culling along the track at that location; the pad object does not move. A pad cannot be detached: disable it to hide it." },
                    new[] { "Milepost", "Set a milepost's track binding to register its marker at that track location. The post stays in place and keeps its model and mile value." } },
                Guides = new[] { new[] { "Track bindings", "repairing-track-bindings.md" }, new[] { "Mileposts", "repositioning-mileposts.md" }, new[] { "Crossing pads", "rebinding-crossing-pads.md" }, new[] { "Crossing coverage", "correcting-crossing-coverage.md" }, new[] { "Lights and gates", "adding-crossing-equipment.md" }, new[] { "Selecting in the world", "select-world-targets.md" } } },
            [ChangesTab] = new HelpText { Title = "Changes", Intro = "Check what this mod changes on the object and complete outstanding work.",
                Terms = new[] {
                    new[] { "Remove modification", "In the footer on every tab. Deletes this mod's modification. The object returns to its underlying state, not to an earlier patched position." },
                    new[] { "Recheck", "In the footer on every tab. After a failed edit, compares the object with the scene patch file and unblocks it once they agree." },
                    new[] { "Terrain output", "The game regenerates terrain after a board move. Save does not write that output. Retry asks for it again." } },
                Guides = new[] { new[] { "Editing in the Map Editor", "editing-placement-in-the-map-editor.md" }, new[] { "Boards", "moving-boards.md" } } },
        };
        void DrawHelp(UIPanelBuilder builder, string region) {
            if (!Help.TryGetValue(region, out var help)) return;
            // The window commands stand in the title bar; where it could not take one, the window's help holds it.
            if (region == NativePresentation.WindowHelpRegion && (!presentation.TitleReset || !presentation.TitleDetails)) builder.HStack(row => {
                if (!presentation.TitleReset) Button(row, "Reset layout", ResetLayout).Tooltip("Reset layout", "Put the window at its default place again and restore the outliner width.");
                if (!presentation.TitleDetails) Button(row, "Session details", () => presentation.ToggleDetails()).Tooltip("Session details", "Open or close the session details in their own window.");
            }).Height(RowHeight);
            builder.AddLabel("<style=H3>" + help.Title + "</style>");
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
