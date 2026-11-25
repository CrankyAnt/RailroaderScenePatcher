// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using RailroaderScenePatcher.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // The mouse wheel for a patcher scroll view when the wheel's event does not reach it. In the outliner's help at UI
    // scale 1.25 the wheel stopped after two lines while the view's own range, which its scroll bar shows and its thumb
    // reaches, went on for about ten more: the view still had room, so the wheel's events no longer arrived. When the
    // pointer is over this view, the wheel turned, and the view took no step this frame, this component takes the step
    // the view would have taken, within the content's measured height. It runs after every Update, so after the
    // event system, and does nothing in a frame in which the view moved by itself. The first time it steps in for a
    // view it writes one debug line that names the object under the pointer and the handler the event went to.
    internal sealed class WheelFallback : MonoBehaviour {
        ScrollRect view;
        float last;
        bool reported;
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        void Awake() {
            view = GetComponent<ScrollRect>();
            if (view != null && view.content != null) last = view.content.anchoredPosition.y;
        }

        void LateUpdate() {
            if (view == null || view.content == null || !view.vertical) return;
            var content = view.content;
            var position = content.anchoredPosition.y;
            var wheel = Input.mouseScrollDelta.y;
            if (wheel != 0 && Mathf.Abs(position - last) < 0.01f && UnderPointer(out var hit)) {
                var viewport = view.viewport != null ? view.viewport : (RectTransform)view.transform;
                var target = CommandBand.WheelOffset(position, wheel, view.scrollSensitivity, LayoutUtility.GetPreferredHeight(content), viewport.rect.height);
                if (Mathf.Abs(target - position) > 0.01f) {
                    content.anchoredPosition = new Vector2(content.anchoredPosition.x, target);
                    view.velocity = Vector2.zero;
                    position = target;
                    if (!reported) {
                        reported = true;
                        var handler = hit == null ? null : ExecuteEvents.GetEventHandler<IScrollHandler>(hit);
                        PatcherLog.Debug(() => "Scroll fallback applied; event handler: " + (handler == null ? "no handler" : Path(handler)) +
                            "; pointer target: " + (hit == null ? "nothing" : Path(hit)) + ".");
                    }
                }
            }
            last = position;
        }

        // Whether the topmost object under the pointer belongs to this view, so the wheel is meant for it and not for
        // a window over it.
        bool UnderPointer(out GameObject hit) {
            hit = null;
            var events = EventSystem.current;
            if (events == null) return false;
            hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = Input.mousePosition }, hits);
            if (hits.Count == 0) return false;
            hit = hits[0].gameObject;
            return hit != null && hit.transform.IsChildOf(transform);
        }

        static string Path(GameObject item) {
            var names = new List<string>();
            for (var at = item.transform; at != null && names.Count < 6; at = at.parent) names.Insert(0, at.name);
            return string.Join("/", names);
        }
    }
}
