// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Reflection;

namespace RailroaderScenePatcher {
    internal static class Native {
        internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        internal static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
        internal static object Read(object receiver, string name) {
            if (receiver == null) throw new InvalidOperationException("Native receiver unavailable for " + name);
            var type = receiver as Type ?? receiver.GetType();
            var instance = receiver is Type ? null : receiver;
            for (var current = type; current != null; current = current.BaseType) {
                var property = current.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(instance, null);
                var field = current.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(instance);
            }
            throw new MissingMemberException(type.FullName, name);
        }
        internal static object Call(object receiver, string name, params object[] args) {
            if (receiver == null) throw new InvalidOperationException("Native receiver unavailable for " + name);
            var type = receiver as Type ?? receiver.GetType();
            var methods = type.GetMethods(Flags).Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray();
            if (methods.Length != 1) throw new MissingMethodException(type.FullName, name);
            return methods[0].Invoke(receiver is Type ? null : receiver, args);
        }
    }
}
