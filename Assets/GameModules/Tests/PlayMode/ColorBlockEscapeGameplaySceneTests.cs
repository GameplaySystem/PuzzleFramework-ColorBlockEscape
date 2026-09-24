using System.Collections;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using NUnit.Framework;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using PuzzleFramework.RuntimeFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeGameplaySceneTests
    {
        private const string SceneName = "ColorBlockEscapeGameplay";

        [UnityTest]
        public IEnumerator GameplaySceneBootsBuildsAuthoredLevelAndAllowsBlockMovement()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;

            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            Assert.IsNotNull(controller);
            Assert.IsTrue(controller.IsRunning, controller.StatusMessage);
            Assert.AreEqual(1, controller.LevelCount);
            Assert.AreEqual(0, controller.SelectedLevelIndex);
            Assert.AreEqual(3, controller.Level.Blocks.Count);
            Assert.AreEqual(3, controller.Level.Exits.Count);
            Assert.AreEqual(3, controller.BlockViews.Count);
            Assert.IsNotNull(controller.DragAdapter);
            Assert.IsNotNull(GameObject.Find("Modular board visuals"),
                "The standalone scene must use the configured modular board prefab.");
            Assert.IsTrue(controller.Level.FrameworkContext.CellOccupancySystem.IsOccupied(
                new GridCoordinate(2, 4)));
            Assert.IsNotNull(controller.BlockViews["red-bar"].GetComponent<MeshFilter>());

            Camera camera = Camera.main;
            Assert.IsNotNull(camera);
            Assert.IsTrue(controller.Level.TryGetBlock("red-bar", out BlockRuntimeState block));
            Vector2 grab = camera.WorldToScreenPoint(new Vector3(2.5f, 4.5f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            Assert.AreEqual("red-bar", controller.DragAdapter.ActiveBlockId);
            Vector2 moved = camera.WorldToScreenPoint(new Vector3(2.5f, 3.8f));
            controller.DragAdapter.ProcessPointerSample(0, moved, false, true, false);
            Assert.That(block.ContinuousOrigin.y, Is.LessThan(4f));
            controller.DragAdapter.ProcessPointerSample(0, moved, false, false, true);
            Assert.AreEqual(BlockLifecycle.OnBoard, block.Lifecycle);
        }

        [UnityTest]
        public IEnumerator GameplayHostIntegratesCaptureOutcomeTimerAndFreshRestart()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            Camera camera = Camera.main;
            Assert.IsNotNull(controller);
            Assert.IsNotNull(camera);

            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.SetTimer(5f, 1f).Success);
            Assert.IsTrue(authored.PlaceBlock("test-block", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("test-exit", ExitSide.Top,
                new GridCoordinate(2, 5), 1, ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.TryCreateLevel(out var definition, out string failure), failure);
            Assert.IsTrue(controller.TryStart(definition, camera,
                exitSettings: new ExitCaptureSettings(0.70f, 0.35f, 20f), showHud: false),
                controller.StatusMessage);

            ColorBlockEscapeRuntimeSession first = controller.Session;
            yield return null;
            Assert.That(first.Outcome.RemainingSeconds, Is.LessThan(5f));
            Assert.AreEqual(GameState.Playing, first.Outcome.State);

            Vector2 grab = camera.WorldToScreenPoint(new Vector3(2.5f, 5.5f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            Vector2 outward = camera.WorldToScreenPoint(new Vector3(2.5f, 6.6f));
            controller.DragAdapter.ProcessPointerSample(0, outward, false, true, false);
            Assert.AreEqual(BlockLifecycle.Exiting, first.Level.Blocks[0].Lifecycle);
            Assert.IsTrue(first.Level.Exits[0].IsBusy);
            yield return null;
            Assert.AreEqual(GameState.Won, first.Outcome.State);

            Assert.IsTrue(controller.RestartCurrentLevel(), controller.StatusMessage);
            Assert.AreNotSame(first, controller.Session);
            Assert.AreEqual(GameState.Playing, controller.Outcome.State);
            Assert.AreEqual(5f, controller.Outcome.RemainingSeconds, 0.0001f);
            Assert.AreEqual(BlockLifecycle.OnBoard, controller.Level.Blocks[0].Lifecycle);
            Assert.IsFalse(controller.Level.Exits[0].IsBusy);
            Assert.IsTrue(controller.Level.FrameworkContext.CellOccupancySystem.IsOccupied(
                new GridCoordinate(2, 5)));
            Assert.IsNotNull(controller.DragAdapter);
        }
    }
}
