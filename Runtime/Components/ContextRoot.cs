using System;

using CDSP.Base;
using CDSP.Entry;

using UnityEngine;

namespace CDSP.Components
{
    /// <summary>Scene root of a context tree. Owns the root context: creates it in <c>Awake</c>, registers it in the tick loop, destroys it in <c>OnDestroy</c>.</summary>
    public abstract class ContextRoot : MonoBehaviour
    {
        /// <summary>Optional root view. Null wakes the root as an empty <c>{GO}_ROOT</c> node for pure-logic roots.</summary>
        public GameObject Prefab;

        void OnValidate()
        {
            if (string.IsNullOrEmpty(gameObject.name))
                gameObject.name = this.GetType().Name;
        }

        /// <summary>Root context created in <c>Awake</c>. Null after <c>OnDestroy</c>.</summary>
        public Context Context { get; protected set; }
        /// <summary>Spawns initial children and configures the root. Runs once per lifetime (not on every enable), after the root presentation exists.</summary>
        public abstract void Bootstrap();
        protected abstract void CreateContext();

        /// <summary>Creates the context, registers it in the tick loop and bootstraps. Runs once per lifetime.</summary>
        public void Awake()
        {
            if (Context != null)
            {
                Debug.LogError($"ContextRoot {GetType().Name} is already awaken (double Awake leaks the previous context).");
                return;
            }
            CreateContext();
            EntryPoint.Register(Context);
            try
            {
                Bootstrap();
            }
            catch
            {
                EntryPoint.Unregister(Context);
                var ctx = Context;
                Context = null;
                try { ctx.Destroy(); } catch { }
                throw;
            }
        }
        /// <summary>Unregisters from the tick loop and destroys the whole tree. Disabling the <see cref="GameObject"/> does not destroy anything.</summary>
        public void OnDestroy()
        {
            if (Context == null) return;
            var ctx = Context;
            Context = null;
            EntryPoint.Unregister(ctx);
            ctx.Destroy();
        }
    }
    /// <summary>Scene root for a context of type <typeparamref name="TContext"/>. Never create the context with <c>new</c> in <c>Awake</c> yourself — the root owns that.</summary>
    public abstract class ContextRoot<TContext> : ContextRoot
        where TContext : Context, new()
    {
        protected override void CreateContext()
        {
            Context = new TContext();
        }

        /// <summary>Sealed bootstrap: creates the root presentation first (<see cref="ContextRoot.Prefab"/> or an empty node), then calls <see cref="Bootstrap(TContext)"/>, then awakes and starts.</summary>
        public sealed override void Bootstrap()
        {
            if (Prefab != null)
                Context.CreatePresentation(Prefab, transform);
            else
                Context.CreateEmptyPresentation(transform, $"{gameObject.name}_ROOT");
            Bootstrap(Context as TContext);
            Context.AwakeAndStart();
        }
        /// <summary>Spawns initial children and configures the root context.</summary>
        public virtual void Bootstrap(TContext context) { }
    }
    /// <summary>Scene root with an Inspector-assigned domain. Swaps the serialized <typeparamref name="TDomain"/> into the fresh context.</summary>
    public abstract class ContextRoot<TContext, TDomain> : ContextRoot
        where TContext : Context<TDomain>, new()
        where TDomain : Domain, new()
    {
        [SerializeField] protected TDomain Domain;

        protected override void CreateContext()
        {
            Context = new TContext();
            Domain ??= new();
            (Context as TContext)?.UpdateDomain(Domain);
        }

        /// <summary>Sealed bootstrap: creates the root presentation first, then calls <see cref="Bootstrap(TContext, TDomain)"/>, then awakes and starts.</summary>
        public sealed override void Bootstrap()
        {
            if (Prefab != null)
                Context.CreatePresentation(Prefab, transform);
            else
                Context.CreateEmptyPresentation(transform, $"{gameObject.name}_ROOT");
            Bootstrap(Context as TContext, Domain);
            Context.AwakeAndStart();
        }
        /// <summary>Spawns initial children and configures the root context and domain.</summary>
        public virtual void Bootstrap(TContext context, TDomain domain) { }
    }
}
