using System.Collections;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeEditorPlayTestTests
    {
        [UnityTest]
        public IEnumerator AuthoredPlayTestPointerCapturesMatchingExitWithoutPresentation()
        {
            GameObject cameraObject = new("CBE capture test camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(3f, 3f, -10f);
            GameObject editorObject = new("CBE capture test editor");
            ColorBlockEscapeEditorController editor =
                editorObject.AddComponent<ColorBlockEscapeEditorController>();
            yield return null;
            try
            {
                Assert.IsTrue(editor.Model.PlaceBlock("block", new GridCoordinate(2, 5),
                    BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
                Assert.IsTrue(editor.Model.PutExit("exit", ExitSide.Top,
                    new GridCoordinate(2, 5), 1, ColorIdentity.Slot0).Success);
                Assert.IsTrue(editor.BeginPlayTest());
                ColorBlockEscapeRuntimeLevel level = editor.PlayTestLevel;
                Assert.IsNotNull(level);
                PlainBlockDragAdapter adapter = GameObject.Find("CBE isolated play-test")
                    .GetComponent<PlainBlockDragAdapter>();
                Vector2 grab = camera.WorldToScreenPoint(new Vector3(2.5f, 5.5f));
                adapter.ProcessPointerSample(0, grab, true, true, false);
                Assert.AreEqual(BlockLifecycle.OnBoard, level.Blocks[0].Lifecycle);
                Vector2 outward = camera.WorldToScreenPoint(new Vector3(2.5f, 6.6f));
                adapter.ProcessPointerSample(0, outward, false, true, false);
                Assert.AreEqual(BlockLifecycle.Exiting, level.Blocks[0].Lifecycle);
                Assert.IsTrue(level.Exits[0].IsBusy);
                Assert.IsTrue(level.FrameworkContext.CellOccupancySystem.IsOccupied(
                    new GridCoordinate(2, 5)));
                editor.ReturnFromPlayTest();
                Assert.IsTrue(editor.Model.Core.TryGetItem("block", out AuthoredFootprint item));
                Assert.AreEqual(new GridCoordinate(2, 5), item.Origin);
            }
            finally
            {
                Object.Destroy(editorObject);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredLevelLaunchesAndReturnsToUnchangedEditorSelection()
        {
            GameObject cameraObject = new("CBE editor test camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(3f, 3f, -10f);
            GameObject editorObject = new("CBE editor test");
            ColorBlockEscapeEditorController editor =
                editorObject.AddComponent<ColorBlockEscapeEditorController>();
            yield return null;
            try
            {
                Assert.IsTrue(editor.Model.PlaceBlock("block", new GridCoordinate(1, 1),
                    BlockShapePresets.Get(BlockShapePreset.Bar2), ColorIdentity.Slot0).Success);
                Assert.IsTrue(editor.Model.PutExit("exit", ExitSide.Top,
                    new GridCoordinate(1, 5), 2, ColorIdentity.Slot0).Success);
                Assert.IsTrue(editor.Model.SelectBlockAt(new GridCoordinate(1, 1)));
                Assert.IsTrue(editor.BeginPlayTest());
                Assert.IsTrue(editor.IsPlayTesting);
                Assert.AreEqual("block", editor.Model.Core.SelectedItemId);
                editor.ReturnFromPlayTest();
                Assert.IsFalse(editor.IsPlayTesting);
                Assert.AreEqual("block", editor.Model.Core.SelectedItemId);
                Assert.IsTrue(editor.Model.Core.TryGetItem("block", out AuthoredFootprint item));
                Assert.AreEqual(new GridCoordinate(1, 1), item.Origin);
            }
            finally
            {
                Object.Destroy(editorObject);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }
    }
}
