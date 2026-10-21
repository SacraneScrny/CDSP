using System;
using System.Collections.Generic;

using UnityEngine;

namespace CDSP.Entities
{
    public delegate void SignalDelegate<in T1, in T2>(T1 arg1, T2 arg2);
    public class Signal<T1, T2> : ASignal
    {
        readonly List<SignalDelegate<T1, T2>> _subscriberHandlers = new();
        int _emitDepth;
        string Payload => $"{typeof(T1).Name}, {typeof(T2).Name}";

        /// <summary>Optional debug tag (any object, e.g. the owning context); errors print its runtime type.</summary>
        public Signal(object tag = null) : base(tag) { }
        
        public override int SubscriberCount => _subscriberHandlers.Count;
        
        public void Emit(T1 arg1, T2 arg2)
        {
            _emitDepth++;
            try
            {
                for (int i = 0; i < _subscriberHandlers.Count; i++)
                    _subscriberHandlers[i].Invoke(arg1, arg2);
            }
            finally { _emitDepth--; }
        }

        public SignalHandler Subscribe(SignalDelegate<T1, T2> signal)
        {
            if (signal == null) return default;
            if (_emitDepth > 0)
            {
                Debug.LogError($"{Describe(Payload)}: cannot Subscribe from inside a handler of the same signal ({_subscriberHandlers.Count} subscriber(s)). Move Subscribe outside of Emit.");
                return default;
            }
            _subscriberHandlers.Add(signal);
            return new SignalHandler(() =>
            {
                if (_emitDepth > 0)
                {
                    Debug.LogError($"{Describe(Payload)}: cannot unsubscribe from inside a handler of the same signal ({_subscriberHandlers.Count} subscriber(s)). Move Dispose outside of Emit.");
                    return;
                }
                _subscriberHandlers.Remove(signal);
            });
        }
    }
}