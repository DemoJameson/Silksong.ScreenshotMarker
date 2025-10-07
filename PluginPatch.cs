using HarmonyLib;
using System.Reflection;

namespace Silksong.ScreenshotMarker;

public class PluginPatch : PluginComponent {
    private Harmony harmony;

    private void Awake() {
        harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
    }
}
