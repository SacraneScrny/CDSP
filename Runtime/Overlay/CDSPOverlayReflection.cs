using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using CDSP.Base;

using UnityEngine;

namespace CDSP.Overlay
{
    /// <summary>
    /// Runtime-safe reflection access to CDSP internals.
    /// Same idea as Editor/CDSPExplorerReflection, but without UnityEditor dependencies,
    /// so it works in builds.
    /// </summary>
    /// <summary>Build-safe reflection bridge over context internals for the runtime overlay. Read-only, exception-tolerant.</summary>
    public static class CDSPOverlayReflection
    {
        static readonly BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Dictionary<Type, FieldInfo> ChildFieldCache = new();
        static readonly Dictionary<Type, PropertyInfo> DomainPropCache = new();
        static readonly Dictionary<Type, PropertyInfo> SystemPropCache = new();
        static readonly Dictionary<Type, PropertyInfo> PresentationPropCache = new();

        static FieldInfo ChildField => GetFieldCached(typeof(Context), "_childContexts", ChildFieldCache);

        /// <summary>Raw child list of <paramref name="ctx"/> (internal list, may include not-yet-swept dead entries).</summary>
        public static List<Context> GetChildren(Context ctx)
        {
            if (ctx == null) return null;
            try
            {
                return ChildField?.GetValue(ctx) as List<Context>;
            }
            catch { return null; }
        }

        /// <summary>Domain of <paramref name="ctx"/>, or null for dataless nodes.</summary>
        public static Domain GetDomain(Context ctx)
        {
            if (ctx == null) return null;
            try
            {
                var t = ctx.GetType();
                if (!DomainPropCache.TryGetValue(t, out var p))
                {
                    p = t.GetProperty("Domain", AnyInstance);
                    DomainPropCache[t] = p;
                }
                return p?.GetValue(ctx) as Domain;
            }
            catch { return null; }
        }

        /// <summary>System of <paramref name="ctx"/>, or null for logicless nodes.</summary>
        public static Base.System GetSystem(Context ctx)
        {
            if (ctx == null) return null;
            try
            {
                var t = ctx.GetType();
                if (!SystemPropCache.TryGetValue(t, out var p))
                {
                    p = t.GetProperty("System", AnyInstance);
                    SystemPropCache[t] = p;
                }
                return p?.GetValue(ctx) as Base.System;
            }
            catch { return null; }
        }

        /// <summary>Presentation of <paramref name="ctx"/>, or null.</summary>
        public static Presentation GetPresentation(Context ctx)
        {
            if (ctx == null) return null;
            try
            {
                var t = ctx.GetType();
                if (!PresentationPropCache.TryGetValue(t, out var p))
                {
                    p = t.GetProperty("Presentation", AnyInstance);
                    PresentationPropCache[t] = p;
                }
                return p?.GetValue(ctx) as Presentation;
            }
            catch { return null; }
        }

        /// <summary>Deferred-lifecycle flags for badges (pending awake/start, not-yet-swept dead children).</summary>
        public struct ContextDebugFlags
        {
            /// <summary>Has children waiting for awake.</summary>
            public bool notAwaken;
            /// <summary>Has children waiting for start.</summary>
            public bool notStarted;
            /// <summary>Has not-yet-swept dead children.</summary>
            public bool hasDeadChildren;
        }

        /// <summary>Reads deferred-lifecycle flags of <paramref name="ctx"/>.</summary>
        public static ContextDebugFlags GetDebugFlags(Context ctx)
        {
            var f = new ContextDebugFlags();
            if (ctx == null) return f;
            try
            {
                var t = typeof(Context);
                f.notAwaken = (bool)(t.GetField("_hasNotAwakenChildren", AnyInstance)?.GetValue(ctx) ?? false);
                f.notStarted = (bool)(t.GetField("_hasNotStartedChildren", AnyInstance)?.GetValue(ctx) ?? false);
                f.hasDeadChildren = (bool)(t.GetField("_hasDeadChildren", AnyInstance)?.GetValue(ctx) ?? false);
            }
            catch { }
            return f;
        }

        /// <summary>Tries to set a bool property (walks base types, uses non-public setters). Used by the overlay pause/tick toggles.</summary>
        public static bool TrySetBoolProp(object obj, string propName, bool value)
        {
            if (obj == null) return false;
            try
            {
                var p = obj.GetType().GetProperty(propName, AnyInstance);
                if (p == null)
                {
                    var bt = obj.GetType().BaseType;
                    while (bt != null && p == null)
                    {
                        p = bt.GetProperty(propName, AnyInstance);
                        bt = bt.BaseType;
                    }
                }
                var setter = p?.GetSetMethod(true);
                if (setter == null) return false;
                setter.Invoke(obj, new object[] { value });
                return true;
            }
            catch { return false; }
        }

        /// <summary>Subscription counts for one event key.</summary>
        public sealed class BusEntryInfo
        {
            /// <summary>Key text.</summary>
            public string keyText;
            /// <summary>Plain handler count.</summary>
            public int plainCount;
            /// <summary>Per-payload-type counts as <c>"Type xN"</c>.</summary>
            public List<string> typedCounts = new();
        }

        /// <summary>Snapshot of a context's event bus for the overlay.</summary>
        public sealed class BusInfo
        {
            /// <summary>False when the context has no bus (not a 4-arg context).</summary>
            public bool hasBus;
            /// <summary>Event key type name.</summary>
            public string keyTypeName;
            /// <summary>Number of keys with subscriptions.</summary>
            public int keyCount;
            /// <summary>Per-key entries (capped).</summary>
            public List<BusEntryInfo> entries = new();
        }

        /// <summary>Reads the event-bus snapshot of <paramref name="ctx"/> (capped at 200 keys).</summary>
        public static BusInfo GetEventBusInfo(Context ctx)
        {
            var info = new BusInfo();
            if (ctx == null) return info;
            try
            {
                var eventsField = ctx.GetType().GetField("Events", AnyInstance);
                if (eventsField == null) return info;
                var bus = eventsField.GetValue(ctx);
                if (bus == null) return info;
                info.hasBus = true;

                var busType = bus.GetType();
                var keyType = busType.IsGenericType ? busType.GetGenericArguments()[0] : null;
                info.keyTypeName = keyType != null ? PrettyName(keyType) : busType.Name;

                var keyCountProp = busType.GetProperty("KeyCount", AnyInstance);
                if (keyCountProp != null)
                    info.keyCount = (int)keyCountProp.GetValue(bus);

                var mapField = busType.GetField("_map", AnyInstance);
                var map = mapField?.GetValue(bus) as IDictionary;
                if (map == null) return info;

                int guard = 0;
                foreach (DictionaryEntry kv in map)
                {
                    if (guard++ > 200) break;
                    var entry = new BusEntryInfo { keyText = kv.Key != null ? kv.Key.ToString() : "<null>" };
                    try
                    {
                        var e = kv.Value;
                        if (e != null)
                        {
                            var et = e.GetType();
                            var plain = et.GetField("plain", AnyInstance)?.GetValue(e) as Delegate;
                            entry.plainCount = plain?.GetInvocationList().Length ?? 0;
                            var typed = et.GetField("typed", AnyInstance)?.GetValue(e) as IDictionary;
                            if (typed != null)
                            {
                                foreach (DictionaryEntry tk in typed)
                                {
                                    var d = tk.Value as Delegate;
                                    int c = d?.GetInvocationList().Length ?? 0;
                                    string tn = (tk.Key as Type)?.Name ?? "?";
                                    entry.typedCounts.Add($"{tn} x{c}");
                                }
                            }
                        }
                    }
                    catch { }
                    info.entries.Add(entry);
                }
            }
            catch { }
            return info;
        }

        /// <summary>One dumped member row (field or property).</summary>
        public struct FieldRow
        {
            /// <summary>Member name.</summary>
            public string name;
            /// <summary>Pretty member type name.</summary>
            public string typeName;
            /// <summary>Short value text (truncated).</summary>
            public string valueText;
        }

        static readonly HashSet<string> StandardMembers = new HashSet<string>
        {
            "IsUpdating", "IsUpdateTick", "IsUpdateFixedTick", "IsUpdateLateTick",
            "IsReady", "IsAwaken", "IsStarted"
        };

        /// <summary>Dumps fields then properties of <paramref name="obj"/> (most-derived first, stops before <paramref name="stopBefore"/>). Skips framework noise; failures render as text, never throw.</summary>
        public static List<FieldRow> DumpMembers(object obj, int max = 40, bool includeNonPublic = false, bool hideStandard = true, Type stopBefore = null)
        {
            var rows = new List<FieldRow>();
            if (obj == null) return rows;
            try
            {
                var root = obj.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
                if (includeNonPublic) flags |= BindingFlags.NonPublic;
                var seen = new HashSet<string>();

                for (var t = root; t != null && t != typeof(object) && t != stopBefore; t = t.BaseType)
                {
                    foreach (var f in t.GetFields(flags))
                    {
                        if (rows.Count >= max) break;
                        if (f.Name.Contains("k__BackingField")) continue;
                        if (!seen.Add("F:" + f.Name)) continue;
                        if (hideStandard && StandardMembers.Contains(f.Name)) continue;
                        object v = null;
                        try { v = f.GetValue(obj); }
                        catch (Exception e)
                        {
                            rows.Add(new FieldRow { name = f.Name, typeName = PrettyName(f.FieldType), valueText = "<err " + e.GetType().Name + ">" });
                            continue;
                        }
                        rows.Add(new FieldRow
                        {
                            name = f.Name,
                            typeName = PrettyName(f.FieldType),
                            valueText = ToShortString(v)
                        });
                    }
                    if (rows.Count >= max) break;
                }
                for (var t = root; t != null && t != typeof(object) && t != stopBefore && rows.Count < max; t = t.BaseType)
                {
                    foreach (var p in t.GetProperties(flags))
                    {
                        if (rows.Count >= max) break;
                        if (p.GetIndexParameters().Length > 0) continue;
                        string pn = p.Name;
                        if (!seen.Add("P:" + pn)) continue;
                        if (pn == "Domain" || pn == "System" || pn == "Presentation" || pn == "Context" || pn == "Presentation" || pn == "Parent") continue;
                        if (hideStandard && StandardMembers.Contains(pn)) continue;
                        MethodInfo g;
                        try { g = p.GetGetMethod(true); } catch { continue; }
                        if (g == null || g.GetParameters().Length > 0) continue;
                        object v = null;
                        try { v = g.Invoke(obj, null); } catch { continue; }
                        rows.Add(new FieldRow
                        {
                            name = p.Name,
                            typeName = PrettyName(p.PropertyType),
                            valueText = ToShortString(v)
                        });
                    }
                }
            }
            catch { }
            return rows;
        }

        /// <summary>Short display text for a value (GameObjects, components, enumerables, 120-char cap).</summary>
        public static string ToShortString(object v)
        {
            if (v == null) return "null";
            if (v is string s) return $"\"{s}\"";
            if (v is GameObject go) return go == null ? "null (destroyed)" : $"<GO> {go.name}";
            if (v is Component c) return c == null ? "null (destroyed)" : $"<{c.GetType().Name}> {c.name}";
            if (v is Type t) return PrettyName(t);
            if (v is IEnumerable e && v is not string)
            {
                int n = 0;
                try { foreach (var _ in e) { n++; if (n > 5) break; } } catch { return "<enumerable?>"; }
                return n > 5 ? $"[...] ({n}+)" : $"[...] ({n})";
            }
            try
            {
                var str = v.ToString();
                if (str != null && str.Length > 120) str = str.Substring(0, 120) + "…";
                return str;
            }
            catch { return "<?>" ; }
        }

        /// <summary>Generic-aware short type name (<c>List&lt;Int32&gt;</c> instead of <c>List`1</c>).</summary>
        public static string PrettyName(Type t)
        {
            if (t == null) return "null";
            if (!t.IsGenericType) return t.Name;
            var args = t.GetGenericArguments();
            var names = new string[args.Length];
            for (int i = 0; i < args.Length; i++) names[i] = PrettyName(args[i]);
            var baseName = t.Name;
            var tick = baseName.IndexOf('`');
            if (tick >= 0) baseName = baseName.Substring(0, tick);
            return $"{baseName}<{string.Join(", ", names)}>";
        }

        static FieldInfo GetFieldCached(Type type, string name, Dictionary<Type, FieldInfo> cache)
        {
            if (cache.TryGetValue(type, out var f)) return f;
            f = type.GetField(name, AnyInstance);
            cache[type] = f;
            return f;
        }
    }
}
