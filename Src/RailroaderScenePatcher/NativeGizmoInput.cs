// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using HarmonyLib;
using UI.Gizmos;
using UnityEngine;

namespace RailroaderScenePatcher {
    internal static class NativeGizmoInput {
        static readonly string[] PointerTools = { "_spanEndpointTool", "_builtinPlacementTool", "_terrainPaintTool", "_terrainTileTool" };

        internal static bool CanStartDrag(Behaviour controller) {
            try {
                foreach (var name in PointerTools) {
                    var tool = Native.Read(controller, name);
                    if (tool != null && (bool)Native.Read(tool, "OwnsPointer")) return false;
                }
                var box = Native.Read(controller, "_boxSelectTool");
                if (box != null && (bool)Native.Read(box, "IsDragging")) return false;
                if ((bool)Native.Read(Native.Type("Game.MapEditor.DtoFieldEditSession"), "AnySessionActive")) return false;
                foreach (var native in controller.GetComponents<TransformGizmo>())
                    if (native != null && native.IsDragging) return false;
            } catch { return false; }
            return true;
        }

        internal static void PatchDragCancellation(Harmony hooks, Behaviour controller, MapEditorAttachment attachment, Type owner, string escape, string saveOrHistory) {
            Patch(hooks, controller.GetType(), "HandleEscapeCancel", owner, escape);
            Patch(hooks, attachment.MapEditor.GetType(), "Save", owner, saveOrHistory);
            Patch(hooks, attachment.Manager.GetType(), "Undo", owner, saveOrHistory);
            Patch(hooks, attachment.Manager.GetType(), "Redo", owner, saveOrHistory);
        }

        static void Patch(Harmony hooks, Type type, string name, Type owner, string prefix) {
            var method = type.GetMethod(name, Native.Flags, null, Type.EmptyTypes, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            hooks.Patch(method, prefix: new HarmonyMethod(owner.GetMethod(prefix, Native.Flags)) { priority = Priority.First });
        }
    }
}
