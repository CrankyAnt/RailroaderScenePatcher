// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RailroaderScenePatcher {
    // Tells its owner when the pointer comes over a control and when it leaves, also when the control goes away under
    // the pointer.
    internal sealed class HoverReport : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        internal Action<bool> Changed;
        bool over;
        public void OnPointerEnter(PointerEventData data) { over = true; Changed?.Invoke(true); }
        public void OnPointerExit(PointerEventData data) { Leave(); }
        void OnDisable() { Leave(); }
        void Leave() { if (!over) return; over = false; Changed?.Invoke(false); }
    }
}
