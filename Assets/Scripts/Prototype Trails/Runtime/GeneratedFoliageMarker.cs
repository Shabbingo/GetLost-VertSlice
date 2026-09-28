using UnityEngine;

namespace GetLost.Trails
{
    /// <summary>
    /// Marker component used to identify GameObjects that were spawned
    /// by the foliage system and are therefore safe for the Trail System
    /// to remove during trail clearance.
    ///
    /// This does not change Unity layers, tags, physics, or controller behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedFoliageMarker : MonoBehaviour
    {
    }
}
