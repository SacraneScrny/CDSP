using UnityEngine;

namespace CDSP.Utils
{
    /// <summary>Shared hidden template for empty presentation nodes. Never lands in the saved scene.</summary>
    public static class EmptyGameObject
    {
        static GameObject _instance;
        /// <summary>Hidden (<see cref="HideFlags.HideAndDontSave"/>) template object.</summary>
        public static GameObject Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameObject
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _instance = null;
        }
    }
}
