using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ColorBlockEscape.Editor
{
    /// <summary>Creates the dedicated Play-mode level authoring and play-test scene.</summary>
    public static class ColorBlockEscapeEditorSceneGenerator
    {
        [MenuItem("Color Block Escape/Create Level Editor Scene")]
        public static void Generate()
        {
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
            new GameObject("Color Block Escape Level Editor")
                .AddComponent<ColorBlockEscapeEditorController>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/ColorBlockEscapeLevelEditor.unity");
            AssetDatabase.Refresh();
        }
    }
}
