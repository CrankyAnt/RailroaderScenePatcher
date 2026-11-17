// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Loader;
using RailroaderScenePatcher.Core;

static class ReloadReturnTests {
    // Separate, collectible copies of the product core share only the runtime's AppDomain and BCL values.
    // No private methods or Unity lifecycle callbacks are simulated here.
    sealed class Copy : IDisposable {
        readonly AssemblyLoadContext context = new AssemblyLoadContext("reload-return-" + Guid.NewGuid(), true);
        readonly object owner;
        internal Copy(string key) {
            var assembly = context.LoadFromAssemblyPath(typeof(PatchEngine).Assembly.Location);
            var type = assembly.GetType("RailroaderScenePatcher.Core.ReloadReturn", true);
            owner = Activator.CreateInstance(type, key);
        }
        internal object Call(string method, params object[] arguments) => owner.GetType().GetMethod(method).Invoke(owner, arguments);
        public void Dispose() { context.Unload(); }
    }
    static object Read(object value, string property) => value.GetType().GetProperty(property).GetValue(value);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run() {
        CapturedLocationSurvivesProductCopyReplacement();
        MapWorldExpiryAndCancellationGuardReturn();
        AJumpRequestNeedsMeasuredArrivalAndCannotCompleteANewerReturn();
        Console.WriteLine("PASS: reload return survives product-copy replacement with map isolation, expiry and measured arrival");
    }
    static void CapturedLocationSurvivesProductCopyReplacement() {
        var key = "reload-return-test/" + Guid.NewGuid();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var first = new Copy(key)) first.Call("Capture", "map-a", 17, new[] { 112.0, 2.0, 33.0 }, now);
        using (var next = new Copy(key)) {
            try {
                var pending = next.Call("Pending", "map-a", 28, now.AddSeconds(10));
                Check(pending != null && ((IEnumerable<double>)Read(pending, "Point")).SequenceEqual(new[] { 112.0, 2.0, 33.0 }),
                    "A fresh loaded product copy recovers the captured map location without the old instance or its statics");
                Check((bool)next.Call("MarkRequested", Read(pending, "RequestId"), "map-a", 28, now.AddSeconds(10)), "Replacement copy requests the return");
            } finally { next.Call("Cancel"); }
        }
    }
    static void MapWorldExpiryAndCancellationGuardReturn() {
        var owner = new ReloadReturn("reload-return-test/" + Guid.NewGuid());
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try {
            owner.Capture("map-a", 17, new[] { 112.0, 2.0, 33.0 }, now);
            Check(owner.Pending("map-a", 17, now) == null, "The original controller cannot consume its own pending return before reload");
            Check(owner.Pending("map-a", 28, now) != null, "The new controller can recover the same-map return");
            Check(owner.Pending("map-b", 28, now) == null && owner.Pending("map-a", 29, now) == null,
                "A different map discards the request so revisiting the original map cannot apply a stale return");
            owner.Capture("map-a", 17, new[] { 112.0, 2.0, 33.0 }, now);
            Check(owner.Pending("map-a", 28, now.AddMinutes(6)) == null && owner.Pending("map-a", 29, now) == null,
                "An expired relaunch is removed, rather than replayed by a later load");
            owner.Capture("map-a", 17, new[] { 112.0, 2.0, 33.0 }, now); owner.Cancel();
            Check(new ReloadReturn("reload-return-test/unused").Pending("map-a", 28, now) == null && owner.Pending("map-a", 28, now) == null,
                "Cancellation releases the pending return without leaking it into another handoff");
        } finally { owner.Cancel(); }
    }
    static void AJumpRequestNeedsMeasuredArrivalAndCannotCompleteANewerReturn() {
        var key = "reload-return-test/" + Guid.NewGuid();
        var owner = new ReloadReturn(key);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try {
            var id = owner.Capture("map-a", 17, new[] { 112.0, 2.0, 33.0 }, now);
            Check(!owner.Complete(id, "map-a", 28, new[] { 112.0, 2.0, 33.0 }, now), "A captured point alone is not a completed return");
            Check(owner.MarkRequested(id, "map-a", 28, now) && new ReloadReturn(key).Pending("map-a", 28, now).Requested,
                "The in-flight request survives replacing the product's return owner too");
            using (var replacement = new Copy(key)) {
                Check((bool)Read(replacement.Call("Pending", "map-a", 28, now), "Requested"),
                    "A separate loaded core copy recovers the in-flight request without retaining the old core's types");
            }
            Check(!owner.Complete(id, "map-a", 28, new[] { 120.0, 2.0, 33.0 }, now) && owner.Pending("map-a", 28, now) != null,
                "A successful jump request is not arrival while the measured map location is elsewhere");
            Check(!owner.Complete(id, "map-a", 28, new[] { Double.NaN, 2.0, 33.0 }, now), "Invalid camera measurements cannot establish arrival");
            Check(owner.Complete(id, "map-a", 28, new[] { 112.1, 2.0, 33.1 }, now) && owner.Pending("map-a", 28, now) == null,
                "Measured arrival at the working area completes and removes the request");
            var nextId = owner.Capture("map-a", 17, new[] { 200.0, 3.0, 50.0 }, now);
            Check(!owner.MarkRequested(id, "map-a", 28, now) && !owner.Complete(id, "map-a", 28, new[] { 200.0, 3.0, 50.0 }, now)
                && owner.Pending("map-a", 28, now).RequestId == nextId, "A late callback cannot claim or clear a newer return");
        } finally { owner.Cancel(); }
    }
}
