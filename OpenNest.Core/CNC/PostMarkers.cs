using System;

namespace OpenNest.CNC
{
    internal static class PostMarkers
    {
        /// <summary>
        /// Refuses a nest whose marked programs a post cannot write: every marked part needs the
        /// post's <paramref name="hasCode"/>, and <paramref name="validate"/> rejects a malformed
        /// program with an <see cref="ArgumentException"/>.
        /// </summary>
        public static void EnsurePostable(Nest nest, bool hasCode, Func<Program, bool> isMarked,
            Action<Program> validate, Func<string, string> missingCode)
        {
            ArgumentNullException.ThrowIfNull(nest);
            foreach (var plate in nest.Plates)
            {
                foreach (var part in plate.Parts)
                {
                    if (part?.Program == null || !isMarked(part.Program))
                        continue;
                    var name = DisplayPerimeterTail.PartName(part);
                    if (!hasCode)
                        throw new InvalidOperationException(missingCode(name));
                    try
                    {
                        validate(part.Program);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException($"{name} cannot be posted: {ex.Message}", ex);
                    }
                }
            }
        }
    }
}
