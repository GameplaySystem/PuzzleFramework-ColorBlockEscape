using ColorBlockEscape.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ColorBlockEscape.Editor
{
    /// <summary>Creates the dedicated, disposable movement fixture scene for play checks.</summary>
    public static class MovementCheckpointSceneGenerator
    {
        [MenuItem("Color Block Escape/Create Plain Movement Checkpoint Scene")]
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
            cameraObject.transform.position = new Vector3(3.5f, 3f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;
            new GameObject("Plain Movement Checkpoint")
                .AddComponent<PlainMovementCheckpointBootstrap>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/PlainMovementCheckpoint.unity");
            AssetDatabase.Refresh();
        }
    }
}
