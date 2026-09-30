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
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            GameObject fixture = new("Movement fixture");
            fixture.AddComponent<PlainMovementCheckpointBootstrap>();

            yield return null;
            PlainBlockDragAdapter adapter = fixture.GetComponent<PlainBlockDragAdapter>();
            Assert.IsNotNull(adapter);
            Transform block = fixture.transform.Find("L block");
            Assert.IsNotNull(block);
            Assert.AreEqual(3, block.childCount);

            Assert.IsFalse(camera.orthographic);
            Assert.That(camera.transform.eulerAngles.x,
                Is.EqualTo(ColorBlockEscapeBoardCamera.DefaultPitchDegrees).Within(0.01f));
            Assert.That(block.position.y, Is.GreaterThan(0f));
            Assert.That(block.position.z, Is.EqualTo(1f).Within(0.001f));
            Vector2 grab = camera.WorldToScreenPoint(World(1.4f, 1.4f));
            Vector2 moved = camera.WorldToScreenPoint(World(1.7f, 1.4f));
            adapter.ProcessPointerSample(0, grab, true, true, false);
            adapter.ProcessPointerSample(0, moved, false, true, false);
            Assert.That(block.position.x, Is.EqualTo(1.3f).Within(0.001f));
            adapter.ProcessPointerSample(0, moved, false, false, true);
            Assert.That(block.position.x, Is.EqualTo(1f).Within(0.001f));

            Object.Destroy(fixture);
            Object.Destroy(cameraObject);
        }

        private static Vector3 World(float x, float y) =>
            ColorBlockEscapeBoardSpace.CreateLayout(Vector3.zero)
                .BoardLocalToWorld(new Vector2(x, y));
    }
}
