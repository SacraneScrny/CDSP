using System;

using UnityEngine;

namespace CDSP.Base
{
    /// <summary>Unity view of a context (transforms, animators, particles, UI). No game rules, no input polling.</summary>
    public abstract class Presentation : MonoBehaviour
    {
        /// <summary>False pauses this view; state is kept.</summary>
        public bool IsUpdating { get; private set; } = true;
        /// <summary>Per-pass opt-out for the Update tick.</summary>
        public bool IsUpdateTick { get; private set; } = true;
        /// <summary>Per-pass opt-out for the FixedUpdate tick.</summary>
        public bool IsUpdateFixedTick { get; private set; } = true;
        /// <summary>Per-pass opt-out for the LateUpdate tick.</summary>
        public bool IsUpdateLateTick { get; private set; } = true;

        /// <summary>True when awaken and started.</summary>
        public bool IsReady => IsAwaken && IsStarted;
        /// <summary>True after <see cref="OnAwake"/> ran once.</summary>
        public bool IsAwaken { get; private set; }
        /// <summary>True after <see cref="OnStart"/> ran once.</summary>
        public bool IsStarted { get; private set; }

        /// <summary>Owning system. Set once on awake, cleared on <see cref="ResetPresentation"/>.</summary>
        public System System { get; private set; }

        [SerializeField] Transform _childrenRoot;
        /// <summary>Slot for logical children (picked in the Inspector from this hierarchy, e.g. <c>RootCanvas/Container</c>). Falls back to the view root when unset.</summary>
        public Transform ChildrenRoot => _childrenRoot != null ? _childrenRoot : transform;

        #region LIFECYCLE
        internal void PresentationAwake(System ownerSystem)
        {
            if (IsAwaken)
            {
                Debug.LogError($"Presentation {GetType().Name} is already awaken.");
                return;
            }
            if (ownerSystem == null) throw new NullReferenceException("Presentation System is null.");
            if (System != null)
            {
                Debug.LogError($"Presentation {GetType().Name} already has an owner system. Replacing...");
                return;
            }
            System = ownerSystem;

            OnAwake();
            IsAwaken = true;
        }
        /// <summary>Caches components and resets visual state. No game decisions here.</summary>
        protected virtual void OnAwake() { }

        internal void PresentationStart()
        {
            if (IsStarted)
            {
                Debug.LogError($"Presentation {GetType().Name} is already started.");
                return;
            }
            if (!IsAwaken)
            {
                Debug.LogError($"Presentation {GetType().Name} is not awaken yet. Calling Awake() before Start().");
                return;
            }
            if (System == null) throw new NullReferenceException("Presentation System is null.");
            OnStart();
            IsStarted = true;
        }
        /// <summary>Runs once after awake. Safe to play initial animations here.</summary>
        protected virtual void OnStart() { }

        internal void Tick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating)  return;
            if (!IsUpdateTick) return;

            OnTick(deltaTime);
        }
        /// <summary>Per-frame view update. Only applies what the system pushes; never decides.</summary>
        protected virtual void OnTick(float deltaTime) { }

        internal void FixedTick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating)  return;
            if (!IsUpdateFixedTick) return;

            OnFixedTick(deltaTime);
        }
        /// <summary>Physics-step view update.</summary>
        protected virtual void OnFixedTick(float deltaTime) { }

        internal void LateTick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating)  return;
            if (!IsUpdateLateTick) return;

            OnLateTick(deltaTime);
        }
        /// <summary>Post-update view update (e.g. follow transforms).</summary>
        protected virtual void OnLateTick(float deltaTime) { }

        bool _disposed;
        internal void InternalDispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnDisposing();
        }
        /// <summary>Releases view resources (tweens, listeners, materials). Fires exactly once per view lifetime.</summary>
        protected virtual void OnDisposing() { }
        #endregion

        void OnDestroy()
        {
            if (!IsAwaken) return;
            System?.PresentationDestroy(this);
        }

        /// <summary>Resets lifecycle so a pooled/reused component starts clean. Called before every awake.</summary>
        /// <returns>This presentation.</returns>
        public Presentation ResetPresentation()
        {
            _disposed = false;
            IsUpdating = true;
            IsUpdateTick = true;
            IsUpdateFixedTick = true;
            IsUpdateLateTick = true;

            IsAwaken = false;
            IsStarted = false;

            System = null;
            OnResetPresentation();
            return this;
        }
        /// <summary>Clears custom visual state on reuse (event handlers, cached sprites, timers).</summary>
        protected virtual void OnResetPresentation() { }
    }
    /// <summary>Presentation with typed <see cref="System"/> access.</summary>
    public abstract class Presentation<TSystem> : Presentation
        where TSystem : System
    {
        /// <summary>Owning system, typed.</summary>
        protected new TSystem System => (TSystem)base.System;
    }
}
