using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DucMinh.UnityMcp.Editor
{
    internal sealed class UnityMcpAnalyticsView : VisualElement
    {
        private const int MaxRows = 20;
        private const int MaxRecentLines = 10000;

        private readonly Label pathLabel;
        private readonly Label summaryLabel;
        private readonly Label listSummaryLabel;
        private readonly Label emptyLabel;
        private readonly VisualElement rowsContainer;

        public UnityMcpAnalyticsView()
        {
            AddToClassList("unity-mcp-stack");

            var summaryCard = CreateCard("MCP analytics", "Project-local tool-call telemetry from this Unity project's Editor-managed gateway.");
            Add(summaryCard);

            pathLabel = new Label();
            pathLabel.AddToClassList("unity-mcp-key-value__value");
            pathLabel.AddToClassList("unity-mcp-code");
            summaryCard.Add(CreateKeyValueRow("Telemetry file", pathLabel));

            summaryLabel = new Label("No telemetry loaded.");
            summaryLabel.AddToClassList("unity-mcp-key-value__value");
            summaryCard.Add(CreateKeyValueRow("Tool calls", summaryLabel));

            listSummaryLabel = new Label("No tools/list telemetry loaded.");
            listSummaryLabel.AddToClassList("unity-mcp-key-value__value");
            summaryCard.Add(CreateKeyValueRow("Tool list", listSummaryLabel));

            var actions = new VisualElement();
            actions.AddToClassList("unity-mcp-action-row");
            var refreshButton = new Button(Refresh) { text = "Refresh", tooltip = "Reload this project's MCP telemetry file." };
            refreshButton.AddToClassList("unity-mcp-primary-button");
            var revealButton = new Button(RevealTelemetryFile) { text = "Reveal file", tooltip = "Show the telemetry JSONL file in the system file browser." };
            revealButton.AddToClassList("unity-mcp-secondary-button");
            var clearButton = new Button(ClearTelemetry) { text = "Clear telemetry", tooltip = "Delete this project's MCP telemetry file." };
            clearButton.AddToClassList("unity-mcp-danger-button");
            actions.Add(refreshButton);
            actions.Add(revealButton);
            actions.Add(clearButton);
            summaryCard.Add(actions);

            var tableCard = CreateCard("Tool payload ranking", "Largest response payloads are the strongest signal for MCP context and API token pressure.");
            Add(tableCard);

            rowsContainer = new VisualElement();
            rowsContainer.AddToClassList("unity-mcp-analytics-table");
            tableCard.Add(rowsContainer);

            emptyLabel = new Label();
            emptyLabel.AddToClassList("unity-mcp-empty-state__description");
            tableCard.Add(emptyLabel);
        }

        public void Refresh()
        {
            var path = UnityMcpGatewayHost.GetProjectTelemetryPath();
            pathLabel.text = path;
            rowsContainer.Clear();
            emptyLabel.text = string.Empty;

            if (!File.Exists(path))
            {
                summaryLabel.text = "0 calls";
                listSummaryLabel.text = "0 events";
                emptyLabel.text = "Start the Editor-managed gateway and use MCP from this project to populate analytics.";
                return;
            }

            var stats = new Dictionary<string, ToolStats>(StringComparer.Ordinal);
            var toolCalls = 0;
            var listEvents = 0;
            var latestToolCount = 0;
            long latestListBytes = 0;

            foreach (var line in ReadRecentLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject entry;
                try
                {
                    entry = JObject.Parse(line);
                }
                catch
                {
                    continue;
                }

                var eventName = entry.Value<string>("event");
                if (string.Equals(eventName, "tools/list", StringComparison.Ordinal))
                {
                    listEvents++;
                    latestToolCount = entry.Value<int?>("toolCount") ?? latestToolCount;
                    latestListBytes = entry.Value<long?>("responseBytes") ?? latestListBytes;
                    continue;
                }
                if (!string.Equals(eventName, "tools/call", StringComparison.Ordinal)) continue;

                var toolName = entry.Value<string>("toolName");
                if (string.IsNullOrWhiteSpace(toolName)) continue;
                toolCalls++;
                if (!stats.TryGetValue(toolName, out var toolStats))
                {
                    toolStats = new ToolStats(toolName);
                    stats.Add(toolName, toolStats);
                }
                toolStats.Add(entry);
            }

            var orderedStats = stats.Values
                .OrderByDescending(item => item.ResponseBytes)
                .ThenByDescending(item => item.Calls)
                .ThenBy(item => item.ToolName, StringComparer.Ordinal)
                .Take(MaxRows)
                .ToList();

            summaryLabel.text = toolCalls.ToString(CultureInfo.InvariantCulture) + " calls, "
                + FormatBytes(stats.Values.Sum(item => item.ResponseBytes)) + " responses, "
                + FormatBytes(stats.Values.Sum(item => item.RequestBytes)) + " requests";
            listSummaryLabel.text = listEvents.ToString(CultureInfo.InvariantCulture) + " events, latest "
                + latestToolCount.ToString(CultureInfo.InvariantCulture) + " tools, "
                + FormatBytes(latestListBytes);

            if (orderedStats.Count == 0)
            {
                emptyLabel.text = "No tool-call rows found in this project's telemetry yet.";
                return;
            }

            rowsContainer.Add(CreateRow("Tool", "Calls", "Errors", "Request", "Response", "Avg ms", "Trunc.", true));
            foreach (var stat in orderedStats)
            {
                rowsContainer.Add(CreateRow(
                    stat.ToolName,
                    stat.Calls.ToString(CultureInfo.InvariantCulture),
                    stat.Errors.ToString(CultureInfo.InvariantCulture),
                    FormatBytes(stat.RequestBytes),
                    FormatBytes(stat.ResponseBytes),
                    stat.AverageDurationMs.ToString("0.0", CultureInfo.InvariantCulture),
                    stat.Truncations.ToString(CultureInfo.InvariantCulture),
                    false));
            }
        }

        private static IEnumerable<string> ReadRecentLines(string path)
        {
            var recent = new Queue<string>(MaxRecentLines);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (recent.Count == MaxRecentLines) recent.Dequeue();
                    recent.Enqueue(line);
                }
            }
            return recent.ToList();
        }

        private static VisualElement CreateRow(string tool, string calls, string errors, string request, string response, string avgDuration, string truncations, bool header)
        {
            var row = new VisualElement();
            row.AddToClassList("unity-mcp-analytics-row");
            if (header) row.AddToClassList("unity-mcp-analytics-row--header");
            row.Add(CreateCell(tool, "unity-mcp-analytics-cell--tool"));
            row.Add(CreateCell(calls, "unity-mcp-analytics-cell--number"));
            row.Add(CreateCell(errors, "unity-mcp-analytics-cell--number"));
            row.Add(CreateCell(request, "unity-mcp-analytics-cell--number"));
            row.Add(CreateCell(response, "unity-mcp-analytics-cell--number"));
            row.Add(CreateCell(avgDuration, "unity-mcp-analytics-cell--number"));
            row.Add(CreateCell(truncations, "unity-mcp-analytics-cell--number"));
            return row;
        }

        private static Label CreateCell(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList("unity-mcp-analytics-cell");
            label.AddToClassList(className);
            label.tooltip = text;
            return label;
        }

        private static VisualElement CreateCard(string title, string description)
        {
            var card = new VisualElement();
            card.AddToClassList("unity-mcp-card");
            var header = new VisualElement();
            header.AddToClassList("unity-mcp-card__header");
            var copy = new VisualElement();
            copy.Add(new Label(title).WithClass("unity-mcp-card__title"));
            copy.Add(new Label(description).WithClass("unity-mcp-card__description"));
            header.Add(copy);
            card.Add(header);
            return card;
        }

        private static VisualElement CreateKeyValueRow(string key, Label value)
        {
            var row = new VisualElement();
            row.AddToClassList("unity-mcp-key-value");
            row.Add(new Label(key).WithClass("unity-mcp-key-value__key"));
            row.Add(value);
            return row;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            var kib = bytes / 1024d;
            if (kib < 1024d) return kib.ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
            var mib = kib / 1024d;
            return mib.ToString("0.00", CultureInfo.InvariantCulture) + " MiB";
        }

        private static void RevealTelemetryFile()
        {
            var path = UnityMcpGatewayHost.GetProjectTelemetryPath();
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }
            EditorUtility.RevealInFinder(Path.GetDirectoryName(path) ?? Application.dataPath);
        }

        private void ClearTelemetry()
        {
            var path = UnityMcpGatewayHost.GetProjectTelemetryPath();
            if (!File.Exists(path))
            {
                Refresh();
                return;
            }
            if (!EditorUtility.DisplayDialog("Clear MCP analytics", "Delete this project's MCP telemetry file?", "Clear", "Cancel")) return;
            try
            {
                File.Delete(path);
            }
            catch (IOException exception)
            {
                EditorUtility.DisplayDialog("Clear MCP analytics", exception.Message, "OK");
            }
            catch (UnauthorizedAccessException exception)
            {
                EditorUtility.DisplayDialog("Clear MCP analytics", exception.Message, "OK");
            }
            Refresh();
        }

        private sealed class ToolStats
        {
            public ToolStats(string toolName)
            {
                ToolName = toolName;
            }

            public string ToolName { get; }
            public int Calls { get; private set; }
            public int Errors { get; private set; }
            public int Truncations { get; private set; }
            public long RequestBytes { get; private set; }
            public long ResponseBytes { get; private set; }
            private double TotalDurationMs { get; set; }
            public double AverageDurationMs => Calls == 0 ? 0d : TotalDurationMs / Calls;

            public void Add(JObject entry)
            {
                Calls++;
                if (entry.Value<bool?>("isError") == true) Errors++;
                RequestBytes += entry.Value<long?>("requestBytes") ?? 0L;
                ResponseBytes += entry.Value<long?>("responseBytes") ?? 0L;
                TotalDurationMs += entry.Value<double?>("durationMs") ?? 0d;
                Truncations += entry["truncationFlags"] is JArray flags ? flags.Count : 0;
            }
        }
    }
}
