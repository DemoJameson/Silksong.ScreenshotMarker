using BepInEx;
using BepInEx.Configuration;
using System.Linq;

namespace Silksong.ScreenshotMarker.Extensions;

public static class ConfigEntryExtensions {
    public static bool IsDown(this ConfigEntry<KeyboardShortcut> configEntry) {
        if (!configEntry.Value.Modifiers.Any()) {
            return UnityInput.Current.GetKeyDown(configEntry.Value.MainKey);
        } else {
            return configEntry.Value.IsDown();
        }
    }
}
