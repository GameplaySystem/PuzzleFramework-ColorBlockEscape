using System;
using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.Presentation;

namespace ColorBlockEscape.Runtime
{
    public enum ExitSide
    {
        Top = 0,
        Bottom = 1,
        Left = 2,
        Right = 3
    }

    /// <summary>Game-owned payload transported opaquely by the framework level envelope.</summary>
    [Serializable]
    public sealed class ColorBlockEscapeLevelData
    {
        public int Version;
        public List<BlockDefinition> Blocks;
        public List<ExitDefinition> Exits;
    }

    [Serializable]
    public sealed class BlockDefinition
    {
        public string Id;
        public ColorIdentity Color;
        public CellCoordinateData Origin;
        public List<CellCoordinateData> FootprintOffsets;
    }

    [Serializable]
    public sealed class ExitDefinition
    {
        public string Id;
        public ExitSide Side;
        public CellCoordinateData StartCell;
        public int Width;
        public ColorIdentity Color;
    }
}
