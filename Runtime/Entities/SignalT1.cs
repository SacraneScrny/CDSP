using System;
using System.Collections.Generic;

using UnityEngine;

namespace CDSP.Entities
{
    public delegate void SignalDelegate<in T>(T arg);
    public class Signal<T> : ASignal
    {
        readonly List<SignalDelegate<T>> _subscriberHandlers = new();
        int _emitDepth;

        /// <summary>Optional debug tag (any object, e.g. the owning context); errors print its runtime type.</summary>
        public Signal(object tag = null) : base(tag) { }
        
        public override int SubscriberCount => _subscriberHandlers.Count;
        
        public void Emit(T payload)
        {
            _emitDepth++;
            try
            {
                for (int i = 0; i < _subscriberHandlers.Count; i++)
                    _subscriberHandlers[i].Invoke(payload);
            }
            finally { _emitDepth--; }
        }

        public SignalHandler Subscribe(SignalDelegate<T> signal)
        {
            if (signal == null) return default;
            if (_emitDepth > 0)
            {
                Debug.LogError($"{Describe(typeof(T).Name)}: cannot Subscribe from inside a handler of the same signal ({_subscriberHandlers.Count} subscriber(s)). Move Subscribe outside of Emit.");
                return default;
            }
            _subscriberHandlers.Add(signal);
            return new SignalHandler(() =>
            {
                if (_emitDepth > 0)
                {
                    Debug.LogError($"{Describe(typeof(T).Name)}: cannot unsubscribe from inside a handler of the same signal ({_subscriberHandlers.Count} subscriber(s)). Move Dispose outside of Emit.");
                    return;
                }
                _subscriberHandlers.Remove(signal);
            });
        }
    }
}