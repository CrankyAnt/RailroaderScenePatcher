// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Reflection;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The native marker's resolved Location setter cannot represent an unresolved serialized reference.
    internal static class NativeMarkerLocation {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo Field(Type type, string name) {
            for (var current = type; current != null; current = current.BaseType) {
                var field = current.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
        internal static void Write(object marker, TrackLocation value, object segment, Type locationType, Type endType) {
            object native = null;
            if (value != null) {
                if (segment == null) {
                    // Captured underlying state can deliberately name a missing segment. Authored values are checked
                    // before mutation; restoration must retain the serialized reference rather than detach or redirect it.
                    var field = Field(marker.GetType(), "_location");
                    var serialized = Activator.CreateInstance(field.FieldType);
                    Field(field.FieldType, "segmentId").SetValue(serialized, value.Segment);
                    Field(field.FieldType, "distance").SetValue(serialized, (float)value.Distance);
                    Field(field.FieldType, "end").SetValue(serialized, Enum.Parse(endType, value.End == "a" ? "A" : "B"));
                    field.SetValue(marker, serialized);
                    marker.GetType().GetMethod("InvalidateCache", Flags, null, Type.EmptyTypes, null).Invoke(marker, null);
                    (Field(marker.GetType(), "OnLocationChanged").GetValue(marker) as Action)?.Invoke();
                    return;
                }
                native = Activator.CreateInstance(locationType, segment, (float)value.Distance, Enum.Parse(endType, value.End == "a" ? "A" : "B"));
            }
            marker.GetType().GetProperty("Location", Flags).SetValue(marker, native, null);
        }
    }
}
