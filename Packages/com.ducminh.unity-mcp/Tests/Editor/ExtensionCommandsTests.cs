using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class ExtensionCommandsTests
    {
        private static readonly HashSet<string> ReservedArgNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "format",
            "json",
            "fields",
            "verbose",
            "omit-nulls"
        };

        private static readonly HashSet<string> CanonicalBuiltInPipelineGroups = new HashSet<string>(StringComparer.Ordinal)
        {
            "animation",
            "asset",
            "authoring",
            "build",
            "capture",
            "component",
            "console",
            "diagnostics",
            "editor",
            "gameobject",
            "job",
            "lighting",
            "material",
            "navmesh",
            "occlusion",
            "package",
            "pipeline",
            "prefab",
            "runtime",
            "scene",
            "script",
            "settings",
            "test"
        };

        private readonly List<GameObject> _tempObjects = new List<GameObject>();
        private readonly List<string> _tempAssetFolders = new List<string>();
        private readonly List<string> _tempDiskFiles = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _tempObjects)
            {
                if (obj != null)
                    UnityEngine.Object.DestroyImmediate(obj);
            }
            _tempObjects.Clear();

            foreach (var folder in _tempAssetFolders)
            {
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
            _tempAssetFolders.Clear();

            foreach (var file in _tempDiskFiles)
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    File.Delete(file);
            }
            _tempDiskFiles.Clear();
        }

        [Test]
        public void CommandRegistry_DiscoversAllExtensionCommands_WithZeroDuplicatesReservedArgsOrGroupCollisions()
        {
            var allCommands = CommandRegistry.DiscoverCommands().ToList();
            Assert.That(allCommands, Is.Not.Empty);

            // 1. Zero duplicate (group, name) across the entire registry
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cmd in allCommands)
            {
                var key = $"{cmd.Group ?? string.Empty}::{cmd.Name}";
                Assert.That(seenKeys.Add(key), Is.True, $"Duplicate command registered: '{cmd.FullName}'.");
            }

            // 2. Partition built-in Unity.Pipeline* commands vs DucMinh.UnityMcp.* extension commands
            var builtInGroups = new HashSet<string>(CanonicalBuiltInPipelineGroups, StringComparer.Ordinal);
            foreach (var cmd in allCommands)
            {
                if (!string.IsNullOrEmpty(cmd.Package)
                    && (string.Equals(cmd.Package, "Unity.Pipeline", StringComparison.Ordinal)
                        || cmd.Package.StartsWith("Unity.Pipeline.", StringComparison.Ordinal))
                    && !string.IsNullOrEmpty(cmd.Group))
                {
                    builtInGroups.Add(cmd.Group);
                }
            }

            var extensionCommands = allCommands
                .Where(c => string.Equals(c.Package, "DucMinh.UnityMcp.Runtime", StringComparison.Ordinal)
                         || string.Equals(c.Package, "DucMinh.UnityMcp.Editor", StringComparison.Ordinal))
                .ToList();

            var extensionByFullName = extensionCommands.ToDictionary(c => c.FullName, StringComparer.Ordinal);

            // 3. Zero collision with built-in groups & zero reserved arg names on extension commands
            foreach (var cmd in extensionCommands)
            {
                Assert.That(cmd.Group, Is.Not.Null.And.Not.Empty, $"Extension command '{cmd.FullName}' has no group.");
                Assert.That(builtInGroups.Contains(cmd.Group), Is.False,
                    $"Extension group '{cmd.Group}' collides with built-in com.unity.pipeline group.");

                foreach (var param in cmd.Parameters)
                {
                    Assert.That(ReservedArgNames.Contains(param.Name), Is.False,
                        $"Command '{cmd.FullName}' declares reserved CLI arg '{param.Name}'.");
                }
            }

            // 4. Always-on extension commands (29 across 10 groups)
            var expectedCommands = new List<string>
            {
                // particle (5)
                "particle create",
                "particle get",
                "particle configure",
                "particle preview",
                "particle save_prefab",
                // physics_ext (4)
                "physics_ext get_collision_matrix",
                "physics_ext set_collision_matrix",
                "physics_ext raycast",
                "physics_ext overlap",
                // navmesh_ext (1)
                "navmesh_ext calculate_path",
                // uitoolkit (4)
                "uitoolkit scan",
                "uitoolkit edit_uxml",
                "uitoolkit edit_uss",
                "uitoolkit scaffold_controller",
                // ugui (1)
                "ugui raycast",
                // scene_ext (2)
                "scene_ext validate",
                "scene_ext analyze_complexity",
                // asset_ext (2)
                "asset_ext get_dependencies",
                "asset_ext find_references",
                // render_ext (3)
                "render_ext get_settings",
                "render_ext set_settings",
                "render_ext generate_texture",
                // visual_ext (3)
                "visual_ext capture_gameobject",
                "visual_ext capture_multiview",
                "visual_ext frame_debugger_events",
                // checkpoint (4)
                "checkpoint create",
                "checkpoint list",
                "checkpoint diff",
                "checkpoint restore"
            };

#if DM_ADDRESSABLES_PRESENT
            expectedCommands.AddRange(new[]
            {
                "addressables list_groups",
                "addressables create_group",
                "addressables add_entry",
                "addressables remove_entry",
                "addressables build"
            });
#endif
#if DM_LOCALIZATION_PRESENT
            expectedCommands.AddRange(new[]
            {
                "localization list_tables",
                "localization get_entry",
                "localization set_entry"
            });
#endif
#if DM_TERRAIN_PRESENT
            expectedCommands.AddRange(new[]
            {
                "terrain set_height",
                "terrain paint_texture"
            });
#endif
#if DM_2D_SPRITE_PRESENT
            expectedCommands.Add("sprite slice");
#endif
#if DM_TILEMAP_PRESENT
            expectedCommands.Add("tilemap set_tiles");
#endif
#if DM_PROBUILDER_PRESENT
            expectedCommands.AddRange(new[] { "probuilder create", "probuilder edit" });
#endif
#if DM_CINEMACHINE_PRESENT
            expectedCommands.Add("cinemachine create");
#endif
#if DM_VFXGRAPH_PRESENT
            expectedCommands.Add("vfxgraph set");
#endif
#if DM_INPUTSYSTEM_PRESENT
            expectedCommands.AddRange(new[] { "input_actions get", "input_actions create_action" });
#endif

            foreach (var expected in expectedCommands)
            {
                Assert.That(extensionByFullName.ContainsKey(expected), Is.True,
                    $"Expected extension command '{expected}' was not discovered by CommandRegistry.");
            }

            Assert.That(extensionCommands.Count, Is.EqualTo(expectedCommands.Count));

            // 5. Verify RuntimeOnly surface separation
            var expectedRuntimeCommands = new HashSet<string>(StringComparer.Ordinal)
            {
                "particle create",
                "particle get",
                "particle configure",
                "particle preview",
                "physics_ext get_collision_matrix",
                "physics_ext set_collision_matrix",
                "physics_ext raycast",
                "physics_ext overlap",
                "navmesh_ext calculate_path"
            };

            foreach (var cmd in extensionCommands)
            {
                var isRuntime = expectedRuntimeCommands.Contains(cmd.FullName);
                Assert.That(cmd.RuntimeOnly, Is.EqualTo(isRuntime), $"Unexpected RuntimeOnly flag on '{cmd.FullName}'.");
                Assert.That(cmd.Package, Is.EqualTo(isRuntime ? "DucMinh.UnityMcp.Runtime" : "DucMinh.UnityMcp.Editor"));
            }
        }

        [Test]
        public void PhysicsQueryCommands_MatrixRaycastAndOverlap_WorkAndEnforceGuards()
        {
            var matrix = PhysicsQueryCommands.GetCollisionMatrix();
            Assert.That(matrix.layers.Count, Is.EqualTo(32));

            var originalIgnore = Physics.GetIgnoreLayerCollision(0, 1);
            try
            {
                var drySet = PhysicsQueryCommands.SetCollisionMatrix(0, 1, !originalIgnore, dryRun: true);
                Assert.That(drySet.dryRun, Is.True);
                Assert.That(drySet.changed, Is.False);
                Assert.That(Physics.GetIgnoreLayerCollision(0, 1), Is.EqualTo(originalIgnore));

                var liveSet = PhysicsQueryCommands.SetCollisionMatrix(0, 1, !originalIgnore, dryRun: false);
                Assert.That(liveSet.dryRun, Is.False);
                Assert.That(liveSet.changed, Is.True);
                Assert.That(Physics.GetIgnoreLayerCollision(0, 1), Is.EqualTo(!originalIgnore));

                Assert.Throws<ArgumentOutOfRangeException>(() => PhysicsQueryCommands.SetCollisionMatrix(-1, 0, true));
                Assert.Throws<ArgumentOutOfRangeException>(() => PhysicsQueryCommands.SetCollisionMatrix(0, 32, true));
            }
            finally
            {
                Physics.IgnoreLayerCollision(0, 1, originalIgnore);
            }

            var target = new GameObject("UnityMcpPhysicsBox_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            _tempObjects.Add(target);
            target.transform.position = new Vector3(0f, 0f, 5f);
            target.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            var rayHit = PhysicsQueryCommands.Raycast(new PhysicsRaycastInput
            {
                origin = Vector3.zero,
                direction = Vector3.forward,
                maxDistance = 20f
            });
            Assert.That(rayHit.hit, Is.True);
            Assert.That(rayHit.gameObjectPath, Does.Contain(target.name));
            Assert.That(rayHit.distance, Is.GreaterThan(0f));

            Assert.Throws<ArgumentException>(() => PhysicsQueryCommands.Raycast(new PhysicsRaycastInput
            {
                origin = Vector3.zero,
                direction = Vector3.zero
            }));

            var sphereOverlap = PhysicsQueryCommands.Overlap(new PhysicsOverlapInput
            {
                shape = "sphere",
                center = new Vector3(0f, 0f, 5f),
                radius = 2f
            });
            Assert.That(sphereOverlap.matches.Any(m => m.name == target.name), Is.True);

            var boxOverlap = PhysicsQueryCommands.Overlap(new PhysicsOverlapInput
            {
                shape = "box",
                center = new Vector3(0f, 0f, 5f),
                halfExtents = new Vector3(1.5f, 1.5f, 1.5f)
            });
            Assert.That(boxOverlap.matches.Any(m => m.name == target.name), Is.True);

            Assert.Throws<ArgumentException>(() => PhysicsQueryCommands.Overlap(new PhysicsOverlapInput
            {
                shape = "capsule",
                center = Vector3.zero
            }));
        }

        [Test]
        public void RuntimeNavigationTools_CalculatePath_ValidatesInputsAndReturnsStatus()
        {
            var result = RuntimeNavigationTools.CalculatePath(new NavMeshPathCalculateInput
            {
                source = Vector3.zero,
                destination = new Vector3(2f, 0f, 2f),
                areaMask = -1,
                maxCorners = 64
            });

            Assert.That(result, Is.Not.Null);
            Assert.That(new[] { "complete", "partial", "invalid" }, Does.Contain(result.status));
            Assert.That(result.corners, Is.Not.Null);

            Assert.Throws<ArgumentException>(() => RuntimeNavigationTools.CalculatePath(new NavMeshPathCalculateInput
            {
                source = Vector3.zero,
                destination = Vector3.one,
                areaMask = 0
            }));
            Assert.Throws<ArgumentException>(() => RuntimeNavigationTools.CalculatePath(new NavMeshPathCalculateInput
            {
                source = Vector3.zero,
                destination = Vector3.one,
                maxCorners = 0
            }));
            Assert.Throws<ArgumentException>(() => RuntimeNavigationTools.CalculatePath(new NavMeshPathCalculateInput
            {
                source = new Vector3(float.NaN, 0f, 0f),
                destination = Vector3.one
            }));
        }

        [Test]
        public void EditorSceneAnalysisCommands_ValidateAndAnalyzeComplexity_WorkOnActiveScene()
        {
            var duplicateName = "UnityMcpDupRoot_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var root1 = new GameObject(duplicateName);
            var root2 = new GameObject(duplicateName);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "UnityMcpMeshCube_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            _tempObjects.Add(root1);
            _tempObjects.Add(root2);
            _tempObjects.Add(cube);

            var referencesResult = EditorSceneAnalysisCommands.Validate(new SceneValidationInput
            {
                profile = "references",
                includeInactive = true,
                maxIssues = 100
            });
            Assert.That(referencesResult.profile, Is.EqualTo("references"));
            Assert.That(referencesResult.objectsScanned, Is.GreaterThanOrEqualTo(3));
            Assert.That(referencesResult.componentsScanned, Is.GreaterThanOrEqualTo(3));

            var lifecycleResult = EditorSceneAnalysisCommands.Validate(new SceneValidationInput
            {
                profile = "lifecycle",
                includeInactive = true,
                maxIssues = 100
            });
            Assert.That(lifecycleResult.issues.Any(i => i.kind == "duplicate-root-name"), Is.True);

            Assert.Throws<ArgumentException>(() => EditorSceneAnalysisCommands.Validate(new SceneValidationInput
            {
                profile = "unknown"
            }));

            var complexity = EditorSceneAnalysisCommands.AnalyzeComplexity(new SceneComplexityInput
            {
                includeInactive = true
            });
            Assert.That(complexity.scenes, Is.Not.Empty);
            var activeStats = complexity.scenes[0];
            Assert.That(activeStats.gameObjectCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(activeStats.meshRendererCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(activeStats.uniqueMeshCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(activeStats.uniqueMeshTriangles, Is.GreaterThan(0));
        }

        [Test]
        public void EditorAssetAnalysisCommands_GetDependenciesAndFindReferences_ResolvePrefabMaterialLink()
        {
            var folder = CreateTempAssetFolder();
            var matPath = folder + "/TestMaterial.mat";
            var prefabPath = folder + "/TestCube.prefab";

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null);

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, matPath);

            var tempGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                tempGo.GetComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(tempGo, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempGo);
            }

            AssetDatabase.SaveAssets();

            var deps = EditorAssetAnalysisCommands.GetDependencies(new AssetDependenciesInput
            {
                path = prefabPath,
                recursive = true
            });
            Assert.That(deps.path, Is.EqualTo(prefabPath));
            Assert.That(deps.dependencies, Does.Contain(matPath));
            Assert.That(deps.dependencies, Does.Not.Contain(prefabPath));

            var refs = EditorAssetAnalysisCommands.FindReferences(new AssetReferencesInput
            {
                path = matPath,
                limit = 50
            });
            Assert.That(refs.path, Is.EqualTo(matPath));
            Assert.That(refs.references.Any(r => string.Equals(r.path, prefabPath, StringComparison.OrdinalIgnoreCase)), Is.True);

            Assert.Throws<ArgumentException>(() => EditorAssetAnalysisCommands.GetDependencies(new AssetDependenciesInput
            {
                path = folder + "/DoesNotExist.mat"
            }));
        }

        [Test]
        public void EditorPackageGatedTools_UiToolkitScanEditAndScaffold_WorkWithRevisionAndDryRun()
        {
            var folder = CreateTempAssetFolder();
            var uxmlPath = folder + "/TestView.uxml";
            var ussPath = folder + "/TestStyles.uss";
            var controllerPath = folder + "/TestUiToolkitController.cs";

            const string uxmlSource = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n"
                + "  <Style src=\"TestStyles.uss\" />\n"
                + "  <ui:VisualElement name=\"root\" class=\"container\" />\n"
                + "</ui:UXML>\n";
            const string ussSource = ".container {\n  flex-grow: 1;\n}\n";

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var fullUxmlPath = Path.Combine(projectRoot, uxmlPath.Replace('/', Path.DirectorySeparatorChar));
            var fullUssPath = Path.Combine(projectRoot, ussPath.Replace('/', Path.DirectorySeparatorChar));
            var fullControllerPath = Path.Combine(projectRoot, controllerPath.Replace('/', Path.DirectorySeparatorChar));

            File.WriteAllText(fullUxmlPath, uxmlSource, new UTF8Encoding(false));
            File.WriteAllText(fullUssPath, ussSource, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ussPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(uxmlPath, ImportAssetOptions.ForceSynchronousImport);

            // 1. Scan UXML
            var scan = EditorPackageGatedTools.ScanCore(uxmlPath);
            Assert.That(scan.path, Is.EqualTo(uxmlPath));
            Assert.That(scan.revision, Is.EqualTo(Sha256Hex(File.ReadAllBytes(fullUxmlPath))));
            Assert.That(scan.styleSheets, Does.Contain("TestStyles.uss"));
            Assert.That(scan.elements.Any(e => e.name == "root" && e.classes.Contains("container")), Is.True);

            // 2. Edit UXML (dry-run then live)
            var idx = uxmlSource.IndexOf("name=\"root\"", StringComparison.Ordinal);
            var uxmlEdits = new List<UiToolkitTextEdit>
            {
                new UiToolkitTextEdit { startOffset = idx, endOffset = idx + "name=\"root\"".Length, newText = "name=\"panel\"" }
            };

            var dryUxmlEdit = EditorPackageGatedTools.EditUxmlCore(uxmlPath, scan.revision, uxmlEdits, dryRun: true);
            Assert.That(dryUxmlEdit.dryRun, Is.True);
            Assert.That(dryUxmlEdit.changed, Is.False);
            Assert.That(File.ReadAllText(fullUxmlPath), Is.EqualTo(uxmlSource));

            var liveUxmlEdit = EditorPackageGatedTools.EditUxmlCore(uxmlPath, scan.revision, uxmlEdits, dryRun: false);
            Assert.That(liveUxmlEdit.dryRun, Is.False);
            Assert.That(liveUxmlEdit.changed, Is.True);
            Assert.That(liveUxmlEdit.revisionAfter, Is.Not.EqualTo(liveUxmlEdit.revisionBefore));

            var rescan = EditorPackageGatedTools.ScanCore(uxmlPath);
            Assert.That(rescan.revision, Is.EqualTo(liveUxmlEdit.revisionAfter));
            Assert.That(rescan.elements.Any(e => e.name == "panel"), Is.True);

            // Stale revision rejected
            Assert.Throws<InvalidOperationException>(() =>
                EditorPackageGatedTools.EditUxmlCore(uxmlPath, scan.revision, uxmlEdits, dryRun: true));

            // 3. Edit USS (dry-run then live, plus unbalanced brace rejection)
            var ussRevision = Sha256Hex(File.ReadAllBytes(fullUssPath));
            var growIdx = ussSource.IndexOf("flex-grow: 1;", StringComparison.Ordinal);
            var ussEdits = new List<UiToolkitTextEdit>
            {
                new UiToolkitTextEdit { startOffset = growIdx, endOffset = growIdx + "flex-grow: 1;".Length, newText = "flex-grow: 2;" }
            };

            var dryUssEdit = EditorPackageGatedTools.EditUssCore(ussPath, ussRevision, ussEdits, dryRun: true);
            Assert.That(dryUssEdit.dryRun, Is.True);
            Assert.That(dryUssEdit.changed, Is.False);
            Assert.That(File.ReadAllText(fullUssPath), Is.EqualTo(ussSource));

            var liveUssEdit = EditorPackageGatedTools.EditUssCore(ussPath, ussRevision, ussEdits, dryRun: false);
            Assert.That(liveUssEdit.dryRun, Is.False);
            Assert.That(liveUssEdit.changed, Is.True);
            StringAssert.Contains("flex-grow: 2;", File.ReadAllText(fullUssPath));

            Assert.Throws<ArgumentException>(() => EditorPackageGatedTools.EditUssCore(
                ussPath,
                liveUssEdit.revisionAfter,
                new List<UiToolkitTextEdit> { new UiToolkitTextEdit { startOffset = 0, endOffset = 0, newText = "{ " } },
                dryRun: true));

            // 4. Scaffold controller (dry-run then live)
            var dryScaffold = EditorPackageGatedTools.ScaffoldControllerCore(
                controllerPath,
                "TestUiToolkitController",
                uxmlPath,
                "TestUiToolkit",
                new List<string> { "titleLabel" },
                dryRun: true);
            Assert.That(dryScaffold.dryRun, Is.True);
            Assert.That(dryScaffold.created, Is.False);
            Assert.That(File.Exists(fullControllerPath), Is.False);

            var liveScaffold = EditorPackageGatedTools.ScaffoldControllerCore(
                controllerPath,
                "TestUiToolkitController",
                uxmlPath,
                "TestUiToolkit",
                new List<string> { "titleLabel" },
                dryRun: false);
            Assert.That(liveScaffold.dryRun, Is.False);
            Assert.That(liveScaffold.created, Is.True);
            Assert.That(File.Exists(fullControllerPath), Is.True);
            var csText = File.ReadAllText(fullControllerPath);
            StringAssert.Contains("namespace TestUiToolkit", csText);
            StringAssert.Contains("public sealed class TestUiToolkitController : MonoBehaviour", csText);
            StringAssert.Contains("root.Q<VisualElement>(\"titleLabel\")", csText);
        }

        [Test]
        public void EditorUguiCommands_RaycastAndParseVector2_WorkOnCanvasHierarchy()
        {
            Assert.That(EditorUguiCommands.ParseVector2("10,20"), Is.EqualTo(new Vector2(10f, 20f)));
            Assert.That(EditorUguiCommands.ParseVector2("[12.5, 25]"), Is.EqualTo(new Vector2(12.5f, 25f)));
            Assert.That(EditorUguiCommands.ParseVector2("{\"x\": 3, \"y\": 4}"), Is.EqualTo(new Vector2(3f, 4f)));
            Assert.Throws<ArgumentException>(() => EditorUguiCommands.ParseVector2("10"));

            var canvasGo = new GameObject("UnityMcpUguiCanvas", typeof(RectTransform), typeof(Canvas));
            _tempObjects.Add(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var childGo = new GameObject("HitPanel", typeof(RectTransform));
            childGo.transform.SetParent(canvasGo.transform, false);
            var childRect = childGo.GetComponent<RectTransform>();
            childRect.anchorMin = Vector2.zero;
            childRect.anchorMax = Vector2.one;
            childRect.offsetMin = Vector2.zero;
            childRect.offsetMax = Vector2.zero;

            var inactiveGo = new GameObject("InactivePanel", typeof(RectTransform));
            inactiveGo.transform.SetParent(canvasGo.transform, false);
            var inactiveRect = inactiveGo.GetComponent<RectTransform>();
            inactiveRect.anchorMin = Vector2.zero;
            inactiveRect.anchorMax = Vector2.one;
            inactiveRect.offsetMin = Vector2.zero;
            inactiveRect.offsetMax = Vector2.zero;
            inactiveGo.SetActive(false);

            Canvas.ForceUpdateCanvases();

            var canvasId = UnityMcpObjectId.Get(canvasGo);
            var activeOnly = EditorUguiCommands.Raycast(new UiRaycastInput
            {
                screenPosition = new Vector2(10f, 10f),
                rootInstanceId = canvasId,
                includeInactive = false
            });
            Assert.That(activeOnly.hits.Any(h => h.name == "HitPanel"), Is.True);
            Assert.That(activeOnly.hits.Any(h => h.name == "InactivePanel"), Is.False);

            var withInactive = EditorUguiCommands.Raycast(new UiRaycastInput
            {
                screenPosition = new Vector2(10f, 10f),
                rootInstanceId = canvasId,
                includeInactive = true
            });
            Assert.That(withInactive.hits.Any(h => h.name == "InactivePanel"), Is.True);
        }

        [Test]
        public void EditorCheckpointCommands_CreateListDiffAndRestore_EnforceGuards()
        {
            var folder = CreateTempAssetFolder();
            var scenePath = folder + "/CheckpointTestScene.unity";

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var fullScenePath = Path.Combine(projectRoot, scenePath.Replace('/', Path.DirectorySeparatorChar));
            const string minimalSceneYaml = "%YAML 1.1\n"
                + "%TAG !u! tag:unity3d.com,2011:\n"
                + "--- !u!29 &1\n"
                + "OcclusionCullingSettings:\n"
                + "  m_ObjectHideFlags: 0\n"
                + "  serializedVersion: 2\n"
                + "--- !u!104 &2\n"
                + "RenderSettings:\n"
                + "  m_ObjectHideFlags: 0\n"
                + "  serializedVersion: 10\n";
            File.WriteAllText(fullScenePath, minimalSceneYaml, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);

            var checkpointDir = Path.Combine(projectRoot, "Library", "UnityMCP", "checkpoints");
            var checkpointId = "mcp-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            TrackTempFile(Path.Combine(checkpointDir, checkpointId + ".json"));
            TrackTempFile(Path.Combine(checkpointDir, checkpointId + ".unity"));

            // 1. Dry-run create writes nothing
            var dryCreate = EditorCheckpointCommands.Create(new CheckpointCreateInput
            {
                scenePath = scenePath,
                checkpointId = checkpointId,
                dryRun = true
            });
            Assert.That(dryCreate.dryRun, Is.True);
            Assert.That(dryCreate.created, Is.False);
            Assert.That(File.Exists(Path.Combine(checkpointDir, checkpointId + ".json")), Is.False);

            // 2. Live create
            var liveCreate = EditorCheckpointCommands.Create(new CheckpointCreateInput
            {
                scenePath = scenePath,
                checkpointId = checkpointId,
                dryRun = false
            });
            Assert.That(liveCreate.dryRun, Is.False);
            Assert.That(liveCreate.created, Is.True);
            Assert.That(liveCreate.checkpoint.snapshotExists, Is.True);

            // 3. List includes checkpoint
            var list = EditorCheckpointCommands.List(new CheckpointListInput { limit = 100 });
            Assert.That(list.checkpoints.Any(c => c.checkpointId == checkpointId), Is.True);

            // 4. Diff matches before mutation, detects changes after mutation
            var cleanDiff = EditorCheckpointCommands.Diff(new CheckpointDiffInput { checkpointId = checkpointId });
            Assert.That(cleanDiff.sameContent, Is.True);
            Assert.That(cleanDiff.lines, Is.Empty);

            File.AppendAllText(fullScenePath, "# unity-mcp checkpoint test edit\n");

            var dirtyDiff = EditorCheckpointCommands.Diff(new CheckpointDiffInput
            {
                checkpointId = checkpointId,
                maxLines = 20
            });
            Assert.That(dirtyDiff.sameContent, Is.False);
            Assert.That(dirtyDiff.lines, Is.Not.Empty);

            // 5. Restore guards: dryRun=true works without confirm; confirm=false without dryRun throws; confirm=true restores
            var dryRestore = EditorCheckpointCommands.Restore(new CheckpointRestoreInput
            {
                checkpointId = checkpointId,
                confirm = false,
                dryRun = true
            });
            Assert.That(dryRestore.dryRun, Is.True);
            Assert.That(dryRestore.restored, Is.False);

            Assert.Throws<InvalidOperationException>(() => EditorCheckpointCommands.Restore(new CheckpointRestoreInput
            {
                checkpointId = checkpointId,
                confirm = false,
                dryRun = false
            }));

            var liveRestore = EditorCheckpointCommands.Restore(new CheckpointRestoreInput
            {
                checkpointId = checkpointId,
                confirm = true,
                dryRun = false
            });
            TrackTempFile(Path.Combine(projectRoot, liveRestore.backupPath.Replace('/', Path.DirectorySeparatorChar)));

            Assert.That(liveRestore.dryRun, Is.False);
            Assert.That(liveRestore.restored, Is.True);

            var restoredDiff = EditorCheckpointCommands.Diff(new CheckpointDiffInput { checkpointId = checkpointId });
            Assert.That(restoredDiff.sameContent, Is.True);
        }

        private string CreateTempAssetFolder()
        {
            var folderName = "TempUnityMcpExtTests_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var guid = AssetDatabase.CreateFolder("Assets", folderName);
            var path = AssetDatabase.GUIDToAssetPath(guid);
            _tempAssetFolders.Add(path);
            return path;
        }

        private void TrackTempFile(string fullPath)
        {
            if (!string.IsNullOrWhiteSpace(fullPath))
                _tempDiskFiles.Add(fullPath);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
