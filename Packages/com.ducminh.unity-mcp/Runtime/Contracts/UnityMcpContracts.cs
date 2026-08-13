using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [Flags]
    public enum UnityMcpScope { None = 0, Editor = 1, Runtime = 2, All = Editor | Runtime }
    public enum UnityMcpSafety { SafeRead, Write, Destructive, Unsafe }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class UnityMcpToolAttribute : Attribute
    {
        public UnityMcpToolAttribute(string name) { Name = name; }
        public string Name { get; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public UnityMcpScope Scope { get; set; } = UnityMcpScope.Editor;
        public UnityMcpSafety Safety { get; set; } = UnityMcpSafety.SafeRead;
        public bool DefaultEnabled { get; set; }
        public bool SupportsDryRun { get; set; }
        public bool SupportsCancellation { get; set; }
        public bool ReturnsJob { get; set; }
        public bool MainThread { get; set; } = true;
        public int TimeoutMs { get; set; } = 30000;
        public Type SchemaProvider { get; set; }
        /// <summary>
        /// Optional assembly-qualified type that must be present before this tool is
        /// registered. It keeps integrations for optional Unity packages out of the
        /// live MCP catalog when that package is not installed in the target.
        /// </summary>
        public string RequiredType { get; set; }
    }

    public sealed class UnityMcpContext
    {
        internal UnityMcpContext(string toolName, bool dryRun, CancellationToken cancellationToken)
        { ToolName = toolName; DryRun = dryRun; CancellationToken = cancellationToken; }
        public string ToolName { get; }
        public bool DryRun { get; }
        public CancellationToken CancellationToken { get; }
    }

    [Serializable]
    public sealed class UnityMcpContent
    {
        public string type = "text";
        public string text;
        public string data;
        public string mimeType;
        public static UnityMcpContent Text(string value) => new UnityMcpContent { text = value ?? string.Empty };
    }

    [Serializable]
    public sealed class UnityMcpResult
    {
        public List<UnityMcpContent> content = new List<UnityMcpContent>();
        public object structuredContent;
        public bool isError;
        public Dictionary<string, object> meta;

        public static UnityMcpResult Success(object structured = null, string text = null) => new UnityMcpResult
        {
            structuredContent = structured,
            content = text == null ? new List<UnityMcpContent>() : new List<UnityMcpContent> { UnityMcpContent.Text(text) }
        };
        public static UnityMcpResult Error(string message, string code = null) => new UnityMcpResult
        {
            isError = true,
            content = new List<UnityMcpContent> { UnityMcpContent.Text(message) },
            meta = code == null ? null : new Dictionary<string, object> { ["errorCode"] = code }
        };
    }

    [Serializable]
    public sealed class UnityMcpResult<T>
    {
        public T structuredContent;
        public bool isError;
        public string message;
        public static implicit operator UnityMcpResult(UnityMcpResult<T> result) => result == null
            ? UnityMcpResult.Error("Tool returned null.")
            : result.isError ? UnityMcpResult.Error(result.message) : UnityMcpResult.Success(result.structuredContent, result.message);
    }

    [Serializable]
    public sealed class UnityMcpJobHandle
    {
        public string jobId;
        public string status;
    }

    [Serializable]
    public sealed class UnityMcpToolDescriptor
    {
        public string name;
        public string title;
        public string description;
        public string category;
        public string[] scopes;
        public string safety;
        public bool enabled;
        public bool defaultEnabled;
        public string source;
        public string schemaHash;
        public Dictionary<string, object> inputSchema;
        public Dictionary<string, object> outputSchema;
        public Dictionary<string, object> annotations;
        public bool mainThread;
        public bool supportsDryRun;
        public bool supportsCancel;
        public bool returnsJob;
        public int timeoutMs;
        public int schemaRevision = 1;
    }

    [Serializable]
    public sealed class UnityMcpInstanceDescriptor
    {
        public int port;
        public string token;
        public int pid;
        public string projectId;
        public string instanceId;
        public string kind;
        public string buildId;
    }
}
