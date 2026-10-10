#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using CDSP.Base;

using UnityEngine;

namespace CDSP.Editor
{
    internal static class CDSPExplorerReflection
    {
        static readonly BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Dictionary<Type, FieldInfo> ChildFieldCache = new();
        static readonly Dictionary<Type, PropertyInfo> DomainPropCache = new();
        static readonly Dictionary<Type, PropertyInfo> SystemPropCache = new();
        static readonly Dictionary<Type, PropertyInfo> PresentationPropCache = new();

        static FieldInfo ChildField => GetFieldCached(typeof(Context), "_childContexts", ChildFieldCache);

        public static List<Context> GetChildren(Context ctx)
        {
            if (ctx == null) return null;
            try
            {
                var list = ChildField?.GetValue(ctx) as List<Context>;
                return list;
            }
            catch { return null; }
        }

        public static int GetChildrenCount(Context ctx)
        {
            var list = GetChildren(ctx);
            return list?.Count ?? 0;
        }

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

        public static CDSP.Base.System GetSystem(Context ctx)
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
                return p?.GetValue(ctx) as CDSP.Base.System;
            }
            catch { return null; }
        }

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

        public struct ContextDebugFlags
        {
            public bool inited;
            public bool justCreated;
            public bool hasDeadChildren;
        }

        public static ContextDebugFlags GetDebugFlags(Context ctx)
        {
            var f = new ContextDebugFlags();
            if (ctx == null) return f;
            try
            {
                var t = typeof(Context);
                f.inited = (bool)(t.GetField("_inited", AnyInstance)?.GetValue(ctx) ?? false);
                f.justCreated = (bool)(t.GetField("_justCreated", AnyInstance)?.GetValue(ctx) ?? false);
                f.hasDeadChildren = (bool)(t.GetField("_hasDeadChildren", AnyInstance)?.GetValue(ctx) ?? false);
            }
            catch { }
            return f;
        }

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

        public sealed class BusEntryInfo
        {
            public string keyText;
            public int plainCount;
            public List<string> typedCounts = new();
        }

        public sealed class BusInfo
        {
            public bool hasBus;
            public string keyTypeName;
            public int keyCount;
            public List<BusEntryInfo> entries = new();
        }

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

        public struct FieldRow
        {
            public string name;
            public string typeName;
            public string valueText;
        }

        static readonly HashSet<string> StandardMembers = new HashSet<string>
        {
            "IsUpdating", "IsUpdateTick", "IsUpdateFixedTick", "IsUpdateLateTick",
            "IsReady", "IsAwaken", "IsStarted"
        };

        public static List<FieldRow> DumpMembers(object obj, int max = 40, bool includeNonPublic = false, bool hideStandard = true)
        {
            var rows = new List<FieldRow>();
            if (obj == null) return rows;
            try
            {
                var t = obj.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public;
                if (includeNonPublic) flags |= BindingFlags.NonPublic;

                foreach (var f in t.GetFields(flags))
                {
                    if (rows.Count >= max) break;
                    if (f.Name.Contains("k__BackingField")) continue;
                    if (hideStandard && StandardMembers.Contains(f.Name)) continue;
                    object v = null;
                    try { v = f.GetValue(obj); } catch (Exception e) { rows.Add(new FieldRow { name = f.Name, typeName = PrettyName(f.FieldType), valueText = "<err " + e.GetType().Name + ">" }); continue; }
                    rows.Add(new FieldRow
                    {
                        name = f.Name,
                        typeName = PrettyName(f.FieldType),
                        valueText = ToShortString(v)
                    });
                }
                foreach (var p in t.GetProperties(flags))
                {
                    if (rows.Count >= max) break;
                    if (p.GetIndexParameters().Length > 0) continue;
                    string pn = p.Name;
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
            catch { }
            return rows;
        }

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
            var key = type;
            if (cache.TryGetValue(key, out var f)) return f;
            f = type.GetField(name, AnyInstance);
            cache[key] = f;
            return f;
        }
    }
}
#endif
