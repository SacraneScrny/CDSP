using System;
using System.Collections.Generic;

using UnityEngine;

namespace CDSP.Entities
{
    public delegate void SignalDelegate<in T1, in T2, in T3, in T4, in T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
    public class Signal<T1, T2, T3, T4, T5> : ASignal
    {
        readonly List<SignalDelegate<T1, T2, T3, T4, T5>> _subscriberHandlers = new();
        int _emitDepth;
        
        public override int SubscriberCount => _subscriberHandlers.Count;

        public void Emit(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            _emitDepth++;
            try
            {
                for (int i = 0; i < _subscriberHandlers.Count; i++)
                    _subscriberHandlers[i].Invoke(arg1, arg2, arg3, arg4, arg5);
            }
            finally { _emitDepth--; }
        }

        public SignalHandler Subscribe(SignalDelegate<T1, T2, T3, T4, T5> signal)
        {
            if (signal == null) return default;
            if (_emitDepth > 0)
            {
                Debug.LogError("Signal: cannot Subscribe from inside a handler of the same signal. Move Subscribe outside of Emit.");
                return default;
            }
            _subscriberHandlers.Add(signal);
            return new SignalHandler(() =>
            {
                if (_emitDepth > 0)
                {
                    Debug.LogError("Signal: cannot unsubscribe from inside a handler of the same signal. Move Dispose outside of Emit.");
                    return;
                }
                _subscriberHandlers.Remove(signal);
            });
        }
    }
}