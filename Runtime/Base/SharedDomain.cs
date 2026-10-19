using System;

using CDSP.Entities;

using UnityEngine;

namespace CDSP.Base
{
    /// <summary>Plain data of a <see cref="SharedContext"/>. No Unity API, no game logic; cleaned up in <see cref="OnDestroy"/>.</summary>
    public abstract class SharedDomain
    {
        SharedContext _ownerContext;
        internal void FillOwner(SharedContext ownerContext)
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
        /// <summary>Cleans up domain state. Called once when the owning service is destroyed.</summary>
        protected virtual void OnDestroy() { }
    }
}
