using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using PuzzleFramework.RuntimeConstruction;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    public enum BlockLifecycle
    {
        OnBoard,
        Exiting,
        Removed
    }

    /// <summary>Game-owned identity, committed footprint and continuous pose for one block.</summary>
    public sealed class BlockRuntimeState
    {
        internal BlockRuntimeState(string id, ColorIdentity color, GridCoordinate origin,
            ShapeFootprint footprint)
        {
            Id = id;
            Color = color;
            CommittedOrigin = origin;
            ContinuousOrigin = new Vector2(origin.X, origin.Y);
            Footprint = footprint;
            RetainedCells = new HashSet<GridCoordinate>(footprint.ResolveCoordinates(origin));
        }

        public string Id { get; }
        public ColorIdentity Color { get; }
        public ShapeFootprint Footprint { get; }
        public GridCoordinate CommittedOrigin { get; internal set; }
        public Vector2 ContinuousOrigin { get; internal set; }
        public BlockLifecycle Lifecycle { get; internal set; } = BlockLifecycle.OnBoard;
        internal HashSet<GridCoordinate> RetainedCells { get; }
        internal string AcceptedExitId { get; set; }
        internal float OutwardTravelCells { get; set; }
    }

    /// <summary>One validated opening and its game-owned busy state.</summary>
    public sealed class ExitRuntimeState
    {
        internal ExitRuntimeState(ExitDefinition definition)
        {
            Id = definition.Id;
            Side = definition.Side;
            StartCell = new GridCoordinate(definition.StartCell.X, definition.StartCell.Y);
            Width = definition.Width;
            Color = definition.Color;
        }

        public string Id { get; }
        public ExitSide Side { get; }
        public GridCoordinate StartCell { get; }
        public int Width { get; }
        public ColorIdentity Color { get; }
        public bool IsBusy { get; internal set; }
    }

    /// <summary>Published only after shared and module construction validation both succeed.</summary>
    public sealed class ColorBlockEscapeRuntimeLevel
    {
        private readonly Dictionary<string, BlockRuntimeState> _blocksById;
        private readonly Dictionary<string, ExitRuntimeState> _exitsById;

        internal ColorBlockEscapeRuntimeLevel(RuntimeLevelContext frameworkContext,
            IReadOnlyList<BlockRuntimeState> blocks, IReadOnlyList<ExitRuntimeState> exits,
            float timerDurationSeconds, float timerWarningSeconds)
        {
            FrameworkContext = frameworkContext;
            Blocks = new ReadOnlyCollection<BlockRuntimeState>(new List<BlockRuntimeState>(blocks));
            Exits = new ReadOnlyCollection<ExitRuntimeState>(new List<ExitRuntimeState>(exits));
            _blocksById = new Dictionary<string, BlockRuntimeState>(StringComparer.Ordinal);
            _exitsById = new Dictionary<string, ExitRuntimeState>(StringComparer.Ordinal);
            foreach (BlockRuntimeState block in blocks) _blocksById.Add(block.Id, block);
            foreach (ExitRuntimeState exit in exits) _exitsById.Add(exit.Id, exit);
            TimerDurationSeconds = timerDurationSeconds;
            TimerWarningSeconds = timerWarningSeconds;
        }

        public RuntimeLevelContext FrameworkContext { get; }
        public IReadOnlyList<BlockRuntimeState> Blocks { get; }
        public IReadOnlyList<ExitRuntimeState> Exits { get; }
        public float TimerDurationSeconds { get; }
        public float TimerWarningSeconds { get; }
        public bool TryGetBlock(string id, out BlockRuntimeState block) =>
            _blocksById.TryGetValue(id, out block);
        public bool TryGetExit(string id, out ExitRuntimeState exit) =>
            _exitsById.TryGetValue(id, out exit);
    }
}
