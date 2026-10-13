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

        /// <summary>Owned <see cref="GameObject"/>. Child instances are parented under the parent's children slot (<see cref="Presentation.ChildrenRoot"/>, defaults to the view root).</summary>
        public GameObject Instance { get; internal set; }
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

            try
            {
                if (prefab != null)
                {
                    child.CreatePresentation(prefab, GetChildrenAnchor());
                    if (Instance != null)
                        child.PutTransformInside(this);
                }
                child.SetUpdating(isUpdating)
                    .FillParent(this);
                init?.Invoke(child);
            }
            catch
            {
                _childContexts?.Remove(child);
                try { child.Destroy(); } catch { }
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

        /// <summary>Destroys all live children matching <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface). Returns true if any were destroyed.</summary>
        public bool DestroyChild<T>() where T : class
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

        /// <summary>First live child matching <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface), or null. Skips dead children.</summary>
        public T GetChild<T>() where T : class
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
        /// <summary>Nth live child matching <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface), or null. Skips dead children.</summary>
        public T GetChild<T>(int num) where T : class
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

        /// <summary>First live child matching <typeparamref name="T"/>. Silent bool variant of <see cref="GetChild{T}"/>.</summary>
        public bool TryGetChild<T>(out T child) where T : class
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

        /// <summary>True if any live child matching <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface) exists.</summary>
        public bool HasChild<T>() where T : class => GetChild<T>() != null;
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
        /// <summary>Number of live children matching <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface).</summary>
        public int SpecificChildCount<T>() where T : class
        {
            if (_childContexts == null) return 0;
            int count = 0;
            for (int i = 0; i < _childContexts.Count; i++)
                if (!_childContexts[i].IsDead && _childContexts[i] is T)
                    count++;
            return count;
        }

        /// <summary>Fills <paramref name="outList"/> with live children matching <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface, clears first).</summary>
        public void GetChildren<T>(List<T> outList) where T : class
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
            Transform prevParent = null;
            if (root == null && Instance != null)
            {
                try { prevParent = Instance.transform.parent; }
                catch { prevParent = null; }
            }
            var newInstance = _instantiateAction(prefab);
            newInstance.transform.SetParent(root, false);
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
            try
            {
                OnPresentationUpdating(Instance);
            }
            catch
            {
                try { RestoreAfterFailedSwap(); }
                catch (global::System.Exception re)
                {
                    Debug.LogError($"Failed-swap restore failed for {GetType().Name}: {re.Message}");
                }
                try { _destroyAction(newInstance); }
                catch (global::System.Exception ce)
                {
                    Debug.LogError($"Failed-swap cleanup failed for {GetType().Name}: {ce.Message}");
                }
                throw;
            }
            Instance = newInstance;
            if (prevParent != null)
                newInstance.transform.SetParent(prevParent, false);
            try
            {
                OnPresentationUpdated(Instance, addComponent);
            }
            catch
            {
                RestoreAfterFailedSwap();
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
        /// <summary>Rolls a failed hot-swap back: re-attaches temporally parked logical children to the surviving view.</summary>
        internal virtual void RestoreAfterFailedSwap()
        {
            RestoreLogicalChildren();
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
            var anchor = GetChildrenAnchor();
            if (anchor != null)
                CollectKeptChildren(anchor, keep, list);
            CollectKeptChildren(Instance.transform, keep, list);
            _plainTemporal = list.ToArray();
            for (int i = _plainTemporal.Length - 1; i >= 0; i--)
                _plainTemporal[i].transform.SetParent(null, false);
        }
        static void CollectKeptChildren(Transform root, HashSet<GameObject> keep, List<GameObject> list)
        {
            int n = root.childCount;
            for (int i = 0; i < n; i++)
            {
                var go = root.GetChild(i).gameObject;
                if (keep.Contains(go) && !list.Contains(go)) list.Add(go);
            }
        }
        void RestoreLogicalChildren()
        {
            if (_plainTemporal == null || _plainTemporal.Length == 0) { _plainTemporal = null; return; }
            if (Instance == null) { _plainTemporal = null; return; }
            var anchor = GetChildrenAnchor() ?? Instance.transform;
            for (int i = 0; i < _plainTemporal.Length; i++)
            {
                _plainTemporal[i].transform.SetParent(anchor, false);
                _plainTemporal[i].transform.SetAsLastSibling();
            }
            _plainTemporal = null;
        }

        internal void MarkDead()
        {
            IsUpdating = false;
            IsDead = true;
        }
        /// <summary>Reparents only the <see cref="GameObject"/> transform under the parent's children slot, keeping its local transform (scale included). Logical parent, tick ownership and <c>Up{T}</c> chain are unchanged.</summary>
        public Context PutTransformInside(Context parent)
        {
            if (IsDead) return this;
            if (Instance == null || parent == null || parent.Instance == null)
            {
                Debug.LogError(
                    $"Cannot put context {GetType().Name} inside {parent?.GetType().Name ?? "null"} because one of them has no Root GameObject.");
                return this;
            }

            Instance.transform.SetParent(parent.GetChildrenAnchor() ?? parent.Instance.transform, false);
            return this;
        }
        /// <summary>Children slot of this node: where logical children are parented. Defaults to the view root; views override it via <see cref="Presentation.ChildrenRoot"/>.</summary>
        internal virtual Transform GetChildrenAnchor()
        {
            return Instance != null ? Instance.transform : null;
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

        /// <summary>Null-if-dead checked cast to <typeparamref name="T"/> (concrete <see cref="Context"/> type or implemented interface).</summary>
        public T IsValid<T>() where T : class => IsDead ? null : this as T;
        /// <summary>Type check (concrete <see cref="Context"/> type or implemented interface).</summary>
        public bool Is<T>() where T : class => this is T;
        /// <summary>Checked cast to <typeparamref name="T"/> (logs an error and returns null on mismatch).</summary>
        public T As<T>() where T : class
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

        /// <summary>True while the chain from the nearest live <typeparamref name="TContext"/> scope (concrete <see cref="Context"/> type or implemented interface) up to the root is updating. Answers "is the scope running up there", not "am I paused".</summary>
        public bool IsSimulationRunning<TContext>() where TContext : class
            => TryUp(out TContext context) && IsSimulationRunning(context as Context);
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
            if (Parent != null && Parent.Instance != null)
                PutTransformInside(Parent);
        }
        void ReparentOrphanedChildren()
        {
            if (_childContexts == null || Instance == null) return;
            var anchor = GetChildrenAnchor() ?? Instance.transform;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                var child = _childContexts[i];
                if (child.IsDead) continue;
                if (child.Instance == null) continue;
                if (child.Instance.transform.parent != anchor)
                    child.Instance.transform.SetParent(anchor, false);
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
            try
            {
                if (_childContexts != null)
                    for (int i = _childContexts.Count - 1; i >= 0; i--)
                    {
                        var context = _childContexts[i];
                        context.Destroy();
                    }

                Destroying();
                OnDestroy();
            }
            finally
            {
                if (Parent != null)
                {
                    Parent.ChildDestroy(this);
                }
                else
                {
                    MarkDead();
                    try { CDSP.Entry.EntryPoint.Unregister(this); } catch { }
                }
            }

            if (Instance != null)
                _destroyAction(Instance);
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
            try { System.Destroy(); }
            finally { Domain.Destroy(); }
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

        /// <summary>Children slot of this node: the bound view's <see cref="Presentation.ChildrenRoot"/>, view root when unbound.</summary>
        internal override Transform GetChildrenAnchor()
        {
            var p = Presentation;
            if (p != null && p.ChildrenRoot != null) return p.ChildrenRoot;
            return Instance != null ? Instance.transform : null;
        }

        /// <summary>Bound view if already set, else searches own <see cref="Context.Instance"/> (self, then children including inactive) without creating anything. Binds and — when the context already runs — awakes the found view immediately. Null when missing. Use instead of <c>CreatePresentation</c> when the view already lives in the hierarchy (e.g. nested inside another presentation).</summary>
        public TPresentation FindPresentation(bool includeChildren = true)
            => FindPresentation(Instance, includeChildren);

        /// <summary>Searches the <paramref name="scope"/> subtree for the view and binds it without instantiating anything (hot-swap parity, but reusing instead of creating). A view found outside the current instance is adopted: its <see cref="GameObject"/> becomes <see cref="Context.Instance"/>, live logical children parked under the previous root are re-attached to it, the previous root is released via the destroy action (the searched scope itself is never destroyed). Ownership transfers — destroying the context later releases the adopted root like a created one. A failed release aborts loudly and keeps the previous root. State is untouched and null is returned when missing.</summary>
        /// <param name="alignTransform">When true (default), the adopted root is parented under the logical parent when ready, so the Unity hierarchy mirrors the tree. Pass false to keep the found placement as is (breaks mirroring).</param>
        public TPresentation FindPresentation(GameObject scope, bool includeChildren = true, bool alignTransform = true)
        {
            if (IsDead) return null;
            var bound = _presentationContainer.Presentation;
            if (bound != null) return bound;
            if (scope == null) return null;
            var found = _presentationContainer.FindIn(scope, includeChildren);
            if (found == null) return null;
            AdoptFoundPresentation(scope, found, alignTransform);
            return _presentationContainer.Presentation;
        }

        /// <summary>Silent bool variant of <see cref="FindPresentation(bool)"/>.</summary>
        public bool TryFindPresentation(out TPresentation presentation, bool includeChildren = true)
        {
            presentation = FindPresentation(includeChildren);
            return presentation != null;
        }

        /// <summary>Silent bool variant of <see cref="FindPresentation(GameObject, bool, bool)"/>.</summary>
        public bool TryFindPresentation(GameObject scope, out TPresentation presentation, bool includeChildren = true, bool alignTransform = true)
        {
            presentation = FindPresentation(scope, includeChildren, alignTransform);
            return presentation != null;
        }

        void AdoptFoundPresentation(GameObject scope, TPresentation found, bool alignTransform)
        {
            var foundGO = found.gameObject;
            if (found.System != null && !ReferenceEquals(found.System, System))
            {
                Debug.LogError(
                    $"FindPresentation: {typeof(TPresentation).Name} on {foundGO.name} is already owned by {found.System.GetType().Name}. Binding it to {GetType().Name} steals the view.");
            }
            bool adopted = false;
            List<GameObject> parked = null;
            if (Instance == null)
            {
                Instance = foundGO;
                adopted = true;
            }
            else if (Instance != foundGO)
            {
                bool nested = false;
                try { nested = foundGO.transform.IsChildOf(Instance.transform); }
                catch (global::System.Exception) { nested = false; }
                if (!nested)
                {
                    var prev = Instance;
                    parked = DetachLogicalChildren(prev);
                    try
                    {
                        if (prev != null && prev != scope)
                            _destroyAction(prev);
                    }
                    catch
                    {
                        ReattachParkedChildren(parked, prev);
                        throw;
                    }
                    Instance = foundGO;
                    adopted = true;
                }
            }
            if (adopted && alignTransform)
                AlignInstanceWithParent();
            _presentationContainer.FillExisting(Instance, found);
            if (adopted && parked != null)
                ReattachParkedChildren(parked, Instance);
            if (IsAwaken && !found.IsAwaken)
            {
                found.PresentationAwake(System);
                if (IsStarted && !found.IsStarted)
                    found.PresentationStart();
                System.OnPresentationUpdated(found);
            }
        }

        List<GameObject> DetachLogicalChildren(GameObject prev)
        {
            if (_childContexts == null || _childContexts.Count == 0) return null;
            if (prev == null) return null;
            List<GameObject> parked = null;
            for (int i = 0; i < _childContexts.Count; i++)
            {
                var ch = _childContexts[i];
                if (ch.IsDead) continue;
                var go = ch.Instance;
                if (go == null || go == prev) continue;
                bool under = false;
                try { under = go.transform.parent == prev.transform || go.transform.IsChildOf(prev.transform); }
                catch (global::System.Exception) { under = false; }
                if (!under) continue;
                parked ??= new List<GameObject>(_childContexts.Count);
                parked.Add(go);
                try { go.transform.SetParent(null, false); }
                catch (global::System.Exception) { parked.Remove(go); }
            }
            return parked;
        }

        void ReattachParkedChildren(List<GameObject> parked, GameObject root)
        {
            if (parked == null || parked.Count == 0) return;
            if (root == null) return;
            var anchor = GetChildrenAnchor() ?? root.transform;
            for (int i = 0; i < parked.Count; i++)
            {
                var go = parked[i];
                if (go == null) continue;
                try
                {
                    go.transform.SetParent(anchor, false);
                    go.transform.SetAsLastSibling();
                }
                catch (global::System.Exception e)
                {
                    Debug.LogError($"FindPresentation could not re-attach {go.name} under {root.name}: {e.Message}");
                }
            }
        }

        void AlignInstanceWithParent()
        {
            var par = Parent;
            if (par == null || par.Instance == null) return;
            if (Instance == null || Instance == par.Instance) return;
            try
            {
                var anchor = par.GetChildrenAnchor() ?? par.Instance.transform;
                if (Instance.transform.parent != anchor)
                    PutTransformInside(par);
            }
            catch (global::System.Exception e)
            {
                Debug.LogError($"FindPresentation could not align {GetType().Name} under {par.GetType().Name}: {e.Message}");
            }
        }

        internal sealed override void OnPresentationDestroy(Presentation presentation)
        {
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
                OnPresentationDestroy(old);
                System.OnPresentationDestroy(old);
            }
        }
        internal sealed override void ValidatePresentation(GameObject newInstance, bool addComponent)
        {
            if (!_presentationContainer.CanFill(newInstance, addComponent))
                throw new NullReferenceException($"Presentation {newInstance.name} doesn't have a Presentation component. {typeof(TPresentation).Name}");
        }
        internal sealed override void RestoreAfterFailedSwap()
        {
            _presentationContainer.ReattachChildren();
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
            try { base.Destroying(); }
            finally
            {
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
            try { base.Destroying(); }
            finally { ClearEvents(); }
        }
    }
}
