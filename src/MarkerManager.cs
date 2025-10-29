using BepInEx;
using GlobalEnums;
using HarmonyLib;
using Newtonsoft.Json;
using Silksong.ScreenshotMarker.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;
using Random = UnityEngine.Random;

namespace Silksong.ScreenshotMarker;

[HarmonyPatch]
public class MarkerManager : PluginComponent {
    private static List<MarkerData> markerDataList = [];
    private static List<GameObject> spawnedMapMarkers = [];
    private static Sprite sprite;
    private static string markerNamePrefix = "ScreenshotMarker_";
    private static MapMarkerMenu.MarkerTypes ScreenshotMarkerType = (MapMarkerMenu.MarkerTypes)9527;
    private static AudioSource placeAudioSource;
    private static int currentSaveSlot => GameManager._instance.profileID;

    private void Awake() {
        var bytes = GetEmbeddedResourceBytes("ScreenshotMarker.png");
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        sprite = Sprite.Create(tex, new Rect(0, 0, 73, 73), Vector2.one / 2f);

        if (GameManager._instance && currentSaveSlot > 0) {
            LoadMarkers(currentSaveSlot);
        }
    }

    private void OnDestroy() {
        foreach (var marker in spawnedMapMarkers) {
            Destroy(marker);
        }

        Destroy(sprite);
        Destroy(placeAudioSource);
        ScreenshotDisplay.Close();
    }

    private void LateUpdate() {
        if (!PluginConfig.Enabled.Value) {
            return;
        }

        if (!HeroController._instance || HeroController._instance.IsInputBlocked()) {
            return;
        }

        if (GameManager._instance) {
            if (GameManager._instance.GameState != GameState.PLAYING) {
                return;
            }

            if (GameManager._instance.IsMemoryScene() &&
                GameManager._instance.GetCurrentMapZoneEnum() != MapZone.CLOVER) {
                return;
            }
        }

        HeroActions actions = InputHandler.Instance.inputActions;
        if (PluginConfig.ScreenshotKey.IsDown() || actions.QuickMap.WasPressed && actions.DreamNail.IsPressed) {
            if (currentSaveSlot > 0) {
                StartCoroutine(CreateScreenshotMarker());

                if (PluginConfig.FlashEffect.Value) {
                    StartCoroutine(CreateFlashEffect());
                }
            }
        }
    }

    private static string GetScreenshotPath(int saveSlot) =>
        Path.Combine(Paths.ConfigPath, "ScreenshotMarker", "Save_" + saveSlot);

    private static string GetScreenshotFilePath(int saveSlot, string fileName) =>
        Path.Combine(GetScreenshotPath(saveSlot), fileName);

    private static string GetMarkerDataPath(int saveSlot) =>
        Path.Combine(GetScreenshotPath(saveSlot), "marker_data.json");

    private static IEnumerator CreateScreenshotMarker() {
        yield return new WaitForEndOfFrame();

        int saveSlot = currentSaveSlot;

        var gameMap = GameManager._instance.gameMap;
        gameMap.UpdateCurrentScene();
        var currentPosition = gameMap.GetMapPosition(HeroController._instance.transform.position, gameMap.currentScene,
            gameMap.currentSceneObj, gameMap.currentScenePos, gameMap.currentSceneSize);

        // 移除附近的截图标记
        foreach (var data in markerDataList.ToList()) {
            if (Vector2.Distance(data.Position, currentPosition) < 0.1f) {
                RemoveMarkerData(data);
            }
        }

        string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + (RenoDxOrLumaEnabled ? ".exr" : ".png");
        string filePath = GetScreenshotFilePath(saveSlot, fileName);

        var markerData = new MarkerData {
            Name = CurrentSceneName,
            Time = DateTime.Now,
            FileName = fileName,
            Position = currentPosition
        };

        Directory.CreateDirectory(GetScreenshotPath(saveSlot));
        TakeScreenshot(filePath);

        markerDataList.Add(markerData);
        SaveMakers(saveSlot);

        var markerMenu = gameMap.mapManager.mapMarkerMenu;
        VibrationManager.PlayVibrationClipOneShot(markerMenu.placementVibration);
        if (!placeAudioSource) {
            placeAudioSource = new GameObject("ScreenshotMarker_PlaceAudioSource").AddComponent<AudioSource>();
            AudioClip clip = markerMenu.placeClip;
            placeAudioSource.clip = clip;
        }

        placeAudioSource.Play();
    }

    private static GameObject CreateMarker(GameMap gameMap, int index) {
        if (spawnedMapMarkers.Count > index) {
            return spawnedMapMarkers[index];
        }

        GameObject newObject = Instantiate(gameMap.mapMarkerTemplates[0], gameMap.markerParent);
        newObject.name = $"{markerNamePrefix}{index}";

        var renderer = newObject.GetComponentInChildren<SpriteRenderer>();
        renderer.sprite = sprite;
        MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
        materialPropertyBlock.SetFloat(Shader.PropertyToID("_TimeOffset"), Random.Range(0f, 10f));
        renderer.SetPropertyBlock(materialPropertyBlock);

        var invMarker = newObject.GetComponentInChildren<InvMarker>();
        invMarker.Colour = ScreenshotMarkerType;
        invMarker.Index = index;

        spawnedMapMarkers.Add(newObject);
        return newObject;
    }

    // 从远到近排序，解决标记过于靠近无法准确吸附的问题
    [HarmonyPatch(typeof(MapMarkerMenu), nameof(MapMarkerMenu.AddToCollidingList))]
    [HarmonyPostfix]
    private static void MapMarkerMenuAddToCollidingList(MapMarkerMenu __instance) {
        var collidingMarkers = __instance.collidingMarkers;
        var cursorPosition = __instance.placementCursor.transform.position;
        var gameMapPosition = __instance.gameMap.transform.parent.position;
        collidingMarkers.Sort((go1, go2) => {
            float distance1 = Vector2.Distance(go1.transform.position - gameMapPosition, cursorPosition);
            float distance2 = Vector2.Distance(go2.transform.position - gameMapPosition, cursorPosition);
            float diff = distance2 - distance1;
            return diff switch {
                > 0 => 1,
                < 0 => -1,
                _ => 0,
            };
        });
    }

    // 显示图片时禁用菜单按钮
    [HarmonyPatch(typeof(Platform), nameof(Platform.GetMenuAction), typeof(HeroActions), typeof(bool), typeof(bool))]
    [HarmonyPrefix]
    private static bool InventoryItemSelectedActionDoAction() {
        if (ScreenshotDisplay.IsShowing) {
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(MapMarkerMenu), nameof(MapMarkerMenu.Update))]
    [HarmonyPrefix]
    private static bool MapMarkerMenuUpdate(MapMarkerMenu __instance) {
        if (__instance.inPlacementMode && __instance.collidingMarkers.Count > 0) {
            if (!ScreenshotDisplay.IsShowing) {
                HeroActions inputActions = InputHandler.Instance.inputActions;
                Platform.MenuActions menuAction = Platform.Current.GetMenuAction(inputActions);
                if (PluginConfig.ScreenshotKey.IsDown() || inputActions.QuickMap.WasPressed || menuAction == Platform.MenuActions.Extra) {
                    var invMarker = __instance.collidingMarkers[^1].GetComponent<InvMarker>();
                    if (invMarker.Colour != ScreenshotMarkerType) {
                        return true;
                    }

                    var markerData = markerDataList[invMarker.Index];
                    var filePath = GetScreenshotFilePath(currentSaveSlot, markerData.FileName);
                    if (File.Exists(filePath)) {
                        ScreenshotDisplay.Show(filePath);
                    } else {
                        RemoveScreenshotMarker(__instance);
                    }

                    return false;
                }
            }
        }

        if (ScreenshotDisplay.IsShowing) {
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(MapMarkerMenu), nameof(MapMarkerMenu.RemoveMarker))]
    [HarmonyPrefix]
    private static bool RemoveMarker(MapMarkerMenu __instance) {
        var invMarker = __instance.collidingMarkers[^1].GetComponent<InvMarker>();
        if (invMarker.Colour != ScreenshotMarkerType) {
            return true;
        }

        RemoveScreenshotMarker(__instance);

        return false;
    }

    private static void RemoveScreenshotMarker(MapMarkerMenu markerMenu) {
        var collidingMarkers = markerMenu.collidingMarkers;
        var marker = collidingMarkers[^1];
        var invMarker = marker.GetComponent<InvMarker>();
        RemoveMarkerData(markerDataList[invMarker.Index]);

        collidingMarkers.Remove(marker);
        if (collidingMarkers.Count <= 0) {
            markerMenu.IsNotColliding();
        }

        markerMenu.audioSource.PlayOneShot(markerMenu.removeClip);
        VibrationManager.PlayVibrationClipOneShot(markerMenu.placementVibration);
        markerMenu.gameMap.SetupMapMarkers();
    }


    [HarmonyPatch(typeof(GameMap), nameof(GameMap.SetupMapMarkers))]
    [HarmonyPostfix]
    private static void SetupMapMarkers(GameMap __instance) {
        if (!PluginConfig.Enabled.Value) {
            return;
        }

        if (CollectableItemManager.IsInHiddenMode()) {
            return;
        }

        for (int i = 0; i < markerDataList.GetCount(); i++) {
            var marker = CreateMarker(__instance, i);
            marker.SetActive(value: true);
            marker.transform.SetLocalPosition2D(markerDataList[i].Position);
        }
    }

    [HarmonyPatch(typeof(GameMap), nameof(GameMap.DisableMarkers))]
    [HarmonyPostfix]
    private static void DisableMarkers() {
        foreach (var marker in spawnedMapMarkers) {
            Destroy(marker);
        }

        spawnedMapMarkers.Clear();
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SetLoadedGameData), typeof(SaveGameData), typeof(int))]
    [HarmonyPostfix]
    private static void LoadMarkers(int saveSlot) {
        if (!File.Exists(GetMarkerDataPath(saveSlot))) {
            markerDataList.Clear();
            return;
        }

        string json = File.ReadAllText(GetMarkerDataPath(saveSlot));
        markerDataList = JsonConvert.DeserializeObject<List<MarkerData>>(json) ?? [];
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ClearSaveFile))]
    [HarmonyPostfix]
    private static void ClearMarkers(int saveSlot) {
        if (Directory.Exists(GetScreenshotPath(saveSlot))) {
            Directory.Delete(GetScreenshotPath(saveSlot), true);
        }

        markerDataList.Clear();
    }

    private static void SaveMakers(int saveSlot) {
        File.WriteAllText(GetMarkerDataPath(saveSlot), JsonConvert.SerializeObject(markerDataList));
    }

    private static void RemoveMarkerData(MarkerData markerData) {
        if (markerDataList.Remove(markerData)) {
            File.Delete(GetScreenshotFilePath(currentSaveSlot, markerData.FileName));
            SaveMakers(currentSaveSlot);
        }
    }

    private static void TakeScreenshot(string filePath) {
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;

        float aspectRatio = (float)screenWidth / screenHeight;
        int targetHeight = Mathf.Min(screenHeight, 1080);
        int targetWidth = Mathf.RoundToInt(aspectRatio * targetHeight);

        var camera = Camera.main;

        var origTargetTexture = camera.targetTexture;
        var origActive = RenderTexture.active;

        RenderTexture rt = new RenderTexture(targetWidth, targetHeight, 24,
            RenoDxOrLumaEnabled ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.Default);
        camera.targetTexture = rt;
        camera.Render();

        RenderTexture.active = rt;
        Texture2D screenshot = new Texture2D(targetWidth, targetHeight,
            RenoDxOrLumaEnabled ? TextureFormat.RGBAHalf : TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);

        if (RenoDxOrLumaEnabled) {
            Color[] pixels = screenshot.GetPixels();
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i].a = 1f;
            }

            screenshot.SetPixels(pixels);
        }

        screenshot.Apply();

        if (RenoDxOrLumaEnabled) {
            File.WriteAllBytes(filePath, screenshot.EncodeToEXR(Texture2D.EXRFlags.CompressPIZ));
        } else {
            File.WriteAllBytes(filePath, screenshot.EncodeToPNG());
        }

        camera.targetTexture = origTargetTexture;
        RenderTexture.active = origActive;
        Destroy(rt);
        Destroy(screenshot);
    }

    private static IEnumerator CreateFlashEffect() {
        GameObject flashObject = new GameObject("ScreenshotMarker_Flash");
        Canvas canvas = flashObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = flashObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        Image flashImage = flashObject.AddComponent<Image>();
        float defaultAlpha = 0.25f;
        flashImage.color = new Color(1f, 1f, 1f, defaultAlpha);

        float duration = 0.3f;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(defaultAlpha, 0f, elapsed / duration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        Destroy(flashObject);
    }

    private static byte[] GetEmbeddedResourceBytes(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        string fullResourceName = $"Silksong.ScreenshotMarker.{resourceName}";
        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null) {
            throw new ArgumentException($"找不到资源: {fullResourceName}");
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static bool? renoDxOrLumaEnabled;

    private static bool RenoDxOrLumaEnabled {
        get {
            if (renoDxOrLumaEnabled != null) {
                return renoDxOrLumaEnabled.Value;
            }

            if (File.Exists(Path.Combine(Paths.GameRootPath, "dxgi.dll"))) {
                renoDxOrLumaEnabled =
                    File.Exists(Path.Combine(Paths.GameRootPath, "renodx-hollowknight-silksong.addon64")) ||
                    File.Exists(Path.Combine(Paths.GameRootPath, "Luma-Unity Engine.addon"));
            } else {
                renoDxOrLumaEnabled = false;
            }

            return renoDxOrLumaEnabled.Value;
        }
    }
}
