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

            var actual = EditorVisualExpansionTools.ResolveLoadedCamera(UnityMcpObjectId.Get(expected), "invalid camera");

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public void ResolveLoadedCamera_AcceptsCameraGameObjectInstanceId()
        {
            var expected = cameraObject.GetComponent<Camera>();

            var actual = EditorVisualExpansionTools.ResolveLoadedCamera(UnityMcpObjectId.Get(cameraObject), "invalid camera");

            Assert.That(actual, Is.SameAs(expected));
        }

        [Test]
        public void ResolveLoadedCamera_RejectsGameObjectWithoutCamera()
        {
            UnityEngine.Object.DestroyImmediate(cameraObject.GetComponent<Camera>());

            var exception = Assert.Throws<ArgumentException>(() =>
                EditorVisualExpansionTools.ResolveLoadedCamera(UnityMcpObjectId.Get(cameraObject), "invalid camera"));

            Assert.That(exception.Message, Is.EqualTo("invalid camera"));
        }

        [Test]
        public void ValidateScreenshotCaptureBudget_AcceptsBoundedAggregate()
        {
            Assert.DoesNotThrow(() => EditorVisualExpansionTools.ValidateScreenshotCaptureBudget(4, 960, 540));
            Assert.DoesNotThrow(() => EditorVisualExpansionTools.ValidateScreenshotCaptureBudget(1, 1920, 1080));
        }

        [Test]
        public void ResolveScreenshotSize_PreservesExplicitRequestedDimensions()
        {
            var size = EditorVisualExpansionTools.ResolveScreenshotSize(1080, 1920);

            Assert.That(size, Is.EqualTo(new Vector2Int(1080, 1920)));
        }

        [Test]
        public void ScreenshotRenderTarget_UsesSrgbEncoding()
        {
            var renderTexture = RenderTexture.GetTemporary(16, 16, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Assert.That(renderTexture.sRGB, Is.True);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        [Test]
        public void ValidateScreenshotCaptureBudget_RejectsWorstCasePayloadOverHttpLimit()
        {
            var multiCamera = Assert.Throws<ArgumentException>(() =>
                EditorVisualExpansionTools.ValidateScreenshotCaptureBudget(8, 960, 540));
            var maximumDimensions = Assert.Throws<ArgumentException>(() =>
                EditorVisualExpansionTools.ValidateScreenshotCaptureBudget(1, 2048, 2048));

            Assert.That(multiCamera.Message, Does.Contain("16 MiB"));
            Assert.That(maximumDimensions.Message, Does.Contain("16 MiB"));
        }

        [Test]
        public void ScreenshotCamera_RejectsOversizedWorstCaseBeforeRendering()
        {
            var exception = Assert.Throws<ArgumentException>(() => EditorVisualExpansionTools.ScreenshotCamera(new ScreenshotCameraInput
            {
                instanceId = UnityMcpObjectId.Get(cameraObject),
                width = 2048,
                height = 2048
            }));

            Assert.That(exception.Message, Does.Contain("16 MiB"));
        }
    }
}
