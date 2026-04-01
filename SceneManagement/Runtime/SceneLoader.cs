using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace FFH.Utilities.SceneManagement
{
    public class SceneLoader : MonoBehaviour
    {
        [Header("Startup")]
        [SerializeField] private bool loadAtStart;
        [SerializeField] private int loadAtStartListIndex;
        [SerializeField] private int loadAtStartSceneIndex;

        [Header("Runtime Behavior")]
        [SerializeField] private bool cancelRunningOperationOnNewRequest = true;
        [SerializeField] private bool setFirstLoadedSceneActive = true;
        [SerializeField] private bool logOperations = true;

        [Header("Scene Lists")]
        [SerializeField] private List<SceneList> sceneLists = new();

        private CancellationTokenSource _operationCts;
        private bool _isOperationRunning;

        public bool IsOperationRunning => _isOperationRunning;
        public string CurrentOperationName { get; private set; }
        public IReadOnlyList<SceneList> SceneLists => sceneLists;

        [Serializable]
        public class SceneList
        {
            public string listName = "Scene List";

#if UNITY_EDITOR
            public List<UnityEngine.Object> scenes = new();
#endif

            [SerializeField] private List<string> scenePaths = new();
            public IReadOnlyList<string> ScenePaths => scenePaths;

#if UNITY_EDITOR
            public void SyncRuntimeDataFromAssets()
            {
                if (scenePaths == null)
                {
                    scenePaths = new List<string>();
                }

                scenePaths.Clear();

                if (scenes == null)
                {
                    return;
                }

                for (int i = 0; i < scenes.Count; i++)
                {
                    UnityEngine.Object sceneAsset = scenes[i];
                    if (sceneAsset == null)
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(sceneAsset);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    scenePaths.Add(path);
                }
            }
#endif
        }

        public enum SwitchOrder
        {
            LoadMissingThenUnloadObsolete,
            UnloadObsoleteThenLoadMissing
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || !loadAtStart)
            {
                return;
            }

            if (!TryGetSceneList(loadAtStartListIndex, out SceneList sceneList))
            {
                return;
            }

            if (!TryGetScenePathAt(sceneList, loadAtStartSceneIndex, out string scenePath))
            {
                return;
            }

            LoadSceneSequential(scenePath, setFirstLoadedSceneActive);
        }

        private void OnDisable()
        {
            CancelCurrentOperation();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (sceneLists == null)
            {
                return;
            }

            for (int i = 0; i < sceneLists.Count; i++)
            {
                sceneLists[i]?.SyncRuntimeDataFromAssets();
            }

            EditorUtility.SetDirty(this);
        }

        [ContextMenu("Sync Scene Paths")]
        public void SyncScenePaths()
        {
            OnValidate();
        }
#endif

        public void CancelCurrentOperation()
        {
            if (_operationCts == null)
            {
                return;
            }

            _operationCts.Cancel();
        }

        public void LoadSceneSequential(string scenePathOrName, bool makeActive = false)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                LoadSceneEditor(scenePathOrName);
                return;
            }
#endif

            _ = RunExclusiveAsync(
                $"Load scene: {scenePathOrName}",
                token => LoadSceneInternalAsync(scenePathOrName, makeActive, token));
        }

        public void UnloadSceneSequential(string scenePathOrName)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnloadSceneEditor(scenePathOrName);
                return;
            }
#endif

            _ = RunExclusiveAsync(
                $"Unload scene: {scenePathOrName}",
                token => UnloadSceneInternalAsync(scenePathOrName, token));
        }

        public void LoadAllScenesInListSequential(string listName, bool makeFirstSceneActive = true)
        {
            if (!TryGetSceneList(listName, out SceneList sceneList))
            {
                Debug.LogWarning($"Scene list '{listName}' was not found.", this);
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                for (int i = 0; i < sceneList.ScenePaths.Count; i++)
                {
                    LoadSceneEditor(sceneList.ScenePaths[i]);
                }
                return;
            }
#endif

            _ = RunExclusiveAsync(
                $"Load scene list: {listName}",
                token => LoadSceneListInternalAsync(sceneList, makeFirstSceneActive, token));
        }

        public void UnloadAllScenesInListSequential(string listName)
        {
            if (!TryGetSceneList(listName, out SceneList sceneList))
            {
                Debug.LogWarning($"Scene list '{listName}' was not found.", this);
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                for (int i = 0; i < sceneList.ScenePaths.Count; i++)
                {
                    UnloadSceneEditor(sceneList.ScenePaths[i]);
                }
                return;
            }
#endif

            _ = RunExclusiveAsync(
                $"Unload scene list: {listName}",
                token => UnloadSceneListInternalAsync(sceneList, token));
        }

        public void SwitchToSceneListSequential(
            string listName,
            SwitchOrder switchOrder = SwitchOrder.LoadMissingThenUnloadObsolete,
            bool makeFirstSceneActive = true)
        {
            if (!TryGetSceneList(listName, out SceneList sceneList))
            {
                Debug.LogWarning($"Scene list '{listName}' was not found.", this);
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                for (int i = 0; i < sceneList.ScenePaths.Count; i++)
                {
                    LoadSceneEditor(sceneList.ScenePaths[i]);
                }
                return;
            }
#endif

            _ = RunExclusiveAsync(
                $"Switch to scene list: {listName}",
                token => SwitchToSceneListInternalAsync(sceneList, switchOrder, makeFirstSceneActive, token));
        }

        private async Awaitable RunExclusiveAsync(string operationName, Func<CancellationToken, Awaitable> operation)
        {
            if (cancelRunningOperationOnNewRequest && _isOperationRunning)
            {
                CancelCurrentOperation();
            }

            while (_isOperationRunning)
            {
                await Awaitable.NextFrameAsync();
            }

            _isOperationRunning = true;
            CurrentOperationName = operationName;
            _operationCts = new CancellationTokenSource();

            try
            {
                await Awaitable.MainThreadAsync();
                Log($"Started: {operationName}");
                await operation(_operationCts.Token);
                Log($"Completed: {operationName}");
            }
            catch (OperationCanceledException)
            {
                Log($"Cancelled: {operationName}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                _operationCts?.Dispose();
                _operationCts = null;
                CurrentOperationName = null;
                _isOperationRunning = false;
            }
        }

        private async Awaitable LoadSceneListInternalAsync(SceneList sceneList, bool makeFirstSceneActive, CancellationToken token)
        {
            if (sceneList == null)
            {
                return;
            }

            for (int i = 0; i < sceneList.ScenePaths.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                string scenePath = sceneList.ScenePaths[i];
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                bool makeActive = makeFirstSceneActive && i == 0;
                await LoadSceneInternalAsync(scenePath, makeActive, token);
            }
        }

        private async Awaitable UnloadSceneListInternalAsync(SceneList sceneList, CancellationToken token)
        {
            if (sceneList == null)
            {
                return;
            }

            for (int i = sceneList.ScenePaths.Count - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();

                string scenePath = sceneList.ScenePaths[i];
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                await UnloadSceneInternalAsync(scenePath, token);
            }
        }

        private async Awaitable SwitchToSceneListInternalAsync(
            SceneList targetList,
            SwitchOrder switchOrder,
            bool makeFirstSceneActive,
            CancellationToken token)
        {
            HashSet<string> targetSceneIds = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < targetList.ScenePaths.Count; i++)
            {
                string scenePath = targetList.ScenePaths[i];
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                targetSceneIds.Add(NormalizeSceneId(scenePath));
            }

            if (switchOrder == SwitchOrder.LoadMissingThenUnloadObsolete)
            {
                await LoadMissingScenesFromListAsync(targetList, makeFirstSceneActive, token);
                await UnloadObsoleteScenesAsync(targetSceneIds, token);
            }
            else
            {
                await UnloadObsoleteScenesAsync(targetSceneIds, token);
                await LoadMissingScenesFromListAsync(targetList, makeFirstSceneActive, token);
            }
        }

        private async Awaitable LoadMissingScenesFromListAsync(SceneList targetList, bool makeFirstSceneActive, CancellationToken token)
        {
            for (int i = 0; i < targetList.ScenePaths.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                string scenePath = targetList.ScenePaths[i];
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                bool makeActive = makeFirstSceneActive && i == 0;
                await LoadSceneInternalAsync(scenePath, makeActive, token);
            }
        }

        private async Awaitable UnloadObsoleteScenesAsync(HashSet<string> targetSceneIds, CancellationToken token)
        {
            List<string> scenesToUnload = new();
            string ownerSceneId = NormalizeSceneId(gameObject.scene.path);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(i);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    continue;
                }

                string loadedSceneId = NormalizeSceneId(loadedScene.path);

                if (string.IsNullOrWhiteSpace(loadedSceneId))
                {
                    continue;
                }

                if (string.Equals(loadedSceneId, ownerSceneId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!targetSceneIds.Contains(loadedSceneId))
                {
                    scenesToUnload.Add(loadedScene.path);
                }
            }

            for (int i = 0; i < scenesToUnload.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                await UnloadSceneInternalAsync(scenesToUnload[i], token);
            }
        }

        private async Awaitable LoadSceneInternalAsync(string scenePathOrName, bool makeActive, CancellationToken token)
        {
            await Awaitable.MainThreadAsync();
            token.ThrowIfCancellationRequested();

            Scene existingScene = FindLoadedScene(scenePathOrName);
            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                if (makeActive)
                {
                    SceneManager.SetActiveScene(existingScene);
                }

                Log($"Scene already loaded: {scenePathOrName}");
                return;
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(scenePathOrName, LoadSceneMode.Additive);
            if (operation == null)
            {
                throw new InvalidOperationException($"LoadSceneAsync returned null for '{scenePathOrName}'.");
            }

            while (!operation.isDone)
            {
                token.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync();
            }

            Scene loadedScene = FindLoadedScene(scenePathOrName);
            if (makeActive && loadedScene.IsValid() && loadedScene.isLoaded)
            {
                SceneManager.SetActiveScene(loadedScene);
            }
        }

        private async Awaitable UnloadSceneInternalAsync(string scenePathOrName, CancellationToken token)
        {
            await Awaitable.MainThreadAsync();
            token.ThrowIfCancellationRequested();

            Scene loadedScene = FindLoadedScene(scenePathOrName);
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
            {
                Log($"Scene not loaded, skip unload: {scenePathOrName}");
                return;
            }

            if (loadedScene.handle == gameObject.scene.handle)
            {
                Log($"Skip unloading owner scene: {loadedScene.path}");
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.handle == loadedScene.handle)
            {
                Scene fallbackScene = FindFirstLoadedSceneExcept(loadedScene.handle);
                if (fallbackScene.IsValid() && fallbackScene.isLoaded)
                {
                    SceneManager.SetActiveScene(fallbackScene);
                }
            }

            AsyncOperation operation = SceneManager.UnloadSceneAsync(loadedScene);
            if (operation == null)
            {
                Log($"UnloadSceneAsync returned null for '{scenePathOrName}'.");
                return;
            }

            while (!operation.isDone)
            {
                token.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync();
            }
        }

        private Scene FindLoadedScene(string scenePathOrName)
        {
            if (string.IsNullOrWhiteSpace(scenePathOrName))
            {
                return default;
            }

            string requestedId = NormalizeSceneId(scenePathOrName);
            string requestedName = Path.GetFileNameWithoutExtension(scenePathOrName);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(i);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                {
                    continue;
                }

                if (string.Equals(NormalizeSceneId(loadedScene.path), requestedId, StringComparison.OrdinalIgnoreCase))
                {
                    return loadedScene;
                }

                if (string.Equals(loadedScene.name, scenePathOrName, StringComparison.OrdinalIgnoreCase))
                {
                    return loadedScene;
                }

                if (string.Equals(loadedScene.name, requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    return loadedScene;
                }
            }

            return default;
        }

        private static Scene FindFirstLoadedSceneExcept(int excludedHandle)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (scene.handle != excludedHandle)
                {
                    return scene;
                }
            }

            return default;
        }

        private bool TryGetSceneList(string listName, out SceneList sceneList)
        {
            sceneList = null;

            if (sceneLists == null)
            {
                return false;
            }

            for (int i = 0; i < sceneLists.Count; i++)
            {
                SceneList candidate = sceneLists[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.listName, listName, StringComparison.OrdinalIgnoreCase))
                {
                    sceneList = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetSceneList(int listIndex, out SceneList sceneList)
        {
            sceneList = null;

            if (sceneLists == null || listIndex < 0 || listIndex >= sceneLists.Count)
            {
                Debug.LogWarning($"Scene list index {listIndex} is invalid.", this);
                return false;
            }

            sceneList = sceneLists[listIndex];
            return sceneList != null;
        }

        private static bool TryGetScenePathAt(SceneList sceneList, int sceneIndex, out string scenePath)
        {
            scenePath = null;

            if (sceneList == null || sceneList.ScenePaths == null)
            {
                return false;
            }

            if (sceneIndex < 0 || sceneIndex >= sceneList.ScenePaths.Count)
            {
                return false;
            }

            scenePath = sceneList.ScenePaths[sceneIndex];
            return !string.IsNullOrWhiteSpace(scenePath);
        }

        private static string NormalizeSceneId(string scenePathOrName)
        {
            if (string.IsNullOrWhiteSpace(scenePathOrName))
            {
                return string.Empty;
            }

            return scenePathOrName.Replace('\\', '/').Trim().ToLowerInvariant();
        }

        private void Log(string message)
        {
            if (!logOperations)
            {
                return;
            }

            Debug.Log($"[SceneLoader] {message}", this);
        }

#if UNITY_EDITOR
        public void LoadSceneEditor(UnityEngine.Object sceneAsset)
        {
            if (sceneAsset == null)
            {
                return;
            }

            LoadSceneEditor(AssetDatabase.GetAssetPath(sceneAsset));
        }

        public void UnloadSceneEditor(UnityEngine.Object sceneAsset)
        {
            if (sceneAsset == null)
            {
                return;
            }

            UnloadSceneEditor(AssetDatabase.GetAssetPath(sceneAsset));
        }

        public void LoadSceneEditor(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return;
            }

            Scene scene = FindLoadedScene(scenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                Log($"Scene already open in editor: {scenePath}");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        public void UnloadSceneEditor(string scenePathOrName)
        {
            Scene scene = FindLoadedScene(scenePathOrName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            if (scene.handle == gameObject.scene.handle)
            {
                Log($"Skip closing owner scene in editor: {scene.path}");
                return;
            }

            EditorSceneManager.CloseScene(scene, true);
        }
#endif
    }
}