using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WManager.Knowledge.Samples;

namespace WManager.Knowledge.Editor
{
    public static class KnowledgeDemoSceneBuilder
    {
        public const string ScenePath = "Assets/KnowledgeDemo/KnowledgeUGUIDemo.unity";

        [MenuItem("Tools/WManager/Create uGUI Knowledge Demo")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath)) { EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)); return; }
            Directory.CreateDirectory("Assets/KnowledgeDemo");
            AssetDatabase.Refresh();
            const string settingsPath = "Assets/KnowledgeDemo/DemoSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<KnowledgeSettings>(settingsPath);
            if (settings == null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<KnowledgeSettings>("Assets/Settings/Knowledge/KnowledgeSettings.asset");
                settings = existing != null ? UnityEngine.Object.Instantiate(existing) : ScriptableObject.CreateInstance<KnowledgeSettings>();
                settings.name = "DemoSettings";
                settings.databaseName = "ugui-demo.db";
                settings.seedDatabase = ""; settings.seedManifest = "";
                AssetDatabase.CreateAsset(settings, settingsPath);
                AssetDatabase.SaveAssets();
            }
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("Knowledge Demo");
                var runtime = root.AddComponent<KnowledgeRuntime>(); runtime.Settings = settings;
                runtime.EditorNativeLibraryDirectory = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(KnowledgeRuntime).Assembly).resolvedPath, "Plugins/Windows/x86_64");
                var serialized = new SerializedObject(runtime);
                serialized.FindProperty("initializeOnStart").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
                var demo = root.AddComponent<KnowledgeUGUIDemo>();
                var demoProperties = new SerializedObject(demo);
                demoProperties.FindProperty("chineseFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/MiSans-Regular.ttf");
                demoProperties.ApplyModifiedPropertiesWithoutUndo();
                demo.BuildUI();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save the demo scene.");
                Debug.Log("[Knowledge] uGUI Demo scene created: " + ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }

    }
}
