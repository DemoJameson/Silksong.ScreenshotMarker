using BepInEx;
using BepInEx.Logging;

namespace Silksong.ScreenshotMarker;

[BepInAutoPlugin(id: "mod.silksong.screenshotmarker", name: "Screenshot Marker")]
public partial class Plugin : BaseUnityPlugin {
    public static Plugin Instance { get; private set; }
    public static ManualLogSource LogSource => Instance.Logger;

    private void Awake() {
        Instance = this;
        PluginComponent.Initialize(gameObject);
    }
}
