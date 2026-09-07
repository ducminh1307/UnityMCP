using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DucMinh.UnityMcp.Editor
{
    internal static class UnityMcpEditorObjectId
    {
        public static Object Resolve(int legacyId)
        {
#if UNITY_6000_6_OR_NEWER
            // The wire contract predates EntityId and carries only an int. Resolve
            // against loaded objects rather than reconstructing an EntityId from a
            // lossy legacy value.
            return Resources.FindObjectsOfTypeAll<Object>()
                .FirstOrDefault(value => UnityMcpObjectId.Get(value) == legacyId);
#else
            return EditorUtility.InstanceIDToObject(legacyId);
#endif
        }
    }
}
