using BepInEx;
using GlobalEnums;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using Silksong.ScreenshotMarker.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
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
    private static int currentSaveSlot => UIManager.instance.saveSlot;

    private void Awake() {
        var bytes = GetEmbeddedResourceBytes("ScreenshotMarker.png");
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        sprite = Sprite.Create(tex, new Rect(0, 0, 73, 73), Vector2.one / 2f);

        if (UIManager.instance) {
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
        
        if (!UIManager.instance) {
            return;
        }

        if (!HeroController.instance || HeroController.instance.IsInputBlocked()) {
            return;
        }

        if (GameManager.instance) {
            if (GameManager.instance.GameState != GameState.PLAYING) {
                return;
            }

            if (GameManager.instance.IsMemoryScene() && GameManager.instance.GetCurrentMapZoneEnum() != MapZone.CLOVER) {
                return;
            }
        }


        HeroActions actions = ManagerSingleton<InputHandler>.Instance.inputActions;
        if (PluginConfig.ScreenshotKey.IsDown() || actions.QuickMap.WasPressed && actions.DreamNail.IsPressed) {
            CaptureScreenshot();
        }
    }

    private static string GetScreenshotPath(int saveSlot) =>
        Path.Combine(Paths.ConfigPath, "ScreenshotMarker", "Save_" + saveSlot);
    
    private static string GetScreenshotFilePath(int saveSlot, string fileName) =>
        Path.Combine(GetScreenshotPath(saveSlot), fileName);

    private static string GetMarkerDataPath(int saveSlot) =>
        Path.Combine(GetScreenshotPath(saveSlot), "marker_data.json");

    private static void CaptureScreenshot() {
        int saveSlot = currentSaveSlot;
        if (saveSlot <= 0) {
            return;
        }

        var gameMap = GameManager.instance.gameMap;
        var currentPosition = gameMap.GetMapPosition(HeroController.instance.transform.position, gameMap.currentScene,
            gameMap.currentSceneObj, gameMap.currentScenePos, gameMap.currentSceneSize);

        // 移除附近的截图标记
        foreach (var data in markerDataList.ToList()) {
            if (Vector2.Distance(data.Position, currentPosition) < 0.1f) {
                RemoveMarkerData(data);
            }
        }

        string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
        string filePath = GetScreenshotFilePath(saveSlot, fileName);

        var markerData = new MarkerData {
            Name = CurrentSceneName,
            Time = DateTime.Now,
            FileName = fileName,
            Position = currentPosition
        };

        Directory.CreateDirectory(GetScreenshotPath(saveSlot));
        ScreenCapture.CaptureScreenshot(filePath);
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
    
    // TODO 显示图片时进去取消按钮
    // 显示图片时禁用隐藏图钉按钮
    [HarmonyPatch(typeof(InventoryItemSelectedAction), nameof(InventoryItemSelectedAction.DoAction))]
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
                HeroActions inputActions = ManagerSingleton<InputHandler>.Instance.inputActions;
                if (PluginConfig.ScreenshotKey.IsDown() || inputActions.QuickMap.WasPressed) {
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
        markerDataList = SaveDataUtility.DeserializeSaveData<List<MarkerData>>(json);
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
        File.WriteAllText(GetMarkerDataPath(saveSlot), SaveDataUtility.SerializeSaveData(markerDataList));
    }

    private static void RemoveMarkerData(MarkerData markerData) {
        if (markerDataList.Remove(markerData)) {
            File.Delete(GetScreenshotFilePath(currentSaveSlot, markerData.FileName));
            SaveMakers(currentSaveSlot);
        }
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
}
