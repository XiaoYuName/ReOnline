using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    public class OdinScriptableManager<T> : SerializedScriptableObject
        where T : OdinScriptableManager<T>
    {
        public static T Instance { get; private set; }

        protected virtual void OnEnable()
        {
            Instance = this as T;
        }

        protected virtual void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}