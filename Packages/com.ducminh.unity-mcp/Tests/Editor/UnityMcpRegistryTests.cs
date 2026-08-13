using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpRegistryTests
    {
        private Func<IEnumerable<MethodInfo>> originalDiscovery;
        [Serializable]
        public sealed class MutationInput { public string name; public bool apply; }

        public enum ValueState { Ready, Busy }

        [Serializable]
        public sealed class ValueInput
        {
            public ValueState state;
            public Quaternion rotation;
            public Color tint;
        }

        [Serializable]
        public sealed class ValueOutput
        {
            public ValueState state;
            public Quaternion rotation;
            public Color tint;
        }

        [UnityMcpTool("project-test-mutation", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.Write, DefaultEnabled = true, SupportsDryRun = true, MainThread = false)]
        public static string ProjectMutation(MutationInput input, UnityMcpContext context) => input.name;

        [UnityMcpTool("project-test-opaque", Scope = UnityMcpScope.Editor, MainThread = false)]
        public static object InvalidOpaque(object input) => input;

        [UnityMcpTool("project-test-missing-package", Scope = UnityMcpScope.Editor, RequiredType = "DucMinh.UnityMcp.Tests.TypeThatDoesNotExist", MainThread = false)]
        public static string MissingPackage(EmptyInput input) => "unreachable";

        [UnityMcpTool("project-test-available-package", Scope = UnityMcpScope.Editor, RequiredType = "UnityEngine.GameObject", MainThread = false)]
        public static string AvailablePackage(EmptyInput input) => "available";

        [UnityMcpTool("project-test-unity-values", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead, MainThread = false)]
        public static ValueOutput EchoUnityValues(ValueInput input) => new ValueOutput
        {
            state = input.state,
            rotation = input.rotation,
            tint = input.tint
        };

        [UnityMcpTool("unity-status", Scope = UnityMcpScope.Editor, Safety = UnityMcpSafety.SafeRead)]
        public static string CollidingProjectStatus(EmptyInput input) => "project collision";

        [SetUp]
        public void SetUp()
        {
            originalDiscovery = UnityMcpRegistry.DiscoveryOverride;
            UnityMcpRegistry.DiscoveryOverride = null;
        }

        [TearDown]
        public void TearDown() => UnityMcpRegistry.DiscoveryOverride = originalDiscovery;

        [Test]
        public void InputSchema_IsObject_AndApplyIsOptional()
        {
            var method = GetType().GetMethod(nameof(ProjectMutation), BindingFlags.Static | BindingFlags.Public);
            var schema = UnityMcpSchemaGenerator.InputFor(method, true);
            Assert.That(schema["type"], Is.EqualTo("object"));
            var propertiesText = schema["properties"].ToString();
            Assert.That(propertiesText, Does.Contain("apply"));
            Assert.That(propertiesText, Does.Contain("default"));
            var requiredText = schema.TryGetValue("required", out var required) ? required.ToString() : string.Empty;
            Assert.That(requiredText, Does.Not.Contain("apply"));
        }

        [Test]
        public void ProjectTool_IsDisabledRegardlessOfAttributeDefault()
        {
            var method = GetType().GetMethod(nameof(ProjectMutation), BindingFlags.Static | BindingFlags.Public);
            UnityMcpRegistry.DiscoveryOverride = () => new[] { method };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            registry.Reload();
            Assert.That(registry.Tools.Count, Is.EqualTo(1));
            Assert.That(registry.Tools[0].enabled, Is.False);
            Assert.That(registry.Tools[0].defaultEnabled, Is.False);
        }

        [Test]
        public void BatchEnablement_UpdatesAllTools_WithSingleChangeEvent()
        {
            var mutation = GetType().GetMethod(nameof(ProjectMutation), BindingFlags.Static | BindingFlags.Public);
            var values = GetType().GetMethod(nameof(EchoUnityValues), BindingFlags.Static | BindingFlags.Public);
            UnityMcpRegistry.DiscoveryOverride = () => new[] { mutation, values };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            registry.Reload();
            var revisionBefore = registry.RegistryRevision;
            var changeCount = 0;
            registry.Changed += () => changeCount++;

            registry.SetEnabled(new[] { "project-test-mutation", "project-test-unity-values" }, true);

            Assert.That(registry.Tools.All(tool => tool.enabled), Is.True);
            Assert.That(registry.RegistryRevision, Is.Not.EqualTo(revisionBefore));
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [Test]
        public void OpaqueContractWithoutProvider_IsRejected()
        {
            var method = GetType().GetMethod(nameof(InvalidOpaque), BindingFlags.Static | BindingFlags.Public);
            UnityMcpRegistry.DiscoveryOverride = () => new[] { method };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("UnityMCP failed to register 'project-test-opaque'"));
            registry.Reload();
            Assert.That(registry.Tools, Is.Empty);
        }

        [Test]
        public void RequiredTypeThatIsUnavailable_IsNotRegistered()
        {
            var method = GetType().GetMethod(nameof(MissingPackage), BindingFlags.Static | BindingFlags.Public);
            UnityMcpRegistry.DiscoveryOverride = () => new[] { method };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            registry.Reload();
            Assert.That(registry.Tools, Is.Empty);
        }

        [Test]
        public void RequiredTypeThatIsAvailable_IsRegistered()
        {
            var method = GetType().GetMethod(nameof(AvailablePackage), BindingFlags.Static | BindingFlags.Public);
            UnityMcpRegistry.DiscoveryOverride = () => new[] { method };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            registry.Reload();
            Assert.That(registry.Tools.Select(tool => tool.name), Is.EqualTo(new[] { "project-test-available-package" }));
        }

        [Test]
        public void BuiltInCatalog_HasUniqueToolIds()
        {
            var methods = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes)
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.GetCustomAttribute<UnityMcpToolAttribute>(false) != null)
                .Where(m => m.DeclaringType?.Assembly.GetName().Name == "DucMinh.UnityMcp.Runtime" || m.DeclaringType?.Assembly.GetName().Name == "DucMinh.UnityMcp.Editor")
                .ToArray();
            var names = methods.Select(m => m.GetCustomAttribute<UnityMcpToolAttribute>(false).Name).ToArray();
            Assert.That(names, Is.Not.Empty);
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length));
        }

        [Test]
        public void ProjectCollision_CannotHideReservedBuiltInTool()
        {
            var builtIn = typeof(RuntimeCoreTools).GetMethod(nameof(RuntimeCoreTools.UnityStatus));
            var collision = GetType().GetMethod(nameof(CollidingProjectStatus));
            UnityMcpRegistry.DiscoveryOverride = () => new[] { collision, builtIn };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("collides with a reserved built-in name"));
            registry.Reload();
            Assert.That(registry.Tools.Count, Is.EqualTo(1));
            Assert.That(registry.Tools[0].name, Is.EqualTo("unity-status"));
            Assert.That(registry.Tools[0].source, Is.EqualTo("builtin"));
            Assert.That(registry.Tools[0].enabled, Is.True);
        }

        [Test]
        public async Task HttpBridge_RoundTripsTypedEnumQuaternionAndColor()
        {
            var method = GetType().GetMethod(nameof(EchoUnityValues), BindingFlags.Static | BindingFlags.Public);
            UnityMcpRegistry.DiscoveryOverride = () => new[] { method };
            var registry = new UnityMcpRegistry(UnityMcpScope.Editor);
            registry.Reload();
            registry.SetEnabled("project-test-unity-values", true);

            using (var server = new UnityMcpHttpServer(registry, UnityMcpScope.Editor))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) })
            {
                server.Start();
                var body = new JObject
                {
                    ["registryRevision"] = registry.RegistryRevision,
                    ["arguments"] = new JObject
                    {
                        ["state"] = "Busy",
                        ["rotation"] = new JObject { ["x"] = 0.1f, ["y"] = 0.2f, ["z"] = 0.3f, ["w"] = 0.4f },
                        ["tint"] = new JObject { ["r"] = 0.25f, ["g"] = 0.5f, ["b"] = 0.75f, ["a"] = 1f }
                    }
                };
                using (var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"http://127.0.0.1:{server.Descriptor.port}/api/v1/tools/project-test-unity-values/call"))
                {
                    request.Headers.Host = $"127.0.0.1:{server.Descriptor.port}";
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.Descriptor.token);
                    request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (var response = await client.SendAsync(request))
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        Assert.That((int)response.StatusCode, Is.EqualTo(200), responseText);
                        var payload = JObject.Parse(responseText);
                        Assert.That(payload["isError"]?.ToObject<bool>() ?? false, Is.False, responseText);
                        var structured = (JObject)payload["structuredContent"];
                        Assert.That(structured["state"]?.ToObject<string>(), Is.EqualTo("Busy"));
                        Assert.That(structured["rotation"]?["w"]?.ToObject<float>(), Is.EqualTo(0.4f).Within(0.0001f));
                        Assert.That(structured["tint"]?["r"]?.ToObject<float>(), Is.EqualTo(0.25f).Within(0.0001f));
                        Assert.That(structured["tint"]?["a"]?.ToObject<float>(), Is.EqualTo(1f).Within(0.0001f));
                    }
                }
            }
        }

        private static Type[] SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { return exception.Types.Where(t => t != null).ToArray(); }
            catch { return Array.Empty<Type>(); }
        }
    }
}
