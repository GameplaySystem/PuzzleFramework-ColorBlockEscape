using ColorBlockEscape.Runtime;
using NUnit.Framework;
using PuzzleFramework.CoreBoard;
using UnityEngine;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeBoardCameraTests
    {
        [Test]
        public void PortraitCameraUsesAngledPerspectiveAndContainsWholeBoard()
        {
            GameObject cameraObject = new("CBE portrait camera test");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.pixelRect = new Rect(0f, 0f, 1000f, 2000f);
                GridWorldLayout layout =
                    ColorBlockEscapeBoardSpace.CreateLayout(Vector3.zero);

                Assert.IsTrue(ColorBlockEscapeBoardCamera.TryFrame(
                    camera,
                    layout,
                    6,
                    10,
                    ColorBlockEscapeBoardCamera.DefaultPitchDegrees,
                    ColorBlockEscapeBoardCamera.DefaultFieldOfView,
                    ColorBlockEscapeBoardCamera.DefaultFramingPadding,
                    ColorBlockEscapeBoardCamera.DefaultMinimumDistance,
                    out string failure), failure);

                Assert.IsFalse(camera.orthographic);
                Assert.That(camera.transform.eulerAngles.x,
                    Is.EqualTo(ColorBlockEscapeBoardCamera.DefaultPitchDegrees).Within(0.001f));
                AssertVisible(camera, layout.BoardLocalToWorld(new Vector2(0f, 0f)));
                AssertVisible(camera, layout.BoardLocalToWorld(new Vector2(6f, 0f)));
                AssertVisible(camera, layout.BoardLocalToWorld(new Vector2(0f, 10f)));
                AssertVisible(camera, layout.BoardLocalToWorld(new Vector2(6f, 10f)));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void AssertVisible(Camera camera, Vector3 worldPoint)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPoint);
            Assert.That(screen.z, Is.GreaterThan(0f));
            Assert.That(screen.x, Is.InRange(0f, camera.pixelWidth));
            Assert.That(screen.y, Is.InRange(0f, camera.pixelHeight));
        }
    }
}
