using System;
using System.IO;
using System.Text;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>
    /// Shared file-system safeguards and atomic writes for project-scoped client configurations.
    /// </summary>
    internal static class UnityMcpProjectConfigFile
    {
        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        internal static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new ArgumentException("The Unity project root is required.", nameof(projectRoot));

            var full = Path.GetFullPath(projectRoot);
            var root = Path.GetPathRoot(full);
            if (!string.Equals(full, root, PathComparison))
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full;
        }

        internal static void EnsureSafeProjectPath(string projectRoot, string targetPath)
        {
            var fullRoot = NormalizeProjectRoot(projectRoot);
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("The target file path is required.", nameof(targetPath));

            var fullTarget = Path.GetFullPath(targetPath);
            var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            if (!fullTarget.StartsWith(rootPrefix, PathComparison))
                throw new InvalidOperationException("Refusing to write outside the Unity project root: " + fullTarget);

            if (File.Exists(fullTarget) && IsReparsePoint(fullTarget))
                throw new InvalidOperationException("Refusing to write through a symbolic link inside the project: " + fullTarget);

            var cursor = Path.GetDirectoryName(fullTarget);
            while (!string.IsNullOrEmpty(cursor)
                && !string.Equals(cursor, fullRoot, PathComparison)
                && cursor.StartsWith(rootPrefix, PathComparison))
            {
                if (Directory.Exists(cursor) && IsReparsePoint(cursor))
                    throw new InvalidOperationException("Refusing to traverse a symbolic link directory inside the project: " + cursor);

                var parent = Path.GetDirectoryName(cursor);
                if (string.Equals(parent, cursor, PathComparison)) break;
                cursor = parent;
            }
        }

        internal static void WriteAtomically(string path, string content)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("The target file path is required.", nameof(path));

            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath) && IsReparsePoint(fullPath))
                throw new InvalidOperationException("Refusing to replace a symbolic link target: " + fullPath);

            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("The target file has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
    }
}
