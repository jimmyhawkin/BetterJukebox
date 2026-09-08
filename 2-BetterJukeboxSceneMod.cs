using System;
using System.Linq;
using System.Reflection;
using UniInject;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;

public class BetterJukeboxSceneMod : ISceneMod, IOnLoadMod
{
    private static int mainMenuWatcherGeneration;
    private bool initialMainSceneInstallCheckScheduled;

    private const string JukeboxModeMarkerFileName = "JukeboxModeActive.txt";
    private static bool jukeboxModeActive;
    private static bool autoStartWasRequested;
    private static bool autoStartWasExecuted;
    private static bool jukeboxEntryInProgress;
    private static bool songSelectStartWaitActive;
    private static int lastAutoStartSongCount;
    private static int stableAutoStartSongCountTicks;

    [Inject]
    private BetterJukeboxModSettings modSettings;

    [Inject]
    private SongMetaManager songMetaManager;

    [Inject]
    private SongQueueManager songQueueManager;

    [Inject]
    private PlaylistManager playlistManager;

    [Inject]
    private NonPersistentSettings nonPersistentSettings;

    [Inject]
    private Settings settings;

    [Inject]
    private VolumeManager volumeManager;

    [Inject]
    private SceneNavigator sceneNavigator;

    public void OnLoadMod()
    {
        // Melody Mania's ModManager invokes IOnLoadMod only after mod settings are
        // loaded and the mod object has been dependency-injected. This is the
        // correct lifecycle hook for Workshop mods that can finish loading after
        // MainScene has already been entered.
        if (modSettings != null)
        {
            modSettings.ActivateImmediatePersistence();
        }

        int generation = ++mainMenuWatcherGeneration;
        BetterJukeboxLog.Info(
            "BetterJukebox 2.1.2.29 - mod-load main-menu watcher started (generation "
            + generation + ")");

        WatchForNativeMainMenuAfterModLoad(0, generation);
    }

    private void WatchForNativeMainMenuAfterModLoad(int attempt, int generation)
    {
        // Ignore delayed callbacks that belong to an obsolete pre-reload instance.
        if (generation != mainMenuWatcherGeneration)
        {
            return;
        }

        try
        {
            UIDocument[] uiDocuments = UnityEngine.Object.FindObjectsOfType<UIDocument>();
            for (int i = 0; i < uiDocuments.Length; i++)
            {
                UIDocument uiDocument = uiDocuments[i];
                if (uiDocument == null || uiDocument.rootVisualElement == null)
                {
                    continue;
                }

                VisualElement root = uiDocument.rootVisualElement;
                Button existingButton = root.Q<Button>("betterJukeboxMainMenuButton");
                if (existingButton != null)
                {
                    BetterJukeboxLog.Info(
                        "BetterJukebox 2.1.2.29 - Jukebox main-menu button already present");
                    return;
                }

                Button startButton = root.Q<Button>("startButton");
                if (startButton == null || startButton.parent == null)
                {
                    continue;
                }

                BetterJukeboxLog.Info(
                    "BetterJukebox 2.1.2.29 - native MainScene startButton found by mod-load watcher after attempt "
                    + (attempt + 1));

                // Reuse the exact same native installer used by scene transitions.
                TryInstallJukeboxMainMenuButton(0);
                return;
            }

            // Workshop discovery/update can finish after the first MainScene event.
            // Keep this bounded but long enough to cover slower startup systems.
            if (attempt < 79)
            {
                if (attempt == 0 || attempt == 19 || attempt == 39 || attempt == 59)
                {
                    BetterJukeboxLog.Info(
                        "BetterJukebox 2.1.2.29 - native MainScene startButton not available yet; watcher attempt "
                        + (attempt + 1));
                }

                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(
                    0.25f,
                    () => WatchForNativeMainMenuAfterModLoad(attempt + 1, generation));
                return;
            }

            BetterJukeboxLog.Warning(
                "BetterJukebox 2.1.2.29 - mod-load watcher timed out after 20 seconds without finding native MainScene startButton");
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
        }
    }

    private void EnsureInitialMainSceneButtonInstall()
    {
        if (initialMainSceneInstallCheckScheduled)
        {
            return;
        }

        initialMainSceneInstallCheckScheduled = true;

        // Keep the scene-lifecycle path as an additional safety trigger.
        // The primary first-start path is now IOnLoadMod above.
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
        {
            try
            {
                UIDocument[] uiDocuments = UnityEngine.Object.FindObjectsOfType<UIDocument>();
                for (int i = 0; i < uiDocuments.Length; i++)
                {
                    UIDocument uiDocument = uiDocuments[i];
                    if (uiDocument == null || uiDocument.rootVisualElement == null)
                    {
                        continue;
                    }

                    VisualElement root = uiDocument.rootVisualElement;
                    Button startButton = root.Q<Button>("startButton");
                    if (startButton != null && startButton.parent != null)
                    {
                        InstallJukeboxMainMenuButton();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                BetterJukeboxLog.Exception(ex);
            }
        });
    }

    public void OnSceneEntered(SceneEnteredContext sceneEnteredContext)
    {
        EnsureInitialMainSceneButtonInstall();
        if (modSettings != null)
        {
            modSettings.ActivateImmediatePersistence();
        }

        if (sceneEnteredContext.Scene == EScene.MainScene)
        {
            ExitJukeboxMode();
            InstallJukeboxMainMenuButton();
            TryAutoStartFromMainScene(sceneEnteredContext);
            return;
        }

        if (sceneEnteredContext.Scene == EScene.SongSelectScene)
        {
            RestoreJukeboxModeMarker();
            if (jukeboxModeActive)
            {
                InstallJukeboxLobby();
            }
            TryAutoStartFromSongSelectScene(sceneEnteredContext);
            return;
        }

        if (sceneEnteredContext.Scene == EScene.SingScene)
        {
            RestoreJukeboxModeMarker();
        }

        if (sceneEnteredContext.Scene != EScene.SingScene || !jukeboxModeActive)
        {
            return;
        }


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


    private void InstallJukeboxLobby()
    {
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(2, () =>
        {
            try
            {
                UIDocument[] uiDocuments = UnityEngine.Object.FindObjectsOfType<UIDocument>();
                for (int i = 0; i < uiDocuments.Length; i++)
                {
                    UIDocument uiDocument = uiDocuments[i];
                    if (uiDocument == null || uiDocument.rootVisualElement == null)
                    {
                        continue;
                    }

                    VisualElement root = uiDocument.rootVisualElement;
                    if (root.Q<VisualElement>("betterJukeboxSongSelectOverlay") != null)
                    {
                        return;
                    }

                    Button nativeStartButton = root.Q<Button>("startButton");
                    if (nativeStartButton != null)
                    {
                        continue;
                    }

                    // This is the native Song Select document. Hide only the visual player list
                    // while the dedicated Jukebox session is active. Player profiles, microphones,
                    // SingSceneData, and all native player state remain untouched.
                    VisualElement nativePlayerList = root.Q<VisualElement>("playerList");
                    if (nativePlayerList != null)
                    {
                        nativePlayerList.style.display = DisplayStyle.None;
                    }

                    // Jukebox owns Song Select presentation only. Hide the native
                    // top-right controls and difficulty selector that are redundant
                    // in the dedicated Jukebox experience. Their underlying native
                    // settings/state are left untouched and Sing remains completely native.
                    HideJukeboxOnlyNativeSongSelectControls(root);
                    AwaitableUtils.ExecuteAfterDelayInFramesAsync(2, () => HideJukeboxOnlyNativeSongSelectControls(root));

                    GameObject gameObject = new GameObject();
                    BetterJukeboxControl control = gameObject.AddComponent<BetterJukeboxControl>();
                    control.name = "BetterJukeboxSongSelectControl";
                    control.InitializeSongSelectJukeboxMenu(
                        uiDocument,
                        modSettings,
                        songMetaManager,
                        songQueueManager,
                        playlistManager,
                        nonPersistentSettings,
                        settings,
                        volumeManager,
                        sceneNavigator,
                        OnLobbyPreviousClicked,
                        OnLobbyNextClicked,
                        OnLobbyReturnLiveClicked);

                    BetterJukeboxLog.Info("BetterJukebox 2.1.2.29 - Native BetterJukebox menu added to Song Select");
                    return;
                }
            }
            catch (Exception ex)
            {
                BetterJukeboxLog.Exception(ex);
            }
        });
    }

    private void HideJukeboxOnlyNativeSongSelectControls(VisualElement root)
    {
        if (root == null)
        {
            return;
        }

        try
        {
            Rect rootBounds = root.worldBound;
            if (rootBounds.width <= 0f || rootBounds.height <= 0f)
            {
                return;
            }

            // The native Song Select utility/settings buttons form the compact
            // cluster in the upper-right corner. This runs before BetterJukebox
            // adds its own overlay, so only native buttons can match this region.
            System.Collections.Generic.List<Button> buttons = root.Query<Button>().ToList();
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                if (button == null || button.style.display == DisplayStyle.None)
                {
                    continue;
                }

                Rect bounds = button.worldBound;
                if (bounds.width <= 0f || bounds.height <= 0f)
                {
                    continue;
                }

                float centerX = (bounds.center.x - rootBounds.xMin) / rootBounds.width;
                float centerY = (bounds.center.y - rootBounds.yMin) / rootBounds.height;
                if (centerX >= 0.78f && centerY <= 0.18f)
                {
                    button.style.display = DisplayStyle.None;
                }

                // Difficulty is the compact native control in the lower-right.
                // Prefer its text when available so normal Song Select buttons are
                // never hidden just because they happen to be near the bottom edge.
                string buttonText = button.text ?? "";
                if (centerX >= 0.72f && centerY >= 0.78f && IsNativeDifficultyText(buttonText))
                {
                    button.style.display = DisplayStyle.None;
                }
            }

            // Some Melody Mania builds render the difficulty text as a Label
            // inside another visual element instead of directly on a Button.
            System.Collections.Generic.List<Label> labels = root.Query<Label>().ToList();
            for (int i = 0; i < labels.Count; i++)
            {
                Label label = labels[i];
                if (label == null || !IsNativeDifficultyText(label.text))
                {
                    continue;
                }

                Rect bounds = label.worldBound;
                if (bounds.width <= 0f || bounds.height <= 0f)
                {
                    continue;
                }

                float centerX = (bounds.center.x - rootBounds.xMin) / rootBounds.width;
                float centerY = (bounds.center.y - rootBounds.yMin) / rootBounds.height;
                if (centerX < 0.72f || centerY < 0.78f)
                {
                    continue;
                }

                VisualElement target = label;
                VisualElement parent = label.parent;
                for (int depth = 0; depth < 3 && parent != null; depth++)
                {
                    Rect parentBounds = parent.worldBound;
                    if (parentBounds.width > 0f
                        && parentBounds.width <= 360f
                        && parentBounds.height > 0f
                        && parentBounds.height <= 120f)
                    {
                        target = parent;
                        parent = parent.parent;
                        continue;
                    }
                    break;
                }
                target.style.display = DisplayStyle.None;
            }
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Warning("BetterJukebox Jukebox-only Song Select cleanup failed: " + ex.Message);
        }
    }

    private bool IsNativeDifficultyText(string text)
    {
        string value = (text ?? "").Trim();
        return value == "Easy"
            || value == "Medium"
            || value == "Hard"
            || value == "Normal"
            || value == "Expert";
    }

    private void OnLobbyPreviousClicked()
    {
        TryStartSpecificSongFromLobby(BetterJukeboxControl.GetPreviousHistorySong());
    }

    private void OnLobbyReturnLiveClicked()
    {
        if (!BetterJukeboxControl.IsBrowsingHistory())
        {
            return;
        }

        // Leave history and resume the live Jukebox flow:
        // native queue first, random only when the queue is empty.
        BetterJukeboxControl.GetLiveHistorySong();
        OnLobbyNextClicked();
    }

    private void OnLobbyNextClicked()
    {
        try
        {
            SongMeta historyForwardSong = BetterJukeboxControl.GetNextHistorySong();
            if (historyForwardSong != null)
            {
                TryStartSpecificSongFromLobby(historyForwardSong);
                return;
            }
            if (songSelectStartWaitActive)
            {
                return;
            }

            object songSelectSceneControl = FindControlByName("SongSelectSceneControl");
            if (songSelectSceneControl == null)
            {
                return;
            }

            songSelectStartWaitActive = true;
            autoStartWasRequested = true;
            autoStartWasExecuted = false;

            if (TryStartNextQueuedSong())
            {
                return;
            }

            if (!TryInvokeNoArg(songSelectSceneControl, "SelectRandomSong"))
            {
                songSelectStartWaitActive = false;
                autoStartWasRequested = false;
                return;
            }

            WaitForSelectedSongThenStart(songSelectSceneControl, 0);
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
            ResetJukeboxEntryRequest();
        }
    }

    private void InstallJukeboxMainMenuButton()
    {
        // MainScene UI Toolkit documents are not guaranteed to be fully built one
        // frame after scene entry on every machine/resolution/DPI configuration.
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () => TryInstallJukeboxMainMenuButton(0));
    }

    private void TryInstallJukeboxMainMenuButton(int attempt)
    {
        try
        {
            UIDocument[] uiDocuments = UnityEngine.Object.FindObjectsOfType<UIDocument>();
            for (int i = 0; i < uiDocuments.Length; i++)
            {
                UIDocument uiDocument = uiDocuments[i];
                if (uiDocument == null || uiDocument.rootVisualElement == null)
                {
                    continue;
                }

                VisualElement root = uiDocument.rootVisualElement;
                Button existingButton = root.Q<Button>("betterJukeboxMainMenuButton");
                if (existingButton != null)
                {
                    // A button can exist in the visual tree before UI Toolkit has
                    // completed geometry/layout. Verify that it is actually laid out.
                    AwaitableUtils.ExecuteAfterDelayInFramesAsync(
                        2,
                        () => VerifyJukeboxMainMenuButtonLayout(existingButton, null, attempt));
                    return;
                }

                Button startButton = root.Q<Button>("startButton");
                if (startButton == null || startButton.parent == null)
                {
                    continue;
                }

                VisualElement buttonRow = startButton.parent;

                Button jukeboxButton = new Button();
                jukeboxButton.name = "betterJukeboxMainMenuButton";
                jukeboxButton.text = "Jukebox";

                // Match Melody Mania's native MainScene buttons exactly.
                // MainSceneUi.uxml defines partyButton / onlineGameButton as:
                // class="mainSceneButton ml-5"
                // style="width: 160px; height: 60px; font-size: 18px;"
                //
                // These are UI Toolkit panel pixels, not physical screen pixels.
                // Melody Mania's PanelSettings / UI Scale handles actual 1440p/4K
                // scaling, so using the native UXML dimensions is correct.
                jukeboxButton.AddToClassList("mainSceneButton");
                jukeboxButton.AddToClassList("ml-5");
                jukeboxButton.style.width = new StyleLength(new Length(160f, LengthUnit.Pixel));
                jukeboxButton.style.height = new StyleLength(new Length(60f, LengthUnit.Pixel));
                jukeboxButton.style.fontSize = new StyleLength(new Length(18f, LengthUnit.Pixel));
                jukeboxButton.clicked += OnJukeboxMainMenuButtonClicked;
                buttonRow.Add(jukeboxButton);

                // Give UI Toolkit time to calculate resolved geometry. A resolution
                // change used to force this later; now BetterJukebox verifies it itself.
                AwaitableUtils.ExecuteAfterDelayInFramesAsync(
                    2,
                    () => VerifyJukeboxMainMenuButtonLayout(jukeboxButton, startButton, attempt));
                return;
            }

            ScheduleJukeboxMainMenuButtonRetry(attempt);
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
        }
    }

    private void VerifyJukeboxMainMenuButtonLayout(Button jukeboxButton, Button startButton, int attempt)
    {
        try
        {
            if (jukeboxButton != null
                && jukeboxButton.panel != null
                && jukeboxButton.parent != null
                && jukeboxButton.resolvedStyle.width > 1f
                && jukeboxButton.resolvedStyle.height > 1f)
            {
                if (startButton != null)
                {
                    startButton.clicked += ExitJukeboxMode;
                }

                BetterJukeboxLog.Info(
                    "BetterJukebox 2.1.2.29 - Jukebox button visible with native layout "
                    + jukeboxButton.resolvedStyle.width + "x" + jukeboxButton.resolvedStyle.height
                    + " after attempt " + (attempt + 1));
                return;
            }

            if (jukeboxButton != null && jukeboxButton.parent != null)
            {
                jukeboxButton.RemoveFromHierarchy();
            }

            BetterJukeboxLog.Warning(
                "BetterJukebox 2.1.2.29 - Jukebox button had no resolved geometry; retrying native layout");
            ScheduleJukeboxMainMenuButtonRetry(attempt);
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
            ScheduleJukeboxMainMenuButtonRetry(attempt);
        }
    }

    private void ScheduleJukeboxMainMenuButtonRetry(int attempt)
    {
        // Retry every 0.25 seconds for up to about 20 seconds. This stays bounded
        // and stops as soon as the button has real resolved geometry.
        if (attempt < 79)
        {
            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(
                0.25f,
                () => TryInstallJukeboxMainMenuButton(attempt + 1));
            return;
        }

        BetterJukeboxLog.Warning(
            "BetterJukebox 2.1.2.29 - native MainScene button could not be installed with valid geometry after 80 attempts");
    }

    private void OnJukeboxMainMenuButtonClicked()
    {
        try
        {
            BetterJukeboxLog.Info("BetterJukebox 2.1.1.1 - Jukebox button clicked");
            if (!BeginJukeboxEntry())
            {
                BetterJukeboxLog.Info("BetterJukebox 2.1.1.1 - duplicate Jukebox entry request ignored");
                return;
            }

            object mainSceneControl = FindControlByName("MainSceneControl");
            if (mainSceneControl == null)
            {
                BetterJukeboxLog.Warning("BetterJukebox 2.1.1.1 - MainSceneControl was not found");
                ResetJukeboxEntryRequest();
                return;
            }

            if (TryInvokeNoArg(mainSceneControl, "OpenSongSelectScene")
                || TryInvokeNoArg(mainSceneControl, "GoToSongSelectScene"))
            {
                BetterJukeboxLog.Info("BetterJukebox 2.1.1.1 - opened native Song Select from Jukebox button");
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(1.0f, StartSongSelectWaitOnce);
                return;
            }

            BetterJukeboxLog.Warning("BetterJukebox 2.1.1.1 - native Song Select method was not found on MainSceneControl");
            LogNoArgMethods(mainSceneControl);
            ResetJukeboxEntryRequest();
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
            ResetJukeboxEntryRequest();
        }
    }


    private string GetJukeboxModeMarkerPath()
    {
        return System.IO.Path.Combine(
            System.IO.Path.Combine(System.IO.Path.Combine(Application.persistentDataPath, "ModsPersistentData"), "BetterJukebox"),
            JukeboxModeMarkerFileName);
    }

    private void WriteJukeboxModeMarker(bool isActive)
    {
        try
        {
            string path = GetJukeboxModeMarkerPath();
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            System.IO.File.WriteAllText(path, isActive ? "1" : "0");
        }
        catch (System.Exception ex)
        {
            BetterJukeboxLog.Warning("BetterJukebox could not save Jukebox session marker: " + ex.Message);
        }
    }

    private bool ReadJukeboxModeMarker()
    {
        try
        {
            string path = GetJukeboxModeMarkerPath();
            return System.IO.File.Exists(path) && System.IO.File.ReadAllText(path).Trim() == "1";
        }
        catch (System.Exception ex)
        {
            BetterJukeboxLog.Warning("BetterJukebox could not read Jukebox session marker: " + ex.Message);
            return false;
        }
    }

    private void EnterJukeboxMode()
    {
        jukeboxModeActive = true;
        WriteJukeboxModeMarker(true);
    }

    private void ExitJukeboxMode()
    {
        jukeboxModeActive = false;
        WriteJukeboxModeMarker(false);
    }

    private void RestoreJukeboxModeMarker()
    {
        if (!jukeboxModeActive && ReadJukeboxModeMarker())
        {
            jukeboxModeActive = true;
            BetterJukeboxLog.Info("BetterJukebox 2.1.1.1 - restored Jukebox mode marker");
        }
    }

    private bool BeginJukeboxEntry()
    {
        if (jukeboxEntryInProgress || autoStartWasRequested)
        {
            return false;
        }

        EnterJukeboxMode();
        jukeboxEntryInProgress = true;
        autoStartWasExecuted = false;
        autoStartWasRequested = true;
        songSelectStartWaitActive = false;
        return true;
    }

    private void StartSongSelectWaitOnce()
    {
        if (!autoStartWasRequested || autoStartWasExecuted || songSelectStartWaitActive)
        {
            return;
        }

        songSelectStartWaitActive = true;
        WaitForSongSelectReadyThenStart(0);
    }

    private void ResetJukeboxEntryRequest()
    {
        autoStartWasRequested = false;
        jukeboxEntryInProgress = false;
        songSelectStartWaitActive = false;
    }

    private void TryAutoStartFromMainScene(SceneEnteredContext sceneEnteredContext)
    {
        if (!modSettings.EnableBetterJukebox || !modSettings.AutoStartJukebox || autoStartWasExecuted || autoStartWasRequested)
        {
            return;
        }

        if (!BeginJukeboxEntry())
        {
            return;
        }

        lastAutoStartSongCount = -1;
        stableAutoStartSongCountTicks = 0;
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
                    BetterJukeboxLog.Exception(ex);
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


                // Wait until the count has been stable for several checks.
                // This avoids opening Sing too early on the first game start.
                if (songCount <= 0 || stableAutoStartSongCountTicks < 8)
                {
                    if (attempt >= 120)
                    {
                        BetterJukeboxLog.Warning("BetterJukebox auto start - timed out while waiting for song library");
                        ResetJukeboxEntryRequest();
                        return;
                    }

                    WaitForSongLibraryThenOpenSongSelect(attempt + 1);
                    return;
                }

                object mainSceneControl = FindControlByName("MainSceneControl");
                if (mainSceneControl == null)
                {
                    if (attempt >= 120)
                    {
                        BetterJukeboxLog.Warning("BetterJukebox auto start - MainSceneControl not found after waiting");
                        ResetJukeboxEntryRequest();
                        return;
                    }

                    WaitForSongLibraryThenOpenSongSelect(attempt + 1);
                    return;
                }

                if (TryInvokeNoArg(mainSceneControl, "OpenSongSelectScene")
                    || TryInvokeNoArg(mainSceneControl, "GoToSongSelectScene"))
                {
                    // Some builds do not fire OnSceneEntered for SongSelectScene when it is opened this way.
                    // Start the SongSelect auto-start wait loop from here as well.
                    AwaitableUtils.ExecuteAfterDelayInSecondsAsync(1.0f, StartSongSelectWaitOnce);
                    return;
                }

                BetterJukeboxLog.Warning("BetterJukebox auto start - could not invoke MainSceneControl.OpenSongSelectScene or GoToSongSelectScene");
                LogNoArgMethods(mainSceneControl);
                ResetJukeboxEntryRequest();
            }
            catch (Exception ex)
            {
                BetterJukeboxLog.Exception(ex);
                ResetJukeboxEntryRequest();
            }
        });
    }

    private void TryAutoStartFromSongSelectScene(SceneEnteredContext sceneEnteredContext)
    {
        if (!autoStartWasRequested || autoStartWasExecuted)
        {
            return;
        }

        StartSongSelectWaitOnce();
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

                    BetterJukeboxLog.Warning("BetterJukebox auto start - SongSelectSceneControl not found after waiting");
                    ResetJukeboxEntryRequest();
                    return;
                }

                bool didStart = TryGetBoolProperty(songSelectSceneControl, "didStart")
                    || TryGetBoolProperty(songSelectSceneControl, "DidStart");

                int filteredCount = GetFilteredSongCount(songSelectSceneControl);
                object selectedSong = TryGetPropertyValue(songSelectSceneControl, "SelectedSong");


                if (!didStart || filteredCount <= 0)
                {
                    if (attempt < 120)
                    {
                        // Force a refresh when possible. On first game start the scene can exist before the list is ready.
                        TryInvokeNoArg(songSelectSceneControl, "UpdateFilteredSongs");
                        WaitForSongSelectReadyThenStart(attempt + 1);
                        return;
                    }

                    BetterJukeboxLog.Warning("BetterJukebox auto start - timed out while waiting for SongSelectScene to become ready");
                    LogNoArgMethods(songSelectSceneControl);
                    ResetJukeboxEntryRequest();
                    return;
                }

                // The manual Sing flow works because this scene is already ready there.
                // For first game start we wait for readiness above, force one final list refresh,
                // then randomize, then wait until SelectedSong is actually set.
                TryInvokeNoArg(songSelectSceneControl, "InitSongMetas");
                TryInvokeNoArg(songSelectSceneControl, "UpdateFilteredSongs");

                if (!modSettings.AutoPlayRandomSong)
                {
                    autoStartWasExecuted = true;
                    ResetJukeboxEntryRequest();
                    return;
                }

                if (TryStartNextQueuedSong())
                {
                    return;
                }

                if (!TryInvokeNoArg(songSelectSceneControl, "SelectRandomSong"))
                {
                    BetterJukeboxLog.Warning("BetterJukebox auto start - SelectRandomSong was not found or failed");
                    LogNoArgMethods(songSelectSceneControl);
                }

                WaitForSelectedSongThenStart(songSelectSceneControl, 0);
            }
            catch (Exception ex)
            {
                BetterJukeboxLog.Exception(ex);
                ResetJukeboxEntryRequest();
            }
        });
    }

    private void TryStartSpecificSongFromLobby(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            NotificationManager.CreateNotification(Translation.Of("No song available in history"));
            return;
        }

        object songSelectSceneControl = FindControlByName("SongSelectSceneControl");
        if (songSelectSceneControl == null)
        {
            BetterJukeboxLog.Warning("BetterJukebox history navigation - SongSelectSceneControl was not found");
            return;
        }

        try
        {
            System.Reflection.PropertyInfo property = songSelectSceneControl.GetType().GetProperty("SelectedSong");
            if (property != null && property.CanWrite)
            {
                property.SetValue(songSelectSceneControl, songMeta, null);
            }
            else
            {
                System.Reflection.FieldInfo field = songSelectSceneControl.GetType().GetField("SelectedSong")
                    ?? songSelectSceneControl.GetType().GetField("selectedSong", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(songSelectSceneControl, songMeta);
                }
            }

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.2f, () => TryStartSelectedSongFromSongSelect(songSelectSceneControl, 0));
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
        }
    }

    private bool TryStartNextQueuedSong()
    {
        try
        {
            if (songQueueManager == null || songQueueManager.IsSongQueueEmpty)
            {
                return false;
            }

            SingSceneData nextSingSceneData = songQueueManager.CreateNextSingSceneData(null);
            if (nextSingSceneData == null || nextSingSceneData.SongMetas == null || nextSingSceneData.SongMetas.Count == 0)
            {
                BetterJukeboxLog.Warning("BetterJukebox queue-first - native SongQueueManager returned no playable song");
                return false;
            }

            autoStartWasExecuted = true;
            ResetJukeboxEntryRequest();
            BetterJukeboxLog.Info("BetterJukebox queue-first - starting next native queued song");
            sceneNavigator.LoadScene(EScene.SingScene, nextSingSceneData);
            return true;
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Warning("BetterJukebox queue-first - native queue start failed: " + ex.Message);
            return false;
        }
    }

    private void WaitForSelectedSongThenStart(object songSelectSceneControl, int attempt)
    {
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.3f, () =>
        {
            try
            {
                if (songSelectSceneControl == null)
                {
                    ResetJukeboxEntryRequest();
                    return;
                }

                object selectedSong = TryGetPropertyValue(songSelectSceneControl, "SelectedSong");

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

                    BetterJukeboxLog.Warning("BetterJukebox auto start - timed out waiting for SelectedSong after SelectRandomSong");
                    ResetJukeboxEntryRequest();
                    return;
                }

                // Give Melody Mania one small tick to update UI selection before starting.
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.4f, () => TryStartSelectedSongFromSongSelect(songSelectSceneControl, 0));
            }
            catch (Exception ex)
            {
                BetterJukeboxLog.Exception(ex);
                ResetJukeboxEntryRequest();
            }
        });
    }

    private void TryStartSelectedSongFromSongSelect(object songSelectSceneControl, int attempt)
    {
        try
        {
            if (songSelectSceneControl == null)
            {
                ResetJukeboxEntryRequest();
                return;
            }


            if (TryInvokeNoArg(songSelectSceneControl, "AttemptStartSelectedEntry")
                || TryInvokeNoArg(songSelectSceneControl, "OnSubmitSongRoulette")
                || TryInvokeNoArg(songSelectSceneControl, "CheckAudioThenStartSingScene")
                || TryInvokeNoArg(songSelectSceneControl, "GoToSingScene")
                || TryInvokeNoArg(songSelectSceneControl, "AttemptStartSong")
                || TryInvokeNoArg(songSelectSceneControl, "StartSingScene")
                || TryInvokeNoArg(songSelectSceneControl, "StartSelectedSong"))
            {
                autoStartWasExecuted = true;
                ResetJukeboxEntryRequest();
                return;
            }

            if (attempt < 5)
            {
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.75f, () => TryStartSelectedSongFromSongSelect(songSelectSceneControl, attempt + 1));
                return;
            }

            BetterJukeboxLog.Warning("BetterJukebox auto start - could not invoke a start-song method");
            LogNoArgMethods(songSelectSceneControl);
            ResetJukeboxEntryRequest();
        }
        catch (Exception ex)
        {
            BetterJukeboxLog.Exception(ex);
            ResetJukeboxEntryRequest();
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
            BetterJukeboxLog.Exception(ex);
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
            BetterJukeboxLog.Exception(ex);
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

    }
}
