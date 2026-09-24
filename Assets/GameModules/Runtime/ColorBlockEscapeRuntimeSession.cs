using System;
using PuzzleFramework.Content;

namespace ColorBlockEscape.Runtime
{
    /// <summary>
    /// One detached CBE gameplay session. Construction and outcomes are composed here while
    /// movement, exit capture, occupancy, timer, and result rules remain in their owning systems.
    /// </summary>
    public sealed class ColorBlockEscapeRuntimeSession
    {
        private ColorBlockEscapeRuntimeSession(ColorBlockEscapeRuntimeLevel level)
        {
            Level = level;
            Outcome = new ColorBlockEscapeOutcomeSession(level);
        }

        public ColorBlockEscapeRuntimeLevel Level { get; }
        public ColorBlockEscapeOutcomeSession Outcome { get; }

        public static bool TryCreate(LevelDefinition definition,
            out ColorBlockEscapeRuntimeSession session, out string failure)
        {
            session = null;
            if (definition == null)
            {
                failure = "A level definition is required.";
                return false;
            }

            ColorBlockEscapeRuntimeBuilder builder = new();
            if (!builder.TryBuild(definition, out ColorBlockEscapeRuntimeLevel level, out failure))
                return false;
            try
            {
                session = new ColorBlockEscapeRuntimeSession(level);
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"Color Block Escape runtime startup failed: {exception.Message}";
                return false;
            }
        }
    }
}
