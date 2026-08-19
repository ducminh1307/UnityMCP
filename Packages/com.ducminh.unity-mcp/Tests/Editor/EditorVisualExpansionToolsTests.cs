using System;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class EditorVisualExpansionToolsTests
    {
        private GameObject cameraObject;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Screenshot Camera");
            cameraObject.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void ResolveLoadedCamera_AcceptsCameraComponentInstanceId()
        {
            var expected = cameraObject.GetComponent<Camera>();

            var actual = EditorVisualExpansionTools.ResolveLoadedCamera(expected.GetInstanceID(), "invalid camera");

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public void ResolveLoadedCamera_AcceptsCameraGameObjectInstanceId()
        {
            var expected = cameraObject.GetComponent<Camera>();

            var actual = EditorVisualExpansionTools.ResolveLoadedCamera(cameraObject.GetInstanceID(), "invalid camera");

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public void ResolveLoadedCamera_RejectsGameObjectWithoutCamera()
        {
            UnityEngine.Object.DestroyImmediate(cameraObject.GetComponent<Camera>());

            var exception = Assert.Throws<ArgumentException>(() =>
                EditorVisualExpansionTools.ResolveLoadedCamera(cameraObject.GetInstanceID(), "invalid camera"));

            Assert.That(exception.Message, Is.EqualTo("invalid camera"));
        }
    }
}
