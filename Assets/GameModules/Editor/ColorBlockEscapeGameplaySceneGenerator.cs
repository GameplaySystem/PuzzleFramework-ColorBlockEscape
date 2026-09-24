using System.Collections.Generic;
using System.IO;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ColorBlockEscape.Editor
{
    /// <summary>Creates the standalone gameplay scene and its first authored level asset.</summary>
    public static class ColorBlockEscapeGameplaySceneGenerator
    {
        public const string ScenePath = "Assets/Scenes/ColorBlockEscapeGameplay.unity";
        public const string LevelPath = "Assets/Levels/Level01.json";

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
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.17f);
            cameraObject.transform.position = new Vector3(3f, 3f, -10f);

            ColorBlockEscapeRuntimeController controller =
                new GameObject("Color Block Escape Gameplay")
                    .AddComponent<ColorBlockEscapeRuntimeController>();
            SerializedObject serialized = new(controller);
            serialized.Update();
            SerializedProperty levels = serialized.FindProperty("_levelAssets");
            levels.arraySize = 1;
            levels.GetArrayElementAtIndex(0).objectReferenceValue = levelAsset;
            serialized.FindProperty("_startingLevelIndex").intValue = 0;
            serialized.FindProperty("_boardCellPrefab").objectReferenceValue = boardPrefab;
            serialized.FindProperty("_showHud").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void WriteLevelAsset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LevelPath));
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            authored.Core.SetMetadata("cbe-level-01", "Level 01", 1);
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
