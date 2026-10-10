using System;
using System.Collections.Generic;

using UnityEngine;

namespace CDSP.Base
{
    /// <summary>Game logic of a context. Ticks, input, spawning and rules; reads/writes <see cref="Domain"/>, calls the presentation.</summary>
    public abstract class System
    {
        /// <summary>False pauses this system; state is kept. Use <see cref="Context.SetUpdating"/> from outside.</summary>
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

        /// <summary>Owning context. Use it to spawn children and reach the tree.</summary>
        public Context Context { get; private set; }
        /// <summary>Untyped domain storage. Prefer the typed <c>Domain</c> on <c>System{TDomain}</c>.</summary>
        public Domain Domain;
        internal void FillContext(Context ownerContext)
        {
            if (Context != null)
            {
                Debug.LogError($"System {GetType().Name} already has an owner context. Reset with {ownerContext.GetType().Name}");
            }
            Context = ownerContext;
        }
        internal void FillDomain(Domain domain)
        {
            if (Domain != null && IsAwaken)
            {
                Debug.LogError($"Awaken System {GetType().Name} already has a domain. Reset with {domain.GetType().Name}");
            }
            Domain = domain;
            OnDomainUpdated(Domain);
        }
        internal virtual void OnDomainUpdated(Domain domain) { }

        internal void PresentationUpdated(Presentation presentation)
        {
            OnPresentationUpdated(presentation);
        }
        internal virtual void OnPresentationUpdated(Presentation presentation) { }

        internal void PresentationDestroy(Presentation presentation)
        {
            Context.PresentationDestroy(presentation);
            OnPresentationDestroy(presentation);
        }
        internal virtual void OnPresentationDestroy(Presentation presentation) { }

        #region CONTEXT
        /// <summary>Walks up the parent chain to the nearest live ancestor of type <typeparamref name="T"/>.</summary>
        protected T Up<T>(bool includeSelf = true) where T : class
            => Context.Up<T>(includeSelf);
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
        /// <summary>Runs once before children awake. Cache <c>Up{T}</c> lookups and subscribe here.</summary>
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
        /// <summary>Runs once after children awake. Safe to talk to awaken children here.</summary>
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
            OnDestroy();
            if (_disposables != null)
                foreach (var d in _disposables)
                    d?.Dispose();
            _disposables?.Clear();
            _disposables = null;
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
        protected void Track(IEnumerable<IDisposable> disposables)
        {
            _disposables ??= new List<IDisposable>();
            _disposables.AddRange(disposables);
        }
        #endregion
    }

    /// <summary>System with typed <see cref="Domain"/> access.</summary>
    public abstract class System<TDomain>  : System
        where TDomain : Domain
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

        internal override void OnDomainUpdated(Domain domain)
        {
            Domain = domain as TDomain;
        }
    }

    /// <summary>System with typed <see cref="Domain"/> and guaranteed <see cref="Presentation"/> view.</summary>
    public abstract class System<TDomain, TPresentation>  : System<TDomain>
        where TDomain : Domain
        where TPresentation : Presentation
    {
        internal PresentationContainer<TPresentation> _presentationContainer;
        /// <summary>Guaranteed view. Never null-check it: null means a contract violation and must fail loudly.</summary>
        public TPresentation Presentation => _presentationContainer?.Presentation;

        internal void Initialize(TDomain domain, PresentationContainer<TPresentation> presentationContainer)
        {
            Initialize(domain);
            _presentationContainer = presentationContainer;
        }

        internal sealed override void OnPresentationDestroy(Presentation presentation)
        {
            OnPresentationDestroy(presentation as TPresentation);
        }
        internal sealed override void OnPresentationUpdated(Presentation presentation)
            => OnPresentationUpdated(presentation as TPresentation);

        /// <summary>Reacts to the view being destroyed or hot-swapped. Release view-bound state here.</summary>
        protected virtual void OnPresentationDestroy(TPresentation presentation) { }
        /// <summary>Reacts to a new view after a hot-swap. Rebind view-bound state here.</summary>
        protected virtual void OnPresentationUpdated(TPresentation presentation) { }

        #region LIFECYCLE
        internal sealed override void _InternalOnAwake()
        {
            Presentation?.PresentationAwake(this);
        }
        internal sealed override void _InternalOnStart()
        {
            Presentation?.PresentationStart();
        }
        internal sealed override void _InternalOnTick(float deltaTime)
        {
            Presentation?.Tick(deltaTime);
        }
        internal sealed override void _InternalOnFixedTick(float deltaTime)
        {
            Presentation?.FixedTick(deltaTime);
        }
        internal sealed override void _InternalOnLateTick(float deltaTime)
        {
            Presentation?.LateTick(deltaTime);
        }
        #endregion
    }
}
