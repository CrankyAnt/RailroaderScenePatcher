// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// A native event source, such as the map manager's terrain completion event.
class FakeEventSource {
    public event Action<int> Fired;
    public int Handlers => Fired?.GetInvocationList().Length ?? 0;
    public void Fire(int value) => Fired?.Invoke(value);
}
// Observers across load attempts: a subscription held by the world state stays single when the state is reused,
// moves to a replaced source exactly once, and ends with the state.
static class SubscriptionTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        SubscriptionMovesToAReplacedSourceExactlyOnce();
        WorldStateObserversFollowTheStateLifetime();
        Console.WriteLine("PASS: single observers across repeated callbacks, re-entry, source replacement and release");
    }
    static InstanceSubscription Subscribe(Action<int> handler) =>
        new InstanceSubscription(s => ((FakeEventSource)s).Fired += handler, s => ((FakeEventSource)s).Fired -= handler);
    static void SubscriptionMovesToAReplacedSourceExactlyOnce() {
        var calls = 0; Action<int> handler = _ => calls++;
        var first = new FakeEventSource(); var replaced = new FakeEventSource();
        var subscription = Subscribe(handler);
        Assert(subscription.Bind(first) && first.Handlers == 1 && ReferenceEquals(subscription.Instance, first), "The first source gets one handler");
        Assert(!subscription.Bind(first) && !subscription.Bind(first) && first.Handlers == 1, "Repeated binding to the same source adds no handler");
        Assert(!subscription.Bind(null) && first.Handlers == 1 && ReferenceEquals(subscription.Instance, first), "Without a current source the subscription stays where it is");
        Assert(subscription.Bind(replaced) && first.Handlers == 0 && replaced.Handlers == 1, "A replaced source takes the handler over exactly once");
        Assert(!subscription.Bind(replaced) && replaced.Handlers == 1, "Repeated binding after the replacement adds no handler");
        first.Fire(1); replaced.Fire(2);
        Assert(calls == 1, "Only the current source reaches the handler");
        subscription.Dispose(); subscription.Dispose();
        Assert(replaced.Handlers == 0 && first.Handlers == 0 && subscription.Instance == null, "Disposal removes the handler once");
        var failing = new InstanceSubscription(_ => throw new InvalidOperationException("event unavailable"), _ => { });
        try { failing.Bind(first); throw new Exception("A failed subscription was reported as bound"); } catch (InvalidOperationException) { }
        Assert(failing.Instance == null, "A failed subscription binds nothing, so a later binding retries it");
    }
    sealed class ObservingWorld : IDisposable {
        public InstanceSubscription Subscription;
        public void Dispose() { Subscription.Dispose(); }
    }
    // The native world state holds its observers, such as the terrain subscription, for its whole lifetime; each load
    // attempt rebinds them to the current source. Repeated callbacks, re-entry and unloads never add one.
    static void WorldStateObserversFollowTheStateLifetime() {
        var source = new FakeEventSource();
        var created = 0;
        var binding = new LocalWorldBinding<ObservingWorld>(() => { created++; var w = new ObservingWorld { Subscription = Subscribe(_ => { }) }; w.Subscription.Bind(source); return w; },
            w => w.Dispose(), w => w.Subscription.Bind(source));
        binding.Enter("map", "graph"); binding.Enter("map", "graph");
        Assert(created == 1 && source.Handlers == 1, "Repeated callbacks keep one observer");
        binding.Enter("map", "rebuilt-graph"); binding.EndAttempt(); binding.Prepare("map"); binding.Enter("map", "graph");
        Assert(created == 1 && source.Handlers == 1, "Re-entry, an unload and an early check keep one observer");
        var replaced = source; source = new FakeEventSource();
        binding.EndAttempt(); binding.Enter("map", "graph");
        Assert(replaced.Handlers == 0 && source.Handlers == 1, "A load attempt with a replaced source moves the observer");
        var previous = source;
        binding.Enter("another-map", "graph");
        Assert(created == 2 && previous.Handlers == 1, "Another map releases the old state's observer and the new state adds its own");
        binding.Release();
        Assert(previous.Handlers == 0, "Releasing the binding removes the observer");
    }
}
