
using System.Collections.Generic;
using CDSP.Base;
using UnityEngine;

namespace CDSP.Entry
{
    /// <summary>Registry of root context trees and the tick loop entry. Roots are added by <c>ContextRoot</c>.</summary>
    public static class EntryPoint
    {
        static readonly List<Context> _mainContextsList = new();
        /// <summary>Read-only list of registered root trees.</summary>
        public static IReadOnlyList<Context> MainContextsList => _mainContextsList;

        /// <summary>Nearest live root of type <typeparamref name="T"/>, or null. For things with no natural ancestor (e.g. UI finding the session); prefer <c>Up{T}</c> otherwise.</summary>
        public static T FindRoot<T>() where T : Context
        {
            for (int i = 0; i < _mainContextsList.Count; i++)
            {
                if (_mainContextsList[i].IsDead) continue;
                if (_mainContextsList[i] is T t)
                    return t;
            }
            return null;
        }

        /// <summary>Adds a root tree to the tick loop (called by ContextRoot). Idempotent.</summary>
        public static bool Register(Context context)
        {
            if (context == null)
            {
                Debug.LogError("Cannot register null context.");
                return false;
            }
            if (_mainContextsList.Contains(context))
            {
                Debug.LogError("Cannot register existing context.");
                return false;
            }
            _mainContextsList.Add(context);
            Debug.Log(context.ToString());
            return true;
        }

        /// <summary>Removes a root tree from the tick loop. Idempotent. Does NOT destroy it.</summary>
        public static bool Unregister(Context context)
        {
            if (context == null) return false;
            return _mainContextsList.Remove(context);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _mainContextsList.Clear();
            EntryPointUpdate.Init();
        }
    }
}
