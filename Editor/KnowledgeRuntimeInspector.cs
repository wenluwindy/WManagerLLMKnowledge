using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    [CustomEditor(typeof(KnowledgeRuntime))]
    internal sealed class KnowledgeRuntimeInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var runtime = (KnowledgeRuntime)target;
            if (GUILayout.Button("设置当前包的编辑器原生库路径"))
            {
                Undo.RecordObject(runtime, "Set Knowledge backend path");
                runtime.EditorNativeLibraryDirectory = Path.Combine(PackageInfo.FindForAssembly(typeof(KnowledgeRuntimeInspector).Assembly).resolvedPath, "Plugins/Windows/x86_64");
                EditorUtility.SetDirty(runtime);
            }
        }
    }
}
