using System;
using System.Collections.Generic;

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
                var anchor = ResolveAnchor();
                for (int i = 0; i < _temporalChildContent.Length; i++)
                {
                    _temporalChildContent[i].transform.SetParent(anchor);
                    _temporalChildContent[i].transform.SetAsLastSibling();
                }
                _temporalChildContent = null;
            }
            HadPreviousAwakenPresentation = false;
        }
        Transform ResolveAnchor()
        {
            if (Presentation != null && Presentation.ChildrenRoot != null)
                return Presentation.ChildrenRoot;
            return _gameObject.transform;
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
        public bool CanFill(GameObject gameObject, bool addComponent)
        {
            if (gameObject == null) return false;
            if (addComponent) return true;
            if (gameObject.GetComponent<TPresentation>() != null) return true;
            if (gameObject.GetComponentInChildren<TPresentation>() != null) return true;
            return false;
        }
        public TPresentation FindIn(GameObject root, bool includeChildren)
        {
            if (root == null) return null;
            var self = root.GetComponent<TPresentation>();
            if (self != null) return self;
            if (includeChildren) return root.GetComponentInChildren<TPresentation>(true);
            return null;
        }
        public void FillExisting(GameObject owner, TPresentation existing)
        {
            if (owner == null) throw new NullReferenceException($"Cannot bind presentation {typeof(TPresentation).Name} to null owner.");
            if (existing == null) throw new NullReferenceException($"Cannot bind null presentation {typeof(TPresentation).Name} to {owner.name}.");
            _gameObject = owner;
            Presentation = existing;
            Presentation.ResetPresentation();
            _temporalChildContent = null;
            HadPreviousAwakenPresentation = false;
        }
        public void CacheChildren(HashSet<GameObject> keep = null)
        {
            if (_gameObject == null) return;
            if (IsValid && Presentation.IsAwaken) HadPreviousAwakenPresentation = true;

            if (keep == null)
            {
                int n = _gameObject.transform.childCount;
                _temporalChildContent = new GameObject[n];
                for (int i = 0; i < n; i++)
                    _temporalChildContent[i] = _gameObject.transform.GetChild(i).gameObject;
                for (int i = n - 1; i >= 0; i--)
                    _temporalChildContent[i].transform.SetParent(null);
                return;
            }

            if (keep.Count == 0) { _temporalChildContent = null; return; }
            var list = new List<GameObject>(keep.Count);
            var anchor = ResolveAnchor();
            if (anchor != null)
                CollectKeptChildren(anchor, keep, list);
            CollectKeptChildren(_gameObject.transform, keep, list);
            _temporalChildContent = list.Count == 0 ? null : list.ToArray();
            if (_temporalChildContent != null)
                for (int i = _temporalChildContent.Length - 1; i >= 0; i--)
                    _temporalChildContent[i].transform.SetParent(null);
        }
        public void Destroy(Action<GameObject> _destroyAction)
        {
            if (_gameObject == null) return;
            if (IsValid) Presentation.InternalDispose();
            _destroyAction(_gameObject);
        }

        /// <summary>Re-attaches temporally parked children back to the surviving view (failed-swap rollback).</summary>
        public void ReattachChildren()
        {
            HadPreviousAwakenPresentation = false;
            if (_temporalChildContent == null || _temporalChildContent.Length == 0)
            {
                _temporalChildContent = null;
                return;
            }

            if (_gameObject != null)
            {
                var anchor = ResolveAnchor();
                for (int i = 0; i < _temporalChildContent.Length; i++)
                {
                    if (_temporalChildContent[i] == null) continue;
                    _temporalChildContent[i].transform.SetParent(anchor);
                    _temporalChildContent[i].transform.SetAsLastSibling();
                }
            }
            _temporalChildContent = null;
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