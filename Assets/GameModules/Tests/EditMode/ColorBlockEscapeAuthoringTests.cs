using System;
using System.IO;
using System.Linq;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;
using UnityEditor;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeAuthoringTests
    {
        [Test]
        public void ImportedBoardPrefabUsesValidNonStencilMaterials()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/RuntimeAssets/Board/CellPrefab.prefab");
            Assert.IsNotNull(prefab);

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            foreach (Renderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, $"{renderer.name} has a missing material.");
                    Assert.IsNotNull(material.shader, $"{material.name} has no shader.");
                    Assert.AreNotEqual("Hidden/InternalErrorShader", material.shader.name,
                        $"{material.name} resolves to Unity's pink error shader.");
                    Assert.That(material.name, Does.Not.Contain("Stencil"));
                }
        }

        [Test]
        public void ImportedDtmBoardPrefabBuildsCbeBoardAndSuppressesExitWall()
        {
            ModularBoardCellView prefab = AssetDatabase.LoadAssetAtPath<ModularBoardCellView>(
                "Assets/RuntimeAssets/Board/CellPrefab.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsTrue(prefab.TryValidateConfiguration(out string configurationFailure),
                configurationFailure);
            ColorBlockEscapeAuthoringSession model = new(3, 3);
            Assert.IsTrue(model.PutExit("exit", ExitSide.Top, new GridCoordinate(1, 2),
                1, ColorIdentity.Slot0).Success);
            GridWorldLayout layout = new(Vector3.zero, Vector2.one,
                Vector3.right, Vector3.up, GridCellAnchor.Corner);
            GameObject root = new("CBE board asset test");
            using ColorBlockEscapeBlockMeshPresentation meshes = new();
            try
            {
                ColorBlockEscapeAuthoringView.Build(model, null, layout, root.transform, meshes,
                    prefab);
                Transform generated = root.transform.Find("Modular board visuals");
                Assert.IsNotNull(generated);
                Assert.AreEqual(9, generated.childCount);
                ModularBoardCellView exitCell = generated
                    .GetComponentsInChildren<ModularBoardCellView>(true)
                    .Single(cell => cell.Coordinate == new GridCoordinate(1, 2));
                Transform[] northWalls = exitCell.GetComponentsInChildren<Transform>(true)
                    .Where(child => child.name == "NorthWalls").ToArray();
                Assert.AreEqual(2, northWalls.Length);
                Assert.IsTrue(northWalls.All(wall => !wall.gameObject.activeSelf));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void EditorAndRuntimeBlocksShareUnifiedGeneratedMeshAndGameOwnedColor()
        {
            ColorBlockEscapeAuthoringSession model = ReadyModel();
            Assert.IsTrue(model.TryBeginPlayTest(out ColorBlockEscapeRuntimeLevel runtime,
                out string failure), failure);
            GridWorldLayout layout = new(Vector3.zero, Vector2.one,
                Vector3.right, Vector3.up, GridCellAnchor.Corner);
            GameObject editorRoot = new("CBE generated editor block test");
            GameObject runtimeRoot = new("CBE generated runtime block test");
            using ColorBlockEscapeBlockMeshPresentation meshes =
                new(new ColorBlockEscapeBlockVisualProfile(0.24f, 0.04f, 2));
            try
            {
                var editorViews = ColorBlockEscapeAuthoringView.Build(model, null, layout,
                    editorRoot.transform, meshes);
                var runtimeViews = ColorBlockEscapeAuthoringView.Build(model, runtime, layout,
                    runtimeRoot.transform, meshes);

                Transform editorBlock = editorViews["block"];
                Transform runtimeBlock = runtimeViews["block"];
                Mesh editorMesh = editorBlock.GetComponent<MeshFilter>().sharedMesh;
                Mesh runtimeMesh = runtimeBlock.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(editorMesh);
                Assert.AreSame(editorMesh, runtimeMesh);
                Assert.AreEqual(0, editorBlock.childCount);
                Assert.AreEqual(1, meshes.CachedMeshCount);
                Color expectedTint = ColorBlockEscapeAuthoringView.Tint(ColorIdentity.Slot0);
                Color actualTint = editorBlock.GetComponent<MeshRenderer>().sharedMaterial.color;
                Assert.That(actualTint.r, Is.EqualTo(expectedTint.r).Within(0.0001f));
                Assert.That(actualTint.g, Is.EqualTo(expectedTint.g).Within(0.0001f));
                Assert.That(actualTint.b, Is.EqualTo(expectedTint.b).Within(0.0001f));
                Assert.That(actualTint.a, Is.EqualTo(expectedTint.a).Within(0.0001f));
                Assert.That(editorMesh.bounds.size.x, Is.EqualTo(2f).Within(0.0001f));
                Assert.That(editorMesh.bounds.size.y, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(editorMesh.bounds.size.z, Is.EqualTo(0.24f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editorRoot);
                UnityEngine.Object.DestroyImmediate(runtimeRoot);
            }
        }

        [Test]
        public void BlockEditsUseLiveSessionAndFailuresLeaveOriginalFootprint()
        {
            ColorBlockEscapeAuthoringSession model = new(4, 4);
            Assert.IsTrue(model.PlaceBlock("block", new GridCoordinate(1, 1),
                BlockShapePresets.Get(BlockShapePreset.L), ColorIdentity.Slot0).Success);
            Assert.IsTrue(model.SetCell(new GridCoordinate(3, 1), AuthoredCellState.Blocked).Success);
            Assert.IsTrue(model.SelectBlockAt(new GridCoordinate(1, 2)));
            Assert.AreEqual("block", model.Core.SelectedItemId);
            Assert.IsTrue(model.RecolorSelectedBlock(ColorIdentity.Slot4).Success);
            Assert.IsTrue(model.TryGetBlockColor("block", out ColorIdentity color));
            Assert.AreEqual(ColorIdentity.Slot4, color);

            Assert.IsFalse(model.RotateSelectedBlock().Success);
            Assert.AreEqual(new GridCoordinate(1, 1), Find(model, "block").Origin);
            Assert.IsFalse(model.MoveSelectedBlock(new GridCoordinate(3, 3)).Success);
            Assert.AreEqual(new GridCoordinate(1, 1), Find(model, "block").Origin);
            Assert.IsTrue(model.MoveSelectedBlock(new GridCoordinate(2, 0)).Success);
            Assert.AreEqual(new GridCoordinate(2, 0), Find(model, "block").Origin);
            Assert.IsTrue(model.EraseBlockAt(new GridCoordinate(2, 1)));
            Assert.IsFalse(model.Core.TryGetItem("block", out _));
        }

        [Test]
        public void StructuralEditsRejectAffectedBlockExitAndNondefaultCroppedCell()
        {
            ColorBlockEscapeAuthoringSession model = new(4, 4);
            Assert.IsTrue(model.PlaceBlock("block", new GridCoordinate(1, 1),
                BlockShapePresets.Get(BlockShapePreset.Single), ColorIdentity.Slot0).Success);
            Assert.IsTrue(model.PutExit("exit", ExitSide.Top, new GridCoordinate(1, 3),
                2, ColorIdentity.Slot0).Success);
            Assert.IsTrue(model.SetCell(new GridCoordinate(3, 0), AuthoredCellState.Blocked).Success);

            AuthoringEditResult blockConflict = model.SetCell(new GridCoordinate(1, 1), AuthoredCellState.Inactive);
            Assert.IsFalse(blockConflict.Success);
            CollectionAssert.Contains(blockConflict.AffectedItemIds, "block");
            Assert.AreEqual(AuthoredCellState.Active, model.Core.GetCellState(new GridCoordinate(1, 1)));

            AuthoringEditResult exitConflict = model.SetCell(new GridCoordinate(1, 3), AuthoredCellState.Blocked);
            Assert.IsFalse(exitConflict.Success);
            CollectionAssert.Contains(exitConflict.AffectedItemIds, "exit");
            Assert.AreEqual(AuthoredCellState.Active, model.Core.GetCellState(new GridCoordinate(1, 3)));

            AuthoringEditResult resize = model.Resize(2, 3);
            Assert.IsFalse(resize.Success);
            CollectionAssert.Contains(resize.AffectedItemIds, "exit");
            CollectionAssert.Contains(resize.AffectedCells, new GridCoordinate(3, 0));
            Assert.AreEqual(4, model.Core.Width);
            Assert.AreEqual(4, model.Core.Height);
        }

        [Test]
        public void ExitsRequireRealExteriorAndWholeWidthClearance()
        {
            ColorBlockEscapeAuthoringSession model = new(5, 5);
            Assert.IsTrue(model.SetCell(new GridCoordinate(2, 2), AuthoredCellState.Inactive).Success);
            Assert.IsFalse(model.PutExit("hole", ExitSide.Top, new GridCoordinate(2, 1),
                1, ColorIdentity.Slot0).Success);
            Assert.AreEqual(0, model.Exits.Count);
            Assert.IsTrue(model.PutExit("outer", ExitSide.Top, new GridCoordinate(2, 4),
                2, ColorIdentity.Slot0).Success);
            Assert.IsFalse(model.PutExit("overlap", ExitSide.Top, new GridCoordinate(3, 4),
                1, ColorIdentity.Slot1).Success);
            Assert.IsFalse(model.PutExit("wide", ExitSide.Top, new GridCoordinate(4, 4),
                2, ColorIdentity.Slot1).Success);
            Assert.AreEqual(1, model.Exits.Count);
        }

        [Test]
        public void AuthoredRotationAndExitEditingSurvivePayloadRoundTrip()
        {
            ColorBlockEscapeAuthoringSession model = new(4, 4);
            Assert.IsTrue(model.PlaceBlock("block", new GridCoordinate(1, 1),
                BlockShapePresets.Get(BlockShapePreset.Bar2), ColorIdentity.Slot1).Success);
            Assert.IsTrue(model.RotateSelectedBlock().Success);
            CollectionAssert.Contains(Find(model, "block").Offsets, new GridCoordinate(0, -1));
            Assert.IsTrue(model.PutExit("exit", ExitSide.Top, new GridCoordinate(1, 3),
                1, ColorIdentity.Slot1).Success);
            Assert.IsNull(model.Core.SelectedItemId);
            Assert.AreEqual("exit", model.SelectedExitId);
            Assert.IsTrue(model.SelectBlockAt(new GridCoordinate(1, 1)));
            Assert.IsNull(model.SelectedExitId);
            Assert.IsTrue(model.PutExit("exit", ExitSide.Right, new GridCoordinate(3, 1),
                2, ColorIdentity.Slot5).Success);
            Assert.IsTrue(model.TryCreateLevel(out LevelDefinition definition, out string failure), failure);
            ColorBlockEscapeAuthoringSession restored = new();
            Assert.IsTrue(restored.TryLoad(definition, out string loadFailure), loadFailure);
            CollectionAssert.Contains(Find(restored, "block").Offsets, new GridCoordinate(0, -1));
            Assert.AreEqual(ExitSide.Right, restored.Exits[0].Side);
            Assert.AreEqual(2, restored.Exits[0].Width);
            Assert.AreEqual(ColorIdentity.Slot5, restored.Exits[0].Color);
        }

        [Test]
        public void SaveLoadAndPlayTestUseDetachedRuntimeWithoutChangingSelection()
        {
            ColorBlockEscapeAuthoringSession model = ReadyModel();
            Assert.IsTrue(model.SelectBlockAt(new GridCoordinate(1, 1)));
            string path = Path.Combine(Path.GetTempPath(), $"cbe-authoring-{Guid.NewGuid():N}.json");
            try
            {
                SaveResult saved = model.Save(path);
                Assert.IsTrue(saved.Success, saved.FailureReason);
                ColorBlockEscapeAuthoringSession loaded = new();
                Assert.IsTrue(loaded.TryLoad(path, out string loadFailure), loadFailure);
                Assert.AreEqual(5, loaded.Core.Width);
                Assert.AreEqual(AuthoredCellState.Blocked,
                    loaded.Core.GetCellState(new GridCoordinate(3, 2)));
                Assert.AreEqual(ColorIdentity.Slot2, loaded.Exits[0].Color);
                Assert.IsTrue(loaded.Core.TryGetItem("block", out AuthoredFootprint restored));
                Assert.AreEqual(2, restored.Offsets.Count);
                Assert.AreEqual(45f, loaded.Core.Timer.DurationSeconds);

                Assert.IsTrue(model.TryBeginPlayTest(out ColorBlockEscapeRuntimeLevel runtime,
                    out string buildFailure), buildFailure);
                Assert.AreEqual(1, runtime.Blocks.Count);
                Assert.AreEqual("block", model.Core.SelectedItemId);
                Assert.IsTrue(model.MoveSelectedBlock(new GridCoordinate(0, 1)).Success);
                Assert.AreEqual(new GridCoordinate(1, 1), runtime.Blocks[0].CommittedOrigin);
                Assert.AreEqual(new GridCoordinate(0, 1), Find(model, "block").Origin);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void InvalidImportLeavesLiveSessionAndSelectionUntouched()
        {
            ColorBlockEscapeAuthoringSession model = ReadyModel();
            model.SelectBlockAt(new GridCoordinate(1, 1));
            Assert.IsTrue(model.TryCreateLevel(out LevelDefinition definition, out string failure), failure);
            definition.FrameworkData.Board.Cells.RemoveAt(0);
            Assert.IsFalse(model.TryLoad(definition, out _));
            Assert.AreEqual("block", model.Core.SelectedItemId);
            Assert.AreEqual(5, model.Core.Width);
            Assert.AreEqual(1, model.Exits.Count);
        }

        [Test]
        public void CornerAnchoredPickingMatchesMovementFootprintSquares()
        {
            GridWorldLayout layout = new(Vector3.zero, Vector2.one,
                Vector3.right, Vector3.up, GridCellAnchor.Corner);
            Ray first = new(new Vector3(1.99f, 1.5f, -5f), Vector3.forward);
            Ray second = new(new Vector3(2.01f, 1.5f, -5f), Vector3.forward);
            Assert.IsTrue(BoardAuthoringPicker.TryPickCell(first, layout, Vector3.zero,
                out GridCoordinate left));
            Assert.IsTrue(BoardAuthoringPicker.TryPickCell(second, layout, Vector3.zero,
                out GridCoordinate right));
            Assert.AreEqual(new GridCoordinate(1, 1), left);
            Assert.AreEqual(new GridCoordinate(2, 1), right);
        }

        [Test]
        public void RegisteredToolsEditTheSameLiveCoreAndCustomShapeGuardRejectsInvalidOffsets()
        {
            ColorBlockEscapeAuthoringSession model = new(5, 5);
            ColorBlockEscapeAuthoringTools tools = new(model);
            LevelAuthoringCore original = model.Core;
            tools.Shape = BlockShapePreset.Bar2;
            tools.Color = ColorIdentity.Slot3;
            Assert.IsTrue(tools.Host.Apply(AuthoringTarget.ForCell(new GridCoordinate(1, 1))).Success);
            Assert.AreSame(original, tools.Host.Session);
            Assert.AreEqual(1, original.Items.Count);
            Assert.IsTrue(tools.SelectTool("Select / move"));
            Assert.IsTrue(tools.Host.Apply(AuthoringTarget.ForCell(new GridCoordinate(1, 1))).Success);
            Assert.IsTrue(tools.Host.Apply(AuthoringTarget.ForCell(new GridCoordinate(2, 2))).Success);
            Assert.AreEqual(new GridCoordinate(2, 2), Find(model, model.Core.SelectedItemId).Origin);

            tools.CustomOffsets = new[] { new GridCoordinate(0, 0), new GridCoordinate(2, 0) };
            Assert.IsFalse(model.PreviewBlock(new GridCoordinate(0, 0), tools.CustomOffsets).Success);
            tools.CustomOffsets = new[] { new GridCoordinate(0, 0), new GridCoordinate(1, 0),
                new GridCoordinate(2, 0), new GridCoordinate(3, 0), new GridCoordinate(4, 0) };
            Assert.IsFalse(model.PreviewBlock(new GridCoordinate(0, 0), tools.CustomOffsets).Success);
        }

        [Test]
        public void SharedBoundaryPickerFindsExteriorEdgeButCbeChecksItsMeaning()
        {
            ColorBlockEscapeAuthoringSession model = new(4, 4);
            GridWorldLayout layout = new(Vector3.zero, Vector2.one,
                Vector3.right, Vector3.up, GridCellAnchor.Corner);
            Ray ray = new(new Vector3(1.5f, 4.05f, -5f), Vector3.forward);
            Assert.IsTrue(BoardAuthoringPicker.TryPickBoundaryEdge(ray, layout, Vector3.zero,
                model.Core.CreateBoundary(state => state == AuthoredCellState.Active), 0.2f,
                out BoardBoundaryEdge edge));
            Assert.AreEqual(new GridCoordinate(1, 3), edge.CellCoordinate);
            ColorBlockEscapeAuthoringTools tools = new(model);
            Assert.IsTrue(tools.SelectTool("Place / edit exit"));
            Assert.IsTrue(tools.Host.Apply(AuthoringTarget.ForBoundaryEdge(edge)).Success);
            Assert.AreEqual(ExitSide.Top, model.Exits[0].Side);
        }

        private static ColorBlockEscapeAuthoringSession ReadyModel()
        {
            ColorBlockEscapeAuthoringSession model = new(5, 4);
            Assert.IsTrue(model.PlaceBlock("block", new GridCoordinate(1, 1),
                BlockShapePresets.Get(BlockShapePreset.Bar2), ColorIdentity.Slot0).Success);
            Assert.IsTrue(model.PutExit("exit", ExitSide.Top, new GridCoordinate(1, 3),
                2, ColorIdentity.Slot2).Success);
            Assert.IsTrue(model.SetCell(new GridCoordinate(3, 2), AuthoredCellState.Blocked).Success);
            Assert.IsTrue(model.SetTimer(45f, 8f).Success);
            return model;
        }

        private static AuthoredFootprint Find(ColorBlockEscapeAuthoringSession model, string id)
        {
            Assert.IsTrue(model.Core.TryGetItem(id, out AuthoredFootprint item));
            return item;
        }
    }
}
