using System.Collections.Generic;
using UnityEngine;

namespace DucMinh.UnityMcp
{
    /// <summary>
    /// Provides the legacy integer object identifier used by the UnityMCP wire contract.
    /// Unity 6.6 replaced Object.GetInstanceID with Object.GetEntityId and no
    /// longer permits converting EntityId directly to an integer.
    /// </summary>
    public static class UnityMcpObjectId
    {
#if UNITY_6000_6_OR_NEWER
        private static readonly Dictionary<EntityId, int> LegacyIds = new Dictionary<EntityId, int>();
        private static int nextLegacyId = 1;
#endif

        public static int Get(Object value)
        {
#if UNITY_6000_6_OR_NEWER
            var entityId = value.GetEntityId();
            lock (LegacyIds)
            {
                if (LegacyIds.TryGetValue(entityId, out var legacyId)) return legacyId;
                legacyId = nextLegacyId++;
                if (legacyId == 0) legacyId = nextLegacyId++;
                LegacyIds.Add(entityId, legacyId);
                return legacyId;
            }
#else
            return value.GetInstanceID();
#endif
        }
    }
}
