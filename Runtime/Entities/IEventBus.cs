namespace CDSP.Entities
{
    /// <summary>Per-instance event bus contract. Use events for one-to-many notifications; direct calls for parent-to-known-child.</summary>
    public interface IEventBus<TKey> where TKey : notnull
    {
        /// <summary>Subscribes a plain handler. Returns a disposable subscription.</summary>
        public Subscription<TKey> Subscribe(TKey key, Handler handler);
        /// <summary>Subscribes a typed handler for payloads of type <typeparamref name="T"/>. Returns a disposable subscription.</summary>
        public Subscription<TKey> Subscribe<T>(TKey key, Handler<T> handler);

        /// <summary>Removes a plain handler.</summary>
        public void Unsubscribe(TKey key, Handler handler);
        /// <summary>Removes a typed handler.</summary>
        public void Unsubscribe<T>(TKey key, Handler<T> handler);

        /// <summary>Fires plain handlers on <paramref name="key"/>.</summary>
        public void Emit(TKey key);
        /// <summary>Fires typed handlers for <typeparamref name="T"/> on <paramref name="key"/>.</summary>
        public void Emit<T>(TKey key, T payload);

        /// <summary>Clears all subscriptions for <paramref name="key"/>.</summary>
        public void ClearEvents(TKey key);
        /// <summary>Clears all subscriptions.</summary>
        public void ClearEvents();
    }
}
