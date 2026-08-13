using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    [Serializable] public sealed class BatchExecuteInput { public string allowlistPath; public List<BatchStepInput> steps = new List<BatchStepInput>(); public bool stopOnError = true; public bool apply; }
    [Serializable] public sealed class BatchStepInput { public string toolName; public string argumentsJson = "{}"; }
    [Serializable] public sealed class BatchStepOutput { public int index; public string toolName; public bool isError; public string message; public string errorCode; public string structuredContentJson; }
    [Serializable] public sealed class BatchExecuteOutput { public bool dryRun; public bool stoppedOnError; public int completedSteps; public List<BatchStepOutput> steps = new List<BatchStepOutput>(); }

    /// <summary>Bounded allowlisted composition of already-enabled Editor tools.</summary>
    public static class EditorBatchTools
    {
        private const int MaxSteps = 20;
        private const int MaxArgumentsCharacters = 65536;

        [UnityMcpTool("batch-execute", Description = "Execute a bounded ordered batch of locally allowlisted enabled Editor tools; dry-run unless apply is true.", Category = "automation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, SupportsCancellation = true, TimeoutMs = 600000)]
        public static async Task<UnityMcpResult<BatchExecuteOutput>> BatchExecute(BatchExecuteInput input, UnityMcpContext context)
        {
            var allowlist = LoadAllowlist(input.allowlistPath);
            var steps = input.steps ?? new List<BatchStepInput>();
            if (steps.Count == 0 || steps.Count > MaxSteps) throw new ArgumentException("steps must contain between 1 and " + MaxSteps + " entries.");
            var registry = UnityMcpEditorBootstrap.Registry ?? throw new InvalidOperationException("The UnityMCP Editor registry is still starting. Retry after it has loaded.");
            var permitted = new HashSet<string>((allowlist.allowedToolNames ?? new List<string>()).Where(IsToolName).Distinct(), StringComparer.Ordinal);
            var output = new BatchExecuteOutput { dryRun = context.DryRun };

            for (var index = 0; index < steps.Count; index++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var step = steps[index] ?? throw new ArgumentException("steps may not contain null entries.");
                if (!IsToolName(step.toolName) || !permitted.Contains(step.toolName))
                    throw new ArgumentException("Step " + index + " uses a tool that is not in the supplied local batch allowlist.");
                if (string.Equals(step.toolName, "batch-execute", StringComparison.Ordinal) || string.Equals(step.toolName, "execute-csharp", StringComparison.Ordinal))
                    throw new ArgumentException("Batch execution cannot invoke recursive or code-execution tools.");
                var arguments = ParseArguments(step.argumentsJson, context.DryRun);
                UnityMcpResult result;
                try { result = await registry.InvokeAsync(step.toolName, arguments, registry.RegistryRevision, context.CancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    result = UnityMcpResult.Error("A batch step could not be dispatched.", "batch_dispatch_failed");
                    Debug.LogWarning("UnityMCP batch step failed (" + exception.GetType().Name + "). Details were redacted.");
                }
                var message = result?.content?.FirstOrDefault(value => value != null && value.type == "text")?.text;
                output.steps.Add(new BatchStepOutput
                {
                    index = index,
                    toolName = step.toolName,
                    isError = result?.isError ?? true,
                    message = Clip(message, 2048),
                    errorCode = result?.meta != null && result.meta.TryGetValue("errorCode", out var code) ? Convert.ToString(code) : null,
                    structuredContentJson = SerializeStructured(result?.structuredContent)
                });
                output.completedSteps++;
                if (result?.isError == true && input.stopOnError)
                {
                    output.stoppedOnError = true;
                    break;
                }
            }
            return new UnityMcpResult<BatchExecuteOutput>
            {
                structuredContent = output,
                message = output.stoppedOnError ? "Batch stopped after an error." : "Batch completed."
            };
        }

        private static UnityMcpBatchAllowlist LoadAllowlist(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("..") || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("allowlistPath must be a project-relative Assets/*.asset path.");
            var asset = AssetDatabase.LoadAssetAtPath<UnityMcpBatchAllowlist>(path);
            if (asset == null) throw new ArgumentException("The supplied UnityMCP batch allowlist asset was not found or has the wrong type.");
            return asset;
        }

        private static JObject ParseArguments(string text, bool forceDryRun)
        {
            text = string.IsNullOrWhiteSpace(text) ? "{}" : text;
            if (text.Length > MaxArgumentsCharacters) throw new ArgumentException("A batch step argumentsJson exceeds the 64 KiB limit.");
            JObject arguments;
            try { arguments = JObject.Parse(text); }
            catch (JsonException) { throw new ArgumentException("Each batch step argumentsJson must be a JSON object."); }
            if (forceDryRun) arguments["apply"] = false;
            return arguments;
        }

        private static bool IsToolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return false;
            return name.All(character => char.IsLower(character) || char.IsDigit(character) || character == '-');
        }

        private static string SerializeStructured(object value)
        {
            if (value == null) return null;
            var json = JsonConvert.SerializeObject(value, Formatting.None);
            return json.Length <= 32768 ? json : json.Substring(0, 32767) + "…";
        }

        private static string Clip(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length) return value;
            return value.Substring(0, length - 1) + "…";
        }
    }
}
