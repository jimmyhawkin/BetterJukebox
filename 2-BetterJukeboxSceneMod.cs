using System;
using System.Linq;
using System.Reflection;
using UniInject;
using UniRx;
using UnityEngine;

public class BetterJukeboxSceneMod : ISceneMod
{
    private static bool autoStartWasRequested;
    private static bool autoStartWasExecuted;
    private static int lastAutoStartSongCount;
    private static int stableAutoStartSongCountTicks;

    [Inject]
    private BetterJukeboxModSettings modSettings;

    [Inject]
    private SongMetaManager songMetaManager;

    public void OnSceneEntered(SceneEnteredContext sceneEnteredContext)
    {
        Debug.Log($"BetterJukeboxSceneMod - entered {sceneEnteredContext.Scene}");

        if (sceneEnteredContext.Scene == EScene.MainScene)
        {
            TryAutoStartFromMainScene(sceneEnteredContext);
            return;
        }

        if (sceneEnteredContext.Scene == EScene.SongSelectScene)
        {
            TryAutoStartFromSongSelectScene(sceneEnteredContext);
            return;
        }

        if (sceneEnteredContext.Scene != EScene.SingScene)
        {
            return;
        }

        Debug.Log("BetterJukeboxSceneMod - entered sing scene");

        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
        {
            GameObject gameObject = new GameObject();
            BetterJukeboxControl monoBehaviour = gameObject.AddComponent<BetterJukeboxControl>();
            monoBehaviour.name = "BetterJukeboxControl";
            sceneEnteredContext.SceneInjector
                .WithBindingForInstance(modSettings)
                .Inject(monoBehaviour);
        });
    }

    private void TryAutoStartFromMainScene(SceneEnteredContext sceneEnteredContext)
    {
        if (!modSettings.EnableBetterJukebox || !modSettings.AutoOpenSing || !modSettings.AutoStartOnGameStart || autoStartWasExecuted || autoStartWasRequested)
        {
            return;
        }

        autoStartWasRequested = true;
        lastAutoStartSongCount = -1;
        stableAutoStartSongCountTicks = 0;
        Debug.Log("BetterJukebox auto start - waiting until song library is loaded");

        WaitForSongLibraryThenOpenSongSelect(0);
    }


    private void WaitForSongLibraryThenOpenSongSelect(int attempt)
    {
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(1f, () =>
        {
            try
            {
                int songCount = 0;
                try
                {
                    songCount = songMetaManager?.GetSongMetas()?.Count() ?? 0;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

                if (songCount > 0 && songCount == lastAutoStartSongCount)
                {
                    stableAutoStartSongCountTicks++;
                }
                else
                {
                    stableAutoStartSongCountTicks = 0;
                    lastAutoStartSongCount = songCount;
                }

                Debug.Log($"BetterJukebox auto start - song count: {songCount}, stable ticks: {stableAutoStartSongCountTicks}");

                // Wait until the count has been stable for several checks.
                // This avoids opening Sing too early on the first game start.
                if (songCount <= 0 || stableAutoStartSongCountTicks < 8)
                {
                    if (attempt >= 120)
                    {
                        Debug.LogWarning("BetterJukebox auto start - timed out while waiting for song library");
                        autoStartWasRequested = false;
                        return;
                    }

                    WaitForSongLibraryThenOpenSongSelect(attempt + 1);
                    return;
                }

                object mainSceneControl = FindControlByName("MainSceneControl");
                if (mainSceneControl == null)
                {
                    Debug.LogWarning("BetterJukebox auto start - MainSceneControl not found");
                    autoStartWasRequested = false;
                    return;
                }

                if (TryInvokeNoArg(mainSceneControl, "OpenSongSelectScene")
                    || TryInvokeNoArg(mainSceneControl, "GoToSongSelectScene"))
                {
                    Debug.Log("BetterJukebox auto start - opened SongSelectScene after song library stayed stable");
                    // Some builds do not fire OnSceneEntered for SongSelectScene when it is opened this way.
                    // Start the SongSelect auto-start wait loop from here as well.
                    AwaitableUtils.ExecuteAfterDelayInSecondsAsync(1.0f, () => WaitForSongSelectReadyThenStart(0));
                    return;
                }

                Debug.LogWarning("BetterJukebox auto start - could not invoke MainSceneControl.OpenSongSelectScene or GoToSongSelectScene");
                LogNoArgMethods(mainSceneControl);
                autoStartWasRequested = false;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                autoStartWasRequested = false;
            }
        });
    }

    private void TryAutoStartFromSongSelectScene(SceneEnteredContext sceneEnteredContext)
    {
        if (!autoStartWasRequested || autoStartWasExecuted)
        {
            return;
        }

        Debug.Log("BetterJukebox auto start - SongSelectScene detected, waiting until scene is ready");
        WaitForSongSelectReadyThenStart(0);
    }

    private void WaitForSongSelectReadyThenStart(int attempt)
    {
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.5f, () =>
        {
            try
            {
                object songSelectSceneControl = FindControlByName("SongSelectSceneControl");
                if (songSelectSceneControl == null)
                {
                    if (attempt < 80)
                    {
                        WaitForSongSelectReadyThenStart(attempt + 1);
                        return;
                    }

                    Debug.LogWarning("BetterJukebox auto start - SongSelectSceneControl not found after waiting");
                    autoStartWasRequested = false;
                    return;
                }

                bool didStart = TryGetBoolProperty(songSelectSceneControl, "didStart")
                    || TryGetBoolProperty(songSelectSceneControl, "DidStart");

                int filteredCount = GetFilteredSongCount(songSelectSceneControl);
                object selectedSong = TryGetPropertyValue(songSelectSceneControl, "SelectedSong");

                Debug.Log($"BetterJukebox auto start - readiness attempt {attempt + 1}, didStart: {didStart}, filtered songs: {filteredCount}, selected song exists: {selectedSong != null}");

                if (!didStart || filteredCount <= 0)
                {
                    if (attempt < 120)
                    {
                        // Force a refresh when possible. On first game start the scene can exist before the list is ready.
                        TryInvokeNoArg(songSelectSceneControl, "UpdateFilteredSongs");
                        WaitForSongSelectReadyThenStart(attempt + 1);
                        return;
                    }

                    Debug.LogWarning("BetterJukebox auto start - timed out while waiting for SongSelectScene to become ready");
                    LogNoArgMethods(songSelectSceneControl);
                    autoStartWasRequested = false;
                    return;
                }

                // The manual Sing flow works because this scene is already ready there.
                // For first game start we wait for readiness above, force one final list refresh,
                // then randomize, then wait until SelectedSong is actually set.
                TryInvokeNoArg(songSelectSceneControl, "InitSongMetas");
                TryInvokeNoArg(songSelectSceneControl, "UpdateFilteredSongs");

                if (!modSettings.AutoPlayRandomSong)
                {
                    Debug.Log("BetterJukebox auto start - Auto Play Random Song is disabled, leaving SongSelectScene open");
                    autoStartWasExecuted = true;
                    autoStartWasRequested = false;
                    return;
                }

                if (!TryInvokeNoArg(songSelectSceneControl, "SelectRandomSong"))
                {
                    Debug.LogWarning("BetterJukebox auto start - SelectRandomSong was not found or failed");
                    LogNoArgMethods(songSelectSceneControl);
                }

                WaitForSelectedSongThenStart(songSelectSceneControl, 0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                autoStartWasRequested = false;
            }
        });
    }

    private void WaitForSelectedSongThenStart(object songSelectSceneControl, int attempt)
    {
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.3f, () =>
        {
            try
            {
                if (songSelectSceneControl == null)
                {
                    autoStartWasRequested = false;
                    return;
                }

                object selectedSong = TryGetPropertyValue(songSelectSceneControl, "SelectedSong");
                Debug.Log($"BetterJukebox auto start - selected song wait attempt {attempt + 1}, selected song exists: {selectedSong != null}");

                if (selectedSong == null)
                {
                    if (attempt < 40)
                    {
                        if (attempt % 5 == 4)
                        {
                            TryInvokeNoArg(songSelectSceneControl, "SelectRandomSong");
                        }
                        WaitForSelectedSongThenStart(songSelectSceneControl, attempt + 1);
                        return;
                    }

                    Debug.LogWarning("BetterJukebox auto start - timed out waiting for SelectedSong after SelectRandomSong");
                    autoStartWasRequested = false;
                    return;
                }

                // Give Melody Mania one small tick to update UI selection before starting.
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.4f, () => TryStartSelectedSongFromSongSelect(songSelectSceneControl, 0));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                autoStartWasRequested = false;
            }
        });
    }

    private void TryStartSelectedSongFromSongSelect(object songSelectSceneControl, int attempt)
    {
        try
        {
            if (songSelectSceneControl == null)
            {
                autoStartWasRequested = false;
                return;
            }

            Debug.Log($"BetterJukebox auto start - trying to start selected song, attempt {attempt + 1}");

            if (TryInvokeNoArg(songSelectSceneControl, "AttemptStartSelectedEntry")
                || TryInvokeNoArg(songSelectSceneControl, "OnSubmitSongRoulette")
                || TryInvokeNoArg(songSelectSceneControl, "CheckAudioThenStartSingScene")
                || TryInvokeNoArg(songSelectSceneControl, "GoToSingScene")
                || TryInvokeNoArg(songSelectSceneControl, "AttemptStartSong")
                || TryInvokeNoArg(songSelectSceneControl, "StartSingScene")
                || TryInvokeNoArg(songSelectSceneControl, "StartSelectedSong"))
            {
                autoStartWasExecuted = true;
                autoStartWasRequested = false;
                Debug.Log("BetterJukebox auto start - start method invoked");
                return;
            }

            if (attempt < 5)
            {
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.75f, () => TryStartSelectedSongFromSongSelect(songSelectSceneControl, attempt + 1));
                return;
            }

            Debug.LogWarning("BetterJukebox auto start - could not invoke a start-song method");
            LogNoArgMethods(songSelectSceneControl);
            autoStartWasRequested = false;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            autoStartWasRequested = false;
        }
    }

    private bool TryGetBoolProperty(object target, string propertyName)
    {
        object value = TryGetPropertyValue(target, propertyName);
        return value is bool boolValue && boolValue;
    }

    private object TryGetPropertyValue(object target, string propertyName)
    {
        try
        {
            if (target == null)
            {
                return null;
            }

            PropertyInfo propertyInfo = target.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(property => property.Name == propertyName && property.GetIndexParameters().Length == 0);

            if (propertyInfo != null)
            {
                return propertyInfo.GetValue(target, null);
            }

            MethodInfo getter = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "get_" + propertyName && method.GetParameters().Length == 0);

            return getter != null ? getter.Invoke(target, null) : null;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return null;
        }
    }

    private int GetFilteredSongCount(object songSelectSceneControl)
    {
        try
        {
            object result = null;
            MethodInfo methodInfo = songSelectSceneControl.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "GetFilteredSongMetas" && method.GetParameters().Length == 0);

            if (methodInfo != null)
            {
                result = methodInfo.Invoke(songSelectSceneControl, null);
            }

            if (result == null)
            {
                result = TryGetPropertyValue(songSelectSceneControl, "FilteredSongMetas");
            }

            if (result is System.Collections.ICollection collection)
            {
                return collection.Count;
            }

            if (result is System.Collections.IEnumerable enumerable)
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    count++;
                    if (count > 0)
                    {
                        // We only need to know that at least one song exists.
                        return count;
                    }
                }
                return count;
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        return 0;
    }

    private object FindControlByName(string typeName)
    {
        MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        return behaviours.FirstOrDefault(behaviour => behaviour != null && behaviour.GetType().Name == typeName);
    }

    private bool TryInvokeNoArg(object target, string methodName)
    {
        if (target == null)
        {
            return false;
        }

        MethodInfo methodInfo = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == methodName && method.GetParameters().Length == 0);

        if (methodInfo == null)
        {
            return false;
        }

        Debug.Log($"BetterJukebox auto start - invoking {target.GetType().Name}.{methodName}()");
        methodInfo.Invoke(target, null);
        return true;
    }

    private void LogNoArgMethods(object target)
    {
        if (target == null)
        {
            return;
        }

        string methodNames = string.Join(", ", target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.GetParameters().Length == 0)
            .Select(method => method.Name)
            .Distinct()
            .OrderBy(name => name)
            .Take(80));

        Debug.Log($"BetterJukebox auto start - no-arg methods on {target.GetType().Name}: {methodNames}");
    }
}
