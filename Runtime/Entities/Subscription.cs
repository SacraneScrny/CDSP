using System;

namespace CDSP.Entities
{
    /// <summary>Disposable event subscription. Dispose to unsubscribe; track it in the system so it dies with the node.</summary>
    public readonly struct Subscription<TKey> : IDisposable
    {
        readonly EventBus<TKey> _bus;
        readonly TKey _key;
        readonly Delegate _handler;
        readonly Type _type;

        internal Subscription(EventBus<TKey> bus, TKey key, Delegate handler, Type type)
        {
            _bus = bus;
            _key = key;
            _handler = handler;
            _type = type;
        }

        /// <summary>Unsubscribes the handler. Safe to call on a default subscription.</summary>
        public void Dispose()
        {
            if (_bus != null && _handler != null)
                _bus.UnsubscribeCore(_key, _handler, _type);
        }
    }
}
