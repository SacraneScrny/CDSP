using System;
using System.Collections.Generic;
using System.Linq;

using CDSP.Entities;
using CDSP.Utils;

using UnityEngine;

using Object = UnityEngine.Object;

namespace CDSP.Base
{
    [Serializable]
    /// <summary>Node of the CDSP tree. Owns a <see cref="GameObject"/>, a Domain/System/Presentation triple and children.</summary>
    public abstract class Context
    {
        /// <summary>False pauses this subtree; state is kept. Destroy is permanent — don't confuse them.</summary>
        public bool IsUpdating { get; private set; } = true;
        /// <summary>Per-pass opt-out for the Update tick.</summary>
        public bool IsUpdateTick { get; private set; } = true;
        /// <summary>Per-pass opt-out for the FixedUpdate tick.</summary>
        public bool IsUpdateFixedTick { get; private set; } = true;
        /// <summary>Per-pass opt-out for the LateUpdate tick.</summary>
        public bool IsUpdateLateTick { get; private set; } = true;

        /// <summary>True when awaken, started and not dead.</summary>
        public bool IsReady => IsAwaken && IsStarted && !IsDead;
        /// <summary>True after awake ran once.</summary>
        public bool IsAwaken { get; private set; }
        /// <summary>True after start ran once (requires awake first).</summary>
        public bool IsStarted { get; private set; }
        /// <summary>True once destroyed. The node is swept from the parent at the end of the tick pass; check this on cached refs.</summary>
        public bool IsDead { get; private set; }

        /// <summary>Owned <see cref="GameObject"/>. Child instances are parented under their parent.</summary>
        public GameObject Instance { get; private set; }
        /// <summary>Logical parent. Only the transform can be reparented; this chain never changes.</summary>
        public Context Parent { get; private set; }
        /// <summary>True when this node has no parent.</summary>
        public bool IsRoot => Parent == null;

        /// <summary>Per-node time multiplier (min <see cref="Mathf.Epsilon"/>, never pauses). Multiplies down the branch, so nested slow-motion composes. Use <see cref="SetUpdating"/> for pausing.</summary>
        public float TimeScale
        {
            get => _timeScale;
            set
            {
                _timeScale = value;
                _timeScale = Mathf.Max(_timeScale, Mathf.Epsilon);
            }
        }

        internal Func<GameObject, GameObject> _instantiateAction = DefaultActions.InstantiateAction;
        internal Action<GameObject> _destroyAction = Object.Destroy;

        internal List<Context> _childContexts;
        bool _hasDeadChildren;
        bool _hasNotAwakenChildren;
        bool _hasNotStartedChildren;
        float _timeScale = 1;
        GameObject[] _plainTemporal;

        protected Context()
        {
            Initialize();
        }

        #region EVENTS
        internal void PresentationDestroy(Presentation presentation)
        {
            OnPresentationDestroy(presentation);
        }
        internal virtual void OnPresentationDestroy(Presentation presentation) { }
        #endregion

        #region CHILDREN
        internal void ChildDestroy(Context child)
        {
            if (child == null) return;
            if (child.IsDead) return;
            child.MarkDead();
            _hasDeadChildren = true;
        }
        void FlushDeadChildren()
        {
            if (!_hasDeadChildren) return;
            if (_childContexts != null)
                for (int i = _childContexts.Count - 1; i >= 0; i--)
                {
                    if (_childContexts[i].IsDead) _childContexts.RemoveAt(i);
                }

            _hasDeadChildren = false;
        }
        void InitializeCreatedChildren()
        {
            if (_childContexts == null || _childContexts.Count == 0) return;
            while (_hasNotAwakenChildren)
            {
                _hasNotAwakenChildren = false;
                for (int i = 0; i <  _childContexts.Count; i++)
                { 
                    if (_childContexts[i].IsDead) continue;
                    if (_childContexts[i].IsAwaken) continue;
                    _childContexts[i].Awake();
                }
            }
            while (_hasNotStartedChildren)
            {
                _hasNotStartedChildren = false;
                for (int i = 0; i <  _childContexts.Count; i++)
                {
                    if (_childContexts[i].IsDead) continue;
                    if (_childContexts[i].IsStarted) continue;
                    if (!_childContexts[i].IsAwaken) _childContexts[i].Awake();
                    if (_childContexts[i].IsAwaken) _childContexts[i].Start();
                }
            }
        }

        /// <summary>Spawns a child and awakes/starts it synchronously (if the parent already runs). Prefer <see cref="CreateChild{T}"/> inside tick callbacks.</summary>
        public T CreateChildImmediate<T>(
            GameObject prefab = null,
            Action<T> init = null,
            bool isUpdating = true)
            where T : Context, new()
            => _InternalCreateChild<T>(prefab, init, isUpdating);
        /// <summary>Spawns a child (deferred by default: safe inside <c>OnTick</c>, wakes at the end of the tick pass). <paramref name="init"/> runs after parenting but before awake; a throwing init rolls the child back.</summary>
        public T CreateChild<T>(
            GameObject prefab = null,
            Action<T> init = null,
            bool isUpdating = true)
            where T : Context, new()
            => _InternalCreateChild<T>(prefab, init, isUpdating, true, true);
        internal T _InternalCreateChild<T>(
            GameObject prefab = null,
            Action<T> init = null,
            bool isUpdating = true,
            bool holdStart = false,
            bool holdAwake = false)
            where T : Context, new()
        {
            if (IsDead)
            {
                Debug.LogError($"Cannot create child {GetType().Name} because it is dead.");
                return null;
            }

            var child = new T();
            _childContexts ??= new();
            _childContexts.Add(child);

            if (prefab != null)
            {
                child.CreatePresentation(prefab, Instance?.transform);
                // Deferred parenting: if the parent has no Instance yet (not awaken),
                // skip PutTransformInside now (it would only log an error) — Awake()
                // reparents orphaned prefab children once the parent Instance exists.
                if (Instance != null)
                    child.PutTransformInside(this);
            }
            child.SetUpdating(isUpdating)
                .FillParent(this);
            try
            {
                init?.Invoke(child);
            }
            catch
            {
                _childContexts?.Remove(child);
                child.Destroy();
                throw;
            }

            if (IsAwaken && !holdAwake)
                child.Awake();
            else _hasNotAwakenChildren = true;
            
            if (IsStarted && !holdStart && child.IsAwaken)
                child.Start();
            else _hasNotStartedChildren = true;

            return child;
        }

        /// <summary>Destroys all live children of type <typeparamref name="T"/>. Returns true if any were destroyed.</summary>
        public bool DestroyChild<T>() where T : Context
        {
            if (IsDead) return false;
            if (_childContexts == null || _childContexts.Count == 0) return false;
            bool result = false;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i] is T)
                {
                    if (_childContexts[i].IsDead) continue;
                    _childContexts[i].Destroy();
                    _childContexts[i].MarkDead();
                    ChildDestroy(_childContexts[i]);
                    result = true;
                }
            }

            return result;
        }
        /// <summary>Destroys a live child by reference. No-op (false) if it is not a live child of this parent.</summary>
        public bool DestroyChild(Context child)
        {
            if (IsDead) return false;
            if (child == null) return false;
            if (child.IsDead) return false;
            if (_childContexts == null || _childContexts.Count == 0) return false;
            var deadBody = _childContexts.FirstOrDefault(c => c == child);
            if (deadBody == null) return false;
            child.Destroy();
            ChildDestroy(deadBody);
            return true;
        }
        /// <summary>Destroys a child by raw internal-list index. Uses the same raw order as <see cref="GetChild(int)"/>.</summary>
        public bool DestroyChild(int num)
        {
            if (IsDead) return false;
            if (_childContexts == null || _childContexts.Count == 0) return false;
            if (num < 0 || num >= _childContexts.Count) return false;
            if (_childContexts[num].IsDead) return false;
            _childContexts[num].Destroy();
            ChildDestroy(_childContexts[num]);
            return true;
        }

        /// <summary>Wakes all deferred children at once, after configuring them. Use after batch-spawning with <see cref="CreateChild{T}"/>.</summary>
        public void StartAllChildren()
        {
            if (IsDead) return;
            if (_childContexts == null || _childContexts.Count == 0) return;
            if (Instance == null)
            {
                EnsurePresentationExists();
                ReparentOrphanedChildren();
            }

            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i].IsDead) continue;
                if (_childContexts[i].IsAwaken) continue;
                _childContexts[i].Awake();
            }

            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i].IsDead) continue;
                if (_childContexts[i].IsStarted) continue;
                if (!_childContexts[i].IsAwaken) _childContexts[i].Awake();
                if (_childContexts[i].IsAwaken) _childContexts[i].Start();
            }

            _hasNotAwakenChildren = false;
            _hasNotStartedChildren = false;
        }

        /// <summary>First live child of type <typeparamref name="T"/>, or null. Skips dead children.</summary>
        public T GetChild<T>() where T : Context
        {
            if (_childContexts == null || _childContexts.Count == 0) return null;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i].IsDead) continue;
                if (_childContexts[i] is T t)
                    return t;
            }

            return null;
        }
        /// <summary>Child by raw internal-list index (same raw order as the internal list, which may briefly hold not-yet-swept dead entries). Null if dead/out of range.</summary>
        public Context GetChild(int num)
        {
            if (_childContexts == null || _childContexts.Count == 0) return null;
            if (num < 0 || num >= _childContexts.Count) return null;
            var child = _childContexts[num];
            if (child == null || child.IsDead) return null;
            return child;
        }
        /// <summary>Nth live child of type <typeparamref name="T"/>, or null. Skips dead children.</summary>
        public T GetChild<T>(int num) where T : Context
        {
            if (_childContexts == null || _childContexts.Count == 0) return null;
            if (num < 0) return null;
            int found = 0;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i].IsDead) continue;
                if (_childContexts[i] is T t)
                {
                    if (found == num)
                        return t;
                    found++;
                }
            }

            return null;
        }

        /// <summary>First live child of type <typeparamref name="T"/>. Silent bool variant of <see cref="GetChild{T}"/>.</summary>
        public bool TryGetChild<T>(out T child) where T : Context
        {
            child = GetChild<T>();
            return child != null;
        }
        /// <summary>Child by raw internal-list index. Silent bool variant of <see cref="GetChild(int)"/>.</summary>
        public bool TryGetChild(int num, out Context child)
        {
            child = GetChild(num);
            return child != null;
        }

        /// <summary>True if any live child of type <typeparamref name="T"/> exists.</summary>
        public bool HasChild<T>() where T : Context => GetChild<T>() != null;
        /// <summary>True if <paramref name="child"/> is a live child of this parent (identity + liveness).</summary>
        public bool HasChild(Context child)
        {
            if (child == null) return false;
            if (_childContexts == null || _childContexts.Count == 0) return false;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i] == child && !_childContexts[i].IsDead)
                    return true;
            }

            return false;
        }

        /// <summary>Number of live children (dead ones excluded).</summary>
        public int ChildCount
        {
            get
            {
                if (_childContexts == null) return 0;
                int count = 0;
                for (int i = 0; i < _childContexts.Count; i++)
                    if (!_childContexts[i].IsDead)
                        count++;
                return count;
            }
        }
        /// <summary>Number of live children of type <typeparamref name="T"/>.</summary>
        public int SpecificChildCount<T>() where T : Context
        {
            if (_childContexts == null) return 0;
            int count = 0;
            for (int i = 0; i < _childContexts.Count; i++)
                if (!_childContexts[i].IsDead && _childContexts[i] is T)
                    count++;
            return count;
        }

        /// <summary>Fills <paramref name="outList"/> with live children of type <typeparamref name="T"/> (clears first).</summary>
        public void GetChildren<T>(List<T> outList) where T : Context
        {
            if (outList == null) return;
            outList.Clear();
            if (_childContexts == null) return;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                if (_childContexts[i].IsDead) continue;
                if (_childContexts[i] is T t)
                    outList.Add(t);
            }
        }
        #endregion

        #region API
        /// <summary>Overrides how the <see cref="GameObject"/> is born. Hook for pooling or addressables. Fluent.</summary>
        public Context SetInstantiateAction(Func<GameObject, GameObject> instantiateAction)
        {
            if (instantiateAction == null)
            {
                Debug.LogError($"Cannot set null InstantiateAction for context {GetType().Name}");
                return this;
            }

            _instantiateAction = instantiateAction;
            return this;
        }
        /// <summary>Overrides how the <see cref="GameObject"/> dies (also used for view hot-swap). Hook for pooling. Fluent.</summary>
        public Context SetDestroyAction(Action<GameObject> destroyAction)
        {
            if (destroyAction == null)
            {
                Debug.LogError($"Cannot set null DestroyAction for context {GetType().Name}");
                return this;
            }

            _destroyAction = destroyAction;
            return this;
        }

        /// <summary>Hot-swaps the view: detaches children, disposes the old view via the destroy action (pooling-safe), re-attaches children. Re-awakes the new view if the old one was awake.</summary>
        public Context CreatePresentation(
            GameObject prefab,
            Transform root = null,
            string goName = null,
            bool addComponent = false)
        {
            if (prefab == null) throw new NullReferenceException($"Cannot create presentation {GetType().Name}");
            var newInstance = _instantiateAction(prefab);
            newInstance.transform.SetParent(root);
            newInstance.name = goName ?? prefab.name;
            try
            {
                ValidatePresentation(newInstance, addComponent);
            }
            catch
            {
                _destroyAction(newInstance);
                throw;
            }
            OnPresentationUpdating(Instance);
            Instance = newInstance;
            try
            {
                OnPresentationUpdated(Instance, addComponent);
            }
            catch
            {
                // Fill validated above, so this should not throw; if it does (e.g. custom
                // addComponent failure), the old view is already gone and logical children
                // are held in the temporal cache — reattach them to the new instance
                // to avoid orphaning, then rethrow.
                RestoreLogicalChildren();
                throw;
            }
            return this;
        }
        /// <summary>Creates an empty <see cref="GameObject"/> node (presentation component auto-added). Roots are named <c>{GO}_ROOT</c>.</summary>
        public Context CreateEmptyPresentation(
            Transform root = null,
            string goName = null)
        {
            CreatePresentation(
                EmptyGameObject.Instance,
                root,
                addComponent: true,
                goName: goName ?? GetType().Name);
            return this;
        }
        internal virtual void OnPresentationUpdating(GameObject instance)
        {
            CacheLogicalChildren();
            if (instance != null)
                _destroyAction(instance);
        }
        internal virtual void OnPresentationUpdated(GameObject instance, bool addComponent)
        {
            RestoreLogicalChildren();
        }
        internal virtual void ValidatePresentation(GameObject newInstance, bool addComponent) { }

        void CacheLogicalChildren()
        {
            if (Instance == null) { _plainTemporal = null; return; }
            if (_childContexts == null || _childContexts.Count == 0) { _plainTemporal = null; return; }
            var keep = new HashSet<GameObject>();
            foreach (var ch in _childContexts)
            {
                if (ch.IsDead) continue;
                if (ch.Instance == null) continue;
                keep.Add(ch.Instance);
            }
            if (keep.Count == 0) { _plainTemporal = null; return; }
            var list = new List<GameObject>(keep.Count);
            int n = Instance.transform.childCount;
            for (int i = 0; i < n; i++)
            {
                var go = Instance.transform.GetChild(i).gameObject;
                if (keep.Contains(go)) list.Add(go);
            }
            _plainTemporal = list.ToArray();
            for (int i = _plainTemporal.Length - 1; i >= 0; i--)
                _plainTemporal[i].transform.SetParent(null);
        }
        void RestoreLogicalChildren()
        {
            if (_plainTemporal == null || _plainTemporal.Length == 0) { _plainTemporal = null; return; }
            if (Instance == null) { _plainTemporal = null; return; }
            for (int i = 0; i < _plainTemporal.Length; i++)
            {
                _plainTemporal[i].transform.SetParent(Instance.transform);
                _plainTemporal[i].transform.SetAsLastSibling();
            }
            _plainTemporal = null;
        }

        internal void MarkDead()
        {
            IsUpdating = false;
            IsDead = true;
        }
        /// <summary>Reparents only the <see cref="GameObject"/> transform. Logical parent, tick ownership and <c>Up{T}</c> chain are unchanged.</summary>
        public Context PutTransformInside(Context parent)
        {
            if (IsDead) return this;
            if (Instance == null || parent == null || parent.Instance == null)
            {
                Debug.LogError(
                    $"Cannot put context {GetType().Name} inside {parent?.GetType().Name ?? "null"} because one of them has no Root GameObject.");
                return this;
            }

            Instance.transform.SetParent(parent.Instance.transform);
            return this;
        }
        /// <summary>Pauses (<c>false</c>) or resumes (<c>true</c>) this subtree. State is kept; <c>Destroy</c> is permanent. Fluent.</summary>
        public Context SetUpdating(bool active)
        {
            if (IsDead) return this;
            IsUpdating = active;
            return this;
        }
        internal Context FillParent(Context parent)
        {
            if (IsDead) return this;
            if (Parent != null)
            {
                Debug.LogError(
                    $"Context {GetType().Name} already has a parent context {Parent.GetType().Name} (reset with {parent.GetType().Name}).");
                return this;
            }
            Parent = parent;
            return this;
        }

        /// <summary>Null-if-dead checked cast to <typeparamref name="T"/>.</summary>
        public T IsValid<T>() where T : Context => IsDead ? null : this as T;
        /// <summary>Type check.</summary>
        public bool Is<T>() where T : Context => this is T;
        /// <summary>Checked cast to <typeparamref name="T"/> (logs an error and returns null on mismatch).</summary>
        public T As<T>() where T : Context
        {
            if (this is not T)
            {
                Debug.LogError($"Context {GetType().Name} is not of type {typeof(T).Name}");
                return null;
            }

            return this as T;
        }

        /// <summary>Nearest live ancestor of type <typeparamref name="T"/> (self included by default). Logs an error when none is found; prefer over stored parent refs so layers can be inserted later.</summary>
        public T Up<T>(bool includeSelf = true) where T : class
        {
            var cur = includeSelf ? this : Parent;
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
        /// <summary>Silent bool variant of <see cref="Up{T}"/>: nearest live ancestor of type <typeparamref name="T"/>.</summary>
        public bool TryUp<T>(out T result, bool includeSelf = true) where T : class
        {
            var cur = includeSelf ? this : Parent;
            while (cur != null)
            {
                if (cur is T t)
                {
                    if (cur.IsDead) continue;
                    result = t;
                    return true;
                }
                cur = cur.Parent;
            }

            result = null;
            return false;
        }

        /// <summary>True while the chain from the nearest live <typeparamref name="TContext"/> scope up to the root is updating. Answers "is the scope running up there", not "am I paused".</summary>
        public bool IsSimulationRunning<TContext>() where TContext : Context
            => TryUp(out TContext context) && IsSimulationRunning(context);
        /// <summary>True while the whole chain from <paramref name="from"/> (default: this) up to the root is updating.</summary>
        public bool IsSimulationRunning(Context from = null)
        {
            var cur = from ?? this;
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
            if (IsDead)
            {
                Debug.LogError($"Context {GetType().Name} is dead, cannot awake.");
                return;
            }
            
            OnCreate();
            EnsurePresentationExists();
            ReparentOrphanedChildren();
            OnAwake();
            if (_childContexts != null)
                for (int i = 0; i < _childContexts?.Count; i++)
                {
                    var context = _childContexts[i];
                    if (context.IsDead) continue;
                    if (context.IsAwaken) continue;
                    context.Awake();
                }

            IsAwaken = true;
        }
        internal virtual void OnAwake() { }
        void EnsurePresentationExists()
        {
            if (Instance != null) return;
            CreateEmptyPresentation();
            // Deferred parenting: if the logical parent has no Instance yet, skip silently —
            // the ancestor Awake() ReparentOrphanedChildren() will attach once ready.
            if (Parent != null && Parent.Instance != null)
                PutTransformInside(Parent);
        }
        void ReparentOrphanedChildren()
        {
            if (_childContexts == null || Instance == null) return;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                var child = _childContexts[i];
                if (child.IsDead) continue;
                if (child.Instance == null) continue;
                if (child.Instance.transform.parent != Instance.transform)
                    child.Instance.transform.SetParent(Instance.transform);
            }
        }

        /// <summary>Starts the node (requires awake first). Awakes any not-awaken children, then starts them.</summary>
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
            if (_childContexts != null)
            {                
                for (int i = 0; i < _childContexts.Count; i++)
                {
                    var context = _childContexts[i];
                    if (context.IsDead) continue;
                    if (context.IsAwaken) continue;
                    context.Awake();
                }
                for (int i = 0; i < _childContexts.Count; i++)
                {
                    var context = _childContexts[i];
                    if (context.IsDead) continue;
                    if (context.IsStarted) continue;
                    if (!context.IsAwaken) context.Awake();
                    if (context.IsAwaken) context.Start();
                }
            }

            IsStarted = true;
        }
        internal virtual void OnStart() { }

        /// <summary>Emergency hook only (e.g. wiring that cannot live in System/Domain). Normal gameplay code belongs in <c>System.OnAwake/OnStart/OnTick/OnDestroy</c>.</summary>
        protected virtual void OnCreate() { }

        internal void Tick(float deltaTime)
        {
            if (!IsReady) return;
            if (!IsUpdating) return;
            if (!IsUpdateTick) return;
            if (deltaTime <= 0f) return;
            deltaTime *= TimeScale;

            OnTick(deltaTime);
            if (IsDead) return;
            if (_childContexts != null)
                for (int i = 0; i < _childContexts.Count; i++)
                {
                    var context = _childContexts[i];
                    context.Tick(deltaTime);
                }

            FlushDeadChildren();
            InitializeCreatedChildren();
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
            if (IsDead) return;
            if (_childContexts != null)
                for (int i = 0; i < _childContexts.Count; i++)
                {
                    var context = _childContexts[i];
                    context.FixedTick(deltaTime);
                }

            FlushDeadChildren();
            InitializeCreatedChildren();
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
            if (IsDead) return;
            if (_childContexts != null)
                for (int i = 0; i < _childContexts.Count; i++)
                {
                    var context = _childContexts[i];
                    context.LateTick(deltaTime);
                }

            FlushDeadChildren();
            InitializeCreatedChildren();
        }
        internal virtual void OnLateTick(float deltaTime) { }

        /// <summary>Destroys this node and its whole subtree (children first, reversed), detaches from the parent and releases the <see cref="GameObject"/> via the destroy action. The only way to remove self.</summary>
        public void Destroy()
        {
            if (IsDead) return;
            if (_childContexts != null)
                for (int i = _childContexts.Count - 1; i >= 0; i--)
                {
                    var context = _childContexts[i];
                    context.Destroy();
                }

            Destroying();
            OnDestroy();
            if (Parent != null)
            {
                Parent.ChildDestroy(this);
            }
            else
            {
                // Roots have no parent to mark them dead: do it directly (keeps
                // IsUpdating/IsDead consistent with non-roots) and leave the tick
                // loop so dead roots are not ticked/leaked.
                MarkDead();
                try { CDSP.Entry.EntryPoint.Unregister(this); } catch { }
            }

            if (Instance != null)
            {
                try { _destroyAction(Instance); }
                catch (global::System.Exception e)
                {
                    Debug.LogError($"Destroy action failed for {GetType().Name}: {e.Message}");
                    throw;
                }
            }
        }
        internal virtual void Destroying() { }

        /// <summary>Escape hatch, not a pattern. Runs after children are destroyed; base implementations dispose System/Domain, view and events.</summary>
        protected virtual void OnDestroy() { }
        #endregion
    }
    
    [Serializable]
    /// <summary>Context with data and hierarchy. Domain is created once in <c>Initialize</c>.</summary>
    public abstract class Context<TDomain> : Context
        where TDomain : Domain, new()
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
    
    [Serializable]
    /// <summary>Context with data and logic. Lifecycle calls are sealed into the system — gameplay code lives only in <c>System.OnX</c>.</summary>
    public abstract class Context<TDomain, TSystem> : Context<TDomain>
        where TDomain : Domain, new()
        where TSystem : System<TDomain>, new()
    {
        /// <summary>Node logic. Owns ticks; reach the tree through its <c>Context</c>.</summary>
        public TSystem System { get; internal set; }

        protected internal override void Initialize()
        {
            Domain ??= new TDomain();
            System = new TSystem();
            
            Domain.FillOwner(this);
            
            System.FillContext(this);
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
            System.Destroy();
            Domain.Destroy();
        }
    }
    
    [Serializable]
    /// <summary>Full triple: data + logic + Unity view. Context and system share one view container, so both see the same presentation.</summary>
    public abstract class Context<TDomain, TSystem, TPresentation> : Context<TDomain, TSystem>
        where TDomain : Domain, new()
        where TPresentation : Presentation
        where TSystem : System<TDomain, TPresentation>, new()
    {
        /// <summary>Guaranteed view. Never null-check it: null means a contract violation.</summary>
        public TPresentation Presentation => _presentationContainer.Presentation;
        internal PresentationContainer<TPresentation> _presentationContainer = new();

        protected internal sealed override void Initialize()
        {
            _presentationContainer ??= new();
            
            Domain = new TDomain();
            System = new TSystem();
            
            Domain.FillOwner(this);
            
            System.FillContext(this);
            System.FillDomain(Domain);
            
            System.Initialize(Domain, _presentationContainer);
        }
        
        internal sealed override void OnPresentationDestroy(Presentation presentation)
        {
            // Fallback for views destroyed outside CDSP (Unity OnDestroy).
            // Stale views (already replaced by a hot-swap — notified explicitly in
            // OnPresentationUpdating) and notifications arriving mid-swap (sync destroy
            // inside the swap action, e.g. DestroyImmediate) must not wipe the pending
            // swap state (HadPreviousAwakenPresentation + detached children cache).
            if (_presentationContainer.IsSwapping) return;
            if (!ReferenceEquals(presentation, _presentationContainer.Presentation)) return;
            Presentation?.InternalDispose();
            _presentationContainer.Reset();
            OnPresentationDestroy(presentation as TPresentation);
        }
        /// <summary>Reacts to the view being destroyed or hot-swapped.</summary>
        protected virtual void OnPresentationDestroy(TPresentation presentation) { }
        
        internal sealed override void OnPresentationUpdating(GameObject instance)
        {
            if (instance != null)
            {
                // Cache only logical children (context-owned Instances). View-internal
                // children stay with the old view and are destroyed with it — otherwise
                // hot-swaps accumulate stale internals in the new view.
                var keep = new HashSet<GameObject>();
                if (_childContexts != null && _childContexts.Count > 0)
                {
                    foreach (var ch in _childContexts)
                    {
                        if (ch.IsDead) continue;
                        if (ch.Instance == null) continue;
                        keep.Add(ch.Instance);
                    }
                }
                _presentationContainer.CacheChildren(keep);
            }
            var old = _presentationContainer.Presentation;
            bool oldAwake = old != null && old.IsAwaken;
            _presentationContainer.BeginSwap();
            try
            {
                _presentationContainer.Destroy(_destroyAction);
            }
            finally
            {
                _presentationContainer.EndSwap();
            }
            if (oldAwake)
            {
                // Deterministic old-view notification at the swap point, independent of
                // the destroy action: pooling actions never raise Unity OnDestroy, and the
                // deferred/sync OnDestroy of the replaced view is skipped as stale above.
                // Order mirrors System.PresentationDestroy: context first, then system.
                OnPresentationDestroy(old);
                System.OnPresentationDestroy(old);
            }
        }
        internal sealed override void ValidatePresentation(GameObject newInstance, bool addComponent)
        {
            if (!_presentationContainer.CanFill(newInstance, addComponent))
                throw new NullReferenceException($"Presentation {newInstance.name} doesn't have a Presentation component. {typeof(TPresentation).Name}");
        }
        internal sealed override void OnPresentationUpdated(GameObject instance, bool addComponent)
        {
            bool hadPreviousPresentation = _presentationContainer.HadPreviousAwakenPresentation;
            _presentationContainer.Fill(instance, addComponent);
            if (hadPreviousPresentation)
            {
                _presentationContainer.Presentation?.PresentationAwake(System);
                _presentationContainer.Presentation?.PresentationStart();
                System.OnPresentationUpdated(_presentationContainer.Presentation);
            }
        }
        
        internal override void Destroying()
        {
            base.Destroying();
            // Deterministic final cleanup: do not rely on the deferred Unity OnDestroy
            // (it becomes a guarded duplicate, skipped as stale once Reset() runs).
            var pres = Presentation;
            pres?.InternalDispose();
            _presentationContainer.Reset();
            if (pres != null && pres.IsAwaken)
            {
                OnPresentationDestroy(pres);
                System.OnPresentationDestroy(pres);
            }
        }
    }
    
    [Serializable]
    /// <summary>Full triple plus a per-instance event bus. Publisher and subscriber must use the same instance (usually the nearest common ancestor).</summary>
    public abstract class Context<TDomain, TSystem, TPresentation, TEventKey>
        : Context<TDomain, TSystem, TPresentation>, IEventBus<TEventKey>
        where TDomain : Domain, new()
        where TPresentation : Presentation
        where TSystem : System<TDomain, TPresentation>, new()
        where TEventKey : notnull
    {
        internal readonly EventBus<TEventKey> Events = new();

        /// <summary>Subscribes a plain handler. Keep the returned subscription (dispose to unsubscribe) or <see cref="System.Track"/> it. Do not subscribe/unsubscribe from inside a handler for the same key.</summary>
        public Subscription<TEventKey> Subscribe(TEventKey key, Handler handler) => Events.Subscribe(key, handler);
        /// <summary>Subscribes a typed handler. <see cref="Emit{T}"/> fires typed and plain handlers on the same key.</summary>
        public Subscription<TEventKey> Subscribe<T>(TEventKey key, Handler<T> handler) => Events.Subscribe(key, handler);
        /// <summary>Removes a plain handler.</summary>
        public void Unsubscribe(TEventKey key, Handler handler) => Events.Unsubscribe(key, handler);
        /// <summary>Removes a typed handler.</summary>
        public void Unsubscribe<T>(TEventKey key, Handler<T> handler) => Events.Unsubscribe(key, handler);
        /// <summary>Publishes to plain handlers on <paramref name="key"/>.</summary>
        public void Emit(TEventKey key) => Events.Emit(key);
        /// <summary>Publishes <paramref name="payload"/> to typed handlers and plain handlers on <paramref name="key"/>.</summary>
        public void Emit<T>(TEventKey key, T payload) => Events.Emit(key, payload);

        /// <summary>Clears all subscriptions on this instance.</summary>
        public void ClearEvents() => Events.ClearEvents();
        /// <summary>Clears all subscriptions for <paramref name="key"/> on this instance.</summary>
        public void ClearEvents(TEventKey key) => Events.ClearEvents(key);
        
        internal override void Destroying()
        {
            base.Destroying();
            ClearEvents();
        }
    }
}