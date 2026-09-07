using System;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class PlayerSettingsToolsTests
    {
        [Test]
        public void PlayerSettingsGet_ReadsSupportedNonSecretProjection()
        {
            var output = EditorWorkflowExpansionTools.PlayerSettingsGet(new PlayerSettingsGetInput { targetGroup = "Standalone" });

            Assert.That(output.targetGroup, Is.EqualTo("Standalone"));
            Assert.That(output.companyName, Is.Not.Null);
            Assert.That(output.productName, Is.Not.Null);
            Assert.That(output.scriptingBackend, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void PlayerSettingsSet_RequiresAtLeastOneSetting()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                EditorWorkflowExpansionTools.ValidatePlayerSettingsSet(new PlayerSettingsSetInput()));

            Assert.That(exception.Message, Does.Contain("at least one"));
        }

        [TestCase("companyName")]
        [TestCase("productName")]
        [TestCase("bundleVersion")]
        [TestCase("applicationIdentifier")]
        public void PlayerSettingsSet_RejectsBlankIdentityValues(string field)
        {
            var input = new PlayerSettingsSetInput();
            switch (field)
            {
                case "companyName": input.companyName = " "; break;
                case "productName": input.productName = " "; break;
                case "bundleVersion": input.bundleVersion = " "; break;
                default: input.applicationIdentifier = " "; break;
            }

            var exception = Assert.Throws<ArgumentException>(() => EditorWorkflowExpansionTools.ValidatePlayerSettingsSet(input));

            Assert.That(exception.Message, Does.Contain(field));
        }
    }
}
