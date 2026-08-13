using System;
using System.Collections.Generic;
using System.IO;

namespace DucMinh.UnityMcp.Editor
{
    internal enum UnityMcpInstallShell
    {
        PowerShell,
        Posix
    }

    /// <summary>
    /// Non-secret, ready-to-copy instructions for installing the local Python gateway.
    /// Building this value never starts a process, downloads code, or changes the file system.
    /// </summary>
    internal sealed class UnityMcpGatewayInstallGuide
    {
        internal const string RepositoryUrl = "https://github.com/ducminh1307/UnityMCP.git";

        internal string SourcePath { get; private set; }
        internal string VirtualEnvironmentPath { get; private set; }
        internal string ExecutablePath { get; private set; }
        internal string Commands { get; private set; }
        internal bool IncludesClone { get; private set; }

        internal static UnityMcpGatewayInstallGuide CreateDefault()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if UNITY_EDITOR_WIN
            const UnityMcpInstallShell shell = UnityMcpInstallShell.PowerShell;
#else
            const UnityMcpInstallShell shell = UnityMcpInstallShell.Posix;
#endif
            var sourcePath = Combine(shell, localAppData, "UnityMCP", "source");
            return Create(localAppData, shell, Directory.Exists(sourcePath));
        }

        internal static UnityMcpGatewayInstallGuide Create(string localAppData, UnityMcpInstallShell shell, bool sourceExists)
        {
            if (string.IsNullOrWhiteSpace(localAppData)) throw new ArgumentException("Local application-data path is required.", nameof(localAppData));

            var sourcePath = Combine(shell, localAppData, "UnityMCP", "source");
            var virtualEnvironmentPath = Combine(shell, localAppData, "UnityMCP", "venv");
            var executablePath = shell == UnityMcpInstallShell.PowerShell
                ? Combine(shell, virtualEnvironmentPath, "Scripts", "unity-mcp.exe")
                : Combine(shell, virtualEnvironmentPath, "bin", "unity-mcp");
            var pythonPath = shell == UnityMcpInstallShell.PowerShell
                ? Combine(shell, virtualEnvironmentPath, "Scripts", "python.exe")
                : Combine(shell, virtualEnvironmentPath, "bin", "python");
            var serverPath = Combine(shell, sourcePath, "server");

            var commands = new List<string>();
            if (!sourceExists)
            {
                commands.Add("git clone --depth 1 --branch main --single-branch "
                    + Quote(RepositoryUrl, shell) + " " + Quote(sourcePath, shell));
            }
            commands.Add(shell == UnityMcpInstallShell.PowerShell
                ? "py -3 -m venv " + Quote(virtualEnvironmentPath, shell)
                : "python3 -m venv " + Quote(virtualEnvironmentPath, shell));
            commands.Add((shell == UnityMcpInstallShell.PowerShell ? "& " : string.Empty)
                + Quote(pythonPath, shell) + " -m pip install -e " + Quote(serverPath, shell));

            return new UnityMcpGatewayInstallGuide
            {
                SourcePath = sourcePath,
                VirtualEnvironmentPath = virtualEnvironmentPath,
                ExecutablePath = executablePath,
                Commands = string.Join("\n", commands),
                IncludesClone = !sourceExists
            };
        }

        private static string Combine(UnityMcpInstallShell shell, string root, params string[] segments)
        {
            var separator = shell == UnityMcpInstallShell.PowerShell ? '\\' : '/';
            var normalized = root.Trim().Replace('\\', separator).Replace('/', separator).TrimEnd(separator);
            foreach (var segment in segments)
                normalized += separator + segment.Trim().Trim(separator);
            return normalized;
        }

        private static string Quote(string value, UnityMcpInstallShell shell)
        {
            return shell == UnityMcpInstallShell.PowerShell
                ? "'" + value.Replace("'", "''") + "'"
                : "'" + value.Replace("'", "'\"'\"'") + "'";
        }
    }
}
