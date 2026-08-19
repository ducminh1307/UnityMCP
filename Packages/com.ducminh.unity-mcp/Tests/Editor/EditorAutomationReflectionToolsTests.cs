using System;
using System.Collections.Generic;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class EditorAutomationReflectionToolsTests
    {
        private const string TestFolder = "Assets/UnityMcpReflectionToolTests";
        private const string PrimaryAllowlistPath = TestFolder + "/Primary.asset";
        private const string SecondaryAllowlistPath = TestFolder + "/Secondary.asset";

        public sealed class ReflectionTarget : ScriptableObject
        {
            public void AllowedMethod(int value) { }
        }

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.CreateFolder("Assets", "UnityMcpReflectionToolTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestFolder);
        }

        [Test]
        public void MethodFind_WithoutPath_DiscoversUniqueMatchingAllowlist()
        {
            CreateAllowlist(PrimaryAllowlistPath);

            var output = EditorAutomationReflectionTools.MethodFind(new MethodFindInput
            {
                type = typeof(ReflectionTarget).AssemblyQualifiedName
            });

            Assert.That(output.allowlistPath, Is.EqualTo(PrimaryAllowlistPath));
            Assert.That(output.type, Is.EqualTo(typeof(ReflectionTarget).FullName));
            Assert.That(output.methods, Has.Count.EqualTo(1));
            Assert.That(output.methods[0].name, Is.EqualTo(nameof(ReflectionTarget.AllowedMethod)));
            Assert.That(output.methods[0].parameterTypes, Is.EqualTo(new[] { "System.Int32" }));
        }

        [Test]
        public void MethodFind_WithoutPath_RejectsAmbiguousMatchingAllowlists()
        {
            CreateAllowlist(PrimaryAllowlistPath);
            CreateAllowlist(SecondaryAllowlistPath);

            var exception = Assert.Throws<ArgumentException>(() =>
                EditorAutomationReflectionTools.MethodFind(new MethodFindInput
                {
                    type = typeof(ReflectionTarget).AssemblyQualifiedName
                }));

            Assert.That(exception.Message, Does.Contain("Multiple project UnityMCP reflection allowlists"));
        }

        private static void CreateAllowlist(string path)
        {
            var allowlist = ScriptableObject.CreateInstance<UnityMcpReflectionAllowlist>();
            allowlist.types.Add(new UnityMcpReflectionTypeRule
            {
                typeName = typeof(ReflectionTarget).AssemblyQualifiedName,
                callableMethods = new List<UnityMcpReflectionMethodRule>
                {
                    new UnityMcpReflectionMethodRule
                    {
                        methodName = nameof(ReflectionTarget.AllowedMethod),
                        parameterTypeNames = new List<string> { typeof(int).FullName }
                    }
                }
            });
            AssetDatabase.CreateAsset(allowlist, path);
            AssetDatabase.SaveAssets();
        }
    }
}
