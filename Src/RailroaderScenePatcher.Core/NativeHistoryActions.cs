// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Optional checked history capability. Group actions distinguish native work from
    // patcher callbacks without relying on menu names or the native change count.
    public interface INativeHistoryActions {
        IReadOnlyList<Action> GroupActions(object group);
    }

    internal sealed class NativeOnlyHistory : INativeHistory {
        readonly INativeHistory history;
        readonly INativeHistoryActions actions;
        readonly Func<Action, bool> isPatcherAction;
        internal NativeOnlyHistory(INativeHistory history, Func<Action, bool> isPatcherAction) {
            this.history = history;
            actions = history as INativeHistoryActions ?? throw new ArgumentException("History action inspection unavailable", nameof(history));
            this.isPatcherAction = isPatcherAction;
        }
        // Mixed groups retain their native identity; only groups containing exclusively
        // this session's patcher actions disappear from native persistence accounting.
        IReadOnlyList<object> NativeGroups(IReadOnlyList<object> groups) => groups
            .Where(group => actions.GroupActions(group).Any(action => !isPatcherAction(action))).ToArray();
        public IReadOnlyList<object> UndoGroups => NativeGroups(history.UndoGroups);
        public IReadOnlyList<object> RedoGroups => NativeGroups(history.RedoGroups);
        public void Register(Action inverse, string name) => history.Register(inverse, name);
    }
}
