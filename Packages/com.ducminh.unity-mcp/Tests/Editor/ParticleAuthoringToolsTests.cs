using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace DucMinh.UnityMcp.Tests
{
    public sealed class ParticleAuthoringToolsTests
    {
        private GameObject target;

        [TearDown]
        public void TearDown()
        {
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
        }

        [Test]
        public void ParticleCreate_CreatesConfiguredSystem()
        {
            RuntimeExpansionTools.ParticleCreate(new ParticleCreateInput
            {
                name = "UnityMcpParticleCreate",
                duration = 2f,
                startLifetime = 0.75f,
                startSpeed = 4f,
                startSize = 1.5f,
                looping = false,
                maxParticles = 64,
                apply = true
            }, Context("particle-create", false));

            target = GameObject.Find("UnityMcpParticleCreate");
            Assert.That(target, Is.Not.Null);
            var system = target.GetComponent<ParticleSystem>();
            Assert.That(system, Is.Not.Null);
            Assert.That(system.main.duration, Is.EqualTo(2f));
            Assert.That(system.main.startLifetime.constant, Is.EqualTo(0.75f));
            Assert.That(system.main.maxParticles, Is.EqualTo(64));
        }

        [Test]
        public void ParticleConfigure_UpdatesTypedModules_AndParticleGetReportsThem()
        {
            target = new GameObject("UnityMcpParticleConfigure");
            var system = target.AddComponent<ParticleSystem>();
            RuntimeExpansionTools.ParticleConfigure(new ParticleConfigureInput
            {
                instanceId = target.GetInstanceID(),
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
                sortingOrder = 7,
                apply = true
            }, Context("particle-configure", false));

            var output = RuntimeExpansionTools.ParticleGet(new ParticleSetInput { instanceId = target.GetInstanceID(), componentIndex = 0 });
            Assert.That(output.instanceId, Is.EqualTo(system.GetInstanceID()));
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

        private static UnityMcpContext Context(string toolName, bool dryRun)
        {
            var constructor = typeof(UnityMcpContext).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool), typeof(CancellationToken) }, null);
            Assert.That(constructor, Is.Not.Null);
            return (UnityMcpContext)constructor.Invoke(new object[] { toolName, dryRun, CancellationToken.None });
        }
    }
}
