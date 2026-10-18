using COMP2165.Clicker;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace COMP2165.Clicker.Editor
{
    public static class ClickerCanvasSceneBuilder
    {
        static Font font;
        // Warm cookie browns, olive greens, and orange controls from the reference.
        static readonly Color cookiePanel = new Color32(95, 112, 46, 248);
        static readonly Color shopPanel = new Color32(116, 80, 41, 248);
        static readonly Color rowColor = new Color32(130, 84, 37, 248);
        static readonly Color buttonColor = new Color32(154, 74, 28, 255);

        static string ClickerScenePath()
        {
            foreach (string path in new[] { "Assets/Resources/Scenes/Clicker.unity", "Assets/Scenes/Clicker.unity", "Assets/Clicker/Scenes/Clicker.unity" })
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return path;
            return "Assets/Scenes/Clicker.unity";
        }

        static string PreviewPath(string filename)
        {
            string path = "Assets/Resources/UIPreview/" + filename;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null ? path : "Assets/Clicker/UIPreview/" + filename;
        }

        [MenuItem("COMP2165/Apply Palette CSV to Canvas")]
        public static void ApplyPaletteCsv()
        {
            string scenePath = ClickerScenePath();
            if (SceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var view = GameObject.Find("Editable Clicker Canvas")?.GetComponent<ClickerCanvasView>();
            if (view == null) { Debug.LogError("Editable Clicker Canvas is missing."); return; }
            ClickerTooltip.Ensure(view);
            if (!ApplyPaletteToView(view)) return;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Applied Palette.csv colours to the editable Canvas.");
        }

        static bool ApplyPaletteToView(ClickerCanvasView view)
        {
            string path = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Student Content", "Palette.csv");
            if (!System.IO.File.Exists(path)) path = System.IO.Path.Combine(Application.streamingAssetsPath, "Data", "Palette.csv");
            if (!System.IO.File.Exists(path)) { Debug.LogError("Palette.csv is missing: " + path); return false; }
            ClickerPalette.Apply(view, Csv.Read(System.IO.File.ReadAllText(path)));
            return true;
        }

        [MenuItem("COMP2165/Create Editable Canvas")]
        public static void CreateEditableCanvas()
        {
            Scene scene = SceneManager.GetActiveScene();
            string path = scene.path.EndsWith("/Clicker.unity") ? scene.path : ClickerScenePath();
            if (scene.path != path) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var game = Object.FindAnyObjectByType<ClickerGame>();
            if (game == null) { Debug.LogError("ClickerGame was not found in the scene."); return; }
            var existing = GameObject.Find("Editable Clicker Canvas");
            if (existing != null) { FinishExistingCanvas(); Debug.Log("Editable Clicker Canvas already exists. Edit it directly in the Hierarchy."); return; }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) { Debug.LogError("Unity's built-in LegacyRuntime.ttf font is missing."); return; }

            var root = new GameObject("Editable Clicker Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ClickerCanvasView));
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var view = root.GetComponent<ClickerCanvasView>();
            Transform r = root.transform;

            Panel(r, "Background Color", 0, 0, 1280, 720, new Color32(165, 144, 72, 255));
            view.background = Picture(r, "Workbook Background PNG", 0, 0, 1280, 720, null);
            view.background.enabled = false;
            Panel(r, "Background Dim", 0, 0, 1280, 720, new Color32(72, 55, 24, 89));

            Transform header = Panel(r, "Header - move and resize", 24, 20, 1232, 78, new Color32(112, 67, 31, 240)).transform;
            view.gameTitle = Label(header, "Game Title", 22, 8, 790, 55, "Cookie Works", 36, TextAnchor.MiddleLeft);
            view.eventCount = Label(header, "Event Count", 846, 10, 300, 50, "EVENTS x 0", 24, TextAnchor.MiddleLeft);
            view.stackButton = Button(header, "Open Event Stack", 967, 12, 237, 52, "OPEN STACK", 18, out view.stackButtonText);

            Transform left = Panel(r, "Cookie Panel - move and resize", 24, 114, 535, 580, cookiePanel).transform;
            view.money = Label(left, "Currency Balances", 24, 14, 480, 95, "0 Cookies\n0 Sugar", 24, TextAnchor.UpperLeft);
            view.production = Label(left, "Production Per Second", 24, 110, 480, 70, "0 Cookies/s\n0 Sugar/s", 16, TextAnchor.UpperLeft);
            view.cookieButton = PictureButton(left, "Clickable Cookie PNG", 96, 184, 335, 300, PreviewPath("clickable.png"), out view.cookie);
            view.gameDescription = Label(left, "Game Description", 31, 505, 460, 60, "Build a cookie factory.", 18, TextAnchor.MiddleLeft);

            Transform shop = Panel(r, "Shop Panel - move and resize", 575, 114, 681, 580, shopPanel).transform;
            view.buildingsTab = Button(shop, "Buildings Heading", 15, 14, 315, 34, "BUILDINGS", 19, out _);
            view.upgradesTab = Button(shop, "Upgrades Heading", 348, 14, 318, 34, "UPGRADES  0/22", 19, out view.upgradesTabText);
            view.buildingControls = NewRect(shop, "Building Purchase Controls", 15, 56, 315, 70).gameObject;
            Transform controls = view.buildingControls.transform;
            view.buyMode = Button(controls, "Buy Mode", 0, 0, 80, 36, "BUY", 15, out _);
            view.sellMode = Button(controls, "Sell Mode", 85, 0, 80, 36, "SELL", 15, out _);
            view.batchOne = Button(controls, "Batch 1", 174, 0, 42, 36, "×1", 14, out _);
            view.batchTen = Button(controls, "Batch 10", 220, 0, 42, 36, "×10", 14, out _);
            view.batchHundred = Button(controls, "Batch 100", 266, 0, 49, 36, "×100", 13, out _);
            view.batchStatus = Label(controls, "Batch Status", 0, 42, 315, 22, "Buying 1 at a time", 14, TextAnchor.MiddleLeft);
            view.buildingList = Scroll(shop, "Buildings Scroll Area", 15, 132, 315, 424, out view.buildingContent);
            view.upgradeList = Scroll(shop, "Upgrades Scroll Area", 348, 56, 318, 500, out view.upgradeContent);
            view.buildingTemplate = ShopRow(view.buildingContent, "Building Row TEMPLATE - edit this", PreviewPath("cursor.png"));
            view.upgradeTemplate = ShopRow(view.upgradeContent, "Upgrade Row TEMPLATE - edit this", PreviewPath("upgrade_click.png"));
            view.buildingTemplate.UseCompactLayout(18);
            view.upgradeTemplate.UseCompactLayout(17);
            view.buildingTemplate.gameObject.SetActive(false);
            view.upgradeTemplate.gameObject.SetActive(false);
            view.buildingsTab.enabled = false;
            view.upgradesTab.enabled = false;
            view.buildingsTab.GetComponent<Image>().raycastTarget = false;
            view.upgradesTab.GetComponent<Image>().raycastTarget = false;
            view.money.resizeTextForBestFit = true;
            view.money.resizeTextMinSize = 16;
            view.money.resizeTextMaxSize = 24;
            view.production.resizeTextForBestFit = true;
            view.production.resizeTextMinSize = 12;
            view.production.resizeTextMaxSize = 16;
            view.layoutVersion = 3;

            view.goldenButton = PictureButton(r, "Golden Cookie PNG", 480, 510, 85, 85, PreviewPath("golden_cookie.png"), out view.goldenCookie);
            view.goldenButton.gameObject.SetActive(false);

            view.eventOverlay = Panel(r, "Event Overlay - edit modal here", 0, 0, 1280, 720, new Color32(46, 33, 20, 196)).gameObject;
            view.eventOverlay.GetComponent<Image>().raycastTarget = true;
            Transform modal = Panel(view.eventOverlay.transform, "Event Card", 260, 115, 760, 490, new Color32(119, 80, 42, 255)).transform;
            view.eventTitle = Label(modal, "Event Title", 40, 30, 640, 50, "Event title", 24, TextAnchor.MiddleLeft);
            view.closeEvent = Button(modal, "Close Event", 682, 25, 48, 40, "×", 22, out _);
            view.eventImage = Picture(modal, "Event PNG from workbook", 40, 100, 170, 160, PreviewPath("event_quality.png"));
            view.eventDescription = Label(modal, "Event Description", 230, 105, 490, 150, "Event description", 18, TextAnchor.UpperLeft);
            view.optionOne = Button(modal, "Event Choice 1", 40, 285, 325, 90, "Choice one", 18, out view.optionOneText);
            view.optionTwo = Button(modal, "Event Choice 2", 395, 285, 325, 90, "Choice two", 18, out view.optionTwoText);
            view.eventQueueText = Label(modal, "Events Waiting", 40, 410, 650, 48, "Cards waiting", 15, TextAnchor.MiddleLeft);
            view.eventOverlay.SetActive(false);

            view.toastPanel = Panel(r, "Notification - always in front", 245, 642, 790, 50, new Color32(101, 122, 47, 250)).gameObject;
            view.toastText = Label(view.toastPanel.transform, "Notification Text", 15, 6, 760, 40, "Notification", 18, TextAnchor.MiddleLeft);
            view.toastPanel.SetActive(false);
            view.loadingPanel = Panel(r, "Loading Message", 0, 0, 1280, 720, new Color32(80, 75, 40, 245)).gameObject;
            view.loadingPanel.GetComponent<Image>().raycastTarget = true;
            view.loadingText = Label(view.loadingPanel.transform, "Loading or Error Text", 80, 260, 1120, 160, "Loading game data...", 24, TextAnchor.MiddleCenter);
            ClickerTooltip.Ensure(view);
            ApplyPaletteToView(view);

            if (GameObject.Find("EventSystem") == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Editable Canvas saved to " + path + ". Select its panels and row templates in the Hierarchy to change the layout.");
        }

        public static void FinishExistingCanvas()
        {
            var root = GameObject.Find("Editable Clicker Canvas");
            if (root == null) return;
            var view = root.GetComponent<ClickerCanvasView>();
            if (view == null) return;
            bool changed = false;
            if (view.layoutVersion < 3)
            {
                if (view.layoutVersion < 2) ApplyCombinedShopLayout(view);
                ApplyTwoColumnShopLayout(view);
                view.layoutVersion = 3;
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                changed = true;
            }
            if (view.GetComponentInChildren<ClickerTooltip>(true) == null) { ClickerTooltip.Ensure(view); changed = true; }
            if (view.layoutVersion < 3)
            {
                changed |= view.buildingTemplate.UseTitleOnlyLayout(24);
                changed |= view.upgradeTemplate.UseTitleOnlyLayout(22);
            }
            changed |= AssignPreview(view.cookie, PreviewPath("clickable.png"));
            changed |= AssignPreview(view.goldenCookie, PreviewPath("golden_cookie.png"));
            changed |= AssignPreview(view.eventImage, PreviewPath("event_quality.png"));
            changed |= AssignPreview(view.buildingTemplate.icon, PreviewPath("cursor.png"));
            changed |= AssignPreview(view.upgradeTemplate.icon, PreviewPath("upgrade_click.png"));
            var overlay = view.eventOverlay.GetComponent<Image>();
            var loading = view.loadingPanel.GetComponent<Image>();
            if (!overlay.raycastTarget) { overlay.raycastTarget = true; changed = true; }
            if (!loading.raycastTarget) { loading.raycastTarget = true; changed = true; }
            if (!changed) return;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Canvas previews and modal input blocking updated.");
        }

        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        static void ApplyCombinedShopLayout(ClickerCanvasView view)
        {
            Place(view.money.rectTransform, 24, 14, 480, 95);
            view.money.alignment = TextAnchor.UpperLeft;
            view.money.resizeTextForBestFit = true;
            view.money.resizeTextMinSize = 16;
            view.money.resizeTextMaxSize = 24;
            Place(view.production.rectTransform, 24, 110, 480, 70);
            view.production.alignment = TextAnchor.UpperLeft;
            view.production.fontSize = 16;
            view.production.resizeTextForBestFit = true;
            view.production.resizeTextMinSize = 12;
            view.production.resizeTextMaxSize = 16;
            Place(view.cookieButton.GetComponent<RectTransform>(), 96, 184, 335, 300);
            Place(view.gameDescription.rectTransform, 31, 505, 460, 60);
            Place(view.buildingsTab.GetComponent<RectTransform>(), 15, 14, 650, 34);
            Place(view.upgradesTab.GetComponent<RectTransform>(), 15, 318, 650, 34);
            view.buildingsTab.enabled = false;
            view.upgradesTab.enabled = false;
            view.buildingsTab.GetComponent<Image>().raycastTarget = false;
            view.upgradesTab.GetComponent<Image>().raycastTarget = false;
            Place(view.buildingControls.GetComponent<RectTransform>(), 0, 50, 681, 78);
            Place(view.buyMode.GetComponent<RectTransform>(), 15, 0, 120, 36);
            Place(view.sellMode.GetComponent<RectTransform>(), 143, 0, 120, 36);
            Place(view.batchOne.GetComponent<RectTransform>(), 341, 0, 91, 36);
            Place(view.batchTen.GetComponent<RectTransform>(), 443, 0, 91, 36);
            Place(view.batchHundred.GetComponent<RectTransform>(), 545, 0, 91, 36);
            Place(view.batchStatus.rectTransform, 15, 42, 630, 24);
            Place(view.buildingList.GetComponent<RectTransform>(), 15, 130, 650, 180);
            Place(view.upgradeList.GetComponent<RectTransform>(), 15, 358, 650, 198);
            view.buildingList.SetActive(true);
            view.upgradeList.SetActive(true);
            view.buildingsTab.name = "Buildings Heading";
            view.upgradesTab.name = "Upgrades Heading";
        }

        static void PlaceButton(Button button, float x, float y, float width, float height, int fontSize)
        {
            Place(button.GetComponent<RectTransform>(), x, y, width, height);
            var caption = button.GetComponentInChildren<Text>(true);
            if (caption == null) return;
            Place(caption.rectTransform, 4, 3, width - 8, height - 6);
            caption.fontSize = fontSize;
        }

        static void ApplyTwoColumnShopLayout(ClickerCanvasView view)
        {
            PlaceButton(view.buildingsTab, 15, 14, 315, 34, 19);
            PlaceButton(view.upgradesTab, 348, 14, 318, 34, 19);
            view.buildingsTab.enabled = false;
            view.upgradesTab.enabled = false;
            view.buildingsTab.GetComponent<Image>().raycastTarget = false;
            view.upgradesTab.GetComponent<Image>().raycastTarget = false;
            Place(view.buildingControls.GetComponent<RectTransform>(), 15, 56, 315, 70);
            PlaceButton(view.buyMode, 0, 0, 80, 36, 15);
            PlaceButton(view.sellMode, 85, 0, 80, 36, 15);
            PlaceButton(view.batchOne, 174, 0, 42, 36, 14);
            PlaceButton(view.batchTen, 220, 0, 42, 36, 14);
            PlaceButton(view.batchHundred, 266, 0, 49, 36, 13);
            Place(view.batchStatus.rectTransform, 0, 42, 315, 22);
            view.batchStatus.fontSize = 14;
            Place(view.buildingList.GetComponent<RectTransform>(), 15, 132, 315, 424);
            Place(view.upgradeList.GetComponent<RectTransform>(), 348, 56, 318, 500);
            view.buildingContent.sizeDelta = new Vector2(-20, view.buildingContent.sizeDelta.y);
            view.upgradeContent.sizeDelta = new Vector2(-20, view.upgradeContent.sizeDelta.y);
            view.buildingTemplate.UseCompactLayout(18);
            view.upgradeTemplate.UseCompactLayout(17);
            view.buildingList.SetActive(true);
            view.upgradeList.SetActive(true);
            view.buildingsTab.name = "Buildings Heading";
            view.upgradesTab.name = "Upgrades Heading";
        }

        [MenuItem("COMP2165/Validate Editable Canvas")]
        public static void ValidateEditableCanvas()
        {
            string path = ClickerScenePath();
            if (SceneManager.GetActiveScene().path != path) EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var root = GameObject.Find("Editable Clicker Canvas");
            if (root == null) throw new System.InvalidOperationException("Editable Canvas is missing from Clicker.unity.");
            var view = root.GetComponent<ClickerCanvasView>();
            if (view == null) throw new System.InvalidOperationException("ClickerCanvasView is missing from the Canvas.");
            foreach (var field in typeof(ClickerCanvasView).GetFields())
                if (field.GetValue(view) == null) throw new System.InvalidOperationException("Unassigned Canvas reference: " + field.Name);
            if (view.buildingTemplate.icon == null || view.buildingTemplate.action == null || view.upgradeTemplate.icon == null || view.upgradeTemplate.action == null)
                throw new System.InvalidOperationException("A shop row template has an unassigned control.");
            if (view.cookie.texture == null || view.goldenCookie.texture == null || view.eventImage.texture == null)
                throw new System.InvalidOperationException("Canvas preview textures are missing.");
            if (!view.eventOverlay.GetComponent<Image>().raycastTarget)
                throw new System.InvalidOperationException("Event overlay does not block clicks behind it.");
            if (Object.FindAnyObjectByType<ClickerGame>() == null || Object.FindAnyObjectByType<EventSystem>() == null)
                throw new System.InvalidOperationException("Game controller or EventSystem is missing.");
            Debug.Log("Editable Canvas validated: all references, preview images, modal blocking, and input objects are present.");
        }

        static bool AssignPreview(RawImage image, string path)
        {
            if (image == null || image.texture != null) return false;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return false;
            image.texture = texture;
            return true;
        }

        static RectTransform NewRect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Image Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var image = NewRect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(Transform parent, string name, float x, float y, float width, float height, string content, int size, TextAnchor alignment)
        {
            var text = NewRect(parent, name, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.text = content;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        static RawImage Picture(Transform parent, string name, float x, float y, float width, float height, string asset)
        {
            var image = NewRect(parent, name, x, y, width, height).gameObject.AddComponent<RawImage>();
            image.texture = asset == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            image.raycastTarget = false;
            return image;
        }

        static Button Button(Transform parent, string name, float x, float y, float width, float height, string content, int size, out Text caption)
        {
            var rect = NewRect(parent, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = buttonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            caption = Label(rect, "Caption - edit font here", 7, 4, width - 14, height - 8, content, size, TextAnchor.MiddleCenter);
            return button;
        }

        static Button PictureButton(Transform parent, string name, float x, float y, float width, float height, string asset, out RawImage picture)
        {
            var rect = NewRect(parent, name, x, y, width, height);
            picture = rect.gameObject.AddComponent<RawImage>();
            picture.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = picture;
            return button;
        }

        static GameObject Scroll(Transform parent, string name, float x, float y, float width, float height, out RectTransform content)
        {
            var viewport = NewRect(parent, name, x, y, width, height);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.viewport = viewport;
            content = NewRect(viewport, "Content - rows appear here", 0, 0, width - 20, 0);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(-20, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            return viewport.gameObject;
        }

        static ClickerShopRow ShopRow(Transform parent, string name, string sampleIcon)
        {
            var rect = NewRect(parent, name, 0, 0, 610, 92);
            rect.gameObject.AddComponent<Image>().color = rowColor;
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 92;
            var row = rect.gameObject.AddComponent<ClickerShopRow>();
            row.icon = Picture(rect, "Icon PNG from workbook", 10, 9, 72, 72, sampleIcon);
            row.title = Label(rect, "Name and Count", 90, 12, 335, 68, "Building name", 24, TextAnchor.MiddleLeft);
            row.description = Label(rect, "Description", 90, 38, 335, 48, "Description and production", 15, TextAnchor.MiddleLeft);
            row.description.gameObject.SetActive(false);
            row.action = Button(rect, "Buy or Sell Button", 440, 17, 150, 58, "BUY", 18, out row.actionText);
            return row;
        }
    }
}
