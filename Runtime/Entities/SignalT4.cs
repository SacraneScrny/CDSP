using System;
using System.Collections.Generic;

using UnityEngine;

namespace CDSP.Entities
{
    public delegate void SignalDelegate<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    public class Signal<T1, T2, T3, T4> : ASignal
    {
        readonly List<SignalDelegate<T1, T2, T3, T4>> _subscriberHandlers = new();
        int _emitDepth;
        string Payload => $"{typeof(T1).Name}, {typeof(T2).Name}, {typeof(T3).Name}, {typeof(T4).Name}";

        /// <summary>Optional debug tag (any object, e.g. the owning context); errors print its runtime type.</summary>
        public Signal(object tag = null) : base(tag) { }
        
        public override int SubscriberCount => _subscriberHandlers.Count;
        
        public void Emit(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            _emitDepth++;
            try
            {
                for (int i = 0; i < _subscriberHandlers.Count; i++)
                    _subscriberHandlers[i].Invoke(arg1, arg2, arg3, arg4);
            }
            finally { _emitDepth--; }
        }

        public SignalHandler Subscribe(SignalDelegate<T1, T2, T3, T4> signal)
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