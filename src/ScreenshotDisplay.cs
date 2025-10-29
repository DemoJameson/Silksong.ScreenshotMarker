using Silksong.ScreenshotMarker;
using Silksong.ScreenshotMarker.Extensions;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class ScreenshotDisplay : MonoBehaviour {
    private static ScreenshotDisplay? instance;
    public static bool IsShowing => instance != null;

    private Texture2D? screenshotTexture;
    private Sprite? screenshotSprite;

    public static void Show(string filePath) {
        if (instance) {
            Close();
        }

        GameObject displayObject = new GameObject("ScreenshotMarker_ScreenshotDisplay");
        instance = displayObject.AddComponent<ScreenshotDisplay>();
        instance.InitializeUI(filePath);
    }

    public static void Close() {
        if (instance) {
            Destroy(instance.gameObject);
            instance = null;
        }
    }

    private void InitializeUI(string filePath) {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler canvasScaler = gameObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(Screen.width, Screen.height);

        GameObject panel = new GameObject("ScreenshotMarker_Panel");
        panel.transform.SetParent(transform, false);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;

        GameObject imageObj = new GameObject("ScreenshotMarker_Image");
        imageObj.transform.SetParent(panel.transform, false);
        Image image = imageObj.AddComponent<Image>();

        byte[] bytes = File.ReadAllBytes(filePath);
        screenshotTexture = new Texture2D(2, 2);
        screenshotTexture.LoadImage(bytes);

        screenshotSprite = Sprite.Create(screenshotTexture,
            new Rect(0, 0, screenshotTexture.width, screenshotTexture.height), new Vector2(0.5f, 0.5f));
        image.sprite = screenshotSprite;

        RectTransform rectTransform = imageObj.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        float scale = PluginConfig.ScreenshotScale.Value;
        float screenWidth = Screen.width * scale;
        float screenHeight = Screen.height * scale;
        float imageRatio = (float)screenshotTexture.width / screenshotTexture.height;
        float width, height;

        if (screenWidth / screenHeight > imageRatio) {
            height = screenHeight;
            width = height * imageRatio;
        } else {
            width = screenWidth;
            height = width / imageRatio;
        }

        rectTransform.sizeDelta = new Vector2(width, height);

        gameObject.AddComponent<ScreenshotDisplayController>();
    }

    private void OnDestroy() {
        if (screenshotSprite != null) {
            Destroy(screenshotSprite);
        }

        if (screenshotTexture != null) {
            Destroy(screenshotTexture);
        }
    }
}

public class ScreenshotDisplayController : MonoBehaviour {
    void Update() {
        HeroActions inputActions = InputHandler.Instance.inputActions;

        if (PluginConfig.ScreenshotKey.IsDown() || 
            Input.GetKeyDown(KeyCode.Escape) ||
            inputActions.MenuSubmit.WasPressed ||
            inputActions.MenuCancel.WasPressed ||
            inputActions.MenuExtra.WasPressed ||
            inputActions.MenuSuper.WasPressed ||
            inputActions.Jump.WasPressed ||
            inputActions.Evade.WasPressed ||
            inputActions.Dash.WasPressed ||
            inputActions.SuperDash.WasPressed ||
            inputActions.DreamNail.WasPressed ||
            inputActions.Attack.WasPressed ||
            inputActions.Cast.WasPressed ||
            inputActions.QuickMap.WasPressed ||
            inputActions.QuickCast.WasPressed ||
            inputActions.Taunt.WasPressed ||
            inputActions.OpenInventory.WasPressed ||
            inputActions.OpenInventoryMap.WasPressed ||
            inputActions.OpenInventoryJournal.WasPressed ||
            inputActions.OpenInventoryTools.WasPressed ||
            inputActions.OpenInventoryQuests.WasPressed ||
            inputActions.Pause.WasPressed
            ) {
            ScreenshotDisplay.Close();
        }
    }
}
