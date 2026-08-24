using System;
using System.Linq;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class UnityMcpAllowListsViewTests
    {
        [Test]
        public void Catalog_ContainsEveryActivePolicyAndExcludesLegacyScriptableObjectPolicy()
        {
            var definitions = UnityMcpAllowListCatalog.Definitions;
            var assetTypes = definitions.Select(definition => definition.AssetType).ToArray();

            Assert.That(assetTypes, Is.EquivalentTo(new[]
            {
                typeof(UnityMcpMenuAllowlist),
                typeof(UnityMcpReflectionAllowlist),
                typeof(UnityMcpCSharpCommandAllowlist),
                typeof(UnityMcpBatchAllowlist)
            }));
            Assert.That(assetTypes.Contains(typeof(UnityMcpScriptableObjectAllowlist)), Is.False);

            foreach (var definition in definitions)
            {
                var asset = ScriptableObject.CreateInstance(definition.AssetType);
                try
                {
                    var serialized = new SerializedObject(asset);
                    Assert.That(serialized.FindProperty(definition.PropertyName), Is.Not.Null, definition.Title);
                    Assert.That(definition.ToolNames, Is.Not.Empty, definition.Title);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

        [Test]
        public void View_BuildsOneEditableSectionPerActivePolicy()
        {
            var view = new UnityMcpAllowListsView(() => null);

            Assert.That(view.Q<HelpBox>(), Is.Not.Null);
            foreach (var definition in UnityMcpAllowListCatalog.Definitions)
            {
                var section = view.Q<VisualElement>("unity-mcp-allow-list-" + definition.Id);
                Assert.That(section, Is.Not.Null, definition.Title);
                Assert.That(section.Q<ObjectField>("unity-mcp-allow-list-" + definition.Id + "-asset"), Is.Not.Null, definition.Title);
                Assert.That(section.Q<Button>("unity-mcp-allow-list-" + definition.Id + "-create"), Is.Not.Null, definition.Title);
                Assert.That(section.Q<Button>("unity-mcp-allow-list-" + definition.Id + "-save"), Is.Not.Null, definition.Title);
            }
        }

        [TestCase("Packages/Policy.asset")]
        [TestCase("Assets/../Policy.asset")]
        [TestCase("Assets/Policy.txt")]
        [TestCase("")]
        public void CreateAsset_RejectsPathsOutsideAssetsPolicyBoundary(string path)
        {
            Assert.Throws<ArgumentException>(() => UnityMcpAllowListCatalog.CreateAsset(UnityMcpAllowListCatalog.Definitions[0], path));
        }
    }
}
