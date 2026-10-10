using UnityEngine;

namespace CDSP.Utils
{
    /// <summary>Default birth/death actions for context instances (<see cref="Object.Instantiate"/> / <see cref="Object.Destroy"/>). Override per context for pooling or addressables.</summary>
    public static class DefaultActions
    {
        /// <summary>Default instantiate: <see cref="Object.Instantiate(Object)"/>.</summary>
        public static GameObject InstantiateAction(GameObject prefab)
        {
            return Object.Instantiate(prefab);
        }
    }
}
