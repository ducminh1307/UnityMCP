using System.Linq;
using System.Reflection;
using System.Threading;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityMcpScriptableObjectFixtures
{
    public sealed class ProjectSettingsFixture : ScriptableObject
    {
        public int count;
        public string label;
    }
}

namespace DucMinh.UnityMcp.Tests
{
    public sealed class EditorScriptableObjectToolsTests
    {
        private const string TestFolder = "Assets/UnityMcpScriptableObjectToolTests";
        private const string AssetPath = TestFolder + "/Settings.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.CreateFolder("Assets", "UnityMcpScriptableObjectToolTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestFolder);
        }

        [Test]
        public void Create_DryRun_AcceptsConcreteTypeWithoutAllowlist()
        {
            var output = EditorSceneAssetExpansionTools.ScriptableObjectCreate(
                new ScriptableObjectCreateInput
                {
                    type = typeof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture).FullName,
                    path = AssetPath
                },
                Context("scriptableobject-create", true));

            Assert.That(output.dryRun, Is.True);
            Assert.That(output.created, Is.False);
            Assert.That(output.type, Is.EqualTo(typeof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture).FullName));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(AssetPath), Is.Null);
        }

        [Test]
        public void Get_ReadsExistingAssetWithoutAllowlist()
        {
            CreateFixture(7, "before");

            var output = EditorSceneAssetExpansionTools.ScriptableObjectGet(
                new ScriptableObjectGetInput { path = AssetPath });
            var fields = output.fields.ToDictionary(field => field.name);

            Assert.That(output.type, Is.EqualTo(typeof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture).FullName));
            Assert.That(fields[nameof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture.count)].valueJson, Is.EqualTo("7"));
            Assert.That(fields[nameof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture.label)].valueJson, Is.EqualTo("\"before\""));
        }

        [Test]
        public void Set_UpdatesExistingAssetWithoutAllowlist()
        {
            CreateFixture(7, "before");

            var output = EditorSceneAssetExpansionTools.ScriptableObjectSet(
                new ScriptableObjectSetInput
                {
                    path = AssetPath,
                    fields = new System.Collections.Generic.List<ScriptableObjectFieldSet>
                    {
                        new ScriptableObjectFieldSet { name = nameof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture.count), valueJson = "42" },
                        new ScriptableObjectFieldSet { name = nameof(UnityMcpScriptableObjectFixtures.ProjectSettingsFixture.label), valueJson = "\"after\"" }
                    }
                },
                Context("scriptableobject-set", false));

            var asset = AssetDatabase.LoadAssetAtPath<UnityMcpScriptableObjectFixtures.ProjectSettingsFixture>(AssetPath);
            Assert.That(output.changed, Is.True);
            Assert.That(asset.count, Is.EqualTo(42));
            Assert.That(asset.label, Is.EqualTo("after"));
        }

        [Test]
        public void Create_RejectsScriptableObjectBaseType()
        {
            var exception = Assert.Throws<System.ArgumentException>(() =>
                EditorSceneAssetExpansionTools.ScriptableObjectCreate(
                    new ScriptableObjectCreateInput { type = typeof(ScriptableObject).FullName, path = AssetPath },
                    Context("scriptableobject-create", true)));

            Assert.That(exception.Message, Does.Contain("base type"));
        }

        private static void CreateFixture(int count, string label)
        {
            var asset = ScriptableObject.CreateInstance<UnityMcpScriptableObjectFixtures.ProjectSettingsFixture>();
            asset.count = count;
            asset.label = label;
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
        }

        private static UnityMcpContext Context(string toolName, bool dryRun)
        {
            var constructor = typeof(UnityMcpContext).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(bool), typeof(CancellationToken) },
                null);
            Assert.That(constructor, Is.Not.Null);
            return (UnityMcpContext)constructor.Invoke(new object[] { toolName, dryRun, CancellationToken.None });
        }
    }
}
