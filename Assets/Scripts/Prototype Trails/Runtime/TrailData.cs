using System;
using System.Collections.Generic;
using UnityEngine;

namespace GetLost.Trails
{
    [Serializable]
    public class TrailPointData
    {
        public Vector3 worldPosition;
        public float distanceFromStart;
        public float timeFromStart;
        public float elevation;
        public float slopeDegrees;
        public float speed;
        public string terrainId;
    }

    [Serializable]
    public class TrailPlayerTrackData
    {
        public string playerId;
        public List<TrailPointData> points =
            new List<TrailPointData>();

        public float Distance
        {
            get
            {
                if (points == null ||
                    points.Count == 0)
                {
                    return 0f;
                }

                return
                    points[points.Count - 1]
                        .distanceFromStart;
            }
        }
    }

    [Serializable]
    public class TrailObservationData
    {
        public string observationType;
        public string playerId;
        public Vector3 worldPosition;
        public float timeFromTrailStart;

        public float numericValue;
        public string textValue;
        public string sourceId;
    }

    [Serializable]
    public class TrailStatistics
    {
        public float length;
        public float elevationGain;
        public float elevationLoss;
        public float averageSlope;
        public float medianSlope;
        public float maxSlope;
        public float averageSpeed;
        public float duration;
        public float surveyedAreaApproximation;
        public int sampleCount;
    }

    [Serializable]
    public class TrailScoreMetricSnapshotData
    {
        public string id;
        public string displayName;

        public float rawValue;
        public float score;
        public float weight;

        public bool contributesToOverallScore;
    }

    [Serializable]
    public class TrailScoreSnapshotData
    {
        public bool hasScore;
        public float overallScore;

        public List<TrailScoreMetricSnapshotData>
            metrics =
                new List<TrailScoreMetricSnapshotData>();
    }

    [Serializable]
    public class TrailData
    {
        public string trailId;
        public string trailName;
        public string contractId;

        public string officialPlayerId;

        public string startTrailHeadId;
        public string finishTrailHeadId;

        // Destination-region trails finish wherever the player confirms the
        // survey. This position is also used to rebuild the completion marker.
        public bool hasChosenFinishPosition;
        public Vector3 chosenFinishPosition;

        public long createdUtcTicks;
        public bool isFinalized;
        // Ground is constructed and persisted independently of the survey track.
        public bool runtimeDeposited;

        public List<TrailPlayerTrackData>
            playerTracks =
                new List<TrailPlayerTrackData>();

        public List<TrailObservationData>
            observations =
                new List<TrailObservationData>();

        public TrailStatistics statistics =
            new TrailStatistics();

        // Historical score snapshot.
        // Loading a trail does not need to rerun scoring providers.
        public TrailScoreSnapshotData scoreSnapshot =
            new TrailScoreSnapshotData();

        public TrailPlayerTrackData GetTrack(
            string playerId)
        {
            if (playerTracks == null)
                return null;

            for (int i = 0;
                 i < playerTracks.Count;
                 i++)
            {
                if (playerTracks[i] != null &&
                    playerTracks[i].playerId ==
                    playerId)
                {
                    return playerTracks[i];
                }
            }

            return null;
        }

        public TrailPlayerTrackData
            GetOfficialTrack()
        {
            return
                GetTrack(
                    officialPlayerId);
        }

        public TrailData DeepClone()
        {
            try
            {
                string json =
                    JsonUtility.ToJson(this);

                return
                    JsonUtility
                        .FromJson<TrailData>(
                            json);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Legacy stand-alone trail file shape.
    /// Retained so old getlost_trails.json files can still be imported.
    /// </summary>
    [Serializable]
    public class TrailSaveData
    {
        public int version = 1;

        public List<TrailData> trails =
            new List<TrailData>();
    }
}
