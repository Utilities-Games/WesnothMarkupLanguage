using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WesnothMarkupLanguage
{
    /// <summary>Resolves includes beneath explicitly allowed roots.</summary>
    public sealed class FileSystemWmlSourceResolver : IWmlSourceResolver
    {
        private readonly string[] _roots;
        public FileSystemWmlSourceResolver(params string[] roots)
        {
            if (roots == null || roots.Length == 0) throw new ArgumentException("At least one root is required.", nameof(roots));
            _roots = roots.Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        }
        public bool RejectReparsePoints { get; set; } = true;
        public async Task<WmlSource?> ResolveAsync(string path, string? includingSource, CancellationToken cancellationToken)
        {
            string? resolved = ResolvePath(path, includingSource); if (resolved == null) return null; EnsureSafe(resolved);
            if (File.Exists(resolved)) return new WmlSource(resolved, await ReadAsync(resolved, cancellationToken).ConfigureAwait(false));
            if (!Directory.Exists(resolved)) return null;
            var files = SelectDirectoryFiles(resolved); var builder = new StringBuilder(); foreach (string file in files) { EnsureSafe(file); builder.Append(await ReadAsync(file, cancellationToken).ConfigureAwait(false)); }
            return new WmlSource(resolved, builder.ToString());
        }
        public Task<bool> ExistsAsync(string path, string? includingSource, CancellationToken cancellationToken)
        { string? resolved = ResolvePath(path, includingSource); return Task.FromResult(resolved != null && (File.Exists(resolved) || Directory.Exists(resolved))); }
        private string? ResolvePath(string path, string? includingSource)
        {
            var candidates = new List<string>(); if (Path.IsPathRooted(path)) candidates.Add(path); else { if (includingSource != null && Path.IsPathRooted(includingSource)) candidates.Add(Path.Combine(Path.GetDirectoryName(includingSource) ?? "", path)); foreach (var root in _roots) candidates.Add(Path.Combine(root, path)); }
            foreach (var candidate in candidates) { string full = Path.GetFullPath(candidate); if (_roots.Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.Equals(full + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))) return full; }
            throw new UnauthorizedAccessException($"Path '{path}' is outside the configured WML roots.");
        }
        private void EnsureSafe(string path)
        {
            if (!RejectReparsePoints) return; string current = Path.GetPathRoot(path) ?? ""; foreach (var part in path.Substring(current.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) { current = Path.Combine(current, part); if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException($"Reparse point '{current}' is not permitted."); }
        }
        private static IEnumerable<string> SelectDirectoryFiles(string directory)
        {
            string main = Path.Combine(directory, "_main.cfg"); if (File.Exists(main)) return new[] { main };
            string initial = Path.Combine(directory, "_initial.cfg"), final = Path.Combine(directory, "_final.cfg");
            var files = Directory.GetFiles(directory, "*.cfg", SearchOption.TopDirectoryOnly).Where(f => !string.Equals(f, initial, StringComparison.OrdinalIgnoreCase) && !string.Equals(f, final, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var sub in Directory.GetDirectories(directory)) { string subMain = Path.Combine(sub, "_main.cfg"); if (File.Exists(subMain)) files.Add(subMain); }
            files = files.OrderBy(f => f.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar), StringComparer.Ordinal).ToList(); if (File.Exists(initial)) files.Insert(0, initial); if (File.Exists(final)) files.Add(final); return files;
        }
        private static async Task<string> ReadAsync(string path, CancellationToken cancellationToken) { using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true)) using (var reader = new StreamReader(stream, new UTF8Encoding(false), true)) { cancellationToken.ThrowIfCancellationRequested(); return await reader.ReadToEndAsync().ConfigureAwait(false); } }
    }
}
