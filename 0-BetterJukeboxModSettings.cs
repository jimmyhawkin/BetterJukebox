using System.Collections.Generic;

public class BetterJukeboxModSettings : IModSettings
{
    public bool EnableBetterJukebox { get; set; } = true;
    public bool AutoOpenSing { get; set; } = true;
    public bool AutoPlayRandomSong { get; set; } = true;
    public bool FadeAnimations { get; set; } = true;
    public bool AutoHideMenu { get; set; } = true;
    public int AnimationSpeed { get; set; } = 1;
    public bool ShakeMouseToShowMenu { get; set; } = true;
    public bool HideMouseAfterTimeout { get; set; } = false;
    public bool HideLyrics { get; set; } = true;
    public bool RandomSelection { get; set; } = true;
    public bool AutoContinue { get; set; } = true;
    public bool ShowNowPlaying { get; set; } = true;
    public bool ShowMouseOverlay { get; set; } = true;
    public bool ShowProgressBar { get; set; } = false;
    public bool AutoStartOnGameStart { get; set; } = true;
    public bool HideBuiltInPauseButton { get; set; } = true;
    public int NowPlayingSeconds { get; set; } = 6;
    public int OverlayHideSeconds { get; set; } = 4;

    public List<IModSettingControl> GetModSettingControls()
    {
        return new List<IModSettingControl>()
        {
            new BoolModSettingControl(() => EnableBetterJukebox, newValue => EnableBetterJukebox = newValue) { Label = "Enable BetterJukebox" },
            new BoolModSettingControl(() => AutoOpenSing, newValue => AutoOpenSing = newValue) { Label = "Auto Open Sing" },
            new BoolModSettingControl(() => AutoPlayRandomSong, newValue => AutoPlayRandomSong = newValue) { Label = "Auto Play Random Song" },
            new BoolModSettingControl(() => HideLyrics, newValue => HideLyrics = newValue) { Label = "Hide Lyrics" },
            new BoolModSettingControl(() => RandomSelection, newValue => RandomSelection = newValue) { Label = "Random Selection" },
            new BoolModSettingControl(() => AutoContinue, newValue => AutoContinue = newValue) { Label = "Auto Continue" },
            new BoolModSettingControl(() => ShowNowPlaying, newValue => ShowNowPlaying = newValue) { Label = "Show Now Playing" },
            new BoolModSettingControl(() => ShowMouseOverlay, newValue => ShowMouseOverlay = newValue) { Label = "Show Overlay" },
            new BoolModSettingControl(() => FadeAnimations, newValue => FadeAnimations = newValue) { Label = "Fade In / Fade Out" },
            new BoolModSettingControl(() => AutoHideMenu, newValue => AutoHideMenu = newValue) { Label = "Auto Hide Menu" },
            new BoolModSettingControl(() => ShakeMouseToShowMenu, newValue => ShakeMouseToShowMenu = newValue) { Label = "Shake Mouse To Show Menu" },
            new BoolModSettingControl(() => HideMouseAfterTimeout, newValue => HideMouseAfterTimeout = newValue) { Label = "Hide Mouse After Timeout" },
                        new BoolModSettingControl(() => AutoStartOnGameStart, newValue => AutoStartOnGameStart = newValue) { Label = "Auto Start On Game Start" },
            new BoolModSettingControl(() => HideBuiltInPauseButton, newValue => HideBuiltInPauseButton = newValue) { Label = "Hide Built-In Pause Button" },
            new IntModSettingControl(() => NowPlayingSeconds, newValue => NowPlayingSeconds = newValue) { Label = "Now Playing Seconds" },
            new IntModSettingControl(() => OverlayHideSeconds, newValue => OverlayHideSeconds = newValue) { Label = "Overlay Hide Seconds" },
            new IntModSettingControl(() => AnimationSpeed, newValue => AnimationSpeed = newValue) { Label = "Animation Speed (0 Slow, 1 Normal, 2 Fast)" },
        };
    }
}
