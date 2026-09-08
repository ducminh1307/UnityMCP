using System;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class EditorAssetDeleteSafetyTests
    {
        [TestCase("Assets")]
        [TestCase("Assets/")]
        public void AssetDelete_RejectsProjectAssetsRoot(string path)
        {
            var exception = Assert.Throws<ArgumentException>(() => EditorCoreTools.ValidateAssetDeleteTarget(path));

            Assert.That(exception.Message, Does.Contain("Assets root"));
        }
    }

    public sealed class ConsoleSeverityTests
    {
        private sealed class LegacyLogEntry { public string condition = "legacy message"; }
        private sealed class Unity66LogEntry { public string message = "Unity 6.6 message"; }
        private sealed class PropertyLogEntry { public string message { get { return "property message"; } } }

        [TestCase(1 << 11, "error")]
        [TestCase(1 << 12, "warning")]
        [TestCase(1 << 8, "error")]
        [TestCase(1 << 9, "warning")]
        [TestCase(1 << 2, "log")]
        public void ConsoleRead_MapsUnityConsoleModeToExpectedSeverity(int mode, string expectedSeverity)
        {
            Assert.That(ConsoleReflection.ClassifySeverity(mode), Is.EqualTo(expectedSeverity));
        }

        [Test]
        public void ConsoleRead_ReadsLegacyCondition()
        {
            Assert.That(ConsoleReflection.Text(typeof(LegacyLogEntry), new LegacyLogEntry(), "condition", "message"), Is.EqualTo("legacy message"));
        }

        [Test]
        public void ConsoleRead_FallsBackToUnity66Message()
        {
            Assert.That(ConsoleReflection.Text(typeof(Unity66LogEntry), new Unity66LogEntry(), "condition", "message"), Is.EqualTo("Unity 6.6 message"));
        }

        [Test]
        public void ConsoleRead_ReadsMessageProperty()
        {
            Assert.That(ConsoleReflection.Text(typeof(PropertyLogEntry), new PropertyLogEntry(), "message"), Is.EqualTo("property message"));
        }
    }

    public sealed class RuntimeGameObjectSelectorTests
    {
        private GameObject first;
        private GameObject second;

        [SetUp]
        public void SetUp()
        {
            var suffix = Guid.NewGuid().ToString("N");
            first = new GameObject("UnityMcpSelectorA-" + suffix);
            second = new GameObject("UnityMcpSelectorB-" + suffix);
        }

        [TearDown]
        public void TearDown()
        {
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
        }

        [Test]
        public void GameObjectSelector_WithIdAndPath_RequiresSameObject()
        {
            var exception = Assert.Throws<ArgumentException>(() => RuntimeCoreTools.GameObjectGet(new GameObjectGetInput
            {
                instanceId = UnityMcpObjectId.Get(first),
                path = PathFor(second)
            }));

            Assert.That(exception.Message, Does.Contain("same GameObject"));
        }

        [Test]
        public void GameObjectSelector_WithMatchingIdAndPath_ReturnsObject()
        {
            var output = RuntimeCoreTools.GameObjectGet(new GameObjectGetInput
            {
                instanceId = UnityMcpObjectId.Get(first),
                path = PathFor(first)
            });

            Assert.That(output.instanceId, Is.EqualTo(UnityMcpObjectId.Get(first)));
        }

        [Test]
        public void GameObjectSelector_RejectsAmbiguousHierarchyPath()
        {
            second.name = first.name;

            var exception = Assert.Throws<ArgumentException>(() => RuntimeCoreTools.GameObjectGet(new GameObjectGetInput
            {
                path = PathFor(first)
            }));

            Assert.That(exception.Message, Does.Contain("ambiguous"));
        }

        private static string PathFor(GameObject value) => value.scene.name + ":/" + value.name;
    }

    public sealed class RuntimeScreenshotSafetyTests
    {
        [Test]
        public void ScreenshotGameViewBudget_ProtectsBridgeResponseLimit()
        {
            Assert.DoesNotThrow(() => RuntimeCoreTools.ValidateScreenshotResponseBudget(1920, 1080));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                RuntimeCoreTools.ValidateScreenshotResponseBudget(2048, 2048));

            Assert.That(exception.Message, Does.Contain("16 MiB"));
        }
    }
}
