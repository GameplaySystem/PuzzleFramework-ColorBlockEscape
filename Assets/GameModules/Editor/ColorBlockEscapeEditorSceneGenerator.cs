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
            camera.orthographic = true;
            camera.orthographicSize = 4.7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.17f);
            cameraObject.transform.position = new Vector3(3f, 3f, -10f);
            new GameObject("Color Block Escape Level Editor")
                .AddComponent<ColorBlockEscapeEditorController>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/ColorBlockEscapeLevelEditor.unity");
            AssetDatabase.Refresh();
        }
    }
}
