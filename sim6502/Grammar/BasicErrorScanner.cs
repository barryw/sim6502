using System;

namespace sim6502.Grammar
{
    /// <summary>
    /// Pure scanner that detects BASIC program-failure messages on a captured
    /// screen. Extracted from <see cref="SimBaseListener.CheckScreenForBasicErrors"/>
    /// so the matching rules can be unit-tested in isolation.
    ///
    /// Background: an earlier matcher used `EndsWith(" Error")` as a fallback
    /// for immediate-mode errors. That produced false positives on benign
    /// screen states — e.g. log text, banner artifacts, or incidental words
    /// ending in " Error" — and sent test authors chasing phantom bugs in
    /// the system under test. The scanner is now narrowed to the unambiguous
    /// "Error in line N" pattern that EhBASIC emits ONLY when RUN hits a
    /// runtime error. Unit tests below pin this behavior so the broader
    /// matcher cannot return without explicit intent.
    /// </summary>
    public static class BasicErrorScanner
    {
        /// <summary>
        /// Returns the trimmed offending screen line if the captured screen
        /// contains a BASIC program-failure message, or null otherwise. A
        /// program-failure message has the unambiguous form
        /// "&lt;Type&gt; Error in line N" — RUN-only output.
        /// </summary>
        public static string FindProgramError(string[] screen)
        {
            if (screen == null) return null;
            foreach (var line in screen)
            {
                if (line == null) continue;
                var trimmed = line.TrimEnd();
                if (trimmed.Contains(" Error in line ", StringComparison.Ordinal))
                {
                    return trimmed.Trim();
                }
            }
            return null;
        }
    }
}
