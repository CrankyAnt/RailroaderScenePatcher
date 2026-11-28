// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using HarmonyLib;
using UI.Tooltips;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // After a click on a control of a patcher window, panel or entry button, that control's tooltip stays hidden while
    // the pointer stays on it (CommandBand.ClickedTooltip). The game's tooltip controller reads the tooltip of the control
    // under the pointer every frame through UITooltipProvider.TooltipInfo; while the rule holds, a patcher control answers
    // with no tooltip there. The controller then fades its callout out, and once the pointer has left the control and
    // returns, it shows the tooltip again after its own delay, as for any control. Fixed and supplied tooltip texts pass
    // the same getter, so one rule covers every control. When the hook is unavailable, tooltips behave as the game's do.
    internal static class ClickedTooltips {
        const string Id = "crankyant.scene-patcher.tooltips";
        static Harmony harmony;
        static readonly CommandBand.ClickedTooltip rule = new CommandBand.ClickedTooltip();
        static readonly List<RaycastResult> hits = new List<RaycastResult>();
        static int frame = -1;
        // Whether an object belongs to the patcher's windows, panels or entry button; set by the presentation while it exists.
        static Func<GameObject, bool> owner;
        internal static string Error { get; private set; }
        internal static void Install() {
            if (harmony != null || Error != null) return;
            var patcher = new Harmony(Id);
            try {
                var getter = AccessTools.PropertyGetter(typeof(UITooltipProvider), nameof(UITooltipProvider.TooltipInfo));
                if (getter == null || getter.ReturnType != typeof(TooltipInfo)) throw new MissingMethodException("UITooltipProvider.TooltipInfo");
                patcher.Patch(getter, postfix: new HarmonyMethod(typeof(ClickedTooltips).GetMethod(nameof(AfterTooltipInfo), Native.Flags)));
                harmony = patcher;
            } catch (Exception e) {
                try { patcher.UnpatchAll(Id); } catch { }
                Error = "The patcher can't hide its controls' tooltips after a click (" + e.GetBaseException().Message + ")";
            }
        }
        internal static void Remove() {
            try { harmony?.UnpatchAll(Id); } catch { }
            harmony = null; Error = null; owner = null;
        }
        internal static void Own(Func<GameObject, bool> owns) { owner = owns; }
        internal static void Release(Func<GameObject, bool> owns) { if (owner == owns) owner = null; }
        // Once per frame, from the patcher's update or from the first tooltip read of the frame, whichever comes first.
        internal static void Tick() {
            var now = Time.frameCount;
            if (now == frame) return;
            frame = now;
            if (harmony == null) return;
            var pointer = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && Pressed(pointer, out var rect)) rule.Press(rect.x, rect.y, rect.width, rect.height);
            rule.Frame(pointer.x, pointer.y);
        }
        // The screen rectangle of the patcher control under the pointer, found as the game finds the control whose tooltip
        // it shows: the first object on the UI layer under the pointer. The control is the nearest object at or above it
        // with a tooltip or a selectable, so a click on a window's background holds back no tooltip.
        static bool Pressed(Vector3 pointer, out Rect rect) {
            rect = default;
            var owns = owner;
            var events = EventSystem.current;
            if (owns == null || events == null) return false;
            hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = pointer }, hits);
            var ui = LayerMask.NameToLayer("UI");
            foreach (var hit in hits) {
                if (hit.gameObject == null || hit.gameObject.layer != ui) continue;
                if (!owns(hit.gameObject)) return false;
                var target = hit.gameObject.transform;
                while (target != null && target.GetComponent<UITooltipProvider>() == null && target.GetComponent<Selectable>() == null) target = target.parent;
                if (!(target is RectTransform control)) return false;
                var corners = new Vector3[4];
                control.GetWorldCorners(corners);
                var canvas = control.GetComponentInParent<Canvas>();
                var camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
                for (var i = 1; i < 4; i++) { var p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                rect = new Rect(min, max - min);
                return true;
            }
            return false;
        }
        static void AfterTooltipInfo(UITooltipProvider __instance, ref TooltipInfo __result) {
            try {
                Tick();
                if (rule.Hides && owner != null && __instance != null && owner(__instance.gameObject)) __result = TooltipInfo.Empty;
            } catch { }
        }
    }
}
