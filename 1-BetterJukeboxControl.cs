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
    private readonly static Dictionary<string, Texture2D> betterJukeboxAlbumArtCache = new Dictionary<string, Texture2D>();
    private readonly static HashSet<string> betterJukeboxMissingAlbumArtCache = new HashSet<string>();
    private readonly static HashSet<string> betterJukeboxFavoriteSongIds = new HashSet<string>();
    private readonly static List<string> betterJukeboxFavoriteSongIdOrder = new List<string>();
    private static bool betterJukeboxFavoritesLoaded;
    private readonly static List<BetterJukeboxPlaylist> betterJukeboxPlaylists = new List<BetterJukeboxPlaylist>();
    private static bool betterJukeboxPlaylistsLoaded;
    private static bool showOverlayAfterBetterJukeboxSongChange;

    private bool popupVolumeGuardActive;
    private int popupLockedVolumePercent = -1;
    private float popupVolumeGuardUntil;

    private const float FadeTimeInSeconds = 1f;

    private const string CompanionAppInfoUrl = "https://melodymania.org";
    private const string CompanionAppGooglePlayUrl = "https://play.google.com/store/apps/details?id=com.melodymania.MelodyManiaCompanion";
    private const string CompanionAppStoreUrl = "https://apps.apple.com/us/app/melody-mania-companion/id6476068878";
    private static readonly string[] CompanionAppQrRows = new string[]
    {
        "000000000000000000000000000",
        "011111110010111110011111110",
        "010000010101001010010000010",
        "010111010010010010010111010",
        "010111010010000100010111010",
        "010111010111010110010111010",
        "010000010001011011010000010",
        "011111110101010101011111110",
        "000000000001111110000000000",
        "010101010000001101000100100",
        "001100100100110001110000010",
        "000011011011101000111101110",
        "010111001010111011000100100",
        "011101011100011011110010110",
        "001111000111110001110010010",
        "010101110011111000101001110",
        "001010101010011110100100100",
        "010001110100101011111110000",
        "000000000110101101000110110",
        "011111110011001111010110110",
        "010000010010101001000110010",
        "010111010101011001111110110",
        "010111010011111010001111000",
        "010111010100111011000100010",
        "010000010000011111010110100",
        "011111110101110111101000110",
        "000000000000000000000000000"
    };

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

    [Inject(Key = nameof(nextGameRoundInfoPlayerEntryUi))]
    private VisualTreeAsset nextGameRoundInfoPlayerEntryUi;

    [Inject]
    private SongQueueManager songQueueManager;

    [Inject]
    private BetterJukeboxModSettings modSettings;

    [Inject]
    private Settings settings;

    [Inject]
    private VolumeManager volumeManager;

    private readonly List<VisualElement> singingUiElements = new List<VisualElement>();

    private bool isFinishing;
    private bool isFadedOut;
    private bool isInjectionFinished;
    private bool singingModeStarted;
    private long lastMicInputTimeInMillis;

    private Label modInfoLabel;
    private Label nowPlayingLabel;
    private VisualElement nowPlayingCard;
    private bool nowPlayingWasHidden;
    private VisualElement progressContainer;
    private VisualElement progressFill;
    private Label progressTimeLabel;
    private Label progressSongLabel;
    private VisualElement mouseClickBlocker;

    private VisualElement actionOverlay;
    private VisualElement brandLogo;
    private Label brandLogoIconLabel;
    private Label brandLogoNameLabel;
    private Label brandLogoAccentLabel;
    private VisualElement searchPanel;
    private VisualElement searchResultsContainer;
    private TextField searchTextField;
    private VisualElement searchInputFrame;
    private Label searchInputIcon;
    private Button searchFavoritesFilterButton;
    private Button searchPlaylistsFilterButton;
    private Button searchSelectModeButton;
    private VisualElement searchFavoritesActionRow;
    private VisualElement searchPlaylistsActionRow;
    private VisualElement multiSelectActionRow;
    private VisualElement playlistDialogPanel;
    private SongMeta playlistDialogSongMeta;
    private readonly Dictionary<string, SongMeta> multiSelectedSongsById = new Dictionary<string, SongMeta>();
    private readonly List<string> multiSelectedSongIdOrder = new List<string>();
    private bool multiSelectMode;
    private SongMeta pendingMultiSelectLongPressSong;
    private VisualElement pendingMultiSelectLongPressRow;
    private float pendingMultiSelectLongPressStartedAt;
    private bool pendingMultiSelectLongPressTriggered;
    private const float MultiSelectLongPressSeconds = 0.50f;

    private int searchRenderGeneration;
    private List<SongMeta> pendingSearchRenderMatches = new List<SongMeta>();
    private VisualElement pendingSearchRenderSection;
    private int pendingSearchRenderIndex;
    private const int SearchRenderBatchSize = 50;

    private TextField playlistNameTextField;
    private int playlistNameCaretIndex;
    private Action<string> playlistNameDialogSubmitAction;
    private string selectedPlaylistName;
    private VisualElement favoriteRemoveConfirmPanel;
    private VisualElement playlistDeleteConfirmPanel;
    private int favoriteSortMode;
    private bool searchInputHasFocus;
    private bool searchInputHasHover;
    private VisualElement queuePanel;
    private VisualElement queueResultsContainer;
    private VisualElement queueActionsContainer;
    private VisualElement companionPanel;
    private Label companionStatusLabel;
    private Label companionVersionLabel;
    private VisualElement companionDeviceListContainer;
    private Label companionDeviceCountLabel;
    private VisualElement settingsCompanionStatusIcon;
    private Label settingsCompanionStatusLabel;
    private Label settingsCompanionDeviceCountLabel;
    private float lastCompanionHubRefreshTimeInSeconds;
    private string lastCompanionHubSignature;
    private VisualElement historyPanel;
    private VisualElement historyResultsContainer;
    private VisualElement settingsPanel;
    private VisualElement settingsResultsContainer;
    private bool actionOverlayIsVisible;
    private bool searchPanelIsVisible;
    private bool queuePanelIsVisible;
    private bool companionPanelIsVisible;
    private bool historyPanelIsVisible;
    private bool settingsPanelIsVisible;
    private bool forceButtonThemeRefreshOnNextSettingsUpdate;
    private Button queueOverlayButton;
    private int lastQueueBadgeCount = -1;
    private int lastRenderedQueueCount = -1;
    private bool queueChangeAnimationPending;
    private float lastQueueBadgeUpdateTimeInSeconds;
    private float lastOverlayActivityTimeInSeconds;
    private Vector2 lastMousePosition;
    private bool hasLastMousePosition;
    private float mouseMovementStartedAt = -1f;
    private float mouseMovementAccumulatedDistance;
    private bool overlayDisabledBySingingMode;
    private bool manualSearchInputHandling;
    private bool showOnlyFavoriteSearchResults;
    private bool showOnlyPlaylistSearchResults;
    private bool showOnlyHistorySearchResults;
    private bool wasPausedByBetterJukebox;
    private double lastLeftClickBlockTime;
    private float lastSearchFocusTime;
    private int searchCaretIndex;
    private Action<InputEventPtr, InputDevice> searchKeyboardBlocker;
    private readonly List<string> pendingSearchTextInput = new List<string>();
    private bool pendingSearchEscape;
    private bool pendingSettingsEscape;
    private bool pendingOverlayEscape;
    private float lastKeyboardDebugHeartbeatTimeInSeconds;
    private int draggingQueueIndex = -1;
    private int currentQueueDropIndex = -1;
    private VisualElement queueDragGhost;
    private Button queueHoldMoveButton;
    private VisualElement queueHoldMoveRow;
    private int queueHoldMoveIndex = -1;
    private int queueHoldMoveDirection;
    private float queueHoldMoveStartedAt = -1f;
    private bool queueHoldMoveSuppressNextClick;
    private const float QueueHoldMoveGraceSeconds = 0.18f;
    private const float QueueHoldMoveReadySeconds = 0.65f;

    private Button playlistHoldMoveButton;
    private VisualElement playlistHoldMoveRow;
    private BetterJukeboxPlaylist playlistHoldMovePlaylist;
    private SongMeta playlistHoldMoveSongMeta;
    private int playlistHoldMoveDirection;
    private float playlistHoldMoveStartedAt = -1f;
    private bool playlistHoldMoveSuppressNextClick;
    private int draggingPlaylistIndex = -1;
    private int currentPlaylistDropIndex = -1;
    private VisualElement playlistDragGhost;
    private BetterJukeboxPlaylist draggingPlaylist;
    private readonly List<VisualElement> playlistRowElements = new List<VisualElement>();
    private readonly List<SongMeta> playlistRowSongMetas = new List<SongMeta>();
    private readonly List<VisualElement> queueRowElements = new List<VisualElement>();
    private readonly static Dictionary<object, string> queueEntryDisplayNameOverrides = new Dictionary<object, string>();
    private readonly static Dictionary<object, SongMeta> queueEntrySongMetaOverrides = new Dictionary<object, SongMeta>();

    private SingSceneFinisher singSceneFinisher;

    public void OnInjectionFinished()
    {

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
        LoadFavoriteSongIds();
        LoadPlaylists();
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
        RestoreActionOverlayAfterSongChangeIfRequested();
        ShowActionOverlayOnSceneStart();
    }

    private void ShowActionOverlayOnSceneStart()
    {
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(3, () =>
        {
            try
            {
                if (actionOverlay == null || overlayDisabledBySingingMode || !modSettings.EnableBetterJukebox)
                {
                    return;
                }

                // Show the menu when entering SingScene, then let the normal auto-hide timer fade it out.
                ShowActionOverlay();
                lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        });
    }

    private void RestoreActionOverlayAfterSongChangeIfRequested()
    {
        if (!showOverlayAfterBetterJukeboxSongChange)
        {
            return;
        }

        showOverlayAfterBetterJukeboxSongChange = false;
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(2, () =>
        {
            try
            {
                if (actionOverlay == null || overlayDisabledBySingingMode || !modSettings.EnableBetterJukebox)
                {
                    return;
                }

                ShowActionOverlay();
                lastOverlayActivityTimeInSeconds = Time.unscaledTime;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        });
    }

    private void RequestKeepActionOverlayAfterSongChange()
    {
        showOverlayAfterBetterJukeboxSongChange = true;
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;
    }

    private void CreateLabels()
    {
        VisualElement songInfoContainer = uiDocument.rootVisualElement.Q("governanceOverlay")?.Q("songInfoContainer");
        if (songInfoContainer != null && modSettings.ShowNowPlaying)
        {
            HideBuiltInSongInfoContainer(songInfoContainer);
        }

        modInfoLabel = null;
        nowPlayingLabel = null;

        if (!modSettings.ShowNowPlaying)
        {
            return;
        }

        SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
        SongMeta currentSongMeta = currentSingSceneData.SongMetas.FirstOrDefault();

        CreateNowPlayingCard(currentSongMeta, currentSingSceneData);
    }

    private void HideBuiltInSongInfoContainer(VisualElement songInfoContainer)
    {
        if (songInfoContainer == null)
        {
            return;
        }

        songInfoContainer.style.display = DisplayStyle.None;
        songInfoContainer.visible = false;
    }

    private void FadeOutNowPlayingCardAfterDelay()
    {
        if (nowPlayingCard == null || nowPlayingWasHidden)
        {
            return;
        }

        int delaySeconds = Math.Max(2, modSettings.NowPlayingSeconds);
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delaySeconds, () =>
        {
            HideNowPlayingCard(true);
        });
    }

    private void HideNowPlayingCard(bool fade)
    {
        if (nowPlayingCard == null || nowPlayingWasHidden)
        {
            return;
        }

        nowPlayingWasHidden = true;
        if (fade && modSettings.FadeAnimations)
        {
            AnimationUtils.FadeOutVisualElement(gameObject, nowPlayingCard, 0.75f);
            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.75f, () =>
            {
                if (nowPlayingCard != null)
                {
                    nowPlayingCard.style.display = DisplayStyle.None;
                }
            });
        }
        else
        {
            nowPlayingCard.style.display = DisplayStyle.None;
        }
    }

    private void CreateNowPlayingCard(SongMeta songMeta, object singSceneData)
    {
        if (uiDocument?.rootVisualElement == null || songMeta == null)
        {
            return;
        }

        nowPlayingCard = new VisualElement();
        nowPlayingCard.name = "betterJukeboxNowPlayingCard";
        nowPlayingCard.style.position = Position.Absolute;
        nowPlayingCard.style.left = new StyleLength(new Length(24, LengthUnit.Percent));
        nowPlayingCard.style.right = new StyleLength(new Length(24, LengthUnit.Percent));
        nowPlayingCard.style.top = new StyleLength(new Length(18, LengthUnit.Percent));
        nowPlayingCard.style.flexDirection = FlexDirection.Column;
        nowPlayingCard.style.paddingLeft = 0f;
        nowPlayingCard.style.paddingRight = 0f;
        nowPlayingCard.style.paddingTop = 0f;
        nowPlayingCard.style.paddingBottom = 0f;
        nowPlayingCard.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        nowPlayingCard.style.borderLeftWidth = 0f;
        nowPlayingCard.style.borderRightWidth = 0f;
        nowPlayingCard.style.borderTopWidth = 0f;
        nowPlayingCard.style.borderBottomWidth = 0f;
        nowPlayingCard.pickingMode = PickingMode.Ignore;

        VisualElement topRow = new VisualElement();
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Center;

        topRow.Add(CreateAlbumArtElement(songMeta, 150f, 12f, 24f));

        VisualElement infoColumn = new VisualElement();
        infoColumn.style.flexDirection = FlexDirection.Column;
        infoColumn.style.flexGrow = 1f;

        Label headerLabel = new Label("♪ NOW PLAYING");
        headerLabel.AddToClassList("tinyFont");
        headerLabel.AddToClassList("textShadow");
        headerLabel.style.color = new Color(0.84f, 0.36f, 1f, 1f);
        headerLabel.style.marginBottom = 8f;
        infoColumn.Add(headerLabel);

        Label titleLabel = new Label(!string.IsNullOrWhiteSpace(songMeta.Title) ? songMeta.Title : "Unknown title");
        titleLabel.AddToClassList("textShadow");
        titleLabel.style.color = Color.white;
        titleLabel.style.fontSize = 38f;
        titleLabel.style.marginBottom = 4f;
        infoColumn.Add(titleLabel);

        Label artistLabel = new Label(!string.IsNullOrWhiteSpace(songMeta.Artist) ? songMeta.Artist : "Unknown artist");
        artistLabel.AddToClassList("textShadow");
        artistLabel.style.color = new Color(1f, 1f, 1f, 0.92f);
        artistLabel.style.fontSize = 26f;
        artistLabel.style.marginBottom = 18f;
        infoColumn.Add(artistLabel);

        VisualElement playerMicElement = CreateSongQueueEntryPlayerMicElement(singSceneData);
        if (playerMicElement != null)
        {
            playerMicElement.style.marginTop = 6f;
            infoColumn.Add(playerMicElement);
        }

        topRow.Add(infoColumn);
        nowPlayingCard.Add(topRow);

        VisualElement separator = new VisualElement();
        separator.style.height = 1f;
        separator.style.marginTop = 14f;
        separator.style.marginBottom = 10f;
        separator.style.backgroundColor = new Color(1f, 1f, 1f, 0.10f);
        nowPlayingCard.Add(separator);

        AddNowPlayingQueuePreview(nowPlayingCard);

        uiDocument.rootVisualElement.Add(nowPlayingCard);
        FadeOutNowPlayingCardAfterDelay();
    }

    private void AddNowPlayingQueuePreview(VisualElement parent)
    {
        if (parent == null)
        {
            return;
        }

        List<object> queueEntries = GetRealSongQueueEntries();

        VisualElement bottomRow = new VisualElement();
        bottomRow.style.flexDirection = FlexDirection.Row;
        bottomRow.style.alignItems = Align.Center;

        VisualElement listColumn = new VisualElement();
        listColumn.style.flexDirection = FlexDirection.Column;
        listColumn.style.flexGrow = 1f;

        Label nextHeader = new Label("Next in Queue");
        nextHeader.AddToClassList("tinyFont");
        nextHeader.AddToClassList("textShadow");
        nextHeader.style.color = new Color(0.84f, 0.36f, 1f, 1f);
        nextHeader.style.marginBottom = 6f;
        listColumn.Add(nextHeader);

        if (queueEntries.Count == 0)
        {
            Label emptyLabel = new Label("Queue is empty");
            emptyLabel.AddToClassList("tinyFont");
            emptyLabel.AddToClassList("textShadow");
            emptyLabel.style.color = new Color(1f, 1f, 1f, 0.70f);
            listColumn.Add(emptyLabel);
        }
        else
        {
            int limit = Math.Min(3, queueEntries.Count);
            for (int i = 0; i < limit; i++)
            {
                object queueEntry = queueEntries[i];

                VisualElement itemContainer = new VisualElement();
                itemContainer.style.flexDirection = FlexDirection.Column;
                itemContainer.style.marginTop = 2f;
                itemContainer.style.marginBottom = 4f;

                Label itemLabel = new Label((i + 1) + ".  " + GetSongQueueEntryDisplayName(queueEntry));
                itemLabel.AddToClassList("tinyFont");
                itemLabel.AddToClassList("textShadow");
                itemLabel.style.color = new Color(1f, 1f, 1f, 0.92f);
                itemLabel.style.marginTop = 0f;
                itemLabel.style.marginBottom = 0f;
                itemContainer.Add(itemLabel);

                if (modSettings.ShowNowPlayingQueuePlayerMics)
                {
                    VisualElement playerMicElement = CreateSongQueueEntryPlayerMicElement(queueEntry);
                    if (playerMicElement != null)
                    {
                        playerMicElement.style.marginTop = 1f;
                        itemContainer.Add(playerMicElement);
                    }
                }

                listColumn.Add(itemContainer);
            }
        }

        bottomRow.Add(listColumn);

        VisualElement countBox = new VisualElement();
        countBox.style.width = 110f;
        countBox.style.height = 96f;
        countBox.style.marginLeft = 18f;
        countBox.style.alignItems = Align.Center;
        countBox.style.justifyContent = Justify.Center;
        countBox.style.backgroundColor = new Color(1f, 1f, 1f, 0f);
        countBox.style.borderTopLeftRadius = 12f;
        countBox.style.borderTopRightRadius = 12f;
        countBox.style.borderBottomLeftRadius = 12f;
        countBox.style.borderBottomRightRadius = 12f;

        Label countLabel = new Label(queueEntries.Count.ToString());
        countLabel.AddToClassList("textShadow");
        countLabel.style.color = new Color(0.84f, 0.36f, 1f, 1f);
        countLabel.style.fontSize = 42f;
        countBox.Add(countLabel);

        Label songsLabel = new Label(queueEntries.Count == 1 ? "SONG" : "SONGS");
        songsLabel.AddToClassList("tinyFont");
        songsLabel.AddToClassList("textShadow");
        songsLabel.style.color = new Color(1f, 1f, 1f, 0.78f);
        countBox.Add(songsLabel);

        Label nextLabel = new Label("NEXT");
        nextLabel.AddToClassList("tinyFont");
        nextLabel.AddToClassList("textShadow");
        nextLabel.style.color = new Color(1f, 1f, 1f, 0.90f);
        countBox.Add(nextLabel);

        bottomRow.Add(countBox);
        parent.Add(bottomRow);
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
        actionOverlay.style.bottom = new StyleLength(new Length(42, LengthUnit.Pixel));
        actionOverlay.style.flexDirection = FlexDirection.Column;
        actionOverlay.style.justifyContent = Justify.Center;
        actionOverlay.style.alignItems = Align.Center;
        actionOverlay.style.display = DisplayStyle.None;
        actionOverlay.focusable = true;

        CreateBrandLogo();

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
        Button queueButton = CreateOverlayButton("📋 Queue", ToggleQueuePanel);
        queueOverlayButton = queueButton;
        UpdateQueueBadge(true);
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

        // Popup panels must be added before the button row so they open above the menu.
        // The menu row is added last and remains at the bottom while panels appear above it.
        CreateSearchPanel();
        CreateQueuePanel();
        CreateCompanionPanel();
        CreateHistoryPanel();
        CreateSettingsPanel();
        actionOverlay.Add(buttonRow);

        uiDocument.rootVisualElement.Add(actionOverlay);
        Debug.Log("BetterJukebox v1.4.5.27 loaded - Native SongMeta Search Cleanup");
        LogNativeSongMetaSearchReadyOnce();
    }

    private static bool nativeSongMetaSearchReadyLogged;

    private void LogNativeSongMetaSearchReadyOnce()
    {
        if (nativeSongMetaSearchReadyLogged)
        {
            return;
        }
        nativeSongMetaSearchReadyLogged = true;

        try
        {
            int count = songMetaManager != null && songMetaManager.GetSongMetas() != null
                ? songMetaManager.GetSongMetas().Count()
                : 0;
            Debug.Log("BetterJukebox Native SongMeta search initialized (" + count + " songs)");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox Native SongMeta search initialization count failed: " + ex.Message);
        }
    }

    private void CreateBrandLogo()
    {
        if (uiDocument?.rootVisualElement == null)
        {
            return;
        }

        brandLogo = new VisualElement();
        brandLogo.name = "betterJukeboxBrandLogo";
        brandLogo.style.position = Position.Absolute;
        brandLogo.style.left = new StyleLength(new Length(26, LengthUnit.Pixel));
        brandLogo.style.top = new StyleLength(new Length(18, LengthUnit.Pixel));
        brandLogo.style.flexDirection = FlexDirection.Row;
        brandLogo.style.alignItems = Align.Center;
        brandLogo.style.display = DisplayStyle.None;
        brandLogo.pickingMode = PickingMode.Ignore;

        brandLogoIconLabel = new Label("♪");
        brandLogoIconLabel.AddToClassList("textShadow");
        brandLogoIconLabel.style.fontSize = 30f;
        brandLogoIconLabel.style.marginRight = 8f;
        brandLogo.Add(brandLogoIconLabel);

        brandLogoNameLabel = new Label("Better");
        brandLogoNameLabel.AddToClassList("textShadow");
        brandLogoNameLabel.style.fontSize = 22f;
        brandLogoNameLabel.style.color = Color.white;
        brandLogo.Add(brandLogoNameLabel);

        brandLogoAccentLabel = new Label("Jukebox");
        brandLogoAccentLabel.AddToClassList("textShadow");
        brandLogoAccentLabel.style.fontSize = 22f;
        brandLogoAccentLabel.style.marginLeft = 0f;
        brandLogo.Add(brandLogoAccentLabel);

        ApplyBrandLogoTheme();
        uiDocument.rootVisualElement.Add(brandLogo);
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
                    bool playlistNameDialogOpen = IsPlaylistNameDialogOpen();

                    if (playlistNameDialogOpen && device is Keyboard)
                    {
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

                            if (keyControl.keyCode == Key.Space
                                || keyControl.keyCode == Key.M
                                || keyControl.keyCode == Key.P
                                || keyControl.keyCode == Key.R
                                || keyControl.keyCode == Key.H
                                || keyControl.keyCode == Key.S
                                || keyControl.keyCode == Key.F
                                || keyControl.keyCode == Key.Q
                                || keyControl.keyCode == Key.Comma
                                || keyControl.keyCode == Key.Escape
                                || keyControl.keyCode == Key.Enter
                                || keyControl.keyCode == Key.NumpadEnter)
                            {
                                // Same idea as Search: keep Melody Mania global shortcuts from firing
                                // while a BetterJukebox text field is active. Do not manually insert text here;
                                // UI Toolkit TextField still owns the actual typing.
                                eventPtr.handled = true;
                                return;
                            }
                        }

                        return;
                    }

                    if (!searchPanelIsVisible && !queuePanelIsVisible && !historyPanelIsVisible && !settingsPanelIsVisible)
                    {
                        return;
                    }

                    UnityEngine.InputSystem.Mouse mouseDevice = device as UnityEngine.InputSystem.Mouse;
                    if (mouseDevice != null)
                    {
                        foreach (InputControl control in eventPtr.EnumerateChangedControls(device))
                        {
                            if (control == mouseDevice.scroll || control.name == "scroll")
                            {
                                Vector2 scrollValue = Vector2.zero;
                                try
                                {
                                    scrollValue = mouseDevice.scroll.ReadValueFromEvent(eventPtr);
                                }
                                catch
                                {
                                    scrollValue = mouseDevice.scroll.ReadValue();
                                }

                                if (Mathf.Abs(scrollValue.x) > 0.01f || Mathf.Abs(scrollValue.y) > 0.01f)
                                {
                                    // Prevent Melody Mania global mouse-wheel volume shortcuts while a BetterJukebox popup is open.
                                    lastOverlayActivityTimeInSeconds = Time.unscaledTime;
                                    eventPtr.handled = true;
                                    return;
                                }
                            }
                        }

                        return;
                    }

                    if (!(device is Keyboard))
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
                            pendingOverlayEscape = true;
                            pendingSettingsEscape = settingsPanelIsVisible;
                            pendingSearchEscape = searchPanelIsVisible;
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

    private void Removed_RegisterEscapeHandler_Obsolete_DoNotCall()
    {
        // Do not register a global UI Toolkit KeyDownEvent handler here.
        // Melody Mania can rebuild/destroy the UI panel during scene changes, and stale
        // UI Toolkit callbacks caused HideActionOverlay() null crashes.
        // Popup Escape and keyboard shortcuts are handled from Update() instead.
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
            pendingOverlayEscape = true;
            pendingSearchEscape = true;
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

        if (companionPanelIsVisible)
        {
            companionPanelIsVisible = false;
            if (companionPanel != null)
            {
                companionPanel.style.display = DisplayStyle.None;
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
            if (multiSelectMode)
            {
                ClearMultiSelectSelection(true);
                lastOverlayActivityTimeInSeconds = Time.unscaledTime;
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedPlaylistName))
            {
                selectedPlaylistName = null;
                showOnlyPlaylistSearchResults = true;
                showOnlyFavoriteSearchResults = false;
                showOnlyHistorySearchResults = false;
                UpdateFavoriteFilterButtonText();
                UpdatePlaylistFilterButtonText();
                UpdateFavoriteActionRowVisibility();
                UpdatePlaylistActionRowVisibility();
                UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
                if (searchTextField != null)
                {
                    searchTextField.Focus();
                }
                lastOverlayActivityTimeInSeconds = Time.unscaledTime;
                return;
            }

            if (showOnlyFavoriteSearchResults || showOnlyPlaylistSearchResults || showOnlyHistorySearchResults)
            {
                SelectAllSearchFilterMode();
                lastOverlayActivityTimeInSeconds = Time.unscaledTime;
                return;
            }

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

    private void SelectAllSearchFilterMode()
    {
        showOnlyFavoriteSearchResults = false;
        showOnlyPlaylistSearchResults = false;
        showOnlyHistorySearchResults = false;
        selectedPlaylistName = null;
        UpdateFavoriteFilterButtonText();
        UpdatePlaylistFilterButtonText();
        UpdateFavoriteActionRowVisibility();
        UpdatePlaylistActionRowVisibility();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        if (searchTextField != null)
        {
            searchTextField.Focus();
        }
    }

    private void CreateSearchPanel()
    {
        searchPanel = new VisualElement();
        searchPanel.name = "betterJukeboxSearchPanel";
        searchPanel.focusable = true;
        searchPanel.RegisterCallback<KeyDownEvent>(evt => HandleSearchTextInput(evt), TrickleDown.TrickleDown);
        searchPanel.RegisterCallback<KeyUpEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
        ApplyPopupPanelStyle(searchPanel);

        searchPanel.Add(CreatePopupHeader("Search songs", CloseSearchPanel));

        VisualElement searchFilterRow = CreatePanelRow();
        searchFilterRow.name = "betterJukeboxSearchFilterRow";
        searchFilterRow.style.marginTop = 2f;
        searchFilterRow.style.marginBottom = 6f;
        searchFilterRow.style.justifyContent = Justify.Center;
        searchFilterRow.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
        ApplyThemedBorder(searchFilterRow);
        searchFilterRow.Add(CreateSmallPanelButton("All", () => { CancelMultiSelectForNavigation(); SelectAllSearchFilterMode(); }));
        searchFavoritesFilterButton = CreateSmallPanelButton(GetFavoritesFilterButtonText(), () =>
        {
            CancelMultiSelectForNavigation();
            showOnlyFavoriteSearchResults = true;
            showOnlyPlaylistSearchResults = false;
            showOnlyHistorySearchResults = false;
            selectedPlaylistName = null;
            UpdateFavoriteFilterButtonText();
            UpdateFavoriteActionRowVisibility();
            UpdatePlaylistActionRowVisibility();
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        });
        searchFilterRow.Add(searchFavoritesFilterButton);
        searchPlaylistsFilterButton = CreateSmallPanelButton(GetPlaylistsFilterButtonText(), () =>
        {
            CancelMultiSelectForNavigation();
            showOnlyFavoriteSearchResults = false;
            showOnlyPlaylistSearchResults = true;
            showOnlyHistorySearchResults = false;
            selectedPlaylistName = null;
            UpdateFavoriteActionRowVisibility();
            UpdatePlaylistActionRowVisibility();
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        });
        searchFilterRow.Add(searchPlaylistsFilterButton);
        VisualElement searchFilterSpacer = new VisualElement();
        searchFilterSpacer.style.flexGrow = 1f;
        searchFilterRow.Add(searchFilterSpacer);
        searchSelectModeButton = CreateSmallPanelButton("Select", ToggleMultiSelectMode);
        searchFilterRow.Add(searchSelectModeButton);
        searchPanel.Add(searchFilterRow);

        searchFavoritesActionRow = new VisualElement();
        searchFavoritesActionRow.name = "betterJukeboxFavoritesActionRow";
        searchFavoritesActionRow.style.flexDirection = FlexDirection.Row;
        searchFavoritesActionRow.style.alignItems = Align.Center;
        searchFavoritesActionRow.style.marginTop = 0f;
        searchFavoritesActionRow.style.marginBottom = 6f;
        searchFavoritesActionRow.style.paddingLeft = 0f;
        searchFavoritesActionRow.style.paddingRight = 0f;
        searchFavoritesActionRow.style.paddingTop = 0f;
        searchFavoritesActionRow.style.paddingBottom = 0f;
        searchFavoritesActionRow.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        searchFavoritesActionRow.Add(CreateSmallPanelButton("Queue All", QueueFavoriteSongs));
        searchFavoritesActionRow.Add(CreateSmallPanelButton("Shuffle Favorites", ShuffleFavoriteSongs));
        searchFavoritesActionRow.Add(CreateSmallPanelButton("Sort A-Z", () =>
        {
            favoriteSortMode = 0;
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        }));
        searchFavoritesActionRow.Add(CreateSmallPanelButton("Sort Recently Added", () =>
        {
            favoriteSortMode = 1;
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        }));
        VisualElement favoriteActionSpacer = new VisualElement();
        favoriteActionSpacer.style.flexGrow = 1f;
        searchFavoritesActionRow.Add(favoriteActionSpacer);
        searchFavoritesActionRow.Add(CreateSmallPanelButton("Remove All Favorites", RemoveAllFavoriteSongs));
        searchPanel.Add(searchFavoritesActionRow);
        UpdateFavoriteActionRowVisibility();

        searchPlaylistsActionRow = new VisualElement();
        searchPlaylistsActionRow.name = "betterJukeboxPlaylistsActionRow";
        searchPlaylistsActionRow.style.flexDirection = FlexDirection.Row;
        searchPlaylistsActionRow.style.alignItems = Align.Center;
        searchPlaylistsActionRow.style.marginTop = 0f;
        searchPlaylistsActionRow.style.marginBottom = 6f;
        searchPlaylistsActionRow.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        searchPanel.Add(searchPlaylistsActionRow);
        UpdatePlaylistActionRowVisibility();

        multiSelectActionRow = new VisualElement();
        multiSelectActionRow.name = "betterJukeboxMultiSelectActionRow";
        multiSelectActionRow.style.flexDirection = FlexDirection.Row;
        multiSelectActionRow.style.alignItems = Align.Center;
        multiSelectActionRow.style.marginTop = 0f;
        multiSelectActionRow.style.marginBottom = 6f;
        multiSelectActionRow.style.paddingLeft = 0f;
        multiSelectActionRow.style.paddingRight = 0f;
        multiSelectActionRow.style.paddingTop = 0f;
        multiSelectActionRow.style.paddingBottom = 0f;
        multiSelectActionRow.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        searchPanel.Add(multiSelectActionRow);
        UpdateMultiSelectActionRowVisibility();

        ScrollView searchScrollView = new ScrollView(ScrollViewMode.Vertical);
        searchScrollView.name = "betterJukeboxSearchScrollView";
        searchScrollView.style.flexGrow = 1f;
        searchScrollView.style.marginTop = 8;
        searchScrollView.style.marginBottom = 8;
        searchScrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0.16f);
        searchScrollView.style.borderTopLeftRadius = 12;
        searchScrollView.style.borderTopRightRadius = 12;
        searchScrollView.style.borderBottomLeftRadius = 12;
        searchScrollView.style.borderBottomRightRadius = 12;

        searchResultsContainer = new VisualElement();
        searchResultsContainer.style.flexDirection = FlexDirection.Column;
        searchResultsContainer.style.paddingLeft = 6;
        searchResultsContainer.style.paddingRight = 6;
        searchResultsContainer.style.paddingTop = 6;
        searchResultsContainer.style.paddingBottom = 6;
        searchScrollView.Add(searchResultsContainer);
        searchPanel.Add(searchScrollView);

        searchInputFrame = new VisualElement();
        searchInputFrame.name = "betterJukeboxSearchInputFrame";
        searchInputFrame.style.flexDirection = FlexDirection.Row;
        searchInputFrame.style.alignItems = Align.Center;
        searchInputFrame.style.marginTop = new StyleLength(new Length(2, LengthUnit.Pixel));
        searchInputFrame.style.marginBottom = new StyleLength(new Length(0, LengthUnit.Pixel));
        searchInputFrame.style.paddingLeft = 14;
        searchInputFrame.style.paddingRight = 12;
        searchInputFrame.style.paddingTop = 8;
        searchInputFrame.style.paddingBottom = 8;
        searchInputFrame.style.borderTopLeftRadius = 14;
        searchInputFrame.style.borderTopRightRadius = 14;
        searchInputFrame.style.borderBottomLeftRadius = 14;
        searchInputFrame.style.borderBottomRightRadius = 14;
        searchInputFrame.style.borderTopWidth = 1f;
        searchInputFrame.style.borderBottomWidth = 1f;
        searchInputFrame.style.borderLeftWidth = 1f;
        searchInputFrame.style.borderRightWidth = 1f;
        searchInputFrame.RegisterCallback<MouseEnterEvent>(evt =>
        {
            searchInputHasHover = true;
            ApplySearchInputTheme();
        });
        searchInputFrame.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            searchInputHasHover = false;
            ApplySearchInputTheme();
        });
        searchInputFrame.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (searchTextField != null)
            {
                searchTextField.Focus();
            }
        });

        searchInputIcon = new Label("🔍");
        searchInputIcon.AddToClassList("smallFont");
        searchInputIcon.style.fontSize = 18;
        searchInputIcon.style.marginRight = 10;
        searchInputIcon.style.flexShrink = 0f;
        searchInputFrame.Add(searchInputIcon);

        searchTextField = new TextField();
        searchTextField.name = "betterJukeboxSearchTextField";
        searchTextField.style.flexGrow = 1f;
        searchTextField.style.marginTop = new StyleLength(new Length(0, LengthUnit.Pixel));
        searchTextField.style.marginBottom = new StyleLength(new Length(0, LengthUnit.Pixel));
        searchTextField.style.marginLeft = new StyleLength(new Length(0, LengthUnit.Pixel));
        searchTextField.style.marginRight = new StyleLength(new Length(0, LengthUnit.Pixel));
        searchTextField.RegisterValueChangedCallback(evt => UpdateSearchResults(evt.newValue));
        searchTextField.RegisterCallback<KeyDownEvent>(evt => HandleSearchTextInput(evt), TrickleDown.TrickleDown);
        searchTextField.RegisterCallback<KeyUpEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
        searchTextField.RegisterCallback<FocusInEvent>(evt =>
        {
            searchInputHasFocus = true;
            ApplySearchInputTheme();
        });
        searchTextField.RegisterCallback<FocusOutEvent>(evt =>
        {
            searchInputHasFocus = false;
            ApplySearchInputTheme();
            if (searchPanelIsVisible && !IsPlaylistNameDialogOpen())
            {
                AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
                {
                    if (searchPanelIsVisible && searchTextField != null && !IsPlaylistNameDialogOpen())
                    {
                        searchTextField.Focus();
                    }
                });
            }
        });
        searchInputFrame.Add(searchTextField);
        searchPanel.Add(searchInputFrame);
        ApplySearchInputTheme();
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, ApplySearchInputTheme);

        actionOverlay.Add(searchPanel);
    }

    private void ApplySearchInputTheme()
    {
        if (searchInputFrame == null)
        {
            return;
        }

        bool active = searchInputHasFocus || searchInputHasHover;
        Color borderColor = active ? GetAccentColor() : GetPanelSideBorderColor();
        Color backgroundColor = active ? GetSearchInputActiveBackgroundColor() : GetSearchInputBackgroundColor();

        searchInputFrame.style.backgroundColor = backgroundColor;
        searchInputFrame.style.borderTopColor = borderColor;
        searchInputFrame.style.borderBottomColor = borderColor;
        searchInputFrame.style.borderLeftColor = borderColor;
        searchInputFrame.style.borderRightColor = borderColor;

        if (searchInputIcon != null)
        {
            searchInputIcon.style.color = active ? GetAccentColor() : new Color(1f, 1f, 1f, 0.62f);
        }

        if (searchTextField != null)
        {
            searchTextField.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            searchTextField.style.color = new Color(1f, 1f, 1f, 0.96f);
            searchTextField.style.borderTopWidth = 0f;
            searchTextField.style.borderBottomWidth = 0f;
            searchTextField.style.borderLeftWidth = 0f;
            searchTextField.style.borderRightWidth = 0f;
            ApplySearchTextFieldInnerTheme(searchTextField);
        }
    }

    private Color GetSearchInputBackgroundColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 2)
        {
            return new Color(0.008f, 0.018f, 0.010f, 0.96f);
        }
        if (theme == 3)
        {
            return new Color(0.008f, 0.012f, 0.022f, 0.96f);
        }
        if (theme == 4)
        {
            return new Color(0.020f, 0.008f, 0.010f, 0.96f);
        }
        if (theme == 5)
        {
            return new Color(0.020f, 0.014f, 0.006f, 0.96f);
        }
        if (theme == 1)
        {
            return new Color(0.012f, 0.011f, 0.016f, 0.96f);
        }
        return new Color(0.012f, 0.014f, 0.018f, 0.96f);
    }

    private Color GetSearchInputActiveBackgroundColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 2)
        {
            return new Color(0.012f, 0.028f, 0.016f, 0.98f);
        }
        if (theme == 3)
        {
            return new Color(0.012f, 0.018f, 0.034f, 0.98f);
        }
        if (theme == 4)
        {
            return new Color(0.032f, 0.012f, 0.014f, 0.98f);
        }
        if (theme == 5)
        {
            return new Color(0.034f, 0.024f, 0.010f, 0.98f);
        }
        if (theme == 1)
        {
            return new Color(0.018f, 0.016f, 0.024f, 0.98f);
        }
        return new Color(0.018f, 0.021f, 0.027f, 0.98f);
    }

    private void ApplySearchTextFieldInnerTheme(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        element.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        element.style.color = Color.white;
        element.style.borderTopWidth = 0f;
        element.style.borderBottomWidth = 0f;
        element.style.borderLeftWidth = 0f;
        element.style.borderRightWidth = 0f;
        element.style.borderTopColor = new Color(0f, 0f, 0f, 0f);
        element.style.borderBottomColor = new Color(0f, 0f, 0f, 0f);
        element.style.borderLeftColor = new Color(0f, 0f, 0f, 0f);
        element.style.borderRightColor = new Color(0f, 0f, 0f, 0f);

        for (int index = 0; index < element.childCount; index++)
        {
            ApplySearchTextFieldInnerTheme(element[index]);
        }
    }

    private void CreateHistoryPanel()
    {
        historyPanel = new VisualElement();
        historyPanel.name = "betterJukeboxHistoryPanel";
        ApplyPopupPanelStyle(historyPanel);

        historyPanel.Add(CreatePopupHeader("History", CloseHistoryPanel));

        ScrollView historyScrollView = new ScrollView(ScrollViewMode.Vertical);
        historyScrollView.name = "betterJukeboxHistoryScrollView";
        historyScrollView.style.flexGrow = 1f;
        historyScrollView.style.marginTop = 8;
        historyScrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0.16f);
        historyScrollView.style.borderTopLeftRadius = 12;
        historyScrollView.style.borderTopRightRadius = 12;
        historyScrollView.style.borderBottomLeftRadius = 12;
        historyScrollView.style.borderBottomRightRadius = 12;

        historyResultsContainer = new VisualElement();
        historyResultsContainer.style.flexDirection = FlexDirection.Column;
        historyResultsContainer.style.paddingLeft = 6;
        historyResultsContainer.style.paddingRight = 6;
        historyResultsContainer.style.paddingTop = 6;
        historyResultsContainer.style.paddingBottom = 6;
        historyScrollView.Add(historyResultsContainer);
        historyPanel.Add(historyScrollView);

        actionOverlay.Add(historyPanel);
    }

    private void CreateQueuePanel()
    {
        queuePanel = new VisualElement();
        queuePanel.name = "betterJukeboxQueuePanel";
        ApplyPopupPanelStyle(queuePanel);

        queuePanel.Add(CreatePopupHeader("Queue", CloseQueuePanel));

        ScrollView queueScrollView = new ScrollView(ScrollViewMode.Vertical);
        queueScrollView.name = "betterJukeboxQueueScrollView";
        queueScrollView.style.flexGrow = 1f;
        queueScrollView.style.marginTop = 8;
        queueScrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0.16f);
        queueScrollView.style.borderTopLeftRadius = 12;
        queueScrollView.style.borderTopRightRadius = 12;
        queueScrollView.style.borderBottomLeftRadius = 12;
        queueScrollView.style.borderBottomRightRadius = 12;

        queueResultsContainer = new VisualElement();
        queueResultsContainer.style.flexDirection = FlexDirection.Column;
        queueResultsContainer.style.paddingLeft = 6;
        queueResultsContainer.style.paddingRight = 6;
        queueResultsContainer.style.paddingTop = 6;
        queueResultsContainer.style.paddingBottom = 6;
        queueScrollView.Add(queueResultsContainer);
        queuePanel.Add(queueScrollView);

        queueActionsContainer = CreatePanelRow();
        queueActionsContainer.style.marginTop = 8f;
        queueActionsContainer.style.marginBottom = 0f;
        queueActionsContainer.style.justifyContent = Justify.Center;
        queueActionsContainer.Add(CreateSmallPanelButton("Clear Queue", ClearRealQueue));
        queueActionsContainer.Add(CreateSmallPanelButton("Shuffle Queue", ShuffleRealQueue));
        queueActionsContainer.Add(CreateSmallPanelButton("Companion App", ToggleCompanionPanel));
        queuePanel.Add(queueActionsContainer);

        actionOverlay.Add(queuePanel);
    }

    private void CreateCompanionPanel()
    {
        companionPanel = new VisualElement();
        companionPanel.name = "betterJukeboxCompanionPanel";
        companionPanel.focusable = true;
        ApplyPopupPanelStyle(companionPanel);

        companionPanel.Add(CreatePopupHeader("Companion", CloseCompanionPanel));

        VisualElement content = new VisualElement();
        content.name = "betterJukeboxCompanionDashboard";
        content.style.flexDirection = FlexDirection.Column;
        content.style.flexGrow = 1f;
        content.style.marginTop = 8f;
        content.style.paddingTop = 12f;
        content.style.paddingBottom = 12f;
        content.style.paddingLeft = 12f;
        content.style.paddingRight = 12f;
        content.style.backgroundColor = new Color(0f, 0f, 0f, 0.16f);
        content.style.borderTopLeftRadius = 12f;
        content.style.borderTopRightRadius = 12f;
        content.style.borderBottomLeftRadius = 12f;
        content.style.borderBottomRightRadius = 12f;

        VisualElement intro = new VisualElement();
        intro.style.flexDirection = FlexDirection.Row;
        intro.style.alignItems = Align.Center;
        intro.style.marginBottom = 10f;

        Label introIcon = new Label("📱");
        introIcon.AddToClassList("smallFont");
        introIcon.style.color = GetAccentColor();
        introIcon.style.marginRight = 8f;
        intro.Add(introIcon);

        Label introText = CreatePanelLabel("Connect phones to browse songs, queue music and control playback.");
        introText.style.color = Color.white;
        introText.style.opacity = 0.82f;
        introText.style.marginRight = 0f;
        introText.style.whiteSpace = WhiteSpace.Normal;
        intro.Add(introText);
        content.Add(intro);

        VisualElement dashboardRow = new VisualElement();
        dashboardRow.style.flexDirection = FlexDirection.Row;
        dashboardRow.style.flexGrow = 1f;
        dashboardRow.style.minHeight = 210f;

        VisualElement qrCard = CreateCompanionHubCard();
        qrCard.style.width = 185f;
        qrCard.style.marginRight = 10f;
        qrCard.style.alignItems = Align.Center;
        qrCard.style.justifyContent = Justify.Center;

        Label qrTitle = CreatePanelLabel("Install App");
        qrTitle.style.color = Color.white;
        qrTitle.style.marginRight = 0f;
        qrTitle.style.marginBottom = 10f;
        qrCard.Add(qrTitle);

        VisualElement qrElement = CreateCompanionAppQrCodeElement();
        qrCard.Add(qrElement);

        Label scanText = CreatePanelLabel("Scan to install");
        scanText.style.marginRight = 0f;
        scanText.style.marginTop = 10f;
        scanText.style.opacity = 0.82f;
        qrCard.Add(scanText);
        dashboardRow.Add(qrCard);

        VisualElement statusCard = CreateCompanionHubCard();
        statusCard.style.width = 185f;
        statusCard.style.marginRight = 10f;

        Label statusTitle = CreatePanelLabel("Status");
        statusTitle.style.color = Color.white;
        statusTitle.style.marginBottom = 8f;
        statusCard.Add(statusTitle);

        companionStatusLabel = CreateCompanionStatusLabel(0);
        companionStatusLabel.style.marginBottom = 8f;
        statusCard.Add(companionStatusLabel);

        companionDeviceCountLabel = CreatePanelLabel("0 devices connected");
        companionDeviceCountLabel.style.opacity = 0.72f;
        companionDeviceCountLabel.style.whiteSpace = WhiteSpace.Normal;
        companionDeviceCountLabel.style.marginBottom = 8f;
        statusCard.Add(companionDeviceCountLabel);

        companionVersionLabel = CreatePanelLabel("Companion App version is managed by the app store.");
        companionVersionLabel.style.opacity = 0.58f;
        companionVersionLabel.style.whiteSpace = WhiteSpace.Normal;
        statusCard.Add(companionVersionLabel);
        dashboardRow.Add(statusCard);

        VisualElement devicesCard = CreateCompanionHubCard();
        devicesCard.style.flexGrow = 1f;

        Label devicesTitle = CreatePanelLabel("Connected Devices");
        devicesTitle.style.color = Color.white;
        devicesTitle.style.marginBottom = 8f;
        devicesCard.Add(devicesTitle);

        ScrollView devicesScrollView = new ScrollView(ScrollViewMode.Vertical);
        devicesScrollView.style.flexGrow = 1f;
        devicesScrollView.style.maxHeight = 160f;
        devicesScrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0f);

        companionDeviceListContainer = new VisualElement();
        companionDeviceListContainer.style.flexDirection = FlexDirection.Column;
        devicesScrollView.Add(companionDeviceListContainer);
        devicesCard.Add(devicesScrollView);
        dashboardRow.Add(devicesCard);

        content.Add(dashboardRow);

        VisualElement footerCard = CreateCompanionHubCard();
        footerCard.style.marginTop = 10f;
        footerCard.style.paddingTop = 12f;
        footerCard.style.paddingBottom = 12f;
        footerCard.style.flexDirection = FlexDirection.Row;
        footerCard.style.alignItems = Align.Center;

        VisualElement footerTextColumn = new VisualElement();
        footerTextColumn.style.flexDirection = FlexDirection.Column;
        footerTextColumn.style.flexGrow = 1f;
        footerTextColumn.style.flexShrink = 1f;
        footerTextColumn.style.marginRight = 12f;

        Label footerTitle = CreatePanelLabel("Install Companion App");
        footerTitle.style.color = Color.white;
        footerTitle.style.marginBottom = 4f;
        footerTextColumn.Add(footerTitle);

        Label footerText = CreatePanelLabel("Browse your music library, queue songs and control playback directly from your phone.");
        footerText.style.opacity = 0.75f;
        footerText.style.whiteSpace = WhiteSpace.Normal;
        footerTextColumn.Add(footerText);
        footerCard.Add(footerTextColumn);

        VisualElement buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.alignItems = Align.Center;
        buttonRow.style.justifyContent = Justify.FlexEnd;
        buttonRow.style.flexGrow = 0f;
        buttonRow.style.flexShrink = 0f;
        buttonRow.style.marginTop = 3f;
        buttonRow.style.marginBottom = 3f;
        buttonRow.Add(CreateSmallPanelButton("🌐 Official Page", () => Application.OpenURL(CompanionAppInfoUrl)));
        buttonRow.Add(CreateSmallPanelButton("▶ Google Play", () => Application.OpenURL(CompanionAppGooglePlayUrl)));
        buttonRow.Add(CreateSmallPanelButton(" App Store", () => Application.OpenURL(CompanionAppStoreUrl)));
        footerCard.Add(buttonRow);
        content.Add(footerCard);

        companionPanel.Add(content);
        actionOverlay.Add(companionPanel);
    }

    private VisualElement CreateCompanionHubCard()
    {
        VisualElement card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.paddingTop = 14f;
        card.style.paddingBottom = 14f;
        card.style.paddingLeft = 14f;
        card.style.paddingRight = 14f;
        card.style.backgroundColor = new Color(0f, 0f, 0f, 0.24f);
        card.style.borderTopLeftRadius = 14f;
        card.style.borderTopRightRadius = 14f;
        card.style.borderBottomLeftRadius = 14f;
        card.style.borderBottomRightRadius = 14f;
        card.style.borderTopWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderLeftWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderTopColor = GetPanelTopBorderColor();
        card.style.borderBottomColor = GetPanelSideBorderColor();
        card.style.borderLeftColor = GetPanelSideBorderColor();
        card.style.borderRightColor = GetPanelSideBorderColor();
        return card;
    }

    private VisualElement CreateCompanionAppQrCodeElement()
    {
        return CreateCompanionAppQrCodeElement(4f, 8f, 8f);
    }

    private VisualElement CreateSmallCompanionAppQrCodeElement()
    {
        return CreateCompanionAppQrCodeElement(2f, 5f, 6f);
    }

    private VisualElement CreateCompanionAppQrCodeElement(float cellSize, float padding, float radius)
    {
        VisualElement frame = new VisualElement();
        frame.style.flexDirection = FlexDirection.Column;
        frame.style.paddingLeft = padding;
        frame.style.paddingRight = padding;
        frame.style.paddingTop = padding;
        frame.style.paddingBottom = padding;
        frame.style.backgroundColor = Color.white;
        frame.style.borderTopLeftRadius = radius;
        frame.style.borderTopRightRadius = radius;
        frame.style.borderBottomLeftRadius = radius;
        frame.style.borderBottomRightRadius = radius;
        frame.pickingMode = PickingMode.Ignore;
        frame.style.flexGrow = 0f;
        frame.style.flexShrink = 0f;

        for (int rowIndex = 0; rowIndex < CompanionAppQrRows.Length; rowIndex++)
        {
            string rowText = CompanionAppQrRows[rowIndex];
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = cellSize;
            row.style.flexGrow = 0f;
            row.style.flexShrink = 0f;

            for (int columnIndex = 0; columnIndex < rowText.Length; columnIndex++)
            {
                VisualElement cell = new VisualElement();
                cell.style.width = cellSize;
                cell.style.height = cellSize;
                cell.style.flexGrow = 0f;
                cell.style.flexShrink = 0f;
                cell.style.backgroundColor = rowText[columnIndex] == '1' ? Color.black : Color.white;
                row.Add(cell);
            }

            frame.Add(row);
        }

        return frame;
    }

    private void ApplyPopupPanelStyle(VisualElement panel)
    {
        panel.style.display = DisplayStyle.None;
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.82f);
        panel.style.paddingLeft = 18;
        panel.style.paddingRight = 18;
        panel.style.paddingTop = 12;
        panel.style.paddingBottom = 12;
        panel.style.marginTop = 4;
        panel.style.borderTopLeftRadius = 18;
        panel.style.borderTopRightRadius = 18;
        panel.style.borderBottomLeftRadius = 18;
        panel.style.borderBottomRightRadius = 18;
        panel.style.flexShrink = 0f;
        panel.style.borderTopWidth = 1f;
        panel.style.borderBottomWidth = 1f;
        panel.style.borderLeftWidth = 1f;
        panel.style.borderRightWidth = 1f;
        panel.style.borderTopColor = GetPanelTopBorderColor();
        panel.style.borderBottomColor = GetPanelSideBorderColor();
        panel.style.borderLeftColor = GetPanelSideBorderColor();
        panel.style.borderRightColor = GetPanelSideBorderColor();
        SuppressWheelEventsForPopup(panel);
    }

    private void SuppressWheelEventsForPopup(VisualElement panel)
    {
        if (panel == null)
        {
            return;
        }

        panel.RegisterCallback<WheelEvent>(evt =>
        {
            try
            {
                if (panel.style.display == DisplayStyle.None)
                {
                    return;
                }

                lastOverlayActivityTimeInSeconds = Time.unscaledTime;
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }
            catch
            {
            }
        });
    }

    private VisualElement CreatePopupHeader(string titleText, Action closeAction)
    {
        VisualElement headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 6;

        Label title = new Label(titleText);
        title.AddToClassList("smallFont");
        title.AddToClassList("textShadow");
        title.style.color = Color.white;
        title.style.flexGrow = 1f;
        headerRow.Add(title);

        Button closeButton = CreateSmallPanelButton("✕", closeAction);
        closeButton.tooltip = "Close";
        closeButton.style.minWidth = 38;
        headerRow.Add(closeButton);
        return headerRow;
    }

    private void UpdatePopupPanelLayout(VisualElement panel)
    {
        if (panel == null || uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        try
        {
            VisualElement root = uiDocument.rootVisualElement;
            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;

            if (float.IsNaN(rootWidth) || rootWidth < 320f)
            {
                rootWidth = Mathf.Max(320f, Screen.width);
            }
            if (float.IsNaN(rootHeight) || rootHeight < 240f)
            {
                rootHeight = Mathf.Max(240f, Screen.height);
            }

            float margin = Mathf.Clamp(Mathf.Min(rootWidth, rootHeight) * 0.035f, 12f, 32f);
            float panelWidth = Mathf.Min(Mathf.Max(420f, rootWidth * 0.70f), rootWidth - (margin * 2f));
            float panelHeight = Mathf.Min(Mathf.Max(260f, rootHeight * 0.70f), rootHeight - (margin * 2f));

            panel.style.width = new StyleLength(new Length(panelWidth, LengthUnit.Pixel));
            panel.style.height = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));
            panel.style.maxHeight = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));

            ScrollView scrollView = panel.Q<ScrollView>();
            if (scrollView != null)
            {
                float reservedHeight = panel == searchPanel ? 92f : (panel == queuePanel ? 104f : 54f);
                float scrollHeight = Mathf.Max(120f, panelHeight - reservedHeight);
                scrollView.style.height = new StyleLength(new Length(scrollHeight, LengthUnit.Pixel));
                scrollView.style.maxHeight = new StyleLength(new Length(scrollHeight, LengthUnit.Pixel));
                scrollView.style.flexGrow = 1f;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox popup layout failed: " + ex.Message);
        }
    }

    private void CreateSettingsPanel()
    {
        settingsPanel = new VisualElement();
        settingsPanel.name = "betterJukeboxSettingsPanel";
        settingsPanel.focusable = true;
        ApplyPopupPanelStyle(settingsPanel);

        VisualElement headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 6;

        Label title = new Label("⚙ BetterJukebox Settings");
        title.AddToClassList("smallFont");
        title.AddToClassList("textShadow");
        title.style.color = Color.white;
        title.style.flexGrow = 1f;
        headerRow.Add(title);

        Button closeButton = CreateSmallPanelButton("✕", CloseSettingsPanel);
        closeButton.tooltip = "Close settings";
        closeButton.style.minWidth = 38;
        headerRow.Add(closeButton);
        settingsPanel.Add(headerRow);

        Label version = CreatePanelLabel("Version 1.4.6.13");
        version.style.color = new Color(1f, 1f, 1f, 0.65f);
        settingsPanel.Add(version);

        ScrollView settingsScrollView = new ScrollView(ScrollViewMode.Vertical);
        settingsScrollView.name = "betterJukeboxSettingsScrollView";
        settingsScrollView.style.flexGrow = 1f;
        settingsScrollView.style.marginTop = 8;
        settingsScrollView.style.backgroundColor = new Color(0f, 0f, 0f, 0.16f);
        settingsScrollView.style.borderTopLeftRadius = 12;
        settingsScrollView.style.borderTopRightRadius = 12;
        settingsScrollView.style.borderBottomLeftRadius = 12;
        settingsScrollView.style.borderBottomRightRadius = 12;

        settingsResultsContainer = new VisualElement();
        settingsResultsContainer.style.flexDirection = FlexDirection.Column;
        settingsScrollView.Add(settingsResultsContainer);
        settingsPanel.Add(settingsScrollView);

        // Keep Settings in the same overlay flow as Search, Queue, and History.
        // This makes the main menu stay above the panel instead of Settings using its own centered window.
        actionOverlay.Add(settingsPanel);
        UpdateSettingsPanelLayout();
    }

    private void UpdateSettingsPanelLayout()
    {
        if (settingsPanel == null || uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        try
        {
            VisualElement root = uiDocument.rootVisualElement;
            float rootWidth = root.resolvedStyle.width;
            float rootHeight = root.resolvedStyle.height;

            if (float.IsNaN(rootWidth) || rootWidth < 320f)
            {
                rootWidth = Mathf.Max(320f, Screen.width);
            }
            if (float.IsNaN(rootHeight) || rootHeight < 240f)
            {
                rootHeight = Mathf.Max(240f, Screen.height);
            }

            float margin = Mathf.Clamp(Mathf.Min(rootWidth, rootHeight) * 0.035f, 12f, 32f);
            float panelWidth = Mathf.Min(Mathf.Max(420f, rootWidth * 0.70f), rootWidth - (margin * 2f));
            float panelHeight = Mathf.Min(Mathf.Max(260f, rootHeight * 0.70f), rootHeight - (margin * 2f));

            settingsPanel.style.width = new StyleLength(new Length(panelWidth, LengthUnit.Pixel));
            settingsPanel.style.height = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));
            settingsPanel.style.maxHeight = new StyleLength(new Length(panelHeight, LengthUnit.Pixel));

            ScrollView settingsScrollView = settingsPanel.Q<ScrollView>("betterJukeboxSettingsScrollView");
            if (settingsScrollView != null)
            {
                float scrollHeight = Mathf.Max(120f, panelHeight - 92f);
                settingsScrollView.style.height = new StyleLength(new Length(scrollHeight, LengthUnit.Pixel));
                settingsScrollView.style.maxHeight = new StyleLength(new Length(scrollHeight, LengthUnit.Pixel));
                settingsScrollView.style.flexGrow = 1f;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox settings layout failed: " + ex.Message);
        }
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

    private void CloseCompanionPanel()
    {
        companionPanelIsVisible = false;
        if (companionPanel != null)
        {
            companionPanel.style.display = DisplayStyle.None;
        }
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;
    }

    private void CloseHistoryPanel()
    {
        historyPanelIsVisible = false;
        if (historyPanel != null)
        {
            historyPanel.style.display = DisplayStyle.None;
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

    private void HideAllPopupPanels()
    {
        CancelMultiSelectForNavigation();
        searchPanelIsVisible = false;
        queuePanelIsVisible = false;
        companionPanelIsVisible = false;
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
        if (companionPanel != null)
        {
            companionPanel.style.display = DisplayStyle.None;
        }
        if (historyPanel != null)
        {
            historyPanel.style.display = DisplayStyle.None;
        }
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
        }
    }

    private void PrepareBetterJukeboxUiForSceneTransition()
    {
        // Popups are bound to the current UI document. Close them before Melody Mania
        // starts loading the next SingScene so no popup keeps focus or stale elements.
        HideAllPopupPanels();
        popupVolumeGuardActive = false;
        popupLockedVolumePercent = -1;
        popupVolumeGuardUntil = 0f;
        HideNowPlayingCard(false);
    }

    private void ToggleSettingsPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        bool shouldOpen = !settingsPanelIsVisible;
        HideAllPopupPanels();

        if (!shouldOpen || settingsPanel == null)
        {
            return;
        }

        settingsPanelIsVisible = true;
        settingsPanel.style.display = DisplayStyle.Flex;
        UpdateSettingsPanelLayout();
        UpdateSettingsPanel();
        ApplyThemeToVisibleElements();
        UpdateCompanionHub(true);
        settingsPanel.Focus();
    }

    private void UpdateSettingsPanel()
    {
        if (settingsResultsContainer == null)
        {
            return;
        }

        settingsResultsContainer.Clear();
        settingsResultsContainer.style.paddingLeft = 8;
        settingsResultsContainer.style.paddingRight = 8;
        settingsResultsContainer.style.paddingTop = 8;
        settingsResultsContainer.style.paddingBottom = 8;

        VisualElement generalSection = CreateSettingsCategory("General", "Basic BetterJukebox behavior and theme.");
        generalSection.Add(CreateSettingsCycleButton("Theme", GetUiThemeLabel(), CycleUiTheme));
        generalSection.Add(CreateSettingsToggle("Show Overlay", () => modSettings.ShowMouseOverlay, value => modSettings.ShowMouseOverlay = value));
        generalSection.Add(CreateSettingsToggle("Auto Hide Menu", () => modSettings.AutoHideMenu, value => modSettings.AutoHideMenu = value));
        AddSettingsCategory(generalSection);

        VisualElement playbackSection = CreateSettingsCategory("Playback", "Automatic karaoke playback and queue flow.");
        playbackSection.Add(CreateSettingsToggle("Auto Open Sing", () => modSettings.AutoOpenSing, value => modSettings.AutoOpenSing = value));
        playbackSection.Add(CreateSettingsToggle("Auto Play Random Song", () => modSettings.AutoPlayRandomSong, value => modSettings.AutoPlayRandomSong = value));
        playbackSection.Add(CreateSettingsToggle("Shuffle", () => modSettings.RandomSelection, value => modSettings.RandomSelection = value));
        playbackSection.Add(CreateSettingsToggle("Auto Continue", () => modSettings.AutoContinue, value => modSettings.AutoContinue = value));
        AddSettingsCategory(playbackSection);

        VisualElement displaySection = CreateSettingsCategory("Display", "Choose what visual information BetterJukebox should show.");
        displaySection.Add(CreateSettingsToggle("Show Now Playing", () => modSettings.ShowNowPlaying, value => modSettings.ShowNowPlaying = value));
        displaySection.Add(CreateSettingsCycleButton("Now Playing Seconds", modSettings.NowPlayingSeconds.ToString() + " sec", CycleNowPlayingSeconds));
        displaySection.Add(CreateSettingsToggle("Album Art in Search", () => modSettings.ShowAlbumArtInSearch, value => { modSettings.ShowAlbumArtInSearch = value; if (searchPanelIsVisible) { UpdateSearchResults(searchTextField != null ? searchTextField.value : ""); } }));
        displaySection.Add(CreateSettingsToggle("Album Art in Queue", () => modSettings.ShowAlbumArtInQueue, value => { modSettings.ShowAlbumArtInQueue = value; if (queuePanelIsVisible) { UpdateQueuePanel(); } }));
        displaySection.Add(CreateSettingsToggle("Album Art in History", () => modSettings.ShowAlbumArtInHistory, value => { modSettings.ShowAlbumArtInHistory = value; if (historyPanelIsVisible) { UpdateHistoryPanel(); } }));
        displaySection.Add(CreateSettingsToggle("Album Art in Playlists", () => modSettings.ShowAlbumArtInPlaylists, value => { modSettings.ShowAlbumArtInPlaylists = value; if (searchPanelIsVisible && showOnlyPlaylistSearchResults) { UpdateSearchResults(searchTextField != null ? searchTextField.value : ""); } }));
        displaySection.Add(CreateSettingsToggle("Player/Mics in Now Playing Queue", () => modSettings.ShowNowPlayingQueuePlayerMics, value => modSettings.ShowNowPlayingQueuePlayerMics = value));
        displaySection.Add(CreateSettingsToggle("Show Favorite Stars", () => modSettings.ShowFavoriteStars, value => { modSettings.ShowFavoriteStars = value; RefreshFavoriteViews(); }));
        displaySection.Add(CreateSettingsToggle("Favorite Star Animation", () => modSettings.ShowFavoriteSparkleAnimation, value => modSettings.ShowFavoriteSparkleAnimation = value));
        displaySection.Add(CreateSettingsToggle("Hide Lyrics", () => modSettings.HideLyrics, value => modSettings.HideLyrics = value));
        AddSettingsCategory(displaySection);

        VisualElement overlaySection = CreateSettingsCategory("Overlay & Controls", "Mouse behavior, animation speed and overlay controls.");
        overlaySection.Add(CreateSettingsToggle("Fade In / Fade Out", () => modSettings.FadeAnimations, value => modSettings.FadeAnimations = value));
        overlaySection.Add(CreateSettingsCycleButton("Animation Speed", GetAnimationSpeedLabel(), CycleAnimationSpeed));
        overlaySection.Add(CreateSettingsToggle("Disable Vanilla Pause Button", () => modSettings.HideBuiltInPauseButton, value => modSettings.HideBuiltInPauseButton = value));
        overlaySection.Add(CreateSettingsToggle("Shake Mouse To Show Menu", () => modSettings.ShakeMouseToShowMenu, value => modSettings.ShakeMouseToShowMenu = value));
        overlaySection.Add(CreateSettingsToggle("Hide Mouse After Timeout", () => modSettings.HideMouseAfterTimeout, value => modSettings.HideMouseAfterTimeout = value));
        overlaySection.Add(CreateSettingsCycleButton("Overlay Hide Seconds", modSettings.OverlayHideSeconds.ToString() + " sec", CycleOverlayHideSeconds));
        AddSettingsCategory(overlaySection);

        VisualElement companionSection = CreateSettingsCategory("Companion", "Install and use the Melody Mania Companion App.");

        VisualElement companionQrRow = new VisualElement();
        companionQrRow.style.flexDirection = FlexDirection.Row;
        companionQrRow.style.alignItems = Align.Center;
        companionQrRow.style.marginTop = 6;
        companionQrRow.style.marginBottom = 6;

        VisualElement companionQr = CreateSmallCompanionAppQrCodeElement();
        companionQr.style.marginRight = 12;
        companionQrRow.Add(companionQr);

        VisualElement companionTextColumn = new VisualElement();
        companionTextColumn.style.flexDirection = FlexDirection.Column;
        companionTextColumn.style.flexGrow = 1f;
        companionTextColumn.style.flexShrink = 1f;

        List<string> settingsCompanionDevices = GetConnectedCompanionDeviceNames();
        int settingsCompanionDeviceCount = settingsCompanionDevices != null ? settingsCompanionDevices.Count : 0;

        Label companionInfo = CreatePanelLabel("Scan the QR code to install the Melody Mania Companion App.");
        companionInfo.style.whiteSpace = WhiteSpace.Normal;
        companionInfo.style.color = new Color(1f, 1f, 1f, 0.78f);
        companionInfo.style.marginTop = 0f;
        companionInfo.style.marginBottom = 4f;
        companionTextColumn.Add(companionInfo);

        VisualElement settingsCompanionStatusRow = new VisualElement();
        settingsCompanionStatusRow.style.flexDirection = FlexDirection.Row;
        settingsCompanionStatusRow.style.alignItems = Align.Center;
        settingsCompanionStatusRow.style.marginTop = 0f;
        settingsCompanionStatusRow.style.marginBottom = 3f;

        settingsCompanionStatusIcon = null;

        settingsCompanionStatusLabel = CreateCompanionStatusLabel(settingsCompanionDeviceCount);
        settingsCompanionStatusRow.Add(settingsCompanionStatusLabel);
        companionTextColumn.Add(settingsCompanionStatusRow);

        settingsCompanionDeviceCountLabel = CreatePanelLabel(settingsCompanionDeviceCount == 0 ? "0 devices connected" : settingsCompanionDeviceCount + (settingsCompanionDeviceCount == 1 ? " device connected" : " devices connected"));
        settingsCompanionDeviceCountLabel.style.whiteSpace = WhiteSpace.Normal;
        settingsCompanionDeviceCountLabel.style.color = new Color(1f, 1f, 1f, 0.68f);
        settingsCompanionDeviceCountLabel.style.marginTop = 0f;
        settingsCompanionDeviceCountLabel.style.marginBottom = 4f;
        companionTextColumn.Add(settingsCompanionDeviceCountLabel);

        Label companionHint = CreatePanelLabel("The full Companion dashboard is still available from Queue.");
        companionHint.style.whiteSpace = WhiteSpace.Normal;
        companionHint.style.color = new Color(1f, 1f, 1f, 0.58f);
        companionHint.style.marginTop = 0f;
        companionHint.style.marginBottom = 0f;
        companionTextColumn.Add(companionHint);

        companionQrRow.Add(companionTextColumn);
        companionSection.Add(companionQrRow);
        AddSettingsCategory(companionSection);

        VisualElement aboutSection = CreateSettingsCategory("About", "Version and mod information.");
        Label versionInfo = CreatePanelLabel("BetterJukebox 1.4.6.14");
        versionInfo.style.color = GetAccentHoverColor();
        aboutSection.Add(versionInfo);
        Label disableInfo = CreatePanelLabel("To fully disable BetterJukebox, open Melody Mania > Mods and disable the mod there. This avoids two different enable states.");
        disableInfo.style.whiteSpace = WhiteSpace.Normal;
        disableInfo.style.color = new Color(1f, 1f, 1f, 0.72f);
        aboutSection.Add(disableInfo);
        AddSettingsCategory(aboutSection);
    }

    private void AddSettingsCategory(VisualElement category)
    {
        if (settingsResultsContainer == null || category == null)
        {
            return;
        }
        settingsResultsContainer.Add(category);
    }

    private VisualElement CreateSettingsCategory(string titleText, string subtitleText)
    {
        VisualElement category = new VisualElement();
        category.style.flexDirection = FlexDirection.Column;
        category.style.marginTop = 6;
        category.style.marginBottom = 10;
        category.style.paddingLeft = 10;
        category.style.paddingRight = 10;
        category.style.paddingTop = 10;
        category.style.paddingBottom = 10;
        category.style.backgroundColor = GetRowColor();
        category.style.borderTopLeftRadius = 14;
        category.style.borderTopRightRadius = 14;
        category.style.borderBottomLeftRadius = 14;
        category.style.borderBottomRightRadius = 14;
        category.style.borderTopWidth = 1f;
        category.style.borderBottomWidth = 1f;
        category.style.borderLeftWidth = 1f;
        category.style.borderRightWidth = 1f;
        category.style.borderTopColor = GetPanelTopBorderColor();
        category.style.borderBottomColor = GetPanelSideBorderColor();
        category.style.borderLeftColor = GetPanelSideBorderColor();
        category.style.borderRightColor = GetPanelSideBorderColor();

        Label title = CreatePanelLabel(titleText);
        title.style.color = GetAccentHoverColor();
        title.style.marginBottom = 2;
        category.Add(title);

        if (!string.IsNullOrEmpty(subtitleText))
        {
            Label subtitle = CreatePanelLabel(subtitleText);
            subtitle.style.color = new Color(1f, 1f, 1f, 0.62f);
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.marginBottom = 6;
            category.Add(subtitle);
        }

        return category;
    }

    private VisualElement CreatePremiumPanelSection(string titleText, string subtitleText)
    {
        VisualElement section = new VisualElement();
        section.style.flexDirection = FlexDirection.Column;
        section.style.marginTop = 6;
        section.style.marginBottom = 10;
        section.style.paddingLeft = 10;
        section.style.paddingRight = 10;
        section.style.paddingTop = 10;
        section.style.paddingBottom = 10;
        section.style.backgroundColor = GetRowColor();
        section.style.borderTopLeftRadius = 14;
        section.style.borderTopRightRadius = 14;
        section.style.borderBottomLeftRadius = 14;
        section.style.borderBottomRightRadius = 14;
        section.style.borderTopWidth = 1f;
        section.style.borderBottomWidth = 1f;
        section.style.borderLeftWidth = 1f;
        section.style.borderRightWidth = 1f;
        section.style.borderTopColor = GetPanelTopBorderColor();
        section.style.borderBottomColor = GetPanelSideBorderColor();
        section.style.borderLeftColor = GetPanelSideBorderColor();
        section.style.borderRightColor = GetPanelSideBorderColor();

        if (!string.IsNullOrEmpty(titleText))
        {
            Label title = CreatePanelLabel(titleText);
            title.style.color = GetAccentHoverColor();
            title.style.marginBottom = 2;
            section.Add(title);
        }

        if (!string.IsNullOrEmpty(subtitleText))
        {
            Label subtitle = CreatePanelLabel(subtitleText);
            subtitle.style.color = new Color(1f, 1f, 1f, 0.62f);
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.marginBottom = 6;
            section.Add(subtitle);
        }

        return section;
    }

    private Label CreateSettingsSectionLabel(string text)
    {
        Label label = CreatePanelLabel(text);
        label.style.marginTop = 10;
        label.style.marginBottom = 4;
        label.style.color = GetAccentHoverColor();
        return label;
    }

    private VisualElement CreateSettingsToggle(string labelText, Func<bool> getter, Action<bool> setter)
    {
        VisualElement row = CreatePanelRow();
        row.style.marginTop = 4;
        row.style.marginBottom = 4;
        Label label = CreatePanelLabel(labelText);
        label.style.flexGrow = 1f;
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
        row.style.marginTop = 4;
        row.style.marginBottom = 4;
        Label label = CreatePanelLabel(labelText);
        label.style.flexGrow = 1f;
        row.Add(label);
        row.Add(CreateSmallPanelButton(valueText, () =>
        {
            clicked();
            UpdateSettingsPanel();
            if (forceButtonThemeRefreshOnNextSettingsUpdate)
            {
                forceButtonThemeRefreshOnNextSettingsUpdate = false;
                ApplyThemeToVisibleElements();
                ApplyButtonTreeTheme(actionOverlay);
            }
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

    private string GetUiThemeLabel()
    {
        return GetUiThemeName();
    }

    private void CycleUiTheme()
    {
        modSettings.UiTheme = (GetUiThemeIndex() + 1) % 6;
        forceButtonThemeRefreshOnNextSettingsUpdate = true;
        ApplyThemeToVisibleElements();
        ApplyButtonTreeTheme(actionOverlay);
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
        {
            ApplyThemeToVisibleElements();
            ApplyButtonTreeTheme(actionOverlay);
        });
    }

    private int GetUiThemeIndex()
    {
        if (modSettings == null)
        {
            return 1;
        }

        int theme = modSettings.UiTheme;
        if (theme < 0 || theme > 5)
        {
            return 1;
        }
        return theme;
    }

    private string GetUiThemeName()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return "DiscoGrey";
        }
        if (theme == 2)
        {
            return "DiscoGreen";
        }
        if (theme == 3)
        {
            return "DiscoBlue";
        }
        if (theme == 4)
        {
            return "DiscoRed";
        }
        if (theme == 5)
        {
            return "DiscoGold";
        }
        return "DiscoPurple";
    }

    private bool IsDiscoPurpleTheme()
    {
        return GetUiThemeIndex() == 1;
    }

    private bool IsDiscoGreyTheme()
    {
        return GetUiThemeIndex() == 0;
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
        Button button = null;
        button = new Button(() =>
        {
            PulseClickedButton(button);
            if (clicked != null)
            {
                clicked();
            }
        });
        button.AddToClassList("smallFont");
        button.style.marginLeft = 6;
        button.style.marginRight = 6;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        AddButtonVisual(button, text, "smallFont", 18f, 18f, 10f, 10f, 18f);
        RegisterButtonThemeHover(button);
        return button;
    }

    private Button CreateOverlayIconButton(string text, Action clicked, string tooltip)
    {
        Button button = null;
        button = new Button(() =>
        {
            PulseClickedButton(button);
            if (clicked != null)
            {
                clicked();
            }
        });
        button.tooltip = tooltip;
        button.AddToClassList("smallFont");
        button.style.marginLeft = 6;
        button.style.marginRight = 6;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.minWidth = new StyleLength(new Length(54, LengthUnit.Pixel));
        AddButtonVisual(button, text, "smallFont", 16f, 16f, 10f, 10f, 18f);
        RegisterButtonThemeHover(button);
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

        ProcessKeyboardShortcuts();
        ProcessMultiSelectLongPress();
        ProcessPlaylistNameDialogKeyboardInput();
        ProcessPopupEscapeInput();
        UpdatePopupWheelVolumeGuard();
        ProcessSettingsKeyboardInput();
        ProcessSearchKeyboardInput();
        ProcessQueueHoldMoveProgress();
        ProcessPlaylistHoldMoveProgress();
        UpdateSkipSong();
        SuppressBuiltInMousePause();
        HideBuiltInPauseButton();
        KeepSearchFieldFocused();
        UpdateActionOverlay();
        UpdateQueueBadge(false);
        UpdateCompanionHub(false);
        UpdateSettingsCompanionStatus();
        UpdateProgressBar();
        UpdateUiElementsFadeOut();
        UpdateFinishingScene();
    }


    private void LateUpdate()
    {
        if (!isInjectionFinished)
        {
            return;
        }

        UpdatePopupWheelVolumeGuard();
    }

    private bool IsAnyBetterJukeboxPopupVisible()
    {
        return searchPanelIsVisible || queuePanelIsVisible || companionPanelIsVisible || historyPanelIsVisible || settingsPanelIsVisible;
    }

    private void UpdatePopupWheelVolumeGuard()
    {
        bool anyPopupVisible = IsAnyBetterJukeboxPopupVisible();
        if (!anyPopupVisible)
        {
            popupVolumeGuardActive = false;
            popupLockedVolumePercent = -1;
            popupVolumeGuardUntil = 0f;
            return;
        }

        if (!popupVolumeGuardActive)
        {
            popupLockedVolumePercent = ReadSettingsVolumePercent();
            popupVolumeGuardActive = true;
        }

        if (HasMouseWheelDelta())
        {
            popupVolumeGuardUntil = Time.unscaledTime + 0.5f;
            lastOverlayActivityTimeInSeconds = Time.unscaledTime;
        }

        if (popupLockedVolumePercent >= 0 && (Time.unscaledTime <= popupVolumeGuardUntil || HasMouseWheelDelta()))
        {
            int currentVolume = ReadSettingsVolumePercent();
            if (currentVolume >= 0 && currentVolume != popupLockedVolumePercent)
            {
                WriteSettingsVolumePercent(popupLockedVolumePercent);
                RefreshVolumeManager();
            }
        }
    }

    private bool HasMouseWheelDelta()
    {
        try
        {
            if (Mouse.current == null)
            {
                return false;
            }

            Vector2 scrollValue = Mouse.current.scroll.ReadValue();
            return Mathf.Abs(scrollValue.x) > 0.01f || Mathf.Abs(scrollValue.y) > 0.01f;
        }
        catch
        {
            return false;
        }
    }

    private int ReadSettingsVolumePercent()
    {
        try
        {
            if (settings == null)
            {
                return -1;
            }

            var property = settings.GetType().GetProperty("VolumePercent");
            if (property == null)
            {
                return -1;
            }

            object value = property.GetValue(settings, null);
            if (value is int)
            {
                return (int)value;
            }
            if (value is float)
            {
                return Mathf.RoundToInt((float)value);
            }
            if (value is double)
            {
                return Mathf.RoundToInt((float)(double)value);
            }
        }
        catch
        {
        }

        return -1;
    }

    private void WriteSettingsVolumePercent(int volumePercent)
    {
        try
        {
            if (settings == null)
            {
                return;
            }

            var property = settings.GetType().GetProperty("VolumePercent");
            if (property != null)
            {
                property.SetValue(settings, volumePercent, null);
            }
        }
        catch
        {
        }
    }

    private void RefreshVolumeManager()
    {
        try
        {
            if (volumeManager == null)
            {
                return;
            }

            var method = volumeManager.GetType().GetMethod("UpdateGeneralVolume");
            if (method != null)
            {
                method.Invoke(volumeManager, null);
                return;
            }

            method = volumeManager.GetType().GetMethod("UpdateVolume");
            if (method != null)
            {
                method.Invoke(volumeManager, null);
            }
        }
        catch
        {
        }
    }


    private void ProcessKeyboardShortcuts()
    {
        if (IsPlaylistNameDialogOpen())
        {
            return;
        }

        if (modSettings == null || !modSettings.EnableBetterJukebox)
        {
            return;
        }

        bool inputSystemCtrlPressed = false;
        bool legacyCtrlPressed = false;

        bool inputSystemFPressed = false;
        bool inputSystemSPressed = false;
        bool inputSystemQPressed = false;
        bool inputSystemHPressed = false;
        bool inputSystemCommaPressed = false;

        bool legacyFPressed = false;
        bool legacySPressed = false;
        bool legacyQPressed = false;
        bool legacyHPressed = false;
        bool legacyCommaPressed = false;

        try
        {
            if (Keyboard.current != null)
            {
                inputSystemCtrlPressed = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
                inputSystemFPressed = Keyboard.current.fKey.wasPressedThisFrame;
                inputSystemSPressed = Keyboard.current.sKey.wasPressedThisFrame;
                inputSystemQPressed = Keyboard.current.qKey.wasPressedThisFrame;
                inputSystemHPressed = Keyboard.current.hKey.wasPressedThisFrame;
                inputSystemCommaPressed = Keyboard.current.commaKey.wasPressedThisFrame;
            }
        }
        catch
        {
            inputSystemCtrlPressed = false;
        }

        try
        {
            legacyCtrlPressed = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            legacyFPressed = UnityEngine.Input.GetKeyDown(KeyCode.F);
            legacySPressed = UnityEngine.Input.GetKeyDown(KeyCode.S);
            legacyQPressed = UnityEngine.Input.GetKeyDown(KeyCode.Q);
            legacyHPressed = UnityEngine.Input.GetKeyDown(KeyCode.H);
            legacyCommaPressed = UnityEngine.Input.GetKeyDown(KeyCode.Comma);
        }
        catch
        {
            legacyCtrlPressed = false;
        }

        bool ctrlPressed = inputSystemCtrlPressed || legacyCtrlPressed;
        if (!ctrlPressed)
        {
            return;
        }

        if (inputSystemFPressed || inputSystemSPressed || legacyFPressed || legacySPressed)
        {
            ToggleSearchPanel();
            return;
        }

        if (inputSystemQPressed || legacyQPressed)
        {
            ToggleQueuePanel();
            return;
        }

        if (inputSystemHPressed || legacyHPressed)
        {
            ToggleHistoryPanel();
            return;
        }

        if (inputSystemCommaPressed || legacyCommaPressed)
        {
            ToggleSettingsPanel();
            return;
        }
    }

    private void ProcessPopupEscapeInput()
    {
        if (IsPlaylistNameDialogOpen())
        {
            return;
        }

        bool anyPopupVisible = searchPanelIsVisible || queuePanelIsVisible || companionPanelIsVisible || historyPanelIsVisible || settingsPanelIsVisible;
        if (!anyPopupVisible)
        {
            pendingOverlayEscape = false;
            return;
        }

        bool escapePressed = pendingOverlayEscape;
        try
        {
            escapePressed = escapePressed || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
        }
        catch
        {
        }

        if (escapePressed)
        {
            pendingOverlayEscape = false;
            pendingSettingsEscape = false;
            pendingSearchEscape = false;
            pendingSearchTextInput.Clear();
            SafeHandleEscapeInOverlay();
        }
    }

    private void SafeHandleEscapeInOverlay()
    {
        try
        {
            HandleEscapeInOverlay();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox Escape handler ignored stale UI reference: " + ex.Message);
            HideAllPopupPanels();
        }
    }

    private GameObject GetSafeOwnerGameObject()
    {
        try
        {
            return gameObject;
        }
        catch
        {
            return null;
        }
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

    private void ProcessPlaylistNameDialogKeyboardInput()
    {
        if (!IsPlaylistNameDialogOpen() || playlistNameTextField == null)
        {
            return;
        }

        // Same approach as the main Search field:
        // let UI Toolkit/TextField handle normal character input, backspace, delete and selection.
        // Only handle dialog actions here, otherwise letters are inserted twice.
        try
        {
            playlistNameTextField.Focus();
        }
        catch
        {
        }

        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame)
        {
            ClosePlaylistDialog();
            return;
        }

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            SubmitPlaylistNameDialog();
            return;
        }
    }

    private void InsertPlaylistNameText(string text)
    {
        if (playlistNameTextField == null || string.IsNullOrEmpty(text))
        {
            return;
        }

        string value = playlistNameTextField.value ?? "";
        int start = 0;
        int end = 0;
        GetPlaylistNameSelection(value, out start, out end);
        string newValue = value.Substring(0, start) + text + value.Substring(end);
        playlistNameTextField.value = newValue;
        SetPlaylistNameCaret(start + text.Length);
    }

    private void DeletePlaylistNameText(bool forward)
    {
        if (playlistNameTextField == null)
        {
            return;
        }

        string value = playlistNameTextField.value ?? "";
        int start = 0;
        int end = 0;
        GetPlaylistNameSelection(value, out start, out end);

        if (start != end)
        {
            string selectedDeleteValue = value.Substring(0, start) + value.Substring(end);
            playlistNameTextField.value = selectedDeleteValue;
            SetPlaylistNameCaret(start);
            return;
        }

        if (!forward && start > 0)
        {
            string backspaceValue = value.Substring(0, start - 1) + value.Substring(start);
            playlistNameTextField.value = backspaceValue;
            SetPlaylistNameCaret(start - 1);
            return;
        }

        if (forward && start < value.Length)
        {
            string deleteValue = value.Substring(0, start) + value.Substring(start + 1);
            playlistNameTextField.value = deleteValue;
            SetPlaylistNameCaret(start);
        }
    }

    private void GetPlaylistNameSelection(string value, out int start, out int end)
    {
        value = value ?? "";
        start = Mathf.Clamp(playlistNameCaretIndex, 0, value.Length);
        end = start;
        try
        {
            start = Mathf.Clamp(Mathf.Min(playlistNameTextField.cursorIndex, playlistNameTextField.selectIndex), 0, value.Length);
            end = Mathf.Clamp(Mathf.Max(playlistNameTextField.cursorIndex, playlistNameTextField.selectIndex), 0, value.Length);
        }
        catch
        {
        }
    }

    private void SetPlaylistNameCaret(int caretIndex)
    {
        if (playlistNameTextField == null)
        {
            return;
        }

        string value = playlistNameTextField.value ?? "";
        playlistNameCaretIndex = Mathf.Clamp(caretIndex, 0, value.Length);
        try
        {
            playlistNameTextField.cursorIndex = playlistNameCaretIndex;
            playlistNameTextField.selectIndex = playlistNameCaretIndex;
        }
        catch
        {
        }
    }

    private void ProcessSearchKeyboardInput()
    {
        if (IsPlaylistNameDialogOpen())
        {
            return;
        }

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
            SafeHandleEscapeInOverlay();
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
        if (IsPlaylistNameDialogOpen())
        {
            return;
        }

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

            SafeHandleEscapeInOverlay();
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
        if (companionPanelIsVisible && companionPanel != null)
        {
            UpdatePopupPanelLayout(companionPanel);
        }

        if (searchPanelIsVisible || queuePanelIsVisible || companionPanelIsVisible || historyPanelIsVisible || settingsPanelIsVisible)
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
            ShowBrandLogo();
            return;
        }

        actionOverlayIsVisible = true;
        UnityEngine.Cursor.visible = true;
        ShowBrandLogo();
        actionOverlay.style.display = DisplayStyle.Flex;
        actionOverlay.BringToFront();
        if (brandLogo != null)
        {
            brandLogo.BringToFront();
        }
        actionOverlay.Focus();
        GameObject ownerObject = GetSafeOwnerGameObject();
        if (modSettings != null && modSettings.FadeAnimations && ownerObject != null)
        {
            AnimationUtils.FadeInVisualElement(ownerObject, actionOverlay, GetOverlayFadeDuration());
        }
    }

    private void HideActionOverlay()
    {
        actionOverlayIsVisible = false;
        searchPanelIsVisible = false;
        queuePanelIsVisible = false;
        companionPanelIsVisible = false;
        historyPanelIsVisible = false;
        settingsPanelIsVisible = false;

        SafeHideElement(searchPanel);
        SafeHideElement(queuePanel);
        SafeHideElement(companionPanel);
        SafeHideElement(historyPanel);
        SafeHideElement(settingsPanel);
        SafeHideElement(actionOverlay);
        HideBrandLogo();
    }

    private void SafeHideElement(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        try
        {
            element.style.display = DisplayStyle.None;
        }
        catch
        {
        }
    }

    private void ShowBrandLogo()
    {
        if (brandLogo == null)
        {
            return;
        }

        ApplyBrandLogoTheme();
        brandLogo.style.display = DisplayStyle.Flex;
        brandLogo.BringToFront();
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, ApplyBrandLogoTheme);
        GameObject ownerObject = GetSafeOwnerGameObject();
        if (modSettings != null && modSettings.FadeAnimations && ownerObject != null)
        {
            AnimationUtils.FadeInVisualElement(ownerObject, brandLogo, GetOverlayFadeDuration());
            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(GetOverlayFadeDuration(), ApplyBrandLogoTheme);
        }
    }

    private void HideBrandLogo()
    {
        if (brandLogo == null)
        {
            return;
        }

        GameObject ownerObject = GetSafeOwnerGameObject();
        if (modSettings != null && modSettings.FadeAnimations && ownerObject != null)
        {
            AnimationUtils.FadeOutVisualElement(ownerObject, brandLogo, GetOverlayFadeDuration());
            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(GetOverlayFadeDuration(), () =>
            {
                if (!actionOverlayIsVisible && brandLogo != null)
                {
                    brandLogo.style.display = DisplayStyle.None;
                }
            });
        }
        else
        {
            brandLogo.style.display = DisplayStyle.None;
        }
    }

    private void ToggleSearchPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        bool shouldOpen = !searchPanelIsVisible;
        HideAllPopupPanels();

        if (!shouldOpen || searchPanel == null)
        {
            return;
        }

        searchPanelIsVisible = true;
        searchPanel.style.display = DisplayStyle.Flex;
        UpdatePopupPanelLayout(searchPanel);
        UpdateFavoriteFilterButtonText();
        UpdateFavoriteActionRowVisibility();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        ApplyThemeToVisibleElements();
        lastSearchFocusTime = Time.unscaledTime;
        if (searchTextField != null)
        {
            searchTextField.Focus();
        }
    }


    private void UpdateQueueBadge(bool force)
    {
        if (queueOverlayButton == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (!force && now - lastQueueBadgeUpdateTimeInSeconds < 0.5f)
        {
            return;
        }
        lastQueueBadgeUpdateTimeInSeconds = now;

        int queueCount = 0;
        try
        {
            queueCount = GetRealSongQueueEntries().Count;
        }
        catch
        {
            queueCount = 0;
        }

        if (!force && queueCount == lastQueueBadgeCount)
        {
            return;
        }

        bool countChanged = lastQueueBadgeCount >= 0 && queueCount != lastQueueBadgeCount;
        lastQueueBadgeCount = queueCount;
        SetButtonVisualText(queueOverlayButton, queueCount > 0 ? "📋 Queue (" + queueCount + ")" : "📋 Queue");
        if (countChanged)
        {
            PulseQueueBadge();
        }
    }

    private void PulseQueueBadge()
    {
        if (queueOverlayButton == null)
        {
            return;
        }

        ApplyButtonPulseStyle(queueOverlayButton);
        Button button = queueOverlayButton;
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.22f, () =>
        {
            if (button != null)
            {
                ApplyButtonNormalStyle(button);
            }
        });
    }

    private void MarkQueueChanged()
    {
        queueChangeAnimationPending = true;
    }

    private void ToggleQueuePanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        bool shouldOpen = !queuePanelIsVisible;
        HideAllPopupPanels();

        if (!shouldOpen || queuePanel == null)
        {
            return;
        }

        queuePanelIsVisible = true;
        queuePanel.style.display = DisplayStyle.Flex;
        UpdatePopupPanelLayout(queuePanel);
        UpdateQueuePanel();
        ApplyThemeToVisibleElements();
        queuePanel.focusable = true;
        queuePanel.Focus();
    }

    private void ToggleCompanionPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        bool shouldOpen = !companionPanelIsVisible;
        HideAllPopupPanels();

        if (!shouldOpen || companionPanel == null)
        {
            return;
        }

        companionPanelIsVisible = true;
        companionPanel.style.display = DisplayStyle.Flex;
        UpdatePopupPanelLayout(companionPanel);
        ApplyThemeToVisibleElements();
        UpdateCompanionHub(true);
        companionPanel.focusable = true;
        companionPanel.Focus();
    }

    private void ToggleHistoryPanel()
    {
        ShowActionOverlay();
        lastOverlayActivityTimeInSeconds = Time.unscaledTime;

        bool shouldOpen = !historyPanelIsVisible;
        HideAllPopupPanels();

        if (!shouldOpen || historyPanel == null)
        {
            return;
        }

        historyPanelIsVisible = true;
        historyPanel.style.display = DisplayStyle.Flex;
        UpdatePopupPanelLayout(historyPanel);
        UpdateHistoryPanel();
        ApplyThemeToVisibleElements();
        historyPanel.focusable = true;
        historyPanel.Focus();
    }


    private void UpdateCompanionHub(bool force)
    {
        if (!companionPanelIsVisible)
        {
            return;
        }

        if (!force && Time.unscaledTime - lastCompanionHubRefreshTimeInSeconds < 1.5f)
        {
            return;
        }
        lastCompanionHubRefreshTimeInSeconds = Time.unscaledTime;

        List<string> connectedDevices = GetConnectedCompanionDeviceNames();
        string signature = string.Join("|", connectedDevices.ToArray());
        if (!force && signature == lastCompanionHubSignature)
        {
            ApplyCompanionStatusStyles();
            return;
        }
        lastCompanionHubSignature = signature;

        int count = connectedDevices.Count;
        if (companionStatusLabel != null)
        {
            companionStatusLabel.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(companionStatusLabel, count);
        }
        if (companionVersionLabel != null)
        {
            companionVersionLabel.text = count > 0
                ? "Server ready. Companion devices use the same queue as the PC."
                : "Open the Companion App on the same network. Melody Mania should be found automatically.";
        }
        if (companionDeviceCountLabel != null)
        {
            companionDeviceCountLabel.text = count == 0
                ? "0 devices connected"
                : count + (count == 1 ? " device connected" : " devices connected");
        }

        if (settingsCompanionStatusLabel != null)
        {
            settingsCompanionStatusLabel.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(settingsCompanionStatusLabel, count);
        }

        if (settingsCompanionDeviceCountLabel != null)
        {
            settingsCompanionDeviceCountLabel.text = count == 0
                ? "0 devices connected"
                : count + (count == 1 ? " device connected" : " devices connected");
        }
        if (companionDeviceListContainer != null)
        {
            companionDeviceListContainer.Clear();
            if (count == 0)
            {
                Label empty = CreatePanelLabel("Waiting for devices...\nOpen Companion on your phone to connect.");
                empty.style.opacity = 0.68f;
                empty.style.whiteSpace = WhiteSpace.Normal;
                companionDeviceListContainer.Add(empty);
            }
            else
            {
                for (int i = 0; i < connectedDevices.Count; i++)
                {
                    companionDeviceListContainer.Add(CreateCompanionDeviceRow(connectedDevices[i]));
                }
            }
        }
    }

    private void UpdateSettingsCompanionStatus()
    {
        if (!settingsPanelIsVisible && settingsCompanionStatusLabel == null && settingsCompanionDeviceCountLabel == null)
        {
            return;
        }

        List<string> connectedDevices = GetConnectedCompanionDeviceNames();
        int count = connectedDevices != null ? connectedDevices.Count : 0;

        if (settingsCompanionStatusLabel != null)
        {
            settingsCompanionStatusLabel.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(settingsCompanionStatusLabel, count);
        }

        if (settingsCompanionDeviceCountLabel != null)
        {
            settingsCompanionDeviceCountLabel.text = count == 0
                ? "0 devices connected"
                : count + (count == 1 ? " device connected" : " devices connected");
        }
    }

    private Label CreateCompanionStatusLabel(int connectedDeviceCount)
    {
        Label label = CreatePanelLabel(GetCompanionStatusText(connectedDeviceCount));
        ApplyCompanionStatusLabelStyle(label, connectedDeviceCount);
        label.RegisterCallback<AttachToPanelEvent>(evt =>
        {
            List<string> connectedDevices = GetConnectedCompanionDeviceNames();
            int count = connectedDevices != null ? connectedDevices.Count : 0;
            label.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(label, count);
        });
        label.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            List<string> connectedDevices = GetConnectedCompanionDeviceNames();
            int count = connectedDevices != null ? connectedDevices.Count : 0;
            label.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(label, count);
        });
        return label;
    }

    private string GetCompanionStatusText(int connectedDeviceCount)
    {
        return connectedDeviceCount > 0
            ? "🟢 Connected"
            : "🟡 Waiting for devices...";
    }

    private string GetCompanionStatusTextWithoutIcon(int connectedDeviceCount)
    {
        return connectedDeviceCount > 0
            ? "Connected"
            : "Waiting for devices...";
    }

    private Color GetCompanionStatusColor(int connectedDeviceCount)
    {
        return connectedDeviceCount > 0
            ? new Color(0.7f, 1f, 0.75f, 1f)
            : new Color(1f, 0.86f, 0.45f, 1f);
    }

    private void ApplyCompanionStatusLabelStyle(Label label, int connectedDeviceCount)
    {
        if (label == null)
        {
            return;
        }

        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.color = GetCompanionStatusColor(connectedDeviceCount);
        label.style.opacity = 1f;
        label.style.marginTop = 0f;
        label.style.marginBottom = 0f;
    }

    private void ApplyCompanionStatusStyles()
    {
        List<string> connectedDevices = GetConnectedCompanionDeviceNames();
        int count = connectedDevices != null ? connectedDevices.Count : 0;

        if (companionStatusLabel != null)
        {
            companionStatusLabel.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(companionStatusLabel, count);
        }

        if (settingsCompanionStatusLabel != null)
        {
            settingsCompanionStatusLabel.text = GetCompanionStatusText(count);
            ApplyCompanionStatusLabelStyle(settingsCompanionStatusLabel, count);
        }
    }

    private VisualElement CreateCompanionDeviceRow(string deviceName)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 4f;
        row.style.paddingTop = 7f;
        row.style.paddingBottom = 7f;
        row.style.paddingLeft = 8f;
        row.style.paddingRight = 8f;
        row.style.backgroundColor = new Color(1f, 1f, 1f, 0.055f);
        row.style.borderTopLeftRadius = 9f;
        row.style.borderTopRightRadius = 9f;
        row.style.borderBottomLeftRadius = 9f;
        row.style.borderBottomRightRadius = 9f;

        QueuePlayerMicInfo deviceInfo = new QueuePlayerMicInfo();
        deviceInfo.PlayerName = string.IsNullOrEmpty(deviceName) ? "Companion device" : deviceName;
        deviceInfo.HasMicColor = false;

        VisualElement nativePlayerEntry = CreateNativePlayerMicEntry(deviceInfo);
        if (nativePlayerEntry != null)
        {
            nativePlayerEntry.style.flexGrow = 1f;
            nativePlayerEntry.style.marginRight = 8f;
            row.Add(nativePlayerEntry);
        }
        else
        {
            Label icon = CreatePanelLabel("🎤");
            icon.style.marginRight = 8f;
            icon.style.color = GetAccentColor();
            row.Add(icon);

            Label name = CreatePanelLabel(deviceInfo.PlayerName);
            name.style.flexGrow = 1f;
            name.style.color = Color.white;
            row.Add(name);
        }

        Label status = CreatePanelLabel("Connected");
        status.style.opacity = 0.72f;
        status.style.marginRight = 0f;
        row.Add(status);
        return row;
    }

    private List<string> GetConnectedCompanionDeviceNames()
    {
        List<string> result = new List<string>();
        try
        {
            MonoBehaviour[] behaviours = GameObject.FindObjectsOfType<MonoBehaviour>();
            if (behaviours == null)
            {
                return result;
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                if (type == null || type.Name != "ServerSideCompanionClientManager")
                {
                    continue;
                }

                AddCompanionClientNamesFromManager(behaviour, result);
                break;
            }
        }
        catch
        {
        }

        result.Sort();
        return result;
    }

    private void AddCompanionClientNamesFromManager(object manager, List<string> result)
    {
        if (manager == null || result == null)
        {
            return;
        }

        object handlers = InvokeZeroArgMember(manager, "GetAllCompanionClientHandlers");
        if (handlers == null)
        {
            handlers = InvokeZeroArgMember(manager, "GetCompanionClientHandlers");
        }
        if (handlers == null)
        {
            object countValue = GetMemberValue(manager, "CompanionClientCount");
            int count = 0;
            if (countValue is int)
            {
                count = (int)countValue;
            }
            for (int i = 0; i < count; i++)
            {
                result.Add("Companion device " + (i + 1));
            }
            return;
        }

        System.Collections.IEnumerable enumerable = handlers as System.Collections.IEnumerable;
        if (enumerable == null)
        {
            AddCompanionClientName(handlers, result);
            return;
        }

        foreach (object handler in enumerable)
        {
            AddCompanionClientName(handler, result);
        }
    }

    private object InvokeZeroArgMember(object target, string methodName)
    {
        if (target == null || string.IsNullOrEmpty(methodName))
        {
            return null;
        }
        try
        {
            System.Reflection.MethodInfo methodInfo = target.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new Type[0], null);
            if (methodInfo != null)
            {
                return methodInfo.Invoke(target, null);
            }
        }
        catch
        {
        }
        return null;
    }

    private void AddCompanionClientName(object handler, List<string> result)
    {
        if (handler == null || result == null)
        {
            return;
        }

        object innerHandler = GetFirstMemberValue(handler, new string[] { "CompanionClientHandler", "companionClientHandler" });
        if (innerHandler != null)
        {
            handler = innerHandler;
        }

        object connectedValue = GetFirstMemberValue(handler, new string[] { "IsConnected", "IsConnectedClient", "isConnected" });
        if (connectedValue is bool && !(bool)connectedValue)
        {
            return;
        }

        string name = GetFirstStringMemberValue(handler, new string[] { "ClientName", "clientName", "DeviceName", "deviceName", "Name", "name" });
        if (string.IsNullOrEmpty(name))
        {
            name = "Companion device " + (result.Count + 1);
        }

        if (!result.Contains(name))
        {
            result.Add(name);
        }
    }

    private string GetFirstStringMemberValue(object obj, string[] memberNames)
    {
        object value = GetFirstMemberValue(obj, memberNames);
        return value == null ? null : value.ToString();
    }

    private void UpdateSearchResults(string searchText)
    {
        searchRenderGeneration++;
        pendingSearchRenderMatches.Clear();
        pendingSearchRenderSection = null;
        pendingSearchRenderIndex = 0;

        if (searchResultsContainer == null)
        {
            return;
        }

        searchResultsContainer.Clear();
        if (showOnlyPlaylistSearchResults)
        {
            UpdatePlaylistSearchResults(searchText);
            return;
        }

        string trimmedSearchText = searchText == null ? "" : searchText.Trim();
        if (!showOnlyFavoriteSearchResults && !showOnlyHistorySearchResults && trimmedSearchText.Length < 2)
        {
            searchResultsContainer.Add(CreateEmptyState("🔍", "Search songs", "Type at least 2 characters."));
            return;
        }

        string query = trimmedSearchText.ToLowerInvariant();
        List<SongMeta> sourceSongs = showOnlyHistorySearchResults ? GetHistorySongMetasForSearch() : (showOnlyFavoriteSearchResults ? GetFavoriteSongMetasForQueue() : GetAllSelectableSongMetas());
        List<SongMeta> matches = sourceSongs
            .Where(songMeta => (!showOnlyFavoriteSearchResults || IsFavoriteSongMeta(songMeta))
                && (query.Length == 0 || MatchesSearch(songMeta, query)))
            .ToList();

        if (showOnlyFavoriteSearchResults && favoriteSortMode == 0)
        {
            matches = matches.OrderBy(songMeta => songMeta != null ? songMeta.GetArtistDashTitle() : "").ToList();
        }

        if (matches.Count == 0)
        {
            string emptyTitle = showOnlyFavoriteSearchResults ? "No favorites found" : (showOnlyHistorySearchResults ? "No history found" : "No matches");
            string emptyBody = showOnlyFavoriteSearchResults ? "Mark songs with ★ to add them here." : (showOnlyHistorySearchResults ? "Played songs will show up here." : "Try another artist or song title.");
            searchResultsContainer.Add(CreateEmptyState("♪", emptyTitle, emptyBody));
            return;
        }

        string sectionTitle = showOnlyFavoriteSearchResults ? "Favorite Songs" : (showOnlyHistorySearchResults ? "History" : "Results");
        StartProgressiveSearchRender(sectionTitle, matches, false, null);
    }

    private void StartProgressiveSearchRender(string sectionTitle, List<SongMeta> matches, bool playlistRows, BetterJukeboxPlaylist playlist)
    {
        searchRenderGeneration++;
        int generation = searchRenderGeneration;

        pendingSearchRenderMatches = matches != null ? matches : new List<SongMeta>();
        pendingSearchRenderIndex = 0;
        string subtitle = pendingSearchRenderMatches.Count + (pendingSearchRenderMatches.Count == 1 ? " song" : " songs");
        if (!playlistRows)
        {
            subtitle += " found";
        }

        pendingSearchRenderSection = CreatePremiumPanelSection(sectionTitle, subtitle);
        if (searchResultsContainer != null)
        {
            searchResultsContainer.Add(pendingSearchRenderSection);
        }

        RenderNextSearchBatch(generation, playlistRows, playlist);
    }

    private void RenderNextSearchBatch(int generation, bool playlistRows, BetterJukeboxPlaylist playlist)
    {
        if (generation != searchRenderGeneration || pendingSearchRenderSection == null || pendingSearchRenderMatches == null)
        {
            return;
        }

        int endIndex = Math.Min(pendingSearchRenderIndex + SearchRenderBatchSize, pendingSearchRenderMatches.Count);
        for (int i = pendingSearchRenderIndex; i < endIndex; i++)
        {
            SongMeta songMeta = pendingSearchRenderMatches[i];
            if (playlistRows)
            {
                pendingSearchRenderSection.Add(CreatePlaylistSongRow(songMeta, playlist));
            }
            else
            {
                pendingSearchRenderSection.Add(CreateSearchResultRow(songMeta));
            }
        }
        pendingSearchRenderIndex = endIndex;

        if (pendingSearchRenderIndex < pendingSearchRenderMatches.Count)
        {
            AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () => RenderNextSearchBatch(generation, playlistRows, playlist));
        }
    }

    private void QueueFavoriteSongs()
    {
        List<SongMeta> favoriteSongs = GetFavoriteSongMetasForQueue();
        if (favoriteSongs.Count == 0)
        {
            NotificationManager.CreateNotification(Translation.Of("No favorites to queue"));
            UpdateFavoriteFilterButtonText();
            return;
        }

        int addedCount = 0;
        foreach (SongMeta songMeta in favoriteSongs)
        {
            if (songMeta != null && AddSongToQueue(songMeta, false))
            {
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            NotificationManager.CreateNotification(Translation.Of("Queued " + addedCount + (addedCount == 1 ? " favorite" : " favorites")));
            if (queuePanelIsVisible)
            {
                UpdateQueuePanel();
            }
            UpdateQueueBadge(true);
        }
        else
        {
            NotificationManager.CreateNotification(Translation.Of("Could not queue favorites"));
        }

        UpdateFavoriteFilterButtonText();
        if (showOnlyFavoriteSearchResults)
        {
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        }
    }

    private void ShuffleFavoriteSongs()
    {
        List<SongMeta> favoriteSongs = GetFavoriteSongMetasForQueue();
        if (favoriteSongs.Count == 0)
        {
            NotificationManager.CreateNotification(Translation.Of("No favorites to shuffle"));
            return;
        }

        System.Random random = new System.Random();
        for (int i = favoriteSongs.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            SongMeta temp = favoriteSongs[i];
            favoriteSongs[i] = favoriteSongs[j];
            favoriteSongs[j] = temp;
        }

        int addedCount = 0;
        foreach (SongMeta songMeta in favoriteSongs)
        {
            if (songMeta != null && AddSongToQueue(songMeta, false))
            {
                addedCount++;
            }
        }

        NotificationManager.CreateNotification(Translation.Of("Queued " + addedCount + (addedCount == 1 ? " shuffled favorite" : " shuffled favorites")));
        UpdateQueueBadge(true);
        if (queuePanelIsVisible)
        {
            UpdateQueuePanel();
        }
    }

    private void RemoveAllFavoriteSongs()
    {
        LoadFavoriteSongIds();
        if (betterJukeboxFavoriteSongIds.Count == 0)
        {
            NotificationManager.CreateNotification(Translation.Of("No favorites to remove"));
            return;
        }

        ShowRemoveAllFavoritesConfirm(false);
    }

    private void ShowRemoveAllFavoritesConfirm(bool finalQuestion)
    {
        CloseRemoveAllFavoritesConfirm();

        if (searchPanel == null)
        {
            return;
        }

        favoriteRemoveConfirmPanel = new VisualElement();
        favoriteRemoveConfirmPanel.name = "betterJukeboxRemoveFavoritesConfirm";
        favoriteRemoveConfirmPanel.style.position = Position.Absolute;
        favoriteRemoveConfirmPanel.style.left = new StyleLength(new Length(16, LengthUnit.Percent));
        favoriteRemoveConfirmPanel.style.right = new StyleLength(new Length(16, LengthUnit.Percent));
        favoriteRemoveConfirmPanel.style.top = new StyleLength(new Length(24, LengthUnit.Percent));
        favoriteRemoveConfirmPanel.style.flexDirection = FlexDirection.Column;
        favoriteRemoveConfirmPanel.style.paddingLeft = 18f;
        favoriteRemoveConfirmPanel.style.paddingRight = 18f;
        favoriteRemoveConfirmPanel.style.paddingTop = 16f;
        favoriteRemoveConfirmPanel.style.paddingBottom = 16f;
        favoriteRemoveConfirmPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.94f);
        favoriteRemoveConfirmPanel.style.borderTopLeftRadius = 16f;
        favoriteRemoveConfirmPanel.style.borderTopRightRadius = 16f;
        favoriteRemoveConfirmPanel.style.borderBottomLeftRadius = 16f;
        favoriteRemoveConfirmPanel.style.borderBottomRightRadius = 16f;
        favoriteRemoveConfirmPanel.style.borderTopWidth = 1f;
        favoriteRemoveConfirmPanel.style.borderBottomWidth = 1f;
        favoriteRemoveConfirmPanel.style.borderLeftWidth = 1f;
        favoriteRemoveConfirmPanel.style.borderRightWidth = 1f;
        favoriteRemoveConfirmPanel.style.borderTopColor = GetPanelTopBorderColor();
        favoriteRemoveConfirmPanel.style.borderBottomColor = GetPanelSideBorderColor();
        favoriteRemoveConfirmPanel.style.borderLeftColor = GetPanelSideBorderColor();
        favoriteRemoveConfirmPanel.style.borderRightColor = GetPanelSideBorderColor();

        Label title = CreatePanelLabel(finalQuestion
            ? "Are you absolutely sure? 😉"
            : "Remove all favorites?");
        title.style.whiteSpace = WhiteSpace.Normal;
        title.style.marginRight = 0f;
        title.style.marginBottom = 6f;
        title.style.color = Color.white;
        favoriteRemoveConfirmPanel.Add(title);

        Label body = CreatePanelLabel(finalQuestion
            ? "Do you really, really want to remove all your favorites?"
            : "This will remove every song from your favorites list.");
        body.style.whiteSpace = WhiteSpace.Normal;
        body.style.marginRight = 0f;
        body.style.marginBottom = 12f;
        body.style.color = new Color(1f, 1f, 1f, 0.78f);
        favoriteRemoveConfirmPanel.Add(body);

        VisualElement buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.justifyContent = Justify.FlexEnd;
        buttonRow.style.alignItems = Align.Center;
        buttonRow.Add(CreateSmallPanelButton("No", CloseRemoveAllFavoritesConfirm));
        buttonRow.Add(CreateSmallPanelButton("Yes", () =>
        {
            if (finalQuestion)
            {
                CloseRemoveAllFavoritesConfirm();
                ExecuteRemoveAllFavoriteSongs();
                ShowFavoritesRemovedSkull();
            }
            else
            {
                ShowRemoveAllFavoritesConfirm(true);
            }
        }));
        favoriteRemoveConfirmPanel.Add(buttonRow);

        searchPanel.Add(favoriteRemoveConfirmPanel);
        favoriteRemoveConfirmPanel.BringToFront();
        AnimateRemoveAllFavoritesConfirmPopup(finalQuestion);
    }

    private void AnimateRemoveAllFavoritesConfirmPopup(bool finalQuestion)
    {
        if (favoriteRemoveConfirmPanel == null)
        {
            return;
        }

        try
        {
            favoriteRemoveConfirmPanel.style.opacity = 0f;
            GameObject ownerObject = GetSafeOwnerGameObject();
            if (ownerObject != null && modSettings != null && modSettings.FadeAnimations)
            {
                AnimationUtils.FadeInVisualElement(ownerObject, favoriteRemoveConfirmPanel, finalQuestion ? 0.16f : 0.10f);
            }
            else
            {
                favoriteRemoveConfirmPanel.style.opacity = 1f;
            }

            if (finalQuestion)
            {
                VisualElement panel = favoriteRemoveConfirmPanel;
                panel.style.backgroundColor = new Color(0.05f, 0.03f, 0.07f, 0.98f);
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.10f, () =>
                {
                    if (panel != null)
                    {
                        panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.94f);
                    }
                });
            }
        }
        catch
        {
        }
    }

    private void CloseRemoveAllFavoritesConfirm()
    {
        if (favoriteRemoveConfirmPanel == null)
        {
            return;
        }

        try
        {
            favoriteRemoveConfirmPanel.RemoveFromHierarchy();
        }
        catch
        {
        }
        favoriteRemoveConfirmPanel = null;
    }

    private void ExecuteRemoveAllFavoriteSongs()
    {
        LoadFavoriteSongIds();
        int removedCount = betterJukeboxFavoriteSongIds.Count;
        betterJukeboxFavoriteSongIds.Clear();
        betterJukeboxFavoriteSongIdOrder.Clear();
        SaveFavoriteSongIds();
        NotificationManager.CreateNotification(Translation.Of("Removed " + removedCount + (removedCount == 1 ? " favorite" : " favorites")));
        RefreshFavoriteViews();
    }

    private void ShowFavoritesRemovedSkull()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement skullContainer = new VisualElement();
        skullContainer.name = "betterJukeboxFavoritesRemovedSkull";
        skullContainer.style.position = Position.Absolute;
        skullContainer.style.left = new StyleLength(new Length(0, LengthUnit.Pixel));
        skullContainer.style.right = new StyleLength(new Length(0, LengthUnit.Pixel));
        skullContainer.style.top = new StyleLength(new Length(34, LengthUnit.Percent));
        skullContainer.style.flexDirection = FlexDirection.Row;
        skullContainer.style.justifyContent = Justify.Center;
        skullContainer.style.alignItems = Align.Center;
        skullContainer.pickingMode = PickingMode.Ignore;

        Label skull = new Label("☠");
        skull.AddToClassList("textShadow");
        skull.style.fontSize = 84f;
        skull.style.color = Color.white;
        skull.pickingMode = PickingMode.Ignore;
        skullContainer.Add(skull);

        uiDocument.rootVisualElement.Add(skullContainer);
        skullContainer.BringToFront();

        GameObject ownerObject = GetSafeOwnerGameObject();
        if (ownerObject != null)
        {
            AnimationUtils.FadeOutVisualElement(ownerObject, skullContainer, 1.25f);
        }
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(1.35f, () =>
        {
            if (skullContainer != null)
            {
                try
                {
                    skullContainer.RemoveFromHierarchy();
                }
                catch
                {
                }
            }
        });
    }

    private List<SongMeta> GetFavoriteSongMetasForQueue()
    {
        LoadFavoriteSongIds();
        NormalizeLoadedFavoriteSongIds();

        List<SongMeta> result = new List<SongMeta>();
        HashSet<string> addedIds = new HashSet<string>();
        List<SongMeta> allSongMetas = null;
        try
        {
            allSongMetas = GetAllSelectableSongMetas();
        }
        catch
        {
            allSongMetas = new List<SongMeta>();
        }

        foreach (SongMeta songMeta in allSongMetas)
        {
            if (songMeta == null || !IsFavoriteSongMeta(songMeta))
            {
                continue;
            }

            string favoriteId = GetFavoriteSongMetaId(songMeta);
            if (string.IsNullOrWhiteSpace(favoriteId))
            {
                favoriteId = songMeta.GetArtistDashTitle();
            }

            if (!addedIds.Contains(favoriteId))
            {
                addedIds.Add(favoriteId);
                result.Add(songMeta);
            }
        }

        if (favoriteSortMode == 1)
        {
            Dictionary<string, int> orderById = GetFavoriteOrderIndexMap();
            result = result.OrderBy(songMeta => GetFavoriteOrderIndexForSong(songMeta, orderById)).ToList();
        }
        else
        {
            result = result.OrderBy(songMeta => songMeta != null ? songMeta.GetArtistDashTitle() : "").ToList();
        }

        return result;
    }

    private Dictionary<string, int> GetFavoriteOrderIndexMap()
    {
        LoadFavoriteSongIds();
        Dictionary<string, int> result = new Dictionary<string, int>();
        for (int i = 0; i < betterJukeboxFavoriteSongIdOrder.Count; i++)
        {
            string id = betterJukeboxFavoriteSongIdOrder[i];
            if (!string.IsNullOrWhiteSpace(id) && !result.ContainsKey(id))
            {
                result.Add(id, i);
            }
        }
        return result;
    }

    private int GetFavoriteOrderIndexForSong(SongMeta songMeta, Dictionary<string, int> orderById)
    {
        List<string> ids = GetFavoriteSongMetaIds(songMeta);
        foreach (string id in ids)
        {
            int index;
            if (!string.IsNullOrWhiteSpace(id) && orderById != null && orderById.TryGetValue(id, out index))
            {
                return index;
            }
        }
        return 999999;
    }

    private string GetFavoritesFilterButtonText()
    {
        int count = GetFavoriteSongIdCountFromFile();
        if (count < 0)
        {
            count = betterJukeboxFavoriteSongIds != null ? betterJukeboxFavoriteSongIds.Count : 0;
        }
        return count > 0 ? "★ Favorites (" + count + ")" : "★ Favorites";
    }

    private void UpdateFavoriteFilterButtonText()
    {
        if (searchFavoritesFilterButton == null)
        {
            return;
        }

        SetButtonVisualText(searchFavoritesFilterButton, GetFavoritesFilterButtonText());
    }


    private void UpdateFavoriteActionRowVisibility()
    {
        if (searchFavoritesActionRow == null)
        {
            return;
        }
        searchFavoritesActionRow.style.display = showOnlyFavoriteSearchResults ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private int GetFavoriteSongIdCountFromFile()
    {
        try
        {
            EnsureFavoritesFileExists();
            string path = GetFavoritesPath();
            if (!System.IO.File.Exists(path))
            {
                return betterJukeboxFavoriteSongIds != null ? betterJukeboxFavoriteSongIds.Count : 0;
            }

            string text = System.IO.File.ReadAllText(path);
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            HashSet<string> uniqueIds = new HashSet<string>();
            string[] parts = text.Split(new char[] { '\n', '\r', ',', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = NormalizeFavoriteIdText(part);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    uniqueIds.Add(id);
                }
            }
            return uniqueIds.Count;
        }
        catch
        {
            return -1;
        }
    }

    private List<SongMeta> GetHistorySongMetasForSearch()
    {
        List<SongMeta> result = new List<SongMeta>();
        foreach (SongMeta songMeta in betterJukeboxHistory)
        {
            if (songMeta != null && !result.Contains(songMeta))
            {
                result.Add(songMeta);
            }
        }
        return result;
    }

    private bool MatchesSearch(SongMeta songMeta, string query)
    {
        return MatchesSmartSearch(songMeta, query);
    }

    private bool MatchesSmartSearch(SongMeta songMeta, string query)
    {
        if (songMeta == null)
        {
            return false;
        }

        query = query == null ? "" : query.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string searchableText = GetSmartSearchText(songMeta);
        string[] tokens = query.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens == null || tokens.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = NormalizeSmartSearchToken(tokens[i]);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (IsSmartSearchNoiseWord(token))
            {
                continue;
            }

            string nextToken = i + 1 < tokens.Length ? NormalizeSmartSearchToken(tokens[i + 1]) : "";
            if (!MatchesSmartSearchToken(songMeta, searchableText, token, nextToken))
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesSmartSearchToken(SongMeta songMeta, string searchableText, string token, string nextToken)
    {
        if (songMeta == null)
        {
            return false;
        }

        if (token.StartsWith("year:"))
        {
            return MatchesSongYear(songMeta, token.Substring(5), false);
        }

        if (token.StartsWith("language:"))
        {
            return MatchesSongLanguage(songMeta, token.Substring(9));
        }

        if (token.StartsWith("lang:"))
        {
            return MatchesSongLanguage(songMeta, token.Substring(5));
        }

        if (token.StartsWith("genre:"))
        {
            return ContainsSmartText(GetSongMetaSmartValue(songMeta, new string[] { "Genre", "Genres" }), token.Substring(6));
        }

        if (token.StartsWith("edition:"))
        {
            return ContainsSmartText(GetSongMetaSmartValue(songMeta, new string[] { "Edition", "Editions" }), token.Substring(8));
        }

        if (token.StartsWith("playlist:"))
        {
            return IsSongInPlaylistName(songMeta, token.Substring(9));
        }

        if (token.StartsWith("has:"))
        {
            return MatchesHasFilter(songMeta, token.Substring(4));
        }

        if (token == "favorite" || token == "favorites" || token == "favourite" || token == "favourites")
        {
            return IsFavoriteSongMeta(songMeta);
        }

        if (IsLanguageSearchToken(token))
        {
            return MatchesSongLanguage(songMeta, token);
        }

        if (IsDecadeToken(token))
        {
            return MatchesSongDecade(songMeta, GetDecadeStart(token));
        }

        if (IsFourDigitYear(token))
        {
            if (nextToken == "songs" || nextToken == "song" || nextToken == "decade" || nextToken == "decades")
            {
                return MatchesSongDecade(songMeta, GetDecadeStart(token));
            }
            return MatchesSongYear(songMeta, token, false);
        }

        if (token == "video" || token == "cover" || token == "background")
        {
            return MatchesHasFilter(songMeta, token) || ContainsSmartText(searchableText, token);
        }

        return ContainsSmartText(searchableText, token);
    }

    private string GetSmartSearchText(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return "";
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        AppendSmartSearchText(builder, songMeta.Artist);
        AppendSmartSearchText(builder, songMeta.Title);
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Year", "ReleaseYear", "YearString" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Language", "Languages" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Genre", "Genres" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Edition", "Editions" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Tags", "Tag", "MedleyTags" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Bpm", "BPM", "BeatsPerMinute" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Video", "VideoFile", "VideoPath" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Background", "BackgroundFile", "BackgroundPath" }));
        AppendSmartSearchText(builder, GetSongMetaSmartValue(songMeta, new string[] { "Cover", "CoverFile", "CoverPath" }));
        return builder.ToString().ToLowerInvariant();
    }

    private void AppendSmartSearchText(System.Text.StringBuilder builder, string text)
    {
        if (builder == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        builder.Append(' ');
        builder.Append(text);
    }

    private string GetSongMetaSmartValue(SongMeta songMeta, string[] memberNames)
    {
        if (songMeta == null || memberNames == null)
        {
            return "";
        }

        for (int i = 0; i < memberNames.Length; i++)
        {
            object value = GetMemberValue(songMeta, memberNames[i]);
            string text = GetReadableSmartSearchValue(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        return "";
    }

    private string GetReadableSmartSearchValue(object value)
    {
        if (value == null)
        {
            return "";
        }

        if (value is string)
        {
            return (string)value;
        }

        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable != null && !(value is string))
        {
            List<string> parts = new List<string>();
            foreach (object item in enumerable)
            {
                string itemText = GetReadableSmartSearchValue(item);
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    parts.Add(itemText);
                }
            }
            return string.Join(" ", parts.ToArray());
        }

        return value.ToString();
    }

    private string NormalizeSmartSearchToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "";
        }
        return token.Trim().Trim(',', '.', ';', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'').ToLowerInvariant();
    }

    private bool IsSmartSearchNoiseWord(string token)
    {
        return token == "="
            || token == "songs"
            || token == "song"
            || token == "year"
            || token == "years"
            || token == "release"
            || token == "released"
            || token == "language"
            || token == "lang"
            || token == "genre"
            || token == "edition";
    }

    private bool ContainsSmartText(string source, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }
        return source.ToLowerInvariant().Contains(token.ToLowerInvariant());
    }

    private bool IsFourDigitYear(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 4)
        {
            return false;
        }
        int value;
        if (!int.TryParse(token, out value))
        {
            return false;
        }
        return value >= 1900 && value <= 2099;
    }

    private bool IsDecadeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (token.EndsWith("s") && token.Length == 5 && IsFourDigitYear(token.Substring(0, 4)))
        {
            return true;
        }

        if (token.EndsWith("s") && token.Length == 3)
        {
            int value;
            if (int.TryParse(token.Substring(0, 2), out value))
            {
                return true;
            }
        }
        return false;
    }

    private int GetDecadeStart(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return -1;
        }

        if (token.EndsWith("s"))
        {
            token = token.Substring(0, token.Length - 1);
        }

        int value;
        if (!int.TryParse(token, out value))
        {
            return -1;
        }

        if (value < 100)
        {
            if (value <= 30)
            {
                value += 2000;
            }
            else
            {
                value += 1900;
            }
        }

        return (value / 10) * 10;
    }

    private bool MatchesSongYear(SongMeta songMeta, string yearText, bool allowDecade)
    {
        int targetYear;
        if (!int.TryParse(yearText, out targetYear))
        {
            return false;
        }

        int songYear = GetSongYear(songMeta);
        if (songYear <= 0)
        {
            return false;
        }

        if (allowDecade)
        {
            int decade = (targetYear / 10) * 10;
            return songYear >= decade && songYear <= decade + 9;
        }
        return songYear == targetYear;
    }

    private bool MatchesSongDecade(SongMeta songMeta, int decadeStart)
    {
        if (decadeStart <= 0)
        {
            return false;
        }
        int songYear = GetSongYear(songMeta);
        return songYear >= decadeStart && songYear <= decadeStart + 9;
    }

    private int GetSongYear(SongMeta songMeta)
    {
        string text = GetSongMetaSmartValue(songMeta, new string[] { "Year", "ReleaseYear", "YearString" });
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        for (int i = 0; i <= text.Length - 4; i++)
        {
            string part = text.Substring(i, 4);
            int year;
            if (int.TryParse(part, out year) && year >= 1900 && year <= 2099)
            {
                return year;
            }
        }
        return -1;
    }

    private bool IsLanguageSearchToken(string token)
    {
        string canonical = GetCanonicalLanguageToken(token);
        return !string.IsNullOrWhiteSpace(canonical);
    }

    private bool MatchesSongLanguage(SongMeta songMeta, string token)
    {
        string language = GetSongMetaSmartValue(songMeta, new string[] { "Language", "Languages" }).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        string canonical = GetCanonicalLanguageToken(token);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            canonical = token;
        }

        if (language.Contains(canonical) || language.Contains(token))
        {
            return true;
        }

        if (canonical == "swedish") { return language.Contains("svenska") || language.Contains("sverige"); }
        if (canonical == "english") { return language.Contains("engelska") || language.Contains("eng"); }
        if (canonical == "finnish") { return language.Contains("finska") || language.Contains("suomi") || language.Contains("finland"); }
        if (canonical == "norwegian") { return language.Contains("norska") || language.Contains("norge"); }
        if (canonical == "danish") { return language.Contains("danska") || language.Contains("danmark"); }
        if (canonical == "german") { return language.Contains("tyska") || language.Contains("deutsch"); }
        if (canonical == "spanish") { return language.Contains("spanska") || language.Contains("espanol") || language.Contains("español"); }
        if (canonical == "french") { return language.Contains("franska") || language.Contains("francais") || language.Contains("français"); }
        if (canonical == "italian") { return language.Contains("italienska"); }
        return false;
    }

    private string GetCanonicalLanguageToken(string token)
    {
        token = NormalizeSmartSearchToken(token);
        if (token == "swedish" || token == "svedish" || token == "svenska" || token == "sverige") { return "swedish"; }
        if (token == "english" || token == "engelska" || token == "eng") { return "english"; }
        if (token == "finnish" || token == "finska" || token == "suomi" || token == "finland") { return "finnish"; }
        if (token == "norwegian" || token == "norska" || token == "norge") { return "norwegian"; }
        if (token == "danish" || token == "danska" || token == "danmark") { return "danish"; }
        if (token == "german" || token == "tyska" || token == "deutsch") { return "german"; }
        if (token == "spanish" || token == "spanska" || token == "espanol" || token == "español") { return "spanish"; }
        if (token == "french" || token == "franska" || token == "francais" || token == "français") { return "french"; }
        if (token == "italian" || token == "italienska") { return "italian"; }
        return "";
    }

    private bool MatchesHasFilter(SongMeta songMeta, string token)
    {
        token = NormalizeSmartSearchToken(token);
        if (token == "video")
        {
            return !string.IsNullOrWhiteSpace(GetSongMetaSmartValue(songMeta, new string[] { "Video", "VideoFile", "VideoPath" }));
        }
        if (token == "cover" || token == "albumart" || token == "art")
        {
            return !string.IsNullOrWhiteSpace(GetSongMetaSmartValue(songMeta, new string[] { "Cover", "CoverFile", "CoverPath" }));
        }
        if (token == "background" || token == "bg")
        {
            return !string.IsNullOrWhiteSpace(GetSongMetaSmartValue(songMeta, new string[] { "Background", "BackgroundFile", "BackgroundPath" }));
        }
        return false;
    }

    private bool IsSongInPlaylistName(SongMeta songMeta, string playlistNameQuery)
    {
        if (songMeta == null || string.IsNullOrWhiteSpace(playlistNameQuery))
        {
            return false;
        }

        LoadPlaylists();
        List<string> ids = GetFavoriteSongMetaIds(songMeta);
        string normalizedPlaylistQuery = playlistNameQuery.ToLowerInvariant();
        for (int i = 0; i < betterJukeboxPlaylists.Count; i++)
        {
            BetterJukeboxPlaylist playlist = betterJukeboxPlaylists[i];
            if (playlist == null || string.IsNullOrWhiteSpace(playlist.Name))
            {
                continue;
            }

            if (!playlist.Name.ToLowerInvariant().Contains(normalizedPlaylistQuery))
            {
                continue;
            }

            for (int idIndex = 0; idIndex < ids.Count; idIndex++)
            {
                if (playlist.SongIds.Contains(ids[idIndex]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private VisualElement CreateSearchResultRow(SongMeta songMeta)
    {
        VisualElement row = CreatePanelRow();
        ConfigureSongRowForMultiSelect(row, songMeta);

        if (multiSelectMode)
        {
            row.Add(CreateMultiSelectCheckmark(songMeta));
        }

        if (modSettings.ShowAlbumArtInSearch)
        {
            row.Add(CreateAlbumArtElement(songMeta));
        }

        VisualElement textColumn = new VisualElement();
        textColumn.style.flexDirection = FlexDirection.Column;
        textColumn.style.flexGrow = 1f;
        textColumn.style.marginRight = 8;

        Label titleLabel = CreatePanelLabel(songMeta != null && !string.IsNullOrWhiteSpace(songMeta.Title) ? songMeta.Title : "Unknown title");
        titleLabel.style.marginBottom = 0f;
        titleLabel.style.marginRight = 0f;
        textColumn.Add(titleLabel);

        Label artistLabel = CreatePanelLabel(songMeta != null && !string.IsNullOrWhiteSpace(songMeta.Artist) ? songMeta.Artist : "Unknown artist");
        artistLabel.style.opacity = 0.72f;
        artistLabel.style.marginTop = 0f;
        artistLabel.style.marginRight = 0f;
        textColumn.Add(artistLabel);

        row.Add(textColumn);

        if (!multiSelectMode)
        {
            Button playNowButton = CreateSmallPanelButton("Play now", () => PlaySongNow(songMeta));
            Button queueButton = null;
            queueButton = CreateSmallPanelButton("Queue", () =>
            {
                if (AddSongToQueue(songMeta, false))
                {
                    ShowQueueAddedButtonFeedback(queueButton);
                }
            });

            row.Add(playNowButton);
            row.Add(queueButton);
            row.Add(CreateSmallPanelButton("🎵", () => ShowAddToPlaylistDialog(songMeta)));
            row.Add(CreateFavoriteStarButton(songMeta, () => UpdateSearchResults(searchTextField != null ? searchTextField.value : "")));
        }
        return row;
    }


    private void ToggleMultiSelectMode()
    {
        if (multiSelectMode)
        {
            ClearMultiSelectSelection(true);
        }
        else
        {
            EnterMultiSelectMode(null);
        }
    }

    private void EnterMultiSelectMode(SongMeta firstSong)
    {
        multiSelectMode = true;
        if (firstSong != null)
        {
            SetSongMultiSelected(firstSong, true);
        }
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void ClearMultiSelectSelection(bool exitMode)
    {
        multiSelectedSongsById.Clear();
        multiSelectedSongIdOrder.Clear();
        if (exitMode)
        {
            multiSelectMode = false;
        }
        pendingMultiSelectLongPressSong = null;
        pendingMultiSelectLongPressRow = null;
        pendingMultiSelectLongPressTriggered = false;
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void CancelMultiSelectForNavigation()
    {
        if (!multiSelectMode && multiSelectedSongIdOrder.Count == 0)
        {
            return;
        }

        multiSelectedSongsById.Clear();
        multiSelectedSongIdOrder.Clear();
        multiSelectMode = false;
        pendingMultiSelectLongPressSong = null;
        pendingMultiSelectLongPressRow = null;
        pendingMultiSelectLongPressTriggered = false;
        UpdateMultiSelectActionRowVisibility();
    }

    private void ConfigureSongRowForMultiSelect(VisualElement row, SongMeta songMeta)
    {
        if (row == null || songMeta == null)
        {
            return;
        }

        if (IsSongMultiSelected(songMeta))
        {
            ApplyMultiSelectSelectedRowStyle(row);
        }

        row.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || IsInsideButton(evt.target as VisualElement))
            {
                return;
            }
            pendingMultiSelectLongPressSong = songMeta;
            pendingMultiSelectLongPressRow = row;
            pendingMultiSelectLongPressStartedAt = Time.unscaledTime;
            pendingMultiSelectLongPressTriggered = false;
        }, TrickleDown.NoTrickleDown);

        row.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != 0 || IsInsideButton(evt.target as VisualElement))
            {
                ClearPendingMultiSelectLongPress();
                return;
            }

            if (multiSelectMode && !pendingMultiSelectLongPressTriggered)
            {
                ToggleSongMultiSelected(songMeta);
                evt.StopPropagation();
            }
            ClearPendingMultiSelectLongPress();
        }, TrickleDown.NoTrickleDown);

        row.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            if (pendingMultiSelectLongPressRow == row && !pendingMultiSelectLongPressTriggered)
            {
                ClearPendingMultiSelectLongPress();
            }
        });
    }

    private void ProcessMultiSelectLongPress()
    {
        if (pendingMultiSelectLongPressSong == null || pendingMultiSelectLongPressTriggered)
        {
            return;
        }
        if (Time.unscaledTime - pendingMultiSelectLongPressStartedAt < MultiSelectLongPressSeconds)
        {
            return;
        }

        pendingMultiSelectLongPressTriggered = true;
        if (!multiSelectMode)
        {
            EnterMultiSelectMode(pendingMultiSelectLongPressSong);
        }
        else
        {
            SetSongMultiSelected(pendingMultiSelectLongPressSong, true);
            UpdateMultiSelectActionRowVisibility();
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        }
    }

    private void ClearPendingMultiSelectLongPress()
    {
        pendingMultiSelectLongPressSong = null;
        pendingMultiSelectLongPressRow = null;
        pendingMultiSelectLongPressTriggered = false;
    }

    private void ToggleSongMultiSelected(SongMeta songMeta)
    {
        bool selected = IsSongMultiSelected(songMeta);
        SetSongMultiSelected(songMeta, !selected);
        if (multiSelectMode && multiSelectedSongIdOrder.Count == 0)
        {
            multiSelectMode = false;
        }
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void SetSongMultiSelected(SongMeta songMeta, bool selected)
    {
        if (songMeta == null)
        {
            return;
        }
        string id = GetFavoriteSongMetaId(songMeta);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }
        if (selected)
        {
            if (!multiSelectedSongsById.ContainsKey(id))
            {
                multiSelectedSongsById.Add(id, songMeta);
                multiSelectedSongIdOrder.Add(id);
            }
            else
            {
                multiSelectedSongsById[id] = songMeta;
            }
        }
        else
        {
            multiSelectedSongsById.Remove(id);
            multiSelectedSongIdOrder.Remove(id);
        }
    }

    private bool IsSongMultiSelected(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return false;
        }
        string id = GetFavoriteSongMetaId(songMeta);
        return !string.IsNullOrWhiteSpace(id) && multiSelectedSongsById.ContainsKey(id);
    }

    private List<SongMeta> GetMultiSelectedSongs()
    {
        List<SongMeta> result = new List<SongMeta>();
        foreach (string id in multiSelectedSongIdOrder.ToArray())
        {
            SongMeta songMeta;
            if (multiSelectedSongsById.TryGetValue(id, out songMeta) && songMeta != null)
            {
                result.Add(songMeta);
            }
        }
        return result;
    }

    private Label CreateMultiSelectCheckmark(SongMeta songMeta)
    {
        Label label = CreatePanelLabel(IsSongMultiSelected(songMeta) ? "☑" : "☐");
        label.style.fontSize = 22f;
        label.style.minWidth = 32f;
        label.style.marginRight = 8f;
        label.style.color = IsSongMultiSelected(songMeta) ? GetAccentHoverColor() : new Color(1f, 1f, 1f, 0.72f);
        return label;
    }

    private void ApplyMultiSelectSelectedRowStyle(VisualElement row)
    {
        if (row == null)
        {
            return;
        }
        row.style.backgroundColor = GetRowPulseColor();
        row.style.borderTopWidth = 1f;
        row.style.borderBottomWidth = 1f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderTopColor = GetAccentColor();
        row.style.borderBottomColor = GetAccentColor();
        row.style.borderLeftColor = GetAccentColor();
        row.style.borderRightColor = GetAccentColor();
    }

    private void UpdateMultiSelectActionRowVisibility()
    {
        if (multiSelectActionRow == null)
        {
            return;
        }
        multiSelectActionRow.Clear();
        if (!multiSelectMode)
        {
            multiSelectActionRow.style.display = DisplayStyle.None;
            if (searchSelectModeButton != null)
            {
                SetButtonVisualText(searchSelectModeButton, "Select");
            }
            if (searchPlaylistsActionRow != null && showOnlyPlaylistSearchResults)
            {
                UpdatePlaylistActionRowVisibility();
            }
            return;
        }

        if (searchPlaylistsActionRow != null)
        {
            searchPlaylistsActionRow.style.display = DisplayStyle.None;
        }
        multiSelectActionRow.style.display = DisplayStyle.Flex;
        int count = multiSelectedSongIdOrder.Count;
        if (searchSelectModeButton != null)
        {
            SetButtonVisualText(searchSelectModeButton, "Done");
        }

        Label countLabel = CreatePanelLabel(count + (count == 1 ? " Selected" : " Selected"));
        countLabel.style.color = GetAccentHoverColor();
        countLabel.style.flexGrow = 1f;
        multiSelectActionRow.Add(countLabel);
        multiSelectActionRow.Add(CreateSmallPanelButton("Select All", SelectAllMultiSelectCurrentList));
        multiSelectActionRow.Add(CreateSmallPanelButton("Queue", QueueMultiSelectedSongs));
        multiSelectActionRow.Add(CreateSmallPanelButton("🎵", ShowAddMultiSelectedToPlaylistDialog));
        multiSelectActionRow.Add(CreateSmallPanelButton("★", ToggleFavoriteMultiSelectedSongs));
        multiSelectActionRow.Add(CreateSmallPanelButton("Clear", () => ClearMultiSelectSelection(true)));
        multiSelectActionRow.Add(CreateSmallPanelButton("Done", () => ClearMultiSelectSelection(true)));
    }



    private List<SongMeta> GetCurrentMultiSelectSongList()
    {
        string searchText = searchTextField != null ? searchTextField.value : "";
        string trimmedSearchText = searchText == null ? "" : searchText.Trim();
        string query = trimmedSearchText.ToLowerInvariant();

        if (showOnlyPlaylistSearchResults)
        {
            if (string.IsNullOrWhiteSpace(selectedPlaylistName))
            {
                return new List<SongMeta>();
            }

            BetterJukeboxPlaylist selected = FindPlaylist(selectedPlaylistName);
            if (selected == null)
            {
                return new List<SongMeta>();
            }

            List<SongMeta> playlistSongs = GetPlaylistSongMetas(selected);
            if (query.Length > 0)
            {
                playlistSongs = playlistSongs.Where(songMeta => MatchesSearch(songMeta, query)).ToList();
            }
            return playlistSongs;
        }

        if (!showOnlyFavoriteSearchResults && !showOnlyHistorySearchResults && trimmedSearchText.Length < 2)
        {
            return new List<SongMeta>();
        }

        List<SongMeta> sourceSongs = showOnlyHistorySearchResults
            ? GetHistorySongMetasForSearch()
            : (showOnlyFavoriteSearchResults ? GetFavoriteSongMetasForQueue() : GetAllSelectableSongMetas());

        List<SongMeta> matches = sourceSongs
            .Where(songMeta => (!showOnlyFavoriteSearchResults || IsFavoriteSongMeta(songMeta))
                && (query.Length == 0 || MatchesSearch(songMeta, query)))
            .ToList();

        if (showOnlyFavoriteSearchResults && favoriteSortMode == 0)
        {
            matches = matches.OrderBy(songMeta => songMeta != null ? songMeta.GetArtistDashTitle() : "").ToList();
        }

        return matches;
    }

    private void SelectAllMultiSelectCurrentList()
    {
        List<SongMeta> songs = GetCurrentMultiSelectSongList();
        if (songs.Count == 0)
        {
            return;
        }

        if (!multiSelectMode)
        {
            EnterMultiSelectMode(null);
        }

        foreach (SongMeta songMeta in songs)
        {
            SetSongMultiSelected(songMeta, true);
        }

        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }


    private void QueueMultiSelectedSongs()
    {
        List<SongMeta> songs = GetMultiSelectedSongs();
        int addedCount = 0;
        foreach (SongMeta songMeta in songs)
        {
            if (AddSongToQueue(songMeta, false))
            {
                addedCount++;
            }
        }
        if (addedCount > 0)
        {
            NotificationManager.CreateNotification(Translation.Of("Queued " + addedCount + (addedCount == 1 ? " song" : " songs")));
            UpdateQueueBadge(true);
            if (queuePanelIsVisible)
            {
                UpdateQueuePanel();
            }
        }
    }

    private void ToggleFavoriteMultiSelectedSongs()
    {
        List<SongMeta> songs = GetMultiSelectedSongs();
        if (songs.Count == 0)
        {
            return;
        }
        bool allAreFavorites = true;
        foreach (SongMeta songMeta in songs)
        {
            if (!IsFavoriteSongMeta(songMeta))
            {
                allAreFavorites = false;
                break;
            }
        }
        foreach (SongMeta songMeta in songs)
        {
            if (allAreFavorites == IsFavoriteSongMeta(songMeta))
            {
                ToggleFavoriteSongMeta(songMeta);
            }
        }
        RefreshFavoriteViews();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void ShowAddMultiSelectedToPlaylistDialog()
    {
        LoadPlaylists();
        ClosePlaylistDialog();
        List<SongMeta> selectedSongs = GetMultiSelectedSongs();
        if (selectedSongs.Count == 0 || uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        playlistDialogPanel = new VisualElement();
        playlistDialogPanel.name = "betterJukeboxAddSelectedToPlaylistDialog";
        playlistDialogPanel.style.position = Position.Absolute;
        playlistDialogPanel.style.left = new StyleLength(new Length(35, LengthUnit.Percent));
        playlistDialogPanel.style.right = new StyleLength(new Length(35, LengthUnit.Percent));
        playlistDialogPanel.style.top = new StyleLength(new Length(28, LengthUnit.Percent));
        playlistDialogPanel.style.flexDirection = FlexDirection.Column;
        playlistDialogPanel.focusable = true;
        playlistDialogPanel.style.paddingLeft = 16f;
        playlistDialogPanel.style.paddingRight = 16f;
        playlistDialogPanel.style.paddingTop = 14f;
        playlistDialogPanel.style.paddingBottom = 14f;
        playlistDialogPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.92f);
        playlistDialogPanel.style.borderTopLeftRadius = 18f;
        playlistDialogPanel.style.borderTopRightRadius = 18f;
        playlistDialogPanel.style.borderBottomLeftRadius = 18f;
        playlistDialogPanel.style.borderBottomRightRadius = 18f;
        playlistDialogPanel.style.borderTopWidth = 1f;
        playlistDialogPanel.style.borderBottomWidth = 1f;
        playlistDialogPanel.style.borderLeftWidth = 1f;
        playlistDialogPanel.style.borderRightWidth = 1f;
        ApplyThemedBorder(playlistDialogPanel);
        playlistDialogPanel.Add(CreatePopupHeader("Add Selected to Playlist", ClosePlaylistDialog));
        if (betterJukeboxPlaylists.Count == 0)
        {
            playlistDialogPanel.Add(CreatePanelLabel("No playlists yet."));
        }
        foreach (BetterJukeboxPlaylist playlist in betterJukeboxPlaylists.OrderBy(it => it.Name).ToList())
        {
            BetterJukeboxPlaylist targetPlaylist = playlist;
            Button playlistButton = null;
            playlistButton = CreateSmallPanelButton("🎵 " + targetPlaylist.Name, () =>
            {
                int addedCount = AddSongsToPlaylist(targetPlaylist, selectedSongs);
                SetButtonVisualText(playlistButton, addedCount + (addedCount == 1 ? " song added" : " songs added") + " to " + targetPlaylist.Name);
                ApplyButtonPulseStyle(playlistButton);
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.75f, () =>
                {
                    ClosePlaylistDialog();
                    UpdatePlaylistFilterButtonText();
                    UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
                });
            });
            playlistButton.style.marginTop = 5f;
            playlistButton.style.marginBottom = 5f;
            playlistDialogPanel.Add(playlistButton);
        }
        uiDocument.rootVisualElement.Add(playlistDialogPanel);
        playlistDialogPanel.BringToFront();
    }

    private int AddSongsToPlaylist(BetterJukeboxPlaylist playlist, List<SongMeta> songs)
    {
        if (playlist == null || songs == null)
        {
            return 0;
        }
        int addedCount = 0;
        for (int i = songs.Count - 1; i >= 0; i--)
        {
            SongMeta songMeta = songs[i];
            if (songMeta == null)
            {
                continue;
            }
            string id = GetFavoriteSongMetaId(songMeta);
            if (string.IsNullOrWhiteSpace(id) || playlist.SongIds.Contains(id))
            {
                continue;
            }
            playlist.SongIds.Insert(0, id);
            addedCount++;
        }
        if (addedCount > 0)
        {
            SavePlaylists();
        }
        return addedCount;
    }

    private VisualElement CreateAlbumArtElement(SongMeta songMeta)
    {
        return CreateAlbumArtElement(songMeta, 48f, 8f, 10f);
    }

    private VisualElement CreateAlbumArtElement(SongMeta songMeta, float size, float radius, float marginRight)
    {
        VisualElement coverContainer = new VisualElement();
        coverContainer.style.width = size;
        coverContainer.style.height = size;
        coverContainer.style.minWidth = size;
        coverContainer.style.marginRight = marginRight;
        coverContainer.style.backgroundColor = new Color(1f, 1f, 1f, 0.10f);
        coverContainer.style.borderTopLeftRadius = radius;
        coverContainer.style.borderTopRightRadius = radius;
        coverContainer.style.borderBottomLeftRadius = radius;
        coverContainer.style.borderBottomRightRadius = radius;
        coverContainer.style.alignItems = Align.Center;
        coverContainer.style.justifyContent = Justify.Center;
        coverContainer.style.overflow = Overflow.Hidden;

        Texture2D texture = LoadAlbumArtTexture(songMeta);
        if (texture != null)
        {
            Image image = new Image();
            image.image = texture;
            image.style.width = size;
            image.style.height = size;
            coverContainer.Add(image);
        }
        else
        {
            Label fallbackIcon = new Label("♪");
            fallbackIcon.AddToClassList("smallFont");
            fallbackIcon.style.color = new Color(1f, 1f, 1f, 0.72f);
            coverContainer.Add(fallbackIcon);
        }

        return coverContainer;
    }

    private Texture2D LoadAlbumArtTexture(SongMeta songMeta)
    {
        string coverPath = GetAlbumArtPath(songMeta);
        if (string.IsNullOrWhiteSpace(coverPath))
        {
            return null;
        }

        Texture2D cachedTexture;
        if (betterJukeboxAlbumArtCache.TryGetValue(coverPath, out cachedTexture))
        {
            return cachedTexture;
        }

        if (betterJukeboxMissingAlbumArtCache.Contains(coverPath))
        {
            return null;
        }

        try
        {
            if (!System.IO.File.Exists(coverPath))
            {
                betterJukeboxMissingAlbumArtCache.Add(coverPath);
                return null;
            }

            byte[] bytes = System.IO.File.ReadAllBytes(coverPath);
            if (bytes == null || bytes.Length == 0)
            {
                betterJukeboxMissingAlbumArtCache.Add(coverPath);
                return null;
            }

            Texture2D texture = new Texture2D(2, 2);
            if (!TryLoadImageIntoTexture(texture, bytes))
            {
                betterJukeboxMissingAlbumArtCache.Add(coverPath);
                return null;
            }

            texture.name = "BetterJukeboxAlbumArt";
            betterJukeboxAlbumArtCache[coverPath] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            betterJukeboxMissingAlbumArtCache.Add(coverPath);
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not load album art: {ex.Message}");
            return null;
        }
    }


    private bool TryLoadImageIntoTexture(Texture2D texture, byte[] bytes)
    {
        if (texture == null || bytes == null || bytes.Length == 0)
        {
            return false;
        }

        try
        {
            Type imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            if (imageConversionType == null)
            {
                imageConversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine.CoreModule");
            }
            if (imageConversionType == null)
            {
                return false;
            }

            System.Reflection.MethodInfo loadImageMethod = imageConversionType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]) });
            object[] args;
            if (loadImageMethod != null)
            {
                args = new object[] { texture, bytes };
            }
            else
            {
                loadImageMethod = imageConversionType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (loadImageMethod == null)
                {
                    return false;
                }
                args = new object[] { texture, bytes, false };
            }

            object result = loadImageMethod.Invoke(null, args);
            if (result is bool)
            {
                return (bool)result;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetAlbumArtPath(SongMeta songMeta)
    {
        if (songMeta == null || string.IsNullOrWhiteSpace(songMeta.Cover))
        {
            return null;
        }

        string cover = songMeta.Cover;
        try
        {
            if (System.IO.Path.IsPathRooted(cover))
            {
                return cover;
            }
        }
        catch { }

        try
        {
            if (songMeta.FileInfo != null && songMeta.FileInfo.DirectoryName != null)
            {
                return System.IO.Path.Combine(songMeta.FileInfo.DirectoryName, cover);
            }
        }
        catch { }

        return null;
    }

    private void UpdateQueuePanel()
    {
        if (queueResultsContainer == null)
        {
            return;
        }

        queueResultsContainer.Clear();

        try
        {
            List<object> realQueueEntries = GetRealSongQueueEntries();

            if (realQueueEntries.Count == 0)
            {
                queueResultsContainer.Add(CreateEmptyState("🎵", "Queue is empty", "Add songs from Search or the Companion App."));
                lastRenderedQueueCount = 0;
                return;
            }

            queueRowElements.Clear();
            VisualElement queueSection = CreatePremiumPanelSection("Upcoming Queue", realQueueEntries.Count + (realQueueEntries.Count == 1 ? " song queued" : " songs queued"));

            for (int i = 0; i < realQueueEntries.Count; i++)
            {
                int index = i;
                object entry = realQueueEntries[index];
                string displayName = GetSongQueueEntryDisplayName(entry);
                string playerMicText = GetSongQueueEntryPlayerMicText(entry);
                SongMeta queueSongMetaForAlbumArt = FindSongMetaForQueueEntry(entry);
                VisualElement row = CreatePanelRow();
                row.style.paddingTop = 9;
                row.style.paddingBottom = 9;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                if (queueChangeAnimationPending && index == realQueueEntries.Count - 1)
                {
                    row.style.backgroundColor = GetRowPulseColor();
                    VisualElement pulseRow = row;
                    AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.45f, () =>
                    {
                        if (pulseRow != null)
                        {
                            pulseRow.style.backgroundColor = GetRowColor();
                        }
                    });
                }

                Label dragHandle = CreatePanelLabel("≡");
                dragHandle.style.marginRight = 12;
                dragHandle.style.fontSize = 24;
                dragHandle.style.color = GetAccentColor();
                row.Add(dragHandle);

                if (modSettings.ShowAlbumArtInQueue)
                {
                    row.Add(CreateAlbumArtElement(queueSongMetaForAlbumArt));
                }

                VisualElement textColumn = new VisualElement();
                textColumn.style.flexDirection = FlexDirection.Column;
                textColumn.style.flexGrow = 1f;

                Label label = CreatePanelLabel((index + 1) + ". " + displayName);
                label.style.flexGrow = 1f;
                label.style.marginBottom = 2;
                textColumn.Add(label);

                VisualElement playerMicElement = CreateSongQueueEntryPlayerMicElement(entry);
                if (playerMicElement != null)
                {
                    textColumn.Add(playerMicElement);
                }
                else if (!string.IsNullOrWhiteSpace(playerMicText))
                {
                    Label playerMicLabel = CreatePanelLabel("🎤 " + playerMicText);
                    playerMicLabel.style.fontSize = 14;
                    playerMicLabel.style.color = new Color(0.8f, 0.9f, 1f, 0.95f);
                    textColumn.Add(playerMicLabel);
                }

                row.Add(textColumn);
                row.Add(CreateFavoriteStarButton(queueSongMetaForAlbumArt, () => UpdateQueuePanel()));
                row.Add(CreateQueueMoveHoldButton("↑", "⇧", "⇈", index, -1, row));
                row.Add(CreateQueueMoveHoldButton("↓", "⇩", "⇊", index, 1, row));
                row.Add(CreateSmallPanelButton("❌", () => RemoveRealQueueItem(index)));

                dragHandle.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }
                    draggingQueueIndex = index;
                    currentQueueDropIndex = index;
                    row.style.opacity = 0.55f;
                    CreateQueueDragGhost(displayName, evt.position);
                    HighlightQueueDropTarget(index);
                    try { dragHandle.CapturePointer(evt.pointerId); } catch { }
                    evt.StopPropagation();
                });

                dragHandle.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (draggingQueueIndex < 0)
                    {
                        return;
                    }
                    UpdateQueueDragGhost(evt.position);
                    int targetIndex = FindQueueDropIndex(evt.position);
                    if (targetIndex >= 0 && targetIndex != currentQueueDropIndex)
                    {
                        currentQueueDropIndex = targetIndex;
                        HighlightQueueDropTarget(targetIndex);
                    }
                    evt.StopPropagation();
                });

                dragHandle.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (draggingQueueIndex < 0)
                    {
                        return;
                    }

                    int sourceIndex = draggingQueueIndex;
                    int targetIndex = FindQueueDropIndex(evt.position);
                    draggingQueueIndex = -1;
                    currentQueueDropIndex = -1;
                    row.style.opacity = 1f;
                    RemoveQueueDragGhost();
                    ClearQueueDropHighlights();
                    try { dragHandle.ReleasePointer(evt.pointerId); } catch { }

                    if (targetIndex >= 0 && targetIndex != sourceIndex)
                    {
                        MoveRealQueueItemTo(sourceIndex, targetIndex);
                    }
                    evt.StopPropagation();
                });

                queueSection.Add(row);
                queueRowElements.Add(row);
            }

            queueResultsContainer.Add(queueSection);
            queueChangeAnimationPending = false;
            lastRenderedQueueCount = realQueueEntries.Count;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not read real Melody Mania queue: {ex.Message}");
            queueResultsContainer.Add(CreatePanelLabel("Could not read queue. Check Player.log."));
        }
    }

    private static bool betterJukeboxLoggedSongQueueManagerApi;

    private void LogSongQueueManagerApiOnce()
    {
        if (betterJukeboxLoggedSongQueueManagerApi || songQueueManager == null)
        {
            return;
        }

        betterJukeboxLoggedSongQueueManagerApi = true;
        Type type = songQueueManager.GetType();
    }

    private List<object> GetRealSongQueueEntries()
    {
        List<object> resultList = new List<object>();
        if (songQueueManager == null)
        {
            return resultList;
        }

        object result = null;
        Type type = songQueueManager.GetType();

        System.Reflection.MethodInfo methodInfo = type.GetMethod("GetSongQueueEntries", new Type[0]);
        if (methodInfo != null)
        {
            result = methodInfo.Invoke(songQueueManager, null);
        }

        if (result == null)
        {
            System.Reflection.PropertyInfo propertyInfo = type.GetProperty("SongQueueEntryDtos")
                ?? type.GetProperty("SongQueueEntries")
                ?? type.GetProperty("QueueEntries")
                ?? type.GetProperty("Entries");
            if (propertyInfo != null)
            {
                result = propertyInfo.GetValue(songQueueManager, null);
            }
        }

        if (result == null)
        {
            var fieldInfo = type.GetField("songQueueEntryDtos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? type.GetField("allSongQueueEntries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fieldInfo != null)
            {
                result = fieldInfo.GetValue(songQueueManager);
            }
        }

        System.Collections.IEnumerable enumerable = result as System.Collections.IEnumerable;
        if (enumerable == null || result is string)
        {
            if (result != null)
            {
                resultList.Add(result);
            }
            return resultList;
        }

        foreach (object item in enumerable)
        {
            if (item != null)
            {
                resultList.Add(item);
            }
        }
        return resultList;
    }

    private string GetSongQueueEntryDisplayName(object entry)
    {
        if (entry == null)
        {
            return "<null>";
        }

        try
        {
            string displayNameOverride;
            if (queueEntryDisplayNameOverrides.TryGetValue(entry, out displayNameOverride)
                && !string.IsNullOrWhiteSpace(displayNameOverride))
            {
                return displayNameOverride;
            }

            SongMeta songMeta = FindSongMetaForQueueEntry(entry);
            if (songMeta != null)
            {
                return songMeta.GetArtistDashTitle();
            }

            // Fallback for debug builds: show useful queue ids instead of object type names.
            SongMeta byQueueEntryOverride;
            if (queueEntrySongMetaOverrides.TryGetValue(entry, out byQueueEntryOverride) && byQueueEntryOverride != null)
            {
                return byQueueEntryOverride.GetArtistDashTitle();
            }

            List<string> ids = GetSongQueueEntrySongIds(entry);
            if (ids.Count > 0)
            {
                string firstUsefulId = ids.FirstOrDefault(it => IsProbablySongIdText(it));
                if (!string.IsNullOrWhiteSpace(firstUsefulId))
                {
                    return "Song " + firstUsefulId;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Failed to format queue entry: {ex.Message}");
        }

        return "Queued song";
    }

    private SongMeta FindSongMetaForQueueEntry(object entry)
    {
        if (entry == null)
        {
            return null;
        }

        if (entry is SongMeta)
        {
            return (SongMeta)entry;
        }

        object directSongMeta = GetFirstMemberValue(entry, new string[] { "SongMeta", "Song", "SongMetaDto", "SongDto" });
        if (directSongMeta is SongMeta)
        {
            return (SongMeta)directSongMeta;
        }

        List<string> ids = GetSongQueueEntrySongIds(entry);
        if (ids.Count == 0)
        {
            SongMeta byDisplayNameOverrideEarly = FindSongMetaByQueueDisplayNameOverride(entry);
            if (byDisplayNameOverrideEarly != null)
            {
                return byDisplayNameOverrideEarly;
            }
            return null;
        }

        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "System.Object")
            {
                continue;
            }

            SongMeta byManager = TryGetSongMetaByAnyId(id);
            if (byManager != null)
            {
                return byManager;
            }
        }

        List<SongMeta> allSongMetas = null;
        try
        {
            allSongMetas = songMetaManager.GetSongMetas().ToList();
        }
        catch
        {
            allSongMetas = new List<SongMeta>();
        }

        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "System.Object")
            {
                continue;
            }

            foreach (SongMeta songMeta in allSongMetas)
            {
                string songMetaId = GetSongMetaId(songMeta);
                if (!string.IsNullOrWhiteSpace(songMetaId) && string.Equals(songMetaId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return songMeta;
                }
            }
        }

        SongMeta byDisplayNameOverride = FindSongMetaByQueueDisplayNameOverride(entry);
        if (byDisplayNameOverride != null)
        {
            return byDisplayNameOverride;
        }

        return null;
    }

    private SongMeta FindSongMetaByQueueDisplayNameOverride(object entry)
    {
        if (entry == null)
        {
            return null;
        }

        string displayNameOverride;
        if (!queueEntryDisplayNameOverrides.TryGetValue(entry, out displayNameOverride)
            || string.IsNullOrWhiteSpace(displayNameOverride))
        {
            return null;
        }

        List<SongMeta> allSongMetas = null;
        try
        {
            allSongMetas = songMetaManager.GetSongMetas().ToList();
        }
        catch
        {
            return null;
        }

        return allSongMetas.FirstOrDefault(songMeta => songMeta != null
            && string.Equals(songMeta.GetArtistDashTitle(), displayNameOverride, StringComparison.OrdinalIgnoreCase));
    }


    private SongMeta TryGetSongMetaByAnyId(string id)
    {
        if (songMetaManager == null || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string[] methodNames = new string[]
        {
            "TryGetSongMetaByGloballyUniqueId",
            "TryGetSongMetaByLocallyUniqueId",
            "GetSongMetaByGloballyUniqueId",
            "GetSongMetaByLocallyUniqueId"
        };

        Type managerType = songMetaManager.GetType();
        foreach (string methodName in methodNames)
        {
            foreach (System.Reflection.MethodInfo methodInfo in managerType.GetMethods().Where(it => it.Name == methodName))
            {
                System.Reflection.ParameterInfo[] parameters = methodInfo.GetParameters();
                try
                {
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        object value = methodInfo.Invoke(songMetaManager, new object[] { id });
                        if (value is SongMeta)
                        {
                            return (SongMeta)value;
                        }
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].IsOut)
                    {
                        object[] args = new object[] { id, null };
                        object success = methodInfo.Invoke(songMetaManager, args);
                        if (success is bool && (bool)success && args[1] is SongMeta)
                        {
                            return (SongMeta)args[1];
                        }
                    }
                }
                catch { }
            }
        }
        return null;
    }

    private List<string> GetSongQueueEntrySongIds(object entry)
    {
        List<string> result = new List<string>();
        AddSongQueueEntrySongIds(entry, result, new List<object>(), 0);
        return result.Distinct().Where(it => IsProbablySongIdText(it)).ToList();
    }

    private void AddSongQueueEntrySongIds(object obj, List<string> result, List<object> visited, int depth)
    {
        if (obj == null || result == null || depth > 6)
        {
            return;
        }

        if (obj is string)
        {
            string text = (string)obj;
            if (IsProbablySongIdText(text))
            {
                result.Add(text);
            }
            return;
        }

        Type objectType = obj.GetType();
        if (objectType.IsPrimitive || obj is decimal || obj is DateTime || obj is UnityEngine.Object)
        {
            return;
        }

        if (visited.Contains(obj))
        {
            return;
        }
        visited.Add(obj);

        if (obj is SongMeta)
        {
            string id = GetSongMetaId((SongMeta)obj);
            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add(id);
            }
            return;
        }

        string[] idMemberNames = new string[]
        {
            "GloballyUniqueSongMetaIds", "GloballyUniqueSongMetaId", "GloballyUniqueSongId",
            "SongMetaIds", "SongMetaId", "SongIds", "SongId", "LocallyUniqueSongId"
        };

        foreach (string memberName in idMemberNames)
        {
            object value = GetMemberValue(obj, memberName);
            AddSongQueueIdValue(value, result);
        }

        // Important: SongQueueEntryDto from playshared hides the song ids inside nested DTOs.
        // Walk all fields/properties instead of guessing only one property name.
        try
        {
            foreach (System.Reflection.PropertyInfo prop in objectType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object value = null;
                try { value = prop.GetValue(obj, null); } catch { }
                if (value == null || object.ReferenceEquals(value, obj))
                {
                    continue;
                }

                if (prop.Name.IndexOf("GloballyUnique", StringComparison.OrdinalIgnoreCase) >= 0
                    || prop.Name.IndexOf("SongMeta", StringComparison.OrdinalIgnoreCase) >= 0
                    || prop.Name.IndexOf("Song", StringComparison.OrdinalIgnoreCase) >= 0
                    || prop.Name.IndexOf("SceneData", StringComparison.OrdinalIgnoreCase) >= 0
                    || prop.Name.IndexOf("Entry", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddSongQueueIdValue(value, result);
                    AddSongQueueEntrySongIds(value, result, visited, depth + 1);
                }
                else if (depth < 3 && !(value is string))
                {
                    AddSongQueueEntrySongIds(value, result, visited, depth + 1);
                }
            }
        }
        catch { }

        try
        {
            foreach (var field in objectType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                object value = null;
                try { value = field.GetValue(obj); } catch { }
                if (value == null || object.ReferenceEquals(value, obj))
                {
                    continue;
                }

                if (field.Name.IndexOf("GloballyUnique", StringComparison.OrdinalIgnoreCase) >= 0
                    || field.Name.IndexOf("SongMeta", StringComparison.OrdinalIgnoreCase) >= 0
                    || field.Name.IndexOf("Song", StringComparison.OrdinalIgnoreCase) >= 0
                    || field.Name.IndexOf("SceneData", StringComparison.OrdinalIgnoreCase) >= 0
                    || field.Name.IndexOf("Entry", StringComparison.OrdinalIgnoreCase) >= 0
                    || field.Name.IndexOf("BackingField", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddSongQueueIdValue(value, result);
                    AddSongQueueEntrySongIds(value, result, visited, depth + 1);
                }
            }
        }
        catch { }
    }

    private void AddSongQueueIdValue(object value, List<string> result)
    {
        if (value == null || result == null)
        {
            return;
        }

        if (value is bool || value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal || value is DateTime)
        {
            return;
        }

        if (value is string)
        {
            string text = (string)value;
            if (IsProbablySongIdText(text))
            {
                result.Add(text);
            }
            return;
        }

        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable != null)
        {
            foreach (object item in enumerable)
            {
                AddSongQueueIdValue(item, result);
            }
            return;
        }

        // Do not use value.ToString() for arbitrary DTO objects.
        // It can produce values like SingScenePlayerDataDto, System.Object or SongQueueEntryDto,
        // which are not song ids and break the queue display.
    }

    private bool IsProbablySongIdText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("System.Object", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Queued song", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("SongQueueEntryDto", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("Dto", StringComparison.OrdinalIgnoreCase)
            || trimmed.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
            || trimmed.IndexOf("Mic", StringComparison.OrdinalIgnoreCase) >= 0
            || trimmed.IndexOf("System.", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        // Real song ids are normally long hashes/paths/UUID-like strings.
        // Avoid treating short debug values or booleans as song ids.
        if (trimmed.Length < 8)
        {
            return false;
        }

        return true;
    }

    private object GetFirstMemberValue(object obj, string[] memberNames)
    {
        if (obj == null || memberNames == null)
        {
            return null;
        }
        foreach (string memberName in memberNames)
        {
            object value = GetMemberValue(obj, memberName);
            if (value != null)
            {
                return value;
            }
        }
        return null;
    }

    private object GetMemberValue(object obj, string memberName)
    {
        if (obj == null)
        {
            return null;
        }

        Type type = obj.GetType();
        System.Reflection.PropertyInfo propertyInfo = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (propertyInfo != null && propertyInfo.CanRead && propertyInfo.GetIndexParameters().Length == 0)
        {
            try { return propertyInfo.GetValue(obj, null); } catch { }
        }

        var fieldInfo = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (fieldInfo != null)
        {
            try { return fieldInfo.GetValue(obj); } catch { }
        }

        return null;
    }

    private string GetReadableSongValue(object value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is SongMeta)
        {
            return ((SongMeta)value).GetArtistDashTitle();
        }

        if (value is string)
        {
            return (string)value;
        }

        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable != null && !(value is string))
        {
            List<string> items = new List<string>();
            foreach (object item in enumerable)
            {
                string itemText = GetReadableSongValue(item);
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    items.Add(itemText);
                }
            }
            return items.Count > 0 ? string.Join(", ", items.Take(3).ToArray()) : null;
        }

        object artist = GetMemberValue(value, "Artist");
        object title = GetMemberValue(value, "Title");
        if (artist != null || title != null)
        {
            return ((artist ?? "").ToString() + " - " + (title ?? "").ToString()).Trim(' ', '-');
        }

        object name = GetMemberValue(value, "Name") ?? GetMemberValue(value, "DisplayName");
        if (name != null)
        {
            return name.ToString();
        }

        return null;
    }



    private void CreateQueueDragGhost(string displayName, Vector2 pointerPosition)
    {
        RemoveQueueDragGhost();
        if (queuePanel == null)
        {
            return;
        }

        queueDragGhost = new VisualElement();
        queueDragGhost.name = "betterJukeboxQueueDragGhost";
        queueDragGhost.style.position = Position.Absolute;
        queueDragGhost.style.left = new StyleLength(new Length(Mathf.Max(8, pointerPosition.x - queuePanel.worldBound.x + 16), LengthUnit.Pixel));
        queueDragGhost.style.top = new StyleLength(new Length(Mathf.Max(8, pointerPosition.y - queuePanel.worldBound.y + 8), LengthUnit.Pixel));
        queueDragGhost.style.paddingLeft = 12;
        queueDragGhost.style.paddingRight = 12;
        queueDragGhost.style.paddingTop = 8;
        queueDragGhost.style.paddingBottom = 8;
        queueDragGhost.style.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.96f);
        queueDragGhost.style.borderTopLeftRadius = 10;
        queueDragGhost.style.borderTopRightRadius = 10;
        queueDragGhost.style.borderBottomLeftRadius = 10;
        queueDragGhost.style.borderBottomRightRadius = 10;
        queueDragGhost.pickingMode = PickingMode.Ignore;

        Label label = CreatePanelLabel("↕ " + displayName);
        label.style.marginRight = new StyleLength(new Length(0, LengthUnit.Pixel));
        queueDragGhost.Add(label);
        queuePanel.Add(queueDragGhost);
    }

    private void UpdateQueueDragGhost(Vector2 pointerPosition)
    {
        if (queueDragGhost == null || queuePanel == null)
        {
            return;
        }
        queueDragGhost.style.left = new StyleLength(new Length(Mathf.Max(8, pointerPosition.x - queuePanel.worldBound.x + 16), LengthUnit.Pixel));
        queueDragGhost.style.top = new StyleLength(new Length(Mathf.Max(8, pointerPosition.y - queuePanel.worldBound.y + 8), LengthUnit.Pixel));
    }

    private void RemoveQueueDragGhost()
    {
        if (queueDragGhost != null)
        {
            queueDragGhost.RemoveFromHierarchy();
            queueDragGhost = null;
        }
    }

    private void HighlightQueueDropTarget(int targetIndex)
    {
        for (int i = 0; i < queueRowElements.Count; i++)
        {
            VisualElement row = queueRowElements[i];
            if (row == null)
            {
                continue;
            }
            if (i == targetIndex)
            {
                row.style.backgroundColor = new Color(0.12f, 0.22f, 0.38f, 0.72f);
            }
            else
            {
                row.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            }
        }
    }

    private void ClearQueueDropHighlights()
    {
        for (int i = 0; i < queueRowElements.Count; i++)
        {
            VisualElement row = queueRowElements[i];
            if (row != null)
            {
                row.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            }
        }
    }

    private int FindQueueDropIndex(Vector2 pointerPosition)
    {
        if (queueRowElements == null || queueRowElements.Count == 0)
        {
            return -1;
        }

        for (int i = 0; i < queueRowElements.Count; i++)
        {
            VisualElement row = queueRowElements[i];
            if (row != null && row.worldBound.Contains(pointerPosition))
            {
                return i;
            }
        }

        // If released between rows, use nearest row by vertical center.
        float bestDistance = float.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < queueRowElements.Count; i++)
        {
            VisualElement row = queueRowElements[i];
            if (row == null)
            {
                continue;
            }
            float centerY = row.worldBound.y + row.worldBound.height / 2f;
            float distance = Mathf.Abs(pointerPosition.y - centerY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }


    private VisualElement CreateSongQueueEntryPlayerMicElement(object entry)
    {
        List<QueuePlayerMicInfo> infos = GetSongQueueEntryPlayerMicInfos(entry);
        if (infos.Count == 0)
        {
            return null;
        }

        VisualElement container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.flexWrap = Wrap.Wrap;
        container.style.marginTop = 2;

        foreach (QueuePlayerMicInfo info in infos.Take(4))
        {
            VisualElement playerMicEntry = CreateNativePlayerMicEntry(info);
            if (playerMicEntry != null)
            {
                container.Add(playerMicEntry);
            }
        }
        return container;
    }

    private VisualElement CreateNativePlayerMicEntry(QueuePlayerMicInfo info)
    {
        if (info == null || nextGameRoundInfoPlayerEntryUi == null)
        {
            return null;
        }

        try
        {
            VisualElement root = nextGameRoundInfoPlayerEntryUi.CloneTree().Children().FirstOrDefault();
            if (root == null)
            {
                return null;
            }

            root.style.marginRight = 12f;
            root.style.marginTop = 2f;
            root.style.marginBottom = 2f;
            root.pickingMode = PickingMode.Ignore;

            VisualElement micIcon = root.Q("nextGameRoundPlayerEntryMicImage");
            if (micIcon != null)
            {
                micIcon.style.display = DisplayStyle.Flex;
                if (info.HasMicColor)
                {
                    Color visualMicColor = info.MicColor;
                    visualMicColor.a = 1f;
                    micIcon.style.color = visualMicColor;
                    micIcon.style.unityBackgroundImageTintColor = visualMicColor;
                }
                micIcon.style.width = 16f;
                micIcon.style.height = 16f;
                micIcon.style.marginRight = 5f;
            }

            Label playerLabel = root.Q<Label>("nextGameRoundPlayerEntryLabel");
            if (playerLabel != null)
            {
                playerLabel.text = info.PlayerName;
                playerLabel.AddToClassList("tinyFont");
                playerLabel.AddToClassList("textShadow");
                playerLabel.style.fontSize = 14f;
                playerLabel.style.color = new Color(0.8f, 0.9f, 1f, 0.95f);
            }

            return root;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not clone native mic icon UI: {ex.Message}");
            return null;
        }
    }

    private class QueuePlayerMicInfo
    {
        public string PlayerName;
        public bool HasMicColor;
        public Color MicColor;
    }

    private List<QueuePlayerMicInfo> GetSongQueueEntryPlayerMicInfos(object entry)
    {
        List<QueuePlayerMicInfo> result = new List<QueuePlayerMicInfo>();
        if (entry == null)
        {
            return result;
        }

        try
        {
            object playerData = GetMemberValue(entry, "SingScenePlayerData")
                ?? GetMemberValue(entry, "SingScenePlayerDataDto")
                ?? GetMemberValue(entry, "PlayerData");

            object micMap = GetMemberValue(entry, "PlayerProfileToMicProfileMap")
                ?? GetMemberValue(playerData, "PlayerProfileToMicProfileMap")
                ?? GetMemberValue(playerData, "PlayerProfileNameToMicProfileDto")
                ?? GetMemberValue(playerData, "PlayerProfileNameToMicProfileMap");

            System.Collections.IEnumerable mapEnumerable = micMap as System.Collections.IEnumerable;
            if (mapEnumerable != null && !(micMap is string))
            {
                foreach (object item in mapEnumerable)
                {
                    object key = GetMemberValue(item, "Key");
                    object val = GetMemberValue(item, "Value");
                    string playerName = GetReadableName(key);
                    if (string.IsNullOrWhiteSpace(playerName))
                    {
                        playerName = GetReadableName(item);
                    }
                    if (string.IsNullOrWhiteSpace(playerName))
                    {
                        continue;
                    }

                    QueuePlayerMicInfo info = new QueuePlayerMicInfo();
                    info.PlayerName = playerName;
                    Color color;
                    if (TryGetMicColor(val, out color) || TryGetMicColor(item, out color))
                    {
                        info.HasMicColor = true;
                        info.MicColor = color;
                    }
                    else
                    {
                    }
                    result.Add(info);
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            object selectedPlayers = GetMemberValue(entry, "SelectedPlayerProfiles")
                ?? GetMemberValue(playerData, "SelectedPlayerProfiles")
                ?? GetMemberValue(playerData, "PlayerProfiles")
                ?? GetMemberValue(playerData, "PlayerProfileNames");

            foreach (string player in GetReadableListItems(selectedPlayers).Take(4))
            {
                QueuePlayerMicInfo info = new QueuePlayerMicInfo();
                info.PlayerName = player;
                result.Add(info);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Failed to read player/mic visual info: {ex.Message}");
        }
        return result;
    }

    private static List<string> loggedMicProfileShapes = new List<string>();

    private void LogMicProfileShapeOnce(string playerName, object micProfile)
    {
        if (micProfile == null)
        {
            return;
        }
        try
        {
            Type type = micProfile.GetType();
            string key = type.FullName + ":" + playerName;
            if (loggedMicProfileShapes.Contains(key))
            {
                return;
            }
            loggedMicProfileShapes.Add(key);

            string props = string.Join(" | ", type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(prop => prop.GetIndexParameters().Length == 0)
                .Select(prop => prop.Name + ":" + prop.PropertyType.Name + "='" + SafeMemberToString(micProfile, prop.Name) + "'")
                .Take(40).ToArray());
            string fields = string.Join(" | ", type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(field => field.Name + ":" + field.FieldType.Name + "='" + SafeMemberToString(micProfile, field.Name) + "'")
                .Take(40).ToArray());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not log MicProfile shape: {ex.Message}");
        }
    }

    private string SafeMemberToString(object target, string memberName)
    {
        try
        {
            object value = GetMemberValue(target, memberName);
            return value != null ? value.ToString() : "";
        }
        catch
        {
            return "";
        }
    }

    private bool TryGetMicColor(object value, out Color color)
    {
        return TryGetMicColor(value, out color, 0);
    }

    private bool TryGetMicColor(object value, out Color color, int depth)
    {
        color = Color.white;
        if (value == null || depth > 3)
        {
            return false;
        }

        if (value is Color)
        {
            color = (Color)value;
            return true;
        }

        Color numericColor;
        if (TryGetColorFromNumericMembers(value, out numericColor))
        {
            color = numericColor;
            return true;
        }

        string[] colorMemberNames = new string[]
        {
            "Color", "MicColor", "PlayerColor", "ColorValue", "ColorName", "Name",
            "ThemeColor", "ProfileColor", "PlayerProfileColor", "ColorHex", "HexColor",
            "HtmlColor", "RgbColor", "RgbaColor"
        };

        foreach (string memberName in colorMemberNames)
        {
            object colorObject = GetMemberValue(value, memberName);
            if (colorObject == null)
            {
                continue;
            }

            if (colorObject is Color)
            {
                color = (Color)colorObject;
                return true;
            }

            if (TryGetColorFromNumericMembers(colorObject, out numericColor))
            {
                color = numericColor;
                return true;
            }

            string colorText = colorObject.ToString();
            if (TryParseNamedMicColor(colorText, out color) || TryParseColorText(colorText, out color))
            {
                return true;
            }

            if (!(colorObject is string) && TryGetMicColor(colorObject, out color, depth + 1))
            {
                return true;
            }
        }

        string readableName = GetReadableName(value);
        if (TryParseNamedMicColor(readableName, out color) || TryParseColorText(readableName, out color))
        {
            return true;
        }

        string valueText = value.ToString();
        return TryParseNamedMicColor(valueText, out color) || TryParseColorText(valueText, out color);
    }

    private bool TryGetColorFromNumericMembers(object value, out Color color)
    {
        color = Color.white;
        if (value == null)
        {
            return false;
        }

        object rObject = GetMemberValue(value, "r") ?? GetMemberValue(value, "R") ?? GetMemberValue(value, "Red");
        object gObject = GetMemberValue(value, "g") ?? GetMemberValue(value, "G") ?? GetMemberValue(value, "Green");
        object bObject = GetMemberValue(value, "b") ?? GetMemberValue(value, "B") ?? GetMemberValue(value, "Blue");
        object aObject = GetMemberValue(value, "a") ?? GetMemberValue(value, "A") ?? GetMemberValue(value, "Alpha");

        if (rObject == null || gObject == null || bObject == null)
        {
            return false;
        }

        float r;
        float g;
        float b;
        float a = 1f;
        if (!TryConvertToFloat(rObject, out r) || !TryConvertToFloat(gObject, out g) || !TryConvertToFloat(bObject, out b))
        {
            return false;
        }
        if (aObject != null)
        {
            TryConvertToFloat(aObject, out a);
        }

        if (r > 1f || g > 1f || b > 1f || a > 1f)
        {
            r = Mathf.Clamp01(r / 255f);
            g = Mathf.Clamp01(g / 255f);
            b = Mathf.Clamp01(b / 255f);
            a = Mathf.Clamp01(a / 255f);
        }

        color = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
        return true;
    }

    private bool TryConvertToFloat(object value, out float result)
    {
        result = 0f;
        if (value == null)
        {
            return false;
        }
        try
        {
            result = Convert.ToSingle(value);
            return true;
        }
        catch
        {
            return float.TryParse(value.ToString(), out result);
        }
    }

    private bool TryParseColorText(string text, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith("#"))
        {
            return ColorUtility.TryParseHtmlString(trimmed, out color);
        }

        int hashIndex = trimmed.IndexOf("#", StringComparison.Ordinal);
        if (hashIndex >= 0 && hashIndex + 7 <= trimmed.Length)
        {
            string hashText = trimmed.Substring(hashIndex, Math.Min(9, trimmed.Length - hashIndex));
            if (ColorUtility.TryParseHtmlString(hashText, out color))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryParseNamedMicColor(string text, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string lower = text.ToLowerInvariant();
        if (lower.Contains("blue") || lower.Contains("blå")) { color = new Color(0.2f, 0.55f, 1f, 1f); return true; }
        if (lower.Contains("red") || lower.Contains("röd")) { color = new Color(1f, 0.22f, 0.22f, 1f); return true; }
        if (lower.Contains("green") || lower.Contains("grön")) { color = new Color(0.2f, 0.9f, 0.35f, 1f); return true; }
        if (lower.Contains("yellow") || lower.Contains("gul")) { color = new Color(1f, 0.85f, 0.15f, 1f); return true; }
        if (lower.Contains("orange")) { color = new Color(1f, 0.5f, 0.15f, 1f); return true; }
        if (lower.Contains("purple") || lower.Contains("violet") || lower.Contains("lila")) { color = new Color(0.75f, 0.35f, 1f, 1f); return true; }
        if (lower.Contains("pink") || lower.Contains("rosa")) { color = new Color(1f, 0.35f, 0.75f, 1f); return true; }
        if (lower.Contains("cyan") || lower.Contains("turquoise") || lower.Contains("turkos")) { color = new Color(0.15f, 0.9f, 1f, 1f); return true; }
        if (lower.Contains("white") || lower.Contains("vit")) { color = Color.white; return true; }
        return false;
    }

    private string GetSongQueueEntryPlayerMicText(object entry)
    {
        if (entry == null)
        {
            return null;
        }

        try
        {
            object playerData = GetMemberValue(entry, "SingScenePlayerData")
                ?? GetMemberValue(entry, "SingScenePlayerDataDto")
                ?? GetMemberValue(entry, "PlayerData");

            object selectedPlayers = GetMemberValue(entry, "SelectedPlayerProfiles")
                ?? GetMemberValue(playerData, "SelectedPlayerProfiles")
                ?? GetMemberValue(playerData, "PlayerProfiles")
                ?? GetMemberValue(playerData, "PlayerProfileNames");

            object micMap = GetMemberValue(entry, "PlayerProfileToMicProfileMap")
                ?? GetMemberValue(playerData, "PlayerProfileToMicProfileMap")
                ?? GetMemberValue(playerData, "PlayerProfileNameToMicProfileDto")
                ?? GetMemberValue(playerData, "PlayerProfileNameToMicProfileMap");

            List<string> players = GetReadableListItems(selectedPlayers);
            List<string> micPairs = GetReadableMapItems(micMap);

            if (micPairs.Count > 0)
            {
                return string.Join("   |   ", micPairs.Take(3).ToArray());
            }

            if (players.Count > 0)
            {
                return string.Join(", ", players.Take(3).ToArray());
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Failed to read player/mic queue info: {ex.Message}");
        }

        return null;
    }

    private List<string> GetReadableListItems(object value)
    {
        List<string> result = new List<string>();
        if (value == null || value is string)
        {
            string single = GetReadableName(value);
            if (!string.IsNullOrWhiteSpace(single))
            {
                result.Add(single);
            }
            return result;
        }

        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable == null)
        {
            string single = GetReadableName(value);
            if (!string.IsNullOrWhiteSpace(single))
            {
                result.Add(single);
            }
            return result;
        }

        foreach (object item in enumerable)
        {
            string itemText = GetReadableName(item);
            if (!string.IsNullOrWhiteSpace(itemText))
            {
                result.Add(itemText);
            }
        }
        return result;
    }

    private List<string> GetReadableMapItems(object value)
    {
        List<string> result = new List<string>();
        if (value == null)
        {
            return result;
        }

        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable == null || value is string)
        {
            return result;
        }

        foreach (object item in enumerable)
        {
            object key = GetMemberValue(item, "Key");
            object val = GetMemberValue(item, "Value");
            string keyText = GetReadableName(key);
            string valText = GetReadableName(val);
            if (!string.IsNullOrWhiteSpace(keyText) || !string.IsNullOrWhiteSpace(valText))
            {
                if (!string.IsNullOrWhiteSpace(keyText) && !string.IsNullOrWhiteSpace(valText))
                {
                    result.Add(keyText + " - " + valText);
                }
                else
                {
                    result.Add(keyText + valText);
                }
            }
        }
        return result;
    }

    private string GetReadableName(object value)
    {
        if (value == null)
        {
            return null;
        }
        if (value is string)
        {
            return (string)value;
        }

        object name = GetMemberValue(value, "Name")
            ?? GetMemberValue(value, "DisplayName")
            ?? GetMemberValue(value, "PlayerProfileName")
            ?? GetMemberValue(value, "MicProfileName")
            ?? GetMemberValue(value, "Id");

        if (name != null)
        {
            return name.ToString();
        }
        return value.ToString();
    }



    private class BetterJukeboxPlaylist
    {
        public string Name;
        public readonly List<string> SongIds = new List<string>();
        public readonly List<string> AddedSongIds = new List<string>();
    }

    private string GetPlaylistsPath()
    {
        return System.IO.Path.Combine(GetBetterJukeboxPersistentDirectory(), "Playlists.json");
    }

    private void EnsurePlaylistsFileExists()
    {
        try
        {
            string directory = GetBetterJukeboxPersistentDirectory();
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            string path = GetPlaylistsPath();
            if (!System.IO.File.Exists(path))
            {
                System.IO.File.WriteAllText(path, "[]");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not create Playlists.json: " + ex.Message);
        }
    }

    private void LoadPlaylists()
    {
        if (betterJukeboxPlaylistsLoaded)
        {
            return;
        }
        betterJukeboxPlaylistsLoaded = true;
        betterJukeboxPlaylists.Clear();
        try
        {
            EnsurePlaylistsFileExists();
            string text = System.IO.File.ReadAllText(GetPlaylistsPath());
            int index = 0;
            while (index >= 0 && index < text.Length)
            {
                int nameKey = text.IndexOf("\"name\"", index, StringComparison.OrdinalIgnoreCase);
                if (nameKey < 0) { break; }
                int colon = text.IndexOf(':', nameKey);
                int quote1 = text.IndexOf('"', colon + 1);
                int quote2 = FindJsonStringEnd(text, quote1 + 1);
                if (colon < 0 || quote1 < 0 || quote2 < 0) { break; }
                BetterJukeboxPlaylist playlist = new BetterJukeboxPlaylist();
                playlist.Name = UnescapeJsonString(text.Substring(quote1 + 1, quote2 - quote1 - 1));
                int songsKey = text.IndexOf("\"songs\"", quote2, StringComparison.OrdinalIgnoreCase);
                int arrayStart = songsKey >= 0 ? text.IndexOf('[', songsKey) : -1;
                int arrayEnd = arrayStart >= 0 ? text.IndexOf(']', arrayStart) : -1;
                if (arrayStart >= 0 && arrayEnd > arrayStart)
                {
                    string songsText = text.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                    string[] parts = songsText.Split(new char[] { '\n', '\r', ',', '"' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string part in parts)
                    {
                        string id = NormalizeFavoriteIdText(part);
                        if (!string.IsNullOrWhiteSpace(id) && !playlist.SongIds.Contains(id))
                        {
                            playlist.SongIds.Add(id);
                        }
                    }

                    int addedKey = text.IndexOf("\"added\"", arrayEnd, StringComparison.OrdinalIgnoreCase);
                    int nextNameKey = text.IndexOf("\"name\"", arrayEnd, StringComparison.OrdinalIgnoreCase);
                    if (addedKey >= 0 && (nextNameKey < 0 || addedKey < nextNameKey))
                    {
                        int addedArrayStart = text.IndexOf('[', addedKey);
                        int addedArrayEnd = addedArrayStart >= 0 ? text.IndexOf(']', addedArrayStart) : -1;
                        if (addedArrayStart >= 0 && addedArrayEnd > addedArrayStart)
                        {
                            string addedText = text.Substring(addedArrayStart + 1, addedArrayEnd - addedArrayStart - 1);
                            string[] addedParts = addedText.Split(new char[] { '\n', '\r', ',', '"' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string part in addedParts)
                            {
                                string id = NormalizeFavoriteIdText(part);
                                if (!string.IsNullOrWhiteSpace(id) && !playlist.AddedSongIds.Contains(id))
                                {
                                    playlist.AddedSongIds.Add(id);
                                }
                            }
                            arrayEnd = addedArrayEnd;
                        }
                    }
                    if (playlist.AddedSongIds.Count == 0)
                    {
                        playlist.AddedSongIds.AddRange(playlist.SongIds);
                    }
                    index = arrayEnd + 1;
                }
                else
                {
                    index = quote2 + 1;
                }
                if (!string.IsNullOrWhiteSpace(playlist.Name) && FindPlaylist(playlist.Name) == null)
                {
                    betterJukeboxPlaylists.Add(playlist);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not load Playlists.json: " + ex.Message);
        }
    }

    private int FindJsonStringEnd(string text, int start)
    {
        bool escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') { return i; }
        }
        return -1;
    }

    private string UnescapeJsonString(string text)
    {
        if (text == null) { return ""; }
        return text.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private string EscapeJsonString(string text)
    {
        return (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void SavePlaylists()
    {
        try
        {
            EnsurePlaylistsFileExists();
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("[");
            for (int i = 0; i < betterJukeboxPlaylists.Count; i++)
            {
                BetterJukeboxPlaylist playlist = betterJukeboxPlaylists[i];
                builder.AppendLine("  {");
                builder.Append("    \"name\": \""); builder.Append(EscapeJsonString(playlist.Name)); builder.AppendLine("\",");
                builder.AppendLine("    \"songs\": [");
                for (int j = 0; j < playlist.SongIds.Count; j++)
                {
                    builder.Append("      \""); builder.Append(EscapeJsonString(playlist.SongIds[j])); builder.Append("\"");
                    if (j < playlist.SongIds.Count - 1) { builder.Append(","); }
                    builder.AppendLine();
                }
                builder.AppendLine("    ],");
                builder.AppendLine("    \"added\": [");
                for (int j = 0; j < playlist.AddedSongIds.Count; j++)
                {
                    builder.Append("      \""); builder.Append(EscapeJsonString(playlist.AddedSongIds[j])); builder.Append("\"");
                    if (j < playlist.AddedSongIds.Count - 1) { builder.Append(","); }
                    builder.AppendLine();
                }
                builder.AppendLine("    ]");
                builder.Append("  }");
                if (i < betterJukeboxPlaylists.Count - 1) { builder.Append(","); }
                builder.AppendLine();
            }
            builder.AppendLine("]");
            System.IO.File.WriteAllText(GetPlaylistsPath(), builder.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not save Playlists.json: " + ex.Message);
        }
    }

    private BetterJukeboxPlaylist FindPlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return null; }
        foreach (BetterJukeboxPlaylist playlist in betterJukeboxPlaylists)
        {
            if (playlist != null && string.Equals(playlist.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return playlist;
            }
        }
        return null;
    }

    private string GetPlaylistsFilterButtonText()
    {
        LoadPlaylists();
        return betterJukeboxPlaylists.Count > 0 ? "🎵 Playlists (" + betterJukeboxPlaylists.Count + ")" : "🎵 Playlists";
    }

    private void UpdatePlaylistActionRowVisibility()
    {
        if (searchPlaylistsActionRow == null) { return; }
        searchPlaylistsActionRow.Clear();
        if (!showOnlyPlaylistSearchResults || multiSelectMode)
        {
            searchPlaylistsActionRow.style.display = DisplayStyle.None;
            return;
        }
        searchPlaylistsActionRow.style.display = DisplayStyle.Flex;
        if (string.IsNullOrWhiteSpace(selectedPlaylistName))
        {
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("New Playlist", () => ShowPlaylistNameDialog("New Playlist", "Create", null, name => CreatePlaylist(name))));
        }
        else
        {
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("‹ Playlists", () => { CancelMultiSelectForNavigation(); selectedPlaylistName = null; UpdatePlaylistActionRowVisibility(); UpdateSearchResults(searchTextField != null ? searchTextField.value : ""); }));
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("Queue All", QueueSelectedPlaylist));
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("Shuffle Playlist", ShuffleSelectedPlaylist));
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("Sort A-Z", SortSelectedPlaylistAlphabetically));
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("Sort Recently Added", SortSelectedPlaylistRecentlyAdded));
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("Rename", () => ShowPlaylistNameDialog("Rename Playlist", "Rename", selectedPlaylistName, name => RenameSelectedPlaylist(name))));
            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            searchPlaylistsActionRow.Add(spacer);
            searchPlaylistsActionRow.Add(CreateSmallPanelButton("Delete", DeleteSelectedPlaylist));
        }
    }

    private void UpdatePlaylistFilterButtonText()
    {
        if (searchPlaylistsFilterButton != null)
        {
            SetButtonVisualText(searchPlaylistsFilterButton, GetPlaylistsFilterButtonText());
        }
    }

    private void UpdatePlaylistSearchResults(string searchText)
    {
        LoadPlaylists();
        UpdatePlaylistFilterButtonText();
        UpdatePlaylistActionRowVisibility();
        string query = searchText == null ? "" : searchText.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(selectedPlaylistName))
        {
            if (betterJukeboxPlaylists.Count == 0)
            {
                searchResultsContainer.Add(CreateEmptyState("🎵", "No playlists yet", "Create a playlist or add a song from Search."));
                return;
            }
            VisualElement section = CreatePremiumPanelSection("Playlists", "Create and manage your song lists.");
            List<BetterJukeboxPlaylist> orderedPlaylists = betterJukeboxPlaylists
                .Where(it => it != null)
                .OrderByDescending(it => query.Length > 0 && it.Name != null && it.Name.ToLowerInvariant().IndexOf(query) >= 0)
                .ThenBy(it => it.Name)
                .ToList();
            foreach (BetterJukeboxPlaylist playlist in orderedPlaylists)
            {
                section.Add(CreatePlaylistListRow(playlist));
            }
            searchResultsContainer.Add(section);
            return;
        }

        BetterJukeboxPlaylist selected = FindPlaylist(selectedPlaylistName);
        if (selected == null)
        {
            selectedPlaylistName = null;
            UpdatePlaylistSearchResults(searchText);
            return;
        }

        List<SongMeta> songs = GetPlaylistSongMetas(selected);
        if (query.Length > 0)
        {
            // Keep the special "move matches to the top" behavior only for the playlist overview.
            // Inside a selected playlist, search should behave like the normal song list and filter songs.
            songs = songs
                .Where(songMeta => MatchesSearch(songMeta, query))
                .ToList();
        }
        if (songs.Count == 0)
        {
            searchResultsContainer.Add(CreateEmptyState("🎵", selected.Name, query.Length > 0 ? "No matching songs in this playlist." : "No songs in this playlist yet."));
            return;
        }
        playlistRowElements.Clear();
        playlistRowSongMetas.Clear();
        StartProgressiveSearchRender(selected.Name, songs, true, selected);
    }

    private VisualElement CreatePlaylistListRow(BetterJukeboxPlaylist playlist)
    {
        VisualElement row = CreatePanelRow();
        row.tooltip = "Open playlist";
        row.RegisterCallback<ClickEvent>(evt =>
        {
            VisualElement target = evt.target as VisualElement;
            if (IsInsideButton(target))
            {
                return;
            }
            OpenPlaylist(playlist);
            evt.StopPropagation();
        });

        Label icon = CreatePanelLabel("🎵");
        icon.style.color = GetAccentColor();
        row.Add(icon);
        Label label = CreatePanelLabel(playlist.Name + " (" + playlist.SongIds.Count + ")");
        label.style.flexGrow = 1f;
        row.Add(label);
        row.Add(CreateSmallPanelButton("Open", () => OpenPlaylist(playlist)));
        row.Add(CreateSmallPanelButton("Rename", () => { selectedPlaylistName = playlist.Name; ShowPlaylistNameDialog("Rename Playlist", "Rename", playlist.Name, name => RenameSelectedPlaylist(name)); }));
        row.Add(CreateSmallPanelButton("Delete", () => { selectedPlaylistName = playlist.Name; DeleteSelectedPlaylist(); }));
        return row;
    }

    private void OpenPlaylist(BetterJukeboxPlaylist playlist)
    {
        if (playlist == null)
        {
            return;
        }
        CancelMultiSelectForNavigation();
        selectedPlaylistName = playlist.Name;
        UpdatePlaylistActionRowVisibility();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private bool IsInsideButton(VisualElement element)
    {
        VisualElement current = element;
        while (current != null)
        {
            if (current is Button)
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private VisualElement CreatePlaylistSongRow(SongMeta songMeta, BetterJukeboxPlaylist playlist)
    {
        VisualElement row = CreatePanelRow();
        ConfigureSongRowForMultiSelect(row, songMeta);
        if (multiSelectMode)
        {
            row.Add(CreateMultiSelectCheckmark(songMeta));
        }
        if (!multiSelectMode)
        {
            int playlistIndex = GetPlaylistSongIndex(playlist, songMeta);
            Label dragHandle = CreatePanelLabel("≡");
            dragHandle.style.marginRight = 12;
            dragHandle.style.fontSize = 24;
            dragHandle.style.color = GetAccentColor();
            row.Add(dragHandle);
            RegisterPlaylistDragHandle(dragHandle, row, playlist, songMeta, playlistIndex);
        }
        if (modSettings.ShowAlbumArtInPlaylists)
        {
            row.Add(CreateAlbumArtElement(songMeta));
        }
        Label label = CreatePanelLabel(songMeta != null ? songMeta.GetArtistDashTitle() : "Unknown song");
        label.style.flexGrow = 1f;
        row.Add(label);
        if (!multiSelectMode)
        {
            row.Add(CreateSmallPanelButton("Queue", () => AddSongToQueue(songMeta, false)));
            row.Add(CreatePlaylistMoveHoldButton("↑", "⇧", "⇈", playlist, songMeta, -1, row));
            row.Add(CreatePlaylistMoveHoldButton("↓", "⇩", "⇊", playlist, songMeta, 1, row));
            row.Add(CreateSmallPanelButton("Remove", () => { RemoveSongFromPlaylist(playlist, songMeta); UpdateSearchResults(searchTextField != null ? searchTextField.value : ""); }));
            row.Add(CreateFavoriteStarButton(songMeta, () => UpdateSearchResults(searchTextField != null ? searchTextField.value : "")));
            playlistRowElements.Add(row);
            playlistRowSongMetas.Add(songMeta);
        }
        return row;
    }

    private void RegisterPlaylistDragHandle(Label dragHandle, VisualElement row, BetterJukeboxPlaylist playlist, SongMeta songMeta, int playlistIndex)
    {
        if (dragHandle == null || row == null || playlist == null || songMeta == null)
        {
            return;
        }

        dragHandle.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
            {
                return;
            }
            draggingPlaylistIndex = GetPlaylistSongIndex(playlist, songMeta);
            currentPlaylistDropIndex = FindPlaylistDisplayIndex(songMeta);
            draggingPlaylist = playlist;
            row.style.opacity = 0.55f;
            CreatePlaylistDragGhost(songMeta.GetArtistDashTitle(), evt.position);
            HighlightPlaylistDropTarget(currentPlaylistDropIndex);
            try { dragHandle.CapturePointer(evt.pointerId); } catch { }
            evt.StopPropagation();
        });

        dragHandle.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (draggingPlaylistIndex < 0 || draggingPlaylist == null)
            {
                return;
            }
            UpdatePlaylistDragGhost(evt.position);
            int targetIndex = FindPlaylistDropIndex(evt.position);
            if (targetIndex >= 0 && targetIndex != currentPlaylistDropIndex)
            {
                currentPlaylistDropIndex = targetIndex;
                HighlightPlaylistDropTarget(targetIndex);
            }
            evt.StopPropagation();
        });

        dragHandle.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (draggingPlaylistIndex < 0 || draggingPlaylist == null)
            {
                return;
            }

            int targetIndex = FindPlaylistDropIndex(evt.position);
            BetterJukeboxPlaylist dragPlaylist = draggingPlaylist;
            draggingPlaylistIndex = -1;
            currentPlaylistDropIndex = -1;
            draggingPlaylist = null;
            row.style.opacity = 1f;
            RemovePlaylistDragGhost();
            ClearPlaylistDropHighlights();
            try { dragHandle.ReleasePointer(evt.pointerId); } catch { }

            int sourceDisplayIndex = FindPlaylistDisplayIndex(songMeta);
            if (targetIndex >= 0 && targetIndex != sourceDisplayIndex)
            {
                MovePlaylistSongToDisplayedIndex(dragPlaylist, songMeta, targetIndex);
            }
            evt.StopPropagation();
        });
    }

    private int FindPlaylistDisplayIndex(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return -1;
        }
        for (int i = 0; i < playlistRowSongMetas.Count; i++)
        {
            if (playlistRowSongMetas[i] == songMeta)
            {
                return i;
            }
        }
        return -1;
    }

    private void MovePlaylistSongToDisplayedIndex(BetterJukeboxPlaylist playlist, SongMeta songMeta, int targetDisplayIndex)
    {
        if (playlist == null || playlist.SongIds == null || songMeta == null || targetDisplayIndex < 0 || targetDisplayIndex >= playlistRowSongMetas.Count)
        {
            return;
        }

        SongMeta targetSongMeta = playlistRowSongMetas[targetDisplayIndex];
        int sourceIndex = GetPlaylistSongIndex(playlist, songMeta);
        int targetIndex = GetPlaylistSongIndex(playlist, targetSongMeta);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        string id = playlist.SongIds[sourceIndex];
        playlist.SongIds.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }
        targetIndex = Mathf.Clamp(targetIndex, 0, playlist.SongIds.Count);
        playlist.SongIds.Insert(targetIndex, id);
        SavePlaylists();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void CreatePlaylistDragGhost(string displayName, Vector2 pointerPosition)
    {
        RemovePlaylistDragGhost();
        if (searchPanel == null)
        {
            return;
        }

        playlistDragGhost = new VisualElement();
        playlistDragGhost.name = "betterJukeboxPlaylistDragGhost";
        playlistDragGhost.style.position = Position.Absolute;
        playlistDragGhost.style.left = new StyleLength(new Length(Mathf.Max(8, pointerPosition.x - searchPanel.worldBound.x + 16), LengthUnit.Pixel));
        playlistDragGhost.style.top = new StyleLength(new Length(Mathf.Max(8, pointerPosition.y - searchPanel.worldBound.y + 8), LengthUnit.Pixel));
        playlistDragGhost.style.paddingLeft = 12;
        playlistDragGhost.style.paddingRight = 12;
        playlistDragGhost.style.paddingTop = 8;
        playlistDragGhost.style.paddingBottom = 8;
        playlistDragGhost.style.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.96f);
        playlistDragGhost.style.borderTopLeftRadius = 10;
        playlistDragGhost.style.borderTopRightRadius = 10;
        playlistDragGhost.style.borderBottomLeftRadius = 10;
        playlistDragGhost.style.borderBottomRightRadius = 10;
        playlistDragGhost.pickingMode = PickingMode.Ignore;

        Label label = CreatePanelLabel("↕ " + displayName);
        label.style.marginRight = new StyleLength(new Length(0, LengthUnit.Pixel));
        playlistDragGhost.Add(label);
        searchPanel.Add(playlistDragGhost);
        playlistDragGhost.BringToFront();
    }

    private void UpdatePlaylistDragGhost(Vector2 pointerPosition)
    {
        if (playlistDragGhost == null || searchPanel == null)
        {
            return;
        }
        playlistDragGhost.style.left = new StyleLength(new Length(Mathf.Max(8, pointerPosition.x - searchPanel.worldBound.x + 16), LengthUnit.Pixel));
        playlistDragGhost.style.top = new StyleLength(new Length(Mathf.Max(8, pointerPosition.y - searchPanel.worldBound.y + 8), LengthUnit.Pixel));
    }

    private void RemovePlaylistDragGhost()
    {
        if (playlistDragGhost != null)
        {
            playlistDragGhost.RemoveFromHierarchy();
            playlistDragGhost = null;
        }
    }

    private void HighlightPlaylistDropTarget(int targetIndex)
    {
        for (int i = 0; i < playlistRowElements.Count; i++)
        {
            VisualElement row = playlistRowElements[i];
            if (row == null)
            {
                continue;
            }
            row.style.backgroundColor = i == targetIndex ? new Color(0.12f, 0.22f, 0.38f, 0.72f) : GetRowColor();
        }
    }

    private void ClearPlaylistDropHighlights()
    {
        for (int i = 0; i < playlistRowElements.Count; i++)
        {
            VisualElement row = playlistRowElements[i];
            if (row != null)
            {
                row.style.backgroundColor = GetRowColor();
            }
        }
    }

    private int FindPlaylistDropIndex(Vector2 pointerPosition)
    {
        if (playlistRowElements == null || playlistRowElements.Count == 0)
        {
            return -1;
        }

        for (int i = 0; i < playlistRowElements.Count; i++)
        {
            VisualElement row = playlistRowElements[i];
            if (row != null && row.worldBound.Contains(pointerPosition))
            {
                return i;
            }
        }

        float bestDistance = float.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < playlistRowElements.Count; i++)
        {
            VisualElement row = playlistRowElements[i];
            if (row == null)
            {
                continue;
            }
            float centerY = row.worldBound.y + row.worldBound.height / 2f;
            float distance = Mathf.Abs(pointerPosition.y - centerY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    private List<SongMeta> GetPlaylistSongMetas(BetterJukeboxPlaylist playlist)
    {
        List<SongMeta> result = new List<SongMeta>();
        if (playlist == null) { return result; }
        List<SongMeta> allSongs = GetAllSelectableSongMetas();
        foreach (string id in playlist.SongIds)
        {
            foreach (SongMeta songMeta in allSongs)
            {
                if (songMeta == null || result.Contains(songMeta)) { continue; }
                List<string> ids = GetFavoriteSongMetaIds(songMeta);
                if (ids.Contains(id))
                {
                    result.Add(songMeta);
                    break;
                }
            }
        }
        return result;
    }

    private void CreatePlaylist(string name)
    {
        LoadPlaylists();
        string cleanName = MakeUniquePlaylistName(name);
        BetterJukeboxPlaylist playlist = new BetterJukeboxPlaylist();
        playlist.Name = cleanName;
        betterJukeboxPlaylists.Add(playlist);
        SavePlaylists();
        selectedPlaylistName = cleanName;
        UpdatePlaylistFilterButtonText();
        UpdatePlaylistActionRowVisibility();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private string MakeUniquePlaylistName(string wantedName)
    {
        string baseName = string.IsNullOrWhiteSpace(wantedName) ? "New Playlist" : wantedName.Trim();
        string name = baseName;
        int index = 2;
        while (FindPlaylist(name) != null)
        {
            name = baseName + " " + index;
            index++;
        }
        return name;
    }

    private void RenameSelectedPlaylist(string newName)
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(selectedPlaylistName);
        if (playlist == null) { return; }
        string cleanName = MakeUniquePlaylistName(newName);
        playlist.Name = cleanName;
        selectedPlaylistName = cleanName;
        SavePlaylists();
        UpdatePlaylistActionRowVisibility();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void DeleteSelectedPlaylist()
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(selectedPlaylistName);
        if (playlist == null) { return; }
        ShowDeletePlaylistConfirm(playlist.Name, false);
    }

    private void ShowDeletePlaylistConfirm(string playlistName, bool finalQuestion)
    {
        if (string.IsNullOrWhiteSpace(playlistName)) { return; }
        CloseDeletePlaylistConfirm();

        if (searchPanel == null)
        {
            return;
        }

        playlistDeleteConfirmPanel = new VisualElement();
        playlistDeleteConfirmPanel.name = "betterJukeboxDeletePlaylistConfirm";
        playlistDeleteConfirmPanel.style.position = Position.Absolute;
        playlistDeleteConfirmPanel.style.left = new StyleLength(new Length(16, LengthUnit.Percent));
        playlistDeleteConfirmPanel.style.right = new StyleLength(new Length(16, LengthUnit.Percent));
        playlistDeleteConfirmPanel.style.top = new StyleLength(new Length(24, LengthUnit.Percent));
        playlistDeleteConfirmPanel.style.flexDirection = FlexDirection.Column;
        playlistDeleteConfirmPanel.style.paddingLeft = 18f;
        playlistDeleteConfirmPanel.style.paddingRight = 18f;
        playlistDeleteConfirmPanel.style.paddingTop = 16f;
        playlistDeleteConfirmPanel.style.paddingBottom = 16f;
        playlistDeleteConfirmPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.94f);
        playlistDeleteConfirmPanel.style.borderTopLeftRadius = 16f;
        playlistDeleteConfirmPanel.style.borderTopRightRadius = 16f;
        playlistDeleteConfirmPanel.style.borderBottomLeftRadius = 16f;
        playlistDeleteConfirmPanel.style.borderBottomRightRadius = 16f;
        playlistDeleteConfirmPanel.style.borderTopWidth = 1f;
        playlistDeleteConfirmPanel.style.borderBottomWidth = 1f;
        playlistDeleteConfirmPanel.style.borderLeftWidth = 1f;
        playlistDeleteConfirmPanel.style.borderRightWidth = 1f;
        playlistDeleteConfirmPanel.style.borderTopColor = GetPanelTopBorderColor();
        playlistDeleteConfirmPanel.style.borderBottomColor = GetPanelSideBorderColor();
        playlistDeleteConfirmPanel.style.borderLeftColor = GetPanelSideBorderColor();
        playlistDeleteConfirmPanel.style.borderRightColor = GetPanelSideBorderColor();

        Label title = CreatePanelLabel(finalQuestion
            ? "Are you absolutely sure? 😉"
            : "Delete playlist?");
        title.style.whiteSpace = WhiteSpace.Normal;
        title.style.marginRight = 0f;
        title.style.marginBottom = 6f;
        title.style.color = Color.white;
        playlistDeleteConfirmPanel.Add(title);

        Label body = CreatePanelLabel(finalQuestion
            ? "Do you really, really want to delete this playlist?"
            : "This will delete " + playlistName + " and remove its song list.");
        body.style.whiteSpace = WhiteSpace.Normal;
        body.style.marginRight = 0f;
        body.style.marginBottom = 12f;
        body.style.color = new Color(1f, 1f, 1f, 0.78f);
        playlistDeleteConfirmPanel.Add(body);

        VisualElement buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.justifyContent = Justify.FlexEnd;
        buttonRow.style.alignItems = Align.Center;
        buttonRow.Add(CreateSmallPanelButton("No", CloseDeletePlaylistConfirm));
        buttonRow.Add(CreateSmallPanelButton("Yes", () =>
        {
            if (finalQuestion)
            {
                CloseDeletePlaylistConfirm();
                ExecuteDeletePlaylist(playlistName);
                ShowFavoritesRemovedSkull();
            }
            else
            {
                ShowDeletePlaylistConfirm(playlistName, true);
            }
        }));
        playlistDeleteConfirmPanel.Add(buttonRow);

        searchPanel.Add(playlistDeleteConfirmPanel);
        playlistDeleteConfirmPanel.BringToFront();
        AnimateDeletePlaylistConfirmPopup(finalQuestion);
    }

    private void AnimateDeletePlaylistConfirmPopup(bool finalQuestion)
    {
        if (playlistDeleteConfirmPanel == null)
        {
            return;
        }

        try
        {
            playlistDeleteConfirmPanel.style.opacity = 0f;
            GameObject ownerObject = GetSafeOwnerGameObject();
            if (ownerObject != null && modSettings != null && modSettings.FadeAnimations)
            {
                AnimationUtils.FadeInVisualElement(ownerObject, playlistDeleteConfirmPanel, finalQuestion ? 0.16f : 0.10f);
            }
            else
            {
                playlistDeleteConfirmPanel.style.opacity = 1f;
            }

            if (finalQuestion)
            {
                VisualElement panel = playlistDeleteConfirmPanel;
                panel.style.backgroundColor = new Color(0.05f, 0.03f, 0.07f, 0.98f);
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.10f, () =>
                {
                    if (panel != null)
                    {
                        panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.94f);
                    }
                });
            }
        }
        catch
        {
        }
    }

    private void CloseDeletePlaylistConfirm()
    {
        if (playlistDeleteConfirmPanel == null)
        {
            return;
        }

        try
        {
            playlistDeleteConfirmPanel.RemoveFromHierarchy();
        }
        catch
        {
        }
        playlistDeleteConfirmPanel = null;
    }

    private void ExecuteDeletePlaylist(string playlistName)
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(playlistName);
        if (playlist == null) { return; }
        betterJukeboxPlaylists.Remove(playlist);
        if (string.Equals(selectedPlaylistName, playlistName, StringComparison.OrdinalIgnoreCase))
        {
            selectedPlaylistName = null;
        }
        SavePlaylists();
        UpdatePlaylistFilterButtonText();
        UpdatePlaylistActionRowVisibility();
        UpdateMultiSelectActionRowVisibility();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void QueueSelectedPlaylist()
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(selectedPlaylistName);
        List<SongMeta> songs = GetPlaylistSongMetas(playlist);
        foreach (SongMeta songMeta in songs)
        {
            AddSongToQueue(songMeta, false);
        }
        UpdateQueueBadge(true);
    }

    private void ShuffleSelectedPlaylist()
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(selectedPlaylistName);
        if (playlist == null || playlist.SongIds == null || playlist.SongIds.Count <= 1)
        {
            return;
        }

        System.Random random = new System.Random();
        for (int i = playlist.SongIds.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            string temp = playlist.SongIds[i];
            playlist.SongIds[i] = playlist.SongIds[j];
            playlist.SongIds[j] = temp;
        }

        SavePlaylists();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void SortSelectedPlaylistAlphabetically()
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(selectedPlaylistName);
        if (playlist == null || playlist.SongIds == null || playlist.SongIds.Count <= 1)
        {
            return;
        }

        List<SongMeta> songs = GetPlaylistSongMetas(playlist).OrderBy(songMeta => songMeta != null ? songMeta.GetArtistDashTitle() : "").ToList();
        List<string> sortedIds = new List<string>();
        foreach (SongMeta songMeta in songs)
        {
            int index = GetPlaylistSongIndex(playlist, songMeta);
            if (index >= 0)
            {
                string id = playlist.SongIds[index];
                if (!sortedIds.Contains(id))
                {
                    sortedIds.Add(id);
                }
            }
        }
        if (sortedIds.Count == playlist.SongIds.Count)
        {
            playlist.SongIds.Clear();
            playlist.SongIds.AddRange(sortedIds);
            SavePlaylists();
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        }
    }

    private void SortSelectedPlaylistRecentlyAdded()
    {
        BetterJukeboxPlaylist playlist = FindPlaylist(selectedPlaylistName);
        if (playlist == null || playlist.SongIds == null || playlist.SongIds.Count <= 1)
        {
            return;
        }

        if (playlist.AddedSongIds.Count == 0)
        {
            playlist.AddedSongIds.AddRange(playlist.SongIds);
        }

        List<string> sortedIds = new List<string>();
        foreach (string id in playlist.AddedSongIds)
        {
            if (!string.IsNullOrWhiteSpace(id) && playlist.SongIds.Contains(id) && !sortedIds.Contains(id))
            {
                sortedIds.Add(id);
            }
        }
        foreach (string id in playlist.SongIds)
        {
            if (!string.IsNullOrWhiteSpace(id) && !sortedIds.Contains(id))
            {
                sortedIds.Add(id);
            }
        }
        playlist.SongIds.Clear();
        playlist.SongIds.AddRange(sortedIds);
        SavePlaylists();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void AddSongToPlaylist(BetterJukeboxPlaylist playlist, SongMeta songMeta)
    {
        if (playlist == null || songMeta == null) { return; }
        string id = GetFavoriteSongMetaId(songMeta);
        if (string.IsNullOrWhiteSpace(id)) { return; }
        if (!playlist.SongIds.Contains(id))
        {
            playlist.SongIds.Insert(0, id);
            playlist.AddedSongIds.Remove(id);
            playlist.AddedSongIds.Insert(0, id);
            SavePlaylists();
        }
        UpdatePlaylistFilterButtonText();
    }

    private void RemoveSongFromPlaylist(BetterJukeboxPlaylist playlist, SongMeta songMeta)
    {
        if (playlist == null || songMeta == null) { return; }
        List<string> ids = GetFavoriteSongMetaIds(songMeta);
        foreach (string id in ids)
        {
            playlist.SongIds.Remove(id);
            playlist.AddedSongIds.Remove(id);
        }
        SavePlaylists();
    }


    private int GetPlaylistSongIndex(BetterJukeboxPlaylist playlist, SongMeta songMeta)
    {
        if (playlist == null || playlist.SongIds == null || songMeta == null)
        {
            return -1;
        }

        List<string> songIds = GetFavoriteSongMetaIds(songMeta);
        for (int i = 0; i < playlist.SongIds.Count; i++)
        {
            string playlistSongId = playlist.SongIds[i];
            if (!string.IsNullOrWhiteSpace(playlistSongId) && songIds.Contains(playlistSongId))
            {
                return i;
            }
        }
        return -1;
    }

    private void MovePlaylistSong(BetterJukeboxPlaylist playlist, SongMeta songMeta, int direction)
    {
        if (playlist == null || playlist.SongIds == null || playlist.SongIds.Count <= 1)
        {
            return;
        }

        int index = GetPlaylistSongIndex(playlist, songMeta);
        int newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= playlist.SongIds.Count)
        {
            return;
        }

        string id = playlist.SongIds[index];
        playlist.SongIds.RemoveAt(index);
        playlist.SongIds.Insert(newIndex, id);
        SavePlaylists();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private void MovePlaylistSongToEdge(BetterJukeboxPlaylist playlist, SongMeta songMeta, int direction)
    {
        if (playlist == null || playlist.SongIds == null || playlist.SongIds.Count <= 1)
        {
            return;
        }

        int index = GetPlaylistSongIndex(playlist, songMeta);
        if (index < 0)
        {
            return;
        }

        int targetIndex = direction < 0 ? 0 : playlist.SongIds.Count - 1;
        if (index == targetIndex)
        {
            return;
        }

        string id = playlist.SongIds[index];
        playlist.SongIds.RemoveAt(index);
        playlist.SongIds.Insert(targetIndex, id);
        SavePlaylists();
        UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
    }

    private Button CreatePlaylistMoveHoldButton(string normalIcon, string middleIcon, string readyIcon, BetterJukeboxPlaylist playlist, SongMeta songMeta, int direction, VisualElement row)
    {
        Button button = null;
        button = new Button(() =>
        {
            if (playlistHoldMoveSuppressNextClick)
            {
                playlistHoldMoveSuppressNextClick = false;
                return;
            }
            MovePlaylistSong(playlist, songMeta, direction);
        });
        button.tooltip = direction < 0 ? "Move up. Hold to move to top." : "Move down. Hold to move to bottom.";
        button.AddToClassList("tinyFont");
        button.style.marginLeft = 4;
        button.style.marginRight = 4;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        AddButtonVisual(button, normalIcon, "tinyFont", 11f, 11f, 6f, 6f, 11f);

        VisualElement fill = new VisualElement();
        fill.name = "betterJukeboxPlaylistHoldFill";
        fill.style.position = Position.Absolute;
        fill.style.left = new StyleLength(new Length(0, LengthUnit.Pixel));
        fill.style.top = new StyleLength(new Length(0, LengthUnit.Pixel));
        fill.style.bottom = new StyleLength(new Length(0, LengthUnit.Pixel));
        fill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
        fill.style.backgroundColor = GetQueueHoldFillColor(0f);
        fill.style.borderTopLeftRadius = 11f;
        fill.style.borderTopRightRadius = 11f;
        fill.style.borderBottomLeftRadius = 11f;
        fill.style.borderBottomRightRadius = 11f;
        fill.pickingMode = PickingMode.Ignore;
        button.Insert(1, fill);

        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.BringToFront();
        }

        button.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                BeginPlaylistHoldMove(button, row, playlist, songMeta, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                EndPlaylistHoldMove(button, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                BeginPlaylistHoldMove(button, row, playlist, songMeta, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                EndPlaylistHoldMove(button, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<PointerCancelEvent>(evt =>
        {
            CancelPlaylistHoldMove(button);
        });

        button.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            CancelPlaylistHoldMove(button);
        });

        RegisterButtonThemeHover(button);
        return button;
    }

    private void BeginPlaylistHoldMove(Button button, VisualElement row, BetterJukeboxPlaylist playlist, SongMeta songMeta, int direction)
    {
        if (button == null)
        {
            return;
        }
        if (playlistHoldMoveButton == button && playlistHoldMoveStartedAt >= 0f)
        {
            return;
        }

        playlistHoldMoveButton = button;
        playlistHoldMoveRow = row;
        playlistHoldMovePlaylist = playlist;
        playlistHoldMoveSongMeta = songMeta;
        playlistHoldMoveDirection = direction;
        playlistHoldMoveStartedAt = Time.unscaledTime;
        ApplyPlaylistHoldMoveProgress(button, row, direction, 0f);
    }

    private void EndPlaylistHoldMove(Button button, int direction)
    {
        if (button == null || button != playlistHoldMoveButton)
        {
            return;
        }

        float progress = GetPlaylistHoldMoveProgress();
        BetterJukeboxPlaylist playlist = playlistHoldMovePlaylist;
        SongMeta songMeta = playlistHoldMoveSongMeta;
        ResetPlaylistHoldMoveVisual(button, playlistHoldMoveRow);
        playlistHoldMoveButton = null;
        playlistHoldMoveRow = null;
        playlistHoldMovePlaylist = null;
        playlistHoldMoveSongMeta = null;
        playlistHoldMoveStartedAt = -1f;

        if (progress >= 1f)
        {
            playlistHoldMoveSuppressNextClick = true;
            MovePlaylistSongToEdge(playlist, songMeta, direction);
        }
    }

    private void CancelPlaylistHoldMove(Button button)
    {
        if (button == null || button != playlistHoldMoveButton)
        {
            return;
        }

        ResetPlaylistHoldMoveVisual(button, playlistHoldMoveRow);
        playlistHoldMoveButton = null;
        playlistHoldMoveRow = null;
        playlistHoldMovePlaylist = null;
        playlistHoldMoveSongMeta = null;
        playlistHoldMoveStartedAt = -1f;
    }

    private void ProcessPlaylistHoldMoveProgress()
    {
        if (playlistHoldMoveButton == null || playlistHoldMoveStartedAt < 0f)
        {
            return;
        }

        ApplyPlaylistHoldMoveProgress(playlistHoldMoveButton, playlistHoldMoveRow, playlistHoldMoveDirection, GetPlaylistHoldMoveProgress());
    }

    private float GetPlaylistHoldMoveProgress()
    {
        if (playlistHoldMoveStartedAt < 0f)
        {
            return 0f;
        }

        float heldSeconds = Time.unscaledTime - playlistHoldMoveStartedAt;
        if (heldSeconds < QueueHoldMoveGraceSeconds)
        {
            return 0f;
        }

        return Mathf.Clamp01((heldSeconds - QueueHoldMoveGraceSeconds) / QueueHoldMoveReadySeconds);
    }

    private void ApplyPlaylistHoldMoveProgress(Button button, VisualElement row, int direction, float progress)
    {
        if (button == null)
        {
            return;
        }

        VisualElement fill = button.Q("betterJukeboxPlaylistHoldFill");
        if (fill != null)
        {
            fill.style.width = new StyleLength(new Length(Mathf.Clamp01(progress) * 100f, LengthUnit.Percent));
            fill.style.backgroundColor = GetQueueHoldFillColor(progress);
        }

        string icon = direction < 0 ? "↑" : "↓";
        if (progress >= 1f)
        {
            icon = direction < 0 ? "⇈" : "⇊";
        }
        else if (progress >= 0.45f)
        {
            icon = direction < 0 ? "⇧" : "⇩";
        }
        SetButtonVisualText(button, icon);

        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.style.color = progress >= 1f ? Color.white : GetButtonTextColor();
        }

        if (row != null)
        {
            row.style.backgroundColor = progress >= 1f
                ? GetRowPulseColor()
                : new Color(0f, 0f, 0f, 0.25f + (0.20f * Mathf.Clamp01(progress)));
        }
    }

    private void ResetPlaylistHoldMoveVisual(Button button, VisualElement row)
    {
        if (button != null)
        {
            VisualElement fill = button.Q("betterJukeboxPlaylistHoldFill");
            if (fill != null)
            {
                fill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
            }
            ApplyButtonNormalStyle(button);
        }

        if (row != null)
        {
            row.style.backgroundColor = GetRowColor();
        }
    }

    private void ShowAddToPlaylistDialog(SongMeta songMeta)
    {
        LoadPlaylists();
        playlistDialogSongMeta = songMeta;
        ClosePlaylistDialog();
        if (uiDocument == null || uiDocument.rootVisualElement == null) { return; }
        playlistDialogPanel = new VisualElement();
        playlistDialogPanel.name = "betterJukeboxAddToPlaylistDialog";
        playlistDialogPanel.style.position = Position.Absolute;
        playlistDialogPanel.style.left = new StyleLength(new Length(35, LengthUnit.Percent));
        playlistDialogPanel.style.right = new StyleLength(new Length(35, LengthUnit.Percent));
        playlistDialogPanel.style.top = new StyleLength(new Length(28, LengthUnit.Percent));
        playlistDialogPanel.style.flexDirection = FlexDirection.Column;
        playlistDialogPanel.focusable = true;
        playlistDialogPanel.style.paddingLeft = 16f;
        playlistDialogPanel.style.paddingRight = 16f;
        playlistDialogPanel.style.paddingTop = 14f;
        playlistDialogPanel.style.paddingBottom = 14f;
        playlistDialogPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.92f);
        playlistDialogPanel.style.borderTopLeftRadius = 18f;
        playlistDialogPanel.style.borderTopRightRadius = 18f;
        playlistDialogPanel.style.borderBottomLeftRadius = 18f;
        playlistDialogPanel.style.borderBottomRightRadius = 18f;
        playlistDialogPanel.style.borderTopWidth = 1f;
        playlistDialogPanel.style.borderBottomWidth = 1f;
        playlistDialogPanel.style.borderLeftWidth = 1f;
        playlistDialogPanel.style.borderRightWidth = 1f;
        ApplyThemedBorder(playlistDialogPanel);
        playlistDialogPanel.Add(CreatePopupHeader("Add to Playlist", ClosePlaylistDialog));
        if (betterJukeboxPlaylists.Count == 0)
        {
            playlistDialogPanel.Add(CreatePanelLabel("No playlists yet."));
        }
        foreach (BetterJukeboxPlaylist playlist in betterJukeboxPlaylists.OrderBy(it => it.Name).ToList())
        {
            BetterJukeboxPlaylist targetPlaylist = playlist;
            Button playlistButton = null;
            playlistButton = CreateSmallPanelButton("🎵 " + targetPlaylist.Name, () =>
            {
                AddSongToPlaylist(targetPlaylist, playlistDialogSongMeta);
                SetButtonVisualText(playlistButton, "Added to " + targetPlaylist.Name);
                ApplyButtonPulseStyle(playlistButton);
                AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.65f, () =>
                {
                    ClosePlaylistDialog();
                });
            });
            playlistButton.style.marginTop = 5f;
            playlistButton.style.marginBottom = 5f;
            playlistDialogPanel.Add(playlistButton);
        }
        playlistDialogPanel.Add(CreateSmallPanelButton("+ New Playlist", () => ShowPlaylistNameDialog("New Playlist", "Create", null, name => { CreatePlaylist(name); AddSongToPlaylist(FindPlaylist(selectedPlaylistName), playlistDialogSongMeta); ClosePlaylistDialog(); })));
        uiDocument.rootVisualElement.Add(playlistDialogPanel);
        playlistDialogPanel.BringToFront();
    }

    private void ClosePlaylistDialog()
    {
        playlistNameTextField = null;
        playlistNameDialogSubmitAction = null;
        if (playlistDialogPanel != null)
        {
            playlistDialogPanel.RemoveFromHierarchy();
            playlistDialogPanel = null;
        }
    }

    private bool IsPlaylistNameDialogOpen()
    {
        return playlistDialogPanel != null
            && playlistNameTextField != null
            && playlistDialogPanel.parent != null;
    }


    private void SubmitPlaylistNameDialog()
    {
        string value = playlistNameTextField != null ? playlistNameTextField.value : "";
        Action<string> done = playlistNameDialogSubmitAction;
        ClosePlaylistDialog();
        if (done != null)
        {
            done(value);
        }
    }

    private void ShowPlaylistNameDialog(string title, string actionText, string initialName, Action<string> done)
    {
        ClosePlaylistDialog();
        if (uiDocument == null || uiDocument.rootVisualElement == null) { return; }
        playlistNameDialogSubmitAction = done;
        playlistDialogPanel = new VisualElement();
        playlistDialogPanel.name = "betterJukeboxPlaylistNameDialog";
        playlistDialogPanel.focusable = true;
        playlistDialogPanel.style.position = Position.Absolute;
        playlistDialogPanel.style.left = new StyleLength(new Length(36, LengthUnit.Percent));
        playlistDialogPanel.style.right = new StyleLength(new Length(36, LengthUnit.Percent));
        playlistDialogPanel.style.top = new StyleLength(new Length(30, LengthUnit.Percent));
        playlistDialogPanel.style.flexDirection = FlexDirection.Column;
        playlistDialogPanel.style.paddingLeft = 16f;
        playlistDialogPanel.style.paddingRight = 16f;
        playlistDialogPanel.style.paddingTop = 14f;
        playlistDialogPanel.style.paddingBottom = 14f;
        playlistDialogPanel.style.backgroundColor = new Color(0f, 0f, 0f, 0.92f);
        playlistDialogPanel.style.borderTopLeftRadius = 18f;
        playlistDialogPanel.style.borderTopRightRadius = 18f;
        playlistDialogPanel.style.borderBottomLeftRadius = 18f;
        playlistDialogPanel.style.borderBottomRightRadius = 18f;
        playlistDialogPanel.style.borderTopWidth = 1f;
        playlistDialogPanel.style.borderBottomWidth = 1f;
        playlistDialogPanel.style.borderLeftWidth = 1f;
        playlistDialogPanel.style.borderRightWidth = 1f;
        ApplyThemedBorder(playlistDialogPanel);
        playlistDialogPanel.Add(CreatePopupHeader(title, ClosePlaylistDialog));
        TextField nameField = new TextField();
        playlistNameTextField = nameField;
        nameField.name = "betterJukeboxPlaylistNameField";
        nameField.value = string.IsNullOrWhiteSpace(initialName) ? "" : initialName;
        nameField.style.flexGrow = 1f;
        nameField.style.marginLeft = 0f;
        nameField.style.marginRight = 0f;
        nameField.style.marginTop = 0f;
        nameField.style.marginBottom = 0f;
        nameField.style.color = Color.white;
        nameField.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        nameField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                string value = nameField != null ? nameField.value : "";
                SubmitPlaylistNameDialog();
                evt.StopImmediatePropagation();
                return;
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                ClosePlaylistDialog();
                evt.StopImmediatePropagation();
                return;
            }
        }, TrickleDown.TrickleDown);

        VisualElement nameFrame = new VisualElement();
        nameFrame.name = "betterJukeboxPlaylistNameFrame";
        nameFrame.style.flexDirection = FlexDirection.Row;
        nameFrame.style.alignItems = Align.Center;
        nameFrame.style.marginTop = 10f;
        nameFrame.style.marginBottom = 10f;
        nameFrame.style.paddingLeft = 12f;
        nameFrame.style.paddingRight = 12f;
        nameFrame.style.paddingTop = 7f;
        nameFrame.style.paddingBottom = 7f;
        nameFrame.style.backgroundColor = GetSearchInputBackgroundColor();
        nameFrame.style.borderTopLeftRadius = 12f;
        nameFrame.style.borderTopRightRadius = 12f;
        nameFrame.style.borderBottomLeftRadius = 12f;
        nameFrame.style.borderBottomRightRadius = 12f;
        nameFrame.style.borderTopWidth = 1f;
        nameFrame.style.borderBottomWidth = 1f;
        nameFrame.style.borderLeftWidth = 1f;
        nameFrame.style.borderRightWidth = 1f;
        nameFrame.style.borderTopColor = GetPanelSideBorderColor();
        nameFrame.style.borderBottomColor = GetPanelSideBorderColor();
        nameFrame.style.borderLeftColor = GetPanelSideBorderColor();
        nameFrame.style.borderRightColor = GetPanelSideBorderColor();
        nameFrame.Add(nameField);
        playlistDialogPanel.Add(nameFrame);
        ApplyPlaylistNameFieldTheme(nameField);

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.FlexEnd;
        row.Add(CreateSmallPanelButton("Cancel", ClosePlaylistDialog));
        Button dialogActionButton = CreateSmallPanelButton(actionText, SubmitPlaylistNameDialog);
        dialogActionButton.name = "betterJukeboxPlaylistDialogActionButton";
        row.Add(dialogActionButton);
        playlistDialogPanel.Add(row);
        uiDocument.rootVisualElement.Add(playlistDialogPanel);
        playlistDialogPanel.BringToFront();
        playlistDialogPanel.Focus();
        nameField.Focus();
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(1, () =>
        {
            if (playlistNameTextField != null && playlistDialogPanel != null && playlistDialogPanel.parent != null)
            {
                playlistNameTextField.Focus();
            }
        });
        AwaitableUtils.ExecuteAfterDelayInFramesAsync(3, () =>
        {
            if (playlistNameTextField != null && playlistDialogPanel != null && playlistDialogPanel.parent != null)
            {
                playlistNameTextField.Focus();
            }
        });
    }

    private void ApplyPlaylistNameFieldTheme(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        element.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        element.style.color = Color.white;
        element.style.borderTopWidth = 0f;
        element.style.borderBottomWidth = 0f;
        element.style.borderLeftWidth = 0f;
        element.style.borderRightWidth = 0f;
        element.style.borderTopColor = new Color(0f, 0f, 0f, 0f);
        element.style.borderBottomColor = new Color(0f, 0f, 0f, 0f);
        element.style.borderLeftColor = new Color(0f, 0f, 0f, 0f);
        element.style.borderRightColor = new Color(0f, 0f, 0f, 0f);

        for (int index = 0; index < element.childCount; index++)
        {
            ApplyPlaylistNameFieldTheme(element[index]);
        }
    }

    private void LoadFavoriteSongIds()
    {
        if (betterJukeboxFavoritesLoaded)
        {
            return;
        }
        betterJukeboxFavoritesLoaded = true;
        betterJukeboxFavoriteSongIds.Clear();
        betterJukeboxFavoriteSongIdOrder.Clear();

        try
        {
            EnsureFavoritesFileExists();
            string path = GetFavoritesPath();
            if (!System.IO.File.Exists(path))
            {
                return;
            }

            string text = System.IO.File.ReadAllText(path);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string[] parts = text.Split(new char[] { '\n', '\r', ',', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = NormalizeFavoriteIdText(part);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    AddFavoriteSongIdToMemory(id, false);
                }
            }

            NormalizeLoadedFavoriteSongIds();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not load favorites: " + ex.Message);
        }
    }

    private string GetFavoritesPath()
    {
        return System.IO.Path.Combine(GetBetterJukeboxPersistentDirectory(), "Favorites.json");
    }

    private string GetBetterJukeboxPersistentDirectory()
    {
        return System.IO.Path.Combine(System.IO.Path.Combine(Application.persistentDataPath, "Mods"), "BetterJukebox");
    }

    private void EnsureFavoritesFileExists()
    {
        try
        {
            string directory = GetBetterJukeboxPersistentDirectory();
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string path = GetFavoritesPath();
            if (!System.IO.File.Exists(path))
            {
                System.IO.File.WriteAllText(path, "[\n]\n");
                Debug.Log("BetterJukebox favorites file created at " + path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not create favorites file: " + ex.Message);
        }
    }

    private void AddFavoriteSongIdToMemory(string id, bool newestFirst)
    {
        string normalizedId = NormalizeFavoriteIdText(id);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }
        betterJukeboxFavoriteSongIds.Add(normalizedId);
        betterJukeboxFavoriteSongIdOrder.Remove(normalizedId);
        if (newestFirst)
        {
            betterJukeboxFavoriteSongIdOrder.Insert(0, normalizedId);
        }
        else
        {
            betterJukeboxFavoriteSongIdOrder.Add(normalizedId);
        }
    }

    private void RemoveFavoriteSongIdFromMemory(string id)
    {
        string normalizedId = NormalizeFavoriteIdText(id);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }
        betterJukeboxFavoriteSongIds.Remove(normalizedId);
        betterJukeboxFavoriteSongIdOrder.Remove(normalizedId);
    }

    private void SaveFavoriteSongIds()
    {
        try
        {
            string directory = GetBetterJukeboxPersistentDirectory();
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("[");
            List<string> idsToSave = new List<string>();
            foreach (string id in betterJukeboxFavoriteSongIdOrder)
            {
                if (!string.IsNullOrWhiteSpace(id) && betterJukeboxFavoriteSongIds.Contains(id) && !idsToSave.Contains(id))
                {
                    idsToSave.Add(id);
                }
            }
            foreach (string id in betterJukeboxFavoriteSongIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && !idsToSave.Contains(id))
                {
                    idsToSave.Add(id);
                }
            }

            int index = 0;
            foreach (string id in idsToSave)
            {
                builder.Append("  \"");
                builder.Append(id.Replace("\\", "\\\\").Replace("\"", "\\\""));
                builder.Append("\"");
                if (index < idsToSave.Count - 1)
                {
                    builder.Append(",");
                }
                builder.AppendLine();
                index++;
            }
            builder.AppendLine("]");
            System.IO.File.WriteAllText(GetFavoritesPath(), builder.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not save favorites: " + ex.Message);
        }
    }

    private bool IsFavoriteSongMeta(SongMeta songMeta)
    {
        List<string> ids = GetFavoriteSongMetaIds(songMeta);
        foreach (string id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && betterJukeboxFavoriteSongIds.Contains(id))
            {
                return true;
            }
        }
        return false;
    }

    private void ToggleFavoriteSongMeta(SongMeta songMeta)
    {
        List<string> ids = GetFavoriteSongMetaIds(songMeta);
        string primaryId = ids.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (string.IsNullOrWhiteSpace(primaryId))
        {
            Debug.LogWarning("BetterJukebox favorites - could not create favorite id for song");
            NotificationManager.CreateNotification(Translation.Of("Could not add favorite"));
            EnsureFavoritesFileExists();
            return;
        }

        bool wasFavorite = false;
        foreach (string id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && betterJukeboxFavoriteSongIds.Contains(id))
            {
                wasFavorite = true;
                break;
            }
        }

        bool added;
        RemoveFavoriteAliases(ids);
        if (wasFavorite)
        {
            added = false;
        }
        else
        {
            AddFavoriteSongIdToMemory(primaryId, true);
            added = true;
        }
        NormalizeLoadedFavoriteSongIds();
        SaveFavoriteSongIds();
        Debug.Log("BetterJukebox favorites - " + (added ? "added" : "removed") + " id '" + primaryId + "' at " + GetFavoritesPath());
        string songName = songMeta != null ? songMeta.GetArtistDashTitle() : "song";
        // Favorite toggle is silent. The filled star and optional sparkle animation provide feedback.
        RefreshFavoriteViews();
    }

    private List<string> GetFavoriteSongMetaIds(SongMeta songMeta)
    {
        // Favorites should use Melody Mania's native song identity, the same identity
        // BetterJukebox already resolves for the real queue / Companion queue.
        // The extra aliases below are only for migrating old v1.4.4 test files that
        // stored title/file based ids. New saves keep only the native id.
        List<string> ids = new List<string>();
        string nativeSongId = GetFavoriteSongMetaId(songMeta);
        AddFavoriteIdIfValid(ids, nativeSongId);
        if (!string.IsNullOrWhiteSpace(nativeSongId))
        {
            AddFavoriteIdIfValid(ids, "song:" + nativeSongId);
        }

        string filePath = GetFavoriteSongFilePath(songMeta);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            AddFavoriteIdIfValid(ids, filePath);
            AddFavoriteIdIfValid(ids, "file:" + filePath);
        }

        if (songMeta != null)
        {
            string artistTitle = songMeta.GetArtistDashTitle();
            AddFavoriteIdIfValid(ids, artistTitle);
            AddFavoriteIdIfValid(ids, "title:" + artistTitle);
            AddFavoriteIdIfValid(ids, "title:" + (songMeta.Artist ?? "") + " - " + (songMeta.Title ?? ""));
        }

        return ids;
    }

    private void AddFavoriteIdIfValid(List<string> ids, string id)
    {
        if (ids == null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }
        string trimmed = NormalizeFavoriteIdText(id);
        if (!string.IsNullOrWhiteSpace(trimmed) && !ids.Contains(trimmed))
        {
            ids.Add(trimmed);
        }
    }

    private string NormalizeFavoriteIdText(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return id.Trim();
    }

    private void RemoveFavoriteAliases(List<string> ids)
    {
        if (ids == null)
        {
            return;
        }
        foreach (string id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                RemoveFavoriteSongIdFromMemory(id);
            }
        }
    }

    private void NormalizeLoadedFavoriteSongIds()
    {
        try
        {
            if (songMetaManager == null || betterJukeboxFavoriteSongIds.Count == 0)
            {
                return;
            }

            HashSet<string> oldIds = new HashSet<string>(betterJukeboxFavoriteSongIds);
            HashSet<string> normalizedIds = new HashSet<string>();
            List<SongMeta> allSongMetas = null;
            try
            {
                allSongMetas = songMetaManager.GetSongMetas().ToList();
            }
            catch
            {
                allSongMetas = new List<SongMeta>();
            }

            foreach (SongMeta songMeta in allSongMetas)
            {
                List<string> aliases = GetFavoriteSongMetaIds(songMeta);
                bool isFavorite = false;
                foreach (string alias in aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias) && oldIds.Contains(alias))
                    {
                        isFavorite = true;
                        break;
                    }
                }

                if (isFavorite)
                {
                    string canonicalId = GetFavoriteSongMetaId(songMeta);
                    if (!string.IsNullOrWhiteSpace(canonicalId))
                    {
                        normalizedIds.Add(canonicalId);
                    }
                }
            }

            foreach (string oldId in oldIds)
            {
                if (string.IsNullOrWhiteSpace(oldId))
                {
                    continue;
                }

                // Keep already-native ids that were not matched above, but never keep
                // the old fallback formats. This removes title:/file:/artist-title
                // duplicates after migration.
                if (IsValidQueueSongHash(oldId))
                {
                    normalizedIds.Add(oldId);
                }
            }

            List<string> orderedNormalizedIds = new List<string>();
            foreach (string oldId in betterJukeboxFavoriteSongIdOrder)
            {
                foreach (string normalizedId in normalizedIds)
                {
                    if (!orderedNormalizedIds.Contains(normalizedId) && (oldId == normalizedId || oldIds.Contains(oldId)))
                    {
                        orderedNormalizedIds.Add(normalizedId);
                    }
                }
            }
            foreach (string id in normalizedIds)
            {
                if (!orderedNormalizedIds.Contains(id))
                {
                    orderedNormalizedIds.Add(id);
                }
            }

            betterJukeboxFavoriteSongIds.Clear();
            betterJukeboxFavoriteSongIdOrder.Clear();
            foreach (string id in orderedNormalizedIds)
            {
                AddFavoriteSongIdToMemory(id, false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not normalize favorites: " + ex.Message);
        }
    }

    private string GetFavoriteSongMetaId(SongMeta songMeta)
    {
        // Use the same song identity that BetterJukebox writes into the real
        // Melody Mania queue DTO for Companion sync: SongDto.Hash / SongHash.
        // Do not use GetSongMetaId here because in some Melody Mania builds
        // that can resolve to the TXT file path, which must never be stored
        // as a favorite id.
        string hash = GetMelodyManiaSongHash(songMeta);
        if (!string.IsNullOrWhiteSpace(hash) && IsValidQueueSongHash(hash))
        {
            return NormalizeFavoriteIdText(hash);
        }

        return null;
    }

    private bool IsValidQueueSongHash(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        string text = id.Trim();
        if (IsProbablyFilePathFavoriteFallback(text)
            || text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("title:", StringComparison.OrdinalIgnoreCase)
            || text.IndexOf("\\", StringComparison.Ordinal) >= 0
            || text.IndexOf("/", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        int colonIndex = text.IndexOf(':');
        if (colonIndex <= 0 || colonIndex != text.LastIndexOf(':'))
        {
            return false;
        }

        string left = text.Substring(0, colonIndex);
        string right = text.Substring(colonIndex + 1);
        return IsMd5HexText(left) && IsMd5HexText(right);
    }

    private bool IsMd5HexText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length != 32)
        {
            return false;
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool isHex = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }
        return true;
    }

    private bool IsProbablyArtistTitleFavoriteFallback(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }
        string text = id.Trim();
        if (text.IndexOf(" - ", StringComparison.Ordinal) < 0)
        {
            return false;
        }
        if (IsProbablyFilePathFavoriteFallback(text))
        {
            return false;
        }
        return true;
    }

    private bool IsProbablyFilePathFavoriteFallback(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }
        string text = id.Trim();
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        try
        {
            if (System.IO.Path.IsPathRooted(text))
            {
                return true;
            }
        }
        catch { }

        return text.IndexOf("\\", StringComparison.Ordinal) >= 0
            || text.IndexOf("/", StringComparison.Ordinal) >= 0
            || text.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private string GetFavoriteSongFilePath(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return null;
        }

        object fileInfo = GetMemberValue(songMeta, "FileInfo");
        if (fileInfo != null)
        {
            object fullName = GetMemberValue(fileInfo, "FullName");
            if (fullName != null && !string.IsNullOrWhiteSpace(fullName.ToString()))
            {
                return fullName.ToString();
            }
        }

        object path = GetMemberValue(songMeta, "FilePath") ?? GetMemberValue(songMeta, "Path");
        if (path != null && !string.IsNullOrWhiteSpace(path.ToString()))
        {
            return path.ToString();
        }

        return null;
    }

    private Button CreateFavoriteStarButton(SongMeta songMeta, Action refreshAction)
    {
        string starText = IsFavoriteSongMeta(songMeta) ? "★" : "☆";
        Button starButton = null;
        starButton = CreateSmallPanelButton(starText, () =>
        {
            bool wasFavorite = IsFavoriteSongMeta(songMeta);
            ToggleFavoriteSongMeta(songMeta);
            bool isFavoriteNow = IsFavoriteSongMeta(songMeta);
            if (!wasFavorite && isFavoriteNow)
            {
                if (modSettings != null && modSettings.ShowFavoriteSparkleAnimation)
                {
                    ShowFavoriteAddedSparkles(starButton);
                }
            }
            if (refreshAction != null)
            {
                refreshAction();
            }
        });
        starButton.tooltip = IsFavoriteSongMeta(songMeta) ? "Remove favorite" : "Add favorite";
        starButton.style.minWidth = 38f;
        if (!modSettings.ShowFavoriteStars || songMeta == null)
        {
            starButton.style.display = DisplayStyle.None;
        }
        return starButton;
    }

    private void ShowFavoriteAddedSparkles(VisualElement sourceElement)
    {
        if (sourceElement == null || uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        try
        {
            Rect bounds = sourceElement.worldBound;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            float centerX = bounds.x + (bounds.width / 2f);
            float centerY = bounds.y + (bounds.height / 2f);

            ShowFavoriteStarBurst(centerX, centerY);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("BetterJukebox favorite sparkle ignored: " + ex.Message);
        }
    }

    private void ShowFavoriteStarBurst(float centerX, float centerY)
    {
        string[] sparkleTexts = new string[]
        {
            "✦", "✧", "✦", "✶", "✧", "✦", "✧", "✶", "✦", "✧", "•", "•", "✦", "✧"
        };
        float[] offsetX = new float[]
        {
            -34f, -22f, -9f, 11f, 28f, 39f, 24f, 7f, -16f, -31f, 33f, -39f, 3f, 18f
        };
        float[] offsetY = new float[]
        {
            -8f, -29f, -42f, -36f, -25f, -4f, 20f, 34f, 27f, 14f, 9f, 4f, -18f, -2f
        };
        float[] fontSizes = new float[]
        {
            26f, 15f, 20f, 24f, 14f, 21f, 17f, 23f, 16f, 19f, 9f, 8f, 18f, 12f
        };

        for (int i = 0; i < sparkleTexts.Length; i++)
        {
            Label sparkle = new Label(sparkleTexts[i]);
            sparkle.name = "betterJukeboxFavoriteAnimatedSparkle";
            sparkle.AddToClassList("textShadow");
            sparkle.style.position = Position.Absolute;
            sparkle.style.left = new StyleLength(new Length(centerX, LengthUnit.Pixel));
            sparkle.style.top = new StyleLength(new Length(centerY, LengthUnit.Pixel));
            sparkle.style.fontSize = 4f;
            sparkle.style.color = GetFavoriteSparkleColor(i, 0);
            sparkle.style.opacity = 0f;
            sparkle.pickingMode = PickingMode.Ignore;
            uiDocument.rootVisualElement.Add(sparkle);
            AnimateFavoriteSparkle(sparkle, centerX, centerY, offsetX[i], offsetY[i], fontSizes[i], i);
        }
    }

    private Color GetFavoriteSparkleColor(int index, int phase)
    {
        int variant = (index + phase) % 6;
        if (variant == 0)
        {
            return new Color(1f, 0.80f, 0.25f, 1f);
        }
        if (variant == 1)
        {
            return new Color(1f, 0.94f, 0.68f, 1f);
        }
        if (variant == 2)
        {
            return GetAccentColor();
        }
        if (variant == 3)
        {
            return Color.white;
        }
        if (variant == 4)
        {
            return GetAccentHoverColor();
        }
        return new Color(1f, 0.66f, 0.28f, 1f);
    }

    private void AnimateFavoriteSparkle(Label sparkle, float centerX, float centerY, float offsetX, float offsetY, float baseFontSize, int index)
    {
        if (sparkle == null)
        {
            return;
        }

        try
        {
            float delay = 0.018f * (index % 7);
            float startLeft = centerX - 3f;
            float startTop = centerY - 7f;
            float firstLeft = centerX + (offsetX * 0.32f);
            float firstTop = centerY + (offsetY * 0.32f);
            float secondLeft = centerX + (offsetX * 0.72f);
            float secondTop = centerY + (offsetY * 0.72f);
            float endLeft = centerX + offsetX;
            float endTop = centerY + offsetY;

            sparkle.style.left = new StyleLength(new Length(startLeft, LengthUnit.Pixel));
            sparkle.style.top = new StyleLength(new Length(startTop, LengthUnit.Pixel));

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay, () =>
            {
                ApplyFavoriteSparkleFrame(sparkle, firstLeft, firstTop, baseFontSize * 0.62f, 0.70f, GetFavoriteSparkleColor(index, 0));
            });

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay + 0.08f, () =>
            {
                ApplyFavoriteSparkleFrame(sparkle, secondLeft, secondTop, baseFontSize * 1.18f, 1f, GetFavoriteSparkleColor(index, 1));
            });

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay + 0.18f, () =>
            {
                ApplyFavoriteSparkleFrame(sparkle, secondLeft + (offsetX * 0.08f), secondTop + (offsetY * 0.08f), baseFontSize * 0.78f, 0.82f, GetFavoriteSparkleColor(index, 2));
            });

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay + 0.28f, () =>
            {
                ApplyFavoriteSparkleFrame(sparkle, endLeft, endTop, baseFontSize * 1.05f, 0.56f, GetFavoriteSparkleColor(index, 3));
            });

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay + 0.42f, () =>
            {
                ApplyFavoriteSparkleFrame(sparkle, endLeft + (offsetX * 0.08f), endTop + (offsetY * 0.08f), baseFontSize * 0.48f, 0.20f, GetFavoriteSparkleColor(index, 4));
            });

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay + 0.56f, () =>
            {
                try
                {
                    if (sparkle != null)
                    {
                        sparkle.style.opacity = 0f;
                    }
                }
                catch
                {
                }
            });

            AwaitableUtils.ExecuteAfterDelayInSecondsAsync(delay + 0.76f, () =>
            {
                try
                {
                    if (sparkle != null)
                    {
                        sparkle.RemoveFromHierarchy();
                    }
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }

    private void ApplyFavoriteSparkleFrame(Label sparkle, float left, float top, float fontSize, float opacity, Color color)
    {
        try
        {
            if (sparkle == null)
            {
                return;
            }

            sparkle.style.left = new StyleLength(new Length(left, LengthUnit.Pixel));
            sparkle.style.top = new StyleLength(new Length(top, LengthUnit.Pixel));
            sparkle.style.fontSize = fontSize;
            sparkle.style.opacity = opacity;
            sparkle.style.color = color;
        }
        catch
        {
        }
    }

    private void RefreshFavoriteViews()
    {
        UpdateFavoriteFilterButtonText();
        UpdateFavoriteActionRowVisibility();
        if (searchPanelIsVisible)
        {
            UpdateSearchResults(searchTextField != null ? searchTextField.value : "");
        }
        if (queuePanelIsVisible)
        {
            UpdateQueuePanel();
        }
        if (historyPanelIsVisible)
        {
            UpdateHistoryPanel();
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
            historyResultsContainer.Add(CreateEmptyState("↺", "No history yet", "Played songs will show up here."));
            return;
        }

        VisualElement historySection = CreatePremiumPanelSection("Recently Played", "Replay or queue songs from this session.");
        foreach (SongMeta songMeta in betterJukeboxHistory.Take(20))
        {
            VisualElement row = CreatePanelRow();
            if (modSettings.ShowAlbumArtInHistory)
            {
                row.Add(CreateAlbumArtElement(songMeta));
            }

            Label label = CreatePanelLabel(songMeta.GetArtistDashTitle());
            label.style.flexGrow = 1f;
            row.Add(label);

            row.Add(CreateSmallPanelButton("Play again", () => PlaySongNow(songMeta)));
            Button queueButton = null;
            queueButton = CreateSmallPanelButton("Queue", () =>
            {
                if (AddSongToQueue(songMeta, false))
                {
                    ShowQueueAddedButtonFeedback(queueButton);
                }
            });
            row.Add(queueButton);
            row.Add(CreateSmallPanelButton("🎵", () => ShowAddToPlaylistDialog(songMeta)));
            row.Add(CreateFavoriteStarButton(songMeta, () => UpdateHistoryPanel()));

            historySection.Add(row);
        }
        historyResultsContainer.Add(historySection);
    }

    private Color GetPanelTopBorderColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.42f, 0.48f, 0.58f, 0.34f);
        }
        if (theme == 2)
        {
            return new Color(0.18f, 0.64f, 0.34f, 0.38f);
        }
        if (theme == 3)
        {
            return new Color(0.22f, 0.50f, 1f, 0.38f);
        }
        if (theme == 4)
        {
            return new Color(0.95f, 0.24f, 0.26f, 0.38f);
        }
        if (theme == 5)
        {
            return new Color(1f, 0.70f, 0.22f, 0.40f);
        }
        return new Color(0.55f, 0.34f, 1f, 0.38f);
    }

    private Color GetPanelSideBorderColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.34f, 0.40f, 0.50f, 0.26f);
        }
        if (theme == 2)
        {
            return new Color(0.14f, 0.50f, 0.27f, 0.24f);
        }
        if (theme == 3)
        {
            return new Color(0.18f, 0.38f, 0.82f, 0.24f);
        }
        if (theme == 4)
        {
            return new Color(0.78f, 0.18f, 0.20f, 0.24f);
        }
        if (theme == 5)
        {
            return new Color(0.86f, 0.56f, 0.16f, 0.26f);
        }
        return new Color(0.55f, 0.34f, 1f, 0.24f);
    }

    private Color GetAccentColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.66f, 0.74f, 0.86f, 0.92f);
        }
        if (theme == 2)
        {
            return new Color(0.18f, 0.94f, 0.44f, 0.96f);
        }
        if (theme == 3)
        {
            return new Color(0.22f, 0.58f, 1f, 0.96f);
        }
        if (theme == 4)
        {
            return new Color(1f, 0.28f, 0.30f, 0.96f);
        }
        if (theme == 5)
        {
            return new Color(1f, 0.74f, 0.22f, 0.98f);
        }
        return new Color(0.62f, 0.36f, 1f, 0.96f);
    }

    private Color GetAccentHoverColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.52f, 0.60f, 0.72f, 0.62f);
        }
        if (theme == 2)
        {
            return new Color(0.12f, 0.64f, 0.30f, 0.72f);
        }
        if (theme == 3)
        {
            return new Color(0.16f, 0.38f, 0.86f, 0.72f);
        }
        if (theme == 4)
        {
            return new Color(0.78f, 0.18f, 0.22f, 0.72f);
        }
        if (theme == 5)
        {
            return new Color(0.88f, 0.54f, 0.12f, 0.74f);
        }
        return new Color(0.45f, 0.33f, 0.78f, 0.72f);
    }

    private Color GetButtonBaseColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 2)
        {
            return new Color(0.08f, 0.15f, 0.10f, 0.98f);
        }
        if (theme == 3)
        {
            return new Color(0.08f, 0.12f, 0.22f, 0.98f);
        }
        if (theme == 4)
        {
            return new Color(0.18f, 0.08f, 0.10f, 0.98f);
        }
        if (theme == 5)
        {
            return new Color(0.18f, 0.14f, 0.07f, 0.98f);
        }
        return new Color(0.13f, 0.16f, 0.24f, 0.98f);
    }

    private Color GetButtonHoverColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.17f, 0.21f, 0.30f, 0.99f);
        }
        if (theme == 2)
        {
            return new Color(0.10f, 0.20f, 0.13f, 0.99f);
        }
        if (theme == 3)
        {
            return new Color(0.10f, 0.15f, 0.30f, 0.99f);
        }
        if (theme == 4)
        {
            return new Color(0.24f, 0.10f, 0.12f, 0.99f);
        }
        if (theme == 5)
        {
            return new Color(0.24f, 0.18f, 0.08f, 0.99f);
        }
        return new Color(0.16f, 0.17f, 0.28f, 0.99f);
    }

    private Color GetButtonBorderColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.34f, 0.40f, 0.52f, 0.70f);
        }
        if (theme == 2)
        {
            return new Color(0.12f, 0.45f, 0.24f, 0.92f);
        }
        if (theme == 3)
        {
            return new Color(0.16f, 0.34f, 0.68f, 0.92f);
        }
        if (theme == 4)
        {
            return new Color(0.62f, 0.16f, 0.18f, 0.92f);
        }
        if (theme == 5)
        {
            return new Color(0.72f, 0.46f, 0.12f, 0.94f);
        }
        return new Color(0.30f, 0.24f, 0.55f, 0.92f);
    }

    private Color GetButtonPulseColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.20f, 0.24f, 0.34f, 0.99f);
        }
        if (theme == 2)
        {
            return new Color(0.10f, 0.26f, 0.14f, 0.99f);
        }
        if (theme == 3)
        {
            return new Color(0.10f, 0.18f, 0.38f, 0.99f);
        }
        if (theme == 4)
        {
            return new Color(0.30f, 0.10f, 0.12f, 0.99f);
        }
        if (theme == 5)
        {
            return new Color(0.30f, 0.22f, 0.08f, 0.99f);
        }
        return new Color(0.23f, 0.18f, 0.38f, 0.99f);
    }

    private Color GetButtonTextColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 2)
        {
            return new Color(0.94f, 1f, 0.96f, 1f);
        }
        if (theme == 5)
        {
            return new Color(1f, 0.98f, 0.92f, 1f);
        }
        if (theme == 0)
        {
            return new Color(0.96f, 0.97f, 1f, 1f);
        }
        return new Color(0.98f, 0.95f, 1f, 1f);
    }

    private Color GetRowColor()
    {
        return new Color(1f, 1f, 1f, 0.055f);
    }

    private Color GetRowHoverColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.60f, 0.68f, 0.78f, 0.12f);
        }
        if (theme == 2)
        {
            return new Color(0.18f, 0.64f, 0.34f, 0.16f);
        }
        if (theme == 3)
        {
            return new Color(0.22f, 0.50f, 1f, 0.16f);
        }
        if (theme == 4)
        {
            return new Color(0.95f, 0.24f, 0.26f, 0.16f);
        }
        if (theme == 5)
        {
            return new Color(1f, 0.70f, 0.22f, 0.18f);
        }
        return new Color(0.55f, 0.34f, 1f, 0.16f);
    }

    private Color GetRowPulseColor()
    {
        int theme = GetUiThemeIndex();
        if (theme == 0)
        {
            return new Color(0.60f, 0.68f, 0.78f, 0.18f);
        }
        if (theme == 2)
        {
            return new Color(0.18f, 0.64f, 0.34f, 0.24f);
        }
        if (theme == 3)
        {
            return new Color(0.22f, 0.50f, 1f, 0.24f);
        }
        if (theme == 4)
        {
            return new Color(0.95f, 0.24f, 0.26f, 0.24f);
        }
        if (theme == 5)
        {
            return new Color(1f, 0.70f, 0.22f, 0.26f);
        }
        return new Color(0.55f, 0.34f, 1f, 0.24f);
    }

    private VisualElement CreatePanelRow()
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 5;
        row.style.marginBottom = 5;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 8;
        row.style.paddingTop = 7;
        row.style.paddingBottom = 7;
        row.style.backgroundColor = GetRowColor();
        row.style.borderTopWidth = 1f;
        row.style.borderBottomWidth = 1f;
        row.style.borderLeftWidth = 1f;
        row.style.borderRightWidth = 1f;
        row.style.borderTopColor = GetPanelTopBorderColor();
        row.style.borderBottomColor = GetPanelSideBorderColor();
        row.style.borderLeftColor = GetPanelSideBorderColor();
        row.style.borderRightColor = GetPanelSideBorderColor();
        row.style.borderTopLeftRadius = 12;
        row.style.borderTopRightRadius = 12;
        row.style.borderBottomLeftRadius = 12;
        row.style.borderBottomRightRadius = 12;
        RegisterRowHover(row);
        return row;
    }

    private void RegisterRowHover(VisualElement row)
    {
        if (row == null)
        {
            return;
        }

        row.RegisterCallback<MouseEnterEvent>(evt =>
        {
            row.style.backgroundColor = GetRowHoverColor();
        });
        row.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            row.style.backgroundColor = GetRowColor();
        });
    }

    private VisualElement GetButtonVisual(Button button)
    {
        if (button == null)
        {
            return null;
        }
        return button.Q<VisualElement>("betterJukeboxButtonVisual");
    }

    private Label GetButtonVisualLabel(Button button)
    {
        if (button == null)
        {
            return null;
        }
        return button.Q<Label>("betterJukeboxButtonLabel");
    }

    private void SetButtonVisualText(Button button, string text)
    {
        if (button == null)
        {
            return;
        }

        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.text = text ?? "";
            return;
        }

        button.text = text ?? "";
    }

    private void AddButtonVisual(Button button, string text, string fontClass, float padLeft, float padRight, float padTop, float padBottom, float radius)
    {
        if (button == null)
        {
            return;
        }

        button.text = "";
        button.AddToClassList(fontClass);
        button.style.position = Position.Relative;
        button.style.overflow = Overflow.Hidden;
        button.style.flexDirection = FlexDirection.Row;
        button.style.alignItems = Align.Center;
        button.style.justifyContent = Justify.Center;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.borderTopLeftRadius = radius;
        button.style.borderTopRightRadius = radius;
        button.style.borderBottomLeftRadius = radius;
        button.style.borderBottomRightRadius = radius;
        button.style.borderTopWidth = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderLeftWidth = 0f;
        button.style.borderRightWidth = 0f;

        VisualElement visual = new VisualElement();
        visual.name = "betterJukeboxButtonVisual";
        visual.style.position = Position.Absolute;
        visual.style.left = new StyleLength(new Length(0, LengthUnit.Pixel));
        visual.style.right = new StyleLength(new Length(0, LengthUnit.Pixel));
        visual.style.top = new StyleLength(new Length(0, LengthUnit.Pixel));
        visual.style.bottom = new StyleLength(new Length(0, LengthUnit.Pixel));
        visual.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        visual.style.borderTopLeftRadius = radius;
        visual.style.borderTopRightRadius = radius;
        visual.style.borderBottomLeftRadius = radius;
        visual.style.borderBottomRightRadius = radius;
        visual.style.borderTopWidth = 2f;
        visual.style.borderBottomWidth = 2f;
        visual.style.borderLeftWidth = 2f;
        visual.style.borderRightWidth = 2f;
        visual.pickingMode = PickingMode.Ignore;

        Label label = new Label(text ?? "");
        label.name = "betterJukeboxButtonLabel";
        label.AddToClassList(fontClass);
        label.AddToClassList("textShadow");
        label.style.position = Position.Relative;
        label.style.paddingLeft = padLeft;
        label.style.paddingRight = padRight;
        label.style.paddingTop = padTop;
        label.style.paddingBottom = padBottom;
        label.style.marginLeft = 0f;
        label.style.marginRight = 0f;
        label.style.marginTop = 0f;
        label.style.marginBottom = 0f;
        label.pickingMode = PickingMode.Ignore;

        button.Add(visual);
        button.Add(label);
        ApplyButtonNormalStyle(button);
    }

    private void ApplyButtonNormalStyle(Button button)
    {
        if (button == null)
        {
            return;
        }

        VisualElement visual = GetButtonVisual(button);
        button.style.backgroundColor = GetButtonBaseColor();
        button.style.color = GetButtonTextColor();
        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.style.color = GetButtonTextColor();
        }
        button.style.borderTopColor = new Color(0f, 0f, 0f, 0f);
        button.style.borderBottomColor = new Color(0f, 0f, 0f, 0f);
        button.style.borderLeftColor = new Color(0f, 0f, 0f, 0f);
        button.style.borderRightColor = new Color(0f, 0f, 0f, 0f);
        if (visual != null)
        {
            visual.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            visual.style.borderTopColor = GetButtonBorderColor();
            visual.style.borderBottomColor = GetButtonBorderColor();
            visual.style.borderLeftColor = GetButtonBorderColor();
            visual.style.borderRightColor = GetButtonBorderColor();
        }
    }

    private void ApplyButtonHoverStyle(Button button)
    {
        if (button == null)
        {
            return;
        }

        VisualElement visual = GetButtonVisual(button);
        button.style.backgroundColor = GetButtonHoverColor();
        button.style.color = Color.white;
        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.style.color = Color.white;
        }
        if (visual != null)
        {
            visual.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            visual.style.borderTopColor = GetAccentHoverColor();
            visual.style.borderBottomColor = GetAccentHoverColor();
            visual.style.borderLeftColor = GetAccentHoverColor();
            visual.style.borderRightColor = GetAccentHoverColor();
        }
    }

    private void ApplyButtonPulseStyle(Button button)
    {
        if (button == null)
        {
            return;
        }

        VisualElement visual = GetButtonVisual(button);
        button.style.backgroundColor = GetButtonPulseColor();
        button.style.color = Color.white;
        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.style.color = Color.white;
        }
        if (visual != null)
        {
            visual.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            visual.style.borderTopColor = GetAccentHoverColor();
            visual.style.borderBottomColor = GetAccentHoverColor();
            visual.style.borderLeftColor = GetAccentHoverColor();
            visual.style.borderRightColor = GetAccentHoverColor();
        }
    }

    private void RegisterButtonThemeHover(Button button)
    {
        if (button == null)
        {
            return;
        }

        ApplyButtonNormalStyle(button);
        button.RegisterCallback<AttachToPanelEvent>(evt =>
        {
            ApplyButtonNormalStyle(button);
        });
        button.RegisterCallback<MouseEnterEvent>(evt =>
        {
            ApplyButtonHoverStyle(button);
        });
        button.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            ApplyButtonNormalStyle(button);
        });
        button.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            ApplyButtonNormalStyle(button);
        });
        button.RegisterCallback<BlurEvent>(evt =>
        {
            ApplyButtonNormalStyle(button);
        });
    }

    private void ApplyThemeToVisibleElements()
    {
        ApplyPanelTheme(searchPanel);
        ApplyPanelTheme(queuePanel);
        ApplyPanelTheme(companionPanel);
        ApplyPanelTheme(historyPanel);
        ApplyPanelTheme(settingsPanel);
        ApplySearchFilterRowTheme();
        ApplySearchInputTheme();
        ApplyBrandLogoTheme();
        ApplyCompanionStatusStyles();
    }


    private void ApplySearchFilterRowTheme()
    {
        try
        {
            VisualElement filterRow = searchPanel != null ? searchPanel.Q<VisualElement>("betterJukeboxSearchFilterRow") : null;
            if (filterRow != null)
            {
                filterRow.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
                ApplyThemedBorder(filterRow);
            }
        }
        catch
        {
        }
    }

    private void ApplyThemedBorder(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        element.style.borderTopWidth = 1f;
        element.style.borderBottomWidth = 1f;
        element.style.borderLeftWidth = 1f;
        element.style.borderRightWidth = 1f;
        element.style.borderTopColor = GetPanelTopBorderColor();
        element.style.borderBottomColor = GetPanelSideBorderColor();
        element.style.borderLeftColor = GetPanelSideBorderColor();
        element.style.borderRightColor = GetPanelSideBorderColor();
    }

    private void ApplyBrandLogoTheme()
    {
        Color accentColor = GetAccentColor();

        if (brandLogo != null)
        {
            brandLogo.style.opacity = 1f;
        }
        if (brandLogoIconLabel != null)
        {
            brandLogoIconLabel.style.color = accentColor;
            brandLogoIconLabel.style.opacity = 1f;
        }
        if (brandLogoNameLabel != null)
        {
            brandLogoNameLabel.style.color = Color.white;
            brandLogoNameLabel.style.opacity = 1f;
        }
        if (brandLogoAccentLabel != null)
        {
            brandLogoAccentLabel.style.color = accentColor;
            brandLogoAccentLabel.style.opacity = 1f;
        }
    }

    private void ApplyPanelTheme(VisualElement panel)
    {
        if (panel == null)
        {
            return;
        }

        panel.style.borderTopColor = GetPanelTopBorderColor();
        panel.style.borderBottomColor = GetPanelSideBorderColor();
        panel.style.borderLeftColor = GetPanelSideBorderColor();
        panel.style.borderRightColor = GetPanelSideBorderColor();
    }

    private void ApplyButtonTreeTheme(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        Button button = element as Button;
        if (button != null)
        {
            ApplyButtonNormalStyle(button);
        }

        for (int i = 0; i < element.childCount; i++)
        {
            ApplyButtonTreeTheme(element[i]);
        }
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

    private void PulseClickedButton(Button button)
    {
        if (button == null)
        {
            return;
        }

        ApplyButtonPulseStyle(button);
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.12f, () =>
        {
            if (button != null)
            {
                ApplyButtonNormalStyle(button);
            }
        });
    }

    private Button CreateSmallPanelButton(string text, Action clicked)
    {
        Button button = null;
        button = new Button(() =>
        {
            PulseClickedButton(button);
            if (clicked != null)
            {
                clicked();
            }
        });
        button.AddToClassList("tinyFont");
        button.style.marginLeft = 4;
        button.style.marginRight = 4;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        AddButtonVisual(button, text, "tinyFont", 11f, 11f, 6f, 6f, 11f);
        RegisterButtonThemeHover(button);
        return button;
    }

    private VisualElement CreateEmptyState(string iconText, string titleText, string bodyText)
    {
        VisualElement empty = new VisualElement();
        empty.style.flexDirection = FlexDirection.Column;
        empty.style.alignItems = Align.Center;
        empty.style.justifyContent = Justify.Center;
        empty.style.paddingTop = 28;
        empty.style.paddingBottom = 28;
        empty.style.marginTop = 10;
        empty.style.backgroundColor = new Color(1f, 1f, 1f, 0.045f);
        empty.style.borderTopLeftRadius = 16;
        empty.style.borderTopRightRadius = 16;
        empty.style.borderBottomLeftRadius = 16;
        empty.style.borderBottomRightRadius = 16;

        Label icon = new Label(iconText);
        icon.AddToClassList("smallFont");
        icon.style.color = GetAccentColor();
        icon.style.marginBottom = 8;
        empty.Add(icon);

        Label title = CreatePanelLabel(titleText);
        title.style.marginRight = 0f;
        title.style.marginBottom = 4;
        title.style.color = Color.white;
        empty.Add(title);

        Label body = CreatePanelLabel(bodyText);
        body.style.marginRight = 0f;
        body.style.opacity = 0.72f;
        empty.Add(body);

        return empty;
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
        PrepareBetterJukeboxUiForSceneTransition();
        HideActionOverlay();
        LoadSong(songMeta);
    }

    private void ShowQueueAddedButtonFeedback(Button button)
    {
        if (button == null)
        {
            return;
        }

        SetButtonVisualText(button, "Added!");
        ApplyButtonPulseStyle(button);
        Button feedbackButton = button;
        AwaitableUtils.ExecuteAfterDelayInSecondsAsync(0.85f, () =>
        {
            if (feedbackButton != null)
            {
                SetButtonVisualText(feedbackButton, "Queue");
                ApplyButtonNormalStyle(feedbackButton);
            }
        });
    }

    private bool AddSongToQueue(SongMeta songMeta)
    {
        return AddSongToQueue(songMeta, false);
    }

    private bool AddSongToQueue(SongMeta songMeta, bool showSuccessNotification)
    {

        if (!TryAddSongToRealQueue(songMeta))
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not add song to real queue via SongQueueManager. Song was not queued.");
            NotificationManager.CreateNotification(Translation.Of("Could not add to queue: " + songMeta.GetArtistDashTitle()));
            UpdateSearchResults(searchTextField?.value ?? "");
            UpdateQueuePanel();
            UpdateQueueBadge(true);
            return false;
        }

        if (showSuccessNotification)
        {
            NotificationManager.CreateNotification(Translation.Of("Added to queue: " + songMeta.GetArtistDashTitle()));
        }

        MarkQueueChanged();
        if (queuePanelIsVisible)
        {
            UpdateQueuePanel();
        }
        UpdateQueueBadge(true);
        return true;
    }


    private Button CreateQueueMoveHoldButton(string normalIcon, string middleIcon, string readyIcon, int index, int direction, VisualElement row)
    {
        Button button = null;
        button = new Button(() =>
        {
            // Normal Button click remains the single-step move.
            // If a completed hold already moved the item to the edge, skip this click.
            if (queueHoldMoveSuppressNextClick)
            {
                queueHoldMoveSuppressNextClick = false;
                return;
            }
            MoveRealQueueItem(index, direction);
        });
        button.tooltip = direction < 0 ? "Move up. Hold to move to top." : "Move down. Hold to move to bottom.";
        button.AddToClassList("tinyFont");
        button.style.marginLeft = 4;
        button.style.marginRight = 4;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        AddButtonVisual(button, normalIcon, "tinyFont", 11f, 11f, 6f, 6f, 11f);

        VisualElement fill = new VisualElement();
        fill.name = "betterJukeboxQueueHoldFill";
        fill.style.position = Position.Absolute;
        fill.style.left = new StyleLength(new Length(0, LengthUnit.Pixel));
        fill.style.top = new StyleLength(new Length(0, LengthUnit.Pixel));
        fill.style.bottom = new StyleLength(new Length(0, LengthUnit.Pixel));
        fill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
        fill.style.backgroundColor = GetQueueHoldFillColor(0f);
        fill.style.borderTopLeftRadius = 11f;
        fill.style.borderTopRightRadius = 11f;
        fill.style.borderBottomLeftRadius = 11f;
        fill.style.borderBottomRightRadius = 11f;
        fill.pickingMode = PickingMode.Ignore;
        button.Insert(1, fill);

        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.BringToFront();
        }

        button.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                BeginQueueHoldMove(button, row, index, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                EndQueueHoldMove(button, index, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                BeginQueueHoldMove(button, row, index, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (evt.button == 0)
            {
                EndQueueHoldMove(button, index, direction);
            }
        }, TrickleDown.TrickleDown);

        button.RegisterCallback<PointerCancelEvent>(evt =>
        {
            CancelQueueHoldMove(button);
        });

        button.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            CancelQueueHoldMove(button);
        });

        RegisterButtonThemeHover(button);
        return button;
    }

    private void BeginQueueHoldMove(Button button, VisualElement row, int index, int direction)
    {
        if (button == null)
        {
            return;
        }

        // Ignore duplicate MouseDown/PointerDown events for the same press.
        if (queueHoldMoveButton == button && queueHoldMoveStartedAt >= 0f)
        {
            return;
        }

        queueHoldMoveButton = button;
        queueHoldMoveRow = row;
        queueHoldMoveIndex = index;
        queueHoldMoveDirection = direction;
        queueHoldMoveStartedAt = Time.unscaledTime;
        ApplyQueueHoldMoveProgress(button, row, direction, 0f);
    }

    private void EndQueueHoldMove(Button button, int index, int direction)
    {
        if (button == null || button != queueHoldMoveButton)
        {
            return;
        }

        float progress = GetQueueHoldMoveProgress();
        ResetQueueHoldMoveVisual(button, queueHoldMoveRow);
        queueHoldMoveButton = null;
        queueHoldMoveRow = null;
        queueHoldMoveIndex = -1;
        queueHoldMoveStartedAt = -1f;

        if (progress >= 1f)
        {
            queueHoldMoveSuppressNextClick = true;
            MoveRealQueueItemToEdge(index, direction);
        }
        // Short press is handled by the normal Button click action.
        // This keeps single-click behavior identical to the old up/down buttons.
    }

    private void CancelQueueHoldMove(Button button)
    {
        if (button == null || button != queueHoldMoveButton)
        {
            return;
        }

        ResetQueueHoldMoveVisual(button, queueHoldMoveRow);
        queueHoldMoveButton = null;
        queueHoldMoveRow = null;
        queueHoldMoveIndex = -1;
        queueHoldMoveStartedAt = -1f;
    }

    private void ProcessQueueHoldMoveProgress()
    {
        if (queueHoldMoveButton == null || queueHoldMoveStartedAt < 0f)
        {
            return;
        }

        ApplyQueueHoldMoveProgress(queueHoldMoveButton, queueHoldMoveRow, queueHoldMoveDirection, GetQueueHoldMoveProgress());
    }

    private float GetQueueHoldMoveProgress()
    {
        if (queueHoldMoveStartedAt < 0f)
        {
            return 0f;
        }

        float heldSeconds = Time.unscaledTime - queueHoldMoveStartedAt;
        if (heldSeconds < QueueHoldMoveGraceSeconds)
        {
            return 0f;
        }

        return Mathf.Clamp01((heldSeconds - QueueHoldMoveGraceSeconds) / QueueHoldMoveReadySeconds);
    }

    private void ApplyQueueHoldMoveProgress(Button button, VisualElement row, int direction, float progress)
    {
        if (button == null)
        {
            return;
        }

        VisualElement fill = button.Q("betterJukeboxQueueHoldFill");
        if (fill != null)
        {
            fill.style.width = new StyleLength(new Length(Mathf.Clamp01(progress) * 100f, LengthUnit.Percent));
            fill.style.backgroundColor = GetQueueHoldFillColor(progress);
        }

        string icon = direction < 0 ? "↑" : "↓";
        if (progress >= 1f)
        {
            icon = direction < 0 ? "⇈" : "⇊";
        }
        else if (progress >= 0.45f)
        {
            icon = direction < 0 ? "⇧" : "⇩";
        }
        SetButtonVisualText(button, icon);

        Label label = GetButtonVisualLabel(button);
        if (label != null)
        {
            label.style.color = progress >= 1f ? Color.white : GetButtonTextColor();
        }

        if (row != null)
        {
            row.style.backgroundColor = progress >= 1f
                ? GetRowPulseColor()
                : new Color(0f, 0f, 0f, 0.25f + (0.20f * Mathf.Clamp01(progress)));
        }
    }

    private Color GetQueueHoldFillColor(float progress)
    {
        Color accent = GetAccentColor();
        accent.a = 0.18f + (0.38f * Mathf.Clamp01(progress));
        return accent;
    }

    private void ResetQueueHoldMoveVisual(Button button, VisualElement row)
    {
        if (button != null)
        {
            VisualElement fill = button.Q("betterJukeboxQueueHoldFill");
            if (fill != null)
            {
                fill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
            }
            ApplyButtonNormalStyle(button);
        }

        if (row != null)
        {
            row.style.backgroundColor = GetRowColor();
        }
    }

    private void MoveRealQueueItemToEdge(int index, int direction)
    {
        List<object> entries = GetRealSongQueueEntries();
        if (index < 0 || index >= entries.Count || entries.Count <= 1)
        {
            return;
        }

        int targetIndex = direction < 0 ? 0 : entries.Count - 1;
        if (index == targetIndex)
        {
            return;
        }

        object entry = entries[index];
        entries.RemoveAt(index);
        entries.Insert(targetIndex, entry);
        SetRealSongQueueEntries(entries);
        MarkQueueChanged();
        UpdateQueuePanel();
        UpdateQueueBadge(true);
    }

    private void MoveQueueItem(int index, int direction)
    {
        MoveRealQueueItem(index, direction);
    }

    private void RemoveQueueItem(int index)
    {
        RemoveRealQueueItem(index);
    }



    private void MoveRealQueueItemTo(int sourceIndex, int targetIndex)
    {
        List<object> entries = GetRealSongQueueEntries();
        if (sourceIndex < 0 || sourceIndex >= entries.Count || targetIndex < 0 || targetIndex >= entries.Count)
        {
            return;
        }

        object entry = entries[sourceIndex];
        entries.RemoveAt(sourceIndex);
        entries.Insert(targetIndex, entry);
        SetRealSongQueueEntries(entries);
        MarkQueueChanged();
        UpdateQueuePanel();
        UpdateQueueBadge(true);
    }

    private void MoveRealQueueItem(int index, int direction)
    {
        List<object> entries = GetRealSongQueueEntries();
        int newIndex = index + direction;
        if (index < 0 || index >= entries.Count || newIndex < 0 || newIndex >= entries.Count)
        {
            return;
        }

        object entry = entries[index];
        entries.RemoveAt(index);
        entries.Insert(newIndex, entry);
        SetRealSongQueueEntries(entries);
        MarkQueueChanged();
        UpdateQueuePanel();
        UpdateQueueBadge(true);
    }

    private void RemoveRealQueueItem(int index)
    {
        List<object> entries = GetRealSongQueueEntries();
        if (index < 0 || index >= entries.Count)
        {
            return;
        }

        object entry = entries[index];
        UnregisterQueueEntryOverride(entry);
        if (!InvokeSongQueueManager("RemoveSongQueueEntry", entry))
        {
            entries.RemoveAt(index);
            SetRealSongQueueEntries(entries);
        }
        MarkQueueChanged();
        UpdateQueuePanel();
        UpdateQueueBadge(true);
    }

    private void ClearRealQueue()
    {
        List<object> entries = GetRealSongQueueEntries();
        if (entries.Count == 0)
        {
            return;
        }

        foreach (object entry in entries)
        {
            UnregisterQueueEntryOverride(entry);
            InvokeSongQueueManager("RemoveSongQueueEntry", entry);
        }
        MarkQueueChanged();
        UpdateQueuePanel();
        UpdateQueueBadge(true);
    }

    private void ShuffleRealQueue()
    {
        List<object> entries = GetRealSongQueueEntries();
        if (entries.Count <= 1)
        {
            return;
        }

        System.Random random = new System.Random();
        for (int i = entries.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            object temp = entries[i];
            entries[i] = entries[j];
            entries[j] = temp;
        }

        SetRealSongQueueEntries(entries);
        MarkQueueChanged();
        UpdateQueuePanel();
        UpdateQueueBadge(true);
    }

    private bool SetRealSongQueueEntries(List<object> entries)
    {
        // Melody Mania's mod compiler has limited reflection support, so do not build
        // generic lists dynamically here. Instead, rewrite the real queue by using the
        // public queue API one entry at a time. This keeps Companion App and BetterJukebox
        // synced without relying on unsupported reflection helpers.
        if (songQueueManager == null)
        {
            return false;
        }

        List<object> currentEntries = GetRealSongQueueEntries();
        foreach (object entry in currentEntries)
        {
            InvokeSongQueueManager("RemoveSongQueueEntry", entry);
        }

        bool allOk = true;
        foreach (object entry in entries)
        {
            if (!InvokeSongQueueManager("AddSongQueueEntry", entry))
            {
                allOk = false;
            }
        }

        if (!allOk)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not rewrite all real queue entries");
        }
        return allOk;
    }

    private bool InvokeSongQueueManager(string methodName, object argument)
    {
        if (songQueueManager == null)
        {
            return false;
        }

        Type type = songQueueManager.GetType();
        foreach (System.Reflection.MethodInfo methodInfo in type.GetMethods().Where(it => it.Name == methodName))
        {
            System.Reflection.ParameterInfo[] parameters = methodInfo.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }
            if (argument == null || parameters[0].ParameterType.IsAssignableFrom(argument.GetType()))
            {
                try
                {
                    methodInfo.Invoke(songQueueManager, new object[] { argument });
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Failed to invoke {methodName}: {ex.Message}");
                }
            }
        }
        return false;
    }

    private bool TryAddSongToRealQueue(SongMeta songMeta)
    {
        if (songQueueManager == null || songMeta == null)
        {
            return false;
        }

        Type managerType = songQueueManager.GetType();
        foreach (System.Reflection.MethodInfo methodInfo in managerType.GetMethods().Where(it => it.Name == "AddSongQueueEntry"))
        {
            System.Reflection.ParameterInfo[] parameters = methodInfo.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            object argument = CreateSongQueueEntryArgument(parameters[0].ParameterType, songMeta);
            if (argument == null)
            {
                continue;
            }

            try
            {
                methodInfo.Invoke(songQueueManager, new object[] { argument });
                RegisterQueueEntryOverride(argument, songMeta);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Failed to add real queue entry via {methodInfo}: {ex.Message}");
            }
        }
        return false;
    }

    private void RegisterQueueEntryOverride(object entry, SongMeta songMeta)
    {
        if (entry == null || songMeta == null)
        {
            return;
        }

        string displayName = songMeta.GetArtistDashTitle();
        if (!queueEntryDisplayNameOverrides.ContainsKey(entry))
        {
            queueEntryDisplayNameOverrides.Add(entry, displayName);
        }
        else
        {
            queueEntryDisplayNameOverrides[entry] = displayName;
        }

        if (!queueEntrySongMetaOverrides.ContainsKey(entry))
        {
            queueEntrySongMetaOverrides.Add(entry, songMeta);
        }
        else
        {
            queueEntrySongMetaOverrides[entry] = songMeta;
        }
    }

    private void UnregisterQueueEntryOverride(object entry)
    {
        if (entry == null)
        {
            return;
        }

        if (queueEntryDisplayNameOverrides.ContainsKey(entry))
        {
            queueEntryDisplayNameOverrides.Remove(entry);
        }
        if (queueEntrySongMetaOverrides.ContainsKey(entry))
        {
            queueEntrySongMetaOverrides.Remove(entry);
        }
    }

    private object CreateSongQueueEntryArgument(Type parameterType, SongMeta songMeta)
    {
        if (parameterType == null || songMeta == null)
        {
            return null;
        }

        if (parameterType.IsAssignableFrom(songMeta.GetType()))
        {
            return songMeta;
        }

        object dto = null;
        try
        {
            var ctor = parameterType.GetConstructor(new Type[0]);
            if (ctor != null)
            {
                dto = ctor.Invoke(new object[0]);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not construct queue DTO of type {parameterType.FullName}: {ex.Message}");
            dto = null;
        }

        if (dto == null)
        {
            return null;
        }

        string songMetaId = GetSongMetaId(songMeta);
        List<string> ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(songMetaId))
        {
            ids.Add(songMetaId);
        }

        string displayName = songMeta.GetArtistDashTitle();
        string title = songMeta.Title ?? displayName;
        string artist = songMeta.Artist ?? "";
        string songHash = GetMelodyManiaSongHash(songMeta);
        string queueEntryId = Guid.NewGuid().ToString();

        // Melody Mania's real queue uses SongDto as the song identity for Companion App
        // and for the built-in Song Queue view. The important fields are Artist, Title,
        // and Hash. Hash is md5(artist + ":" + title) + ":" + md5(full txt file path).
        object songDto = CreateNestedDtoForMember(dto, "SongDto");
        if (songDto != null)
        {
            TrySetMemberValue(songDto, "Artist", artist);
            TrySetMemberValue(songDto, "Title", title);
            TrySetMemberValue(songDto, "Hash", songHash);
            TrySetMemberValue(dto, "SongDto", songDto);
        }

        TrySetMemberValue(dto, "Hash", songHash);
        TrySetMemberValue(dto, "SongHash", songHash);

        // The Companion App reads the real queue DTO, not BetterJukebox display overrides.
        // Therefore PC-created entries must carry their own visible name and unique identity.
        // If these fields are left at defaults, the phone can show placeholder data such as
        // "Very long name" and multiple PC entries can collapse into one visible object.
        TrySetMemberValue(dto, "Id", queueEntryId);
        TrySetMemberValue(dto, "ID", queueEntryId);
        TrySetMemberValue(dto, "Guid", queueEntryId);
        TrySetMemberValue(dto, "Uuid", queueEntryId);
        TrySetMemberValue(dto, "UUID", queueEntryId);
        TrySetMemberValue(dto, "GloballyUniqueId", queueEntryId);
        TrySetMemberValue(dto, "SongQueueEntryId", queueEntryId);
        TrySetMemberValue(dto, "SongQueueEntryDtoId", queueEntryId);

        TrySetMemberValue(dto, "Name", displayName);
        TrySetMemberValue(dto, "DisplayName", displayName);
        TrySetMemberValue(dto, "Label", displayName);
        TrySetMemberValue(dto, "SongName", displayName);
        TrySetMemberValue(dto, "SongDisplayName", displayName);
        TrySetMemberValue(dto, "Title", title);
        TrySetMemberValue(dto, "Artist", artist);

        if (ids.Count > 0)
        {
            TrySetMemberValue(dto, "GloballyUniqueSongMetaIds", ids);
            TrySetMemberValue(dto, "SongMetaIds", ids);
            TrySetMemberValue(dto, "SongIds", ids);
            TrySetMemberValue(dto, "GloballyUniqueSongMetaId", songMetaId);
            TrySetMemberValue(dto, "SongMetaId", songMetaId);
            TrySetMemberValue(dto, "SongId", songMetaId);

            try
            {
                CommonOnlineMultiplayer.SingSceneDataDto singSceneDataDto = new CommonOnlineMultiplayer.SingSceneDataDto();
                singSceneDataDto.GloballyUniqueSongMetaIds = ids;

                TrySetMemberValue(singSceneDataDto, "Name", displayName);
                TrySetMemberValue(singSceneDataDto, "DisplayName", displayName);
                TrySetMemberValue(singSceneDataDto, "SongName", displayName);
                TrySetMemberValue(singSceneDataDto, "SongDisplayName", displayName);
                TrySetMemberValue(singSceneDataDto, "Title", title);
                TrySetMemberValue(singSceneDataDto, "Artist", artist);
                TrySetMemberValue(singSceneDataDto, "Id", queueEntryId);
                TrySetMemberValue(singSceneDataDto, "Guid", queueEntryId);
                TrySetMemberValue(singSceneDataDto, "Uuid", queueEntryId);

                TrySetMemberValue(dto, "SingSceneDataDto", singSceneDataDto);
                TrySetMemberValue(dto, "SingSceneData", singSceneDataDto);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not create SingSceneDataDto for queue entry: {ex.Message}");
            }
        }

        object currentPlayerDataDto = TryCreateCurrentSingScenePlayerDataDto();
        if (currentPlayerDataDto != null)
        {
            // Important: SongQueueEntryDto stores player/mic assignments directly here.
            // Companion-created entries have SingScenePlayerDataDto.PlayerProfileNames,
            // PlayerProfileToMicProfileMap, and PlayerProfileToVoiceIdMap filled on the queue entry itself.
            TrySetMemberValue(dto, "SingScenePlayerDataDto", currentPlayerDataDto);
            TrySetMemberValue(dto, "SingScenePlayerData", currentPlayerDataDto);
            TrySetMemberValue(dto, "PlayerData", currentPlayerDataDto);
        }
        else
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not attach current SingScenePlayerDataDto to PC queue entry");
        }

        TrySetMemberValue(dto, "SongMeta", songMeta);
        TrySetMemberValue(dto, "Song", songMeta);

        return dto;
    }

    private object CreateNestedDtoForMember(object target, string memberName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        try
        {
            Type targetType = target.GetType();
            System.Reflection.PropertyInfo prop = targetType.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Type memberType = prop != null ? prop.PropertyType : null;
            if (memberType == null)
            {
                var field = targetType.GetField("<" + memberName + ">k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    ?? targetType.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    memberType = field.FieldType;
                }
            }

            if (memberType == null)
            {
                return null;
            }

            var ctor = memberType.GetConstructor(new Type[0]);
            if (ctor == null)
            {
                return null;
            }
            return ctor.Invoke(new object[0]);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not create nested DTO {memberName}: {ex.Message}");
            return null;
        }
    }

    private object TryCreateCurrentSingScenePlayerDataDto()
    {
        try
        {
            SingSceneData currentSingSceneData = SceneNavigator.GetSceneDataOrThrow<SingSceneData>();
            if (currentSingSceneData == null || currentSingSceneData.SingScenePlayerData == null)
            {
                return null;
            }

            // Use Melody Mania's own DTO converter. This is the same data shape seen in
            // Companion-created queue entries: PlayerProfileNames, PlayerProfileToMicProfileMap,
            // and PlayerProfileToVoiceIdMap on SingScenePlayerDataDto.
            object playerDataDto = DtoConverter.ToDto(currentSingSceneData.SingScenePlayerData);
            if (playerDataDto == null)
            {
                return null;
            }

            object names = GetMemberValue(playerDataDto, "PlayerProfileNames");
            object micMap = GetMemberValue(playerDataDto, "PlayerProfileToMicProfileMap");
            object voiceMap = GetMemberValue(playerDataDto, "PlayerProfileToVoiceIdMap");
            return playerDataDto;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not create current SingScenePlayerDataDto: {ex.Message}");
            return null;
        }
    }

    private string GetMelodyManiaSongHash(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return "";
        }

        string artist = songMeta.Artist ?? "";
        string title = songMeta.Title ?? "";
        string filePath = "";
        try
        {
            if (songMeta.FileInfo != null)
            {
                filePath = songMeta.FileInfo.FullName ?? "";
            }
        }
        catch { }

        return GetMd5Hex(artist + ":" + title) + ":" + GetMd5Hex(filePath);
    }

    private byte[] GetUtf8BytesNoEncodingType(string text)
    {
        if (text == null)
        {
            text = "";
        }

        List<byte> bytes = new List<byte>();
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];

            if (codePoint >= 0xD800 && codePoint <= 0xDBFF && i + 1 < text.Length)
            {
                int low = text[i + 1];
                if (low >= 0xDC00 && low <= 0xDFFF)
                {
                    codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + (low - 0xDC00);
                    i++;
                }
            }

            if (codePoint <= 0x7F)
            {
                bytes.Add((byte)codePoint);
            }
            else if (codePoint <= 0x7FF)
            {
                bytes.Add((byte)(0xC0 | (codePoint >> 6)));
                bytes.Add((byte)(0x80 | (codePoint & 0x3F)));
            }
            else if (codePoint <= 0xFFFF)
            {
                bytes.Add((byte)(0xE0 | (codePoint >> 12)));
                bytes.Add((byte)(0x80 | ((codePoint >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (codePoint & 0x3F)));
            }
            else
            {
                bytes.Add((byte)(0xF0 | (codePoint >> 18)));
                bytes.Add((byte)(0x80 | ((codePoint >> 12) & 0x3F)));
                bytes.Add((byte)(0x80 | ((codePoint >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (codePoint & 0x3F)));
            }
        }
        return bytes.ToArray();
    }

    private string GetMd5Hex(string text)
    {
        try
        {
            if (text == null)
            {
                text = "";
            }
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = GetUtf8BytesNoEncodingType(text);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                char[] chars = new char[hashBytes.Length * 2];
                const string hex = "0123456789abcdef";
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    chars[i * 2] = hex[hashBytes[i] >> 4];
                    chars[i * 2 + 1] = hex[hashBytes[i] & 15];
                }
                return new string(chars);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not create md5 hash: {ex.Message}");
            return "";
        }
    }



    private void DumpSongMetaForPcQueue(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return;
        }

        try
        {
            Type type = songMeta.GetType();

            string[] interestingNames = new string[]
            {
                "GloballyUniqueSongId", "GloballyUniqueId", "GloballyUniqueSongMetaId", "GloballyUniqueSongMetaIds",
                "LocallyUniqueSongId", "SongMetaId", "SongId", "Id", "ID", "Hash", "SongHash",
                "FileInfo", "File", "FilePath", "Directory", "Folder", "Mp3", "Audio", "Video", "Cover"
            };

            foreach (string name in interestingNames)
            {
                try
                {
                    object value = GetMemberValue(songMeta, name);
                    if (value != null)
                    {
                    }
                }
                catch { }
            }

            string props = string.Join(", ", type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(it => it.Name + ":" + it.PropertyType.Name)
                .Distinct()
                .OrderBy(it => it)
                .Take(180)
                .ToArray());

            foreach (System.Reflection.PropertyInfo prop in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(it => it.GetIndexParameters().Length == 0)
                .OrderBy(it => it.Name)
                .Take(180))
            {
                try
                {
                    object value = prop.GetValue(songMeta, null);
                    if (value != null)
                    {
                        string formatted = FormatDebugValue(value);
                        if (!string.IsNullOrWhiteSpace(formatted))
                        {
                        }
                    }
                }
                catch { }
            }

            string fields = string.Join(", ", type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(it => it.Name + ":" + it.FieldType.Name)
                .Distinct()
                .OrderBy(it => it)
                .Take(180)
                .ToArray());

            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .OrderBy(it => it.Name)
                .Take(180))
            {
                try
                {
                    object value = field.GetValue(songMeta);
                    if (value != null)
                    {
                        string formatted = FormatDebugValue(value);
                        if (!string.IsNullOrWhiteSpace(formatted))
                        {
                        }
                    }
                }
                catch { }
            }

            string methods = string.Join(", ", type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(it => it.GetParameters().Length == 0)
                .Select(it => it.Name + ":" + it.ReturnType.Name)
                .Distinct()
                .OrderBy(it => it)
                .Take(180)
                .ToArray());

            DumpSongMetaManagerForPcQueue();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - DumpSongMetaForPcQueue failed: {ex.Message}");
        }
    }

    private void DumpSongMetaManagerForPcQueue()
    {
        if (songMetaManager == null)
        {
            return;
        }
        try
        {
            Type type = songMetaManager.GetType();
            string methods = string.Join(", ", type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(it => it.Name + "(" + string.Join(",", it.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + "):" + it.ReturnType.Name)
                .Distinct()
                .OrderBy(it => it)
                .Where(it => it.IndexOf("Song", StringComparison.OrdinalIgnoreCase) >= 0 || it.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0 || it.IndexOf("Unique", StringComparison.OrdinalIgnoreCase) >= 0 || it.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(220)
                .ToArray());

            string props = string.Join(", ", type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(it => it.Name + ":" + it.PropertyType.Name)
                .Distinct()
                .OrderBy(it => it)
                .Where(it => it.IndexOf("Song", StringComparison.OrdinalIgnoreCase) >= 0 || it.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0 || it.IndexOf("Unique", StringComparison.OrdinalIgnoreCase) >= 0 || it.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(220)
                .ToArray());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - DumpSongMetaManagerForPcQueue failed: {ex.Message}");
        }
    }

    private string FormatDebugValue(object value)
    {
        if (value == null)
        {
            return "";
        }
        if (value is string)
        {
            return value.ToString();
        }
        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable != null)
        {
            List<string> parts = new List<string>();
            foreach (object item in enumerable)
            {
                if (item != null)
                {
                    parts.Add(item.ToString());
                }
                if (parts.Count >= 10)
                {
                    break;
                }
            }
            return string.Join(", ", parts.ToArray());
        }
        return value.ToString();
    }

    private string GetSongMetaId(SongMeta songMeta)
    {
        if (songMeta == null)
        {
            return null;
        }

        string[] methodNames = new string[]
        {
            "GetAndCacheGloballyUniqueId", "GetGloballyUniqueSongId", "GetGloballyUniqueId"
        };

        Type songMetaType = songMeta.GetType();
        foreach (string methodName in methodNames)
        {
            try
            {
                System.Reflection.MethodInfo methodInfo = songMetaType.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (methodInfo != null && methodInfo.GetParameters().Length == 0)
                {
                    object value = methodInfo.Invoke(songMeta, null);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                    {
                        return value.ToString();
                    }
                }
            }
            catch { }
        }

        string[] memberNames = new string[]
        {
            "GloballyUniqueSongId", "GloballyUniqueId", "GloballyUniqueSongMetaId", "GloballyUniqueSongMetaIds",
            "LocallyUniqueSongId", "SongMetaId", "Id", "ID"
        };

        foreach (string memberName in memberNames)
        {
            object value = GetMemberValue(songMeta, memberName);
            if (value == null)
            {
                continue;
            }
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (object item in enumerable)
                {
                    if (item != null)
                    {
                        return item.ToString();
                    }
                }
            }
            return value.ToString();
        }
        return null;
    }

    private void TrySetMemberValue(object target, string memberName, object value)
    {
        if (target == null || value == null)
        {
            return;
        }
        Type type = target.GetType();
        try
        {
            System.Reflection.PropertyInfo prop = type.GetProperty(memberName);
            if (prop != null && prop.CanWrite)
            {
                object converted = ConvertValueForMember(value, prop.PropertyType);
                if (converted != null || !prop.PropertyType.IsValueType)
                {
                    prop.SetValue(target, converted, null);
                    return;
                }
            }
        }
        catch { }
        try
        {
            var field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                object converted = ConvertValueForMember(value, field.FieldType);
                if (converted != null || !field.FieldType.IsValueType)
                {
                    field.SetValue(target, converted);
                }
            }
        }
        catch { }
    }

    private object ConvertValueForMember(object value, Type targetType)
    {
        if (value == null || targetType == null)
        {
            return null;
        }
        if (targetType.IsAssignableFrom(value.GetType()))
        {
            return value;
        }
        if (targetType == typeof(string))
        {
            return value.ToString();
        }
        if (targetType == typeof(List<string>))
        {
            List<string> list = new List<string>();
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (object item in enumerable)
                {
                    if (item != null)
                    {
                        list.Add(item.ToString());
                    }
                }
            }
            else
            {
                list.Add(value.ToString());
            }
            return list;
        }
        return null;
    }


    private void DumpRealQueueEntries(string context)
    {
        try
        {
            List<object> entries = GetRealSongQueueEntries();
            for (int i = 0; i < entries.Count && i < 5; i++)
            {
                DumpQueueObject(context + " entry " + i, entries[i]);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - DumpRealQueueEntries failed: {ex.Message}");
        }
    }

    private void DumpQueueObject(string context, object obj)
    {
        if (obj == null)
        {
            return;
        }

        try
        {
            Type type = obj.GetType();
            DumpQueueObjectDeep(context, obj, 0, new List<object>());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - DumpQueueObject failed for {context}: {ex.Message}");
        }
    }

    private void DumpQueueObjectDeep(string context, object obj, int depth, List<object> visited)
    {
        if (obj == null || depth > 3)
        {
            return;
        }
        if (visited.Contains(obj))
        {
            return;
        }
        visited.Add(obj);

        Type type = obj.GetType();
        string typeName = type.FullName ?? type.Name;
        bool interestingType = typeName.IndexOf("Song", StringComparison.OrdinalIgnoreCase) >= 0
                               || typeName.IndexOf("Queue", StringComparison.OrdinalIgnoreCase) >= 0
                               || typeName.IndexOf("SingScene", StringComparison.OrdinalIgnoreCase) >= 0
                               || typeName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
                               || typeName.IndexOf("Mic", StringComparison.OrdinalIgnoreCase) >= 0
                               || typeName.IndexOf("GameRound", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!interestingType && depth > 0)
        {
            return;
        }

        try
        {
            foreach (System.Reflection.PropertyInfo prop in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(it => it.GetIndexParameters().Length == 0)
                .OrderBy(it => it.Name)
                .Take(120))
            {
                object value = null;
                try { value = prop.GetValue(obj, null); } catch { }
                if (value == null)
                {
                    continue;
                }

                string formatted = FormatDebugValue(value);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                }

                if (ShouldDumpNestedQueueValue(value))
                {
                    DumpQueueObjectDeep(context + "." + prop.Name, value, depth + 1, visited);
                }
            }

            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .OrderBy(it => it.Name)
                .Take(120))
            {
                object value = null;
                try { value = field.GetValue(obj); } catch { }
                if (value == null)
                {
                    continue;
                }

                string formatted = FormatDebugValue(value);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                }

                if (ShouldDumpNestedQueueValue(value))
                {
                    DumpQueueObjectDeep(context + "." + field.Name, value, depth + 1, visited);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - DumpQueueObjectDeep failed for {context}: {ex.Message}");
        }
    }

    private bool ShouldDumpNestedQueueValue(object value)
    {
        if (value == null || value is string)
        {
            return false;
        }
        Type valueType = value.GetType();
        if (valueType.IsPrimitive || valueType.IsEnum)
        {
            return false;
        }
        string name = valueType.FullName ?? valueType.Name;
        return name.IndexOf("Song", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Queue", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("SingScene", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Mic", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("GameRound", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Dto", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void StartSingingNow()
    {
        singingModeStarted = true;
        HideNowPlayingCard(false);
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
        if (element == null || element == actionOverlay || element == searchPanel || element == queuePanel || element == historyPanel || element == progressContainer || element == nowPlayingCard)
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
                PrepareBetterJukeboxUiForSceneTransition();
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
        singingUiElements.ForEach(element => AnimationUtils.FadeInVisualElement(gameObject, element, 1f));
    }

    private void FadeOutSingingUiElements()
    {
        if (isFadedOut)
        {
            return;
        }

        isFadedOut = true;
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

            PrepareBetterJukeboxUiForSceneTransition();
            RequestKeepActionOverlayAfterSongChange();
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
            return;
        }

        PrepareBetterJukeboxUiForSceneTransition();
        RequestKeepActionOverlayAfterSongChange();
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

        PrepareBetterJukeboxUiForSceneTransition();
        sceneNavigator.LoadScene(EScene.SingScene, nextSingSceneData);
    }

    private SongMeta GetNextSongMeta(SongMeta currentSongMeta)
    {
        SongMeta nextBetterJukeboxQueueSongMeta = GetNextBetterJukeboxQueueSongMeta();
        if (nextBetterJukeboxQueueSongMeta != null)
        {
            return nextBetterJukeboxQueueSongMeta;
        }

        SongMeta nextSongQueueSongMeta = GetNextSongQueueSongMeta();
        if (nextSongQueueSongMeta != null)
        {
            return nextSongQueueSongMeta;
        }

        List<SongMeta> allSelectableSongMetas = GetAllSelectableSongMetas();
        if (allSelectableSongMetas.IsNullOrEmpty())
        {
            return null;
        }

        if (modSettings.RandomSelection)
        {
            return GetNextRandomSongMeta(allSelectableSongMetas);
        }
        else
        {
            return GetNextSequentialSongMeta(allSelectableSongMetas, currentSongMeta);
        }
    }

    private List<SongMeta> GetAllSelectableSongMetas()
    {
        bool usePlaylist = !nonPersistentSettings.PlaylistName.Value.IsNullOrEmpty() && nonPersistentSettings.PlaylistName.Value != UltraStarAllSongsPlaylist.Instance.Name;

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
        // Always prefer Melody Mania's native queue flow. This keeps Companion App and PC synced.
        // Only fall back to reading the first queue entry if the native flow fails.
        try
        {
            if (songQueueManager != null && singSceneControl != null)
            {
                SingSceneData singSceneData = songQueueManager.CreateNextSingSceneData(singSceneControl.PartyModeSceneData);
                SongMeta nativeNextSongMeta = singSceneData?.SongMetas?.FirstOrDefault();
                if (nativeNextSongMeta != null)
                {
                    return nativeNextSongMeta;
                }

                Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Native SongQueueManager returned no next song. Trying BetterJukebox queue-entry fallback.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Native SongQueueManager next-song failed: {ex.Message}. Trying BetterJukebox queue-entry fallback.");
        }

        return GetNextSongQueueSongMetaByReadingFirstQueueEntry();
    }

    private SongMeta GetNextBetterJukeboxCreatedSongQueueEntry()
    {
        try
        {
            List<object> entries = GetRealSongQueueEntries();
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            object firstEntry = entries[0];
            SongMeta songMeta;
            if (!queueEntrySongMetaOverrides.TryGetValue(firstEntry, out songMeta) || songMeta == null)
            {
                return null;
            }

            RemoveRealQueueItemWithoutRefreshing(0);
            return songMeta;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - PC queue-entry override failed: {ex.Message}");
            return null;
        }
    }

    private SongMeta GetNextSongQueueSongMetaByReadingFirstQueueEntry()
    {
        try
        {
            List<object> entries = GetRealSongQueueEntries();
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            object firstEntry = entries[0];
            SongMeta songMeta = FindSongMetaForQueueEntry(firstEntry);
            if (songMeta == null)
            {
                Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Could not resolve first queue entry to SongMeta. Entry type: {firstEntry?.GetType().FullName}");
                return null;
            }

            RemoveRealQueueItemWithoutRefreshing(0);
            return songMeta;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{nameof(BetterJukeboxControl)} - Queue-entry fallback failed: {ex.Message}");
            return null;
        }
    }

    private void RemoveRealQueueItemWithoutRefreshing(int index)
    {
        List<object> entries = GetRealSongQueueEntries();
        if (index < 0 || index >= entries.Count)
        {
            return;
        }

        object entry = entries[index];
        UnregisterQueueEntryOverride(entry);
        if (!InvokeSongQueueManager("RemoveSongQueueEntry", entry))
        {
            entries.RemoveAt(index);
            SetRealSongQueueEntries(entries);
        }
    }

    private SongMeta GetNextRandomSongMeta(List<SongMeta> allSelectableSongMetas)
    {
        List<SongMeta> unseenSongMetas = allSelectableSongMetas
            .Except(seenSongMetas)
            .ToList();

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

        return songMeta;
    }

    private void DisableSingSceneFinisher()
    {
        try
        {
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
