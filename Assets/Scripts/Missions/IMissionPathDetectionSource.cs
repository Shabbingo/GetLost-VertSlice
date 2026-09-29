using System;
using System.Collections.Generic;

namespace GetLost.Missions
{
    /// <summary>
    /// Replaceable bridge between mission POIs and whichever path representation
    /// the game uses. A future detector only needs to implement this contract.
    /// </summary>
    public interface IMissionPathDetectionSource
    {
        event Action<MissionPointOfInterest> PathReachedPoi;
        void Configure(IReadOnlyList<MissionPointOfInterest> pointsOfInterest);
    }
}
