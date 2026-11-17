// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // One problem a read found in a scene patch file: where it is, the text there, and what the format expects.
    public sealed class ScenePatchFileDiagnostic {
        public const string FormatReference = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/scene-patch-file-v1.md";
        public const string RecoveryReference = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/recover-scene-patch-files.md";
        // The most lines and characters a fragment shows.
        public const int FragmentLines = 8, FragmentCharacters = 1200;
        // The JSON path of the problem, such as modifications.place-board.placement; empty for the whole scene patch file.
        public string Path { get; private set; }
        // The first and last line of the problem in the text; zero when the text has no line for it, such as a missing field.
        public int StartLine { get; private set; }
        public int EndLine { get; private set; }
        public string Fragment { get; private set; }
        // Whether the fragment shows only the start of the problem's text; it then ends by saying so.
        public bool Truncated { get; private set; }
        public string Message { get; private set; }
        public string Expected { get; private set; }
        // The format reference that describes the expected structure and values.
        public string Reference => FormatReference;
        // The guide that describes the corrections scene patch editing offers for a protected scene patch file.
        public string Corrections => RecoveryReference;
        internal static ScenePatchFileDiagnostic Create(string text, string path, string message, string expected, JToken token = null, int line = 0) {
            var start = (token as IJsonLineInfo)?.HasLineInfo() == true ? ((IJsonLineInfo)token).LineNumber : line;
            var end = start;
            if (start > 0 && token is JContainer container)
                end = Math.Max(start, container.DescendantsAndSelf().Select(t => (t as IJsonLineInfo)?.LineNumber ?? 0).DefaultIfEmpty(start).Max());
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var span = Math.Max(1, end - start + 1);
            var fragment = start > 0 && start <= lines.Length
                ? String.Join("\n", lines.Skip(start - 1).Take(Math.Min(FragmentLines, span)))
                : "The text has no line for this problem.";
            // A problem longer than the fragment says that its text goes on.
            var cut = start > 0 && span > FragmentLines;
            if (cut) fragment += "\n... " + (span - FragmentLines) + " more " + (span - FragmentLines == 1 ? "line" : "lines") + ", to line " + end;
            // A scene patch file written on one long line still shows a bounded fragment.
            if (fragment.Length > FragmentCharacters) { fragment = fragment.Substring(0, FragmentCharacters) + "..."; cut = true; }
            return new ScenePatchFileDiagnostic { Path = path ?? "", StartLine = start, EndLine = end, Fragment = fragment, Message = message, Expected = expected, Truncated = cut };
        }
    }
}
