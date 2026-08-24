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
                instanceId = first.GetInstanceID(),
                path = PathFor(second)
            }));

            Assert.That(exception.Message, Does.Contain("same GameObject"));
        }

        [Test]
        public void GameObjectSelector_WithMatchingIdAndPath_ReturnsObject()
        {
            var output = RuntimeCoreTools.GameObjectGet(new GameObjectGetInput
            {
                instanceId = first.GetInstanceID(),
                path = PathFor(first)
            });

            Assert.That(output.instanceId, Is.EqualTo(first.GetInstanceID()));
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
