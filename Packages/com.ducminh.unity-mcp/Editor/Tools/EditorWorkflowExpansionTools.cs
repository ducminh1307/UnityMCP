using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    // Script tools ------------------------------------------------------------
    [Serializable] public sealed class ScriptSearchInput { public string query; public List<string> folders = new List<string>(); public int limit = 100; }
    [Serializable] public sealed class ScriptSearchMatch { public string path; public int line; public int column; public string preview; }
    [Serializable] public sealed class ScriptSearchOutput { public List<ScriptSearchMatch> matches = new List<ScriptSearchMatch>(); public int scannedFiles; public bool truncated; }
    [Serializable] public sealed class ScriptReadInput { public string path; public int startLine = 1; public int lineCount = 200; }
    [Serializable] public sealed class ScriptLine { public int line; public string text; }
    [Serializable] public sealed class ScriptReadOutput { public string path; public string revision; public int totalLines; public int startLine; public List<ScriptLine> lines = new List<ScriptLine>(); public bool truncated; }
    [Serializable] public sealed class ScriptCreateInput { public string path; public string content; public bool apply; }
    [Serializable] public sealed class ScriptDeleteInput { public string path; public bool apply; }
    [Serializable] public sealed class ScriptTextEdit { public int startOffset; public int endOffset; public string newText; }
    [Serializable] public sealed class ScriptApplyTextEditsInput { public string path; public string expectedRevision; public List<ScriptTextEdit> edits = new List<ScriptTextEdit>(); public bool apply; }
    [Serializable] public sealed class ScriptWriteOutput { public bool dryRun; public bool changed; public string path; public string revisionBefore; public string revisionAfter; public bool rollbackSupported; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }
    [Serializable] public sealed class ScriptValidateInput { public string path; }
    [Serializable] public sealed class ScriptDiagnostic { public string severity; public string code; public string message; public int line; public int column; }
    [Serializable] public sealed class ScriptValidationOutput { public string path; public string revision; public bool valid; public string validator; public List<ScriptDiagnostic> diagnostics = new List<ScriptDiagnostic>(); public bool truncated; }

    // Compilation and Console tools ------------------------------------------
    [Serializable] public sealed class CompileRequestInput { public bool apply; }
    [Serializable] public sealed class WorkflowJobStartOutput { public bool dryRun; public bool accepted; public string jobId; public string status; public string summary; }
    [Serializable] public sealed class CompileRequestResult { public bool requested; public bool compilationObserved; public bool isCompiling; public string note; }
    [Serializable] public sealed class ConsoleAnalyzeInput { public int limit = 500; public string severity; public string contains; }
    [Serializable] public sealed class ConsoleAnalysisGroup { public string signature; public string severity; public int count; public string example; }
    [Serializable] public sealed class ConsoleAnalysisOutput { public int total; public int errors; public int warnings; public int logs; public bool truncated; public List<ConsoleAnalysisGroup> groups = new List<ConsoleAnalysisGroup>(); }

    // Test discovery is intentionally dependency-free.  It identifies compiled
    // NUnit/Unity test attributes but does not depend on the optional Test Framework.
    [Serializable] public sealed class TestListInput { public string query; public string mode = "all"; public List<string> assemblyNames = new List<string>(); public List<string> categories = new List<string>(); public List<string> excludeCategories = new List<string>(); public string @explicit = "exclude"; public int limit = 200; public string cursor; }
    [Serializable] public sealed class TestListItem { public string id; public string fullName; public string assembly; public string mode; public bool unityTest; public List<string> categories = new List<string>(); public bool @explicit; public int? timeoutMs; public string sourceFile; public int? sourceLine; }
    [Serializable] public sealed class TestListOutput { public List<TestListItem> tests = new List<TestListItem>(); public int total; public bool truncated; public string nextCursor; public string note; }

    // Package tools -----------------------------------------------------------
    [Serializable] public sealed class PackageSearchInput { public string query; public bool offlineMode; public int limit = 100; }
    [Serializable] public sealed class PackageChangeInput { public string packageId; public bool apply; }
    [Serializable] public sealed class PackageResolveInput { public bool apply; }
    [Serializable] public sealed class WorkflowPackageInfo { public string name; public string displayName; public string version; public string source; public string resolvedPath; }
    [Serializable] public sealed class PackageSearchResult { public string query; public bool offlineMode; public List<WorkflowPackageInfo> packages = new List<WorkflowPackageInfo>(); public bool truncated; }
    [Serializable] public sealed class PackageChangeResult { public string operation; public string packageId; public string note; }

    // Build tools -------------------------------------------------------------
    [Serializable] public sealed class BuildSceneSetting { public string path; public bool enabled = true; }
    [Serializable] public sealed class BuildSettingsOutput { public string activeBuildTarget; public string selectedBuildTargetGroup; public bool development; public bool allowDebugging; public bool connectProfiler; public List<BuildSceneSetting> scenes = new List<BuildSceneSetting>(); }
    [Serializable] public sealed class BuildSettingsSetInput { public List<BuildSceneSetting> scenes; public bool? development; public bool? allowDebugging; public bool? connectProfiler; public bool apply; }
    [Serializable] public sealed class BuildSettingsChangeOutput { public bool dryRun; public bool changed; public string summary; public bool rollbackSupported; public List<ChangeJournalEntry> journal = new List<ChangeJournalEntry>(); }
    [Serializable] public sealed class BuildTargetSwitchInput { public string targetGroup; public string target; public bool apply; }
    [Serializable] public sealed class BuildPlayerInput { public string outputPath; public string targetGroup; public string target; public List<string> scenes; public bool development = true; public bool allowDebugging; public bool connectProfiler; public bool apply; }
    [Serializable] public sealed class BuildPlayerJobResult { public string outputPath; public string target; public string targetGroup; public string result; public int totalErrors; public int totalWarnings; public ulong totalSize; public double totalTimeSeconds; }
    [Serializable] public sealed class BuildTargetSwitchJobResult { public string target; public string targetGroup; public bool switched; }
    [Serializable] public sealed class BuildJobGetInput { public string jobId; }
    [Serializable] public sealed class BuildJobGetOutput { public string jobId; public string jobType; public string status; public float progress; public string progressMessage; public string createdUtc; public string startedUtc; public string completedUtc; public long durationMilliseconds; public string resultJson; public string error; }

    /// <summary>
    /// Editor-only workflow tools.  Every mutation remains opt-in through the
    /// standard UnityMCP enablement profile and is dry-run by default.
    /// </summary>
    public static class EditorWorkflowExpansionTools
    {
        private const int MaxScriptBytes = 1024 * 1024;
        private const int MaxScriptEditBytes = 256 * 1024;
        private const int MaxScriptSearchFiles = 10000;
        private const int MaxTextEdits = 128;
        private static readonly Regex PackageIdentifier = new Regex("^[a-z0-9][a-z0-9._-]*(?:@[0-9A-Za-z.+_-]+)?$", RegexOptions.Compiled);
        private static readonly Regex PackageName = new Regex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.Compiled);
        private static readonly Regex ConsoleNumber = new Regex(@"\b\d+\b", RegexOptions.Compiled);

        [UnityMcpTool("script-search", Description = "Search C# source text under contained Assets folders.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ScriptSearchOutput ScriptSearch(ScriptSearchInput input)
        {
            if (string.IsNullOrWhiteSpace(input.query)) throw new ArgumentException("query is required.");
            if (input.query.Length > 256) throw new ArgumentException("query is limited to 256 characters.");
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            var folders = input.folders == null || input.folders.Count == 0 ? new[] { "Assets" } : input.folders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var output = new ScriptSearchOutput();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                var fullFolder = RequireAssetDirectory(folder);
                foreach (var fullPath in EnumerateContainedCSharpFiles(fullFolder))
                {
                    if (!visited.Add(fullPath)) continue;
                    output.scannedFiles++;
                    if (output.scannedFiles > MaxScriptSearchFiles) { output.truncated = true; return output; }
                    var source = ReadSource(fullPath);
                    var line = 1;
                    var lineStart = 0;
                    while (lineStart <= source.text.Length)
                    {
                        var lineEnd = source.text.IndexOf('\n', lineStart);
                        if (lineEnd < 0) lineEnd = source.text.Length;
                        var current = source.text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
                        var index = current.IndexOf(input.query, StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            if (output.matches.Count >= limit) { output.truncated = true; return output; }
                            output.matches.Add(new ScriptSearchMatch { path = ToProjectPath(fullPath), line = line, column = index + 1, preview = Clip(current, 240) });
                        }
                        if (lineEnd == source.text.Length) break;
                        lineStart = lineEnd + 1;
                        line++;
                    }
                }
            }
            return output;
        }

        [UnityMcpTool("script-read", Description = "Read a bounded line range from a contained C# script.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ScriptReadOutput ScriptRead(ScriptReadInput input)
        {
            var fullPath = RequireScript(input.path, true);
            var source = ReadSource(fullPath);
            var startLine = Math.Max(1, input.startLine);
            var count = Math.Max(1, Math.Min(input.lineCount, 500));
            var allLines = SplitLines(source.text);
            var output = new ScriptReadOutput { path = ToProjectPath(fullPath), revision = Revision(source.bytes), totalLines = allLines.Count, startLine = startLine };
            for (var index = startLine - 1; index < allLines.Count && output.lines.Count < count; index++)
                output.lines.Add(new ScriptLine { line = index + 1, text = allLines[index] });
            output.truncated = startLine - 1 + output.lines.Count < allLines.Count;
            return output;
        }

        [UnityMcpTool("script-create", Description = "Create a new contained C# script; dry-run unless apply is true.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ScriptWriteOutput ScriptCreate(ScriptCreateInput input, UnityMcpContext context)
        {
            var fullPath = RequireScript(input.path, false);
            if (File.Exists(fullPath)) throw new InvalidOperationException("A script already exists at this path.");
            var parent = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(parent)) throw new ArgumentException("The target script folder must already exist under Assets.");
            EnsureNoReparsePoint(AssetsRoot, parent);
            var content = input.content ?? string.Empty;
            ValidateNewScriptContent(content);
            var after = EncodeUtf8(content, false);
            if (!context.DryRun)
            {
                File.WriteAllBytes(fullPath, after);
                AssetDatabase.ImportAsset(ToProjectPath(fullPath), ImportAssetOptions.ForceSynchronousImport);
            }
            return ScriptChange(context, ToProjectPath(fullPath), null, Revision(after), "create-script", null, ToProjectPath(fullPath));
        }

        [UnityMcpTool("script-delete", Description = "Delete a contained C# script and its Unity metadata; dry-run unless apply is true.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true)]
        public static ScriptWriteOutput ScriptDelete(ScriptDeleteInput input, UnityMcpContext context)
        {
            var fullPath = RequireScript(input.path, true);
            var source = ReadSource(fullPath);
            var assetPath = ToProjectPath(fullPath);
            if (!context.DryRun && !AssetDatabase.DeleteAsset(assetPath)) throw new InvalidOperationException("Unity could not delete the script asset.");
            return ScriptChange(context, assetPath, Revision(source.bytes), null, "delete-script", assetPath, null);
        }

        [UnityMcpTool("script-apply-text-edits", Description = "Apply revision-checked text edits to a contained C# script; dry-run unless apply is true.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static ScriptWriteOutput ScriptApplyTextEdits(ScriptApplyTextEditsInput input, UnityMcpContext context)
        {
            var fullPath = RequireScript(input.path, true);
            var source = ReadSource(fullPath);
            var beforeRevision = Revision(source.bytes);
            if (string.IsNullOrWhiteSpace(input.expectedRevision) || !string.Equals(input.expectedRevision, beforeRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("expectedRevision does not match the current script contents.");
            var edits = input.edits ?? new List<ScriptTextEdit>();
            if (edits.Count == 0) throw new ArgumentException("At least one text edit is required.");
            if (edits.Count > MaxTextEdits) throw new ArgumentException("Too many text edits.");
            ValidateTextEdits(edits, source.text.Length);
            var builder = new StringBuilder(source.text);
            foreach (var edit in edits.OrderByDescending(value => value.startOffset).ThenByDescending(value => value.endOffset))
                builder.Remove(edit.startOffset, edit.endOffset - edit.startOffset).Insert(edit.startOffset, edit.newText);
            var afterText = builder.ToString();
            ValidateNewScriptContent(afterText);
            var afterBytes = EncodeUtf8(afterText, source.hasBom);
            var changed = !source.bytes.SequenceEqual(afterBytes);
            if (!context.DryRun && changed)
            {
                File.WriteAllBytes(fullPath, afterBytes);
                AssetDatabase.ImportAsset(ToProjectPath(fullPath), ImportAssetOptions.ForceSynchronousImport);
            }
            var output = ScriptChange(context, ToProjectPath(fullPath), beforeRevision, Revision(afterBytes), "edit-script", ToProjectPath(fullPath), ToProjectPath(fullPath));
            output.changed = changed && !context.DryRun;
            return output;
        }

        [UnityMcpTool("script-validate", Description = "Perform bounded structural validation of a contained C# source file without compiling.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ScriptValidationOutput ScriptValidate(ScriptValidateInput input)
        {
            var fullPath = RequireScript(input.path, true);
            var source = ReadSource(fullPath);
            var output = new ScriptValidationOutput { path = ToProjectPath(fullPath), revision = Revision(source.bytes), validator = "utf8-and-delimiter-structure" };
            ValidateCSharpStructure(source.text, output);
            output.valid = output.diagnostics.All(value => !string.Equals(value.severity, "error", StringComparison.Ordinal));
            return output;
        }

        [UnityMcpTool("compile-request", Description = "Request Unity script compilation and return a local asynchronous job; dry-run unless apply is true.", Category = "scripts-compilation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static WorkflowJobStartOutput CompileRequest(CompileRequestInput input, UnityMcpContext context)
        {
            if (context.DryRun) return DryRunJob("Request Unity script compilation.");
            var job = EditorWorkflowJobRunner.Start(new CompileOperation(), "compile");
            return AcceptedJob(job, "Script compilation request queued.");
        }

        [UnityMcpTool("console-analyze", Description = "Group bounded Unity Console entries by normalized message signature.", Category = "console-tests", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static ConsoleAnalysisOutput ConsoleAnalyze(ConsoleAnalyzeInput input)
        {
            var read = ConsoleReflection.Read(new ConsoleReadInput { limit = Math.Max(1, Math.Min(input.limit, 1000)), severity = input.severity, contains = input.contains });
            var output = new ConsoleAnalysisOutput { total = read.entries.Count, truncated = read.truncated };
            foreach (var entry in read.entries)
            {
                if (string.Equals(entry.severity, "error", StringComparison.OrdinalIgnoreCase)) output.errors++;
                else if (string.Equals(entry.severity, "warning", StringComparison.OrdinalIgnoreCase)) output.warnings++;
                else output.logs++;
            }
            output.groups = read.entries
                .GroupBy(entry => new { severity = entry.severity ?? "log", signature = NormalizeConsoleSignature(entry.message) })
                .Select(group => new ConsoleAnalysisGroup { severity = group.Key.severity, signature = group.Key.signature, count = group.Count(), example = Clip(group.First().message, 240) })
                .OrderByDescending(group => group.count).ThenBy(group => group.severity, StringComparer.Ordinal).ThenBy(group => group.signature, StringComparer.Ordinal)
                .Take(200).ToList();
            return output;
        }

        [UnityMcpTool("test-list", Description = "List compiled methods marked with NUnit or Unity Test Framework test attributes without requiring test-runner APIs.", Category = "console-tests", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static TestListOutput TestList(TestListInput input)
        {
            input = input ?? new TestListInput();
            var mode = EditorTestCatalog.NormalizeMode(input.mode, true);
            var query = (input.query ?? string.Empty).Trim();
            var limit = Math.Max(1, Math.Min(input.limit, 1000));
            var assemblies = (input.assemblyNames ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            var categories = (input.categories ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            var excluded = (input.excludeCategories ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            var explicitMode = (input.@explicit ?? "exclude").Trim().ToLowerInvariant();
            if (explicitMode != "include" && explicitMode != "exclude" && explicitMode != "only") throw new UnityMcpValidationException("TEST_FILTER_REQUIRED", "explicit must be include, exclude, or only.");
            var all = EditorTestCatalog.Discover(mode).Where(test =>
                (assemblies.Count == 0 || assemblies.Contains(test.assembly, StringComparer.OrdinalIgnoreCase)) &&
                (categories.Count == 0 || test.categories.Any(category => categories.Contains(category, StringComparer.OrdinalIgnoreCase))) &&
                !test.categories.Any(category => excluded.Contains(category, StringComparer.OrdinalIgnoreCase)) &&
                (explicitMode != "only" || test.explicitTest) &&
                (explicitMode != "exclude" || !test.explicitTest) &&
                (query.Length == 0 || test.fullName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || test.assembly.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || test.categories.Any(category => category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
            var offset = 0;
            if (!string.IsNullOrWhiteSpace(input.cursor) && (!int.TryParse(input.cursor, out offset) || offset < 0 || offset > all.Count)) throw new UnityMcpValidationException("TEST_FILTER_REQUIRED", "cursor is invalid.");
            var page = all.Skip(offset).Take(limit).ToList();
            var output = new TestListOutput { total = all.Count, truncated = offset + page.Count < all.Count, nextCursor = offset + page.Count < all.Count ? (offset + page.Count).ToString() : null, note = "Discovery is attribute-based; source locations are unavailable for reflected methods." };
            output.tests = page.Select(test => new TestListItem { id = test.id, fullName = test.fullName, assembly = test.assembly, mode = test.mode, unityTest = test.unityTest, categories = test.categories, @explicit = test.explicitTest, timeoutMs = test.timeoutMs }).ToList();
            return output;
        }

        [UnityMcpTool("package-search", Description = "Search the Unity Package Manager registry as a serialized asynchronous job.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, ReturnsJob = true, TimeoutMs = 120000)]
        public static WorkflowJobStartOutput PackageSearch(PackageSearchInput input)
        {
            if (string.IsNullOrWhiteSpace(input.query)) throw new ArgumentException("query is required; broad registry searches are intentionally not exposed.");
            if (input.query.Length > 256) throw new ArgumentException("query is limited to 256 characters.");
            var request = new DeferredPackageRequestOperation(() => Client.Search(input.query.Trim(), input.offlineMode), requestValue =>
            {
                var search = (SearchRequest)requestValue;
                var result = new PackageSearchResult { query = input.query.Trim(), offlineMode = input.offlineMode };
                var limit = Math.Max(1, Math.Min(input.limit, 500));
                foreach (var package in search.Result.OrderBy(value => value.name, StringComparer.Ordinal).Take(limit)) result.packages.Add(PackageInfoOf(package));
                result.truncated = search.Result.Length > limit;
                return result;
            }, "package-search");
            var job = EditorWorkflowJobRunner.Start(request);
            return AcceptedJob(job, "Package search queued. Unity Package Manager operations are serialized.");
        }

        [UnityMcpTool("package-add", Description = "Add an explicit registry package identifier; dry-run unless apply is true.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static WorkflowJobStartOutput PackageAdd(PackageChangeInput input, UnityMcpContext context)
        {
            var packageId = ValidateRegistryPackageIdentifier(input.packageId, true);
            if (context.DryRun) return DryRunJob("Add package '" + packageId + "'.");
            var job = EditorWorkflowJobRunner.Start(new DeferredPackageRequestOperation(() => Client.Add(packageId), request => new PackageChangeResult { operation = "add", packageId = packageId, note = "Package Manager completed the add request." }, "package-add"));
            return AcceptedJob(job, "Package add queued. Only registry name[@version] identifiers are accepted.");
        }

        [UnityMcpTool("package-remove", Description = "Remove an explicit project package; dry-run unless apply is true.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Destructive, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static WorkflowJobStartOutput PackageRemove(PackageChangeInput input, UnityMcpContext context)
        {
            var packageName = ValidateRegistryPackageIdentifier(input.packageId, false);
            if (context.DryRun) return DryRunJob("Remove package '" + packageName + "'.");
            var job = EditorWorkflowJobRunner.Start(new DeferredPackageRequestOperation(() => Client.Remove(packageName), request => new PackageChangeResult { operation = "remove", packageId = packageName, note = "Package Manager completed the remove request." }, "package-remove"));
            return AcceptedJob(job, "Package removal queued.");
        }

        [UnityMcpTool("package-resolve", Description = "Ask Unity Package Manager to resolve project packages; dry-run unless apply is true.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static WorkflowJobStartOutput PackageResolve(PackageResolveInput input, UnityMcpContext context)
        {
            if (context.DryRun) return DryRunJob("Resolve project package dependencies.");
            var job = EditorWorkflowJobRunner.Start(new PackageResolveOperation());
            return AcceptedJob(job, "Package resolve request queued. Unity exposes this request without a completion handle.");
        }

        [UnityMcpTool("build-settings-get", Description = "Read supported Editor build settings and enabled build scenes.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static BuildSettingsOutput BuildSettingsGet(EmptyInput input)
        {
            var output = new BuildSettingsOutput
            {
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                selectedBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                development = EditorUserBuildSettings.development,
                allowDebugging = EditorUserBuildSettings.allowDebugging,
                connectProfiler = EditorUserBuildSettings.connectProfiler
            };
            foreach (var scene in EditorBuildSettings.scenes.OrderBy(value => value.path, StringComparer.Ordinal)) output.scenes.Add(new BuildSceneSetting { path = scene.path, enabled = scene.enabled });
            return output;
        }

        [UnityMcpTool("build-settings-set", Description = "Update supported build settings and contained scene entries; dry-run unless apply is true.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static BuildSettingsChangeOutput BuildSettingsSet(BuildSettingsSetInput input, UnityMcpContext context)
        {
            var normalizedScenes = input.scenes == null ? null : ValidateBuildScenes(input.scenes);
            if (input.development == null && input.allowDebugging == null && input.connectProfiler == null && normalizedScenes == null)
                throw new ArgumentException("Supply at least one supported build setting.");
            if (!context.DryRun)
            {
                if (normalizedScenes != null) EditorBuildSettings.scenes = normalizedScenes.Select(value => new EditorBuildSettingsScene(value.path, value.enabled)).ToArray();
                if (input.development.HasValue) EditorUserBuildSettings.development = input.development.Value;
                if (input.allowDebugging.HasValue) EditorUserBuildSettings.allowDebugging = input.allowDebugging.Value;
                if (input.connectProfiler.HasValue) EditorUserBuildSettings.connectProfiler = input.connectProfiler.Value;
            }
            var output = new BuildSettingsChangeOutput { dryRun = context.DryRun, changed = !context.DryRun, summary = "Update supported Editor build settings.", rollbackSupported = false };
            if (normalizedScenes != null) output.journal.Add(new ChangeJournalEntry { operation = "set-build-scenes", before = "EditorBuildSettings.scenes", after = normalizedScenes.Count.ToString() + " scene entries" });
            if (input.development.HasValue) output.journal.Add(new ChangeJournalEntry { operation = "set-development", before = null, after = input.development.Value.ToString() });
            if (input.allowDebugging.HasValue) output.journal.Add(new ChangeJournalEntry { operation = "set-allow-debugging", before = null, after = input.allowDebugging.Value.ToString() });
            if (input.connectProfiler.HasValue) output.journal.Add(new ChangeJournalEntry { operation = "set-connect-profiler", before = null, after = input.connectProfiler.Value.ToString() });
            return output;
        }

        [UnityMcpTool("build-target-switch", Description = "Switch the active supported build target as a local asynchronous job; dry-run unless apply is true.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static WorkflowJobStartOutput BuildTargetSwitch(BuildTargetSwitchInput input, UnityMcpContext context)
        {
            var group = ParseBuildTargetGroup(input.targetGroup);
            var target = ParseBuildTarget(input.target);
            ValidateTargetSupported(group, target);
            if (context.DryRun) return DryRunJob("Switch active build target to " + target + " (" + group + ").");
            if (EditorUserBuildSettings.activeBuildTarget == target) return new WorkflowJobStartOutput { accepted = true, status = "succeeded", summary = "The requested build target is already active." };
            var job = EditorWorkflowJobRunner.Start(new BuildTargetSwitchOperation(group, target));
            BuildJobTracker.Register(job.jobId);
            return AcceptedJob(job, "Build target switch queued. Unity may reload the domain while switching targets.");
        }

        [UnityMcpTool("build-player", Description = "Build the active target to a contained Builds/ output path as a local job; dry-run unless apply is true.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Unsafe, SupportsDryRun = true, ReturnsJob = true, TimeoutMs = 600000)]
        public static WorkflowJobStartOutput BuildPlayer(BuildPlayerInput input, UnityMcpContext context)
        {
            var group = ParseBuildTargetGroup(input.targetGroup);
            var target = ParseBuildTarget(input.target);
            ValidateTargetSupported(group, target);
            if (target != EditorUserBuildSettings.activeBuildTarget) throw new InvalidOperationException("The requested target is not active. Use build-target-switch first and wait for Unity to finish reloading.");
            var outputPath = RequireBuildOutputPath(input.outputPath);
            var scenes = input.scenes == null || input.scenes.Count == 0
                ? EditorBuildSettings.scenes.Where(value => value.enabled).Select(value => value.path).ToList()
                : ValidateBuildScenePaths(input.scenes);
            if (scenes.Count == 0) throw new InvalidOperationException("At least one enabled build scene is required.");
            if (context.DryRun) return DryRunJob("Build " + target + " to " + ToProjectRelativePath(outputPath) + " using " + scenes.Count + " scene(s).");
            var job = EditorWorkflowJobRunner.Start(new BuildPlayerOperation(group, target, outputPath, scenes, input.development, input.allowDebugging, input.connectProfiler), "build-player");
            BuildJobTracker.Register(job.jobId);
            return AcceptedJob(job, "Player build queued. The Editor will remain busy during BuildPipeline.BuildPlayer.");
        }

        [UnityMcpTool("build-job-get", Description = "Read a job created by build-target-switch or build-player.", Category = "packages-build", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static BuildJobGetOutput BuildJobGet(BuildJobGetInput input)
        {
            if (string.IsNullOrWhiteSpace(input.jobId) || !BuildJobTracker.Contains(input.jobId)) throw new ArgumentException("Unknown build job.");
            UnityMcpJob job;
            if (!UnityMcpJobStore.Shared.TryGet(input.jobId, out job)) throw new ArgumentException("Build job is no longer available in this Unity domain.");
            return new BuildJobGetOutput { jobId = job.jobId, jobType = job.jobType, status = job.status, progress = job.progress, progressMessage = job.progressMessage, createdUtc = job.createdUtc, startedUtc = job.startedUtc, completedUtc = job.completedUtc, durationMilliseconds = job.durationMilliseconds, resultJson = job.result == null ? null : JsonConvert.SerializeObject(job.result), error = job.error };
        }

        private static WorkflowJobStartOutput DryRunJob(string summary) => new WorkflowJobStartOutput { dryRun = true, accepted = false, status = "dry-run", summary = summary };
        private static WorkflowJobStartOutput AcceptedJob(UnityMcpJobHandle job, string summary) => new WorkflowJobStartOutput { accepted = true, jobId = job.jobId, status = job.status, summary = summary };

        private static ScriptWriteOutput ScriptChange(UnityMcpContext context, string path, string before, string after, string operation, string journalBefore, string journalAfter)
        {
            return new ScriptWriteOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                path = path,
                revisionBefore = before,
                revisionAfter = after,
                rollbackSupported = false,
                journal = new List<ChangeJournalEntry> { new ChangeJournalEntry { operation = operation, before = journalBefore, after = journalAfter } }
            };
        }

        private static string RequireScript(string assetPath, bool mustExist)
        {
            var fullPath = RequireAssetPath(assetPath, ".cs");
            if (mustExist && !File.Exists(fullPath)) throw new ArgumentException("Script was not found: " + assetPath);
            if (mustExist) EnsureNoReparsePoint(AssetsRoot, fullPath);
            return fullPath;
        }

        private static string RequireAssetDirectory(string assetPath)
        {
            var normalized = NormalizeAssetPath(assetPath, null);
            var fullPath = ToFullProjectPath(normalized);
            if (!Directory.Exists(fullPath)) throw new ArgumentException("Assets folder was not found: " + assetPath);
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            return fullPath;
        }

        private static string RequireAssetPath(string assetPath, string extension) => ToFullProjectPath(NormalizeAssetPath(assetPath, extension));

        private static string NormalizeAssetPath(string assetPath, string extension)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) throw new ArgumentException("path is required.");
            var normalized = assetPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized) || normalized.Contains(":")) throw new ArgumentException("path must be relative to Assets.");
            var parts = normalized.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal) || parts.Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("path must be a normalized path under Assets and may not contain traversal segments.");
            if (extension != null && !normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("path must end with " + extension + ".");
            return normalized;
        }

        private static string ToFullProjectPath(string projectPath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(ProjectRoot, projectPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsContained(ProjectRoot, fullPath)) throw new ArgumentException("path must remain within this Unity project.");
            return fullPath;
        }

        private static string AssetsRoot => Path.GetFullPath(Application.dataPath);
        private static string ProjectRoot => Directory.GetParent(AssetsRoot).FullName;

        private static IEnumerable<string> EnumerateContainedCSharpFiles(string root)
        {
            var results = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                string[] directories;
                string[] files;
                try { directories = Directory.GetDirectories(current); files = Directory.GetFiles(current, "*.cs"); }
                catch { continue; }
                foreach (var directory in directories) pending.Push(directory);
                foreach (var file in files)
                {
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && IsContained(AssetsRoot, file)) results.Add(file);
                    }
                    catch { }
                }
            }
            return results;
        }

        private static void EnsureNoReparsePoint(string root, string fullPath)
        {
            if (!IsContained(root, fullPath)) throw new ArgumentException("path is not contained by the allowed root.");
            var relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = root;
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The allowed root cannot be a symbolic link or junction.");
            foreach (var part in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current)) break;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Symbolic links and junctions are not permitted in contained paths.");
            }
        }

        private static bool IsContained(string root, string path)
        {
            var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var canonicalPath = Path.GetFullPath(path);
            return string.Equals(canonicalRoot, canonicalPath, StringComparison.OrdinalIgnoreCase) || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProjectPath(string fullPath)
        {
            if (!IsContained(ProjectRoot, fullPath)) throw new ArgumentException("Path is outside the Unity project.");
            return fullPath.Substring(ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
        }

        internal static string ToProjectRelativePath(string fullPath) => ToProjectPath(fullPath);

        private static SourceFile ReadSource(string fullPath)
        {
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length > MaxScriptBytes) throw new ArgumentException("Script exceeds the 1 MiB tool limit.");
            var offset = 0;
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (hasBom) offset = 3;
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                throw new ArgumentException("Only UTF-8 C# source files are supported by this tool.");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException) { throw new ArgumentException("Script is not valid UTF-8."); }
            return new SourceFile { bytes = bytes, text = text, hasBom = hasBom };
        }

        private static byte[] EncodeUtf8(string text, bool includeBom)
        {
            var body = new UTF8Encoding(false, true).GetBytes(text);
            if (!includeBom) return body;
            var bom = Encoding.UTF8.GetPreamble();
            var all = new byte[bom.Length + body.Length];
            Buffer.BlockCopy(bom, 0, all, 0, bom.Length);
            Buffer.BlockCopy(body, 0, all, bom.Length, body.Length);
            return all;
        }

        private static string Revision(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void ValidateNewScriptContent(string content)
        {
            if (content.IndexOf('\0') >= 0) throw new ArgumentException("Script content may not contain NUL characters.");
            if (new UTF8Encoding(false).GetByteCount(content) > MaxScriptBytes) throw new ArgumentException("Script content exceeds the 1 MiB tool limit.");
        }

        private static void ValidateTextEdits(List<ScriptTextEdit> edits, int length)
        {
            ScriptTextEdit previous = null;
            var replacementBytes = 0;
            foreach (var edit in edits.OrderBy(value => value.startOffset).ThenBy(value => value.endOffset))
            {
                if (edit == null || edit.startOffset < 0 || edit.endOffset < edit.startOffset || edit.endOffset > length) throw new ArgumentException("Text edit offsets are invalid.");
                if (edit.newText == null) throw new ArgumentException("newText may not be null.");
                var editBytes = new UTF8Encoding(false).GetByteCount(edit.newText);
                if (editBytes > MaxScriptEditBytes) throw new ArgumentException("One text edit exceeds the 256 KiB limit.");
                replacementBytes += editBytes;
                if (replacementBytes > MaxScriptBytes) throw new ArgumentException("The combined replacement text exceeds the 1 MiB script limit.");
                if (previous != null && (edit.startOffset < previous.endOffset || (edit.startOffset == previous.startOffset && edit.endOffset == previous.endOffset && edit.startOffset == edit.endOffset)))
                    throw new ArgumentException("Text edits may not overlap or insert at the same offset.");
                previous = edit;
            }
        }

        private static List<string> SplitLines(string value)
        {
            var lines = new List<string>();
            var start = 0;
            while (start <= value.Length)
            {
                var end = value.IndexOf('\n', start);
                if (end < 0) end = value.Length;
                lines.Add(value.Substring(start, end - start).TrimEnd('\r'));
                if (end == value.Length) break;
                start = end + 1;
            }
            return lines;
        }

        private static void ValidateCSharpStructure(string text, ScriptValidationOutput output)
        {
            var stack = new Stack<ScriptDelimiter>();
            var line = 1;
            var column = 1;
            var inLineComment = false;
            var inBlockComment = false;
            var inString = false;
            var inVerbatimString = false;
            var inCharacter = false;
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                var next = index + 1 < text.Length ? text[index + 1] : '\0';
                if (character == '\0' || (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t')) AddDiagnostic(output, "error", "invalid-character", "Source contains an unsupported control character.", line, column);
                if (inLineComment)
                {
                    if (character == '\n') inLineComment = false;
                    Advance(character, ref line, ref column);
                    continue;
                }
                if (inBlockComment)
                {
                    if (character == '*' && next == '/') { index++; column += 2; inBlockComment = false; continue; }
                    Advance(character, ref line, ref column);
                    continue;
                }
                if (inString)
                {
                    if (inVerbatimString && character == '"' && next == '"') { index++; column += 2; continue; }
                    if (character == '"' && (inVerbatimString || !IsEscaped(text, index))) inString = false;
                    Advance(character, ref line, ref column);
                    continue;
                }
                if (inCharacter)
                {
                    if (character == '\'' && !IsEscaped(text, index)) inCharacter = false;
                    if (character == '\n') AddDiagnostic(output, "error", "unterminated-character", "Character literal crosses a line boundary.", line, column);
                    Advance(character, ref line, ref column);
                    continue;
                }
                if (character == '/' && next == '/') { index++; column += 2; inLineComment = true; continue; }
                if (character == '/' && next == '*') { index++; column += 2; inBlockComment = true; continue; }
                if (character == '@' && next == '"') { index++; column += 2; inString = true; inVerbatimString = true; continue; }
                if (character == '"') { inString = true; inVerbatimString = false; Advance(character, ref line, ref column); continue; }
                if (character == '\'') { inCharacter = true; Advance(character, ref line, ref column); continue; }
                if (character == '{' || character == '(' || character == '[') stack.Push(new ScriptDelimiter { character = character, line = line, column = column });
                else if (character == '}' || character == ')' || character == ']')
                {
                    if (stack.Count == 0 || !Matches(stack.Peek().character, character)) AddDiagnostic(output, "error", "unmatched-delimiter", "Unmatched '" + character + "'.", line, column);
                    else stack.Pop();
                }
                Advance(character, ref line, ref column);
            }
            if (inBlockComment) AddDiagnostic(output, "error", "unterminated-comment", "Block comment is not closed.", line, column);
            if (inString) AddDiagnostic(output, "error", "unterminated-string", "String literal is not closed.", line, column);
            if (inCharacter) AddDiagnostic(output, "error", "unterminated-character", "Character literal is not closed.", line, column);
            while (stack.Count > 0)
            {
                var delimiter = stack.Pop();
                AddDiagnostic(output, "error", "unclosed-delimiter", "Unclosed '" + delimiter.character + "'.", delimiter.line, delimiter.column);
            }
        }

        private static void AddDiagnostic(ScriptValidationOutput output, string severity, string code, string message, int line, int column)
        {
            if (output.diagnostics.Count >= 100) { output.truncated = true; return; }
            output.diagnostics.Add(new ScriptDiagnostic { severity = severity, code = code, message = message, line = line, column = column });
        }

        private static bool IsEscaped(string value, int index)
        {
            var slashes = 0;
            for (var position = index - 1; position >= 0 && value[position] == '\\'; position--) slashes++;
            return slashes % 2 != 0;
        }

        private static bool Matches(char opening, char closing) => (opening == '{' && closing == '}') || (opening == '(' && closing == ')') || (opening == '[' && closing == ']');
        private static void Advance(char character, ref int line, ref int column) { if (character == '\n') { line++; column = 1; } else column++; }
        private static string Clip(string value, int limit) { value = value ?? string.Empty; return value.Length <= limit ? value : value.Substring(0, limit - 1) + "…"; }
        private static string NormalizeConsoleSignature(string message) => ConsoleNumber.Replace((message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim(), "#");

        private static bool HasTestAttribute(MethodInfo method, out bool unityTest)
        {
            unityTest = false;
            IEnumerable<CustomAttributeData> attributes;
            try { attributes = method.CustomAttributes; }
            catch { return false; }
            foreach (var attribute in attributes)
            {
                var name = attribute.AttributeType.FullName;
                if (name == "UnityEngine.TestTools.UnityTestAttribute") { unityTest = true; return true; }
                if (name == "NUnit.Framework.TestAttribute" || name == "NUnit.Framework.TestCaseAttribute" || name == "NUnit.Framework.TheoryAttribute") return true;
            }
            return false;
        }

        private static string InferTestMode(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName)) return "playmode";
            return assemblyName.IndexOf("EditMode", StringComparison.OrdinalIgnoreCase) >= 0
                || assemblyName.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0
                ? "editmode"
                : "playmode";
        }

        private static WorkflowPackageInfo PackageInfoOf(UnityEditor.PackageManager.PackageInfo package)
        {
            return new WorkflowPackageInfo { name = package.name, displayName = package.displayName, version = package.version, source = package.source.ToString(), resolvedPath = package.resolvedPath };
        }

        private static string ValidateRegistryPackageIdentifier(string packageId, bool allowVersion)
        {
            var value = (packageId ?? string.Empty).Trim();
            var valid = allowVersion ? PackageIdentifier.IsMatch(value) : PackageName.IsMatch(value);
            if (!valid) throw new ArgumentException(allowVersion
                ? "packageId must be a registry package name or name@version; Git, file, and URL package sources are not accepted."
                : "packageId must be a registry package name without a version.");
            return value;
        }

        private static List<BuildSceneSetting> ValidateBuildScenes(List<BuildSceneSetting> scenes)
        {
            if (scenes.Count > 256) throw new ArgumentException("At most 256 build scene entries are supported.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var output = new List<BuildSceneSetting>();
            foreach (var scene in scenes)
            {
                if (scene == null) throw new ArgumentException("Build scene entry may not be null.");
                var path = RequireBuildScenePath(scene.path);
                if (!paths.Add(path)) throw new ArgumentException("Build scene paths must be unique.");
                output.Add(new BuildSceneSetting { path = path, enabled = scene.enabled });
            }
            return output;
        }

        private static List<string> ValidateBuildScenePaths(List<string> scenes)
        {
            if (scenes.Count > 256) throw new ArgumentException("At most 256 build scene entries are supported.");
            var values = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in scenes)
            {
                var path = RequireBuildScenePath(scene);
                if (!unique.Add(path)) throw new ArgumentException("Build scene paths must be unique.");
                values.Add(path);
            }
            return values;
        }

        private static string RequireBuildScenePath(string path)
        {
            var fullPath = RequireAssetPath(path, ".unity");
            if (!File.Exists(fullPath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ToProjectPath(fullPath)) == null) throw new ArgumentException("Build scene was not found: " + path);
            EnsureNoReparsePoint(AssetsRoot, fullPath);
            return ToProjectPath(fullPath);
        }

        private static BuildTarget ParseBuildTarget(string value)
        {
            BuildTarget target;
            if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value, true, out target) || !Enum.IsDefined(typeof(BuildTarget), target) || target == BuildTarget.NoTarget)
                throw new ArgumentException("target must be a named supported Unity BuildTarget value.");
            return target;
        }

        private static BuildTargetGroup ParseBuildTargetGroup(string value)
        {
            BuildTargetGroup group;
            if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value, true, out group) || !Enum.IsDefined(typeof(BuildTargetGroup), group) || group == BuildTargetGroup.Unknown)
                throw new ArgumentException("targetGroup must be a named supported Unity BuildTargetGroup value.");
            return group;
        }

        private static void ValidateTargetSupported(BuildTargetGroup group, BuildTarget target)
        {
            if (!BuildPipeline.IsBuildTargetSupported(group, target)) throw new ArgumentException("The requested build target is not supported by this Unity installation.");
        }

        private static string RequireBuildOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath is required.");
            var normalized = outputPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized) || normalized.Contains(":") || !normalized.StartsWith("Builds/", StringComparison.Ordinal) || normalized.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new ArgumentException("outputPath must be a normalized project-relative path under Builds/.");
            var fullPath = ToFullProjectPath(normalized);
            var existingParent = Path.GetDirectoryName(fullPath);
            while (!Directory.Exists(existingParent) && !string.Equals(existingParent, ProjectRoot, StringComparison.OrdinalIgnoreCase)) existingParent = Path.GetDirectoryName(existingParent);
            if (existingParent != null) EnsureNoReparsePoint(ProjectRoot, existingParent);
            return fullPath;
        }

        private sealed class SourceFile { public byte[] bytes; public string text; public bool hasBom; }
        private sealed class ScriptDelimiter { public char character; public int line; public int column; }
    }

    internal interface IEditorWorkflowOperation
    {
        bool DrainWhenCancelled { get; }
        bool Tick(UnityMcpJob job);
    }

    internal static class EditorWorkflowJobRunner
    {
        private static readonly Dictionary<string, IEditorWorkflowOperation> Operations = new Dictionary<string, IEditorWorkflowOperation>(StringComparer.Ordinal);
        private static bool hooked;

        public static UnityMcpJobHandle Start(IEditorWorkflowOperation operation, string jobType = "workflow")
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            var job = UnityMcpJobStore.Shared.Create(jobType);
            Operations.Add(job.jobId, operation);
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return new UnityMcpJobHandle { jobId = job.jobId, jobType = job.jobType, status = job.status, progress = job.progress, progressMessage = job.progressMessage };
        }

        /// <summary>Reattaches an Editor operation to a job restored after a domain reload.</summary>
        public static void Resume(UnityMcpJob job, IEditorWorkflowOperation operation)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            Operations[job.jobId] = operation;
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
        }

        public static void Succeed(UnityMcpJob job, object result)
        {
            UnityMcpJobStore.Shared.Complete(job, UnityMcpResult.Success(result));
        }

        public static void Fail(UnityMcpJob job, string error)
        {
            UnityMcpJobStore.Shared.Fail(job, string.IsNullOrWhiteSpace(error) ? "The Unity Editor operation failed." : error);
        }

        private static void Tick()
        {
            foreach (var pair in Operations.ToArray())
            {
                UnityMcpJob job;
                if (!UnityMcpJobStore.Shared.TryGet(pair.Key, out job)) { Operations.Remove(pair.Key); continue; }
                if (string.Equals(job.status, "cancelled", StringComparison.Ordinal) && !pair.Value.DrainWhenCancelled)
                {
                    if (string.Equals(job.jobType, "play-mode", StringComparison.Ordinal)) PlayModeTransitionRecovery.Clear(job.jobId);
                    Operations.Remove(pair.Key);
                    continue;
                }
                UnityMcpJobStore.Shared.Start(job, "Running Unity Editor workflow.");
                try
                {
                    if (pair.Value.Tick(job)) Operations.Remove(pair.Key);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("UnityMCP workflow job failed (" + exception.GetType().Name + "). Details were redacted.");
                    Fail(job, "The Unity Editor operation failed. See the local Unity Console for details.");
                    Operations.Remove(pair.Key);
                }
            }
            if (Operations.Count == 0 && hooked) { EditorApplication.update -= Tick; hooked = false; }
        }
    }

    internal static class PackageOperationGate
    {
        private static bool active;
        public static bool TryEnter() { if (active) return false; active = true; return true; }
        public static void Exit() { active = false; }
    }

    internal sealed class DeferredPackageRequestOperation : IEditorWorkflowOperation
    {
        private readonly Func<Request> start;
        private readonly Func<Request, object> makeResult;
        private readonly string operation;
        private Request request;
        private bool entered;

        public DeferredPackageRequestOperation(Func<Request> start, Func<Request, object> makeResult, string operation)
        {
            this.start = start;
            this.makeResult = makeResult;
            this.operation = operation;
        }

        public bool DrainWhenCancelled => request != null;

        public bool Tick(UnityMcpJob job)
        {
            if (request == null)
            {
                if (!PackageOperationGate.TryEnter()) return false;
                entered = true;
                job.status = "running";
                try { request = start(); }
                catch
                {
                    PackageOperationGate.Exit();
                    entered = false;
                    throw;
                }
                return false;
            }
            if (!request.IsCompleted) return false;
            try
            {
                if (!string.Equals(job.status, "cancelled", StringComparison.Ordinal))
                {
                    if (request.Status == StatusCode.Success) EditorWorkflowJobRunner.Succeed(job, makeResult(request));
                    else EditorWorkflowJobRunner.Fail(job, request.Error == null ? "Unity Package Manager " + operation + " failed." : request.Error.message);
                }
            }
            finally
            {
                if (entered) PackageOperationGate.Exit();
            }
            return true;
        }
    }

    internal sealed class PackageResolveOperation : IEditorWorkflowOperation
    {
        public bool DrainWhenCancelled => false;

        public bool Tick(UnityMcpJob job)
        {
            if (!PackageOperationGate.TryEnter()) return false;
            try
            {
                job.status = "running";
                Client.Resolve();
                EditorWorkflowJobRunner.Succeed(job, new PackageChangeResult { operation = "resolve", note = "Unity Package Manager accepted the resolve request. Consult the local Console for dependency diagnostics." });
            }
            finally { PackageOperationGate.Exit(); }
            return true;
        }
    }

    internal sealed class CompileOperation : IEditorWorkflowOperation
    {
        private bool requested;
        private bool observedCompilation;
        private DateTime requestedAtUtc;

        public bool DrainWhenCancelled => false;

        public bool Tick(UnityMcpJob job)
        {
            if (!requested)
            {
                requested = true;
                requestedAtUtc = DateTime.UtcNow;
                job.status = "running";
                CompilationPipeline.RequestScriptCompilation();
                return false;
            }
            if (EditorApplication.isCompiling) { observedCompilation = true; return false; }
            if (!observedCompilation && DateTime.UtcNow - requestedAtUtc < TimeSpan.FromSeconds(2)) return false;
            EditorWorkflowJobRunner.Succeed(job, new CompileRequestResult
            {
                requested = true,
                compilationObserved = observedCompilation,
                isCompiling = EditorApplication.isCompiling,
                note = observedCompilation ? "Compilation completed before this Editor domain reloaded." : "Unity accepted the request but did not report a compilation phase in this domain."
            });
            return true;
        }
    }

    internal sealed class BuildTargetSwitchOperation : IEditorWorkflowOperation
    {
        private readonly BuildTargetGroup group;
        private readonly BuildTarget target;
        private bool started;
        private DateTime startedAtUtc;

        public bool DrainWhenCancelled => false;

        public BuildTargetSwitchOperation(BuildTargetGroup group, BuildTarget target) { this.group = group; this.target = target; }

        public bool Tick(UnityMcpJob job)
        {
            if (!started)
            {
                started = true;
                startedAtUtc = DateTime.UtcNow;
                job.status = "running";
                if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(group, target))
                {
                    EditorWorkflowJobRunner.Fail(job, "Unity did not accept the build target switch request.");
                    return true;
                }
                return false;
            }
            if (EditorUserBuildSettings.activeBuildTarget == target && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                EditorWorkflowJobRunner.Succeed(job, new BuildTargetSwitchJobResult { target = target.ToString(), targetGroup = group.ToString(), switched = true });
                return true;
            }
            if (DateTime.UtcNow - startedAtUtc > TimeSpan.FromMinutes(10))
            {
                EditorWorkflowJobRunner.Fail(job, "Timed out waiting for Unity to switch the active build target.");
                return true;
            }
            return false;
        }
    }

    internal sealed class BuildPlayerOperation : IEditorWorkflowOperation
    {
        private readonly BuildTargetGroup group;
        private readonly BuildTarget target;
        private readonly string outputPath;
        private readonly List<string> scenes;
        private readonly bool development;
        private readonly bool allowDebugging;
        private readonly bool connectProfiler;
        private bool started;

        public bool DrainWhenCancelled => false;

        public BuildPlayerOperation(BuildTargetGroup group, BuildTarget target, string outputPath, List<string> scenes, bool development, bool allowDebugging, bool connectProfiler)
        {
            this.group = group;
            this.target = target;
            this.outputPath = outputPath;
            this.scenes = scenes;
            this.development = development;
            this.allowDebugging = allowDebugging;
            this.connectProfiler = connectProfiler;
        }

        public bool Tick(UnityMcpJob job)
        {
            if (started) return false;
            started = true;
            job.status = "running";
            if (BuildPipeline.isBuildingPlayer)
            {
                EditorWorkflowJobRunner.Fail(job, "Unity is already building a player.");
                return true;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var options = BuildOptions.None;
            if (development) options |= BuildOptions.Development;
            if (allowDebugging) options |= BuildOptions.AllowDebugging;
            if (connectProfiler) options |= BuildOptions.ConnectWithProfiler;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = outputPath, target = target, targetGroup = group, options = options });
            var summary = report.summary;
            var result = new BuildPlayerJobResult
            {
                outputPath = EditorWorkflowExpansionTools.ToProjectRelativePath(outputPath),
                target = target.ToString(),
                targetGroup = group.ToString(),
                result = summary.result.ToString(),
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                totalSize = summary.totalSize,
                totalTimeSeconds = summary.totalTime.TotalSeconds
            };
            if (summary.result == BuildResult.Succeeded) EditorWorkflowJobRunner.Succeed(job, result);
            else EditorWorkflowJobRunner.Fail(job, "Unity player build finished with result " + summary.result + ". See the local Console for details.");
            return true;
        }
    }

    internal static class BuildJobTracker
    {
        private static readonly HashSet<string> JobIds = new HashSet<string>(StringComparer.Ordinal);
        public static void Register(string jobId) { if (!string.IsNullOrWhiteSpace(jobId)) JobIds.Add(jobId); }
        public static bool Contains(string jobId) => !string.IsNullOrWhiteSpace(jobId) && JobIds.Contains(jobId);
    }
}
