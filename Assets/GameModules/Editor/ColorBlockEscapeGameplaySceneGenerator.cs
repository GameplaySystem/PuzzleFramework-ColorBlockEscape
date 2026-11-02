using System.Collections.Generic;
using System.IO;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ColorBlockEscape.Editor
{
    /// <summary>Creates the standalone gameplay scene and its first authored level asset.</summary>
    public static class ColorBlockEscapeGameplaySceneGenerator
    {
        public const string ScenePath = "Assets/Scenes/ColorBlockEscapeGameplay.unity";
        public const string LevelPath =
            "Assets/Resources/ColorBlockEscape/Levels/level_1.json";
        public const string FontAssetPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string CanvasName = "Color Block Escape Canvas";
        private const string EventSystemName = "CBE Gameplay EventSystem";

        [MenuItem("Color Block Escape/Create Gameplay Scene")]
        public static void Generate()
        {
            WriteLevelAsset();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TextAsset levelAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LevelPath);
            ModularBoardCellView boardPrefab = AssetDatabase.LoadAssetAtPath<ModularBoardCellView>(
                "Assets/RuntimeAssets/Board/CellPrefab.prefab");
            if (levelAsset == null || boardPrefab == null)
                throw new System.InvalidOperationException(
                    "Gameplay scene generation requires the authored level and board-cell prefab.");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = ColorBlockEscapeBoardCamera.DefaultFieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.17f);
            cameraObject.transform.rotation = Quaternion.Euler(
                ColorBlockEscapeBoardCamera.DefaultPitchDegrees, 0f, 0f);

            ColorBlockEscapeRuntimeController controller =
                new GameObject("Color Block Escape Gameplay")
                    .AddComponent<ColorBlockEscapeRuntimeController>();
            SerializedObject serialized = new(controller);
            serialized.Update();
            SerializedProperty levels = serialized.FindProperty("_levelAssets");
            levels.arraySize = 1;
            levels.GetArrayElementAtIndex(0).objectReferenceValue = levelAsset;
            serialized.FindProperty("_useResourcesLevelCatalog").boolValue = true;
            serialized.FindProperty("_resourcesLevelCatalogPath").stringValue =
                ColorBlockEscapeRuntimeController.DefaultResourcesLevelCatalogPath;
            serialized.FindProperty("_startingLevelIndex").intValue = 0;
            serialized.FindProperty("_boardCellPrefab").objectReferenceValue = boardPrefab;
            serialized.FindProperty("_legacyGameplayGuiEnabled").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            CreateCanvasUi(controller, EnsureFontAsset());
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Color Block Escape/Install Gameplay Canvas UI")]
        public static void InstallCanvasUi()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            if (controller == null)
                throw new System.InvalidOperationException(
                    "The CBE gameplay scene has no runtime controller.");

            GameObject existingCanvas = GameObject.Find(CanvasName);
            if (existingCanvas != null) Object.DestroyImmediate(existingCanvas);
            GameObject existingEventSystem = GameObject.Find(EventSystemName);
            if (existingEventSystem != null) Object.DestroyImmediate(existingEventSystem);

            SerializedObject controllerData = new(controller);
            controllerData.FindProperty("_legacyGameplayGuiEnabled").boolValue = false;
            controllerData.ApplyModifiedPropertiesWithoutUndo();
            CreateCanvasUi(controller, EnsureFontAsset());
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static TMP_FontAsset EnsureFontAsset()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null) return existing;
            throw new System.InvalidOperationException(
                "The committed TMP Essential Resources and default font asset are missing.");
        }

        private static void CreateCanvasUi(ColorBlockEscapeRuntimeController controller,
            TMP_FontAsset font)
        {
            GameObject eventSystemObject = new(EventSystemName,
                typeof(EventSystem), typeof(InputSystemUIInputModule));

            GameObject canvasObject = CreateUiObject(CanvasName, null);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            Color ink = new(0.94f, 0.97f, 1f, 1f);
            Color muted = new(0.69f, 0.75f, 0.84f, 1f);
            Color panel = new(0.055f, 0.075f, 0.12f, 0.97f);
            Color panelSoft = new(0.075f, 0.105f, 0.17f, 0.96f);
            Color accent = new(0.17f, 0.58f, 0.95f, 1f);
            Color success = new(0.20f, 0.69f, 0.43f, 1f);
            Color danger = new(0.83f, 0.20f, 0.18f, 1f);

            Image hud = CreateImage("HUD", canvasObject.transform, panelSoft);
            Anchor(hud.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(-64f, 108f));
            TMP_Text levelLabel = CreateText("Level Label", hud.transform, font,
                "Level 1", 36f, TextAlignmentOptions.MidlineLeft, ink);
            Stretch(levelLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0.38f, 1f),
                new Vector2(30f, 8f), new Vector2(-8f, -8f));

            Image timerBackground = CreateImage("Timer", hud.transform,
                new Color(0.08f, 0.12f, 0.20f, 0.94f));
            Anchor(timerBackground.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(230f, 76f));
            TMP_Text timerLabel = CreateText("Timer Label", timerBackground.transform, font,
                "01:30", 43f, TextAlignmentOptions.Center, ink);
            Stretch(timerLabel.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(10f, 4f), new Vector2(-10f, -4f));

            Button chooseButton = CreateButton("Choose Level Button", hud.transform, font,
                "CHOOSE LEVEL", accent, 25f);
            Anchor((RectTransform)chooseButton.transform, new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f),
                new Vector2(230f, 70f));

            Image chooserBackdrop = CreateImage("Level Chooser", canvasObject.transform,
                new Color(0.015f, 0.025f, 0.055f, 0.80f));
            Stretch(chooserBackdrop.rectTransform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            Image chooserPanel = CreateImage("Chooser Panel", chooserBackdrop.transform, panel);
            Anchor(chooserPanel.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(720f, 820f));
            TMP_Text chooserTitle = CreateText("Title", chooserPanel.transform, font,
                "CHOOSE LEVEL", 48f, TextAlignmentOptions.MidlineLeft, ink);
            Anchor(chooserTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(-20f, -34f), new Vector2(-180f, 88f));
            Button closeChooser = CreateButton("Close Button", chooserPanel.transform, font,
                "CLOSE", panelSoft, 24f);
            Anchor((RectTransform)closeChooser.transform, new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -32f),
                new Vector2(150f, 66f));

            GameObject scrollObject = CreateUiObject("Level List", chooserPanel.transform);
            Image scrollBackground = scrollObject.AddComponent<Image>();
            scrollBackground.color = new Color(0.03f, 0.045f, 0.08f, 0.9f);
            ScrollRect scrollRect = scrollObject.AddComponent<ScrollRect>();
            Stretch((RectTransform)scrollObject.transform, Vector2.zero, Vector2.one,
                new Vector2(42f, 42f), new Vector2(-42f, -150f));
            Image viewport = CreateImage("Viewport", scrollObject.transform, Color.clear);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            Stretch(viewport.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(16f, 16f), new Vector2(-16f, -16f));
            GameObject contentObject = CreateUiObject("Content", viewport.transform);
            RectTransform content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup listLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            listLayout.padding = new RectOffset(8, 8, 8, 8);
            listLayout.spacing = 14f;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport.rectTransform;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            Button levelTemplate = CreateButton("Level Button Template", content, font,
                "Level", accent, 30f);
            levelTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
            levelTemplate.gameObject.SetActive(false);

            ResultUi win = CreateResultUi("Win Result", canvasObject.transform, font,
                "LEVEL COMPLETE", "The board is clear.", success, panel, ink, muted,
                new[] { "NEXT LEVEL", "REPLAY LEVEL", "CHOOSE LEVEL" });
            ResultUi loss = CreateResultUi("Loss Result", canvasObject.transform, font,
                "TIME UP", "Try the level again.", danger, panel, ink, muted,
                new[] { "RETRY", "CHOOSE LEVEL" });

            ColorBlockEscapeCanvasPresenter presenter =
                canvasObject.AddComponent<ColorBlockEscapeCanvasPresenter>();
            SerializedObject data = new(presenter);
            SetReference(data, "_controller", controller);
            SetReference(data, "_levelLabel", levelLabel);
            SetReference(data, "_timerLabel", timerLabel);
            SetReference(data, "_timerBackground", timerBackground);
            SetReference(data, "_chooseLevelButton", chooseButton);
            SetReference(data, "_levelChooserPanel", chooserBackdrop.gameObject);
            SetReference(data, "_levelButtonContent", content);
            SetReference(data, "_levelButtonTemplate", levelTemplate);
            SetReference(data, "_closeChooserButton", closeChooser);
            SetReference(data, "_winPanel", win.Root);
            SetReference(data, "_winMessage", win.Message);
            SetReference(data, "_nextLevelButton", win.Buttons[0]);
            SetReference(data, "_replayButton", win.Buttons[1]);
            SetReference(data, "_winChooseLevelButton", win.Buttons[2]);
            SetReference(data, "_lossPanel", loss.Root);
            SetReference(data, "_retryButton", loss.Buttons[0]);
            SetReference(data, "_lossChooseLevelButton", loss.Buttons[1]);
            data.ApplyModifiedPropertiesWithoutUndo();

            chooserBackdrop.gameObject.SetActive(false);
            win.Root.SetActive(false);
            loss.Root.SetActive(false);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(eventSystemObject);
        }

        private static ResultUi CreateResultUi(string name, Transform parent, TMP_FontAsset font,
            string title, string message, Color accent, Color panel, Color ink, Color muted,
            IReadOnlyList<string> buttonLabels)
        {
            Image backdrop = CreateImage(name, parent,
                new Color(0.015f, 0.025f, 0.055f, 0.82f));
            Stretch(backdrop.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image card = CreateImage("Result Card", backdrop.transform, panel);
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 650f));
            Image stripe = CreateImage("Accent", card.transform, accent);
            Anchor(stripe.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 16f));
            TMP_Text titleText = CreateText("Title", card.transform, font, title, 55f,
                TextAlignmentOptions.Center, ink);
            Anchor(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(-60f, 100f));
            TMP_Text messageText = CreateText("Message", card.transform, font, message, 29f,
                TextAlignmentOptions.Center, muted);
            Anchor(messageText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(-80f, 80f));

            GameObject actions = CreateUiObject("Actions", card.transform);
            Anchor((RectTransform)actions.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(-100f, 310f));
            VerticalLayoutGroup layout = actions.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            Button[] buttons = new Button[buttonLabels.Count];
            for (int i = 0; i < buttonLabels.Count; i++)
            {
                buttons[i] = CreateButton($"{buttonLabels[i]} Button", actions.transform, font,
                    buttonLabels[i], i == 0 ? accent : new Color(0.12f, 0.17f, 0.26f, 1f), 29f);
                buttons[i].gameObject.AddComponent<LayoutElement>().preferredHeight = 82f;
            }
            return new ResultUi(backdrop.gameObject, messageText, buttons);
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject result = new(name, typeof(RectTransform));
            if (parent != null) result.transform.SetParent(parent, false);
            return result;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject result = CreateUiObject(name, parent);
            Image image = result.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font,
            string text, float size, TextAlignmentOptions alignment, Color color)
        {
            GameObject result = CreateUiObject(name, parent);
            TextMeshProUGUI label = result.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private static Button CreateButton(string name, Transform parent, TMP_FontAsset font,
            string label, Color color, float fontSize)
        {
            Image background = CreateImage(name, parent, color);
            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.78f, 0.82f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            TMP_Text text = CreateText("Label", button.transform, font, label, fontSize,
                TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(12f, 4f), new Vector2(-12f, -4f));
            return button;
        }

        private static void SetReference(SerializedObject data, string property,
            Object value)
        {
            data.FindProperty(property).objectReferenceValue = value;
        }

        private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private readonly struct ResultUi
        {
            public ResultUi(GameObject root, TMP_Text message, Button[] buttons)
            {
                Root = root;
                Message = message;
                Buttons = buttons;
            }

            public GameObject Root { get; }
            public TMP_Text Message { get; }
            public Button[] Buttons { get; }
        }

        private static void WriteLevelAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LevelPath));
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Require(authored.SetLevelNumber(1));
            Require(authored.SetTimer(90f, 10f));
            Require(authored.SetCell(new GridCoordinate(3, 2), AuthoredCellState.Blocked));
            Require(authored.PlaceBlock("red-bar", new GridCoordinate(2, 4),
                BlockShapePresets.Get(BlockShapePreset.Bar2), ColorIdentity.Slot0));
            Require(authored.PlaceBlock("blue-l", new GridCoordinate(0, 1),
                BlockShapePresets.Get(BlockShapePreset.L), ColorIdentity.Slot1));
            Require(authored.PlaceBlock("green-single", new GridCoordinate(5, 4),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot2));
            Require(authored.PutExit("red-top", ExitSide.Top,
                new GridCoordinate(2, 5), 2, ColorIdentity.Slot0));
            Require(authored.PutExit("blue-left", ExitSide.Left,
                new GridCoordinate(0, 1), 3, ColorIdentity.Slot1));
            Require(authored.PutExit("green-right", ExitSide.Right,
                new GridCoordinate(5, 4), 1, ColorIdentity.Slot2));
            if (!authored.TryCreateLevel(out LevelDefinition definition, out string failure))
                throw new System.InvalidOperationException(failure);
            File.WriteAllText(LevelPath, JsonUtility.ToJson(definition, true));
        }

        private static void Require(AuthoringEditResult result)
        {
            if (!result.Success) throw new System.InvalidOperationException(result.Reason);
        }

        private static void AddToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);
            if (scenes.Exists(scene => scene.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
