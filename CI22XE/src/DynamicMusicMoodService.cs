using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield dynamic-music mood evaluation (0.0.9.64).
    /// DynamicMusic owns clips, fades, and playback host.
    /// </summary>
    internal static class DynamicMusicMoodService
    {
        internal enum MoodKind
        {
            None = 0,
            Menu,
            Start,
            Tactical,
            Strategic,
            Combat,
            Takeoff,
            Victory,
            Defeat
        }

        internal struct MoodInput
        {
            public bool Victory;
            public bool Defeat;
            public bool MenuLike;
            public bool MissionRunning;
            public bool HasLocalAircraft;
            public bool CombatNearby;
            public bool Airborne;
            public float MissionAgeSec;   // <0 if unknown
            public float AirborneAgeSec;  // <0 if not airborne
            public float StartWindowSec;
            public float TakeoffWindowSec;
            public float RadarAltM;
            public float StrategicAltM;
        }

        /// <summary>Mirrors prior DynamicMusic.EvaluateMood priority order.</summary>
        internal static MoodKind Evaluate(MoodInput i)
        {
            if (i.Victory)
                return MoodKind.Victory;
            if (i.Defeat)
                return MoodKind.Defeat;

            if (i.MenuLike || !i.MissionRunning)
                return MoodKind.Menu;

            if (!i.HasLocalAircraft)
            {
                if (i.MissionAgeSec >= 0f && i.MissionAgeSec < i.StartWindowSec)
                    return MoodKind.Start;
                return MoodKind.Tactical;
            }

            if (i.CombatNearby)
                return MoodKind.Combat;

            if (i.AirborneAgeSec >= 0f && i.AirborneAgeSec < i.TakeoffWindowSec)
                return MoodKind.Takeoff;

            if (i.MissionAgeSec >= 0f && i.MissionAgeSec < i.StartWindowSec)
                return MoodKind.Start;

            if (i.RadarAltM >= i.StrategicAltM)
                return MoodKind.Strategic;

            return MoodKind.Tactical;
        }

        internal static float MoodPriority(MoodKind mood)
        {
            switch (mood)
            {
                case MoodKind.Defeat: return 90f;
                case MoodKind.Victory: return 85f;
                case MoodKind.Combat: return 70f;
                case MoodKind.Takeoff: return 55f;
                case MoodKind.Start: return 50f;
                case MoodKind.Strategic: return 40f;
                case MoodKind.Tactical: return 35f;
                case MoodKind.Menu: return 20f;
                default: return 10f;
            }
        }
    }
}
