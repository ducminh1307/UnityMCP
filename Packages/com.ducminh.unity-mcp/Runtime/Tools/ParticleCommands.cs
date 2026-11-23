using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [Serializable]
    public sealed class ParticleCreateInput
    {
        public string name;
        public int? parentInstanceId;
        public string parentPath;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3? localPosition;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3? localEulerAngles;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3? localScale;
        public float? duration;
        public float? startLifetime;
        public float? startSpeed;
        public float? startSize;
        [JsonConverter(typeof(RuntimeColorJsonConverter))]
        public Color? startColor;
        public bool? looping;
        public int? maxParticles;
        public bool playOnAwake = true;
    }

    [Serializable]
    public sealed class ParticleConfigureInput
    {
        public int? instanceId;
        public string path;
        public int componentIndex;
        public float? duration;
        public float? startDelay;
        public float? startLifetime;
        public float? startSpeed;
        public float? startSize;
        [JsonConverter(typeof(RuntimeColorJsonConverter))]
        public Color? startColor;
        public bool? looping;
        public bool? prewarm;
        public int? maxParticles;
        public bool? emissionEnabled;
        public float? rateOverTime;
        public bool? shapeEnabled;
        public string shapeType;
        public float? shapeRadius;
        public float? shapeAngle;
        public bool? velocityOverLifetimeEnabled;
        [JsonConverter(typeof(RuntimeVector3JsonConverter))]
        public Vector3? velocityOverLifetime;
        public bool? colorOverLifetimeEnabled;
        [JsonConverter(typeof(RuntimeColorJsonConverter))]
        public Color? colorOverLifetimeColor;
        public bool? sizeOverLifetimeEnabled;
        public float? sizeOverLifetimeMultiplier;
        public bool? noiseEnabled;
        public float? noiseStrength;
        public float? noiseFrequency;
        public float? noiseScrollSpeed;
        public bool? trailsEnabled;
        public float? trailsRatio;
        public float? trailsLifetime;
        public bool? collisionEnabled;
        public string collisionType;
        public float? collisionDampen;
        public string renderMode;
        public int? sortingOrder;
    }

    [Serializable]
    public sealed class ParticlePreviewInput
    {
        public int? instanceId;
        public string path;
        public int componentIndex;
        public string action;
        public float simulateTime;
        public bool withChildren = true;
    }

    [Serializable]
    public sealed class ParticleInfoOutput
    {
        public int instanceId;
        public string path;
        public bool isPlaying;
        public float duration;
        public float startLifetime;
        public float startSpeed;
        public float startSize;
        [JsonConverter(typeof(RuntimeColorJsonConverter))]
        public Color startColor;
        public bool looping;
        public int maxParticles;
        public bool emissionEnabled;
        public float rateOverTime;
        public bool shapeEnabled;
        public string shapeType;
        public float shapeRadius;
        public bool velocityOverLifetimeEnabled;
        public bool colorOverLifetimeEnabled;
        public bool sizeOverLifetimeEnabled;
        public bool noiseEnabled;
        public bool trailsEnabled;
        public bool collisionEnabled;
        public string renderMode;
        public int sortingOrder;
    }

    /// <summary>
    /// Runtime and Editor Shuriken ParticleSystem authoring and inspection commands.
    /// </summary>
    public static class ParticleCommands
    {
        public const string Group = "particle";

        private static readonly JsonSerializer ConfigureSerializer = JsonSerializer.CreateDefault(
            new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });

        [CliGroupCommand(
            Group,
            "Create and configure a Shuriken ParticleSystem GameObject.",
            Name = "create",
            Effect = CommandEffect.Additive,
            Tags = new[] { "runtime", "vfx" })]
        internal static ExitCode CreateCommand(
            CommandContext cmd,
            [CliArg("name", "Name for the created GameObject.", Required = true)] string name,
            [CliArg("parent-id", "Optional parent GameObject instance ID.")] int? parentId = null,
            [CliArg("parent-path", "Optional parent GameObject hierarchy path.")] string parentPath = null,
            [CliArg("position", "Optional local position as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":0}'.")] string position = null,
            [CliArg("rotation", "Optional local Euler rotation in degrees as '[x,y,z]', 'x,y,z', or '{\"x\":0,\"y\":0,\"z\":0}'.")] string rotation = null,
            [CliArg("scale", "Optional local scale as '[x,y,z]', 'x,y,z', or '{\"x\":1,\"y\":1,\"z\":1}'.")] string scale = null,
            [CliArg("duration", "Duration of the particle system in seconds (>= 0).")] float? duration = null,
            [CliArg("start-lifetime", "Initial particle lifetime in seconds (>= 0).")] float? startLifetime = null,
            [CliArg("start-speed", "Initial particle speed.")] float? startSpeed = null,
            [CliArg("start-size", "Initial particle size (>= 0).")] float? startSize = null,
            [CliArg("start-color", "Initial particle color as hex/named color, '[r,g,b(,a)]', or '{\"r\":1,\"g\":1,\"b\":1,\"a\":1}'.")] string startColor = null,
            [CliArg("looping", "Whether the particle system loops.")] bool? looping = null,
            [CliArg("max-particles", "Maximum active particles (>= 1).")] int? maxParticles = null,
            [CliArg("play-on-awake", "Whether the particle system plays automatically on awake.")] bool playOnAwake = true,
            [CliArg("dry-run", "Validate and preview without creating the GameObject.")] bool dryRun = false)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => Create(new ParticleCreateInput
            {
                name = name,
                parentInstanceId = parentId,
                parentPath = parentPath,
                localPosition = RuntimeCommandHelpers.ParseVector3Nullable(position, "position"),
                localEulerAngles = RuntimeCommandHelpers.ParseVector3Nullable(rotation, "rotation"),
                localScale = RuntimeCommandHelpers.ParseVector3Nullable(scale, "scale"),
                duration = duration,
                startLifetime = startLifetime,
                startSpeed = startSpeed,
                startSize = startSize,
                startColor = RuntimeCommandHelpers.ParseColorNullable(startColor, "start-color"),
                looping = looping,
                maxParticles = maxParticles,
                playOnAwake = playOnAwake
            }, dryRun));
        }

        [CliGroupCommand(
            Group,
            "Read supported Shuriken ParticleSystem module settings.",
            Name = "get",
            Effect = CommandEffect.ReadOnly,
            Tags = new[] { "runtime", "vfx" })]
        internal static ExitCode GetCommand(
            CommandContext cmd,
            [CliArg("instance-id", "GameObject instance ID containing the ParticleSystem.")] int? instanceId = null,
            [CliArg("path", "GameObject hierarchy path containing the ParticleSystem.")] string path = null,
            [CliArg("component-index", "Zero-based index of the ParticleSystem component on the GameObject.")] int componentIndex = 0)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => Get(instanceId, path, componentIndex));
        }

        [CliGroupCommand(
            Group,
            "Configure Shuriken ParticleSystem modules from JSON configuration.",
            Name = "configure",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "runtime", "vfx" })]
        internal static ExitCode ConfigureCommand(
            CommandContext cmd,
            [CliArg("instance-id", "GameObject instance ID containing the ParticleSystem.")] int? instanceId = null,
            [CliArg("path", "GameObject hierarchy path containing the ParticleSystem.")] string path = null,
            [CliArg("component-index", "Zero-based index of the ParticleSystem component on the GameObject.")] int componentIndex = 0,
            [CliArg("config", "JSON object matching ParticleConfigureInput fields for main, emission, shape, velocityOverLifetime, colorOverLifetime, sizeOverLifetime, noise, trails, collision, and renderer.", Required = true)] string config = null,
            [CliArg("dry-run", "Validate and preview changes without modifying the ParticleSystem.")] bool dryRun = false)
        {
            return RuntimeCommandHelpers.Execute(cmd, () =>
            {
                var input = ParseConfigureInput(config, instanceId, path, componentIndex);
                return Configure(input, dryRun);
            });
        }

        [CliGroupCommand(
            Group,
            "Play, stop, clear, or deterministically simulate a Shuriken ParticleSystem.",
            Name = "preview",
            Effect = CommandEffect.Idempotent,
            Tags = new[] { "runtime", "vfx" })]
        internal static ExitCode PreviewCommand(
            CommandContext cmd,
            [CliArg("instance-id", "GameObject instance ID containing the ParticleSystem.")] int? instanceId = null,
            [CliArg("path", "GameObject hierarchy path containing the ParticleSystem.")] string path = null,
            [CliArg("component-index", "Zero-based index of the ParticleSystem component on the GameObject.")] int componentIndex = 0,
            [CliArg("action", "Playback action: play, stop, clear, or simulate.")] string action = "play",
            [CliArg("simulate-time", "Time in seconds to simulate when action is 'simulate' (>= 0).")] float simulateTime = 0f,
            [CliArg("with-children", "Whether to apply the preview action to child ParticleSystems.")] bool withChildren = true,
            [CliArg("dry-run", "Validate and preview without modifying playback state.")] bool dryRun = false)
        {
            return RuntimeCommandHelpers.Execute(cmd, () => Preview(new ParticlePreviewInput
            {
                instanceId = instanceId,
                path = path,
                componentIndex = componentIndex,
                action = action,
                simulateTime = simulateTime,
                withChildren = withChildren
            }, dryRun));
        }

        internal static RuntimeChangeResult Create(ParticleCreateInput input, bool dryRun = false)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.name)) throw new ArgumentException("name is required.");

            var parent = input.parentInstanceId.HasValue || !string.IsNullOrWhiteSpace(input.parentPath)
                ? RuntimeCommandHelpers.RequireGameObject(input.parentInstanceId, input.parentPath)
                : null;

            if (input.localPosition.HasValue) RuntimeCommandHelpers.RequireFinite(input.localPosition.Value, nameof(input.localPosition));
            if (input.localEulerAngles.HasValue) RuntimeCommandHelpers.RequireFinite(input.localEulerAngles.Value, nameof(input.localEulerAngles));
            if (input.localScale.HasValue) RuntimeCommandHelpers.RequireFinite(input.localScale.Value, nameof(input.localScale));
            if (input.duration.HasValue) RequireNonNegative(input.duration.Value, nameof(input.duration));
            if (input.startLifetime.HasValue) RequireNonNegative(input.startLifetime.Value, nameof(input.startLifetime));
            if (input.startSpeed.HasValue) RuntimeCommandHelpers.RequireFinite(input.startSpeed.Value, nameof(input.startSpeed));
            if (input.startSize.HasValue) RequireNonNegative(input.startSize.Value, nameof(input.startSize));
            if (input.maxParticles.HasValue) RequirePositive(input.maxParticles.Value, nameof(input.maxParticles));

            if (dryRun)
                return RuntimeCommandHelpers.Change(true, $"Create ParticleSystem '{input.name}'.", rollbackSupported: Application.isEditor);

            var target = new GameObject(input.name);
            UnityMcpUndo.RegisterCreated(target, "UnityMCP Create ParticleSystem");
            if (parent != null) target.transform.SetParent(parent.transform, false);
            if (input.localPosition.HasValue) target.transform.localPosition = input.localPosition.Value;
            if (input.localEulerAngles.HasValue) target.transform.localEulerAngles = input.localEulerAngles.Value;
            if (input.localScale.HasValue) target.transform.localScale = input.localScale.Value;

            var system = target.AddComponent<ParticleSystem>();
            var main = system.main;
            if (input.duration.HasValue) main.duration = input.duration.Value;
            if (input.startLifetime.HasValue) main.startLifetime = input.startLifetime.Value;
            if (input.startSpeed.HasValue) main.startSpeed = input.startSpeed.Value;
            if (input.startSize.HasValue) main.startSize = input.startSize.Value;
            if (input.startColor.HasValue) main.startColor = input.startColor.Value;
            if (input.looping.HasValue) main.loop = input.looping.Value;
            if (input.maxParticles.HasValue) main.maxParticles = input.maxParticles.Value;
            main.playOnAwake = input.playOnAwake;

            return RuntimeCommandHelpers.Change(false, $"Created ParticleSystem '{RuntimeCommandHelpers.HierarchyPath(target)}'.", target, rollbackSupported: Application.isEditor);
        }

        internal static ParticleInfoOutput Get(int? instanceId, string path, int componentIndex = 0)
        {
            var system = RequireParticleSystem(instanceId, path, componentIndex);
            var main = system.main;
            var emission = system.emission;
            var shape = system.shape;
            var velocity = system.velocityOverLifetime;
            var color = system.colorOverLifetime;
            var size = system.sizeOverLifetime;
            var noise = system.noise;
            var trails = system.trails;
            var collision = system.collision;
            var renderer = system.GetComponent<ParticleSystemRenderer>();

            return new ParticleInfoOutput
            {
                instanceId = UnityMcpObjectId.Get(system),
                path = RuntimeCommandHelpers.HierarchyPath(system.gameObject),
                isPlaying = system.isPlaying,
                duration = main.duration,
                startLifetime = main.startLifetime.constant,
                startSpeed = main.startSpeed.constant,
                startSize = main.startSize.constant,
                startColor = main.startColor.color,
                looping = main.loop,
                maxParticles = main.maxParticles,
                emissionEnabled = emission.enabled,
                rateOverTime = emission.rateOverTime.constant,
                shapeEnabled = shape.enabled,
                shapeType = shape.shapeType.ToString(),
                shapeRadius = shape.radius,
                velocityOverLifetimeEnabled = velocity.enabled,
                colorOverLifetimeEnabled = color.enabled,
                sizeOverLifetimeEnabled = size.enabled,
                noiseEnabled = noise.enabled,
                trailsEnabled = trails.enabled,
                collisionEnabled = collision.enabled,
                renderMode = renderer != null ? renderer.renderMode.ToString() : "None",
                sortingOrder = renderer != null ? renderer.sortingOrder : 0
            };
        }

        internal static RuntimeChangeResult Configure(ParticleConfigureInput input, bool dryRun = false)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var system = RequireParticleSystem(input.instanceId, input.path, input.componentIndex);
            var renderer = system.GetComponent<ParticleSystemRenderer>();

            if (input.duration.HasValue) RequireNonNegative(input.duration.Value, nameof(input.duration));
            if (input.startDelay.HasValue) RequireNonNegative(input.startDelay.Value, nameof(input.startDelay));
            if (input.startLifetime.HasValue) RequireNonNegative(input.startLifetime.Value, nameof(input.startLifetime));
            if (input.startSpeed.HasValue) RuntimeCommandHelpers.RequireFinite(input.startSpeed.Value, nameof(input.startSpeed));
            if (input.startSize.HasValue) RequireNonNegative(input.startSize.Value, nameof(input.startSize));
            if (input.maxParticles.HasValue) RequirePositive(input.maxParticles.Value, nameof(input.maxParticles));
            if (input.rateOverTime.HasValue) RequireNonNegative(input.rateOverTime.Value, nameof(input.rateOverTime));

            var parsedShapeType = input.shapeType != null ? ParseEnum<ParticleSystemShapeType>(input.shapeType, nameof(input.shapeType)) : (ParticleSystemShapeType?)null;
            if (input.shapeRadius.HasValue) RequireNonNegative(input.shapeRadius.Value, nameof(input.shapeRadius));
            if (input.shapeAngle.HasValue) RequireRange(input.shapeAngle.Value, 0f, 360f, nameof(input.shapeAngle));
            if (input.velocityOverLifetime.HasValue) RuntimeCommandHelpers.RequireFinite(input.velocityOverLifetime.Value, nameof(input.velocityOverLifetime));
            if (input.sizeOverLifetimeMultiplier.HasValue) RequireNonNegative(input.sizeOverLifetimeMultiplier.Value, nameof(input.sizeOverLifetimeMultiplier));
            if (input.noiseStrength.HasValue) RequireNonNegative(input.noiseStrength.Value, nameof(input.noiseStrength));
            if (input.noiseFrequency.HasValue) RequireNonNegative(input.noiseFrequency.Value, nameof(input.noiseFrequency));
            if (input.noiseScrollSpeed.HasValue) RuntimeCommandHelpers.RequireFinite(input.noiseScrollSpeed.Value, nameof(input.noiseScrollSpeed));
            if (input.trailsRatio.HasValue) RequireRange(input.trailsRatio.Value, 0f, 1f, nameof(input.trailsRatio));
            if (input.trailsLifetime.HasValue) RequireNonNegative(input.trailsLifetime.Value, nameof(input.trailsLifetime));

            var parsedCollisionType = input.collisionType != null ? ParseEnum<ParticleSystemCollisionType>(input.collisionType, nameof(input.collisionType)) : (ParticleSystemCollisionType?)null;
            if (input.collisionDampen.HasValue) RequireRange(input.collisionDampen.Value, 0f, 1f, nameof(input.collisionDampen));

            var parsedRenderMode = input.renderMode != null ? ParseEnum<ParticleSystemRenderMode>(input.renderMode, nameof(input.renderMode)) : (ParticleSystemRenderMode?)null;
            if ((parsedRenderMode.HasValue || input.sortingOrder.HasValue) && renderer == null)
                throw new InvalidOperationException("ParticleSystem GameObject has no ParticleSystemRenderer component.");

            var targetPath = RuntimeCommandHelpers.HierarchyPath(system.gameObject);
            if (dryRun)
                return RuntimeCommandHelpers.Change(true, $"Configure ParticleSystem on '{targetPath}'.", system.gameObject, rollbackSupported: Application.isEditor);

            var undoGroup = UnityMcpUndo.Begin("UnityMCP Configure ParticleSystem");
            try
            {
                UnityMcpUndo.Record(system, "UnityMCP Configure ParticleSystem");

                var main = system.main;
                if (input.duration.HasValue) main.duration = input.duration.Value;
                if (input.startDelay.HasValue) main.startDelay = input.startDelay.Value;
                if (input.startLifetime.HasValue) main.startLifetime = input.startLifetime.Value;
                if (input.startSpeed.HasValue) main.startSpeed = input.startSpeed.Value;
                if (input.startSize.HasValue) main.startSize = input.startSize.Value;
                if (input.startColor.HasValue) main.startColor = input.startColor.Value;
                if (input.looping.HasValue) main.loop = input.looping.Value;
                if (input.prewarm.HasValue) main.prewarm = input.prewarm.Value;
                if (input.maxParticles.HasValue) main.maxParticles = input.maxParticles.Value;

                var emission = system.emission;
                if (input.emissionEnabled.HasValue) emission.enabled = input.emissionEnabled.Value;
                if (input.rateOverTime.HasValue) emission.rateOverTime = input.rateOverTime.Value;

                var shape = system.shape;
                if (input.shapeEnabled.HasValue) shape.enabled = input.shapeEnabled.Value;
                if (parsedShapeType.HasValue) shape.shapeType = parsedShapeType.Value;
                if (input.shapeRadius.HasValue) shape.radius = input.shapeRadius.Value;
                if (input.shapeAngle.HasValue) shape.angle = input.shapeAngle.Value;

                var velocity = system.velocityOverLifetime;
                if (input.velocityOverLifetimeEnabled.HasValue) velocity.enabled = input.velocityOverLifetimeEnabled.Value;
                if (input.velocityOverLifetime.HasValue)
                {
                    var v = input.velocityOverLifetime.Value;
                    velocity.x = v.x;
                    velocity.y = v.y;
                    velocity.z = v.z;
                }

                var color = system.colorOverLifetime;
                if (input.colorOverLifetimeEnabled.HasValue) color.enabled = input.colorOverLifetimeEnabled.Value;
                if (input.colorOverLifetimeColor.HasValue) color.color = input.colorOverLifetimeColor.Value;

                var size = system.sizeOverLifetime;
                if (input.sizeOverLifetimeEnabled.HasValue) size.enabled = input.sizeOverLifetimeEnabled.Value;
                if (input.sizeOverLifetimeMultiplier.HasValue) size.size = input.sizeOverLifetimeMultiplier.Value;

                var noise = system.noise;
                if (input.noiseEnabled.HasValue) noise.enabled = input.noiseEnabled.Value;
                if (input.noiseStrength.HasValue) noise.strength = input.noiseStrength.Value;
                if (input.noiseFrequency.HasValue) noise.frequency = input.noiseFrequency.Value;
                if (input.noiseScrollSpeed.HasValue) noise.scrollSpeed = input.noiseScrollSpeed.Value;

                var trails = system.trails;
                if (input.trailsEnabled.HasValue) trails.enabled = input.trailsEnabled.Value;
                if (input.trailsRatio.HasValue) trails.ratio = input.trailsRatio.Value;
                if (input.trailsLifetime.HasValue) trails.lifetime = input.trailsLifetime.Value;

                var collision = system.collision;
                if (input.collisionEnabled.HasValue) collision.enabled = input.collisionEnabled.Value;
                if (parsedCollisionType.HasValue) collision.type = parsedCollisionType.Value;
                if (input.collisionDampen.HasValue) collision.dampenMultiplier = input.collisionDampen.Value;

                if (renderer != null && (parsedRenderMode.HasValue || input.sortingOrder.HasValue))
                {
                    UnityMcpUndo.Record(renderer, "UnityMCP Configure ParticleSystem Renderer");
                    if (parsedRenderMode.HasValue) renderer.renderMode = parsedRenderMode.Value;
                    if (input.sortingOrder.HasValue) renderer.sortingOrder = input.sortingOrder.Value;
                }
            }
            finally
            {
                UnityMcpUndo.End(undoGroup);
            }

            return RuntimeCommandHelpers.Change(false, $"Configured ParticleSystem on '{targetPath}'.", system.gameObject, rollbackSupported: Application.isEditor);
        }

        internal static RuntimeChangeResult Preview(ParticlePreviewInput input, bool dryRun = false)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var system = RequireParticleSystem(input.instanceId, input.path, input.componentIndex);
            var action = (input.action ?? "play").Trim().ToLowerInvariant();
            if (action != "play" && action != "stop" && action != "clear" && action != "simulate")
                throw new ArgumentException("action must be play, stop, clear, or simulate.");

            if (action == "simulate")
                RequireNonNegative(input.simulateTime, nameof(input.simulateTime));

            if (!dryRun)
            {
                if (action == "play") system.Play(input.withChildren);
                else if (action == "stop") system.Stop(input.withChildren, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (action == "clear") system.Clear(input.withChildren);
                else system.Simulate(input.simulateTime, input.withChildren, true, true);
            }

            return RuntimeCommandHelpers.Change(dryRun, $"ParticleSystem {action} on '{RuntimeCommandHelpers.HierarchyPath(system.gameObject)}'.", system.gameObject);
        }

        private static ParticleConfigureInput ParseConfigureInput(string configJson, int? instanceId, string path, int componentIndex)
        {
            if (string.IsNullOrWhiteSpace(configJson))
                throw new ArgumentException("config is required.");

            ParticleConfigureInput parsed;
            try
            {
                using (var stringReader = new StringReader(configJson))
                using (var reader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None })
                {
                    var token = JToken.ReadFrom(reader);
                    if (!(token is JObject obj))
                        throw new ArgumentException("config must be a JSON object.");
                    parsed = obj.ToObject<ParticleConfigureInput>(ConfigureSerializer) ?? new ParticleConfigureInput();
                }
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ArgumentException($"config is not valid ParticleConfigureInput JSON: {exception.Message}", exception);
            }

            if (instanceId.HasValue)
            {
                if (parsed.instanceId.HasValue && parsed.instanceId.Value != instanceId.Value)
                    throw new ArgumentException("instance-id and config.instanceId must match when both are supplied.");
                parsed.instanceId = instanceId.Value;
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var trimmedPath = path.Trim();
                if (!string.IsNullOrWhiteSpace(parsed.path) && !string.Equals(parsed.path.Trim(), trimmedPath, StringComparison.Ordinal))
                    throw new ArgumentException("path and config.path must match when both are supplied.");
                parsed.path = trimmedPath;
            }

            if (componentIndex != 0)
                parsed.componentIndex = componentIndex;

            return parsed;
        }

        private static ParticleSystem RequireParticleSystem(int? instanceId, string path, int componentIndex)
        {
            var target = RuntimeCommandHelpers.RequireGameObject(instanceId, path);
            var systems = target.GetComponents<ParticleSystem>();
            if (systems.Length == 0)
                throw new ArgumentException($"GameObject has no {typeof(ParticleSystem).FullName} component.");
            if (componentIndex < 0 || componentIndex >= systems.Length)
                throw new ArgumentOutOfRangeException(nameof(componentIndex), $"componentIndex must be between 0 and {systems.Length - 1}.");
            return systems[componentIndex];
        }

        private static float RequireNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name, $"{name} must be finite and non-negative.");
            return value;
        }

        private static int RequirePositive(int value, string name)
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(name, $"{name} must be at least 1.");
            return value;
        }

        private static float RequireRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum}.");
            return value;
        }

        private static T ParseEnum<T>(string value, string name) where T : struct
        {
            if (!Enum.TryParse(value, true, out T result) || !Enum.IsDefined(typeof(T), result))
                throw new ArgumentException($"{name} is not a supported {typeof(T).Name} value.");
            return result;
        }
    }
}
