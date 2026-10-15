using System;
using System.Collections.Generic;

namespace CDSP.Entities
{
    /// <summary>Plain one-to-many notification handler.</summary>
    public delegate void Handler();
    /// <summary>Typed one-to-many notification handler.</summary>
    public delegate void Handler<in T>(T payload);
    sealed class Entry
    {
        public Handler plain;
        public Dictionary<Type, Delegate> typed;
    }

    /// <summary>Per-instance event bus: publisher and subscriber must use the same instance (usually the nearest common ancestor). Not guarded against subscribe/unsubscribe from inside a handler for the same key.</summary>
    public sealed class EventBus<TKey> : IEventBus<TKey>
        where TKey : notnull
    {
        readonly Dictionary<TKey, Entry> _map;

        /// <summary>Creates a bus with the given key-capacity hint.</summary>
        public EventBus(int capacity = 32) => _map = new Dictionary<TKey, Entry>(capacity);
        /// <summary>Number of keys with at least one subscription.</summary>
        public int KeyCount => _map.Count;

        Entry GetOrCreate(TKey key)
        {
            if (!_map.TryGetValue(key, out var e))
            {
                e = new Entry();
                _map[key] = e;
            }

            return e;
        }

        /// <summary>Subscribes a plain handler. Returns a disposable subscription.</summary>
        public Subscription<TKey> Subscribe(TKey key, Handler handler)
        {
            if (handler == null) return default;
            GetOrCreate(key).plain += handler;
            return new Subscription<TKey>(this, key, handler, null);
        }
        /// <summary>Subscribes a typed handler for payloads of type <typeparamref name="T"/>. Returns a disposable subscription.</summary>
        public Subscription<TKey> Subscribe<T>(TKey key, Handler<T> handler)
        {
            if (handler == null) return default;
            var e = GetOrCreate(key);
            e.typed ??= new Dictionary<Type, Delegate>(4);
            var t = typeof(T);
            e.typed.TryGetValue(t, out var d);
            e.typed[t] = (Handler<T>)d + handler;
            return new Subscription<TKey>(this, key, handler, t);
        }

        /// <summary>Removes a plain handler.</summary>
        public void Unsubscribe(TKey key, Handler handler)
        {
            if (handler == null) return;
            UnsubscribeCore(key, handler, null);
        }
        /// <summary>Removes a typed handler.</summary>
        public void Unsubscribe<T>(TKey key, Handler<T> handler)
        {
            if (handler == null) return;
            UnsubscribeCore(key, handler, typeof(T));
        }
        internal void UnsubscribeCore(TKey key, Delegate handler, Type type)
        {
            if (!_map.TryGetValue(key, out var e)) return;
            if (type == null)
            {
                e.plain -= (Handler)handler;
            }
            else
            {
                if (e.typed == null) return;
                if (!e.typed.TryGetValue(type, out var d)) return;
                var next = Delegate.Remove(d, handler);
                if (next == null) e.typed.Remove(type);
                else e.typed[type] = next;
            }

            if (e.plain == null && (e.typed == null || e.typed.Count == 0))
                _map.Remove(key);
        }

        /// <summary>Fires plain handlers on <paramref name="key"/>.</summary>
        public void Emit(TKey key)
        {
            if (_map.TryGetValue(key, out var e))
                e.plain?.Invoke();
        }
        /// <summary>Fires typed handlers for <typeparamref name="T"/> on <paramref name="key"/>.</summary>
        public void Emit<T>(TKey key, T payload)
        {
            if (!_map.TryGetValue(key, out var e)) return;
            if (e.typed != null && e.typed.TryGetValue(typeof(T), out var d))
                ((Handler<T>)d)?.Invoke(payload);
        }

        /// <summary>Clears all subscriptions for <paramref name="key"/>.</summary>
        public void ClearEvents(TKey key) => _map.Remove(key);
        /// <summary>Clears all subscriptions.</summary>
        public void ClearEvents() => _map.Clear();
    }
}
