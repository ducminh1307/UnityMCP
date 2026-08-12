using System;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    internal static class UnityMcpUndo
    {
        private static readonly Type UndoType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.Undo", false)).FirstOrDefault(type => type != null);

        public static int Begin(string name)
        {
            if (UndoType == null) return -1;
            TryInvoke("IncrementCurrentGroup", Type.EmptyTypes);
            var get = UndoType.GetMethod("GetCurrentGroup", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            var group = get == null ? -1 : (int)get.Invoke(null, null);
            TryInvoke("SetCurrentGroupName", new[] { typeof(string) }, name);
            return group;
        }

        public static void End(int group)
        {
            if (group >= 0) TryInvoke("CollapseUndoOperations", new[] { typeof(int) }, group);
        }

        public static void Record(UnityEngine.Object value, string name)
        {
            if (!TryInvoke("RecordObject", new[] { typeof(UnityEngine.Object), typeof(string) }, value, name)) { }
        }

        public static void RegisterCreated(UnityEngine.Object value, string name)
        {
            if (!TryInvoke("RegisterCreatedObjectUndo", new[] { typeof(UnityEngine.Object), typeof(string) }, value, name)) { }
        }

        public static void Destroy(UnityEngine.Object value)
        {
            if (!TryInvoke("DestroyObjectImmediate", new[] { typeof(UnityEngine.Object) }, value)) UnityEngine.Object.Destroy(value);
        }

        public static Component AddComponent(GameObject target, Type componentType)
        {
            if (UndoType != null)
            {
                var method = UndoType.GetMethod("AddComponent", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject), typeof(Type) }, null);
                if (method != null) return (Component)method.Invoke(null, new object[] { target, componentType });
            }
            return target.AddComponent(componentType);
        }

        public static void SetParent(Transform target, Transform parent, bool worldPositionStays, string name)
        {
            if (UndoType != null)
            {
                var localPosition = target.localPosition;
                var localRotation = target.localRotation;
                var localScale = target.localScale;
                var method = UndoType.GetMethod("SetTransformParent", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Transform), typeof(Transform), typeof(string) }, null);
                if (method != null)
                {
                    method.Invoke(null, new object[] { target, parent, name });
                    if (!worldPositionStays) { target.localPosition = localPosition; target.localRotation = localRotation; target.localScale = localScale; }
                    return;
                }
            }
            target.SetParent(parent, worldPositionStays);
        }

        private static bool TryInvoke(string name, Type[] signature, params object[] arguments)
        {
            if (UndoType == null) return false;
            var method = UndoType.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, signature, null);
            if (method == null) return false;
            method.Invoke(null, arguments);
            return true;
        }
    }
}
