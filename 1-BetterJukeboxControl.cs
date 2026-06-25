using System;
using System.Collections.Generic;
using System.Linq;
using UniInject;
using UniRx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

public class BetterJukeboxControl : MonoBehaviour, INeedInjection, IInjectionFinishedListener
{
    private readonly static HashSet<SongMeta> seenSongMetas = new HashSet<SongMeta>();
    private readonly static List<SongMeta> betterJukeboxQueue = new List<SongMeta>();
    private readonly static List<SongMeta> betterJukeboxHistory = new List<SongMeta>();

    private const float FadeTimeInSeconds = 1f;

    [Inject]
    private SingSceneControl singSceneControl;

    [Inject]
    private SongAudioPlayer songAudioPlayer;

    [Inject]
    private SceneNavigator sceneNavigator;

    [Inject]
    private SongMetaManager songMetaManager;

    [Inject]
    private PlaylistManager playlistManager;

    [Inject]
    private NonPersistentSettings nonPersistentSettings;

    [Inject]
    private UIDocument uiDocument;

    [Inject]
    private SongQueueManager songQueueManager;

    [Inject]
    private BetterJukeboxModSettings modSettings;

    private readonly List<VisualElement> singingUiElements = new List<VisualElement>();

    private bool isFinishing;
    private bool isFadedOut;
    private bool isInjectionFinished;
    private bool singingModeStarted;
    private long lastMicInputTimeInMillis;

    private Label modInfoLabel;
    private Label nowPlayingLabel;
    private bool nowPlayingWasHidden;
    private VisualElement progressContainer;
    private VisualElement progressFill;
    private Label progressTimeLabel;
    private Label progressSongLabel;
    private VisualElement mouseClickBlocker;

    private VisualElement actionOverlay;
    private VisualElement searchPanel;
    private VisualElement searchResultsContainer;
    private TextField searchTextField;
    private VisualElement queuePanel;
    private VisualElement queueResultsContainer;
    private VisualElement historyPanel;
    private VisualElement historyResultsContainer;
    private VisualElement settingsPanel;
    private VisualElement settingsResultsContainer;
    private bool actionOverlayIsVisible;
    private bool searchPanelIsVisible;
    private bool queuePanelIsVisible;
    private bool historyPanelIsVisible;
    private bool settingsPanelIsVisible;
    private float lastOverlayActivityTimeInSeconds;
    private Vector2 lastMousePosition;
    private bool hasLastMousePosition;
    private float mouseMovementStartedAt = -1f;
    private float mouseMovementAccumulatedDistance;
    private bool overlayDisabledBySingingMode;
    private bool manualSearchInputHandling;
    private bool wasPausedByBetterJukebox;
    private double lastLeftClickBlockTime;
    private float lastSearchFocusTime;
    private int searchCaretIndex;
    private Action<InputEventPtr, InputDevice> searchKeyboardBlocker;
    private readonly List<string> pendingSearchTextInput = new List<string>();
    private bool pendingSearchEscape;
    private bool pendingSettingsEscape;

    private SingSceneFinisher singSceneFinisher;

    public void OnInjectionFinished()
    {
        Debug.Log($"{nameof(BetterJukeboxControl)} - OnInjectionFinished");

        isInjectionFinished = true;

        // RC7: do not use an internal disable flag anymore. Full enable/disable belongs to Melody Mania Mods menu.
        // Reset this legacy setting so users are not locked out after testing older builds.
        modSettings.EnableBetterJukebox = true;
        overlayDisabledBySingingMode = false;

        singingUiElements.AddRange(uiDocument.rootVisualElement.Query(R.UxmlNames.playerUiContainer).ToList());
        singingUiElements.AddRange(uiDocument.rootVisualElement.Query(R.UxmlNames.playerInfoContainer).ToList());
        if (modSettings.HideLyrics)
        {
            singingUiElements.AddRange(uiDocument.rootVisualElement.Query(R.UxmlNames.bottomLyricsContainer).ToList());
            singingUiElements.AddRange(uiDocument.rootVisualElement.Query(R.UxmlNames.topLyricsContainer).ToList());
        }

        DisableVfxCamera();
        CreateLabels();
        CreateProgressBar();
        CreateActionOverlay();
        AddCurrentSongToHistory();
        HideBuiltInPauseButton();
        CreateMouseClickBlocker();
        // Block only Melody Mania global shortcut keys while Search is open.
        InstallSearchKeyboardBlocker();

        singingUiElements.ForEach(elem => elem.HideByVisibility());
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(FadeTimeInSeconds, () => singingUiElements.ForEach(elem => elem.ShowByVisibility()));
        FadeOutSingingUiElements();

        singSceneControl.PlayerControls
            .Select(playerControl => playerControl.PlayerNoteRecorder.RecordedNoteStartedEventStream)
            .Merge()
            .Where(evt => evt.RecordedNote?.TargetNote != null
                          && evt.RecordedNote.TargetNote.MidiNote == evt.RecordedNote.RoundedMidiNote)
            .Subscribe(evt => lastMicInputTimeInMillis = TimeUtils.GetUnixTimeMilliseconds());

        DisableSingSceneFinisher();
    }

    private void CreateLabels()
    {
        VisualElement songInfoContainer = uiDocument.rootVisualElement.Q("governanceOverlay")?.Q("songInfoContainer");
        if (songInfoContainer == null)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - songInfoContainer not found");
            return;
        }

        modInfoLabel = new Label();
        modInfoLabel.AddToClassList("tinyFont");
        modInfoLabel.AddToClassList("textShadow");
        modInfoLabel.text = "BetterJukebox is active";
        songInfoContainer.Add(modInfoLabel);

        if (!modSettings.ShowNowPlaying)
        {
            return;
        }

        SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
        SongMeta currentSongMeta = currentSingSceneData.SongMetas.FirstOrDefault();

        nowPlayingLabel = new Label();
        nowPlayingLabel.AddToClassList("smallFont");
        nowPlayingLabel.AddToClassList("textShadow");
        nowPlayingLabel.text = CreateNowPlayingText(currentSongMeta);
        songInfoContainer.Add(nowPlayingLabel);

        int seconds = Math.Max(1, modSettings.NowPlayingSeconds);
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(seconds, HideNowPlayingLabel);
    }

    private void CreateProgressBar()
    {
        // Disabled by default because Melody Mania already has its own progress bar.
        // Keeping this method empty avoids showing a duplicate progress bar.
        return;

        if (!modSettings.ShowProgressBar || uiDocument?.rootVisualElement == null)
        {
            return;
        }

        progressContainer = new VisualElement();
        progressContainer.name = "betterJukeboxProgressBar";
        progressContainer.style.position = Position.Absolute;
        progressContainer.style.left = new StyleLength(new Length(32, LengthUnit.Pixel));
        progressContainer.style.right = new StyleLength(new Length(32, LengthUnit.Pixel));
        progressContainer.style.bottom = new StyleLength(new Length(18, LengthUnit.Pixel));
        progressContainer.style.flexDirection = FlexDirection.Column;
        progressContainer.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
        progressContainer.style.paddingLeft = 12;
        progressContainer.style.paddingRight = 12;
        progressContainer.style.paddingTop = 8;
        progressContainer.style.paddingBottom = 8;
        progressContainer.style.borderTopLeftRadius = 12;
        progressContainer.style.borderTopRightRadius = 12;
        progressContainer.style.borderBottomLeftRadius = 12;
        progressContainer.style.borderBottomRightRadius = 12;

        progressSongLabel = new Label();
        progressSongLabel.AddToClassList("tinyFont");
        progressSongLabel.AddToClassList("textShadow");
        progressSongLabel.style.color = Color.white;
        progressSongLabel.text = GetCurrentSongTitle();
        progressContainer.Add(progressSongLabel);

        VisualElement track = new VisualElement();
        track.style.height = new StyleLength(new Length(6, LengthUnit.Pixel));
        track.style.marginTop = 5;
        track.style.marginBottom = 5;
        track.style.backgroundColor = new Color(1f, 1f, 1f, 0.25f);
        track.style.borderTopLeftRadius = 4;
        track.style.borderTopRightRadius = 4;
        track.style.borderBottomLeftRadius = 4;
        track.style.borderBottomRightRadius = 4;

        progressFill = new VisualElement();
        progressFill.style.height = new StyleLength(new Length(6, LengthUnit.Pixel));
        progressFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
        progressFill.style.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
        progressFill.style.borderTopLeftRadius = 4;
        progressFill.style.borderTopRightRadius = 4;
        progressFill.style.borderBottomLeftRadius = 4;
        progressFill.style.borderBottomRightRadius = 4;
        track.Add(progressFill);
        progressContainer.Add(track);

        progressTimeLabel = new Label("00:00 / 00:00");
        progressTimeLabel.AddToClassList("tinyFont");
        progressTimeLabel.AddToClassList("textShadow");
        progressTimeLabel.style.color = Color.white;
        progressContainer.Add(progressTimeLabel);

        uiDocument.rootVisualElement.Add(progressContainer);
    }

    private void CreateMouseClickBlocker()
    {
        if (uiDocument?.rootVisualElement == null)
        {
            return;
        }

        mouseClickBlocker = new VisualElement();
        mouseClickBlocker.name = "betterJukeboxMouseClickBlocker";
        mouseClickBlocker.style.position = Position.Absolute;
        mouseClickBlocker.style.left = new StyleLength(new Length(0, LengthUnit.Pixel));
        mouseClickBlocker.style.right = new StyleLength(new Length(0, LengthUnit.Pixel));
        mouseClickBlocker.style.top = new StyleLength(new Length(0, LengthUnit.Pixel));
        mouseClickBlocker.style.bottom = new StyleLength(new Length(0, LengthUnit.Pixel));
        mouseClickBlocker.style.backgroundColor = new Color(0, 0, 0, 0);
        mouseClickBlocker.pickingMode = PickingMode.Position;

        mouseClickBlocker.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0 && !IsPointerInsideBetterJukeboxOverlay(evt.position))
            {
                evt.StopImmediatePropagation();
            }
        }, TrickleDown.TrickleDown);

        mouseClickBlocker.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0 && !IsPointerInsideBetterJukeboxOverlay(evt.position))
            {
                evt.StopImmediatePropagation();
            }
        }, TrickleDown.TrickleDown);

        mouseClickBlocker.RegisterCallback<ClickEvent>(evt =>
        {
            if (!IsPointerInsideBetterJukeboxOverlay(evt.position))
            {
                evt.StopImmediatePropagation();
            }
        }, TrickleDown.TrickleDown);

        // Add before the BetterJukebox overlay. The overlay is added afterwards and stays clickable.
        uiDocument.rootVisualElement.Add(mouseClickBlocker);
    }

    private bool IsPointerInsideBetterJukeboxOverlay(Vector2 position)
    {
        try
        {
            if (actionOverlay != null && actionOverlay.style.display != DisplayStyle.None && actionOverlay.worldBound.Contains(position))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void CreateActionOverlay()
    {
        if (!modSettings.ShowMouseOverlay || uiDocument?.rootVisualElement == null)
        {
            return;
        }

        actionOverlay = new VisualElement();
        actionOverlay.name = "betterJukeboxActionOverlay";
        actionOverlay.style.position = Position.Absolute;
        actionOverlay.style.left = new StyleLength(new Length(0, LengthUnit.Pixel));
        actionOverlay.style.right = new StyleLength(new Length(0, LengthUnit.Pixel));
        actionOverlay.style.bottom = new StyleLength(new Length(70, LengthUnit.Pixel));
        actionOverlay.style.flexDirection = FlexDirection.Column;
        actionOverlay.style.justifyContent = Justify.Center;
        actionOverlay.style.alignItems = Align.Center;
        actionOverlay.style.display = DisplayStyle.None;
        actionOverlay.focusable = true;

        VisualElement buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.justifyContent = Justify.Center;
        buttonRow.style.alignItems = Align.Center;
        buttonRow.style.paddingTop = 10;
        buttonRow.style.paddingBottom = 10;

        Button singButton = CreateOverlayButton("🎤 Sing!", StartSingingNow);
        Button previousButton = CreateOverlayIconButton("⏮", StartPreviousSong, "Previous song");
        Button playPauseButton = CreateOverlayIconButton("⏯", TogglePlayPause, "Pause / play");
        Button nextButton = CreateOverlayIconButton("⏭", StartNextSong, "Next song");
        Button searchButton = CreateOverlayButton("🔍 Search", ToggleSearchPanel);
        Button queueButton = CreateOverlayButton("📋 Que", ToggleQueuePanel);
        Button historyButton = CreateOverlayButton("🕘 History", ToggleHistoryPanel);
        Button settingsButton = CreateOverlayIconButton("⚙", ToggleSettingsPanel, "BetterJukebox settings");

        buttonRow.Add(singButton);
        buttonRow.Add(previousButton);
        buttonRow.Add(playPauseButton);
        buttonRow.Add(nextButton);
        buttonRow.Add(searchButton);
        buttonRow.Add(queueButton);
        buttonRow.Add(historyButton);
        buttonRow.Add(settingsButton);

        actionOverlay.Add(buttonRow);
        CreateSearchPanel();
        CreateQueuePanel();
        CreateHistoryPanel();
        CreateSettingsPanel();

        uiDocument.rootVisualElement.Add(actionOverlay);
        RegisterEscapeHandler();
    }



    private void InstallSearchKeyboardBlocker()
    {
        try
        {
            if (searchKeyboardBlocker != null)
            {
                return;
            }

            searchKeyboardBlocker = (eventPtr, device) =>
            {
                try
                {
                    if ((!searchPanelIsVisible && !settingsPanelIsVisible) || !(device is Keyboard))
                    {
                        return;
                    }

                    foreach (InputControl control in eventPtr.EnumerateChangedControls(device))
                    {
                        KeyControl keyControl = control as KeyControl;
                        if (keyControl == null)
                        {
                            continue;
                        }

                        float value = 0f;
                        try
                        {
                            value = keyControl.ReadValueFromEvent(eventPtr);
                        }
                        catch
                        {
                            value = keyControl.ReadValue();
                        }

                        if (value < 0.5f)
                        {
                            continue;
                        }

                        bool shift = Keyboard.current != null
                                     && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

                        if (keyControl.keyCode == Key.M
                            || keyControl.keyCode == Key.P
                            || keyControl.keyCode == Key.R)
                        {
                            // Block Melody Mania global shortcuts only.
                            // Do not write the character manually here, because the TextField already receives it.
                            eventPtr.handled = true;
                            return;
                        }

                        if (keyControl.keyCode == Key.Escape)
                        {
                            if (settingsPanelIsVisible)
                            {
                                pendingSettingsEscape = true;
                            }
                            else
                            {
                                pendingSearchEscape = true;
                            }
                            eventPtr.handled = true;
                            return;
                        }
                    }
                }
                catch
                {
                }
            };

            InputSystem.onEvent += searchKeyboardBlocker;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void RegisterEscapeHandler()
    {
        try
        {
            if (uiDocument?.rootVisualElement == null)
            {
                return;
            }

            uiDocument.rootVisualElement.focusable = true;
            uiDocument.rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (searchPanelIsVisible && searchTextField != null)
                {
                    HandleSearchTextInput(evt);
                    return;
                }

                if (evt.keyCode == KeyCode.Escape && (actionOverlayIsVisible || searchPanelIsVisible || queuePanelIsVisible || historyPanelIsVisible || settingsPanelIsVisible))
                {
                    HandleEscapeInOverlay();
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);

            uiDocument.rootVisualElement.RegisterCallback<KeyUpEvent>(evt =>
            {
                if (searchPanelIsVisible)
                {
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void HandleSearchTextInput(KeyDownEvent evt)
    {
        if (evt == null || !searchPanelIsVisible)
        {
            return;
        }

        // Let the TextField receive normal character input.
        // We only stop propagation after it has had a chance to process the key.
        // Raw InputSystem blocking is disabled because it made typing impossible.
        if (evt.keyCode == KeyCode.Escape)
        {
            HandleEscapeInOverlay();
            evt.StopImmediatePropagation();
        }
    }

    private void SetSearchTextAndCaret(string value, int caretIndex)
    {
        if (searchTextField == null)
        {
            return;
        }

        searchCaretIndex = Mathf.Clamp(caretIndex, 0, (value ?? "").Length);
        searchTextField.value = value ?? "";
        SetSearchCaret(searchCaretIndex);
        UpdateSearchResults(searchTextField.value);
    }

    private void SetSearchCaret(int caretIndex)
    {
        if (searchTextField == null)
        {
            return;
        }

        string value = searchTextField.value ?? "";
        searchCaretIndex = Mathf.Clamp(caretIndex, 0, value.Length);

        try
        {
            searchTextField.cursorIndex = searchCaretIndex;
            searchTextField.selectIndex = searchCaretIndex;
        }
        catch
        {
        }
    }

    private void HandleEscapeInOverlay()
    {
        if (queuePanelIsVisible)
        {
            queuePanelIsVisible = false;
            if (queuePanel != null)
            {
                queuePanel.style.display = DisplayStyle.None;
            }
            lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            return;
        }

        if (historyPanelIsVisible)
        {
            historyPanelIsVisible = false;
            if (historyPanel != null)
            {
                historyPanel.style.display = DisplayStyle.None;
            }
            lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            return;
        }

        if (settingsPanelIsVisible)
        {
            settingsPanelIsVisible = false;
            if (settingsPanel != null)
            {
                settingsPanel.style.display = DisplayStyle.None;
            }
            lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            return;
        }

        if (searchPanelIsVisible)
        {
            searchPanelIsVisible = false;
            if (searchPanel != null)
            {
                searchPanel.style.display = DisplayStyle.None;
            }
            lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            return;
        }

        HideActionOverlay();
    }

    private void CreateSearchPanel()
    {
        searchPanel = new VisualElement();
        searchPanel.name = "betterJukeboxSearchPanel";
        searchPanel.focusable = true;
        searchPanel.RegisterCallback<KeyDownEvent>(evt => HandleSearchTextInput(evt), TrickleDown.TrickleDown);
        searchPanel.RegisterCallback<KeyUpEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
        searchPanel.style.display = DisplayStyle.None;
        searchPanel.style.position = Position.Absolute;
        searchPanel.style.flexDirection = FlexDirection.Column;
        searchPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.88f);
        searchPanel.style.paddingLeft = 18;
        searchPanel.style.paddingRight = 18;
        searchPanel.style.paddingTop = 12;
        searchPanel.style.paddingBottom = 12;
        searchPanel.style.borderTopLeftRadius = 18;
        searchPanel.style.borderTopRightRadius = 18;
        searchPanel.style.borderBottomLeftRadius = 18;
        searchPanel.style.borderBottomRightRadius = 18;

        VisualElement headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 6;

        Label title = new Label("Search songs");
        title.AddToClassList("smallFont");
        title.AddToClassList("textShadow");
        title.style.color = Color.white;
        title.style.flexGrow = 1;
        headerRow.Add(title);

        Button closeButton = CreateSmallPanelButton("✕", CloseSearchPanel);
        closeButton.tooltip = "Close search";
        closeButton.style.minWidth = 38;
        headerRow.Add(closeButton);
        searchPanel.Add(headerRow);

        searchTextField = new TextField();
        searchTextField.style.marginTop = 8;
        searchTextField.style.marginBottom = 8;
        searchTextField.RegisterValueChangedCallback(evt => UpdateSearchResults(evt.newValue));
        searchTextField.RegisterCallback<KeyDownEvent>(evt => HandleSearchTextInput(evt), TrickleDown.TrickleDown);
        searchTextField.RegisterCallback<KeyUpEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
        searchTextField.RegisterCallback<FocusOutEvent>(evt =>
        {
            if (searchPanelIsVisible)
            {
                AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
                {
                    if (searchPanelIsVisible && searchTextField != null)
                    {
                        searchTextField.Focus();
                    }
                });
            }
        });
        searchPanel.Add(searchTextField);

        ScrollView searchScrollView = new ScrollView(ScrollViewMode.Vertical);
        searchScrollView.name = "betterJukeboxSearchScrollView";
        searchScrollView.style.flexGrow = 1;

        searchResultsContainer = new VisualElement();
        searchResultsContainer.style.flexDirection = FlexDirection.Column;
        searchScrollView.Add(searchResultsContainer);
        searchPanel.Add(searchScrollView);

        uiDocument.rootVisualElement.Add(searchPanel);
        UpdatePopupPanelLayout(searchPanel, "betterJukeboxSearchScrollView", 118f, 620f, 0.74f);
    }

    private void CreateHistoryPanel()
    {
        historyPanel = new VisualElement();
        historyPanel.name = "betterJukeboxHistoryPanel";
        historyPanel.style.display = DisplayStyle.None;
        historyPanel.style.flexDirection = FlexDirection.Column;
        historyPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.82f);
        historyPanel.style.paddingLeft = 18;
        historyPanel.style.paddingRight = 18;
        historyPanel.style.paddingTop = 12;
        historyPanel.style.paddingBottom = 12;
        historyPanel.style.marginTop = 4;
        historyPanel.style.borderTopLeftRadius = 18;
        historyPanel.style.borderTopRightRadius = 18;
        historyPanel.style.borderBottomLeftRadius = 18;
        historyPanel.style.borderBottomRightRadius = 18;

        Label title = new Label("History");
        title.AddToClassList("smallFont");
        title.AddToClassList("textShadow");
        title.style.color = Color.white;
        historyPanel.Add(title);

        historyResultsContainer = new VisualElement();
        historyResultsContainer.style.flexDirection = FlexDirection.Column;
        historyResultsContainer.style.marginTop = 8;
        historyPanel.Add(historyResultsContainer);

        actionOverlay.Add(historyPanel);
    }

    private void CreateQueuePanel()
    {
        queuePanel = new VisualElement();
        queuePanel.name = "betterJukeboxQueuePanel";
        queuePanel.focusable = true;
        queuePanel.style.display = DisplayStyle.None;
        queuePanel.style.position = Position.Absolute;
        queuePanel.style.flexDirection = FlexDirection.Column;
        queuePanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.88f);
        queuePanel.style.paddingLeft = 18;
        queuePanel.style.paddingRight = 18;
        queuePanel.style.paddingTop = 12;
        queuePanel.style.paddingBottom = 12;
        queuePanel.style.borderTopLeftRadius = 18;
        queuePanel.style.borderTopRightRadius = 18;
        queuePanel.style.borderBottomLeftRadius = 18;
        queuePanel.style.borderBottomRightRadius = 18;

        VisualElement headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 6;

        Label title = new Label("Que");
        title.AddToClassList("smallFont");
        title.AddToClassList("textShadow");
        title.style.color = Color.white;
        title.style.flexGrow = 1;
        headerRow.Add(title);

        Button closeButton = CreateSmallPanelButton("✕", CloseQueuePanel);
        closeButton.tooltip = "Close que";
        closeButton.style.minWidth = 38;
        headerRow.Add(closeButton);
        queuePanel.Add(headerRow);

        ScrollView queueScrollView = new ScrollView(ScrollViewMode.Vertical);
        queueScrollView.name = "betterJukeboxQueueScrollView";
        queueScrollView.style.flexGrow = 1;

        queueResultsContainer = new VisualElement();
        queueResultsContainer.style.flexDirection = FlexDirection.Column;
        queueResultsContainer.style.marginTop = 8;
        queueScrollView.Add(queueResultsContainer);
        queuePanel.Add(queueScrollView);

        uiDocument.rootVisualElement.Add(queuePanel);
        UpdatePopupPanelLayout(queuePanel, "betterJukeboxQueueScrollView", 72f, 620f, 0.74f);
    }


    private void CreateSettingsPanel()
    {
        settingsPanel = new VisualElement();
        settingsPanel.name = "betterJukeboxSettingsPanel";
        settingsPanel.focusable = true;
        settingsPanel.style.display = DisplayStyle.None;
        settingsPanel.style.position = Position.Absolute;
        settingsPanel.style.left = new StyleLength(new Length(24, LengthUnit.Pixel));
        settingsPanel.style.top = new StyleLength(new Length(24, LengthUnit.Pixel));
        settingsPanel.style.width = new StyleLength(new Length(720, LengthUnit.Pixel));
        settingsPanel.style.height = new StyleLength(new Length(520, LengthUnit.Pixel));
        settingsPanel.style.flexDirection = FlexDirection.Column;
        settingsPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.88f);
        settingsPanel.style.paddingLeft = 18;
        settingsPanel.style.paddingRight = 18;
        settingsPanel.style.paddingTop = 12;
        settingsPanel.style.paddingBottom = 12;
        settingsPanel.style.borderTopLeftRadius = 18;
        settingsPanel.style.borderTopRightRadius = 18;
        settingsPanel.style.borderBottomLeftRadius = 18;
        settingsPanel.style.borderBottomRightRadius = 18;

        VisualElement headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 6;

        Label title = new Label("⚙ BetterJukebox Settings");
        title.AddToClassList("smallFont");
        title.AddToClassList("textShadow");
        title.style.color = Color.white;
        title.style.flexGrow = 1;
        headerRow.Add(title);

        Button closeButton = CreateSmallPanelButton("✕", CloseSettingsPanel);
        closeButton.tooltip = "Close settings";
        closeButton.style.minWidth = 38;
        headerRow.Add(closeButton);
        settingsPanel.Add(headerRow);

        Label version = CreatePanelLabel("Version 1.0");
        version.style.marginBottom = 8;
        settingsPanel.Add(version);

        ScrollView settingsScrollView = new ScrollView(ScrollViewMode.Vertical);
        settingsScrollView.name = "betterJukeboxSettingsScrollView";
        settingsScrollView.style.flexGrow = 1;

        settingsResultsContainer = new VisualElement();
        settingsResultsContainer.style.flexDirection = FlexDirection.Column;
        settingsResultsContainer.style.marginTop = 8;
        settingsScrollView.Add(settingsResultsContainer);
        settingsPanel.Add(settingsScrollView);

        uiDocument.rootVisualElement.Add(settingsPanel);
        UpdateSettingsPanelLayout();
    }

    private void UpdateSettingsPanelLayout()
    {
        if (settingsPanel == null)
        {
            return;
        }

        try
        {
            VisualElement root = uiDocument != null ? uiDocument.rootVisualElement : null;
            float rootWidth = root != null ? root.resolvedStyle.width : 0f;
            float rootHeight = root != null ? root.resolvedStyle.height : 0f;

            if (float.IsNaN(rootWidth) || rootWidth < 320f)
            {
                rootWidth = Mathf.Max(320f, Screen.width);
            }
            if (float.IsNaN(rootHeight) || rootHeight < 240f)
            {
                rootHeight = Mathf.Max(240f, Screen.height);
            }

            float margin = Mathf.Clamp(Mathf.Min(rootWidth, rootHeight) * 0.035f, 12f, 32f);
            float maxWidthInsideScreen = Mathf.Max(280f, rootWidth - (margin * 2f));
            float maxHeightInsideScreen = Mathf.Max(220f, rootHeight - (margin * 2f));

            float panelWidth = Mathf.Min(820f, maxWidthInsideScreen);
            float panelHeight = Mathf.Min(maxHeightInsideScreen, Mathf.Max(260f, rootHeight * 0.82f));

            float left = Mathf.Clamp((rootWidth - panelWidth) * 0.5f, margin, Mathf.Max(margin, rootWidth - panelWidth - margin));
            float top = Mathf.Clamp((rootHeight - panelHeight) * 0.5f, margin, Mathf.Max(margin, rootHeight - panelHeight - margin));

            // Important: always clear right/bottom and set a fully clamped rectangle.
            // This prevents the settings panel from being positioned outside the visible UI area
            // on ultrawide, windowed, scaled, or low-resolution displays.
            settingsPanel.style.right = StyleKeyword.Auto;
            settingsPanel.style.bottom = StyleKeyword.Auto;
            settingsPanel.style.left = new StyleLength(new Length(left, LengthUnit.Pixel));
            settingsPanel.style.top = new StyleLength(new Length(top, LengthUnit.Pixel));
            settingsPanel.style.width = new StyleLength(new Length(panelWidth, LengthUnit.Pixel));
            settingsPanel.style.height = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));
            settingsPanel.style.maxHeight = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));

            ScrollView settingsScrollView = settingsPanel.Q<ScrollView>("betterJukeboxSettingsScrollView");
            if (settingsScrollView != null)
            {
                float scrollMaxHeight = Mathf.Max(120f, panelHeight - 92f);
                settingsScrollView.style.height = new StyleLength(new Length(scrollMaxHeight, LengthUnit.Pixel));
                settingsScrollView.style.maxHeight = new StyleLength(new Length(scrollMaxHeight, LengthUnit.Pixel));
                settingsScrollView.style.flexGrow = 1;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox settings layout failed: " + ex.Message);
        }
    }

    private void UpdatePopupPanelLayout(VisualElement panel, string scrollViewName, float reservedHeight, float maxPanelWidth, float heightFactor)
    {
        if (panel == null)
        {
            return;
        }

        try
        {
            VisualElement root = uiDocument != null ? uiDocument.rootVisualElement : null;
            float rootWidth = root != null ? root.resolvedStyle.width : 0f;
            float rootHeight = root != null ? root.resolvedStyle.height : 0f;

            if (float.IsNaN(rootWidth) || rootWidth < 320f)
            {
                rootWidth = Mathf.Max(320f, Screen.width);
            }
            if (float.IsNaN(rootHeight) || rootHeight < 240f)
            {
                rootHeight = Mathf.Max(240f, Screen.height);
            }

            float margin = Mathf.Clamp(Mathf.Min(rootWidth, rootHeight) * 0.035f, 12f, 32f);
            float maxWidthInsideScreen = Mathf.Max(280f, rootWidth - (margin * 2f));
            float maxHeightInsideScreen = Mathf.Max(180f, rootHeight - (margin * 2f));

            float panelWidth = Mathf.Min(maxPanelWidth, maxWidthInsideScreen);
            float panelHeight = Mathf.Min(maxHeightInsideScreen, Mathf.Max(220f, rootHeight * heightFactor));

            float left = Mathf.Clamp((rootWidth - panelWidth) * 0.5f, margin, Mathf.Max(margin, rootWidth - panelWidth - margin));
            float top = Mathf.Clamp((rootHeight - panelHeight) * 0.5f, margin, Mathf.Max(margin, rootHeight - panelHeight - margin));

            panel.style.right = StyleKeyword.Auto;
            panel.style.bottom = StyleKeyword.Auto;
            panel.style.left = new StyleLength(new Length(left, LengthUnit.Pixel));
            panel.style.top = new StyleLength(new Length(top, LengthUnit.Pixel));
            panel.style.width = new StyleLength(new Length(panelWidth, LengthUnit.Pixel));
            panel.style.height = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));
            panel.style.maxHeight = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));

            ScrollView scrollView = panel.Q<ScrollView>(scrollViewName);
            if (scrollView != null)
            {
                float scrollMaxHeight = Mathf.Max(90f, panelHeight - reservedHeight);
                scrollView.style.height = new StyleLength(new Length(scrollMaxHeight, LengthUnit.Pixel));
                scrollView.style.maxHeight = new StyleLength(new Length(scrollMaxHeight, LengthUnit.Pixel));
                scrollView.style.flexGrow = 1;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox popup layout failed: " + ex.Message);
        }
    }

    private void UpdateSearchPanelLayout()
    {
        UpdatePopupPanelLayout(searchPanel, "betterJukeboxSearchScrollView", 118f, 620f, 0.74f);
    }

    private void UpdateQueuePanelLayout()
    {
        UpdatePopupPanelLayout(queuePanel, "betterJukeboxQueueScrollView", 72f, 620f, 0.74f);
    }

    private void CloseSearchPanel()
    {
        searchPanelIsVisible = false;
        if (searchPanel != null)
        {
            searchPanel.style.display = DisplayStyle.None;
        }
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;
    }

    private void CloseQueuePanel()
    {
        queuePanelIsVisible = false;
        if (queuePanel != null)
        {
            queuePanel.style.display = DisplayStyle.None;
        }
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;
    }

    private void CloseSettingsPanel()
    {
        settingsPanelIsVisible = false;
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
        }
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;
    }

    private void ToggleSettingsPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        settingsPanelIsVisible = !settingsPanelIsVisible;
        searchPanelIsVisible = false;
        queuePanelIsVisible = false;
        historyPanelIsVisible = false;

        if (settingsPanel != null)
        {
            settingsPanel.style.display = settingsPanelIsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (settingsPanelIsVisible)
            {
                UpdateSettingsPanelLayout();
                settingsPanel.BringToFront();
                settingsPanel.Focus();
            }
        }
        if (searchPanel != null)
        {
            searchPanel.style.display = DisplayStyle.None;
        }
        if (queuePanel != null)
        {
            queuePanel.style.display = DisplayStyle.None;
        }
        if (historyPanel != null)
        {
            historyPanel.style.display = DisplayStyle.None;
        }
        if (settingsPanelIsVisible)
        {
            UpdateSettingsPanel();
            settingsPanel.Focus();
        }
    }

    private void UpdateSettingsPanel()
    {
        if (settingsResultsContainer == null)
        {
            return;
        }

        settingsResultsContainer.Clear();

        settingsResultsContainer.Add(CreateSettingsSectionLabel("General"));
        settingsResultsContainer.Add(CreateSettingsToggle("Auto Open Sing", () => modSettings.AutoOpenSing, value => modSettings.AutoOpenSing = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Auto Play Random Song", () => modSettings.AutoPlayRandomSong, value => modSettings.AutoPlayRandomSong = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Shuffle", () => modSettings.RandomSelection, value => modSettings.RandomSelection = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Show Overlay", () => modSettings.ShowMouseOverlay, value => modSettings.ShowMouseOverlay = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Fade In / Fade Out", () => modSettings.FadeAnimations, value => modSettings.FadeAnimations = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Auto Hide Menu", () => modSettings.AutoHideMenu, value => modSettings.AutoHideMenu = value));
        settingsResultsContainer.Add(CreateSettingsCycleButton("Animation Speed", GetAnimationSpeedLabel(), CycleAnimationSpeed));
        settingsResultsContainer.Add(CreateSettingsToggle("Disable Vanilla Pause Button", () => modSettings.HideBuiltInPauseButton, value => modSettings.HideBuiltInPauseButton = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Auto Continue", () => modSettings.AutoContinue, value => modSettings.AutoContinue = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Show Now Playing", () => modSettings.ShowNowPlaying, value => modSettings.ShowNowPlaying = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Hide Lyrics", () => modSettings.HideLyrics, value => modSettings.HideLyrics = value));
        settingsResultsContainer.Add(CreateSettingsCycleButton("Now Playing Seconds", modSettings.NowPlayingSeconds.ToString() + " sec", CycleNowPlayingSeconds));

        settingsResultsContainer.Add(CreateSettingsSectionLabel("Controls"));
        settingsResultsContainer.Add(CreateSettingsToggle("Shake Mouse To Show Menu", () => modSettings.ShakeMouseToShowMenu, value => modSettings.ShakeMouseToShowMenu = value));
        settingsResultsContainer.Add(CreateSettingsToggle("Hide Mouse After Timeout", () => modSettings.HideMouseAfterTimeout, value => modSettings.HideMouseAfterTimeout = value));
        settingsResultsContainer.Add(CreateSettingsCycleButton("Overlay Hide Seconds", modSettings.OverlayHideSeconds.ToString() + " sec", CycleOverlayHideSeconds));

        settingsResultsContainer.Add(CreateSettingsSectionLabel("About"));
        Label disableInfo = CreatePanelLabel("To fully disable BetterJukebox, open Melody Mania > Mods and disable the mod there. This avoids two different enable states.");
        disableInfo.style.whiteSpace = WhiteSpace.Normal;
        settingsResultsContainer.Add(disableInfo);
    }

    private Label CreateSettingsSectionLabel(string text)
    {
        Label label = CreatePanelLabel(text);
        label.style.marginTop = 10;
        label.style.marginBottom = 4;
        label.style.color = new Color(1f, 1f, 1f, 0.95f);
        return label;
    }

    private VisualElement CreateSettingsToggle(string labelText, Func<bool> getter, Action<bool> setter)
    {
        VisualElement row = CreatePanelRow();
        Label label = CreatePanelLabel(labelText);
        label.style.flexGrow = 1;
        row.Add(label);

        Button button = CreateSmallPanelButton(getter() ? "On" : "Off", () =>
        {
            setter(!getter());
            UpdateSettingsPanel();
        });
        row.Add(button);
        return row;
    }

    private VisualElement CreateSettingsCycleButton(string labelText, string valueText, Action clicked)
    {
        VisualElement row = CreatePanelRow();
        Label label = CreatePanelLabel(labelText);
        label.style.flexGrow = 1;
        row.Add(label);
        row.Add(CreateSmallPanelButton(valueText, () =>
        {
            clicked();
            UpdateSettingsPanel();
        }));
        return row;
    }

    private void CycleNowPlayingSeconds()
    {
        modSettings.NowPlayingSeconds += 1;
        if (modSettings.NowPlayingSeconds > 12)
        {
            modSettings.NowPlayingSeconds = 3;
        }
    }

    private void CycleOverlayHideSeconds()
    {
        modSettings.OverlayHideSeconds += 1;
        if (modSettings.OverlayHideSeconds > 10)
        {
            modSettings.OverlayHideSeconds = 2;
        }
    }

    private string GetAnimationSpeedLabel()
    {
        if (modSettings.AnimationSpeed <= 0)
        {
            return "Slow";
        }
        if (modSettings.AnimationSpeed >= 2)
        {
            return "Fast";
        }
        return "Normal";
    }

    private void CycleAnimationSpeed()
    {
        modSettings.AnimationSpeed = (modSettings.AnimationSpeed + 1) % 3;
    }

    private float GetOverlayFadeDuration()
    {
        if (modSettings.AnimationSpeed <= 0)
        {
            return 0.35f;
        }
        if (modSettings.AnimationSpeed >= 2)
        {
            return 0.12f;
        }
        return 0.22f;
    }

    private Button CreateOverlayButton(string text, Action clicked)
    {
        Button button = new Button(clicked);
        button.text = text;
        button.AddToClassList("smallFont");
        button.style.marginLeft = 6;
        button.style.marginRight = 6;
        button.style.paddingLeft = 18;
        button.style.paddingRight = 18;
        button.style.paddingTop = 10;
        button.style.paddingBottom = 10;
        button.style.borderTopLeftRadius = 18;
        button.style.borderTopRightRadius = 18;
        button.style.borderBottomLeftRadius = 18;
        button.style.borderBottomRightRadius = 18;
        button.style.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
        button.style.color = Color.white;
        return button;
    }

    private Button CreateOverlayIconButton(string text, Action clicked, string tooltip)
    {
        Button button = CreateOverlayButton(text, clicked);
        button.tooltip = tooltip;
        button.style.paddingLeft = 14;
        button.style.paddingRight = 14;
        button.style.minWidth = 48;
        return button;
    }

    private string CreateNowPlayingText(SongMeta songMeta)
    {
        string artistTitle = songMeta != null ? songMeta.GetArtistDashTitle() : "Unknown song";
        return "♪ Now Playing\n" + artistTitle;
    }

    private void HideNowPlayingLabel()
    {
        if (nowPlayingLabel == null || nowPlayingWasHidden)
        {
            return;
        }

        nowPlayingWasHidden = true;
        AnimationUtils.FadeOutVisualElement(gameObject, nowPlayingLabel, 1f);
    }

    private void Update()
    {
        if (!isInjectionFinished)
        {
            return;
        }

        ProcessSettingsKeyboardInput();
        ProcessSearchKeyboardInput();
        UpdateSkipSong();
        SuppressBuiltInMousePause();
        HideBuiltInPauseButton();
        KeepSearchFieldFocused();
        UpdateActionOverlay();
        UpdateProgressBar();
        UpdateUiElementsFadeOut();
        UpdateFinishingScene();
    }


    private void ProcessSettingsKeyboardInput()
    {
        if (!settingsPanelIsVisible)
        {
            pendingSettingsEscape = false;
            return;
        }

        if (pendingSettingsEscape || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            pendingSettingsEscape = false;
            CloseSettingsPanel();
            return;
        }
    }

    private void ProcessSearchKeyboardInput()
    {
        if (!searchPanelIsVisible || searchTextField == null || Keyboard.current == null)
        {
            return;
        }

        searchTextField.Focus();
        lastSearchFocusTime = Time.unscaledTime;

        try
        {
            searchCaretIndex = Mathf.Clamp(searchTextField.cursorIndex, 0, (searchTextField.value ?? "").Length);
        }
        catch
        {
            searchCaretIndex = Mathf.Clamp(searchCaretIndex, 0, (searchTextField.value ?? "").Length);
        }

        if (pendingSearchEscape || Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            pendingSearchEscape = false;
            pendingSearchTextInput.Clear();
            HandleEscapeInOverlay();
            ResumeIfEscapePausedSong();
            return;
        }

        if (pendingSearchTextInput.Count > 0)
        {
            string pendingText = string.Join("", pendingSearchTextInput.ToArray());
            pendingSearchTextInput.Clear();

            string beforeValue = searchTextField.value ?? "";
            int caret = Mathf.Clamp(searchCaretIndex, 0, beforeValue.Length);
            string newValue = beforeValue.Insert(caret, pendingText);
            SetSearchTextAndCaret(newValue, caret + pendingText.Length);
        }
    }

    private string GetPressedSearchText(Keyboard kb, bool shift)
    {
        string result = "";
        AddPressedChar(kb.aKey, shift ? "A" : "a", ref result);
        AddPressedChar(kb.bKey, shift ? "B" : "b", ref result);
        AddPressedChar(kb.cKey, shift ? "C" : "c", ref result);
        AddPressedChar(kb.dKey, shift ? "D" : "d", ref result);
        AddPressedChar(kb.eKey, shift ? "E" : "e", ref result);
        AddPressedChar(kb.fKey, shift ? "F" : "f", ref result);
        AddPressedChar(kb.gKey, shift ? "G" : "g", ref result);
        AddPressedChar(kb.hKey, shift ? "H" : "h", ref result);
        AddPressedChar(kb.iKey, shift ? "I" : "i", ref result);
        AddPressedChar(kb.jKey, shift ? "J" : "j", ref result);
        AddPressedChar(kb.kKey, shift ? "K" : "k", ref result);
        AddPressedChar(kb.lKey, shift ? "L" : "l", ref result);
        AddPressedChar(kb.mKey, shift ? "M" : "m", ref result);
        AddPressedChar(kb.nKey, shift ? "N" : "n", ref result);
        AddPressedChar(kb.oKey, shift ? "O" : "o", ref result);
        AddPressedChar(kb.pKey, shift ? "P" : "p", ref result);
        AddPressedChar(kb.qKey, shift ? "Q" : "q", ref result);
        AddPressedChar(kb.rKey, shift ? "R" : "r", ref result);
        AddPressedChar(kb.sKey, shift ? "S" : "s", ref result);
        AddPressedChar(kb.tKey, shift ? "T" : "t", ref result);
        AddPressedChar(kb.uKey, shift ? "U" : "u", ref result);
        AddPressedChar(kb.vKey, shift ? "V" : "v", ref result);
        AddPressedChar(kb.wKey, shift ? "W" : "w", ref result);
        AddPressedChar(kb.xKey, shift ? "X" : "x", ref result);
        AddPressedChar(kb.yKey, shift ? "Y" : "y", ref result);
        AddPressedChar(kb.zKey, shift ? "Z" : "z", ref result);
        AddPressedChar(kb.digit0Key, shift ? ")" : "0", ref result);
        AddPressedChar(kb.digit1Key, shift ? "!" : "1", ref result);
        AddPressedChar(kb.digit2Key, shift ? "@" : "2", ref result);
        AddPressedChar(kb.digit3Key, shift ? "#" : "3", ref result);
        AddPressedChar(kb.digit4Key, shift ? "$" : "4", ref result);
        AddPressedChar(kb.digit5Key, shift ? "%" : "5", ref result);
        AddPressedChar(kb.digit6Key, shift ? "^" : "6", ref result);
        AddPressedChar(kb.digit7Key, shift ? "&" : "7", ref result);
        AddPressedChar(kb.digit8Key, shift ? "*" : "8", ref result);
        AddPressedChar(kb.digit9Key, shift ? "(" : "9", ref result);
        AddPressedChar(kb.spaceKey, " ", ref result);
        AddPressedChar(kb.minusKey, shift ? "_" : "-", ref result);
        AddPressedChar(kb.equalsKey, shift ? "+" : "=", ref result);
        AddPressedChar(kb.commaKey, shift ? "<" : ",", ref result);
        AddPressedChar(kb.periodKey, shift ? ">" : ".", ref result);
        AddPressedChar(kb.slashKey, shift ? "?" : "/", ref result);
        AddPressedChar(kb.semicolonKey, shift ? ":" : ";", ref result);
        AddPressedChar(kb.quoteKey, shift ? "\"" : "'", ref result);
        return result;
    }

    private void AddPressedChar(KeyControl key, string text, ref string result)
    {
        if (key != null && key.wasPressedThisFrame)
        {
            result += text;
        }
    }

    private void UpdateSkipSong()
    {
        if (IsTypingInBetterJukeboxTextField())
        {
            return;
        }

        if (InputUtils.IsKeyboardShiftPressed()
            && Keyboard.current != null
            && (Keyboard.current.sKey.wasReleasedThisFrame
                || Keyboard.current.rightArrowKey.wasReleasedThisFrame))
        {
            StartNextSong();
        }
    }

    private bool IsTypingInBetterJukeboxTextField()
    {
        try
        {
            if (searchPanelIsVisible && searchTextField != null)
            {
                return true;
            }

            if (searchTextField == null || uiDocument?.rootVisualElement?.panel?.focusController == null)
            {
                return false;
            }

            Focusable focusedElement = uiDocument.rootVisualElement.panel.focusController.focusedElement;
            if (focusedElement == null)
            {
                return false;
            }

            VisualElement focusedVisualElement = focusedElement as VisualElement;
            while (focusedVisualElement != null)
            {
                if (focusedVisualElement == searchTextField)
                {
                    return true;
                }
                focusedVisualElement = focusedVisualElement.parent;
            }
        }
        catch
        {
        }

        return false;
    }

    private void KeepSearchFieldFocused()
    {
        if (!searchPanelIsVisible || searchTextField == null)
        {
            return;
        }

        if (Time.unscaledTime - lastSearchFocusTime < 0.25f)
        {
            return;
        }

        lastSearchFocusTime = Time.unscaledTime;
        try
        {
            searchTextField.Focus();
        }
        catch
        {
        }
    }

    private void UpdateActionOverlay()
    {
        if (actionOverlay == null || overlayDisabledBySingingMode || !modSettings.EnableBetterJukebox)
        {
            if (actionOverlay != null)
            {
                actionOverlay.style.display = DisplayStyle.None;
            }
            return;
        }

        if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasReleasedThisFrame))
        {
            if (settingsPanelIsVisible)
            {
                CloseSettingsPanel();
                return;
            }

            if (searchPanelIsVisible && searchTextField != null)
            {
                return;
            }

            HandleEscapeInOverlay();
            return;
        }

        if (Mouse.current != null)
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            if (!hasLastMousePosition)
            {
                lastMousePosition = mousePosition;
                hasLastMousePosition = true;
            }
            else
            {
                float distance = Vector2.Distance(lastMousePosition, mousePosition);
                if (distance > 0.5f)
                {
                    UnityEngine.Cursor.visible = true;
                }
                lastMousePosition = mousePosition;

                if (distance > 2f && modSettings.ShakeMouseToShowMenu)
                {
                    if (actionOverlayIsVisible)
                    {
                        lastOverlayActivityTimeInSeconds = Time.unscaledTime;
                    }
                    else
                    {
                        if (mouseMovementStartedAt < 0 || Time.unscaledTime - mouseMovementStartedAt > 1.25f)
                        {
                            mouseMovementStartedAt = Time.unscaledTime;
                            mouseMovementAccumulatedDistance = 0f;
                        }

                        mouseMovementAccumulatedDistance += distance;

                        float requiredMovementTime = singingModeStarted ? 1.0f : 0.5f;
                        float requiredDistance = singingModeStarted ? 55f : 20f;

                        if (Time.unscaledTime - mouseMovementStartedAt >= requiredMovementTime
                            && mouseMovementAccumulatedDistance >= requiredDistance)
                        {
                            ShowActionOverlay();
                            mouseMovementStartedAt = -1f;
                            mouseMovementAccumulatedDistance = 0f;
                        }
                    }
                }
                else if (!actionOverlayIsVisible && mouseMovementStartedAt >= 0 && Time.unscaledTime - mouseMovementStartedAt > 1.25f)
                {
                    mouseMovementStartedAt = -1f;
                    mouseMovementAccumulatedDistance = 0f;
                }
            }
        }

        // Only auto-hide the small action overlay.
        // Search and queue panels must stay open until the user chooses something or presses Escape.
        if (settingsPanelIsVisible)
        {
            UpdateSettingsPanelLayout();
        }
        if (searchPanelIsVisible)
        {
            UpdateSearchPanelLayout();
        }
        if (queuePanelIsVisible)
        {
            UpdateQueuePanelLayout();
        }

        if (searchPanelIsVisible || queuePanelIsVisible || historyPanelIsVisible || settingsPanelIsVisible)
        {
            return;
        }

        if (!modSettings.AutoHideMenu)
        {
            return;
        }

        int hideSeconds = Math.Max(1, modSettings.OverlayHideSeconds);
        if (modSettings.HideMouseAfterTimeout && !actionOverlayIsVisible && Time.unscaledTime - lastOverlayActivityTimeInSeconds >= hideSeconds)
        {
            UnityEngine.Cursor.visible = false;
        }

        if (actionOverlayIsVisible
            && Time.unscaledTime - lastOverlayActivityTimeInSeconds >= hideSeconds)
        {
            HideActionOverlay();
            mouseMovementStartedAt = -1f;
            mouseMovementAccumulatedDistance = 0f;
        }
    }

    private void ShowActionOverlay()
    {
        if (overlayDisabledBySingingMode || !modSettings.EnableBetterJukebox)
        {
            return;
        }

        if (actionOverlay == null)
        {
            return;
        }

        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        if (actionOverlayIsVisible)
        {
            return;
        }

        actionOverlayIsVisible = true;
        UnityEngine.Cursor.visible = true;
        actionOverlay.style.display = DisplayStyle.Flex;
        actionOverlay.BringToFront();
        actionOverlay.Focus();
        if (modSettings.FadeAnimations)
        {
            AnimationUtils.FadeInVisualElement(gameObject, actionOverlay, GetOverlayFadeDuration());
        }
    }

    private void HideActionOverlay()
    {
        if (actionOverlay == null)
        {
            return;
        }

        actionOverlayIsVisible = false;
        searchPanelIsVisible = false;
        queuePanelIsVisible = false;
        historyPanelIsVisible = false;
        settingsPanelIsVisible = false;

        if (searchPanel != null)
        {
            searchPanel.style.display = DisplayStyle.None;
        }
        if (queuePanel != null)
        {
            queuePanel.style.display = DisplayStyle.None;
        }
        if (historyPanel != null)
        {
            historyPanel.style.display = DisplayStyle.None;
        }
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
        }

        if (modSettings.FadeAnimations)
        {
            AnimationUtils.FadeOutVisualElement(gameObject, actionOverlay, GetOverlayFadeDuration());
            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(GetOverlayFadeDuration(), () =>
            {
                if (!actionOverlayIsVisible && actionOverlay != null)
                {
                    actionOverlay.style.display = DisplayStyle.None;
                }
            });
        }
        else
        {
            actionOverlay.style.display = DisplayStyle.None;
        }
        mouseMovementStartedAt = -1f;
        mouseMovementAccumulatedDistance = 0f;
    }

    private void ToggleSearchPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        searchPanelIsVisible = !searchPanelIsVisible;
        queuePanelIsVisible = false;
        historyPanelIsVisible = false;
        settingsPanelIsVisible = false;

        if (searchPanel != null)
        {
            searchPanel.style.display = searchPanelIsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (queuePanel != null)
        {
            queuePanel.style.display = DisplayStyle.None;
        }
        if (historyPanel != null)
        {
            historyPanel.style.display = DisplayStyle.None;
        }
        if (settingsPanelIsVisible)
        {
            settingsPanelIsVisible = false;
            if (settingsPanel != null)
            {
                settingsPanel.style.display = DisplayStyle.None;
            }
            lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            return;
        }

        if (searchPanelIsVisible)
        {
            UpdateSearchPanelLayout();
            searchPanel.BringToFront();
            UpdateSearchResults(searchTextField?.value ?? "");
            lastSearchFocusTime = Time.unscaledTime;
            searchTextField?.Focus();
        }
    }

    private void ToggleQueuePanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        queuePanelIsVisible = !queuePanelIsVisible;
        searchPanelIsVisible = false;
        historyPanelIsVisible = false;
        settingsPanelIsVisible = false;

        if (queuePanel != null)
        {
            queuePanel.style.display = queuePanelIsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (searchPanel != null)
        {
            searchPanel.style.display = DisplayStyle.None;
        }
        if (historyPanel != null)
        {
            historyPanel.style.display = DisplayStyle.None;
        }
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
        }

        if (queuePanelIsVisible)
        {
            UpdateQueuePanelLayout();
            queuePanel.BringToFront();
            UpdateQueuePanel();
            queuePanel.focusable = true;
            queuePanel.Focus();
        }
    }

    private void ToggleHistoryPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        historyPanelIsVisible = !historyPanelIsVisible;
        searchPanelIsVisible = false;
        queuePanelIsVisible = false;
        settingsPanelIsVisible = false;

        if (historyPanel != null)
        {
            historyPanel.style.display = historyPanelIsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (searchPanel != null)
        {
            searchPanel.style.display = DisplayStyle.None;
        }
        if (queuePanel != null)
        {
            queuePanel.style.display = DisplayStyle.None;
        }
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
        }

        if (historyPanelIsVisible)
        {
            UpdateHistoryPanel();
            historyPanel.focusable = true;
            historyPanel.Focus();
        }
    }

    private void UpdateSearchResults(string searchText)
    {
        if (searchResultsContainer == null)
        {
            return;
        }

        searchResultsContainer.Clear();

        if (string.IsNullOrWhiteSpace(searchText) || searchText.Trim().Length < 2)
        {
            Label hint = CreatePanelLabel("Type at least 2 characters to search.");
            searchResultsContainer.Add(hint);
            return;
        }

        string query = searchText.Trim().ToLowerInvariant();
        List<SongMeta> matches = GetAllSelectableSongMetas()
            .Where(songMeta => MatchesSearch(songMeta, query))
            .Take(8)
            .ToList();

        if (matches.IsNullOrEmpty())
        {
            searchResultsContainer.Add(CreatePanelLabel("No matches."));
            return;
        }

        foreach (SongMeta songMeta in matches)
        {
            searchResultsContainer.Add(CreateSearchResultRow(songMeta));
        }
    }

    private bool MatchesSearch(SongMeta songMeta, string query)
    {
        string artist = songMeta?.Artist ?? "";
        string title = songMeta?.Title ?? "";
        string combined = (artist + " " + title).ToLowerInvariant();
        return combined.Contains(query);
    }

    private VisualElement CreateSearchResultRow(SongMeta songMeta)
    {
        VisualElement row = CreatePanelRow();

        Label label = CreatePanelLabel(songMeta.GetArtistDashTitle());
        label.style.flexGrow = 1;
        row.Add(label);

        Button playNowButton = CreateSmallPanelButton("Play now", () => PlaySongNow(songMeta));
        Button queueButton = CreateSmallPanelButton("Que", () => AddSongToQueue(songMeta));

        row.Add(playNowButton);
        row.Add(queueButton);
        return row;
    }

    private void UpdateQueuePanel()
    {
        if (queueResultsContainer == null)
        {
            return;
        }

        queueResultsContainer.Clear();

        if (betterJukeboxQueue.Count == 0)
        {
            queueResultsContainer.Add(CreatePanelLabel("Quen är tom. Next song väljs enligt spelläget."));
            return;
        }

        for (int i = 0; i < betterJukeboxQueue.Count; i++)
        {
            int index = i;
            SongMeta songMeta = betterJukeboxQueue[index];
            VisualElement row = CreatePanelRow();

            Label label = CreatePanelLabel((index + 1) + ". " + songMeta.GetArtistDashTitle());
            label.style.flexGrow = 1;
            row.Add(label);

            row.Add(CreateSmallPanelButton("Up", () => MoveQueueItem(index, -1)));
            row.Add(CreateSmallPanelButton("Down", () => MoveQueueItem(index, 1)));
            row.Add(CreateSmallPanelButton("Remove", () => RemoveQueueItem(index)));

            queueResultsContainer.Add(row);
        }
    }

    private void UpdateHistoryPanel()
    {
        if (historyResultsContainer == null)
        {
            return;
        }

        historyResultsContainer.Clear();

        if (betterJukeboxHistory.Count == 0)
        {
            historyResultsContainer.Add(CreatePanelLabel("No history yet."));
            return;
        }

        foreach (SongMeta songMeta in betterJukeboxHistory.Take(20))
        {
            VisualElement row = CreatePanelRow();

            Label label = CreatePanelLabel(songMeta.GetArtistDashTitle());
            label.style.flexGrow = 1;
            row.Add(label);

            row.Add(CreateSmallPanelButton("Play again", () => PlaySongNow(songMeta)));
            row.Add(CreateSmallPanelButton("Que", () => AddSongToQueue(songMeta)));

            historyResultsContainer.Add(row);
        }
    }

    private VisualElement CreatePanelRow()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 4;
        row.style.marginBottom = 4;
        return row;
    }

    private Label CreatePanelLabel(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("tinyFont");
        label.AddToClassList("textShadow");
        label.style.color = Color.white;
        label.style.marginRight = 8;
        return label;
    }

    private Button CreateSmallPanelButton(string text, Action clicked)
    {
        Button button = new Button(clicked);
        button.text = text;
        button.AddToClassList("tinyFont");
        button.style.marginLeft = 4;
        button.style.marginRight = 4;
        button.style.paddingLeft = 10;
        button.style.paddingRight = 10;
        button.style.paddingTop = 5;
        button.style.paddingBottom = 5;
        button.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        button.style.color = Color.white;
        return button;
    }

    private void AddCurrentSongToHistory()
    {
        try
        {
            SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
            SongMeta currentSongMeta = currentSingSceneData.SongMetas.FirstOrDefault();
            AddSongToHistory(currentSongMeta);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void AddSongToHistory(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return;
        }

        betterJukeboxHistory.Remove(songMeta);
        betterJukeboxHistory.Insert(0, songMeta);
        while (betterJukeboxHistory.Count > 50)
        {
            betterJukeboxHistory.RemoveAt(betterJukeboxHistory.Count - 1);
        }
    }

    private void PlaySongNow(SongMeta songMeta)
    {
        Debug.Log($"{nameof(BetterJukeboxControl)} - Play now '{songMeta.GetArtistDashTitle()}'");
        HideActionOverlay();
        LoadSong(songMeta);
    }

    private void AddSongToQueue(SongMeta songMeta)
    {
        Debug.Log($"{nameof(BetterJukeboxControl)} - Added to queue '{songMeta.GetArtistDashTitle()}'");
        betterJukeboxQueue.Add(songMeta);
        NotificationManager.CreateNotification(Translation.Of("Added to que: " + songMeta.GetArtistDashTitle()));
        UpdateSearchResults(searchTextField?.value ?? "");
        UpdateQueuePanel();
    }

    private void MoveQueueItem(int index, int direction)
    {
        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= betterJukeboxQueue.Count)
        {
            return;
        }

        SongMeta songMeta = betterJukeboxQueue[index];
        betterJukeboxQueue.RemoveAt(index);
        betterJukeboxQueue.Insert(newIndex, songMeta);
        UpdateQueuePanel();
    }

    private void RemoveQueueItem(int index)
    {
        if (index < 0 || index >= betterJukeboxQueue.Count)
        {
            return;
        }

        betterJukeboxQueue.RemoveAt(index);
        UpdateQueuePanel();
    }

    private void StartSingingNow()
    {
        Debug.Log($"{nameof(BetterJukeboxControl)} - Start singing current song");
        singingModeStarted = true;
        // Keep the BetterJukebox overlay available, but hide it immediately.
        // If the user accidentally clicked this, a deliberate mouse shake for about one second shows the menu again.
        overlayDisabledBySingingMode = false;
        HideActionOverlay();
        singingUiElements.ForEach(elem => elem.ShowByVisibility());
        FadeInSingingUiElements();
        EnableSingSceneFinisher();
        EnableVfxCamera();
        NotificationManager.CreateNotification(Translation.Of("Singing mode enabled - sing into the mic"));
    }


    private void HideBuiltInPauseButton()
    {
        if (!modSettings.HideBuiltInPauseButton || uiDocument?.rootVisualElement == null)
        {
            return;
        }

        try
        {
            HidePauseLikeElements(uiDocument.rootVisualElement);
            HideCenterPauseOverlay(uiDocument.rootVisualElement);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void HidePauseLikeElements(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        string elementName = (element.name ?? "").ToLowerInvariant();
        bool looksLikePauseButton = elementName.Contains("pause")
                                    || elementName.Contains("playpause")
                                    || elementName.Contains("play-pause");

        Button button = element as Button;
        if (button != null)
        {
            string buttonText = (button.text ?? "").ToLowerInvariant();
            looksLikePauseButton = looksLikePauseButton
                                   || buttonText.Contains("pause")
                                   || buttonText.Contains("paus");
        }

        if (looksLikePauseButton)
        {
            element.style.display = DisplayStyle.None;
            element.visible = false;

            VisualElement parent = element.parent;
            if (parent != null && parent != uiDocument.rootVisualElement)
            {
                parent.style.display = DisplayStyle.None;
                parent.visible = false;
            }
        }

        for (int i = 0; i < element.childCount; i++)
        {
            HidePauseLikeElements(element[i]);
        }
    }

    private void HideCenterPauseOverlay(VisualElement rootElement)
    {
        if (rootElement == null)
        {
            return;
        }

        try
        {
            Rect rootBounds = rootElement.worldBound;
            if (rootBounds.width <= 0 || rootBounds.height <= 0)
            {
                return;
            }

            Vector2 screenCenter = new Vector2(rootBounds.x + rootBounds.width / 2f, rootBounds.y + rootBounds.height / 2f);
            HideCenterPauseOverlayRecursive(rootElement, screenCenter);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void HideCenterPauseOverlayRecursive(VisualElement element, Vector2 screenCenter)
    {
        if (element == null || element == actionOverlay || element == searchPanel || element == queuePanel || element == historyPanel || element == progressContainer)
        {
            return;
        }

        Rect bounds = element.worldBound;
        bool hasReasonablePauseOverlaySize = bounds.width >= 70 && bounds.width <= 260 && bounds.height >= 70 && bounds.height <= 260;
        bool isNearCenter = bounds.Contains(screenCenter);
        Color backgroundColor = element.resolvedStyle.backgroundColor;
        bool hasDarkTransparentBackground = backgroundColor.a >= 0.15f && backgroundColor.r < 0.25f && backgroundColor.g < 0.25f && backgroundColor.b < 0.25f;

        if (hasReasonablePauseOverlaySize && isNearCenter && hasDarkTransparentBackground)
        {
            element.style.display = DisplayStyle.None;
            element.visible = false;
            return;
        }

        for (int i = 0; i < element.childCount; i++)
        {
            HideCenterPauseOverlayRecursive(element[i], screenCenter);
        }
    }

    private void OnDestroy()
    {
        try
        {
            if (searchKeyboardBlocker != null)
            {
                InputSystem.onEvent -= searchKeyboardBlocker;
                searchKeyboardBlocker = null;
            }
        }
        catch
        {
        }
        EnableVfxCamera();
    }

    private void DisableVfxCamera()
    {
        try
        {
            foreach (Transform vfxCamera in Camera.main.transform)
            {
                Camera camera = vfxCamera.GetComponent<Camera>();
                if (camera != null)
                {
                    camera.enabled = false;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void EnableVfxCamera()
    {
        try
        {
            foreach (Transform vfxCamera in Camera.main.transform)
            {
                Camera camera = vfxCamera.GetComponent<Camera>();
                if (camera != null)
                {
                    camera.enabled = true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void UpdateProgressBar()
    {
        if (progressContainer == null || !modSettings.ShowProgressBar)
        {
            return;
        }

        try
        {
            int position = Math.Max(0, (int)songAudioPlayer.PositionInMillis);
            int duration = Math.Max(0, (int)songAudioPlayer.DurationInMillis);
            float percent = duration > 0 ? Mathf.Clamp01(position / (float)duration) * 100f : 0f;

            if (progressFill != null)
            {
                progressFill.style.width = new StyleLength(new Length(percent, LengthUnit.Percent));
            }

            if (progressTimeLabel != null)
            {
                progressTimeLabel.text = FormatMillis(position) + " / " + FormatMillis(duration);
            }

            if (progressSongLabel != null)
            {
                progressSongLabel.text = GetCurrentSongTitle();
            }

            progressContainer.style.bottom = new StyleLength(new Length(actionOverlayIsVisible ? 145 : 18, LengthUnit.Pixel));
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private string GetCurrentSongTitle()
    {
        try
        {
            SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
            SongMeta currentSongMeta = currentSingSceneData.SongMetas.FirstOrDefault();
            return currentSongMeta != null ? currentSongMeta.GetArtistDashTitle() : "Unknown song";
        }
        catch
        {
            return "Unknown song";
        }
    }

    private string FormatMillis(int millis)
    {
        int totalSeconds = Math.Max(0, millis / 1000);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return minutes.ToString("00") + ":" + seconds.ToString("00");
    }

    private void UpdateUiElementsFadeOut()
    {
        if (singingModeStarted || lastMicInputTimeInMillis <= 0)
        {
            return;
        }

        if (TimeUtils.IsDurationAboveThresholdInMillis(lastMicInputTimeInMillis, 10000))
        {
            FadeOutSingingUiElements();
        }
        else
        {
            FadeInSingingUiElements();
        }
    }

    private void UpdateFinishingScene()
    {
        if (isFinishing || singingModeStarted || !modSettings.AutoContinue)
        {
            return;
        }

        int timeBeforeEndInMillis = 1000;
        if (songAudioPlayer.IsFullyLoaded
            && songAudioPlayer.PositionInMillis >= songAudioPlayer.DurationInMillis - timeBeforeEndInMillis)
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - End of song detected. Starting next song soon.");
            isFinishing = true;
            float timeBeforeEndInSeconds = timeBeforeEndInMillis / 1000f;
            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(timeBeforeEndInSeconds, StartNextSong);
        }
    }

    private void FadeInSingingUiElements()
    {
        if (!isFadedOut)
        {
            return;
        }

        isFadedOut = false;
        Debug.Log($"{nameof(BetterJukeboxControl)} - Fade in singing UI elements");
        singingUiElements.ForEach(element => AnimationUtils.FadeInVisualElement(gameObject, element, 1f));
    }

    private void FadeOutSingingUiElements()
    {
        if (isFadedOut)
        {
            return;
        }

        isFadedOut = true;
        Debug.Log($"{nameof(BetterJukeboxControl)} - Fade out singing UI elements");
        singingUiElements.ForEach(element => AnimationUtils.FadeOutVisualElement(gameObject, element, 1f));
    }

    public void StartPreviousSong()
    {
        try
        {
            SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
            SongMeta currentSongMeta = currentSingSceneData.SongMetas.FirstOrDefault();

            SongMeta previousSongMeta = betterJukeboxHistory
                .Where(songMeta => songMeta != null && songMeta != currentSongMeta)
                .FirstOrDefault();

            if (previousSongMeta == null)
            {
                NotificationManager.CreateNotification(Translation.Of("No previous song in history"));
                return;
            }

            Debug.Log($"{nameof(BetterJukeboxControl)} - Starting previous song '{previousSongMeta.GetArtistDashTitle()}'");
            LoadSong(previousSongMeta);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    public void TogglePlayPause()
    {
        try
        {
            // This was the working strategy from the earlier version: try Melody Mania's native toggle first.
            if (InvokeSongAudioPlayerMethod("TogglePlayPause")
                || InvokeSingSceneControlMethod("TogglePlayPause"))
            {
                wasPausedByBetterJukebox = !wasPausedByBetterJukebox;
                return;
            }

            if (IsSongAudioPaused())
            {
                if (!InvokeSongAudioPlayerMethod("ResumeFromPause")
                    && !InvokeSongAudioPlayerMethod("ResumePlayback")
                    && !InvokeSongAudioPlayerMethod("Play")
                    && !InvokeSongAudioPlayerMethod("Unpause"))
                {
                    SetSongAudioPaused(false);
                }
                wasPausedByBetterJukebox = false;
            }
            else
            {
                if (!InvokeSongAudioPlayerMethod("PausePlayback")
                    && !InvokeSongAudioPlayerMethod("Pause")
                    && !InvokeSongAudioPlayerMethod("PauseAudio")
                    && !SetSongAudioPaused(true))
                {
                    NotificationManager.CreateNotification(Translation.Of("Pause is not available from this build yet"));
                }
                wasPausedByBetterJukebox = true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void SuppressBuiltInMousePause()
    {
        try
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            Vector2 mousePosition = Mouse.current.position.ReadValue();
            if (IsPointerInsideBetterJukeboxOverlay(mousePosition))
            {
                return;
            }

            lastLeftClickBlockTime = Time.unscaledTimeAsDouble;
            AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
            {
                try
                {
                    if (!wasPausedByBetterJukebox && IsSongAudioPaused())
                    {
                        ForceResumePlayback();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void ResumeIfEscapePausedSong()
    {
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
        {
            try
            {
                if (!wasPausedByBetterJukebox && IsSongAudioPaused())
                {
                    ForceResumePlayback();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        });
    }

    private void ForceResumePlayback()
    {
        if (!InvokeSongAudioPlayerMethod("ResumeFromPause")
            && !InvokeSongAudioPlayerMethod("ResumePlayback")
            && !InvokeSongAudioPlayerMethod("Play")
            && !InvokeSongAudioPlayerMethod("Unpause"))
        {
            SetSongAudioPaused(false);
        }
    }

    private bool IsSongAudioPaused()
    {
        try
        {
            object value = GetSongAudioPlayerProperty("IsPaused");
            if (value is bool)
            {
                return (bool)value;
            }

            value = GetSongAudioPlayerProperty("Paused");
            if (value is bool)
            {
                return (bool)value;
            }
        }
        catch
        {
        }

        return false;
    }

    private object GetSongAudioPlayerProperty(string propertyName)
    {
        if (songAudioPlayer == null)
        {
            return null;
        }

        System.Reflection.PropertyInfo propertyInfo = songAudioPlayer.GetType().GetProperty(propertyName);
        return propertyInfo?.GetValue(songAudioPlayer, null);
    }

    private bool SetSongAudioPaused(bool paused)
    {
        try
        {
            if (songAudioPlayer == null)
            {
                return false;
            }

            System.Reflection.MethodInfo methodInfo = songAudioPlayer.GetType().GetMethod("SetPause")
                ?? songAudioPlayer.GetType().GetMethod("SetPaused")
                ?? songAudioPlayer.GetType().GetMethod("RequestPause");

            if (methodInfo == null)
            {
                return false;
            }

            methodInfo.Invoke(songAudioPlayer, new object[] { paused });
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return false;
        }
    }

    private bool InvokeSingSceneControlMethod(string methodName)
    {
        try
        {
            if (singSceneControl == null)
            {
                return false;
            }

            System.Reflection.MethodInfo methodInfo = singSceneControl.GetType().GetMethod(methodName, new Type[0]);
            if (methodInfo == null)
            {
                return false;
            }

            methodInfo.Invoke(singSceneControl, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool InvokeSongAudioPlayerMethod(string methodName)
    {
        try
        {
            if (songAudioPlayer == null)
            {
                return false;
            }

            System.Reflection.MethodInfo methodInfo = songAudioPlayer.GetType().GetMethod(methodName, new Type[0]);
            if (methodInfo == null)
            {
                return false;
            }

            methodInfo.Invoke(songAudioPlayer, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartNextSong()
    {
        SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
        SongMeta currentSongMeta = currentSingSceneData.SongMetas.FirstOrDefault();
        seenSongMetas.Add(currentSongMeta);

        SongMeta nextSongMeta = GetNextSongMeta(currentSongMeta);
        if (nextSongMeta == null)
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - No next song found");
            return;
        }

        Debug.Log($"{nameof(BetterJukeboxControl)} - Starting next song '{nextSongMeta.GetArtistDashTitle()}'");
        LoadSong(nextSongMeta);
    }

    private void LoadSong(SongMeta songMeta)
    {
        AddSongToHistory(songMeta);

        SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();

        SingSceneData nextSingSceneData = new SingSceneData();
        nextSingSceneData.SingScenePlayerData = currentSingSceneData.SingScenePlayerData;
        nextSingSceneData.gameRoundSettings = currentSingSceneData.gameRoundSettings;
        nextSingSceneData.SongMetas = new List<SongMeta>() { songMeta };

        sceneNavigator.LoadScene(EScene.SingScene, nextSingSceneData);
    }

    private SongMeta GetNextSongMeta(SongMeta currentSongMeta)
    {
        SongMeta nextBetterJukeboxQueueSongMeta = GetNextBetterJukeboxQueueSongMeta();
        if (nextBetterJukeboxQueueSongMeta != null)
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - Choosing next song from BetterJukebox queue");
            return nextBetterJukeboxQueueSongMeta;
        }

        SongMeta nextSongQueueSongMeta = GetNextSongQueueSongMeta();
        if (nextSongQueueSongMeta != null)
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - Choosing next song from song queue");
            return nextSongQueueSongMeta;
        }

        List<SongMeta> allSelectableSongMetas = GetAllSelectableSongMetas();
        if (allSelectableSongMetas.IsNullOrEmpty())
        {
            return null;
        }

        if (modSettings.RandomSelection)
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - Choosing next song randomly out of {allSelectableSongMetas.Count} selectable songs.");
            return GetNextRandomSongMeta(allSelectableSongMetas);
        }
        else
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - Choosing next song sequentially out of {allSelectableSongMetas.Count} selectable songs.");
            return GetNextSequentialSongMeta(allSelectableSongMetas, currentSongMeta);
        }
    }

    private List<SongMeta> GetAllSelectableSongMetas()
    {
        bool usePlaylist = !nonPersistentSettings.PlaylistName.Value.IsNullOrEmpty() && nonPersistentSettings.PlaylistName.Value != UltraStarAllSongsPlaylist.Instance.Name;
        Debug.Log($"{nameof(BetterJukeboxControl)} - Choosing next song from " + (usePlaylist ? nonPersistentSettings.PlaylistName.Value : "all songs"));

        List<SongMeta> songMetas = usePlaylist
            ? playlistManager.GetSongMetas(playlistManager.GetPlaylistByName(nonPersistentSettings.PlaylistName.Value)).ToList()
            : songMetaManager.GetSongMetas().ToList();

        return songMetas
            .OrderBy(songMeta => string.IsNullOrEmpty(songMeta.Artist))
            .ThenBy(songMeta => songMeta.Artist ?? "")
            .ThenBy(songMeta => string.IsNullOrEmpty(songMeta.Title))
            .ThenBy(songMeta => songMeta.Title ?? "")
            .ToList();
    }

    private SongMeta GetNextBetterJukeboxQueueSongMeta()
    {
        if (betterJukeboxQueue.Count == 0)
        {
            return null;
        }

        SongMeta songMeta = betterJukeboxQueue[0];
        betterJukeboxQueue.RemoveAt(0);
        return songMeta;
    }

    private SongMeta GetNextSongQueueSongMeta()
    {
        SingSceneData singSceneData = songQueueManager.CreateNextSingSceneData(singSceneControl.PartyModeSceneData);
        return singSceneData?.SongMetas?.FirstOrDefault();
    }

    private SongMeta GetNextRandomSongMeta(List<SongMeta> allSelectableSongMetas)
    {
        List<SongMeta> unseenSongMetas = allSelectableSongMetas
            .Except(seenSongMetas)
            .ToList();
        Debug.Log($"{nameof(BetterJukeboxControl)} - Choosing next song from {unseenSongMetas.Count} unseen songs");

        if (unseenSongMetas.IsNullOrEmpty())
        {
            seenSongMetas.Clear();
            unseenSongMetas = allSelectableSongMetas;
        }
        return RandomUtils.RandomOf(unseenSongMetas);
    }

    private SongMeta GetNextSequentialSongMeta(List<SongMeta> allSelectableSongMetas, SongMeta currentSongMeta)
    {
        SongMeta songMeta = currentSongMeta != null
            ? allSelectableSongMetas.GetElementAfter(currentSongMeta, true)
            : allSelectableSongMetas.FirstOrDefault();

        Debug.Log($"{nameof(BetterJukeboxControl)} - Next sequential song: '{songMeta?.GetArtistDashTitle()}'");
        return songMeta;
    }

    private void DisableSingSceneFinisher()
    {
        try
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - Disable SingSceneFinisher");
            singSceneFinisher = FindFirstObjectByType<SingSceneFinisher>();
            if (singSceneFinisher != null)
            {
                singSceneFinisher.gameObject.SetActive(false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            Debug.LogError($"{nameof(BetterJukeboxControl)} - Failed to disable SingSceneFinisher");
        }
    }

    private void EnableSingSceneFinisher()
    {
        try
        {
            Debug.Log($"{nameof(BetterJukeboxControl)} - Enable SingSceneFinisher");
            if (singSceneFinisher == null)
            {
                singSceneFinisher = FindFirstObjectByType<SingSceneFinisher>(FindObjectsInactive.Include);
            }
            if (singSceneFinisher != null)
            {
                singSceneFinisher.gameObject.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            Debug.LogError($"{nameof(BetterJukeboxControl)} - Failed to enable SingSceneFinisher");
        }
    }
}
