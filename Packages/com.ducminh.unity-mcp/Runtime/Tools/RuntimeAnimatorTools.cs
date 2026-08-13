using System;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    [Serializable]
    public sealed class AnimatorParameterSetInput
    {
        public int? instanceId;
        public string path;
        public string parameter;
        public string kind;
        public bool? boolValue;
        public int? intValue;
        public float? floatValue;
        public bool apply;
    }

    [Serializable]
    public sealed class AnimatorParameterSetOutput
    {
        public bool dryRun;
        public bool changed;
        public int instanceId;
        public string parameter;
        public string kind;
        public string summary;
    }

    /// <summary>Runtime-safe Animator parameter changes for a loaded Animator component.</summary>
    public static class RuntimeAnimatorTools
    {
        [UnityMcpTool("animator-parameter-set", Description = "Set or trigger a loaded Animator parameter; dry-run unless apply is true.", Category = "animation", Scope = UnityMcpScope.All, Safety = UnityMcpSafety.Write, SupportsDryRun = true)]
        public static AnimatorParameterSetOutput AnimatorParameterSet(AnimatorParameterSetInput input, UnityMcpContext context)
        {
            if (string.IsNullOrWhiteSpace(input.parameter)) throw new ArgumentException("parameter is required.");
            var gameObject = RuntimeCoreTools.RequireGameObject(input.instanceId, input.path);
            var animator = gameObject.GetComponent<Animator>();
            if (animator == null) throw new ArgumentException("The target GameObject does not have an Animator component.");
            var kind = NormalizeKind(input.kind);
            var hash = Animator.StringToHash(input.parameter);
            RequireParameter(animator, input.parameter, kind);

            if (!context.DryRun)
            {
                UnityMcpUndo.Record(animator, "UnityMCP Set Animator Parameter");
                switch (kind)
                {
                    case "bool": animator.SetBool(hash, input.boolValue ?? throw new ArgumentException("boolValue is required for kind=bool.")); break;
                    case "int": animator.SetInteger(hash, input.intValue ?? throw new ArgumentException("intValue is required for kind=int.")); break;
                    case "float": animator.SetFloat(hash, input.floatValue ?? throw new ArgumentException("floatValue is required for kind=float.")); break;
                    case "trigger": animator.SetTrigger(hash); break;
                    case "reset-trigger": animator.ResetTrigger(hash); break;
                }
            }

            return new AnimatorParameterSetOutput
            {
                dryRun = context.DryRun,
                changed = !context.DryRun,
                instanceId = animator.GetInstanceID(),
                parameter = input.parameter,
                kind = kind,
                summary = (kind == "trigger" ? "Set" : kind == "reset-trigger" ? "Reset" : "Set") + " Animator parameter '" + input.parameter + "'."
            };
        }

        private static string NormalizeKind(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "bool":
                case "boolean": return "bool";
                case "int":
                case "integer": return "int";
                case "float": return "float";
                case "trigger": return "trigger";
                case "reset-trigger": return "reset-trigger";
                default: throw new ArgumentException("kind must be bool, int, float, trigger, or reset-trigger.");
            }
        }

        private static void RequireParameter(Animator animator, string name, string requestedKind)
        {
            foreach (var parameter in animator.parameters)
            {
                if (!string.Equals(parameter.name, name, StringComparison.Ordinal)) continue;
                var actual = parameter.type == AnimatorControllerParameterType.Bool ? "bool"
                    : parameter.type == AnimatorControllerParameterType.Int ? "int"
                    : parameter.type == AnimatorControllerParameterType.Float ? "float"
                    : "trigger";
                if (requestedKind == "reset-trigger" ? actual != "trigger" : actual != requestedKind)
                    throw new ArgumentException("Animator parameter '" + name + "' has type '" + actual + "', not '" + requestedKind + "'.");
                return;
            }
            throw new ArgumentException("Animator parameter was not found: " + name);
        }
    }
}
