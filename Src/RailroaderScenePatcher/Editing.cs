// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using UI.Builder;

namespace RailroaderScenePatcher {
    // The native map editor's Save button stays enabled with its own unsaved tint and tooltip. Its click callback writes the
    // edited terrain before MapEditor.Save, so a Save refusal runs before that callback: it reports the patcher's reason in the
    // scene patch editing feedback, or in a native alert while the Scene patches window is closed, and skips the callback,
    // which leaves no native Save Failed alert. Without this hook the edited terrain tiles are already written when the
    // refusal stops the map write from the Save prefix, and the refusal reaches the native Save Failed alert.
    internal static class NativeSaveClick {
        const string Id = "crankyant.scene-patcher.save-click";
        static Harmony harmony;
        static FieldInfo mapEditorField;
        // Why the click hook is not installed, for the log only, or null.
        internal static string Problem { get; private set; }
        static IEnumerable<Type> Nested(Type type) {
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) {
                yield return nested;
                foreach (var child in Nested(nested)) yield return child;
            }
        }
        static bool CallsSave(MethodBase method, MethodInfo save) {
            try { return PatchProcessor.GetOriginalInstructions(method).Any(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && Equals(i.operand, save)); }
            catch { return false; }
        }
        internal static void Install() {
            if (harmony != null || Problem != null) return;
            var patcher = new Harmony(Id);
            try {
                var window = Native.Type("Game.MapEditor.MapEditorWindow") ?? throw new MissingMemberException("Native map editor window unavailable");
                var mapEditor = Native.Type("Game.MapEditor.MapEditor") ?? throw new MissingMemberException("Native map editor unavailable");
                var save = mapEditor.GetMethod("Save", Native.Flags, null, Type.EmptyTypes, null) ?? throw new MissingMethodException("Native map editor Save unavailable");
                var candidates = Nested(window).SelectMany(t => t.GetMethods(Native.Flags | BindingFlags.DeclaredOnly))
                    .Where(m => !m.IsStatic && m.GetParameters().Length == 0 && m.GetMethodBody() != null && CallsSave(m, save)).ToArray();
                if (candidates.Length != 1) throw new MissingMethodException("Native Save click could not be identified uniquely");
                var fields = candidates[0].DeclaringType.GetFields(Native.Flags).Where(f => !f.IsStatic && f.FieldType == mapEditor).ToArray();
                if (fields.Length != 1) throw new MissingMemberException("Native Save click Map Editor identity unavailable");
                mapEditorField = fields[0];
                patcher.Patch(candidates[0], prefix: new HarmonyMethod(typeof(NativeSaveClick).GetMethod(nameof(BeforeClick), Native.Flags)));
                harmony = patcher;
            } catch (Exception e) {
                try { patcher.UnpatchAll(Id); } catch { }
                mapEditorField = null;
                Problem = "The patcher could not add its checks to the Save button. Refused saves will show the game's Save Failed alert. Details: " + e.GetBaseException().Message;
            }
        }
        internal static void Remove() { harmony?.UnpatchAll(Id); harmony = null; mapEditorField = null; }
        static bool BeforeClick(object __instance) {
            var current = Hooks.Current;
            if (current == null || !ReferenceEquals(mapEditorField?.GetValue(__instance), current.MapEditor)) return true;
            string refusal;
            try { refusal = current.Session.SaveRefusalBeforeWrite(); }
            catch (Exception e) { refusal = "The scene patch file could not be checked before saving: " + e.GetBaseException().Message; }
            if (refusal == null) return true;
            try { current.Log("Save refused: " + refusal); current.SaveRefused(refusal); }
            finally { current.VersionChanged(); current.RefreshChrome(); }
            return false;
        }
    }
    // The native map editor's own undo manager, read through the internal members this capability checks.
    internal sealed class NativeHistory : INativeHistory, INativeHistoryActions {
        readonly object manager;
        readonly FieldInfo undo, redo;
        readonly MethodInfo register;
        readonly Type groupType;
        readonly PropertyInfo actions;
        internal NativeHistory(object manager) {
            this.manager = manager;
            var type = manager.GetType();
            undo = type.GetField("_undoStack", Native.Flags); redo = type.GetField("_redoStack", Native.Flags);
            register = type.GetMethod("RegisterUndo", new[] { typeof(Action), typeof(string) });
            groupType = type.GetNestedType("UndoGroup", BindingFlags.Public | BindingFlags.NonPublic);
            actions = groupType?.GetProperty("Actions", BindingFlags.Public | BindingFlags.Instance);
            if (undo == null || redo == null || register == null || !(undo.GetValue(manager) is IEnumerable) || !(redo.GetValue(manager) is IEnumerable))
                throw new MissingMemberException("Native undo history members unavailable");
            if (groupType == null || actions == null || !typeof(IEnumerable<Action>).IsAssignableFrom(actions.PropertyType))
                throw new MissingMemberException("Native undo group actions unavailable");
            foreach (var group in ((IEnumerable)undo.GetValue(manager)).Cast<object>().Concat(((IEnumerable)redo.GetValue(manager)).Cast<object>())) GroupActions(group);
        }
        // The native stacks enumerate newest first.
        static IReadOnlyList<object> Oldest(object stack) { var items = ((IEnumerable)stack).Cast<object>().ToList(); items.Reverse(); return items; }
        public IReadOnlyList<object> UndoGroups => Oldest(undo.GetValue(manager));
        public IReadOnlyList<object> RedoGroups => Oldest(redo.GetValue(manager));
        public IReadOnlyList<Action> GroupActions(object group) {
            if (group == null || !groupType.IsInstanceOfType(group) || !(actions.GetValue(group) is IEnumerable<Action> values))
                throw new InvalidOperationException("Native undo group actions unavailable");
            return values.ToArray();
        }
        public void Register(Action inverse, string name) { register.Invoke(manager, new object[] { inverse, name }); }
    }
    // One attached native map editor: its session, history and files.
    internal sealed class MapEditorAttachment : IDisposable {
        internal readonly object MapEditor, Manager;
        internal readonly PatchEditingSession Session;
        internal readonly string PatchPath;
        internal string Fault;
        internal Action<string> Log = _ => { };
        internal Action VersionChanged = () => { };
        internal Action<string> SaveRefused = _ => { };
        internal string Outstanding() { var list = Session.Outstanding(NativeDirty); return list.Count == 0 ? "none" : String.Join(", ", list.Select(PatchEditingSession.DisplayItem)); }
        readonly EventInfo changed;
        readonly Action handler;
        readonly NativeDirtyTracker nativeDirty;
        int ChangeCount => Convert.ToInt32(Native.Read(Manager, "ChangeCount"));
        internal MapEditorAttachment(PatchEngine engine, object mapEditor) {
            MapEditor = mapEditor; Manager = Native.Read(mapEditor, "UndoManager");
            if (Manager == null) throw new MissingMemberException("Native undo manager unavailable");
            Native.Read(mapEditor, "_cleanChangeCount"); Native.Read(mapEditor, "_upgradedOnLoad"); Native.Read(Manager, "ChangeCount");
            var storage = ModScenePatchFiles.ActiveStorage(); PatchPath = storage.PatchPath;
            var history = new NativeHistory(Manager);
            Session = new PatchEditingSession(engine, history, storage, ModScenePatchFiles.PatcherId, ModScenePatchFiles.EditedModId());
            var cleanCount = Convert.ToInt32(Native.Read(MapEditor, "_cleanChangeCount"));
            nativeDirty = new NativeDirtyTracker(history, ChangeCount, ChangeCount != cleanCount, cleanCount);
            changed = Manager.GetType().GetEvent("DidChange", Native.Flags);
            if (changed == null || changed.EventHandlerType != typeof(Action)) throw new MissingMemberException("Native history notification unavailable");
            // The native controller rebuilds the title and Save button on this notification before this handler accounts for
            // the change, so a patch-only edit refreshes them again. Undo and Redo refresh once their history identity settled.
            Session.OutstandingMayHaveChanged = RefreshChrome;
            handler = () => {
                try { Session.HistoryChanged(); var ignored = NativeDirty; }
                catch (Exception e) { Fault = "Could not track a change to the Map Editor history: " + e.GetBaseException().Message; }
            };
            changed.AddEventHandler(Manager, handler);
        }
        // The native map editor values every change of the outstanding work passes through: an edit, Undo and Redo move the change
        // count, a completed Save moves the clean count.
        internal string ChangeKey => Native.Read(MapEditor, "_upgradedOnLoad") + "|" + Native.Read(Manager, "ChangeCount") + "|" + Native.Read(MapEditor, "_cleanChangeCount");
        // Tracks native work separately from patch-only history and retains unexplained count changes until Save.
        internal bool NativeDirty {
            get {
                var initialPending = nativeDirty.InitialDirtyPending;
                var dirty = nativeDirty.Observe(ChangeCount, (bool)Native.Read(MapEditor, "_upgradedOnLoad"));
                if (initialPending && !nativeDirty.InitialDirtyPending) Session.AlignNativeCleanHistory();
                return dirty;
            }
        }
        internal void BeginHistoryExecution() { nativeDirty.BeginHistoryExecution(); Session.BeginHistoryExecution(); }
        internal void EndHistoryExecution(Exception error) { try { Session.EndHistoryExecution(error); } finally { nativeDirty.EndHistoryExecution(); } }
        internal void NativeSaved() => nativeDirty.Saved(ChangeCount);
        // The native title and Save button read the dirty state when they are built; rebuild them after the accounting changed.
        internal void RefreshChrome() {
            try {
                var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
                var window = controller as UnityEngine.Object == null ? null : Native.Read(controller, "_window");
                if (window as UnityEngine.Object != null) Native.Call(window, "RefreshChrome");
            } catch (Exception e) { Fault = "Native map editor title and Save button could not be refreshed: " + e.GetBaseException().Message; }
        }
        public void Dispose() { changed.RemoveEventHandler(Manager, handler); }
    }
    // Extends the native map editor's Save, dirty state, Undo/Redo and exit confirmation. Installed once per process.
    internal static class Hooks {
        const string Id = "crankyant.scene-patcher";
        static Harmony harmony;
        internal static MapEditorAttachment Current;
        // Why Save and history integration is unavailable, or null. Scene patch editing stays disabled without it.
        internal static string Error { get; private set; }
        internal static void Install() {
            if (harmony != null || Error != null) return;
            var patcher = new Harmony(Id);
            try {
                var mapEditor = Native.Type("Game.MapEditor.MapEditor"); var undo = Native.Type("Core.UndoManager"); var window = Native.Type("Game.MapEditor.MapEditorWindow");
                var save = mapEditor?.GetMethod("Save", Native.Flags, null, Type.EmptyTypes, null);
                var dirty = mapEditor?.GetProperty("IsDirty", Native.Flags)?.GetGetMethod(true);
                var undoMethod = undo?.GetMethod("Undo", Native.Flags, null, Type.EmptyTypes, null);
                var redoMethod = undo?.GetMethod("Redo", Native.Flags, null, Type.EmptyTypes, null);
                var exit = window?.GetMethod("ConfirmExitEditor", Native.Flags, null, Type.EmptyTypes, null);
                if (save == null || dirty == null || undoMethod == null || redoMethod == null || exit == null || window.GetMethod("RefreshChrome", Native.Flags) == null) throw new MissingMethodException("Native map editor Save, history or exit functions unavailable");
                PresentMethod();
                patcher.Patch(save, prefix: Method(nameof(BeforeSave)), postfix: Method(nameof(AfterSave)), finalizer: Method(nameof(SaveFinalizer)));
                patcher.Patch(dirty, postfix: Method(nameof(AfterIsDirty)));
                patcher.Patch(undoMethod, prefix: Method(nameof(BeforeHistory)), finalizer: Method(nameof(HistoryFinalizer)));
                patcher.Patch(redoMethod, prefix: Method(nameof(BeforeHistory)), finalizer: Method(nameof(HistoryFinalizer)));
                patcher.Patch(exit, prefix: Method(nameof(BeforeConfirmExit)));
                harmony = patcher;
            } catch (Exception e) {
                try { patcher.UnpatchAll(Id); } catch { }
                Error = "Native map editor Save and history integration unavailable on this build: " + e.GetBaseException().Message;
            }
        }
        internal static void Remove() {
            Current = null;
            if (harmony != null) harmony.UnpatchAll(Id);
            harmony = null;
        }
        static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(Hooks).GetMethod(name, Native.Flags));
        static MapEditorAttachment For(object mapEditor) { var current = Current; return current != null && ReferenceEquals(current.MapEditor, mapEditor) ? current : null; }
        static MapEditorAttachment ForManager(object manager) { var current = Current; return current != null && ReferenceEquals(current.Manager, manager) ? current : null; }
        // Runs before the native map write. A refusal the Save click did not catch first, and a failing patcher write, abort the
        // Save and reach the native Save Failed alert.
        static void BeforeSave(object __instance) {
            var current = For(__instance);
            if (current == null) return;
            try { var ignored = current.NativeDirty; current.Session.BeginSave(); }
            finally { current.VersionChanged(); }
        }
        static void AfterSave(object __instance) {
            var current = For(__instance);
            if (current == null) return;
            current.Session.CompleteNativeSave();
            current.NativeSaved();
            current.Log("Save completed: " + String.Join(", ", current.Session.LastSave.Completed.Select(PatchEditingSession.DisplayItem)) + "; outstanding: " + current.Outstanding());
        }
        static Exception SaveFinalizer(object __instance, Exception __exception) {
            var current = For(__instance);
            if (__exception == null || current == null) return __exception;
            try {
                var failure = new IOException(current.Session.FailSave(__exception), __exception);
                current.Log("Save failed: " + failure.Message);
                return failure;
            } catch { return __exception; }
        }
        static void AfterIsDirty(object __instance, ref bool __result) {
            var current = For(__instance);
            if (current == null) return;
            try { __result = current.Session.IsDirty(current.NativeDirty); }
            catch (Exception e) { __result = true; current.Fault = "Outstanding work could not be determined; the native map editor is treated as unsaved: " + e.GetBaseException().Message; }
        }
        static void BeforeHistory(object __instance) { ForManager(__instance)?.BeginHistoryExecution(); }
        static Exception HistoryFinalizer(object __instance, Exception __exception) {
            var current = ForManager(__instance);
            if (current != null) {
                current.EndHistoryExecution(__exception);
                // Native Undo and Redo rebuild the chrome before the history identity is settled here.
                current.RefreshChrome();
                try { current.Log("Native history executed" + (__exception == null ? "" : " with error " + __exception.GetBaseException().Message) + "; outstanding: " + current.Outstanding() + (current.Session.IncompleteOperation == null ? "" : "; incomplete: " + current.Session.IncompleteOperation)); }
                catch (Exception e) { current.Fault = e.GetBaseException().Message; }
            }
            return __exception;
        }
        internal static MethodInfo PresentMethod() {
            var alerts = Native.Type("UI.Common.ModalAlertController");
            var present = alerts?.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(m => m.Name == "Present" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4 && m.GetParameters()[1].ParameterType == typeof(string));
            var mode = Native.Type("UI.CarEditor.DefinitionEditorModeController");
            if (present == null || mode?.GetProperty("IsEditing", Native.Flags) == null || mode.GetMethod("ExitEditorMode", Native.Flags, null, Type.EmptyTypes, null) == null) throw new MissingMethodException("Native exit confirmation unavailable");
            return present.MakeGenericMethod(typeof(int));
        }
        // Replaces the native exit confirmation only while work is outstanding, with the same Cancel and Exit choices.
        static bool BeforeConfirmExit() {
            var current = Current;
            if (current == null) return true;
            try {
                current.Session.CheckFileVersion();
                var message = current.Session.ExitPrompt(current.NativeDirty);
                if (message == null) return true;
                Action<int> choose = choice => {
                    var mode = Native.Type("UI.CarEditor.DefinitionEditorModeController");
                    if (choice == 1 && (bool)Native.Read(mode, "IsEditing")) Native.Call(mode, "ExitEditorMode");
                };
                PresentMethod().Invoke(null, new object[] { "Exit Map Editor", message, new[] { (0, "Cancel"), (1, "Exit") }, choose });
                return false;
            } catch (Exception e) { current.Fault = "Exit cancelled because outstanding work could not be confirmed. " + e.GetBaseException().Message; return false; }
        }
    }
}
