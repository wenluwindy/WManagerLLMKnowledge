using UnityEditor;
using UnityEngine;

namespace WManager.Knowledge.Editor
{
    [InitializeOnLoad]
    internal static class KnowledgeRuntimeLifecycle
    {
        static KnowledgeRuntimeLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Shutdown();
            };
        }

        private static void Shutdown()
        {
            foreach (var runtime in UnityEngine.Object.FindObjectsByType<KnowledgeRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                runtime.Shutdown();
        }
    }
}
