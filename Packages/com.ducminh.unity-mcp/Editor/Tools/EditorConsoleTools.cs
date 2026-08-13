using System;
using System.Reflection;
using UnityEditor;

namespace DucMinh.UnityMcp.Editor
{
    /// <summary>Explicitly opt-in operations on the local Unity Console.</summary>
    public static class EditorConsoleTools
    {
        [UnityMcpTool("console-clear", Description = "Clear local Unity Console entries; dry-run unless apply is true.", Category = "console", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ChangeOutput ConsoleClear(EditorActionInput input, UnityMcpContext context)
        {
            var logEntries = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
            var clear = logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (clear == null) throw new NotSupportedException("This Unity Editor version does not expose its local Console clear operation.");
            if (!context.DryRun) clear.Invoke(null, null);
            return new ChangeOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                summary = "Clear local Unity Console entries.",
                rollbackSupported = false
            };
        }
    }
}
