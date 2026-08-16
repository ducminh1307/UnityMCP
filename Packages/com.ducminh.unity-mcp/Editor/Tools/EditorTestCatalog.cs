using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DucMinh.UnityMcp.Editor
{
    internal sealed class TestCatalogItem
    {
        public string id;
        public string assembly;
        public string fullName;
        public string mode;
        public bool unityTest;
        public List<string> categories;
        public bool explicitTest;
        public int? timeoutMs;
    }

    internal sealed class TestSelection
    {
        public string mode;
        public List<TestCatalogItem> tests;
        public List<string> unknownTests;
        public string hash;
    }

    [Serializable]
    internal sealed class TestValidationError
    {
        public string code;
        public string message;
        public List<string> unknownTests = new List<string>();
        public List<string> explicitTests = new List<string>();
    }

    internal static class EditorTestCatalog
    {
        public static List<TestCatalogItem> Discover(string requestedMode)
        {
            var tests = new List<TestCatalogItem>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(value => value.GetName().Name, StringComparer.Ordinal))
            {
                var assemblyName = assembly.GetName().Name;
                var mode = InferMode(assemblyName);
                if (requestedMode != "all" && mode != requestedMode) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(value => value != null).ToArray(); }
                catch { continue; }
                foreach (var type in types.OrderBy(value => value.FullName, StringComparer.Ordinal))
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                    catch { continue; }
                    foreach (var method in methods.OrderBy(value => value.Name, StringComparer.Ordinal))
                    {
                        bool unityTest;
                        if (!HasTestAttribute(method, out unityTest)) continue;
                        var fullName = (type.FullName ?? type.Name) + "." + method.Name;
                        var attributes = type.GetCustomAttributesData().Concat(method.GetCustomAttributesData()).ToArray();
                        var categories = attributes.Where(attribute => attribute.AttributeType.FullName == "NUnit.Framework.CategoryAttribute")
                            .Select(attribute => attribute.ConstructorArguments.Count == 0 ? null : attribute.ConstructorArguments[0].Value as string)
                            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                        var timeout = attributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == "NUnit.Framework.TimeoutAttribute");
                        tests.Add(new TestCatalogItem
                        {
                            id = StableId(assemblyName, fullName), assembly = assemblyName, fullName = fullName, mode = mode, unityTest = unityTest,
                            categories = categories,
                            explicitTest = attributes.Any(attribute => attribute.AttributeType.FullName == "NUnit.Framework.ExplicitAttribute"),
                            timeoutMs = timeout == null || timeout.ConstructorArguments.Count == 0 ? (int?)null : Convert.ToInt32(timeout.ConstructorArguments[0].Value)
                        });
                    }
                }
            }
            return tests.OrderBy(value => value.fullName, StringComparer.Ordinal).ToList();
        }

        public static TestSelection Resolve(TestRunInput input)
        {
            input = input ?? new TestRunInput();
            var mode = NormalizeMode(input.mode, false);
            var ids = Normalize(input.testIds, "testIds");
            var names = Normalize(input.testNames, "testNames");
            var assemblies = Normalize(input.assemblyNames, "assemblyNames");
            var categories = Normalize(input.categories, "categories");
            var excluded = Normalize(input.excludeCategories, "excludeCategories");
            var hasSelector = ids.Count > 0 || names.Count > 0 || !string.IsNullOrWhiteSpace(input.namePattern) || categories.Count > 0;
            if (!hasSelector && !input.runAll)
                throw Error("TEST_FILTER_REQUIRED", "Provide testIds, testNames, namePattern or categories; otherwise set runAll=true.");

            var all = Discover(mode);
            var unknown = names.Where(name => !all.Any(test => string.Equals(test.fullName, name, StringComparison.Ordinal))).Concat(ids.Where(id => !all.Any(test => string.Equals(test.id, id, StringComparison.Ordinal)))).ToList();
            if (unknown.Count > 0) throw Error("TEST_NOT_FOUND", "One or more requested tests were not found.", unknown);
            var selected = all.Where(test =>
                (assemblies.Count == 0 || assemblies.Contains(test.assembly, StringComparer.OrdinalIgnoreCase)) &&
                (ids.Count == 0 || ids.Contains(test.id, StringComparer.Ordinal)) &&
                (names.Count == 0 || names.Contains(test.fullName, StringComparer.Ordinal)) &&
                (categories.Count == 0 || test.categories.Any(category => categories.Contains(category, StringComparer.OrdinalIgnoreCase))) &&
                (string.IsNullOrWhiteSpace(input.namePattern) || Glob(input.namePattern, test.fullName))).ToList();

            if (!input.includeExplicit)
            {
                var selectedExplicit = selected.Where(test => test.explicitTest || test.categories.Any(category => string.Equals(category, "Stress", StringComparison.OrdinalIgnoreCase))).ToList();
                if (selectedExplicit.Count > 0 && (ids.Count > 0 || names.Count > 0))
                    throw Error("TEST_EXPLICIT_CONFIRMATION_REQUIRED", "Explicit or Stress tests require includeExplicit=true.", selectedExplicit.Select(test => test.fullName).ToList());
                selected = selected.Except(selectedExplicit).ToList();
            }
            selected = selected.Where(test => !test.categories.Any(category => excluded.Contains(category, StringComparer.OrdinalIgnoreCase))).ToList();
            if (selected.Count == 0) throw Error("TEST_FILTER_EMPTY", "The supplied filters resolved to zero tests.");
            var hash = Hash(string.Join("\n", selected.Select(test => test.id).OrderBy(value => value, StringComparer.Ordinal)));
            if (!string.IsNullOrWhiteSpace(input.expectedSelectionHash) && !string.Equals(input.expectedSelectionHash, hash, StringComparison.Ordinal))
                throw Error("TEST_SELECTION_CHANGED", "The resolved test selection differs from expectedSelectionHash.");
            return new TestSelection { mode = mode, tests = selected, unknownTests = unknown, hash = hash };
        }

        public static string NormalizeMode(string value, bool allowAll)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (allowAll && (value == "" || value == "all")) return "all";
            if (value == "editmode" || value == "edit") return "editmode";
            if (value == "playmode" || value == "play") return "playmode";
            throw Error("INVALID_TEST_MODE", "mode must be editmode or playmode.");
        }

        public static string InferMode(string assemblyName) => !string.IsNullOrWhiteSpace(assemblyName) &&
            (assemblyName.IndexOf("EditMode", StringComparison.OrdinalIgnoreCase) >= 0 || assemblyName.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0) ? "editmode" : "playmode";

        private static bool HasTestAttribute(MethodInfo method, out bool unityTest)
        {
            unityTest = false;
            foreach (var attribute in method.GetCustomAttributesData())
            {
                var name = attribute.AttributeType.FullName;
                if (name == "UnityEngine.TestTools.UnityTestAttribute") { unityTest = true; return true; }
                if (name == "NUnit.Framework.TestAttribute" || name == "NUnit.Framework.TestCaseAttribute" || name == "NUnit.Framework.TheoryAttribute") return true;
            }
            return false;
        }

        private static List<string> Normalize(List<string> values, string field)
        {
            values = values ?? new List<string>();
            if (values.Count > 128) throw Error("TEST_FILTER_REQUIRED", field + " contains too many values.");
            return values.Select(value => (value ?? string.Empty).Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        }
        private static bool Glob(string pattern, string value) => Regex.IsMatch(value ?? string.Empty, "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
        private static string StableId(string assembly, string fullName) => Hash((assembly ?? string.Empty) + "\n" + (fullName ?? string.Empty));
        private static string Hash(string value) { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)).Select(item => item.ToString("x2"))); }
        private static UnityMcpValidationException Error(string code, string message, object details = null)
        {
            var output = new TestValidationError { code = code, message = message };
            var names = details as IEnumerable<string>;
            if (names != null)
            {
                if (code == "TEST_NOT_FOUND") output.unknownTests = names.ToList();
                else if (code == "TEST_EXPLICIT_CONFIRMATION_REQUIRED") output.explicitTests = names.ToList();
            }
            return new UnityMcpValidationException(code, message, output);
        }
    }
}
