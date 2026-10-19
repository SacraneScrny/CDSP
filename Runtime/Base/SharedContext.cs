using System;

using UnityEngine;

namespace CDSP.Base
{
    /// <summary>Leaf service node. No children, no <see cref="GameObject"/>, no presentation. Owned by a <see cref="Context"/> (see <see cref="Parent"/>), ticks with it.</summary>
    public abstract class SharedContext
    {
        /// <summary>False pauses this service; state is kept. Destroy is permanent — don't confuse them.</summary>
        public bool IsUpdating { get; private set; } = true;
        /// <summary>Per-pass opt-out for the Update tick.</summary>
        public bool IsUpdateTick { get; private set; } = true;
        /// <summary>Per-pass opt-out for the FixedUpdate tick.</summary>
        public bool IsUpdateFixedTick { get; private set; } = true;
        /// <summary>Per-pass opt-out for the LateUpdate tick.</summary>
        public bool IsUpdateLateTick { get; private set; } = true;

        /// <summary>True when awaken, started and attached (<see cref="IsValid"/>).</summary>
        public bool IsReady => IsAwaken && IsStarted && IsValid;
        /// <summary>True after awake ran once.</summary>
        public bool IsAwaken { get; private set; }
        /// <summary>True after start ran once (requires awake first).</summary>
        public bool IsStarted { get; private set; }

        /// <summary>Owning <see cref="Context"/>. Set once on create, cleared on <see cref="Destroy"/>; the chain never changes otherwise.</summary>
        public Context Parent { get; private set; }
        
        /// <summary>True while attached to the owner <see cref="Context"/>. False before attach and after <see cref="Destroy"/>.</summary>
        public bool IsValid => Parent != null;

        /// <summary>Per-service time multiplier (min <see cref="Mathf.Epsilon"/>, never pauses). Multiplies the already owner-scaled <c>deltaTime</c>. Use <see cref="SetUpdating"/> for pausing.</summary>
        public float TimeScale
        {
            get => _timeScale;
            set
            {
                _timeScale = value;
                _timeScale = Mathf.Max(_timeScale, Mathf.Epsilon);
            }
        }
        float _timeScale = 1;

        protected SharedContext()
        {
            Initialize();
        }
        
        #region API
        /// <summary>Nearest live service of type <typeparamref name="T"/> from the owner scope up to the root. Logs an error when none is found or the service is detached; skips detached services.</summary>
        public T Shared<T>() where T : SharedContext
        {
            if (Parent == null)
            {
                Debug.LogError($"SharedContext {GetType().Name} is detached, cannot resolve shared context {typeof(T).Name}.");
                return null;
            }
            return Parent.Shared<T>();
        }
        /// <summary>Silent bool variant of <see cref="Shared{T}"/>.</summary>
        public bool TryShared<T>(out T shared) where T : SharedContext
        {
            if (Parent == null)
            {
                shared = null;
                return false;
            }
            return Parent.TryShared<T>(out shared);
        }
        /// <summary>Pauses (<c>false</c>) or resumes (<c>true</c>) this service. State is kept; <c>Destroy</c> is permanent. Fluent.</summary>
        public SharedContext SetUpdating(bool active)
        {
            IsUpdating = active;
            return this;
        }
        internal SharedContext FillParent(Context parent)
        {
            if (Parent != null)
            {
                Debug.LogError(
                    $"SharedContext {GetType().Name} already has a parent context {Parent.GetType().Name} (reset with {parent.GetType().Name}).");
                return this;
            }
            Parent = parent;
            return this;
        }
        
        /// <summary>Type check (concrete <see cref="SharedContext"/> type).</summary>
        public bool Is<T>() where T : class => this is T;
        /// <summary>Checked cast to <typeparamref name="T"/> (logs an error and returns null on mismatch).</summary>
        public T As<T>() where T : class
        {
            if (this is not T)
            {
                Debug.LogError($"SharedContext {GetType().Name} is not of type {typeof(T).Name}");
                return null;
            }

            return this as T;
        }
        
        /// <summary>Nearest live ancestor <see cref="Context"/> of type <typeparamref name="T"/> above the owner (owner's chain, self excluded). Logs an error when none is found; prefer over stored parent refs so layers can be inserted later.</summary>
        public T Up<T>() where T : class
        {
            var cur = Parent;
            bool availableIsDead = false;
            while (cur != null)
            {
                if (cur is T t)
                {
                    if (cur.IsDead)
                        availableIsDead = true;
                    else
                        return t;
                }
                cur = cur.Parent;
            }

            Debug.LogError($"Context {GetType().Name} does not have a parent context {typeof(T).Name} {(availableIsDead ? "(Found was dead)" : "")}");
            return null;
        }
        /// <summary>Silent bool variant of <see cref="Up{T}"/>: nearest live ancestor above the owner.</summary>
        public bool TryUp<T>(out T result) where T : class
        {
            var cur = Parent;
            while (cur != null)
            {
                if (cur is T t && !cur.IsDead)
                {
                    result = t;
                    return true;
                }
                cur = cur.Parent;
            }

            result = null;
            return false;
        }
        
        /// <summary>True while the chain from the nearest live <typeparamref name="TContext"/> scope (concrete <see cref="Context"/> type or implemented interface) above the owner up to the root is updating. Answers "is the scope running up there", not "am I paused".</summary>
        public bool IsSimulationRunning<TContext>() where TContext : class
            => TryUp(out TContext context) && IsSimulationRunning(context as Context);
        /// <summary>True while the whole chain from <paramref name="from"/> up to the root is updating.</summary>
        public bool IsSimulationRunning(Context from)
        {
            var cur = from;
            while (cur != null)
            {
                if (!cur.IsUpdating) return false;
                cur = cur.Parent;
            }
            return true;
        }
        #endregion

        #region LIFECYCLE
        protected internal virtual void Initialize() { }
        
        /// <summary>Runs awake then start. Entry point used by roots and batch-spawn.</summary>
        public void AwakeAndStart()
        {
            Awake();
            Start();
        }

        internal void Awake()
        {
            if (IsAwaken)
            {
                Debug.LogError($"Context {GetType().Name} is already awaken.");
                return;
            }
            
            OnCreate();
            OnAwake();

            IsAwaken = true;
        }
        /// <summary>Emergency hook only. Normal service logic belongs in <see cref="SharedSystem"/> / <see cref="SharedDomain"/>.</summary>
        protected virtual void OnCreate() { }
        internal virtual void OnAwake() { }
        
        /// <summary>Starts the service (requires awake first).</summary>
        public void Start()
        {
            if (IsStarted)
            {
                Debug.LogError($"Context {GetType().Name} is already started.");
                return;
            }

            if (!IsAwaken)
            {
                Debug.LogError($"Context {GetType().Name} is not awaken yet. Calling Awake() before Start().");
                return;
            }

            OnStart();

            IsStarted = true;
        }
        internal virtual void OnStart() { }

        internal void Tick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating) return;
            if (!IsUpdateTick) return;
            if (deltaTime <= 0f) return;
            deltaTime *= TimeScale;

            OnTick(deltaTime);
        }
        internal virtual void OnTick(float deltaTime) { }

        internal void FixedTick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating) return;
            if (!IsUpdateFixedTick) return;
            if (deltaTime <= 0f) return;
            deltaTime *= TimeScale;

            OnFixedTick(deltaTime);
        }
        internal virtual void OnFixedTick(float deltaTime) { }

        internal void LateTick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating) return;
            if (!IsUpdateLateTick) return;
            if (deltaTime <= 0f) return;
            deltaTime *= TimeScale;

            OnLateTick(deltaTime);
        }
        internal virtual void OnLateTick(float deltaTime) { }

        /// <summary>Destroys this service and detaches it from the owner (<see cref="IsValid"/> becomes false). The only way to remove self.</summary>
        public void Destroy()
        {
            if (!IsValid) return;
            try
            {
                Destroying();
                OnDestroy();
            }
            finally
            {
                Parent?.SharedContextDestroyFromInside(this);
                Parent = null;
            }
        }
        internal virtual void Destroying() { }

        /// <summary>Runs on destroy; base implementations dispose <see cref="SharedSystem"/> and <see cref="SharedDomain"/>.</summary>
        protected virtual void OnDestroy() { }
        #endregion
    }
    
    public abstract class SharedContext<TDomain> : SharedContext
        where TDomain : SharedDomain, new()
    {
        /// <summary>Node state. Update it from the system; serialize/clone it for saves.</summary>
        public TDomain Domain { get; internal set; }
        
        protected internal override void Initialize()
        {
            Domain = new TDomain();
            Domain.FillOwner(this);
        }
        
        internal void UpdateDomain(TDomain domain)
        {
            Domain = domain;
            domain.FillOwner(this);
            OnDomainUpdated(domain);
        }
        internal virtual void OnDomainUpdated(TDomain domain) { }
        
        internal override void Destroying()
        {
            Domain.Destroy();
        }
    }
    
    public abstract class SharedContext<TDomain, TSystem> : SharedContext<TDomain>
        where TDomain : SharedDomain, new()
        where TSystem : SharedSystem<TDomain>, new()
    {
        /// <summary>Service logic. Owns ticks; reach the owner scope through <see cref="SharedContext.Parent"/>.</summary>
        public TSystem System { get; internal set; }

        protected internal override void Initialize()
        {
            Domain ??= new TDomain();
            System = new TSystem();
            
            Domain.FillOwner(this);
            
            System.FillSharedContext(this);
            System.FillDomain(Domain);
            
            System.Initialize(Domain);
        }
        
        internal override void OnDomainUpdated(TDomain domain)
        {
            System.FillDomain(Domain);
        }
        
        internal sealed override void OnAwake()
        {
            System.Awake();
        }
        internal sealed override void OnStart()
        {
            System.Start();
        }
        
        internal sealed override void OnTick(float deltaTime)
        {
            System.Tick(deltaTime);
        }
        internal sealed override void OnFixedTick(float deltaTime)
        {
            System.FixedTick(deltaTime);
        }
        internal sealed override void OnLateTick(float deltaTime)
        {
            System.LateTick(deltaTime);
        }
        
        internal override void Destroying()
        {
            try { System.Destroy(); }
            finally { Domain.Destroy(); }
        }
    }
}