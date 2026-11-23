using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class EditorVisualExpansionToolsTests
    {
        private GameObject _cameraObject;
        private GameObject _cubeObject;
        private readonly List<string> _tempAssetFolders = new List<string>();
        private readonly List<string> _tempDiskFiles = new List<string>();
        private readonly List<string> _tempDiskDirs = new List<string>();

        private bool _origFog;
        private Color _origFogColor;
        private float _origFogDensity;
        private FogMode _origFogMode;
        private UnityEngine.Rendering.AmbientMode _origAmbientMode;
        private float _origAmbientIntensity;
        private float _origReflectionIntensity;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("Screenshot Camera");
            _cameraObject.AddComponent<Camera>();

            _origFog = RenderSettings.fog;
            _origFogColor = RenderSettings.fogColor;
            _origFogDensity = RenderSettings.fogDensity;
            _origFogMode = RenderSettings.fogMode;
            _origAmbientMode = RenderSettings.ambientMode;
            _origAmbientIntensity = RenderSettings.ambientIntensity;
            _origReflectionIntensity = RenderSettings.reflectionIntensity;
        }

        [TearDown]
        public void TearDown()
        {
            RenderSettings.fog = _origFog;
            RenderSettings.fogColor = _origFogColor;
            RenderSettings.fogDensity = _origFogDensity;
            RenderSettings.fogMode = _origFogMode;
            RenderSettings.ambientMode = _origAmbientMode;
            RenderSettings.ambientIntensity = _origAmbientIntensity;
            RenderSettings.reflectionIntensity = _origReflectionIntensity;

            if (_cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_cameraObject);
                _cameraObject = null;
            }

            if (_cubeObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_cubeObject);
                _cubeObject = null;
            }

            foreach (var file in _tempDiskFiles)
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    File.Delete(file);
            }
            _tempDiskFiles.Clear();

            foreach (var dir in _tempDiskDirs)
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            _tempDiskDirs.Clear();

            foreach (var folder in _tempAssetFolders)
            {
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
            _tempAssetFolders.Clear();
        }

        [Test]
        public void RenderAndVisualCommands_RegisterCleanlyInCommandRegistry()
        {
            var commands = CommandRegistry.DiscoverCommands().ToList();
            var renderNames = commands
                .Where(c => string.Equals(c.Group, "render_ext", StringComparison.Ordinal))
                .Select(c => c.Name)
                .ToArray();
            var visualNames = commands
                .Where(c => string.Equals(c.Group, "visual_ext", StringComparison.Ordinal))
                .Select(c => c.Name)
                .ToArray();

            Assert.That(renderNames, Is.EquivalentTo(new[]
            {
                "get_settings",
                "set_settings",
                "generate_texture"
            }));
            Assert.That(visualNames, Is.EquivalentTo(new[]
            {
                "capture_gameobject",
                "capture_multiview",
                "frame_debugger_events"
            }));
        }

        [Test]
        public void RenderExtension_GetAndSetSettings_RespectsDryRunAndAppliesChanges()
        {
            var initial = EditorRenderExtensionCommands.GetSettings();
            Assert.That(initial, Is.Not.Null);
            Assert.That(initial.fog, Is.EqualTo(_origFog));
            Assert.That(initial.fogDensity, Is.EqualTo(_origFogDensity));

            var targetFog = !_origFog;
            var dryResult = EditorRenderExtensionCommands.SetSettings(new RenderSettingsSetInput
            {
                fog = targetFog,
                fogDensity = 0.042f,
                fogMode = "ExponentialSquared",
                dryRun = true
            });

            Assert.That(dryResult.dryRun, Is.True);
            Assert.That(dryResult.changed, Is.False);
            Assert.That(RenderSettings.fog, Is.EqualTo(_origFog));

            var liveResult = EditorRenderExtensionCommands.SetSettings(new RenderSettingsSetInput
            {
                fog = targetFog,
                fogColor = Color.blue,
                fogDensity = 0.042f,
                fogMode = "ExponentialSquared",
                ambientIntensity = 1.25f,
                reflectionIntensity = 0.75f,
                dryRun = false
            });

            Assert.That(liveResult.dryRun, Is.False);
            Assert.That(liveResult.changed, Is.True);

            var updated = EditorRenderExtensionCommands.GetSettings();
            Assert.That(updated.fog, Is.EqualTo(targetFog));
            Assert.That(updated.fogDensity, Is.EqualTo(0.042f).Within(0.0001f));
            Assert.That(updated.fogMode, Is.EqualTo(FogMode.ExponentialSquared.ToString()));
            Assert.That(updated.ambientIntensity, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(updated.reflectionIntensity, Is.EqualTo(0.75f).Within(0.0001f));

            Assert.Throws<ArgumentException>(() => EditorRenderExtensionCommands.SetSettings(new RenderSettingsSetInput()));
        }

        [Test]
        public void RenderExtension_GenerateTexture_RespectsDryRunAndCreatesPngAsset()
        {
            var folder = CreateTempAssetFolder();
            var texPath = folder + "/GeneratedSolid.png";

            var dryResult = EditorRenderExtensionCommands.GenerateTexture(new TextureGenerateInput
            {
                path = texPath,
                width = 16,
                height = 16,
                color = Color.cyan,
                dryRun = true
            });

            Assert.That(dryResult.dryRun, Is.True);
            Assert.That(dryResult.changed, Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(texPath), Is.Null);

            var liveResult = EditorRenderExtensionCommands.GenerateTexture(new TextureGenerateInput
            {
                path = texPath,
                width = 16,
                height = 16,
                color = Color.cyan,
                dryRun = false
            });

            Assert.That(liveResult.dryRun, Is.False);
            Assert.That(liveResult.changed, Is.True);
            Assert.That(liveResult.path, Is.EqualTo(texPath));

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(16));
            Assert.That(texture.height, Is.EqualTo(16));
        }

        [Test]
        public void ResolveLoadedCamera_AcceptsCameraComponentAndGameObject_AndRejectsMissingCamera()
        {
            var expected = _cameraObject.GetComponent<Camera>();

            Assert.That(
                EditorVisualQaCommands.ResolveLoadedCamera(UnityMcpObjectId.Get(expected), "invalid camera"),
                Is.SameAs(expected));
            Assert.That(
                EditorVisualQaCommands.ResolveLoadedCamera(UnityMcpObjectId.Get(_cameraObject), "invalid camera"),
                Is.SameAs(expected));

            UnityEngine.Object.DestroyImmediate(expected);
            var exception = Assert.Throws<ArgumentException>(() =>
                EditorVisualQaCommands.ResolveLoadedCamera(UnityMcpObjectId.Get(_cameraObject), "invalid camera"));
            Assert.That(exception.Message, Is.EqualTo("invalid camera"));
        }

        [Test]
        public void ValidateScreenshotCaptureBudget_AndSizeAndTargetSrgb_BehaveAsExpected()
        {
            Assert.DoesNotThrow(() => EditorVisualQaCommands.ValidateScreenshotCaptureBudget(4, 960, 540));
            Assert.DoesNotThrow(() => EditorVisualQaCommands.ValidateScreenshotCaptureBudget(1, 1920, 1080));
            Assert.That(EditorVisualQaCommands.ResolveScreenshotSize(1080, 1920), Is.EqualTo(new Vector2Int(1080, 1920)));

            var renderTexture = RenderTexture.GetTemporary(16, 16, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Assert.That(renderTexture.sRGB, Is.EqualTo(QualitySettings.activeColorSpace == ColorSpace.Linear));
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            var multiCamera = Assert.Throws<ArgumentException>(() =>
                EditorVisualQaCommands.ValidateScreenshotCaptureBudget(8, 960, 540));
            var maximumDimensions = Assert.Throws<ArgumentException>(() =>
                EditorVisualQaCommands.ValidateScreenshotCaptureBudget(1, 2048, 2048));

            Assert.That(multiCamera.Message, Does.Contain("16 MiB"));
            Assert.That(maximumDimensions.Message, Does.Contain("16 MiB"));
        }

        [Test]
        public void CaptureMultiview_CapturesCameraAndRespectsOutputDirAndInlineBudget()
        {
            var oversized = Assert.Throws<ArgumentException>(() => EditorVisualQaCommands.CaptureMultiview(new CaptureMultiviewInput
            {
                cameraInstanceIds = new List<int> { UnityMcpObjectId.Get(_cameraObject) },
                width = 2048,
                height = 2048
            }));
            Assert.That(oversized.Message, Does.Contain("16 MiB"));

            // 1. Default (no outputDir) -> writes under Temp/unity-mcp-captures and includes base64
            var inlineCapture = EditorVisualQaCommands.CaptureMultiview(new CaptureMultiviewInput
            {
                cameraInstanceIds = new List<int> { UnityMcpObjectId.Get(_cameraObject) },
                width = 128,
                height = 128
            });

            Assert.That(inlineCapture.screenshots.Count, Is.EqualTo(1));
            var item1 = inlineCapture.screenshots[0];
            TrackTempFile(item1.savedPath);
            Assert.That(File.Exists(item1.savedPath), Is.True);
            Assert.That(item1.width, Is.EqualTo(128));
            Assert.That(item1.height, Is.EqualTo(128));
            Assert.That(item1.encoding, Is.EqualTo("png"));
            Assert.That(item1.bytes, Is.GreaterThan(0));
            Assert.That(item1.base64, Is.Not.Null.And.Not.Empty);

            // 2. With outputDir + includeInlineImage=false -> writes per-camera PNG and omits base64
            var outDir = CreateTempDiskDir();
            var diskCapture = EditorVisualQaCommands.CaptureMultiview(new CaptureMultiviewInput
            {
                cameraInstanceIds = new List<int> { UnityMcpObjectId.Get(_cameraObject) },
                width = 96,
                height = 96,
                outputDir = outDir,
                includeInlineImage = false
            });

            Assert.That(diskCapture.screenshots.Count, Is.EqualTo(1));
            var item2 = diskCapture.screenshots[0];
            Assert.That(File.Exists(item2.savedPath), Is.True);
            Assert.That(item2.savedPath, Does.StartWith(Path.GetFullPath(outDir)));
            Assert.That(item2.base64, Is.Null);
        }

        [Test]
        public void CaptureGameObject_WritesPngAndRespectsInlineImageFlag()
        {
            _cubeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var id = UnityMcpObjectId.Get(_cubeObject);

            var outDir = CreateTempDiskDir();
            var explicitPath = Path.Combine(outDir, "cube-preview.png");

            var diskOnly = EditorVisualQaCommands.CaptureGameObject(new CaptureGameObjectInput
            {
                instanceId = id,
                width = 128,
                height = 128,
                outputPath = explicitPath,
                includeInlineImage = false
            });

            Assert.That(diskOnly.instanceId, Is.EqualTo(id));
            Assert.That(diskOnly.width, Is.EqualTo(128));
            Assert.That(diskOnly.height, Is.EqualTo(128));
            Assert.That(diskOnly.encoding, Is.EqualTo("png"));
            Assert.That(diskOnly.bytes, Is.GreaterThan(0));
            Assert.That(File.Exists(diskOnly.savedPath), Is.True);
            Assert.That(diskOnly.base64, Is.Null);

            var defaultInline = EditorVisualQaCommands.CaptureGameObject(new CaptureGameObjectInput
            {
                instanceId = id,
                width = 64,
                height = 64
            });
            TrackTempFile(defaultInline.savedPath);

            Assert.That(File.Exists(defaultInline.savedPath), Is.True);
            Assert.That(defaultInline.base64, Is.Not.Null.And.Not.Empty);
            Assert.That(Convert.FromBase64String(defaultInline.base64), Is.EqualTo(File.ReadAllBytes(defaultInline.savedPath)));
        }

        [Test]
        public void GetFrameDebuggerEvents_ReturnsBoundedOutputWithoutEnablingDebugger()
        {
            var output = EditorVisualQaCommands.GetFrameDebuggerEvents(new FrameDebuggerEventsInput
            {
                startIndex = 0,
                limit = 25,
                includeObjectMetadata = true
            });

            Assert.That(output, Is.Not.Null);
            Assert.That(output.events, Is.Not.Null);
            Assert.That(output.events.Count, Is.LessThanOrEqualTo(25));

            Assert.Throws<ArgumentOutOfRangeException>(() => EditorVisualQaCommands.GetFrameDebuggerEvents(new FrameDebuggerEventsInput
            {
                startIndex = -1
            }));
        }

        private string CreateTempAssetFolder()
        {
            var folderName = "TempUnityMcpVisualTests_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var guid = AssetDatabase.CreateFolder("Assets", folderName);
            var path = AssetDatabase.GUIDToAssetPath(guid);
            _tempAssetFolders.Add(path);
            return path;
        }

        private string CreateTempDiskDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "unity-mcp-visual-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            _tempDiskDirs.Add(path);
            return path;
        }

        private void TrackTempFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                _tempDiskFiles.Add(path);
        }
    }
}
