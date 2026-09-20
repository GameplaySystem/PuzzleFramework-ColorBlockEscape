using System;
using System.IO;
using ColorBlockEscape.Runtime;
using ColorBlockEscape.Runtime.Authoring;
using NUnit.Framework;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Tests
{
    public sealed class ColorBlockEscapeAuthoringTests
    {
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
