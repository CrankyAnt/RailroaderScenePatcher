// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

// A native button or segment is a UI.Common.RailroaderButton. Its Awake reads the colour of the text it finds among
// active objects and throws when it finds none, which happens when its window is first shown. So the patcher never
// switches off a text object inside a native control: it empties the text instead.
static class NativeButtonSourceTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Root([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));
    // A line that switches off the object of a TextMesh Pro text: "label.gameObject.SetActive(false)" where the line
    // names TMP_Text, or a text or label variable.
    static readonly Regex SwitchedOffText = new Regex(@"(TMP_Text|\b\w*(label|text|Label|Text)\b)[^;]*\.gameObject\s*\.\s*SetActive\s*\(\s*false\s*\)", RegexOptions.CultureInvariant);

    internal static void Run() {
        var root = Root();
        var found = Directory.GetFiles(Path.Combine(root, "Src", "RailroaderScenePatcher"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => new { File = Path.GetFileName(f), Line = i + 1, Text = line }))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && SwitchedOffText.IsMatch(l.Text))
            .Select(l => l.File + ":" + l.Line + ": " + l.Text.Trim()).ToArray();
        Assert(found.Length == 0, "No text object inside a native control is switched off, so RailroaderButton.Awake finds its text:\n" + String.Join("\n", found));
        Assert(SwitchedOffText.IsMatch("foreach (var label in segment.GetComponentsInChildren<TMP_Text>(true)) label.gameObject.SetActive(false);"),
            "The check recognises the switched-off segment text that threw at the first Show");
        Assert(!SwitchedOffText.IsMatch("if (entry != null) { entry.SetActive(false); UnityEngine.Object.Destroy(entry); }"), "Switching off another object is not reported");
        Console.WriteLine("PASS: no text object inside a native control is switched off");
    }
}
