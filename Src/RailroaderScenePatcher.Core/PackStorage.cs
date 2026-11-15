// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Text;

namespace RailroaderScenePatcher.Core {
    // The file operations the active edited MapMod's storage uses. Tests substitute a failing operation.
    public interface IPackFiles {
        bool Exists(string path);
        byte[] Read(string path);
        void Write(string path, byte[] bytes);
        void Replace(string source, string destination);
        void Move(string source, string destination);
        void Delete(string path);
        void CreateDirectory(string path);
    }
    public class DiskFiles : IPackFiles {
        public virtual bool Exists(string path) => File.Exists(path);
        public virtual byte[] Read(string path) => File.ReadAllBytes(path);
        // The bytes reach the disk before the call returns, so the file that replaces another is complete after a power loss.
        public virtual void Write(string path, byte[] bytes) {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        public virtual void Replace(string source, string destination) { File.Replace(source, destination, null); }
        public virtual void Move(string source, string destination) { File.Move(source, destination); }
        public virtual void Delete(string path) { File.Delete(path); }
        public virtual void CreateDirectory(string path) { Directory.CreateDirectory(path); }
    }
    // The two files of the active edited MapMod that the patcher writes: Data/scene-patches.json and Info.json.
    public sealed class PackStorage : IStorage {
        public static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        readonly IPackFiles files;
        readonly Action<string> patchWritten;
        readonly Action<int> pause;
        // How often the new content tries to take the file's place before the write fails.
        public const int ReplaceAttempts = 5;
        // pause receives the milliseconds to wait before another attempt; tests pass one that also records it.
        public PackStorage(string root, IPackFiles files = null, Action<string> patchWritten = null, Action<int> pause = null) {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            this.files = files ?? new DiskFiles(); this.patchWritten = patchWritten;
            this.pause = pause ?? (milliseconds => System.Threading.Thread.Sleep(milliseconds));
        }
        public string Root { get; }
        public string PatchPath => Path.Combine(Root, "Data", "scene-patches.json");
        public string InfoPath => Path.Combine(Root, "Info.json");
        // UTF-8 text without its byte-order mark.
        public static string Decode(byte[] bytes) {
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return Utf8.GetString(bytes, offset, bytes.Length - offset);
        }
        public string ReadPatchDocument() => ReadText(PatchPath);
        public string ReadPackInfo() => ReadText(InfoPath);
        string ReadText(string path) => files.Exists(path) ? Decode(files.Read(path)) : null;
        public void WritePatchDocument(string text) {
            files.CreateDirectory(Path.GetDirectoryName(PatchPath));
            Publish(PatchPath, text);
            // The owner learns the exact text that is on disk now: its own write is no outside change of the document.
            try { patchWritten?.Invoke(text); } catch { }
        }
        public void WritePackInfo(string text) { Publish(InfoPath, text); }
        // The new content is written completely beside the file first and only then takes the file's place, so a write that
        // stops midway leaves the previous file, or no file, as it was. The beside file has one fixed name per file: a
        // leftover of an interrupted Save is replaced by the next attempt and removed after a failure, never published.
        void Publish(string path, string text) {
            var pending = path + ".saving";
            try {
                if (files.Exists(pending)) files.Delete(pending);
                files.Write(pending, Utf8.GetBytes(text));
                TakePlace(pending, path);
            } catch {
                try { if (files.Exists(pending)) files.Delete(pending); } catch { }
                throw;
            }
        }
        // Another process, such as a virus scanner, a sync client or the patcher's own periodic read of the document, can
        // hold the file open for a moment; Windows then refuses to replace it. An I/O failure is tried again after a short
        // pause, a bounded number of times; any other failure, such as a read-only file, fails at once.
        void TakePlace(string pending, string path) {
            for (var attempt = 1; ; attempt++) {
                try {
                    if (files.Exists(path)) files.Replace(pending, path); else files.Move(pending, path);
                    return;
                } catch (IOException) when (attempt < ReplaceAttempts) { pause(50 * attempt); }
            }
        }
    }
}
