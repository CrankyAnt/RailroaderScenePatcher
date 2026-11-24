// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT

namespace RailroaderScenePatcher.Core {
    // The patcher version that a capability's targets require.
    internal static class VersionRequirement {
        // The minimum that the capabilities of the first usable scene patcher share.
        public const string Delivery = "0.5.0";
        // The version when the target names the structure, or names no structure and names the component; null
        // otherwise. Without a component, only the structure counts.
        public static string For(TargetIdentity target, string structure, string component, string version) =>
            target != null && (target.Structure == structure || (component != null && target.Structure == null && target.Component == component)) ? version : null;
    }
}
