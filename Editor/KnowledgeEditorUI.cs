using UnityEditor;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
#endif

namespace WManager.Knowledge.Editor
{
    // Keep the exported package usable in projects that do not have Odin installed.
    internal sealed class KnowledgeEditorUI : System.IDisposable
    {
        private Object target;
#if ODIN_INSPECTOR
        private PropertyTree tree;
#else
        private SerializedObject serialized;
#endif

        internal void DrawSettings(Object value)
        {
            if (value == null) return;
            if (target != value)
            {
                Dispose();
                target = value;
#if ODIN_INSPECTOR
                tree = PropertyTree.Create(value);
                tree.DrawMonoScriptObjectField = false;
#else
                serialized = new SerializedObject(value);
#endif
            }
#if ODIN_INSPECTOR
            tree.Draw(true);
#else
            serialized.Update();
            var iterator = serialized.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.name != "m_Script") EditorGUILayout.PropertyField(iterator, true);
            }
            serialized.ApplyModifiedProperties();
#endif
        }

        internal static void BeginSection(string title)
        {
#if ODIN_INSPECTOR
            SirenixEditorGUI.BeginBox(title);
#else
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
#endif
        }

        internal static void EndSection()
        {
#if ODIN_INSPECTOR
            SirenixEditorGUI.EndBox();
#else
            EditorGUILayout.EndVertical();
#endif
        }

        public void Dispose()
        {
#if ODIN_INSPECTOR
            tree?.Dispose();
            tree = null;
#else
            serialized?.Dispose();
            serialized = null;
#endif
            target = null;
        }
    }
}
