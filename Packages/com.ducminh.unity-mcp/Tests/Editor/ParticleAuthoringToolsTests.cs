using System;
using System.Collections.Generic;
using System.Linq;
using DucMinh.UnityMcp.Editor;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class ParticleAuthoringToolsTests
    {
        private GameObject _target;
        private readonly List<string> _tempAssetFolders = new List<string>();

        [TearDown]
        public void TearDown()
        {
            if (_target != null)
            {
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }

            foreach (var folder in _tempAssetFolders)
            {
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            }
            _tempAssetFolders.Clear();
        }

        [Test]
        public void AllFiveParticleCommands_RegisterCleanlyInCommandRegistry()
        {
            var particleCommands = CommandRegistry.DiscoverCommands()
                .Where(c => string.Equals(c.Group, "particle", StringComparison.Ordinal))
                .ToDictionary(c => c.Name, StringComparer.Ordinal);

            Assert.That(particleCommands.Keys, Is.EquivalentTo(new[]
            {
                "create",
                "get",
                "configure",
                "preview",
                "save_prefab"
            }));
            Assert.That(particleCommands.ContainsKey("set"), Is.False);

            Assert.That(particleCommands["create"].Effect, Is.EqualTo(CommandEffect.Additive));
            Assert.That(particleCommands["create"].RuntimeOnly, Is.True);
            Assert.That(particleCommands["create"].Package, Is.EqualTo("DucMinh.UnityMcp.Runtime"));

            Assert.That(particleCommands["get"].Effect, Is.EqualTo(CommandEffect.ReadOnly));
            Assert.That(particleCommands["get"].RuntimeOnly, Is.True);
            Assert.That(particleCommands["get"].Package, Is.EqualTo("DucMinh.UnityMcp.Runtime"));

            Assert.That(particleCommands["configure"].Effect, Is.EqualTo(CommandEffect.Idempotent));
            Assert.That(particleCommands["configure"].RuntimeOnly, Is.True);
            Assert.That(particleCommands["configure"].Package, Is.EqualTo("DucMinh.UnityMcp.Runtime"));

            Assert.That(particleCommands["preview"].Effect, Is.EqualTo(CommandEffect.Idempotent));
            Assert.That(particleCommands["preview"].RuntimeOnly, Is.True);
            Assert.That(particleCommands["preview"].Package, Is.EqualTo("DucMinh.UnityMcp.Runtime"));

            Assert.That(particleCommands["save_prefab"].Effect, Is.EqualTo(CommandEffect.Destructive));
            Assert.That(particleCommands["save_prefab"].RuntimeOnly, Is.False);
            Assert.That(particleCommands["save_prefab"].Package, Is.EqualTo("DucMinh.UnityMcp.Editor"));

            var saveParams = particleCommands["save_prefab"].Parameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
            Assert.That(saveParams["asset-path"].Required, Is.True);
            Assert.That(saveParams.ContainsKey("overwrite"), Is.True);
            Assert.That(saveParams.ContainsKey("confirm"), Is.True);
            Assert.That(saveParams.ContainsKey("dry-run"), Is.True);
        }

        [Test]
        public void ParticleCreate_RespectsDryRun_AndCreatesConfiguredSystem()
        {
            var uniqueName = "UnityMcpParticleCreate_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var dryResult = ParticleCommands.Create(new ParticleCreateInput
            {
                name = uniqueName,
                duration = 2f,
                startLifetime = 0.75f,
                startSpeed = 4f,
                startSize = 1.5f,
                looping = false,
                maxParticles = 64
            }, dryRun: true);

            Assert.That(dryResult.dryRun, Is.True);
            Assert.That(dryResult.changed, Is.False);
            Assert.That(GameObject.Find(uniqueName), Is.Null);

            var liveResult = ParticleCommands.Create(new ParticleCreateInput
            {
                name = uniqueName,
                duration = 2f,
                startLifetime = 0.75f,
                startSpeed = 4f,
                startSize = 1.5f,
                startColor = Color.green,
                looping = false,
                maxParticles = 64,
                playOnAwake = false
            }, dryRun: false);

            _target = GameObject.Find(uniqueName);
            Assert.That(liveResult.dryRun, Is.False);
            Assert.That(liveResult.changed, Is.True);
            Assert.That(_target, Is.Not.Null);

            var system = _target.GetComponent<ParticleSystem>();
            Assert.That(system, Is.Not.Null);
            Assert.That(system.main.duration, Is.EqualTo(2f));
            Assert.That(system.main.startLifetime.constant, Is.EqualTo(0.75f));
            Assert.That(system.main.startSpeed.constant, Is.EqualTo(4f));
            Assert.That(system.main.startSize.constant, Is.EqualTo(1.5f));
            Assert.That(system.main.loop, Is.False);
            Assert.That(system.main.maxParticles, Is.EqualTo(64));
            Assert.That(system.main.playOnAwake, Is.False);
        }

        [Test]
        public void ParticleConfigure_UpdatesTypedModules_AndParticleGetReportsThem()
        {
            _target = new GameObject("UnityMcpParticleConfigure");
            var system = _target.AddComponent<ParticleSystem>();
            var id = UnityMcpObjectId.Get(_target);

            var dryResult = ParticleCommands.Configure(new ParticleConfigureInput
            {
                instanceId = id,
                componentIndex = 0,
                rateOverTime = 24f,
                emissionEnabled = true
            }, dryRun: true);

            Assert.That(dryResult.dryRun, Is.True);
            Assert.That(dryResult.changed, Is.False);

            var liveResult = ParticleCommands.Configure(new ParticleConfigureInput
            {
                instanceId = id,
                componentIndex = 0,
                rateOverTime = 12f,
                emissionEnabled = true,
                shapeEnabled = true,
                shapeType = "Cone",
                shapeRadius = 2f,
                velocityOverLifetimeEnabled = true,
                velocityOverLifetime = new Vector3(1f, 2f, 3f),
                colorOverLifetimeEnabled = true,
                colorOverLifetimeColor = Color.red,
                sizeOverLifetimeEnabled = true,
                sizeOverLifetimeMultiplier = 0.5f,
                noiseEnabled = true,
                noiseStrength = 0.3f,
                trailsEnabled = true,
                trailsRatio = 0.5f,
                collisionEnabled = true,
                collisionType = "World",
                renderMode = "Billboard",
                sortingOrder = 7
            }, dryRun: false);

            Assert.That(liveResult.dryRun, Is.False);
            Assert.That(liveResult.changed, Is.True);

            var output = ParticleCommands.Get(id, null, componentIndex: 0);
            Assert.That(output.instanceId, Is.EqualTo(UnityMcpObjectId.Get(system)));
            Assert.That(output.rateOverTime, Is.EqualTo(12f));
            Assert.That(output.shapeType, Is.EqualTo("Cone"));
            Assert.That(output.shapeRadius, Is.EqualTo(2f));
            Assert.That(output.velocityOverLifetimeEnabled, Is.True);
            Assert.That(output.colorOverLifetimeEnabled, Is.True);
            Assert.That(output.sizeOverLifetimeEnabled, Is.True);
            Assert.That(output.noiseEnabled, Is.True);
            Assert.That(output.trailsEnabled, Is.True);
            Assert.That(output.collisionEnabled, Is.True);
            Assert.That(output.sortingOrder, Is.EqualTo(7));
        }

        [Test]
        public void ParticlePreview_SimulatesPlaysAndStops_WithDryRunGuard()
        {
            _target = new GameObject("UnityMcpParticlePreview");
            var system = _target.AddComponent<ParticleSystem>();
            var id = UnityMcpObjectId.Get(_target);

            var dryPreview = ParticleCommands.Preview(new ParticlePreviewInput
            {
                instanceId = id,
                action = "play"
            }, dryRun: true);
            Assert.That(dryPreview.dryRun, Is.True);
            Assert.That(dryPreview.changed, Is.False);
            Assert.That(system.isPlaying, Is.False);

            var simulateResult = ParticleCommands.Preview(new ParticlePreviewInput
            {
                instanceId = id,
                action = "simulate",
                simulateTime = 0.2f,
                withChildren = true
            }, dryRun: false);
            Assert.That(simulateResult.dryRun, Is.False);
            Assert.That(simulateResult.changed, Is.True);

            var stopResult = ParticleCommands.Preview(new ParticlePreviewInput
            {
                instanceId = id,
                action = "stop"
            }, dryRun: false);
            Assert.That(stopResult.changed, Is.True);
            Assert.That(system.isPlaying, Is.False);

            Assert.Throws<ArgumentException>(() => ParticleCommands.Preview(new ParticlePreviewInput
            {
                instanceId = id,
                action = "invalid-action"
            }));
        }

        [Test]
        public void SavePrefab_RespectsDryRunOverwriteAndConfirmGuards()
        {
            _target = new GameObject("UnityMcpParticlePrefabSource");
            _target.AddComponent<ParticleSystem>();
            var id = UnityMcpObjectId.Get(_target);

            var folder = CreateTempAssetFolder();
            var prefabPath = folder + "/SavedParticle.prefab";

            // 1. Dry-run create -> no prefab written
            var dryCreate = EditorParticleCommands.SavePrefab(new ParticlePrefabSaveInput
            {
                instanceId = id,
                assetPath = prefabPath,
                dryRun = true
            });
            Assert.That(dryCreate.dryRun, Is.True);
            Assert.That(dryCreate.changed, Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), Is.Null);

            // 2. Live create -> prefab created
            var liveCreate = EditorParticleCommands.SavePrefab(new ParticlePrefabSaveInput
            {
                instanceId = id,
                assetPath = prefabPath,
                dryRun = false
            });
            Assert.That(liveCreate.dryRun, Is.False);
            Assert.That(liveCreate.changed, Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), Is.Not.Null);

            // 3. Existing prefab without overwrite -> throws
            Assert.Throws<InvalidOperationException>(() => EditorParticleCommands.SavePrefab(new ParticlePrefabSaveInput
            {
                instanceId = id,
                assetPath = prefabPath,
                overwrite = false,
                confirm = false,
                dryRun = false
            }));

            // 4. Existing prefab with overwrite=true, confirm=false, dryRun=true -> preview succeeds
            var dryOverwrite = EditorParticleCommands.SavePrefab(new ParticlePrefabSaveInput
            {
                instanceId = id,
                assetPath = prefabPath,
                overwrite = true,
                confirm = false,
                dryRun = true
            });
            Assert.That(dryOverwrite.dryRun, Is.True);
            Assert.That(dryOverwrite.changed, Is.False);

            // 5. Existing prefab with overwrite=true, confirm=false, dryRun=false -> throws
            Assert.Throws<InvalidOperationException>(() => EditorParticleCommands.SavePrefab(new ParticlePrefabSaveInput
            {
                instanceId = id,
                assetPath = prefabPath,
                overwrite = true,
                confirm = false,
                dryRun = false
            }));

            // 6. Existing prefab with overwrite=true, confirm=true, dryRun=false -> succeeds
            var confirmedOverwrite = EditorParticleCommands.SavePrefab(new ParticlePrefabSaveInput
            {
                instanceId = id,
                assetPath = prefabPath,
                overwrite = true,
                confirm = true,
                dryRun = false
            });
            Assert.That(confirmedOverwrite.dryRun, Is.False);
            Assert.That(confirmedOverwrite.changed, Is.True);
            Assert.That(confirmedOverwrite.journal.Count, Is.EqualTo(1));
            Assert.That(confirmedOverwrite.journal[0].operation, Is.EqualTo("overwrite-particle-prefab"));
        }

        private string CreateTempAssetFolder()
        {
            var folderName = "TempUnityMcpParticleTests_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var guid = AssetDatabase.CreateFolder("Assets", folderName);
            var path = AssetDatabase.GUIDToAssetPath(guid);
            _tempAssetFolders.Add(path);
            return path;
        }
    }
}
