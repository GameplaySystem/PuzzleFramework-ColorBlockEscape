using ColorBlockEscape.Runtime.Authoring;
using PuzzleFramework.Content;
using UnityEngine;

namespace ColorBlockEscape.Runtime
{
    /// <summary>Validates CBE JSON and exposes its canonical Level N catalog identity.</summary>
    public sealed class ColorBlockEscapeLevelCatalogMetadataReader : ILevelCatalogMetadataReader
    {
        public bool TryReadMetadata(TextAsset levelAsset,
            out LevelCatalogMetadata metadata, out string failureReason)
        {
            metadata = default;
            if (levelAsset == null || string.IsNullOrWhiteSpace(levelAsset.text))
            {
                failureReason = "A non-empty Color Block Escape level asset is required.";
                return false;
            }

            LevelDefinition definition;
            try
            {
                definition = JsonUtility.FromJson<LevelDefinition>(levelAsset.text);
            }
            catch (System.Exception exception)
            {
                failureReason = $"Color Block Escape level JSON cannot be parsed: {exception.Message}";
                return false;
            }

            if (!ColorBlockEscapeRuntimeSession.TryCreate(definition, out _, out failureReason))
                return false;

            string levelId = definition.Metadata.LevelId?.Trim();
            if (!ColorBlockEscapeLevelNaming.TryParseCanonicalLabel(
                    levelId, out int sequenceNumber))
            {
                failureReason =
                    $"Color Block Escape catalog level id '{levelId}' must use the exact " +
                    "'Level N' format with a positive integer and no leading zeroes.";
                return false;
            }

            metadata = new LevelCatalogMetadata(levelId, sequenceNumber);
            failureReason = string.Empty;
            return true;
        }
    }
}
