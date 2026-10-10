using System;

using CDSP.Entities;

using UnityEngine;

namespace CDSP.Base
{
    /// <summary>Plain data of a context. No Unity API, no game logic; cleaned up in <see cref="OnDestroy"/>.</summary>
    public abstract class Domain
    {
        Context _ownerContext;
        internal void FillOwner(Context ownerContext)
        {
            if (_ownerContext != null)
            {
                Debug.LogError($"Domain {GetType().Name} already has an owner context.");
                return;
            }
            _ownerContext = ownerContext;
        }

        internal void Destroy()
        {
            OnDestroy();
        }
        /// <summary>Cleans up domain state. Called once when the owning context is destroyed.</summary>
        protected virtual void OnDestroy() { }
    }
}
