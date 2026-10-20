#if UNITY_EDITOR
using System.Collections.Generic;

using CDSP.Base;

using UnityEditor;

using UnityEngine;

namespace CDSP.Editor
{
    [CustomEditor(typeof(Presentation), true)]
    public sealed class PresentationChildrenRootEditor : UnityEditor.Editor
    {
        SerializedProperty _childrenRoot;

        void OnEnable()
        {
            _childrenRoot = serializedObject.FindProperty("_childrenRoot");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "_childrenRoot");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("CDSP Children", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Logical children are parented under this root instead of the view root.", MessageType.None);

            var pres = (Presentation)target;
            if (targets.Length != 1)
            {
                EditorGUILayout.HelpBox("Select a single presentation to pick the children root.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var current = pres.ChildrenRoot;
            string caption = current == pres.transform
                ? "Self (view root)"
                : RelativePath(pres.transform, current);
            var rect = EditorGUILayout.GetControlRect();
            if (EditorGUI.DropdownButton(rect, new GUIContent($"Children Root: {caption}"), FocusType.Keyboard))
                ShowMenu(pres, rect);

            serializedObject.ApplyModifiedProperties();
        }

        void ShowMenu(Presentation pres, Rect rect)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Self (view root)"),
                _childrenRoot.objectReferenceValue == null,
                () => SetRoot(null));
            menu.AddSeparator("");
            var all = pres.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == pres.transform) continue;
                var captured = t;
                menu.AddItem(
                    new GUIContent(RelativePath(pres.transform, t)),
                    _childrenRoot.objectReferenceValue == t,
                    () => SetRoot(captured));
            }
            menu.DropDown(rect);
        }

        void SetRoot(Transform root)
        {
            Undo.RecordObject(target, "Set Children Root");
            _childrenRoot.objectReferenceValue = root;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        static string RelativePath(Transform root, Transform t)
        {
            var parts = new List<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
