using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Additive FPS viewmodel clips (fire / reload / idle / walk / run)
    /// baked in Blender to text keyframes. BepInEx cannot play FBX/Mecanim from disk.
    /// </summary>
    internal static class InfantryViewAnim
    {
        internal const int KindFire = 0;
        internal const int KindReload = 1;

        private static readonly string[] Prefix = new string[]
        {
            "m16", "ak47", "pistol", "shotgun", "sniper", "at4", "rpg7", "rbs70", "g11"
        };

        private class Key
        {
            public float T;
            public float Px;
            public float Py;
            public float Pz;
            public float Rx;
            public float Ry;
            public float Rz;
        }

        private class Clip
        {
            public Key[] Keys;
            public float Duration;
        }

        private static Clip[,] _clips;
        private static Clip _idle;
        private static Clip _walk;
        private static Clip _run;
        private static bool _tried;
        private static int _kind = -1;
        private static int _gun = -1;
        private static float _start;
        private static float _wIdle = 1f;
        private static float _wWalk;
        private static float _wRun;
        private static float _locoAmp = 1f;
        private static float _lean;

        internal static void Ensure(string dir)
        {
            if (_tried)
                return;
            _tried = true;
            _clips = new Clip[InfantryCombat.GunCount, 2];
            for (int g = 0; g < InfantryCombat.GunCount; g++)
            {
                _clips[g, KindFire] = LoadOrFallback(dir, Prefix[g] + "_fire.txt", g, KindFire);
                _clips[g, KindReload] = LoadOrFallback(dir, Prefix[g] + "_reload.txt", g, KindReload);
            }
            _idle = LoadNamed(dir, "idle.txt", 0);
            _walk = LoadNamed(dir, "walk.txt", 1);
            _run = LoadNamed(dir, "run.txt", 2);
        }

        internal static void PlayFire(int gun)
        {
            if (_kind == KindReload)
                return;
            if (gun < 0 || gun >= InfantryCombat.GunCount)
                return;
            _gun = gun;
            _kind = KindFire;
            _start = Time.time;
        }

        internal static void PlayReload(int gun)
        {
            if (gun < 0 || gun >= InfantryCombat.GunCount)
                return;
            _gun = gun;
            _kind = KindReload;
            _start = Time.time;
        }

        internal static void Stop()
        {
            _kind = -1;
            _gun = -1;
            _wIdle = 1f;
            _wWalk = 0f;
            _wRun = 0f;
            _locoAmp = 1f;
            _lean = 0f;
        }

        internal static void TickLoco(float dt, float speed, bool sprint, float ads, bool reloading, float strafe)
        {
            if (_idle == null && _walk == null && _run == null)
                return;
            float wantIdle = 0f;
            float wantWalk = 0f;
            float wantRun = 0f;
            if (speed < 0.4f)
                wantIdle = 1f;
            else if (sprint)
                wantRun = 1f;
            else
                wantWalk = 1f;
            float k = dt * 7f;
            _wIdle = Mathf.MoveTowards(_wIdle, wantIdle, k);
            _wWalk = Mathf.MoveTowards(_wWalk, wantWalk, k);
            _wRun = Mathf.MoveTowards(_wRun, wantRun, k);
            float amp = 1f;
            if (ads > 0.01f)
                amp *= Mathf.Lerp(1f, 0.22f, ads);
            if (reloading)
                amp *= 0.22f;
            InfantryGun g = InfantryCombat.Gun;
            if (g == InfantryGun.Pistol)
                amp *= 0.85f;
            else if (g == InfantryGun.At4 || g == InfantryGun.Rpg7 || g == InfantryGun.Rbs70)
                amp *= 1.18f;
            _locoAmp = amp;
            _lean = Mathf.MoveTowards(_lean, strafe * 4.2f, dt * 9f);
        }

        internal static void EvaluateLoco(out Vector3 pos, out Vector3 euler)
        {
            pos = Vector3.zero;
            euler = Vector3.zero;
            Vector3 p;
            Vector3 e;
            if (_wIdle > 0.001f && _idle != null)
            {
                SampleLoop(_idle, Time.time, out p, out e);
                pos += p * _wIdle;
                euler += e * _wIdle;
            }
            if (_wWalk > 0.001f && _walk != null)
            {
                SampleLoop(_walk, Time.time, out p, out e);
                pos += p * _wWalk;
                euler += e * _wWalk;
            }
            if (_wRun > 0.001f && _run != null)
            {
                SampleLoop(_run, Time.time, out p, out e);
                pos += p * _wRun;
                euler += e * _wRun;
            }
            pos *= _locoAmp;
            euler *= _locoAmp;
            euler.z += _lean * _locoAmp;
        }

        internal static bool IsReloadPlaying()
        {
            return _kind == KindReload;
        }

        internal static float Duration(int gun, int kind)
        {
            Clip c = Get(gun, kind);
            if (c == null || c.Duration < 0.05f)
            {
                if (kind == KindReload)
                    return 2.4f;
                return 0.14f;
            }
            return c.Duration;
        }

        internal static void Evaluate(out Vector3 pos, out Vector3 euler)
        {
            pos = Vector3.zero;
            euler = Vector3.zero;
            if (_kind < 0 || _gun < 0)
                return;
            Clip c = Get(_gun, _kind);
            if (c == null || c.Keys == null || c.Keys.Length == 0)
                return;
            float t = Time.time - _start;
            if (_kind == KindFire && t >= c.Duration)
            {
                _kind = -1;
                return;
            }
            if (t > c.Duration)
                t = c.Duration;
            Sample(c, t, out pos, out euler);
        }

        private static Clip Get(int gun, int kind)
        {
            if (_clips == null || gun < 0 || gun >= InfantryCombat.GunCount)
                return null;
            if (kind < 0 || kind > 1)
                return null;
            return _clips[gun, kind];
        }

        private static Clip LoadNamed(string dir, string file, int locoKind)
        {
            if (!string.IsNullOrEmpty(dir))
            {
                string path = Path.Combine(dir, "anims");
                path = Path.Combine(path, file);
                Clip loaded = TryLoad(path);
                if (loaded != null)
                    return loaded;
            }
            return MakeLoco(locoKind);
        }

        private static Clip LoadOrFallback(string dir, string file, int gun, int kind)
        {
            if (!string.IsNullOrEmpty(dir))
            {
                string path = Path.Combine(dir, "anims");
                path = Path.Combine(path, file);
                Clip loaded = TryLoad(path);
                if (loaded != null)
                    return loaded;
            }
            return Fallback(gun, kind);
        }

        private static Clip TryLoad(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                string[] lines = File.ReadAllLines(path);
                List<Key> keys = new List<Key>(64);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (line[0] == '#' || line[0] == 'F' || line[0] == 'f')
                        continue;
                    string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 7)
                        continue;
                    Key k = new Key();
                    k.T = Parse(p[0]);
                    k.Px = Parse(p[1]);
                    k.Py = Parse(p[2]);
                    k.Pz = Parse(p[3]);
                    k.Rx = Parse(p[4]);
                    k.Ry = Parse(p[5]);
                    k.Rz = Parse(p[6]);
                    keys.Add(k);
                }
                if (keys.Count < 2)
                    return null;
                Clip c = new Clip();
                c.Keys = keys.ToArray();
                c.Duration = c.Keys[c.Keys.Length - 1].T;
                return c;
            }
            catch
            {
                return null;
            }
        }

        private static float Parse(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        private static Clip Fallback(int gun, int kind)
        {
            List<Key> keys = new List<Key>(8);
            InfantryGun g = (InfantryGun)gun;
            if (kind == KindFire)
            {
                if (g == InfantryGun.Shotgun)
                {
                    Add(keys, 0f, 0, 0, 0, 0, 0, 0);
                    Add(keys, 0.05f, 0.006f, 0.016f, -0.070f, -12f, 1.5f, 2.5f);
                    Add(keys, 0.14f, 0.002f, 0.004f, -0.018f, -3f, 0.4f, 0.6f);
                    Add(keys, 0.22f, 0f, -0.004f, 0.028f, 1.5f, 0f, 0f);
                    Add(keys, 0.34f, 0, 0, 0, 0, 0, 0);
                }
                else if (g == InfantryGun.Sniper)
                {
                    Add(keys, 0f, 0, 0, 0, 0, 0, 0);
                    Add(keys, 0.06f, 0.008f, 0.018f, -0.080f, -14f, 1.2f, 1.8f);
                    Add(keys, 0.16f, 0.010f, 0.006f, -0.020f, -4f, 6f, 0f);
                    Add(keys, 0.42f, 0, 0, 0, 0, 0, 0);
                }
                else if (g == InfantryGun.At4 || g == InfantryGun.Rpg7)
                {
                    Add(keys, 0f, 0, 0, 0, 0, 0, 0);
                    Add(keys, 0.08f, 0.012f, 0.022f, -0.095f, -16f, 2f, 3f);
                    Add(keys, 0.48f, 0, 0, 0, 0, 0, 0);
                }
                else
                {
                    float kick = g == InfantryGun.Ak47 ? -0.048f : -0.040f;
                    float pitch = g == InfantryGun.Ak47 ? -8.5f : -7f;
                    float dur = g == InfantryGun.Pistol ? 0.12f : 0.14f;
                    Add(keys, 0f, 0, 0, 0, 0, 0, 0);
                    Add(keys, dur * 0.22f, 0.004f, 0.010f, kick, pitch, 1.1f, 2f);
                    Add(keys, dur, 0, 0, 0, 0, 0, 0);
                }
            }
            else
            {
                float dur = 2.4f;
                if (g == InfantryGun.Pistol)
                    dur = 1.6f;
                else if (g == InfantryGun.Shotgun)
                    dur = 3.2f;
                else if (g == InfantryGun.Sniper)
                    dur = 2.8f;
                Add(keys, 0f, 0, 0, 0, 0, 0, 0);
                Add(keys, dur * 0.22f, 0.055f, -0.085f, -0.040f, 8f, 6f, -22f);
                Add(keys, dur * 0.48f, 0.070f, -0.120f, -0.055f, 12f, 8f, -28f);
                Add(keys, dur * 0.78f, 0.010f, -0.020f, 0.018f, -3f, 1f, -4f);
                Add(keys, dur, 0, 0, 0, 0, 0, 0);
            }
            Clip c = new Clip();
            c.Keys = keys.ToArray();
            c.Duration = c.Keys[c.Keys.Length - 1].T;
            return c;
        }

        private static void Add(List<Key> keys, float t, float px, float py, float pz, float rx, float ry, float rz)
        {
            Key k = new Key();
            k.T = t;
            k.Px = px;
            k.Py = py;
            k.Pz = pz;
            k.Rx = rx;
            k.Ry = ry;
            k.Rz = rz;
            keys.Add(k);
        }

        private static Clip MakeLoco(int kind)
        {
            float dur;
            int n;
            if (kind == 0)
            {
                dur = 2.4f;
                n = 16;
            }
            else if (kind == 1)
            {
                dur = 0.5f;
                n = 12;
            }
            else
            {
                dur = 0.34f;
                n = 12;
            }
            List<Key> keys = new List<Key>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float t = dur * (float)i / (float)n;
                float u = (dur < 0.0001f) ? 0f : (t / dur) * 6.2831853f;
                float su = Mathf.Sin(u);
                float asu = Mathf.Abs(su);
                if (kind == 0)
                    Add(keys, t, su * 0.004f, Mathf.Sin(u * 2f) * 0.007f, su * 0.002f,
                        Mathf.Sin(u * 2f) * 0.7f, su * 0.45f, su * 0.55f);
                else if (kind == 1)
                    Add(keys, t, su * 0.018f, asu * 0.024f, -asu * 0.010f,
                        -asu * 2f, su * 1.4f, su * 2.8f);
                else
                    Add(keys, t, su * 0.032f, asu * 0.042f, -asu * 0.018f,
                        -asu * 3.6f, su * 2.6f, su * 5f);
            }
            Clip c = new Clip();
            c.Keys = keys.ToArray();
            c.Duration = dur;
            return c;
        }

        private static void SampleLoop(Clip c, float time, out Vector3 pos, out Vector3 euler)
        {
            if (c == null || c.Keys == null || c.Keys.Length == 0)
            {
                pos = Vector3.zero;
                euler = Vector3.zero;
                return;
            }
            float d = c.Duration;
            float t = time;
            if (d > 0.001f)
                t = t - Mathf.Floor(t / d) * d;
            Sample(c, t, out pos, out euler);
        }

        private static void Sample(Clip c, float t, out Vector3 pos, out Vector3 euler)
        {
            Key[] ks = c.Keys;
            if (t <= ks[0].T)
            {
                pos = new Vector3(ks[0].Px, ks[0].Py, ks[0].Pz);
                euler = new Vector3(ks[0].Rx, ks[0].Ry, ks[0].Rz);
                return;
            }
            int last = ks.Length - 1;
            if (t >= ks[last].T)
            {
                pos = new Vector3(ks[last].Px, ks[last].Py, ks[last].Pz);
                euler = new Vector3(ks[last].Rx, ks[last].Ry, ks[last].Rz);
                return;
            }
            int i = 0;
            while (i < last && ks[i + 1].T < t)
                i++;
            Key a = ks[i];
            Key b = ks[i + 1];
            float span = b.T - a.T;
            float u = span < 0.0001f ? 0f : (t - a.T) / span;
            u = u * u * (3f - 2f * u);
            pos = new Vector3(
                a.Px + (b.Px - a.Px) * u,
                a.Py + (b.Py - a.Py) * u,
                a.Pz + (b.Pz - a.Pz) * u);
            euler = new Vector3(
                Mathf.LerpAngle(a.Rx, b.Rx, u),
                Mathf.LerpAngle(a.Ry, b.Ry, u),
                Mathf.LerpAngle(a.Rz, b.Rz, u));
        }
    }
}
