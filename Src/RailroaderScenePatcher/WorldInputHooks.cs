// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using HarmonyLib;
using UI.Gizmos;
using UnityEngine;

namespace RailroaderScenePatcher {
    internal static class WorldInputHooks {
        const string HookId = "CrankyAnt.ScenePatcher.WorldInput";
        static Harmony hooks;
        static int owners;

        internal static bool EditOpen { get; set; }
        internal static bool OverlayShielded { get; set; }
        // A world choice is pending: the click in the world is the choice's, so the game's own click targets take none.
        internal static bool ChoicePending { get; set; }

        internal static void Acquire() {
            if (owners != 0) { owners++; return; }
            var patcher = new Harmony(HookId);
            try {
                Prefix(patcher, Native.Type("Game.MapEditor.MapEditorController"), "HandleMouseInput", nameof(BlockWorldClick));
                Prefix(patcher, Native.Type("UI.ContextMenu.TrackContextMenu"), "TryShow", nameof(BlockWorldClick), typeof(Camera));
                Prefix(patcher, typeof(ObjectPicker), "TryGetPickableUnderMouse", nameof(FindPickable),
                    typeof(Ray), typeof(PickableActivation?), typeof(IPickable).MakeByRefType(), typeof(float).MakeByRefType());
                Prefix(patcher, Native.Type("Game.MapEditor.BoxSelectTool"), "Update", nameof(UpdateBoxSelect));
                Prefix(patcher, typeof(TransformGizmo), "OnRenderObject", nameof(RenderGizmo));
                var gameInput = Native.Type("UI.GameInput");
                var uiGate = gameInput?.GetMethod("IsMouseOverUI", Native.Flags);
                if (uiGate == null) throw new MissingMethodException("UI.GameInput", "IsMouseOverUI");
                // IMGUI is absent from EventSystem's cached hover state, including on the first frame over a row.
                patcher.Patch(uiGate, postfix: new HarmonyMethod(typeof(WorldInputHooks), nameof(IncludeOverlay)));
                hooks = patcher;
                owners = 1;
            } catch { patcher.UnpatchAll(HookId); throw; }
        }

        internal static void Release() {
            if (--owners != 0) return;
            hooks.UnpatchAll(HookId);
            hooks = null;
        }

        static void Prefix(Harmony patcher, Type type, string name, string prefix, params Type[] parameters) {
            var method = type?.GetMethod(name, Native.Flags, null, parameters, null);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            patcher.Patch(method, prefix: new HarmonyMethod(typeof(WorldInputHooks), prefix) { priority = Priority.First });
        }

        static bool BlockWorldClick(ref bool __result) {
            if (!EditOpen) return true;
            __result = false;
            return false;
        }

        static bool FindPickable(PickableActivation? __1, ref IPickable __2, ref float __3, ref bool __result) {
            // The picker can activate a world object directly before it falls back to the track context menu.
            if (!CommandBand.BlocksPickable(GizmoController.IsShown, __1.HasValue, EditOpen, OverlayShielded, ChoicePending)) return true;
            __2 = null;
            __3 = float.MaxValue;
            __result = false;
            return false;
        }

        static void IncludeOverlay(ref bool __result, ref TooltipInfo __0, ref string __1) {
            if (!OverlayShielded || __result) return;
            __0 = TooltipInfo.Empty;
            __1 = "Over a Scene Patcher world label";
            __result = true;
        }

        static bool UpdateBoxSelect() => !GizmoController.IsShown;

        static bool RenderGizmo(TransformGizmo __instance) => !GizmoController.RestingNativeGizmo(__instance);
    }
}
