using System;
using System.Collections.Generic;

using UnityEngine;

namespace CDSP.Base
{
    /// <summary>Logic of a <see cref="SharedContext"/>. Ticks with the owner; reads/writes <see cref="SharedDomain"/>.</summary>
    public abstract class SharedSystem
    {
        /// <summary>False pauses this system; state is kept. Use <see cref="SharedContext.SetUpdating"/> from outside.</summary>
        public bool IsUpdating { get; internal set; } = true;
        /// <summary>Per-pass opt-out for the Update tick.</summary>
        public bool IsUpdateTick { get; internal set; } = true;
        /// <summary>Per-pass opt-out for the FixedUpdate tick.</summary>
        public bool IsUpdateFixedTick { get; internal set; } = true;
        /// <summary>Per-pass opt-out for the LateUpdate tick.</summary>
        public bool IsUpdateLateTick { get; internal set; } = true;

        /// <summary>True when awaken, started and initialized.</summary>
        public bool IsReady => IsAwaken && IsStarted && _isInitialized;
        /// <summary>True after <see cref="OnAwake"/> ran once.</summary>
        public bool IsAwaken { get; internal set; }
        /// <summary>True after <see cref="OnStart"/> ran once.</summary>
        public bool IsStarted { get; internal set; }
        internal bool _isInitialized;

        /// <summary>Owning <see cref="SharedContext"/>. Use it to reach the owner scope and resolve services.</summary>
        public SharedContext Context { get; private set; }
        /// <summary>Untyped domain storage. Prefer the typed <c>Domain</c> on <see cref="SharedSystem{TDomain}"/>.</summary>
        public SharedDomain Domain;
        internal void FillSharedContext(SharedContext ownerContext)
        {
            if (Context != null)
            {
                Debug.LogError($"System {GetType().Name} already has an owner context. Reset with {ownerContext.GetType().Name}");
                return;
            }
            Context = ownerContext;
        }
        internal void FillDomain(SharedDomain domain)
        {
            if (Domain != null && IsAwaken)
            {
                Debug.LogError($"Awaken System {GetType().Name} already has a domain. Reset with {domain.GetType().Name}");
            }
            Domain = domain;
            OnDomainUpdated(Domain);
        }
        internal virtual void OnDomainUpdated(SharedDomain domain) { }

        #region CONTEXT
        /// <summary>Nearest live ancestor <see cref="Context"/> of type <typeparamref name="T"/> above the owner service.</summary>
        protected T Up<T>() where T : class
            => Context.Up<T>();
        /// <summary>Nearest live service of type <typeparamref name="T"/> from the owner scope up to the root.</summary>
        protected T Shared<T>() where T : SharedContext 
            => Context.Shared<T>();
        #endregion

        #region LIFECYCLE
        internal void Awake()
        {
            if (IsAwaken)
            {
                Debug.LogError($"System {GetType().Name} is already awaken.");
                return;
            }
            OnAwake();
            _InternalOnAwake();
            IsAwaken = true;
        }
        internal virtual void _InternalOnAwake() { }
        /// <summary>Runs once before the owner ticks. Cache <c>Shared{T}</c> / <c>Up{T}</c> lookups here.</summary>
        protected virtual void OnAwake() { }

        internal void Start()
        {
            if (IsStarted)
            {
                Debug.LogError($"System {GetType().Name} is already started.");
                return;
            }
            if (!IsAwaken)
            {
                Debug.LogError($"System {GetType().Name} is not awaken yet. Calling Awake() before Start().");
                return;
            }
            OnStart();
            _InternalOnStart();
            IsStarted = true;
        }
        internal virtual void _InternalOnStart() { }
        /// <summary>Runs once after awake. Safe to talk to resolved services here.</summary>
        protected virtual void OnStart() { }

        internal void Tick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating)  return;
            if (!IsUpdateTick) return;

            OnTick(deltaTime);
            _InternalOnTick(deltaTime);
        }
        internal virtual void _InternalOnTick(float deltaTime) { }
        /// <summary>Per-frame logic. Receives already time-scaled <paramref name="deltaTime"/>.</summary>
        protected virtual void OnTick(float deltaTime) { }

        internal void FixedTick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating)  return;
            if (!IsUpdateFixedTick) return;

            OnFixedTick(deltaTime);
            _InternalOnFixedTick(deltaTime);
        }
        internal virtual void _InternalOnFixedTick(float deltaTime) { }
        /// <summary>Physics-step logic. Receives already time-scaled <paramref name="deltaTime"/>.</summary>
        protected virtual void OnFixedTick(float deltaTime) { }

        internal void LateTick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating)  return;
            if (!IsUpdateLateTick) return;

            OnLateTick(deltaTime);
            _InternalOnLateTick(deltaTime);
        }
        internal virtual void _InternalOnLateTick(float deltaTime) { }
        /// <summary>Post-update logic (e.g. camera follow). Receives already time-scaled <paramref name="deltaTime"/>.</summary>
        protected virtual void OnLateTick(float deltaTime) { }

        internal void Destroy()
        {
            try
            {
                OnDestroy();
            }
            finally
            {
                if (_disposables != null)
                    foreach (var d in _disposables)
                        d?.Dispose();
                _disposables?.Clear();
                _disposables = null;
            }
        }
        /// <summary>Cleans up system state. Tracked subscriptions are disposed automatically.</summary>
        protected virtual void OnDestroy() { }
        #endregion

        #region DISPOSABLES
        List<IDisposable> _disposables;
        /// <summary>Tracks a subscription so it is disposed with the node. Prefer over manual <c>Dispose</c>.</summary>
        protected void Track(IDisposable disposable)
        {
            _disposables ??= new List<IDisposable>();
            _disposables.Add(disposable);
        }
        /// <summary>Tracks several subscriptions so they are disposed with the node.</summary>
        protected void Track(params IDisposable[] disposables)
        {
            if (disposables == null) return;
            _disposables ??= new List<IDisposable>(disposables.Length);
            _disposables.AddRange(disposables);
        }
        /// <summary>Tracks several subscriptions so they are disposed with the node.</summary>
        protected void Track(IEnumerable<IDisposable> disposables)
        {
            _disposables ??= new List<IDisposable>();
            _disposables.AddRange(disposables);
        }
        #endregion
    }

    /// <summary>Service logic with typed <see cref="SharedDomain"/> access.</summary>
    public abstract class SharedSystem<TDomain>  : SharedSystem
        where TDomain : SharedDomain
    {
        /// <summary>Typed domain state.</summary>
        public new TDomain Domain { get; private set; }

        internal void Initialize(TDomain domain)
        {
            if (_isInitialized)
            {
                Debug.LogError("System already initialized");
                return;
            }
            Domain = domain;
            _isInitialized = true;
        }

        internal override void OnDomainUpdated(SharedDomain domain)
        {
            Domain = domain as TDomain;
        }
    }
}
