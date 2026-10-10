using System;

using UnityEngine;

namespace CDSP.Base
{
    internal class PresentationContainer<TPresentation>
        where TPresentation : Presentation
    {
        GameObject _gameObject;
        GameObject[] _temporalChildContent;
        
        public bool HadPreviousAwakenPresentation  { get; private set; }
        public TPresentation Presentation { get; private set; }
        public bool IsValid => Presentation != null;

        /// <summary>True while a hot-swap replacement is in flight (between <see cref="CacheChildren"/>/<see cref="Destroy"/> and <see cref="Fill"/>). Deferred/synchronous <c>OnDestroy</c> of the replaced view must not wipe the pending swap state.</summary>
        public bool IsSwapping { get; private set; }
        public void BeginSwap() => IsSwapping = true;
        public void EndSwap() => IsSwapping = false;
        
        public void Fill(GameObject gameObject, bool addComponent)
        {
            _gameObject = gameObject;
            Presentation = gameObject.GetComponent<TPresentation>();
            if (Presentation == null) Presentation = gameObject.GetComponentInChildren<TPresentation>();
            if (Presentation == null)
            {
                if (addComponent)
                    Presentation = gameObject.AddComponent<TPresentation>();
                else
                    throw new NullReferenceException($"Presentation {gameObject.name} doesn't have a Presentation component. {typeof(TPresentation).Name}");
            }

            Presentation.ResetPresentation();

            if (_temporalChildContent != null 
                && _temporalChildContent.Length > 0)
            {
                for (int i = 0; i < _temporalChildContent.Length; i++)
                {
                    _temporalChildContent[i].transform.SetParent(gameObject.transform);
                    _temporalChildContent[i].transform.SetAsLastSibling();
                }
                _temporalChildContent = null;
            }
            HadPreviousAwakenPresentation = false;
        }
        public void CacheChildren()
        {
            if (_gameObject == null) return;
            if (IsValid && Presentation.IsAwaken) HadPreviousAwakenPresentation = true;
            
            int n = _gameObject.transform.childCount;
            _temporalChildContent = new GameObject[n];
            for (int i = 0; i < n; i++)
                _temporalChildContent[i] = _gameObject.transform.GetChild(i).gameObject;
            for (int i = n-1; i >= 0; i--)
                _temporalChildContent[i].transform.SetParent(null);
        }
        public void Destroy(Action<GameObject> _destroyAction)
        {
            if (_gameObject == null) return;
            if (IsValid) Presentation.InternalDispose();
            _destroyAction(_gameObject);
        }

        public void Reset()
        {
            _gameObject = null;
            _temporalChildContent = null;
            Presentation  = null;
            HadPreviousAwakenPresentation = false;
        }
    }
}