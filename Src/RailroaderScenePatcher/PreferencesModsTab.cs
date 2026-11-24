// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using TMPro;
using UI.Builder;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The Scene Patcher section in the Mods tab of the game's Preferences. The game has no Mods tab of its own: mods that
    // offer settings share one through a convention. Each adds the drawing of its section to a list in the AppDomain data
    // slot below and patches PreferencesBuilder.BuildTabs; the mod whose section is first in the list adds the tab, which
    // draws every section in list order. The patcher adds the tab itself when no other mod has, and depends on none.
    // BuildTabs is an internal game function: when it is missing the section is unavailable and the log stays in player mode.
    internal static class PreferencesModsTab {
        const string Id = "crankyant.scene-patcher.preferences";
        const string Slot = "ashenmare.preferences-mods.v1";
        const string TabId = "ashenmare.mods";
        static Harmony harmony;
        static Action<UIPanelBuilder> section;
        static DebugOption option;
        static readonly UIState<bool> expanded = new UIState<bool>(true);
        internal const string Title = "Scene Patcher";
        internal const string DebugLabel = "Debug logging";
        internal const string DebugTooltip = "Adds detail to the Player log: load steps, the result of each scene patch file, edits, saves and diagnostics. Turn it on before you reproduce a problem you report, and off again afterward.";
        // Installs the section and returns the debug option, which is unavailable when the Preferences hook is incompatible.
        internal static DebugOption Install(string modDirectory) {
            var available = false;
            var patcher = new Harmony(Id);
            try {
                var target = AccessTools.Method(typeof(UI.PreferencesWindow.PreferencesBuilder), "BuildTabs", new[] { typeof(UITabbedPanelBuilder) });
                if (target == null || target.ReturnType != typeof(void)) throw new MissingMethodException("PreferencesBuilder.BuildTabs");
                patcher.Patch(target, postfix: new HarmonyMethod(typeof(PreferencesModsTab), nameof(AfterBuildTabs)));
                section = DrawSection;
                var list = Sections(true);
                lock (list) list.Add(section);
                harmony = patcher;
                available = true;
            } catch (Exception e) {
                try { patcher.UnpatchAll(Id); } catch { }
                section = null;
                PatcherLog.Warning("The debug option is unavailable because the patcher can't add its section to the Mods tab in Preferences", e);
            }
            return option = new DebugOption(new SettingsFile(Path.Combine(modDirectory, DebugOption.FileName)), available);
        }
        internal static void Remove() {
            try {
                var list = Sections(false);
                if (list != null && section != null) lock (list) list.Remove(section);
            } catch { }
            try { harmony?.UnpatchAll(Id); } catch { }
            harmony = null; section = null; option = null;
        }
        static List<Action<UIPanelBuilder>> Sections(bool create) {
            var domain = AppDomain.CurrentDomain;
            var list = domain.GetData(Slot) as List<Action<UIPanelBuilder>>;
            if (list == null && create) { list = new List<Action<UIPanelBuilder>>(); domain.SetData(Slot, list); }
            return list;
        }
        static void AfterBuildTabs(UITabbedPanelBuilder builder) {
            try {
                var list = Sections(false);
                if (list == null || section == null) return;
                lock (list) if (list.Count == 0 || !ReferenceEquals(list[0], section)) return;
                builder.AddTab("Mods", TabId, DrawTab);
            } catch (Exception e) { PatcherLog.Warning("The patcher couldn't add the Mods tab to Preferences", e); }
        }
        static void DrawTab(UIPanelBuilder builder) {
            builder.VScrollView(scroll => {
                scroll.ConfigureLayout(null, new RectOffset(0, 4, 0, 0), null);
                scroll.AddLabel("<style=Footnote>Settings for the mods you have that offer them, saved on this computer.</style>");
                var list = Sections(false);
                if (list == null) return;
                Action<UIPanelBuilder>[] sections;
                lock (list) sections = list.ToArray();
                foreach (var draw in sections) {
                    try { draw(scroll); }
                    catch (Exception e) { PatcherLog.Warning("A mod's section in the Mods tab of Preferences couldn't be drawn", e); }
                }
            });
        }
        static void DrawSection(UIPanelBuilder builder) {
            var current = option;
            if (current == null) return;
            builder.AddExpandable(Title, expanded, content => {
                content.HStack(row => {
                    row.Spacer(12f);
                    row.AddToggle(() => current.On, on => {
                        try { current.Set(on); }
                        catch (Exception e) { PatcherLog.Error("The debug option couldn't be changed", e); }
                    });
                    row.Spacer(8f);
                    row.AddLabel(DebugLabel, text => { text.fontStyle |= FontStyles.Bold; text.alignment = TextAlignmentOptions.Left; }).FlexibleWidth();
                }).Tooltip(DebugLabel, DebugTooltip);
            }, ExpandableStyle.SectionHeader);
        }
    }

    // The patcher's settings file in its own mod folder. The release package ships none; the patcher writes it the first
    // time a setting changes, through a temporary file so a failed write leaves the earlier file whole.
    internal sealed class SettingsFile : ISettingsStore {
        readonly string path;
        internal SettingsFile(string path) { this.path = path; }
        public string Read() => File.Exists(path) ? File.ReadAllText(path) : null;
        public void Write(string text) {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
