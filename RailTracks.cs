using DV.PointSet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class RailTracks
    {
        private const float SIMPLIFIED_RESOLUTION = 40f;

        private struct MapBounds
        {
            float minX, maxX;
            float minZ, maxZ;

            public MapBounds(float minX, float maxX, float minZ, float maxZ)
            {
                this.minX = minX;
                this.maxX = maxX;
                this.minZ = minZ;
                this.maxZ = maxZ;
            }

            public (float x, float z) Normalize((float x, float z) p) =>
                ( Mathf.InverseLerp(minZ, maxZ, p.z), Mathf.InverseLerp(minX, maxX, p.x) );
        }

        private static readonly MapBounds Bounds = new MapBounds(
                minX: GetAllTrackPoints().SelectMany(x => x).Min(p => p.Item1),
                maxX: GetAllTrackPoints().SelectMany(x => x).Max(p => p.Item1),
                minZ: GetAllTrackPoints().SelectMany(x => x).Min(p => p.Item2),
                maxZ: GetAllTrackPoints().SelectMany(x => x).Max(p => p.Item2));

        private static IEnumerable<(float x, float z)> NormalizeTrackPoints(IEnumerable<(float x, float z)> rawPoints) =>
            rawPoints.Select(Bounds.Normalize);

        public static IEnumerable<IEnumerable<(float x, float z)>> GetNormalizedTrackCoordinates() =>
            GetAllTrackPoints().Select(NormalizeTrackPoints);

        public static IEnumerable<IEnumerable<(float x, float z)>> GetAllTrackPoints(float resolution = SIMPLIFIED_RESOLUTION)
        {
            foreach (var rt in Component.FindObjectsOfType<RailTrack>())
                yield return GetTrackPoints(rt, resolution);
        }

        private static IEnumerable<(float x, float z)> GetTrackPoints(RailTrack track, float resolution = SIMPLIFIED_RESOLUTION)
        {
            var pointSet = track.GetPointSet();
            EquiPointSet simplified = EquiPointSet.ResampleEquidistant(
                pointSet,
                Mathf.Min(resolution, (float)pointSet.span / 3));

            foreach (var pt in simplified.points)
                yield return ((float)pt.position.x, (float)pt.position.z);
        }

        private static readonly string trackPointJSON = JsonConvert.SerializeObject(
            GetNormalizedTrackCoordinates().Select(trackPoints =>
                trackPoints.Select(pt =>
                    new JArray(pt.x, pt.z)
                )
            )
        );

        public static string GetTrackPointJSON() => trackPointJSON;

        private static readonly string junctionPointJSON = JsonConvert.SerializeObject(
            JunctionsSaveManager.OrderedJunctions.Select(j =>
            {
                var moved = j.position - WorldMover.currentMove;
                var normalized = Bounds.Normalize((moved.x, moved.z));
                return new JArray(normalized.x, normalized.z);
            })
        );

        public static string GetJunctionPointJSON() => junctionPointJSON;

        public static string GetJunctionStateJSON()
        {
            return JsonConvert.SerializeObject(
                JunctionsSaveManager.OrderedJunctions.Select(j => j.selectedBranch));
        }
    }
}