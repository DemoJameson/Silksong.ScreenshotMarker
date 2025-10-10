using BepInEx.Configuration;
using UnityEngine;

namespace Silksong.ScreenshotMarker;

public class PluginConfig : PluginComponent {
    public static ConfigEntry<bool> Enabled { get; private set; }
    public static ConfigEntry<bool> FlashEffect { get; private set; }
    public static ConfigEntry<float> ScreenshotScale { get; private set; }
    public static ConfigEntry<KeyboardShortcut> ScreenshotKey { get; private set; }

    private void Awake() {
        var config = Plugin.Instance.Config;

        Enabled = config.Bind(
            "General",
            "Screenshot Marker",
            true,
            "Whether to enable screenshot marker"
        );

        FlashEffect = config.Bind(
            "General",
            "Flash Effect",
            true,
            "Whether to enable flash effect when taking a screenshot"
        );

        ScreenshotScale = config.Bind(
            "General",
            "Screenshot Scale",
            0.75f,
            new ConfigDescription("The scale of the screenshot", new AcceptableValueRange<float>(0, 1)));

        ScreenshotKey = config.Bind(
            "General",
            "Screenshot Key",
            new KeyboardShortcut(KeyCode.P),
            "The key to take a screenshot and remove marker"
        );
    }
}
