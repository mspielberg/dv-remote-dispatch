using DV.PointSet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DvMod.RemoteDispatch
{
    public static class World
    {
        private const float EARTH_CIRCUMFERENCE = 40e6f;
        private const float DEGREES_PER_METER = 360f / EARTH_CIRCUMFERENCE;

        public readonly struct Position
        {
            public readonly float x;
            public readonly float z;

            public Position(float x, float z)
            {
                this.x = x;
                this.z = z;
            }

            public Position(Vector3 position) : this(position.x, position.z) { }
            public Position(Transform transform) : this(transform.position) { }

            public LatLon ToLatLon() => LatLon.From(this);
        }

        public readonly struct LatLon
        {
            public readonly float latitude;
            public readonly float longitude;

            public LatLon(float latitude, float longitude)
            {
                this.latitude = latitude;
                this.longitude = longitude;
            }

            public static LatLon From(Position p) => new LatLon(DEGREES_PER_METER * p.z, DEGREES_PER_METER * p.x);

            public JToken ToJson() => new JArray(latitude, longitude);
        }
    }

    public static class RailTracks
    {
        private const float SIMPLIFIED_RESOLUTION = 40f;

        private static IEnumerable<World.LatLon> NormalizeTrackPoints(IEnumerable<World.Position> positions) => positions.Select(p => p.ToLatLon());

        public static Dictionary<RailTrack, IEnumerable<World.LatLon>> GetNormalizedTrackCoordinates() =>
            GetAllTrackPoints().ToDictionary(kvp => kvp.Key, kvp => NormalizeTrackPoints(kvp.Value));

        public static Dictionary<RailTrack, IEnumerable<World.Position>> GetAllTrackPoints(float resolution = SIMPLIFIED_RESOLUTION) =>
            Component.FindObjectsOfType<RailTrack>().ToDictionary(track => track, track => GetTrackPoints(track, resolution));

        private static IEnumerable<World.Position> GetTrackPoints(RailTrack track, float resolution = SIMPLIFIED_RESOLUTION)
        {
            var pointSet = track.GetPointSet();
            EquiPointSet simplified = EquiPointSet.ResampleEquidistant(
                pointSet,
                Mathf.Min(resolution, (float)pointSet.span / 3));

            foreach (var pt in simplified.points)
                yield return new World.Position((float)pt.position.x, (float)pt.position.z);
        }

        private static readonly string trackPointJSON = JsonConvert.SerializeObject(
            GetNormalizedTrackCoordinates().ToDictionary(
                kvp => kvp.Key.logicTrack.ID,
                kvp => kvp.Value.Select(ll => ll.ToJson())));

        public static string GetTrackPointJSON() => trackPointJSON;
    }

    public static class Junctions
    {
        private static readonly string junctionPointJSON = JsonConvert.SerializeObject(
            JunctionsSaveManager.OrderedJunctions.Select(j =>
            {
                var moved = j.position - WorldMover.currentMove;
                return new JObject(
                    new JProperty("position", new World.Position(moved.x, moved.z).ToLatLon().ToJson()),
                    new JProperty("branches", j.outBranches.Select(b => b.track.logicTrack.ID.ToString()))
                );
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