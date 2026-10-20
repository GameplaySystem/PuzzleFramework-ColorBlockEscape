using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using ColorBlockEscape.Runtime.Presentation;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using PuzzleFramework.RuntimeFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
            Assert.AreEqual("Level 01", controller.GetLevelDisplayLabel(0));
            Assert.AreEqual("Level 01", controller.SelectedLevelDisplayLabel);
            Assert.IsTrue(controller.HasTimer);
            Assert.AreEqual(10f, controller.TimerWarningThresholdSeconds, 0.0001f);
            Assert.AreEqual(GameState.Playing, controller.CommittedOutcome);
            Assert.AreEqual(3, controller.Level.Blocks.Count);
            Assert.AreEqual(3, controller.Level.Exits.Count);
            Assert.AreEqual(3, controller.BlockViews.Count);
            Assert.IsNotNull(controller.DragAdapter);
            Assert.IsNotNull(controller.ChipperPresenter);
            ColorBlockEscapeCanvasPresenter presenter =
                Object.FindFirstObjectByType<ColorBlockEscapeCanvasPresenter>();
            Assert.IsNotNull(presenter);
            Assert.AreSame(controller, presenter.Controller);
            Assert.AreEqual("Level 01", presenter.DisplayedLevelLabel);
            StringAssert.IsMatch("^[0-9]{2}:[0-9]{2}$", presenter.DisplayedTimer);
            Assert.IsFalse(controller.LegacyGameplayGuiEnabled);
            Assert.IsNotNull(EventSystem.current);
            Assert.IsNotNull(EventSystem.current.GetComponent<InputSystemUIInputModule>());
            Canvas authoredCanvas = presenter.GetComponent<Canvas>();
            Assert.IsNotNull(authoredCanvas);
            Assert.IsNotNull(authoredCanvas.GetComponent<GraphicRaycaster>());
            Assert.IsNotNull(GameObject.Find("Modular board visuals"),
                "The standalone scene must use the configured modular board prefab.");
            Assert.IsTrue(controller.Level.FrameworkContext.CellOccupancySystem.IsOccupied(
                new GridCoordinate(2, 4)));
            Transform redBarView = controller.BlockViews["red-bar"];
            MeshFilter blockFilter = redBarView.GetComponent<MeshFilter>();
            MeshRenderer blockRenderer = redBarView.GetComponent<MeshRenderer>();
            Assert.IsNotNull(blockFilter);
            Assert.AreEqual(2, blockFilter.sharedMesh.subMeshCount,
                "Runtime blocks must expose physical body and edge surfaces.");
            Assert.AreEqual(2, blockRenderer.sharedMaterials.Length);
            Assert.IsNull(redBarView.Find("Block outline"),
                "Physical edge surfaces must not use an overlapping child mesh.");

            Camera camera = Camera.main;
            Assert.IsNotNull(camera);
            Assert.IsFalse(camera.orthographic);
            Assert.That(camera.transform.eulerAngles.x,
                Is.EqualTo(controller.CameraPitchDegrees).Within(0.01f));
            Assert.IsTrue(controller.Level.TryGetBlock("red-bar", out BlockRuntimeState block));
            Assert.That(controller.BlockViews["red-bar"].position.y, Is.GreaterThan(0f));
            Assert.That(controller.BlockViews["red-bar"].position.z, Is.EqualTo(4f).Within(0.001f));
            Vector2 grab = camera.WorldToScreenPoint(World(2.5f, 4.5f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            Assert.AreEqual("red-bar", controller.DragAdapter.ActiveBlockId);
            Vector2 moved = camera.WorldToScreenPoint(World(2.5f, 3.8f));
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
            ColorBlockEscapeCanvasPresenter presenter =
                Object.FindFirstObjectByType<ColorBlockEscapeCanvasPresenter>();
            Assert.IsNotNull(presenter);

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

            Vector2 grab = camera.WorldToScreenPoint(World(2.5f, 5.5f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            Vector2 outward = camera.WorldToScreenPoint(World(2.5f, 6.6f));
            controller.DragAdapter.ProcessPointerSample(0, outward, false, true, false);
            Assert.AreEqual(BlockLifecycle.Exiting, first.Level.Blocks[0].Lifecycle);
            Assert.IsTrue(first.Level.Exits[0].IsBusy);
            yield return null;
            Assert.AreEqual(GameState.Won, first.Outcome.State);
            Assert.IsTrue(controller.ChipperPresenter.IsBusy);
            Assert.IsFalse(controller.IsResultPresentationVisible);
            Assert.IsFalse(presenter.IsWinPanelVisible);

            ColorBlockEscapeChipperPresenter firstPresenter = controller.ChipperPresenter;
            Assert.IsTrue(controller.RestartCurrentLevel(), controller.StatusMessage);
            Assert.AreNotSame(first, controller.Session);
            Assert.AreNotSame(firstPresenter, controller.ChipperPresenter);
            Assert.AreEqual(0, controller.ChipperPresenter.ActiveEffectCount);
            Assert.AreEqual(0, controller.ChipperPresenter.ActiveFragmentCount);
            Assert.IsFalse(controller.IsResultPresentationVisible);
            Assert.AreEqual(GameState.Playing, controller.Outcome.State);
            Assert.AreEqual(5f, controller.Outcome.RemainingSeconds, 0.0001f);
            Assert.AreEqual(BlockLifecycle.OnBoard, controller.Level.Blocks[0].Lifecycle);
            Assert.IsFalse(controller.Level.Exits[0].IsBusy);
            Assert.IsTrue(controller.Level.FrameworkContext.CellOccupancySystem.IsOccupied(
                new GridCoordinate(2, 5)));
            Assert.IsNotNull(controller.DragAdapter);
            Assert.IsFalse(presenter.IsWinPanelVisible);
            Assert.IsFalse(presenter.IsLossPanelVisible);
        }

        [UnityTest]
        public IEnumerator AcceptedBlockClipsEmitsFragmentsAndEventuallyRevealsCommittedWin()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            ColorBlockEscapeCanvasPresenter presenter =
                Object.FindFirstObjectByType<ColorBlockEscapeCanvasPresenter>();
            Camera camera = Camera.main;
            int loadedCount = 0;
            List<GameState> outcomeChanges = new();
            List<bool> visibilityChanges = new();
            controller.LevelLoaded += () => loadedCount++;
            controller.OutcomeChanged += outcomeChanges.Add;
            controller.ResultVisibilityChanged += visibilityChanges.Add;
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            Assert.IsTrue(authored.SetTimer(5f, 1f).Success);
            Assert.IsTrue(authored.PlaceBlock("chipper-block", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("chipper-exit", ExitSide.Top,
                new GridCoordinate(2, 5), 1, ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.TryCreateLevel(out var definition, out string failure), failure);
            ColorBlockEscapeChipperSettings chipper = new(
                xySubdivision: 2,
                depthLayers: 1,
                fragmentGap: 0.02f,
                baseDuration: 0.08f,
                durationVariation: 0f,
                releaseStagger: 0f,
                staggerVariation: 0f,
                forwardTravelDistance: 0.3f,
                forwardVariation: 0f,
                sidewaysScatter: 0f,
                arcHeight: 0.1f,
                arcVariation: 0f,
                prewarmCount: 4,
                maximumFragmentCount: 4,
                postEffectDelay: 0.02f,
                maximumWaitTimeout: 1f);
            Assert.IsTrue(controller.TryStart(definition, camera,
                exitSettings: new ExitCaptureSettings(0.70f, 0.35f, 5f),
                showHud: false, chipperSettings: chipper), controller.StatusMessage);
            Assert.AreEqual(1, loadedCount);

            Vector2 grab = camera.WorldToScreenPoint(World(2.5f, 5.5f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            Vector2 outward = camera.WorldToScreenPoint(World(2.5f, 6.6f));
            controller.DragAdapter.ProcessPointerSample(0, outward, false, true, false);
            Assert.AreEqual(BlockLifecycle.Exiting, controller.Level.Blocks[0].Lifecycle);
            controller.ChipperPresenter.Advance(0f);
            Assert.IsTrue(controller.ChipperPresenter.IsBusy);
            Assert.IsFalse(presenter.IsWinPanelVisible,
                "A committed win must remain hidden while chipper presentation is active.");
            MeshRenderer renderer = controller.BlockViews["chipper-block"]
                .GetComponent<MeshRenderer>();
            Assert.AreEqual("ColorBlockEscape/ChipperClippedUnlit",
                renderer.sharedMaterial.shader.name);

            float timeout = Time.realtimeSinceStartup + 2f;
            while (!controller.IsResultPresentationVisible && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.AreEqual(GameState.Won, controller.Outcome.State,
                "Presentation must not own or reverse the accepted gameplay result.");
            CollectionAssert.Contains(outcomeChanges, GameState.Won);
            Assert.IsTrue(controller.IsResultPresentationVisible);
            CollectionAssert.Contains(visibilityChanges, true);
            Assert.IsFalse(controller.ChipperPresenter.IsBusy);
            Assert.IsTrue(presenter.IsWinPanelVisible);
            Assert.IsFalse(presenter.IsLossPanelVisible);
            Assert.IsFalse(presenter.IsNextLevelActionVisible,
                "The scene's only assigned level must not wrap to a next level.");
            Assert.AreEqual(0, controller.ChipperPresenter.ActiveFragmentCount);
            Assert.AreEqual(0, controller.ChipperPresenter.SkippedFragmentCount);
            Assert.AreEqual(BlockLifecycle.Removed, controller.Level.Blocks[0].Lifecycle);
        }

        [UnityTest]
        public IEnumerator PointerSuspensionBlocksDraggingKeepsTimerRunningAndSurvivesRestart()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            Camera camera = Camera.main;
            Assert.IsNotNull(controller);
            Assert.IsNotNull(camera);
            Assert.IsTrue(controller.Level.TryGetBlock("red-bar", out BlockRuntimeState block));
            Vector2 original = block.ContinuousOrigin;
            float remainingBefore = controller.RemainingSeconds;

            controller.LegacyGameplayGuiEnabled = false;
            controller.SetBoardPointerInputSuspended(true);
            Assert.IsTrue(controller.IsBoardPointerInputSuspended);
            Assert.IsTrue(controller.DragAdapter.IsPointerInputSuspended);
            yield return null;
            Assert.That(controller.RemainingSeconds, Is.LessThan(remainingBefore),
                "Suspending pointer input must not pause the timer.");

            Vector2 grab = camera.WorldToScreenPoint(World(2.5f, 4.5f));
            Vector2 moved = camera.WorldToScreenPoint(World(2.5f, 3.8f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            controller.DragAdapter.ProcessPointerSample(0, moved, false, true, false);
            controller.DragAdapter.ProcessPointerSample(0, moved, false, false, true);
            Assert.IsNull(controller.DragAdapter.ActiveBlockId);
            Assert.AreEqual(original, block.ContinuousOrigin);

            int loadedCount = 0;
            controller.LevelLoaded += () => loadedCount++;
            Assert.IsTrue(controller.RestartCurrentLevel(), controller.StatusMessage);
            Assert.AreEqual(1, loadedCount);
            Assert.IsTrue(controller.IsBoardPointerInputSuspended);
            Assert.IsTrue(controller.DragAdapter.IsPointerInputSuspended);
            Assert.IsFalse(controller.LegacyGameplayGuiEnabled,
                "Session replacement must not re-enable legacy IMGUI.");

            controller.SetBoardPointerInputSuspended(false);
            Assert.IsFalse(controller.IsBoardPointerInputSuspended);
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            Assert.AreEqual("red-bar", controller.DragAdapter.ActiveBlockId);
            controller.DragAdapter.ProcessPointerSample(0, grab, false, false, true);
        }

        [UnityTest]
        public IEnumerator CanvasChooserListsAssignedLevelsSelectsAndRestoresBoardInput()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            ColorBlockEscapeCanvasPresenter presenter =
                Object.FindFirstObjectByType<ColorBlockEscapeCanvasPresenter>();
            Assert.IsNotNull(controller);
            Assert.IsNotNull(presenter);

            LevelDefinition first = CreateSingleBlockLevel("First Level", 30f, 5f);
            LevelDefinition second = CreateSingleBlockLevel("Second Level", 30f, 5f);
            TextAsset firstAsset = new(JsonUtility.ToJson(first)) { name = "First" };
            TextAsset secondAsset = new(JsonUtility.ToJson(second)) { name = "Second" };
            FieldInfo levelAssets = typeof(ColorBlockEscapeRuntimeController).GetField(
                "_levelAssets", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(levelAssets);
            levelAssets.SetValue(controller, new[] { firstAsset, secondAsset });
            Assert.IsTrue(controller.TrySelectLevel(0), controller.StatusMessage);

            presenter.OpenLevelChooser();
            Assert.IsTrue(presenter.IsLevelChooserOpen);
            Assert.AreEqual(2, presenter.LevelButtonCount);
            Assert.IsTrue(controller.IsBoardPointerInputSuspended);
            Assert.IsTrue(controller.DragAdapter.IsPointerInputSuspended);

            Assert.IsTrue(presenter.TryChooseLevel(1));
            Assert.AreEqual(1, controller.SelectedLevelIndex);
            Assert.AreEqual("Second Level", presenter.DisplayedLevelLabel);
            Assert.IsFalse(presenter.IsLevelChooserOpen);
            Assert.IsFalse(controller.IsBoardPointerInputSuspended);
            Assert.IsFalse(controller.DragAdapter.IsPointerInputSuspended);

            presenter.OpenLevelChooser();
            Assert.IsTrue(presenter.TryChooseLevel(0));
            Camera camera = Camera.main;
            Vector2 grab = camera.WorldToScreenPoint(World(2.5f, 5.5f));
            Vector2 outward = camera.WorldToScreenPoint(World(2.5f, 6.6f));
            controller.DragAdapter.ProcessPointerSample(0, grab, true, true, false);
            controller.DragAdapter.ProcessPointerSample(0, outward, false, true, false);
            float timeout = Time.realtimeSinceStartup + 5f;
            while (!presenter.IsWinPanelVisible && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.IsTrue(presenter.IsWinPanelVisible);
            Assert.IsTrue(presenter.IsNextLevelActionVisible,
                "Next Level must be available when another assigned level exists.");
            Assert.IsTrue(presenter.TryNextLevel());
            Assert.AreEqual(1, controller.SelectedLevelIndex);
            Assert.AreEqual("Second Level", presenter.DisplayedLevelLabel);

            presenter.OpenLevelChooser();
            presenter.enabled = false;
            Assert.IsFalse(controller.IsBoardPointerInputSuspended,
                "Disabling the presenter must never leave board input suspended.");
            Object.Destroy(firstAsset);
            Object.Destroy(secondAsset);
        }

        [UnityTest]
        public IEnumerator CanvasWarningLossAndReplayFollowRuntimeState()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            ColorBlockEscapeRuntimeController controller =
                Object.FindFirstObjectByType<ColorBlockEscapeRuntimeController>();
            ColorBlockEscapeCanvasPresenter presenter =
                Object.FindFirstObjectByType<ColorBlockEscapeCanvasPresenter>();
            Assert.IsNotNull(controller);
            Assert.IsNotNull(presenter);
            LevelDefinition definition = CreateSingleBlockLevel("Warning Test", 0.08f, 0.08f);
            Assert.IsTrue(controller.TryStart(definition, Camera.main, showHud: false),
                controller.StatusMessage);
            yield return null;
            Assert.IsTrue(presenter.IsTimerWarningActive);

            float timeout = Time.realtimeSinceStartup + 2f;
            while (!presenter.IsLossPanelVisible && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.AreEqual(GameState.Lost, controller.CommittedOutcome);
            Assert.IsTrue(controller.IsResultPresentationVisible);
            Assert.IsTrue(presenter.IsLossPanelVisible);
            Assert.IsFalse(presenter.IsWinPanelVisible);
            Assert.IsTrue(presenter.ReplayCurrentLevel());
            Assert.AreEqual(GameState.Playing, controller.CommittedOutcome);
            Assert.IsFalse(presenter.IsLossPanelVisible);
            Assert.AreEqual("Warning Test", presenter.DisplayedLevelLabel);
        }

        private static LevelDefinition CreateSingleBlockLevel(string displayName,
            float duration, float warning)
        {
            ColorBlockEscapeAuthoringSession authored = new(6, 6);
            authored.Core.SetMetadata(displayName.ToLowerInvariant().Replace(' ', '-'),
                displayName, 1);
            Assert.IsTrue(authored.SetTimer(duration, warning).Success);
            Assert.IsTrue(authored.PlaceBlock("test-block", new GridCoordinate(2, 5),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.PutExit("test-exit", ExitSide.Top,
                new GridCoordinate(2, 5), 1, ColorIdentity.Slot0).Success);
            Assert.IsTrue(authored.TryCreateLevel(out LevelDefinition definition,
                out string failure), failure);
            return definition;
        }

        private static Vector3 World(float x, float y) =>
            ColorBlockEscapeBoardSpace.CreateLayout(Vector3.zero)
                .BoardLocalToWorld(new Vector2(x, y));
    }
}
