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
    // fixed footer. Every region has a fixed height or scrolls inside its own rectangle, so showing or hiding text or
    // feedback moves no other control.
    public sealed partial class PatcherBehaviour {
        const string ObjectTab = "object", PlacementTab = "placement", ConnectionsTab = "connections", ChangesTab = "changes";
        // Canvas units.
        // The outliner is never narrower than its three filter segments at their natural widths.
        // The inspector's minimum holds the four footer slots, their spacing and the resize grip.
        // The outliner is also never narrower than its band: Refresh and both status lamps with their words.
        const float OutlinerDefaultWidth = 252, OutlinerMinimumWidth = 252, InspectorMinimumWidth = 350, SlotWidth = 80, TabBodyMinimumHeight = 80,
            RowHeight = 24, LineHeight = 20, MessageHeight = 40,
            // What a row adds to its name: the folder and child indent, the two markers and the scroll bar.
            RowDecoration = 80;
        // The changed state reads the same in the state line and on the Changes tab: the native Map Editor's badge.
        static readonly string ChangedBadge = CommandBand.Changed.Markup;
        // The space the name row keeps free at its right end, under Details, for the Inspector and Guided switch. The
        // left band's widths are CommandBand's, and the outliner's minimum holds them: 252 against the 251 they need.
        const float GuidedSwitchSpace = 150;
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
        enum BodyView { Tabs, TabHelp, MainHelp, Session }
        static readonly string[] PlacementModes = { "Numeric", "Gizmo", "Snap", "Follow" };
        OutlinerFilter outlinerFilter;
        float outlinerWidth = OutlinerDefaultWidth;
        BodyView bodyView;
        bool outlinerHelp;
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
        // Scene and document text is shown literally inside the native text styles.
        static string Literal(string value) => (value ?? "").Replace("<", "<noparse><</noparse>");
        static string Tint(string color, string value) => "<color=" + color + ">" + Literal(value) + "</color>";
        // A named value: the native field label above the value, left-aligned. Every tab uses this one form, so the same
        // kind of element has the same size, weight and colour everywhere.
        RectTransform Field(UIPanelBuilder builder, string label, string value) => FieldMarkup(builder, label, Literal(value));
        RectTransform FieldMarkup(UIPanelBuilder builder, string label, string markup) { FieldLabel(builder, label); return builder.AddLabel(markup); }
        // A section heading: the native section header under a rule. The rule sets a heading apart from a field label,
        // which has the same small upper-case letters.
        void Section(UIPanelBuilder builder, string title) { builder.AddHRule(); builder.AddSection(title); }
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
                if (key != null) Field(builder, (group + " " + key).Trim(), Breakable(value));
                key = part.Substring(0, equals); value = part.Substring(equals + 1);
            }
            if (key != null) Field(builder, (group + " " + key).Trim(), Breakable(value));
        }
        // An input field, or its value as text without the input frame when it cannot be edited now. Both are as tall, so
        // a field that becomes read-only moves nothing.
        void ValueField(UIPanelBuilder field, string label, string[] values, int index, string block, string tip, bool numeric) {
            FieldLabel(field, label);
            if (block != null) {
                var text = Line(field, Literal(values[index]), label, block, 30);
                if (numeric) text.HorizontalTextAlignment(HorizontalAlignmentOptions.Right);
                return;
            }
            var input = field.AddInputField(values[index], value => values[index] = value);
            input.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => values[index] = value);
            if (numeric) AlignNumeric(input);
            input.Tooltip(label, tip);
            var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 0; layout.flexibleWidth = 1;
        }
        // The label of an input field and the alignment of a number in it, as the native inspectors build them.
        void FieldLabel(UIPanelBuilder builder, string label) => builder.AddLabel("<style=FieldLabel>" + Literal(label) + "</style>");
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
        float OutlinerMaximum() => Mathf.Max(OutlinerMinimumWidth, presentation.ContentWidth - NativeEditorPresentation.DividerWidth - InspectorMinimumWidth - 26);

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
                const int size = 32;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CrankyAnt Scene Patcher lamp", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++) {
                        // One pixel of soft edge around a filled disc.
                        var distance = Mathf.Sqrt((x + 0.5f - size * 0.5f) * (x + 0.5f - size * 0.5f) + (y + 0.5f - size * 0.5f) * (y + 0.5f - size * 0.5f));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(size * 0.5f - 0.5f - distance)));
                    }
                texture.SetPixels32(pixels); texture.Apply();
                lampSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
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
            layout.minWidth = layout.preferredWidth = CommandBand.Dot; layout.minHeight = layout.preferredHeight = CommandBand.Dot; layout.flexibleWidth = 0; layout.flexibleHeight = 0;
            return (RectTransform)dot.transform;
        }
        // The right band, over the inspector: the commands for the object, with Details at the right end. A command that
        // cannot run stays in place, greyed out, with its reason.
        void DrawRightBand(UIPanelBuilder builder) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var select = CommandBand.SelectInWorld(selection != null, selection?.Pending != null);
                Button(row, "Select in world", () => BeginSelection(SelectionKind.SceneObject, null)).Disable(select != null)
                    .Tooltip("Select in world", select ?? "Click a marking in the world to choose the object to inspect.");
                var focus = CommandBand.Focus(selected != null);
                Button(row, "Focus", () => { if (selected != null) Focus(selected); }).Disable(focus != null)
                    .Tooltip("Focus", focus ?? "Move the camera to the inspected object. Focus selects nothing.");
                var copy = CommandBand.CopyId(selected != null);
                Button(row, "Copy ID", CopyIdentity).Disable(copy != null)
                    .Tooltip("Copy ID", copy ?? "Copy the inspected object's map context, target identity and placement as patch-document JSON.");
                row.Spacer();
                // The window's "?" belongs in the title bar; it stands here only when the title bar could not take it.
                if (!presentation.TitleHelp) HelpButton(row, bodyView == BodyView.MainHelp, "Window", ToggleWindowHelp);
                Button(row, "Details", () => bodyView = bodyView == BodyView.Session ? BodyView.Tabs : BodyView.Session).Width(66)
                    .Tooltip("Session details", "Messages, inventory and save state, supplied definitions and diagnostics.");
            }).Height(RowHeight);
        }
        void ToggleWindowHelp() { bodyView = bodyView == BodyView.MainHelp ? BodyView.Tabs : BodyView.MainHelp; }
        // A "?" names its scope in its tooltip and shows "x" while its help is open.
        void HelpButton(UIPanelBuilder row, bool open, string scope, Action toggle) =>
            Button(row, open ? "x" : "?", toggle).Width(28).Tooltip(scope + " help", open ? "Close the " + scope.ToLowerInvariant() + " help." : "Explain the " + scope.ToLowerInvariant() + ".");
        bool Unsaved(out string text) {
            if (authoring == null) { text = String.IsNullOrEmpty(authoringStatus) ? "Editing is unavailable." : authoringStatus; return false; }
            try {
                var outstanding = authoring.Session.Outstanding(authoring.NativeDirty);
                text = outstanding.Count == 0 ? "All required writes are saved." : "Unsaved: " + String.Join(", ", outstanding) + ". Use the editor's Save button.";
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

        // No supported operation adds a scene object yet: a detection addition adds a circuit to an existing crossing
        // and is not an added object. The marker and the badge are ready for the first operation that does.
        bool IsAdded(SceneObservation entry) => false;
        bool IsChanged(SceneObservation entry) {
            try { return authoring != null && authoring.Session.IsChanged(entry.Identity); } catch { return false; }
        }
        void DrawOutliner(UIPanelBuilder builder) {
            builder.ConfigureLayout(4f);
            DrawLeftBand(builder);
            UIPanel results = null;
            builder.HStack(row => {
                row.AddFilterField(filter, value => { filter = value; results?.SetNeedsRebuild(); }, null, true).FlexibleWidth();
                HelpButton(row, outlinerHelp, "Outliner", () => outlinerHelp = !outlinerHelp);
                // Beside the divider, because it sets the column width. The native builder's icon buttons take a sprite
                // name from the game's assets and none of the known ones means width, so the control is a short text.
                IConfigurableElement fit = null;
                fit = Button(row, "Fit", () => FitOutliner(fit.RectTransform.GetComponentInChildren<TMP_Text>(true)));
                fit.Width(40).Tooltip("Fit names", "Size this column to most names shown. Drag the divider for another width.");
            }).Height(30);
            builder.AddSegmentedControlCompact(segments => {
                segments.AddSegment("All", outlinerFilter == OutlinerFilter.All, presentation.Action(() => outlinerFilter = OutlinerFilter.All)).Tooltip("All", "Show every observed object.");
                segments.AddSegment("Problems", outlinerFilter == OutlinerFilter.Problems, presentation.Action(() => outlinerFilter = OutlinerFilter.Problems)).Tooltip("Problems", "Show the objects with a detected problem.");
                segments.AddSegment("Changed", outlinerFilter == OutlinerFilter.Changed, presentation.Action(() => outlinerFilter = OutlinerFilter.Changed)).Tooltip("Changed", "Show the objects this MapMod changes.");
            });
            results = builder.AddRebuildableSection(items => {
                items.ConfigureLayout(4f);
                if (outlinerHelp) { presentation.Scroll(items, 120, help => { help.ConfigureLayout(4f); DrawHelp(help, "outliner", () => outlinerHelp = false); }, false); return; }
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
                    var groupKey = "family:" + group.Key;
                    // The count stands first, so a narrow column cuts the family name and never the count; the name
                    // follows it directly.
                    var parent = new UIOutlineItem(groupKey, "<alpha=#99>" + group.Value.Count.ToString(CultureInfo.InvariantCulture) + "<alpha=#FF> " + Literal(group.Key));
                    var repeated = new HashSet<string>(group.Value.GroupBy(InventoryPresentationState.Name).Where(names => names.Count() > 1).Select(names => names.Key));
                    foreach (var item in group.Value) {
                        var key = indices[item].ToString(CultureInfo.InvariantCulture);
                        byKey[key] = item;
                        if (ReferenceEquals(selected, item)) {
                            selectedKey = key;
                            // A newly inspected object is revealed once; afterwards its folder stays as the user leaves it.
                            if (!ReferenceEquals(revealed, item.Handle)) { revealed = item.Handle; state.SetExpanded(groupKey, true); }
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
                        parent.AddChild(new UIOutlineItem(key, markers.Replace("!", "<color=" + Red + "><b>!</b></color>").Replace("*", "<color=" + Amber + "><b>*</b></color>").Replace("+", "<color=" + Green + "><b>+</b></color>") + (markers.Length == 0 ? "" : " ") +
                            (item.Active ? Literal(name) : "<i>" + Literal(name) + "</i>") + context + (item.Active ? "" : "  <alpha=#99>inactive<alpha=#FF>"), item) {
                            Tooltip = (name + "\n" + item.Identity.Path + "\n" + item.Family + (item.Active ? "" : "\nInactive") + (changed ? "\nChanged by this MapMod" : "") + (added ? "\nAdded by this MapMod" : "") +
                                (item.Problems.Count == 0 ? "" : "\n" + String.Join("\n", item.Problems))).Replace("<", "\\u003c").Replace(">", "\\u003e")
                        });
                    }
                    rows.Add(parent);
                }
                var outline = items.AddOutlineView(rows, state, new UIState<string>(selectedKey), key => {
                    if (!byKey.TryGetValue(key, out var observation)) return;
                    presentation.Action(() => {
                        if (!inventoryPresentation.CanSelect(inventory, observation)) return;
                        CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; selected = inventoryPresentation.CurrentSelection(inventory, observation); ReadPose();
                    })();
                });
                var listLayout = outline.GetComponent<LayoutElement>();
                listLayout.minHeight = 120; listLayout.flexibleHeight = 10000;
            });
            // Three anchors for the facts a modder needs before the first edit. They sit at the bottom edge of the window,
            // so their tooltips are one line; the window help holds the full explanation.
            builder.AddHRule();
            builder.Spacer(4f);
            Line(builder, "<style=Footnote>Save in Map Editor</style>", "Save", "Writes map data, patches and Info.json.");
            Line(builder, "<style=Footnote>Refresh after native edits</style>", "Refresh", "Refresh after Undo, Redo or a native edit.");
            Line(builder, "<style=Footnote>Reload clears Undo history</style>", "Editor reload", "Reloading ends the Undo history.");
            builder.Spacer(4f);
        }
        void FitOutliner(TMP_Text measure) {
            if (measure == null || inventory == null) return;
            var widths = InventoryPresentationState.Rows(inventory, filter, outlinerFilter, IsChanged).Select(row => measure.GetPreferredValues(InventoryPresentationState.Name(row)).x).ToArray();
            var divider = NativeEditorPresentation.DividerWidth + 26;
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
            // The name row keeps its right end free, under Details, for the Inspector and Guided switch.
            builder.HStack(row => {
                Heading(row, Literal(name), "Object", selected == null ? null : selected.Identity.Path).FlexibleWidth();
                row.Spacer(GuidedSwitchSpace);
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
                parts.Add(badge.Markup); text.Add(ReferenceEquals(badge, CommandBand.Changed) ? "Changed by this MapMod" : "Added by this MapMod");
            }
            if (selected.Problems.Count != 0) {
                var count = selected.Problems.Count == 1 ? "1 problem" : selected.Problems.Count + " problems";
                parts.Add("<color=" + Red + ">" + count + "</color>"); text.Add(count);
            }
            plain = String.Join("; ", text) + "\n" + selected.Identity.Path;
            return String.Join("   ", parts);
        }
        // A tab body scrolls inside its own rectangle. Its first row names what the tab is for and holds the tab's own
        // "?", so that help visibly belongs to the tab. Help and the session details take the place of the content.
        void Tab(UIPanelBuilder builder, string id, Action<UIPanelBuilder> body) => presentation.Scroll(builder, TabBodyMinimumHeight, content => {
            content.ConfigureLayout(4f);
            if (bodyView == BodyView.Session) { DrawSession(content); return; }
            if (bodyView == BodyView.MainHelp) { DrawHelp(content, "main", () => bodyView = BodyView.Tabs); return; }
            var help = Help[id];
            content.HStack(row => {
                row.AddLabel("<style=Footnote>" + help.Intro + "</style>").TextWrap(TextOverflowModes.Ellipsis, TextWrappingModes.Normal).Height(32).FlexibleWidth();
                HelpButton(row, bodyView == BodyView.TabHelp, help.Title, () => bodyView = bodyView == BodyView.TabHelp ? BodyView.Tabs : BodyView.TabHelp);
            }).Height(32);
            if (bodyView == BodyView.TabHelp) DrawHelp(content, id, () => bodyView = BodyView.Tabs);
            else if (selected == null) content.AddExpandingVerticalSpacer();
            else body(content);
        });

        // The Object tab: what the object is, whether it is enabled, and what was detected about it.
        void DrawObject(UIPanelBuilder builder) {
            // Problems first: the red count in the state line points at text that is in view.
            Section(builder, "Detected problems");
            if (selected.Problems.Count == 0) Line(builder, "None detected", null, null);
            foreach (var problem in selected.Problems) builder.AddLabel(Tint(Red, problem));
            Section(builder, "State");
            var target = selected.Handle as GameObject;
            var own = target != null && target.activeSelf;
            Field(builder, "Enabled", own ? (selected.Active ? "Yes" : "Yes; a parent is inactive") : "No")
                .Tooltip("Enabled", "Whether this object is active in the scene.");
            if (FollowsGroup) Field(builder, "Track group", "Activity follows the track group. Disable holds it inactive; Clear returns it to the group; an explicit enable is refused.");
            FieldLabel(builder, "Supported edits");
            PlainLabel(builder, selected.Operations);
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
        // A path wraps after a separator instead of in the middle of a name.
        static string Breakable(string path) => (path ?? "").Replace("/", "/\u200B");
        bool FollowsGroup => selected != null && selected.Bindings.Any(b => b.IndexOf("follows track group", StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf("targetActiveFollowsTrackGroup=True", StringComparison.OrdinalIgnoreCase) >= 0);

        // The Placement tab: every way to put the object somewhere. The footer holds the action of the chosen way.
        void DrawPlacement(UIPanelBuilder builder) {
            var tips = new[] { "Enter a position in map coordinates and a rotation in degrees.", "Drag the native Move or Rotate gizmo in the world.",
                "Move the object once onto a chosen track or endpoint.", "Keep the object's pose relative to a chosen track or endpoint." };
            builder.AddSegmentedControlCompact(segments => {
                for (var i = 0; i < PlacementModes.Length; i++) {
                    var mode = i;
                    segments.AddSegment(PlacementModes[i], placementMode == i, presentation.Action(() => placementMode = mode)).Tooltip(PlacementModes[i], tips[i]);
                }
            }, true);
            var block = EditBlock();
            if (placementMode == 0) {
                var labels = new[] { "X", "Y", "Z", "Rot X", "Rot Y", "Rot Z" };
                for (var row = 0; row < 2; row++) {
                    var first = row * 3;
                    builder.HStack(fields => {
                        for (var column = 0; column < 3; column++) {
                            var index = first + column;
                            fields.VStack(field => ValueField(field, labels[index], pose, index, block, index < 3 ? "Map coordinates, in meters" : "Euler angles, degrees", true)).EqualFlexibleWidth();
                        }
                    });
                }
            } else if (placementMode == 1) {
                Field(builder, "Gizmo", gizmo?.Active == true ? gizmo.State : "Idle");
            } else if (placementMode == 2) {
                Field(builder, "Target", preparedSnap == null ? "None chosen" : preparedSnap.Identity.ToString());
                Field(builder, "Position", preparedSnap == null ? "None" : String.Join(", ", preparedSnap.Placement.Position.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))));
            } else {
                TrackFollowing following = null;
                if (block == null && trackPoses != null) try { following = authoring.Session.FollowingOf(selected.Identity); } catch { }
                Field(builder, "Following", following == null ? "Nothing" : following.Anchor.ToString());
                Field(builder, "Offset", following == null ? "None" : String.Join(", ", following.Offset.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))));
                var picked = FollowTarget();
                Field(builder, "Picked", picked == null ? "None" : picked.Identity.ToString());
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
        // crossing object never moves, and proximity never assigns coverage. The footer holds Preview, Detach, Clear and
        // Apply for the circuit being edited, and Preview, Delete and Apply for a detection addition.
        void DrawCoverage(UIPanelBuilder builder) {
            var identity = selected.Identity;
            var block = EditBlock();
            Section(builder, "Crossing coverage");
            CrossingCoverage current = null;
            if (authoring != null) try { current = authoring.Session.Coverage(identity); }
                catch (Exception e) { builder.AddLabel(Tint(Red, "Coverage unavailable: " + e.GetBaseException().Message)); }
            if (current != null) {
                foreach (var circuit in current.Circuits) {
                    var key = circuit.Key; var value = circuit.Value;
                    // A native circuit is named by its island span, a detection addition by its ID.
                    var name = DetectionAddition.Id(key) == null ? "Circuit " + key : "Detection addition " + DetectionAddition.Id(key);
                    Field(builder, name, value.ToString());
                    Button(builder, "Edit " + name, () => { OpenedForm(null); coverageCircuit = key; coverageAddition = DetectionAddition.Id(key); preparedCoverage = null; ReadCoverage(value); }).Disable(block != null)
                        .Tooltip("Edit " + name, block ?? (DetectionAddition.Id(key) == null ? "Select the tracks this circuit covers, or detach it. Circuits you do not edit keep their coverage." : "Change the tracks of this added detection, or delete it."));
                }
                if (current.Residue.Count > 0) builder.AddLabel(Tint(Amber, "Owned spans left by an incomplete cleanup: " + String.Join(", ", current.Residue) + ". Use Recheck on the Changes tab."));
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
                        coverageAddition = id; coverageCircuit = existing.Key ?? DetectionAddition.Key("", id); preparedCoverage = null; ReadCoverage(existing.Value);
                        OpenedForm("Detection addition " + id + " is open below: select its tracks, then Preview.");
                    }).Disable(block != null).Tooltip("Add detection with this ID", block ?? "Make the crossing detect an extra track: give the addition an ID that is unique in this MapMod's patch document, then select its tracks.");
                });
            }
            if (coverageCircuit == null) return;
            Section(builder, coverageAddition == null ? "Circuit " + coverageCircuit : "Detection addition " + coverageAddition);
            var confirmed = selection?.Confirmed?.Identity;
            var picked = confirmed != null && confirmed.Kind == SelectionKind.TrackSegment;
            var labels = new[] { "Segment", "End (A or B)", "From (m)", "To (m)" };
            for (var role = 0; role < 3; role++) {
                var first = role * 4; var roleName = CircuitCoverage.Roles[role];
                builder.HStack(fields => {
                    for (var column = 0; column < 4; column++) {
                        var index = first + column; var label = roleName + " " + labels[column];
                        fields.VStack(field => ValueField(field, label, coverageFields, index, block, "Distances are metres from the chosen end. From is the end next to the island for an approach.", column >= 2)).EqualFlexibleWidth();
                    }
                });
                Button(builder, "Picked for " + roleName, () => { coverageFields[first] = confirmed.Identifier; preparedCoverage = null; }).Disable(!picked || block != null)
                    .Tooltip("Picked segment", block ?? (picked ? "Fill this role's segment with the confirmed segment." : "Choose a segment under Replacement targets and confirm it first."));
            }
            Section(builder, "Preview");
            if (preparedCoverage == null || preparedCoverage.Circuit != coverageCircuit) Line(builder, "None", null, null);
            else if (preparedCoverage.Preview.Error != null) builder.AddLabel(Tint(Red, "Preview refused: " + preparedCoverage.Preview.Error));
            else foreach (var line in preparedCoverage.Preview.Lines) PlainLabel(builder, Breakable(line));
        }
        // Why the inspected object has no editable track binding: its own detected reason when it holds a marker the
        // adapter refuses, such as a milepost's, or that the operation is for markers and pads.
        string BindingUnavailable() {
            var listed = selected?.Problems.FirstOrDefault(problem => problem.StartsWith("Track binding unavailable", StringComparison.Ordinal) || problem.StartsWith("Pad binding unavailable", StringComparison.Ordinal));
            if (listed != null) return listed;
            // A milepost holds a marker too, but the inventory lists it as a milepost and records no binding problem for
            // it: ask the marker adapter why it refuses this object.
            try {
                if (selected?.Handle is GameObject target && target != null && target.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == TrackMarkerWorld.MarkerType)) {
                    var refusal = TrackMarkerWorld.Problem(target, NativeInventory.LastReferrers, null);
                    if (refusal != null) return "Track binding unavailable: " + refusal;
                }
            } catch { }
            return "Track binding applies to Generic track markers and crossing pads.";
        }
        void CloseCoverageForm() { OpenedForm(null); coverageCircuit = coverageAddition = null; preparedCoverage = null; newAddition = ""; for (var i = 0; i < coverageFields.Length; i++) coverageFields[i] = ""; }
        // The message that says a form is open. It leaves when the form closes or another one opens.
        string openedForm;
        void OpenedForm(string message) {
            if (openedForm != null) feedback.Withdraw(CoveragePlace, openedForm);
            openedForm = message;
            if (message != null) feedback.Report(CoveragePlace, message);
        }
        void DrawConnections(UIPanelBuilder builder) {
            var coverage = selected.Identity.Structure == CrossingCoverageScene.Structure;
            if (coverage) DrawCoverage(builder);
            // A crossing pad has the binding of its marker: setting it regenerates the pad along the track there.
            var pad = selected.Identity.Structure == CrossingPadScene.Structure;
            var marker = pad || selected.Identity.Structure == TrackMarkerScene.Structure;
            var block = marker ? EditBlock() : BindingUnavailable();
            Section(builder, pad ? "Crossing pad binding" : "Track binding");
            var current = selected.Bindings.Where(b => b.StartsWith("Marker", StringComparison.Ordinal) || b.StartsWith("Graph registration", StringComparison.Ordinal)).ToArray();
            if (current.Length == 0) Line(builder, "None", null, null);
            foreach (var line in current) BindingFields(builder, line);
            var labels = new[] { "Segment", "End (A or B)", "Distance (m)" };
            builder.HStack(fields => {
                for (var column = 0; column < 3; column++) {
                    var index = column;
                    fields.VStack(field => ValueField(field, labels[index], binding, index, block, pad ? "The track location this pad's marker is registered at. Setting it regenerates the pad along the track there; the pad object does not move." : "The track location this marker is registered at. A binding edit does not move the object.", index == 2)).EqualFlexibleWidth();
                }
            });
            Section(builder, "Replacement targets");
            var busy = !marker && !coverage ? "Replacement targets serve a track binding or a crossing's coverage." : selection == null ? "World selection is unavailable." : selection.Pending != null ? "Confirm or cancel the pending world choice first." : null;
            builder.HStack(row => {
                Button(row, "Object", () => BeginSelection(SelectionKind.SceneObject, selected)).Disable(busy != null).Tooltip("Replacement object", busy ?? "Choose a replacement object in the world and confirm its identity.");
                Button(row, "Segment", () => BeginSelection(SelectionKind.TrackSegment, selected)).Disable(busy != null).Tooltip("Replacement segment", busy ?? "Choose a track segment in the world and confirm its identity.");
                Button(row, "Node", () => BeginSelection(SelectionKind.TrackNode, selected)).Disable(busy != null).Tooltip("Replacement node", busy ?? "Choose a track node in the world and confirm its identity.");
            }).Height(RowHeight);
            Section(builder, "Observed bindings");
            var others = selected.Bindings.Where(line => !current.Contains(line)).ToArray();
            if (others.Length == 0) Line(builder, "None observed", null, null);
            foreach (var line in others) BindingFields(builder, line);
        }

        // The Changes tab: what this MapMod changes about the inspected object, and what that change still awaits.
        void DrawChanges(UIPanelBuilder builder) {
            Section(builder, "This MapMod");
            FieldMarkup(builder, "Object", IsChanged(selected) ? ChangedBadge : "Unchanged")
                .Tooltip("Changed", "Whether Data/scene-patches.json of the active MapMod holds a modification of this object.");
            Section(builder, "Terrain output");
            var problem = (string)null;
            var output = authoring == null || !Editable ? DependentOutput.None : inspectedOutput.Of(authoring.Session, selected, out problem);
            if (problem != null) builder.AddLabel(Tint(Red, "Terrain output unknown: " + problem));
            else if (output.Completed.Count == 0 && output.Settled) Line(builder, "None outstanding", null, null);
            else PlainLabel(builder, output.Error != null ? "Terrain output stopped: " + output.Error + ". Completed: " + Join(output.Completed) + ". Outstanding: " + Join(output.Outstanding) + ". The board edit is kept."
                : output.Settled ? "Terrain output complete: " + Join(output.Completed) + "."
                : "Terrain output outstanding; the game is regenerating it: " + Join(output.Outstanding) + ". Completed: " + Join(output.Completed) + ".");
        }

        // The footer: a status line, a target line and four action slots, each of fixed size. The slots hold the actions
        // of the open tab; a pending world choice or an active gizmo takes them over until it ends.
        struct Act { internal string Label, Tip, Reason; internal Action Run; }
        static Act Slot(string label, string tip, string reason, Action run) => new Act { Label = label, Tip = tip, Reason = reason, Run = run };
        static readonly Act Empty = new Act();
        void DrawFooter(UIPanelBuilder builder) {
            builder.AddHRule();
            // The first slot says what the window is waiting for or why the open tab cannot edit now.
            var pending = selection?.Pending;
            string source = null, state = "";
            if (queuedPlace != null) { source = queuedPlace; state = "Working..."; }
            else if (pending != null) {
                var candidate = selection.Find(pending.Candidate);
                source = "World choice";
                // A short purpose with the count on the first line, the candidate on its own line.
                var purpose = choosingSnap ? "Pick the snap target" : pending.Kind == SelectionKind.SceneObject ? "Pick an object" : pending.Kind == SelectionKind.TrackSegment ? "Pick a track" : "Pick an endpoint";
                state = (pending.Offers.Count > 1 ? Tint(Blue, pending.Offers.Count + " candidates; see Details") + " " : "") + Literal(purpose + " in the world.") +
                    (pending.Problem == null ? "" : " " + Tint(Red, pending.Problem)) + (inputBlocker.WorldInputProblem == null ? "" : " " + Tint(Red, inputBlocker.WorldInputProblem)) +
                    (selectionProblem == null ? "" : " " + Tint(Red, selectionProblem)) +
                    (candidate == null ? "" : "\n" + Tint(Blue, "New: " + Tail(candidate.Choice.Label, 60)));
            } else if (gizmo?.Active == true) { source = GizmoPlace; state = Literal(gizmo.State); }
            else if (selectionProblem != null) { source = "World choice"; state = Tint(Red, selectionProblem); }
            else if (preparedSnap != null) { source = SnapPlace; state = Tint(Blue, "Snap target: " + preparedSnap.Identity); }
            // A confirmed target belongs to the object it was chosen for; another object does not show it.
            else if (selected != null && selection?.Confirmed is ConfirmedSelection confirmed && confirmed.Source != null && confirmed.Source.Equals(SelectionIdentity.SceneObject(selected.Identity))) {
                source = "World choice"; state = Tint(Green, "Confirmed: " + confirmed.Identity + " in " + confirmed.MapIds[0]);
            } else if (selected != null && EditBlock() is string block) { source = "Editing"; state = Tint(Amber, block); }
            // The area has a fixed height, so nothing outside it moves, and its entries stack from the top without gaps:
            // first what the window waits for now, then the messages, newest first, the older ones dimmed. Each names
            // the action group it came from. The area is a native scroll view: its content takes the height of its
            // entries, so an entry that wraps has its wrapped height, and its viewport cuts off what does not fit. A
            // stack of fixed height would instead squeeze its entries below their text, and the text would overprint.
            // What lies below the area's edge is reached by scrolling; Details lists every message as well.
            var messages = feedback.Messages;
            presentation.Scroll(builder, 3 * MessageHeight, lines => {
                if (!String.IsNullOrEmpty(state)) MessageSlot(lines, source, state, false);
                for (var i = 0; i < messages.Count; i++) {
                    var message = messages[i]; var older = i > 0;
                    var tone = ToneOf(message.Text);
                    var text = WithoutSource(message);
                    MessageSlot(lines, message.Source, tone == null ? Literal(text) : Tint(tone + (older ? "80" : ""), text), older);
                }
            }, false).Height(3 * MessageHeight);
            var slots = Slots();
            builder.HStack(row => {
                foreach (var slot in slots) {
                    var act = slot;
                    // Four cells of one fixed width, from the left. A native compact button sizes itself to its text; here
                    // it takes its cell's width, so another label changes no rectangle. The cells do not stretch with
                    // the divider or the window, and the space at the right keeps them clear of the resize grip.
                    row.VStack(cell => {
                        if (act.Label == null) return;
                        var button = Button(cell, act.Label, act.Run);
                        button.Disable(act.Reason != null).Tooltip(act.Label, act.Reason ?? act.Tip);
                        var fitter = button.RectTransform.GetComponent<ContentSizeFitter>();
                        if (fitter != null) fitter.enabled = false;
                        var layout = button.RectTransform.GetComponent<LayoutElement>();
                        if (layout != null) { layout.minWidth = 0; layout.preferredWidth = -1; layout.flexibleWidth = 1; }
                    }).Width(SlotWidth);
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
            if (text.IndexOf("outstanding", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("the column stops", StringComparison.OrdinalIgnoreCase) >= 0) return Amber;
            return null;
        }
        // The end of a long text, with a leading ellipsis.
        static string Tail(string text, int length) => text == null || text.Length <= length ? text : "..." + text.Substring(text.Length - length);
        void CancelSelection() { selection.Cancel(); selectionProblem = null; ClearSnap(); }
        Act[] Slots() {
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
                    Slot("Details", "Show the candidate's identity and the other candidates at this point.", null, () => bodyView = BodyView.Session)
                };
            }
            if (gizmo?.Active == true) return new[] { Slot("Cancel", "Cancel the gizmo and restore the placement.", null, CancelGizmo), Empty, Empty, Empty };
            var block = selected == null ? "Inspect an object first." : EditBlock();
            var identity = selected?.Identity;
            switch (inspectorTab.Value) {
                case PlacementTab:
                    if (placementMode == 0) return new[] {
                        Slot("Apply", "Apply the entered position and rotation. Placement does not change a track binding.", block, ApplyPlacement),
                        Slot("Read", "Fill the fields with the inspected values.", block, ReadPose), Empty, Empty };
                    if (placementMode == 1) return new[] {
                        Slot("Move", "Move the object with the native gizmo.", block, () => BeginGizmo(UI.Gizmos.TransformGizmoMode.Move)),
                        Slot("Rotate", "Rotate the object with the native gizmo.", block, () => BeginGizmo(UI.Gizmos.TransformGizmoMode.Rotate)), Empty, Empty };
                    if (placementMode == 2) {
                        var unavailable = block ?? (snap == null ? "Snapping is unavailable." : null);
                        var unprepared = unavailable ?? (preparedSnap == null ? "Choose a track or endpoint and confirm it first." : null);
                        return new[] {
                            Slot("Track", "Choose the track to snap to.", unavailable, () => BeginSnap(SelectionKind.TrackSegment)),
                            Slot("Node", "Choose the track node, an endpoint, to snap to.", unavailable, () => BeginSnap(SelectionKind.TrackNode)),
                            Slot("Apply", "Move the object once to the snap position. Rotation and binding stay.", unprepared, () => {
                                var request = preparedSnap;
                                Edit(SnapPlace, "Physical snap", (session, inspection) => snap.Apply(request));
                            }),
                            Slot("Clear", "Discard the prepared snap.", unprepared, ClearSnap) };
                    } else {
                        var unavailable = block ?? (trackPoses == null ? "Track-following placement is unavailable in this game build." : null);
                        var picked = FollowTarget();
                        TrackFollowing following = null;
                        if (unavailable == null) try { following = authoring.Session.FollowingOf(identity); } catch { }
                        return new[] {
                            Slot("Track", "Choose the track to follow.", unavailable, () => BeginSelection(SelectionKind.TrackSegment, selected)),
                            Slot("Node", "Choose the track node, an endpoint, to follow.", unavailable, () => BeginSelection(SelectionKind.TrackNode, selected)),
                            Slot("Follow", "Keep the current pose relative to the picked track or endpoint.", unavailable ?? (picked == null ? "Choose a track or endpoint and confirm it first." : null), () => {
                                var maps = picked.MapIds.ToArray();
                                Edit(FollowPlace, "Track following", (current, inspection) => {
                                    if (!maps.SequenceEqual(capabilities.MapIds)) throw new InvalidOperationException("Map context changed; select the track again");
                                    var anchor = picked.Identity.Kind == SelectionKind.TrackNode ? TrackAnchor.Endpoint(picked.Identity.Identifier)
                                        : TrackAnchor.At(trackPoses.Nearest(picked.Identity.Identifier, ((GameObject)selected.Handle).transform.position));
                                    return current.Follow(identity, anchor, inspection);
                                });
                            }),
                            Slot("Stop", "Stop following and keep the current pose.", unavailable ?? (following == null ? "This object does not follow a track." : null),
                                () => Edit(FollowPlace, "Stop following", (current, inspection) => current.StopFollowing(identity, inspection))) };
                    }
                case ConnectionsTab: {
                    if (block == null && identity.Structure == CrossingCoverageScene.Structure) {
                        var session = authoring.Session;
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
                        });
                        var preview = Slot("Preview", "Preview the selected tracks for every circuit.", none, () => {
                            var value = ParseCoverage(out var problem, additionId != null);
                            if (value == null) feedback.Refuse(CoveragePlace, "Coverage preview", problem); else PrepareCoverage(session, identity, circuitKey, value, additionId);
                        });
                        if (additionId != null) {
                            // Deleting applies to an addition the document holds; a crossing keeps at least one detection.
                            var authored = current != null && current.Circuits.Any(c => c.Value.Kind == CircuitKind.Added && DetectionAddition.Id(c.Key) == additionId);
                            return new[] { preview,
                                Slot("Delete", "Preview deleting this detection addition.", !authored ? "This addition is not in the document yet." : last ? "This is the crossing's last detection; return a native circuit to its coverage first." : null, () => PrepareCoverage(session, identity, circuitKey, null, additionId)),
                                Empty,
                                Slot("Apply", "Apply the previewed coverage.", prepared == null ? "Preview a change first." : null, apply) };
                        }
                        return new[] { preview,
                            Slot("Detach", "Preview detaching this circuit.", none ?? (last ? "This is the crossing's last covered circuit; detaching it would leave the crossing without detection." : null), () => PrepareCoverage(session, identity, circuitKey, CircuitCoverage.Detached, null)),
                            Slot("Clear", "Preview clearing this MapMod's value for the circuit; it returns to its underlying coverage.", none, () => PrepareCoverage(session, identity, circuitKey, null, null)),
                            Slot("Apply", "Apply the previewed coverage.", none ?? (prepared == null ? "Preview a change first." : null), apply) };
                    }
                    var pad = identity.Structure == CrossingPadScene.Structure;
                    // An object without a binding names that first; why it cannot be edited in general comes second.
                    var unavailable = pad || identity.Structure == TrackMarkerScene.Structure ? block : BindingUnavailable();
                    var confirmed = selection?.Confirmed?.Identity;
                    var segment = confirmed != null && confirmed.Kind == SelectionKind.TrackSegment;
                    return new[] {
                        Slot("Set", pad ? "Register the pad's marker at the entered track location and regenerate the pad there." : "Register the marker at the entered track location. The object does not move.", unavailable, ApplyBinding),
                        Slot("Detach", "Detach the marker from its track.", unavailable ?? (pad ? "A crossing pad cannot be detached. Disable it on the Object tab to hide it." : null), () => Edit(BindingPlace, "Track detachment", (current, inspection) => current.Bind(identity, MarkerBinding.Detached, inspection))),
                        Slot("Clear", "Clear this MapMod's value for the binding; the marker returns to its underlying binding.", unavailable, () => Edit(BindingPlace, "Clearing the binding's MapMod value", (current, inspection) => current.Bind(identity, null, inspection))),
                        Slot("Picked", "Fill the segment field with the confirmed segment.", unavailable ?? (segment ? null : "Choose a replacement segment and confirm it first."), () => { binding[0] = confirmed.Identifier; }) };
                }
                case ChangesTab: {
                    var unchanged = block ?? (IsChanged(selected) ? null : "This MapMod does not change this object.");
                    var idle = block;
                    if (idle == null) { var output = inspectedOutput.Of(authoring.Session, selected, out _); if (output.Outstanding.Count == 0) idle = "No terrain output is outstanding."; }
                    return new[] {
                        Slot("Remove", selected != null && selected.Identity.Structure == CrossingCoverageScene.Structure ? "Remove this MapMod's modification. Every circuit returns to its coverage without it, and its added detection is deleted." : "Remove this MapMod's modification. The object returns to its underlying state.", unchanged, () => Edit(ModificationPlace, "Removal", (current, inspection) => {
                            var removed = current.Remove(identity, inspection);
                            // Removal deletes the crossing's additions and returns its circuits: a form that was open for one closes.
                            if (removed?.Applied == true) CloseCoverageForm();
                            return removed;
                        })),
                        Slot("Recheck", "Recheck the object after a failed edit.", block, () => Edit(ModificationPlace, "Recheck", (current, inspection) => current.Recheck(identity, inspection))),
                        Slot("Retry", "Ask the game to rebuild the outstanding terrain output again.", idle, () => {
                            var outcome = authoring.Session.RetryOutput(identity);
                            feedback.Report(TerrainPlace, outcome.Applied ? "Terrain output requested again." : "Terrain output retry refused: " + outcome.Error);
                        }), Empty };
                }
                default: {
                    var target = selected?.Handle as GameObject;
                    var enabled = target != null && target.activeSelf;
                    return new[] {
                        Slot(enabled ? "Disable" : "Enable", enabled ? "Disable this object." : "Enable this object.", block, () => { var enable = !enabled; Edit(ActivityPlace, enable ? "Enable" : "Disable", (current, inspection) => current.Edit(identity, null, enable, inspection)); }),
                        Slot("Clear", "Clear this MapMod's value for the activity; the object returns to its underlying activity.", block, () => Edit(ActivityPlace, "Clearing the activity's MapMod value", (current, inspection) => current.ClearActivity(identity, inspection))),
                        Empty, Empty };
                }
            }
        }
        void ApplyPlacement() {
            var identity = selected.Identity;
            var values = new float[6]; var valid = true;
            for (var i = 0; i < 6; i++) valid &= Single.TryParse(pose[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
            if (!valid) { feedback.Refuse(PlacementPlace, "Placement", "enter six numbers with a decimal point"); return; }
            var rotation = Quaternion.Euler(values[3], values[4], values[5]);
            var placement = new Placement(new double[] { values[0], values[1], values[2] }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w });
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
            Edit(BindingPlace, "Track binding", (current, inspection) => current.Bind(identity, value, inspection));
        }

        // The session details, behind the band's state buttons: everything that does not belong to one object.
        void DrawSession(UIPanelBuilder builder) {
            builder.HStack(row => {
                Button(row, "Back", () => bodyView = BodyView.Tabs).Tooltip("Back", "Return to the tab.");
                Heading(row, "Session details", null, null).FlexibleWidth();
            }).ChildrenKeepHeight().Height(RowHeight);
            var pending = selection?.Pending;
            if (pending != null) {
                Section(builder, "World choice");
                PlainLabel(builder, (choosingSnap ? "Select physical snap target" : pending.Purpose) + ".");
                if (pending.Source != null) PlainLabel(builder, "Object: " + pending.Source.Label);
                if (pending.Problem != null) builder.AddLabel(Tint(Red, pending.Problem));
                var candidate = selection.Find(pending.Candidate);
                if (candidate != null) {
                    builder.AddLabel(Tint(Blue, "New: " + candidate.Choice.Label));
                    PlainLabel(builder, candidate.Choice.Identity.ToString());
                    PlainLabel(builder, candidate.Details);
                    Button(builder, "Inspect candidate", () => {
                        if (candidate.Observation == null) return;
                        CancelGizmo(); InspectObservation(candidate.Observation); ReadPose();
                    }).Disable(candidate.Observation == null).Tooltip("Inspect candidate", candidate.Observation == null ? "Only a scene object can be inspected." : "Inspect the candidate without confirming it.");
                }
                foreach (var offer in pending.Offers) {
                    if (ReferenceEquals(offer, pending.Candidate)) continue;
                    var choice = offer;
                    Button(builder, "Choose " + Tail(choice.Label, 34), () => pending.Choose(choice)).Tooltip("Choose", choice.Label);
                }
            }
            if (feedback.Messages.Count != 0) {
                Section(builder, "Messages");
                foreach (var message in feedback.Messages) {
                    var tone = ToneOf(message.Text);
                    builder.AddLabel("<b>" + Literal(message.Source) + "</b> " + (tone == null ? Literal(WithoutSource(message)) : Tint(tone, WithoutSource(message))));
                }
            }
            Section(builder, "Inventory");
            PlainLabel(builder, status);
            var notice = InventoryPresentationState.Notice(inventory, definitionsStale);
            if (inventory == null || inventory.IsStale || definitionsStale) builder.AddLabel(Tint(Amber, notice)); else PlainLabel(builder, notice);
            if (scene != null) PlainLabel(builder, scene.Status);
            Section(builder, "Save");
            Unsaved(out var saveText);
            PlainLabel(builder, saveText);
            if (!String.IsNullOrEmpty(authoringStatus)) PlainLabel(builder, authoringStatus);
            if (authoring != null) {
                var session = authoring.Session;
                if (session.LastSave != null && session.LastSave.Error != null) builder.AddLabel(Tint(Red, "Last Save failed: " + session.LastSave.Error + " Completed: " + (session.LastSave.Completed.Count == 0 ? "none" : String.Join(", ", session.LastSave.Completed)) + ". Outstanding: " + String.Join(", ", session.LastSave.Outstanding) + ". Edits remain in memory; correct the cause and Save again."));
                if (session.IncompleteOperation != null) builder.AddLabel(Tint(Red, "Incomplete operation: " + session.IncompleteOperation));
                if (authoring.Fault != null) builder.AddLabel(Tint(Red, authoring.Fault));
            }
            Section(builder, "Supplied definitions");
            if (String.IsNullOrEmpty(documentInspection)) Line(builder, definitionsStale ? "Not read yet; select Refresh" : "None", null, null);
            else PlainLabel(builder, documentInspection);
            Section(builder, "Diagnostics");
            if (diagnostics.Count == 0 && presentation.EntryProblem == null) Line(builder, "None", null, null);
            if (presentation.EntryProblem != null) builder.AddLabel(Tint(Amber, presentation.EntryProblem));
            foreach (var diagnostic in diagnostics) PlainLabel(builder, diagnostic);
        }

        // Context help of one region: an introduction, short terms and the guides. It takes the place of that region's
        // content; nothing else moves.
        sealed class HelpText {
            internal string Title, Intro;
            internal string[][] Terms, Guides;
        }
        static readonly Dictionary<string, HelpText> Help = new Dictionary<string, HelpText> {
            ["main"] = new HelpText { Title = "Scene patches", Intro = "This help applies to the whole Scene patches window. Edit scene objects of the active MapMod here, then save through the Map Editor.",
                Terms = new[] {
                    new[] { "Save", "The editor's Save button writes map data, Data/scene-patches.json and the patcher dependency in Info.json. Save never reloads." },
                    new[] { "Undo and Redo", "Every completed edit here is one step in the editor's own history: use the editor's Undo and Redo." },
                    new[] { "Refresh", "Reads the scene again and applies nothing. Refresh after a native edit, Undo or Redo; a stale inventory refuses edits." },
                    new[] { "Editor reload", "Reloading the editor ends the Undo and Redo history. Leaving the editor discards unsaved work." },
                    new[] { "Current, Stale, Saved, Unsaved", "The two lamps above the outliner show the inventory and save state: green when good, amber otherwise. They are not buttons. Select Details for the reasons, the messages and the diagnostics." },
                    new[] { "* and +", "Before a name, * marks an object this MapMod changes and + an object it adds; the state line shows the same as CHANGED and ADDED. Neither means unsaved: only the Unsaved lamp does." },
                    new[] { "Failed edit", "A failed edit restores the object. If that cannot be verified, the object is blocked until Recheck succeeds or the editor reloads." } },
                Guides = new[] { new[] { "Editing in the editor", "editing-placement-in-the-editor.md" }, new[] { "Document reference", "scene-patch-document-v1.md" } } },
            ["outliner"] = new HelpText { Title = "Outliner", Intro = "Find an object and inspect it here, or select it in the world.",
                Terms = new[] {
                    new[] { "Search", "Matches names, paths, families, binding identifiers, dependencies and problems." },
                    new[] { "Problems", "Objects with a detected problem, marked !. No detected problem does not prove correct placement or the intended track." },
                    new[] { "Changed", "Objects this MapMod changes, marked *." },
                    new[] { "Dimmed rows", "Loaded objects that are inactive." },
                    new[] { "Focus", "Moves the camera. It selects nothing." },
                    new[] { "Fit names", "Sizes the column to most names. Drag the divider for another width." } },
                Guides = new[] { new[] { "Inspecting objects", "inspect-scene-objects.md" }, new[] { "Selecting in the world", "select-world-targets.md" } } },
            [ObjectTab] = new HelpText { Title = "Object", Intro = "Check the object's state, problems and dependencies before choosing an edit.",
                Terms = new[] {
                    new[] { "Supported edits", "What this patcher can change on this object. Other objects are inspection only." },
                    new[] { "Enabled", "Whether the object is active. Disable holds it inactive; Clear returns it to its underlying activity." },
                    new[] { "Track group", "Some markers switch with their track group. For those, an explicit enable is refused." },
                    new[] { "Dependencies", "Culling, masks, graph registration and other state that follows an edit. Indirect references are not inspected." },
                    new[] { "Runtime state", "Enabled flags the game switches while it runs. They are shown and do not make the inventory stale." },
                    new[] { "Copy ID", "Copies the identity as patch-document JSON." } },
                Guides = new[] { new[] { "Inspecting objects", "inspect-scene-objects.md" }, new[] { "Following a track and group activity", "following-track.md" } } },
            [PlacementTab] = new HelpText { Title = "Placement", Intro = "Move or rotate the object without changing its logical binding.",
                Terms = new[] {
                    new[] { "Numeric", "Position in map coordinates, rotation in degrees." },
                    new[] { "Gizmo", "Drag the native Move or Rotate gizmo. Release applies; Escape or Cancel restores." },
                    new[] { "Snap", "Moves the object once onto a chosen track or endpoint. Rotation and binding stay; Save records fixed coordinates." },
                    new[] { "Follow", "Keeps the current pose relative to a chosen track or endpoint, and moves the object when that track moves." },
                    new[] { "Bridges and boards", "A bridge moves with its colliders; track-owned scenery stays. A board's terrain output regenerates after the move." } },
                Guides = new[] { new[] { "Fixed placement", "authoring-fixed-placement.md" }, new[] { "Transform gizmos", "using-transform-gizmos.md" }, new[] { "Snapping", "snapping-to-track.md" },
                    new[] { "Following a track", "following-track.md" }, new[] { "Bridges", "placing-bridges.md" }, new[] { "Boards", "moving-boards.md" } } },
            [ConnectionsTab] = new HelpText { Title = "Connections", Intro = "Choose which track the object uses, then set the connection.",
                Terms = new[] {
                    new[] { "Track binding", "The track location a marker is registered at. Changing it does not move the object." },
                    new[] { "Replacement targets", "Choose a target in the world, check its ID and confirm it. Confirming changes nothing." },
                    new[] { "Set, Detach, Clear", "Set registers the marker at the entered location. Detach removes it from the track. Clear returns it to its underlying binding." },
                    new[] { "Crossing coverage", "Each circuit has an approach A, an island and an approach B. Select the tracks a circuit covers, or detach it; proximity never assigns coverage. Preview shows the result for every circuit before Apply." },
                    new[] { "Detection addition", "Makes the crossing detect an extra track: a circuit of its own with three spans on tracks you select, under an ID you give it. Delete removes it again." },
                    new[] { "Crossing pad", "A pad has the binding of its marker. Setting it regenerates the pad geometry and its culling along the track at that location; the pad object does not move. A pad cannot be detached: disable it to hide it." },
                    new[] { "Other families", "Mileposts have their own operation." } },
                Guides = new[] { new[] { "Track bindings", "repairing-track-bindings.md" }, new[] { "Crossing pads", "rebinding-crossing-pads.md" }, new[] { "Crossing coverage", "correcting-crossing-coverage.md" }, new[] { "Selecting in the world", "select-world-targets.md" } } },
            [ChangesTab] = new HelpText { Title = "Changes", Intro = "Check what this MapMod changes on the object and complete outstanding work.",
                Terms = new[] {
                    new[] { "Remove", "Deletes this MapMod's modification. The object returns to its underlying state, not to an earlier patched position." },
                    new[] { "Recheck", "After a failed edit, compares the object with the document and unblocks it once they agree." },
                    new[] { "Terrain output", "The game regenerates terrain after a board move. Save does not write that output. Retry asks for it again." } },
                Guides = new[] { new[] { "Editing in the editor", "editing-placement-in-the-editor.md" }, new[] { "Boards", "moving-boards.md" } } },
        };
        void DrawHelp(UIPanelBuilder builder, string region, Action back) {
            var help = Help[region];
            builder.HStack(row => {
                Button(row, "Back", back).Tooltip("Back", "Close this help.");
                if (region == "main") Button(row, "Reset layout", () => { outlinerWidth = OutlinerDefaultWidth; ForgetOutlinerWidth(); presentation.ResetLayout(); })
                    .Tooltip("Reset layout", "Put the window below the Map Editor window again and restore the outliner width.");
            }).Height(RowHeight);
            builder.AddLabel("<style=H3>" + help.Title + "</style>");
            builder.AddLabel(help.Intro);
            foreach (var term in help.Terms) builder.AddLabel("<b>" + term[0] + "</b> " + term[1]);
            Section(builder, "Guides");
            foreach (var guide in help.Guides) {
                var document = guide[1];
                Button(builder, guide[0], () => Application.OpenURL(GuideRoot + document)).Tooltip(guide[0], "Open this guide in the browser.");
            }
        }
    }
}
