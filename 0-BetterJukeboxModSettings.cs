using System.Collections.Generic;
using UnityEngine;

public class BetterJukeboxModSettings : IModSettings
{
    private const string PersistentSettingsVersion = "2.0.0.24";
    private static bool isLoadingPersistentSettings;

    private bool enableBetterJukebox = true;
    private bool autoStartJukebox = false;
    private bool autoPlayRandomSong = true;
    private bool fadeAnimations = true;
    private bool autoHideMenu = true;
    private int animationSpeed = 1;
    private bool shakeMouseToShowMenu = true;
    private bool hideMouseAfterTimeout = false;
    private bool hideLyrics = false;
    private bool randomSelection = true;
    private bool autoContinue = true;
    private bool showNowPlaying = true;
    private bool showMouseOverlay = true;
    private bool showProgressBar = false;
    private bool showAlbumArtInSearch = false;
    private bool showAlbumArtInQueue = true;
    private bool showAlbumArtInHistory = true;
    private bool showAlbumArtInPlaylists = true;
    private bool showNowPlayingQueuePlayerMics = true;
    private bool showFavoriteStars = true;
    private bool showFavoriteSparkleAnimation = false;
    private bool autoStartOnGameStart = true;
    private bool hideBuiltInPauseButton = true;
    private bool debugLogging = false;
    private int nowPlayingSeconds = 6;
    private int overlayHideSeconds = 4;
    private int uiTheme = 0;

    public BetterJukeboxModSettings()
    {
        LoadPersistentSettings();
    }

    public bool EnableBetterJukebox { get { return enableBetterJukebox; } set { SetBool(ref enableBetterJukebox, value); } }
    public bool AutoStartJukebox { get { return autoStartJukebox; } set { SetBool(ref autoStartJukebox, value); } }
    public bool AutoPlayRandomSong { get { return autoPlayRandomSong; } set { SetBool(ref autoPlayRandomSong, value); } }
    public bool FadeAnimations { get { return fadeAnimations; } set { SetBool(ref fadeAnimations, value); } }
    public bool AutoHideMenu { get { return autoHideMenu; } set { SetBool(ref autoHideMenu, value); } }
    public int AnimationSpeed { get { return animationSpeed; } set { SetInt(ref animationSpeed, value); } }
    public bool ShakeMouseToShowMenu { get { return shakeMouseToShowMenu; } set { SetBool(ref shakeMouseToShowMenu, value); } }
    public bool HideMouseAfterTimeout { get { return hideMouseAfterTimeout; } set { SetBool(ref hideMouseAfterTimeout, value); } }
    public bool HideLyrics { get { return hideLyrics; } set { SetBool(ref hideLyrics, value); } }
    public bool RandomSelection { get { return randomSelection; } set { SetBool(ref randomSelection, value); } }
    public bool AutoContinue { get { return autoContinue; } set { SetBool(ref autoContinue, value); } }
    public bool ShowNowPlaying { get { return showNowPlaying; } set { SetBool(ref showNowPlaying, value); } }
    public bool ShowMouseOverlay { get { return showMouseOverlay; } set { SetBool(ref showMouseOverlay, value); } }
    public bool ShowProgressBar { get { return showProgressBar; } set { SetBool(ref showProgressBar, value); } }
    public bool ShowAlbumArtInSearch { get { return showAlbumArtInSearch; } set { SetBool(ref showAlbumArtInSearch, value); } }
    public bool ShowAlbumArtInQueue { get { return showAlbumArtInQueue; } set { SetBool(ref showAlbumArtInQueue, value); } }
    public bool ShowAlbumArtInHistory { get { return showAlbumArtInHistory; } set { SetBool(ref showAlbumArtInHistory, value); } }
    public bool ShowAlbumArtInPlaylists { get { return showAlbumArtInPlaylists; } set { SetBool(ref showAlbumArtInPlaylists, value); } }
    public bool ShowNowPlayingQueuePlayerMics { get { return showNowPlayingQueuePlayerMics; } set { SetBool(ref showNowPlayingQueuePlayerMics, value); } }
    public bool ShowFavoriteStars { get { return showFavoriteStars; } set { SetBool(ref showFavoriteStars, value); } }
    public bool ShowFavoriteSparkleAnimation { get { return showFavoriteSparkleAnimation; } set { SetBool(ref showFavoriteSparkleAnimation, value); } }
    public bool AutoStartOnGameStart { get { return autoStartOnGameStart; } set { SetBool(ref autoStartOnGameStart, value); } }
    public bool HideBuiltInPauseButton { get { return hideBuiltInPauseButton; } set { SetBool(ref hideBuiltInPauseButton, value); } }
    public bool DebugLogging { get { return debugLogging; } set { SetBool(ref debugLogging, value); BetterJukeboxLog.Enabled = value; } }
    public int NowPlayingSeconds { get { return nowPlayingSeconds; } set { SetInt(ref nowPlayingSeconds, value); } }
    public int OverlayHideSeconds { get { return overlayHideSeconds; } set { SetInt(ref overlayHideSeconds, value); } }
    public int UiTheme { get { return uiTheme; } set { SetInt(ref uiTheme, value); } }

    private void SetBool(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        SavePersistentSettingsIfReady();
    }

    private void SetInt(ref int field, int value)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        SavePersistentSettingsIfReady();
    }

    private void SavePersistentSettingsIfReady()
    {
        if (isLoadingPersistentSettings)
        {
            return;
        }
        SavePersistentSettings();
    }

    private static string GetPersistentSettingsDirectory()
    {
        return System.IO.Path.Combine(System.IO.Path.Combine(Application.persistentDataPath, "Mods"), "BetterJukebox");
    }

    private static string GetPersistentSettingsPath()
    {
        return System.IO.Path.Combine(GetPersistentSettingsDirectory(), "BetterJukeboxSettings.json");
    }

    public void LoadPersistentSettings()
    {
        isLoadingPersistentSettings = true;

        try
        {
            string path = GetPersistentSettingsPath();
            if (System.IO.File.Exists(path))
            {
                string json = System.IO.File.ReadAllText(path);

                enableBetterJukebox = ReadBool(json, "EnableBetterJukebox", enableBetterJukebox);
                autoStartJukebox = ReadBool(json, "AutoStartJukebox", autoStartJukebox);
                autoPlayRandomSong = ReadBool(json, "AutoPlayRandomSong", autoPlayRandomSong);
                fadeAnimations = ReadBool(json, "FadeAnimations", fadeAnimations);
                autoHideMenu = ReadBool(json, "AutoHideMenu", autoHideMenu);
                animationSpeed = ReadInt(json, "AnimationSpeed", animationSpeed);
                shakeMouseToShowMenu = ReadBool(json, "ShakeMouseToShowMenu", shakeMouseToShowMenu);
                hideMouseAfterTimeout = ReadBool(json, "HideMouseAfterTimeout", hideMouseAfterTimeout);
                hideLyrics = ReadBool(json, "HideLyrics", hideLyrics);
                randomSelection = ReadBool(json, "RandomSelection", randomSelection);
                autoContinue = ReadBool(json, "AutoContinue", autoContinue);
                showNowPlaying = ReadBool(json, "ShowNowPlaying", showNowPlaying);
                showMouseOverlay = ReadBool(json, "ShowMouseOverlay", showMouseOverlay);
                showProgressBar = ReadBool(json, "ShowProgressBar", showProgressBar);
                showAlbumArtInSearch = ReadBool(json, "ShowAlbumArtInSearch", showAlbumArtInSearch);
                showAlbumArtInQueue = ReadBool(json, "ShowAlbumArtInQueue", showAlbumArtInQueue);
                showAlbumArtInHistory = ReadBool(json, "ShowAlbumArtInHistory", showAlbumArtInHistory);
                showAlbumArtInPlaylists = ReadBool(json, "ShowAlbumArtInPlaylists", showAlbumArtInPlaylists);
                showNowPlayingQueuePlayerMics = ReadBool(json, "ShowNowPlayingQueuePlayerMics", showNowPlayingQueuePlayerMics);
                showFavoriteStars = ReadBool(json, "ShowFavoriteStars", showFavoriteStars);
                showFavoriteSparkleAnimation = ReadBool(json, "ShowFavoriteSparkleAnimation", showFavoriteSparkleAnimation);
                autoStartOnGameStart = ReadBool(json, "AutoStartOnGameStart", autoStartOnGameStart);
                hideBuiltInPauseButton = ReadBool(json, "HideBuiltInPauseButton", hideBuiltInPauseButton);
                debugLogging = ReadBool(json, "DebugLogging", debugLogging);
                nowPlayingSeconds = ReadInt(json, "NowPlayingSeconds", nowPlayingSeconds);
                overlayHideSeconds = ReadInt(json, "OverlayHideSeconds", overlayHideSeconds);
                uiTheme = ReadInt(json, "UiTheme", uiTheme);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not load persistent settings: " + ex.Message);
        }
        finally
        {
            isLoadingPersistentSettings = false;
        }

        BetterJukeboxLog.Enabled = debugLogging;
        SavePersistentSettings();
    }

    public void SavePersistentSettings()
    {
        try
        {
            string directory = GetPersistentSettingsDirectory();
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.AppendLine("{");
            AppendString(builder, "Version", PersistentSettingsVersion, true);
            AppendBool(builder, "EnableBetterJukebox", enableBetterJukebox, true);
            AppendBool(builder, "AutoStartJukebox", autoStartJukebox, true);
            AppendBool(builder, "AutoPlayRandomSong", autoPlayRandomSong, true);
            AppendBool(builder, "FadeAnimations", fadeAnimations, true);
            AppendBool(builder, "AutoHideMenu", autoHideMenu, true);
            AppendInt(builder, "AnimationSpeed", animationSpeed, true);
            AppendBool(builder, "ShakeMouseToShowMenu", shakeMouseToShowMenu, true);
            AppendBool(builder, "HideMouseAfterTimeout", hideMouseAfterTimeout, true);
            AppendBool(builder, "HideLyrics", hideLyrics, true);
            AppendBool(builder, "RandomSelection", randomSelection, true);
            AppendBool(builder, "AutoContinue", autoContinue, true);
            AppendBool(builder, "ShowNowPlaying", showNowPlaying, true);
            AppendBool(builder, "ShowMouseOverlay", showMouseOverlay, true);
            AppendBool(builder, "ShowProgressBar", showProgressBar, true);
            AppendBool(builder, "ShowAlbumArtInSearch", showAlbumArtInSearch, true);
            AppendBool(builder, "ShowAlbumArtInQueue", showAlbumArtInQueue, true);
            AppendBool(builder, "ShowAlbumArtInHistory", showAlbumArtInHistory, true);
            AppendBool(builder, "ShowAlbumArtInPlaylists", showAlbumArtInPlaylists, true);
            AppendBool(builder, "ShowNowPlayingQueuePlayerMics", showNowPlayingQueuePlayerMics, true);
            AppendBool(builder, "ShowFavoriteStars", showFavoriteStars, true);
            AppendBool(builder, "ShowFavoriteSparkleAnimation", showFavoriteSparkleAnimation, true);
            AppendBool(builder, "AutoStartOnGameStart", autoStartOnGameStart, true);
            AppendBool(builder, "HideBuiltInPauseButton", hideBuiltInPauseButton, true);
            AppendBool(builder, "DebugLogging", debugLogging, true);
            AppendInt(builder, "NowPlayingSeconds", nowPlayingSeconds, true);
            AppendInt(builder, "OverlayHideSeconds", overlayHideSeconds, true);
            AppendInt(builder, "UiTheme", uiTheme, false);
            builder.AppendLine("}");

            System.IO.File.WriteAllText(GetPersistentSettingsPath(), builder.ToString());
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("BetterJukebox could not save persistent settings: " + ex.Message);
        }
    }

    private static void AppendString(System.Text.StringBuilder builder, string key, string value, bool comma)
    {
        builder.Append("  \"");
        builder.Append(key);
        builder.Append("\": \"");
        builder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        builder.Append("\"");
        if (comma)
        {
            builder.Append(",");
        }
        builder.AppendLine();
    }

    private static void AppendBool(System.Text.StringBuilder builder, string key, bool value, bool comma)
    {
        builder.Append("  \"");
        builder.Append(key);
        builder.Append("\": ");
        builder.Append(value ? "true" : "false");
        if (comma)
        {
            builder.Append(",");
        }
        builder.AppendLine();
    }

    private static void AppendInt(System.Text.StringBuilder builder, string key, int value, bool comma)
    {
        builder.Append("  \"");
        builder.Append(key);
        builder.Append("\": ");
        builder.Append(value.ToString());
        if (comma)
        {
            builder.Append(",");
        }
        builder.AppendLine();
    }

    private static bool ReadBool(string json, string key, bool defaultValue)
    {
        string rawValue = ReadRawValue(json, key);
        if (string.IsNullOrEmpty(rawValue))
        {
            return defaultValue;
        }
        rawValue = rawValue.Trim().ToLowerInvariant();
        if (rawValue.StartsWith("true"))
        {
            return true;
        }
        if (rawValue.StartsWith("false"))
        {
            return false;
        }
        return defaultValue;
    }

    private static int ReadInt(string json, string key, int defaultValue)
    {
        string rawValue = ReadRawValue(json, key);
        if (string.IsNullOrEmpty(rawValue))
        {
            return defaultValue;
        }
        rawValue = rawValue.Trim();
        int commaIndex = rawValue.IndexOf(',');
        if (commaIndex >= 0)
        {
            rawValue = rawValue.Substring(0, commaIndex);
        }
        int parsedValue;
        if (int.TryParse(rawValue, out parsedValue))
        {
            return parsedValue;
        }
        return defaultValue;
    }

    private static string ReadRawValue(string json, string key)
    {
        string pattern = "\"" + key + "\"";
        int keyIndex = json.IndexOf(pattern);
        if (keyIndex < 0)
        {
            return null;
        }
        int colonIndex = json.IndexOf(':', keyIndex + pattern.Length);
        if (colonIndex < 0)
        {
            return null;
        }
        int valueStart = colonIndex + 1;
        int lineEnd = json.IndexOf('\n', valueStart);
        if (lineEnd < 0)
        {
            lineEnd = json.Length;
        }
        return json.Substring(valueStart, lineEnd - valueStart).Trim().TrimEnd(',');
    }

    public List<IModSettingControl> GetModSettingControls()
    {
        return new List<IModSettingControl>()
        {
            new BoolModSettingControl(() => EnableBetterJukebox, newValue => EnableBetterJukebox = newValue) { Label = "Enable BetterJukebox" },
            new BoolModSettingControl(() => AutoStartJukebox, newValue => AutoStartJukebox = newValue) { Label = "Auto Start Jukebox On Game Start" },
            new BoolModSettingControl(() => AutoPlayRandomSong, newValue => AutoPlayRandomSong = newValue) { Label = "Auto Play Random Song When Opening Jukebox" },
            new BoolModSettingControl(() => HideLyrics, newValue => HideLyrics = newValue) { Label = "Hide Lyrics" },
            new BoolModSettingControl(() => RandomSelection, newValue => RandomSelection = newValue) { Label = "Random Selection" },
            new BoolModSettingControl(() => AutoContinue, newValue => AutoContinue = newValue) { Label = "Auto Continue" },
            new BoolModSettingControl(() => ShowNowPlaying, newValue => ShowNowPlaying = newValue) { Label = "Show Now Playing" },
            new BoolModSettingControl(() => ShowNowPlayingQueuePlayerMics, newValue => ShowNowPlayingQueuePlayerMics = newValue) { Label = "Show Now Playing Queue Player/Mics" },
            new BoolModSettingControl(() => ShowFavoriteStars, newValue => ShowFavoriteStars = newValue) { Label = "Show Favorite Stars" },
            new BoolModSettingControl(() => ShowFavoriteSparkleAnimation, newValue => ShowFavoriteSparkleAnimation = newValue) { Label = "Favorite Star Animation" },
            new BoolModSettingControl(() => ShowAlbumArtInSearch, newValue => ShowAlbumArtInSearch = newValue) { Label = "Show Album Art In Search" },
            new BoolModSettingControl(() => ShowAlbumArtInQueue, newValue => ShowAlbumArtInQueue = newValue) { Label = "Show Album Art In Queue" },
            new BoolModSettingControl(() => ShowAlbumArtInHistory, newValue => ShowAlbumArtInHistory = newValue) { Label = "Show Album Art In History" },
            new BoolModSettingControl(() => ShowAlbumArtInPlaylists, newValue => ShowAlbumArtInPlaylists = newValue) { Label = "Show Album Art In Playlists" },
            new BoolModSettingControl(() => ShowMouseOverlay, newValue => ShowMouseOverlay = newValue) { Label = "Show Overlay" },
            new BoolModSettingControl(() => FadeAnimations, newValue => FadeAnimations = newValue) { Label = "Fade In / Fade Out" },
            new BoolModSettingControl(() => AutoHideMenu, newValue => AutoHideMenu = newValue) { Label = "Auto Hide Menu" },
            new BoolModSettingControl(() => ShakeMouseToShowMenu, newValue => ShakeMouseToShowMenu = newValue) { Label = "Shake Mouse To Show Menu" },
            new BoolModSettingControl(() => HideMouseAfterTimeout, newValue => HideMouseAfterTimeout = newValue) { Label = "Hide Mouse After Timeout" },
            new BoolModSettingControl(() => HideBuiltInPauseButton, newValue => HideBuiltInPauseButton = newValue) { Label = "Hide Built-In Pause Button" },
            new BoolModSettingControl(() => DebugLogging, newValue => DebugLogging = newValue) { Label = "Debug Logging" },
            new IntModSettingControl(() => NowPlayingSeconds, newValue => NowPlayingSeconds = newValue) { Label = "Now Playing Seconds" },
            new IntModSettingControl(() => OverlayHideSeconds, newValue => OverlayHideSeconds = newValue) { Label = "Overlay Hide Seconds" },
            new IntModSettingControl(() => AnimationSpeed, newValue => AnimationSpeed = newValue) { Label = "Animation Speed (0 Slow, 1 Normal, 2 Fast)" },
            new IntModSettingControl(() => UiTheme, newValue => UiTheme = newValue) { Label = "UI Theme (0 DiscoGrey, 1 DiscoPurple, 2 DiscoGreen, 3 DiscoBlue, 4 DiscoRed, 5 DiscoGold)" },
        };
    }
}


public static class BetterJukeboxLog
{
    public static bool Enabled = false;

    public static void Info(string message)
    {
        if (Enabled)
        {
            Debug.Log(message);
        }
    }

    public static void Warning(string message)
    {
        if (Enabled)
        {
            Debug.LogWarning(message);
        }
    }

    public static void Exception(System.Exception exception)
    {
        if (Enabled && exception != null)
        {
            Debug.LogException(exception);
        }
    }
}
