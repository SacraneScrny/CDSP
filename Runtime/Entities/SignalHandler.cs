using System;

namespace CDSP.Entities
{
    public readonly struct SignalHandler : IDisposable
    {
        readonly Action _disposeAction;
        
        public SignalHandler(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }
        
        public void Dispose()
        {
            _disposeAction?.Invoke();
        }
    }
}