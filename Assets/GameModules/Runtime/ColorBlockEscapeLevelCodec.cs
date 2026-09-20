using System;
using System.Collections.Generic;
using PuzzleFramework.Content;
using PuzzleFramework.CoreBoard;
using PuzzleFramework.Presentation;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>Validates the game payload schema without interpreting board construction or solvability.</summary>
    public static class ColorBlockEscapeLevelCodec
    {
        public const string ContentTypeId = "color-block-escape.v1";
        public const int CurrentVersion = 1;

        public static bool TryDecode(LevelDefinition definition,
            out ColorBlockEscapeLevelData payload, out string failure)
        {
            payload = null;
            if (definition?.ContentPayload == null ||
                definition.ContentPayload.ContentTypeId != ContentTypeId)
            {
                failure = $"Expected content type '{ContentTypeId}'.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(definition.ContentPayload.PayloadJson))
            {
                failure = "Color Block Escape payload JSON is empty.";
                return false;
            }
            try
            {
                payload = JsonUtility.FromJson<ColorBlockEscapeLevelData>(
                    definition.ContentPayload.PayloadJson);
            }
            catch (Exception exception)
            {
                failure = $"Color Block Escape payload JSON cannot be parsed: {exception.Message}";
                return false;
            }
            if (!TryValidateSchema(payload, out failure))
            {
                payload = null;
                return false;
            }
            return true;
        }

        public static bool TryEncode(ColorBlockEscapeLevelData payload,
            out string json, out string failure)
        {
            json = string.Empty;
            if (!TryValidateSchema(payload, out failure)) return false;
            json = JsonUtility.ToJson(payload, true);
            return true;
        }

        public static bool TryValidateSchema(ColorBlockEscapeLevelData payload, out string failure)
        {
            if (payload == null) return Fail("Payload is missing.", out failure);
            if (payload.Version != CurrentVersion)
                return Fail($"Unsupported payload version {payload.Version}.", out failure);
            if (payload.Blocks == null || payload.Exits == null)
                return Fail("Block and exit lists are required.", out failure);
            if (payload.Blocks.Count == 0 || payload.Exits.Count == 0)
                return Fail("A level needs at least one block and one exit.", out failure);

            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (BlockDefinition block in payload.Blocks)
            {
                if (block == null || string.IsNullOrWhiteSpace(block.Id))
                    return Fail("A block has a missing ID.", out failure);
                if (!ids.Add(block.Id)) return Fail($"Duplicate item ID '{block.Id}'.", out failure);
                if (!IsPlayableColor(block.Color))
                    return Fail($"Block '{block.Id}' has an invalid color.", out failure);
                if (block.Origin == null || block.FootprintOffsets == null ||
                    block.FootprintOffsets.Count == 0)
                    return Fail($"Block '{block.Id}' needs an origin and nonempty offsets.", out failure);

                HashSet<GridCoordinate> offsets = new();
                foreach (CellCoordinateData offset in block.FootprintOffsets)
                {
                    if (offset == null)
                        return Fail($"Block '{block.Id}' has a missing offset.", out failure);
                    GridCoordinate coordinate = new(offset.X, offset.Y);
                    if (!offsets.Add(coordinate))
                        return Fail($"Block '{block.Id}' repeats offset {coordinate}.", out failure);
                }
                if (!IsConnected(offsets))
                    return Fail($"Block '{block.Id}' footprint is disconnected.", out failure);
            }

            foreach (ExitDefinition exit in payload.Exits)
            {
                if (exit == null || string.IsNullOrWhiteSpace(exit.Id))
                    return Fail("An exit has a missing ID.", out failure);
                if (!ids.Add(exit.Id)) return Fail($"Duplicate item ID '{exit.Id}'.", out failure);
                if (!Enum.IsDefined(typeof(ExitSide), exit.Side) || exit.StartCell == null ||
                    exit.Width <= 0 || !IsPlayableColor(exit.Color))
                    return Fail($"Exit '{exit.Id}' has invalid side, start, width, or color.", out failure);
            }

            failure = string.Empty;
            return true;
        }

        private static bool IsPlayableColor(ColorIdentity color) =>
            color >= ColorIdentity.Slot0 && color <= ColorIdentity.Slot9;

        private static bool IsConnected(HashSet<GridCoordinate> offsets)
        {
            Queue<GridCoordinate> queue = new();
            HashSet<GridCoordinate> visited = new();
            foreach (GridCoordinate first in offsets)
            {
                queue.Enqueue(first);
                visited.Add(first);
                break;
            }
            while (queue.Count > 0)
            {
                GridCoordinate current = queue.Dequeue();
                GridCoordinate[] neighbors =
                {
                    current.Offset(1, 0), current.Offset(-1, 0),
                    current.Offset(0, 1), current.Offset(0, -1)
                };
                foreach (GridCoordinate neighbor in neighbors)
                    if (offsets.Contains(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
            }
            return visited.Count == offsets.Count;
        }

        private static bool Fail(string reason, out string failure)
        {
            failure = reason;
            return false;
        }
    }
}
