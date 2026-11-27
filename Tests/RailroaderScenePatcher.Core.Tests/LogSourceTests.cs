// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

// Every log write of the patcher goes through its one logging entry point, PatcherLog. Only the log outputs, which hand
// PatcherLog's lines to the game, write to the game's log, the Unity console or the game's message log.
static class LogSourceTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Root([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));
    // The files that hand log lines and Log window rows to the game.
    static readonly string[] Outputs = { "Src/RailroaderScenePatcher/LogOutputs.cs" };
    static readonly Regex DirectWrite = new Regex(@"\bDebug\s*\.\s*Log\w*\s*\(|\.Log\s*\.\s*(Info|Warning|Error|Debug)\s*\(|\bMessageLog\s*\.|\bIModLogger\b|\bConsole\s*\.\s*(Write|Error|Out)|\bTrace\s*\.\s*Write|\bFileLog\b|logMessageReceived", RegexOptions.CultureInvariant);
    static readonly string[] CompiledFolders = { "Src/RailroaderScenePatcher.Core", "Src/RailroaderScenePatcher", "Tests/RailroaderScenePatcher.Core.Tests", "Tests/RailroaderScenePatcher.Core.FaultTests" };
    static readonly Regex IssueConstant = new Regex(@"\bISSUE[0-9]+_[A-Z_]*", RegexOptions.CultureInvariant);

    internal static void Run() {
        var root = Root();
        string Relative(string file) => file.Substring(root.Length + 1).Replace('\\', '/');
        var sources = Directory.GetFiles(Path.Combine(root, "Src"), "*.cs", SearchOption.AllDirectories).Select(f => new { File = Relative(f), Text = File.ReadAllText(f) }).ToArray();
        Assert(sources.Length > 50 && Outputs.All(o => sources.Any(s => s.File == o)), "The source check reads the patcher's sources and finds its log outputs");
        var direct = sources.Where(s => !Outputs.Contains(s.File))
            .SelectMany(s => s.Text.Split('\n').Select((line, i) => new { s.File, Line = i + 1, Text = line }))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && DirectWrite.IsMatch(l.Text))
            .Select(l => l.File + ":" + l.Line + ": " + l.Text.Trim()).ToArray();
        Assert(direct.Length == 0, "No log write bypasses PatcherLog:\n" + String.Join("\n", direct));

        // The scan reads only what the core test projects compile and build with: the folders and the build file that
        // the dotnet-test part of .github/ci-config.yml lists, so a change elsewhere cannot change its result.
        var tracked = CompiledFolders.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
            .Concat(new[] { Path.Combine(root, "Directory.Build.props") })
            .Where(f => new[] { ".cs", ".csproj", ".props", ".targets", ".json" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Relative(f).Split('/').Any(p => p == "bin" || p == "obj"))
            .ToArray();
        Assert(CompiledFolders.All(d => tracked.Any(f => Relative(f).StartsWith(d + "/", StringComparison.Ordinal))), "The constant scan reads every compiled folder");
        var self = Relative(Path.Combine(root, "Tests", "RailroaderScenePatcher.Core.Tests", "LogSourceTests.cs"));
        var constants = tracked.Where(f => Relative(f) != self).SelectMany(f => IssueConstant.Matches(File.ReadAllText(f)).Cast<Match>().Select(m => Relative(f) + ": " + m.Value)).ToArray();
        Assert(constants.Length == 0, "No issue-named compile constant remains:\n" + String.Join("\n", constants));
        Console.WriteLine("PASS: every patcher log write goes through PatcherLog, and no issue-named compile constant remains");
    }
}
