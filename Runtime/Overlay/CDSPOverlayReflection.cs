using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using CDSP.Base;

using UnityEngine;

namespace CDSP.Overlay
{
    public static class CDSPOverlayReflection
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
                return ChildField?.GetValue(ctx) as List<Context>;
            }
            catch { return null; }
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
            public bool notAwaken;
            public bool notStarted;
            public bool hasDeadChildren;
        }

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

        public sealed class SignalEntryInfo
        {
            public string fieldName;
            public string signalType;
            public int subscriberCount;
        }

        public sealed class SignalInfo
        {
            public bool hasSignals;
            public int signalCount;
            public List<SignalEntryInfo> entries = new();
        }

        public static SignalInfo GetSignalInfo(Context ctx)
        {
            var info = new SignalInfo();
            if (ctx == null) return info;
            try
            {
                var t = ctx.GetType();
                int depth = 0;
                while (t != null && t != typeof(object) && depth++ < 16)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (info.entries.Count >= 200) break;
                        if (!typeof(CDSP.Entities.ASignal).IsAssignableFrom(f.FieldType)) continue;
                        var entry = new SignalEntryInfo { fieldName = f.Name, signalType = PrettyName(f.FieldType) };
                        try
                        {
                            var s = f.GetValue(ctx) as CDSP.Entities.ASignal;
                            entry.subscriberCount = s != null ? s.SubscriberCount : 0;
                        }
                        catch { }
                        info.entries.Add(entry);
                    }
                    t = t.BaseType;
                }
                info.hasSignals = info.entries.Count > 0;
                info.signalCount = info.entries.Count;
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
            if (cache.TryGetValue(type, out var f)) return f;
            f = type.GetField(name, AnyInstance);
            cache[type] = f;
            return f;
        }
    }
}
