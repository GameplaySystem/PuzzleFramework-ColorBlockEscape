using System.Collections;
using ColorBlockEscape.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ColorBlockEscape.Tests
{
    public sealed class PlainMovementSceneTests
    {
        [UnityTest]
        public IEnumerator CheckpointFixtureRendersAndMovesSubcellBeforeRelease()
        {
            foreach (Camera existing in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                existing.gameObject.tag = "Untagged";
            GameObject cameraObject = new("Movement test camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.7f;
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            cameraObject.transform.position = new Vector3(3.5f, 3f, -10f);
            GameObject fixture = new("Movement fixture");
            fixture.AddComponent<PlainMovementCheckpointBootstrap>();

            yield return null;
            PlainBlockDragAdapter adapter = fixture.GetComponent<PlainBlockDragAdapter>();
            Assert.IsNotNull(adapter);
            Transform block = fixture.transform.Find("L block");
            Assert.IsNotNull(block);
            Assert.AreEqual(3, block.childCount);

            Vector2 grab = camera.WorldToScreenPoint(new Vector3(1.4f, 1.4f));
            Vector2 moved = camera.WorldToScreenPoint(new Vector3(1.7f, 1.4f));
            adapter.ProcessPointerSample(0, grab, true, true, false);
            adapter.ProcessPointerSample(0, moved, false, true, false);
            Assert.That(block.position.x, Is.EqualTo(1.3f).Within(0.001f));
            adapter.ProcessPointerSample(0, moved, false, false, true);
            Assert.That(block.position.x, Is.EqualTo(1f).Within(0.001f));

            Object.Destroy(fixture);
            Object.Destroy(cameraObject);
        }
    }
}
