using UnityEditor;
using UnityEngine;

namespace FFH.Utilities.SceneManagement
{
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(SceneLoader))]
    public class SceneLoaderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SceneLoader sceneLoader = (SceneLoader)target;
            if (sceneLoader == null || sceneLoader.SceneLists == null)
            {
                return;
            }

            EditorGUILayout.Space();

            bool removedScene = false;

            for (int listIndex = 0; listIndex < sceneLoader.SceneLists.Count; listIndex++)
            {
                SceneLoader.SceneList sceneList = sceneLoader.SceneLists[listIndex];
                if (sceneList == null)
                {
                    continue;
                }

                EditorGUILayout.LabelField(sceneList.listName, EditorStyles.boldLabel);

#if UNITY_EDITOR
                for (int sceneIndex = 0; sceneIndex < sceneList.scenes.Count; sceneIndex++)
                {
                    UnityEngine.Object scene = sceneList.scenes[sceneIndex];

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(scene, typeof(SceneAsset), false);

                    GUI.enabled = scene != null;
                    if (GUILayout.Button("Load"))
                    {
                        if (Application.isPlaying)
                        {
                            sceneLoader.LoadSceneSequential(AssetDatabase.GetAssetPath(scene), false);
                        }
                        else
                        {
                            sceneLoader.LoadSceneEditor(scene);
                        }
                    }

                    if (GUILayout.Button("Unload"))
                    {
                        if (Application.isPlaying)
                        {
                            sceneLoader.UnloadSceneSequential(AssetDatabase.GetAssetPath(scene));
                        }
                        else
                        {
                            sceneLoader.UnloadSceneEditor(scene);
                        }
                    }
                    GUI.enabled = true;

                    GUI.enabled = !Application.isPlaying;
                    if (GUILayout.Button("Remove"))
                    {
                        RemoveSceneFromList(sceneLoader, sceneList, sceneIndex);
                        removedScene = true;
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    GUI.enabled = true;

                    EditorGUILayout.EndHorizontal();
                }
#endif

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"Load All Scenes in {sceneList.listName}"))
                {
                    sceneLoader.LoadAllScenesInListSequential(sceneList.listName, true);
                }

                if (GUILayout.Button($"Unload All Scenes in {sceneList.listName}"))
                {
                    sceneLoader.UnloadAllScenesInListSequential(sceneList.listName);
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button($"Switch To This List ({sceneList.listName})"))
                {
                    sceneLoader.SwitchToSceneListSequential(
                        sceneList.listName,
                        SceneLoader.SwitchOrder.LoadMissingThenUnloadObsolete,
                        true);
                }

                EditorGUILayout.Space();

                if (removedScene)
                {
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space();
            GUI.enabled = sceneLoader.IsOperationRunning;
            if (GUILayout.Button("Cancel Current Runtime Operation"))
            {
                sceneLoader.CancelCurrentOperation();
            }
            GUI.enabled = true;
        }

#if UNITY_EDITOR
        private static void RemoveSceneFromList(SceneLoader sceneLoader, SceneLoader.SceneList sceneList, int sceneIndex)
        {
            if (sceneLoader == null || sceneList == null)
            {
                return;
            }

            if (sceneIndex < 0 || sceneIndex >= sceneList.scenes.Count)
            {
                return;
            }

            Undo.RecordObject(sceneLoader, "Remove Scene From SceneLoader List");
            sceneList.scenes.RemoveAt(sceneIndex);
            sceneList.SyncRuntimeDataFromAssets();
            EditorUtility.SetDirty(sceneLoader);
        }
#endif
    }

}