using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oritasy
{
    /// <summary>Wavefront OBJ loader (same approach as WeXon missile meshes).</summary>
    internal static class InfantryObjLoader
    {
        internal static Mesh Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            List<Vector3> positions = new List<Vector3>(4096);
            List<Vector2> uvs = new List<Vector2>(4096);
            List<Vector3> normals = new List<Vector3>(4096);
            List<Vector3> outPos = new List<Vector3>(8192);
            List<Vector2> outUv = new List<Vector2>(8192);
            List<Vector3> outNrm = new List<Vector3>(8192);
            List<int> tris = new List<int>(16384);
            Dictionary<string, int> remap = new Dictionary<string, int>(8192);
            bool anyUv = false;
            bool anyNrm = false;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;
                if (line.Length < 2)
                    continue;
                if (line[0] == 'v' && line[1] == ' ')
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 4)
                        positions.Add(new Vector3(ParseF(p[1]), ParseF(p[2]), ParseF(p[3])));
                }
                else if (line.StartsWith("vt ", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 3)
                        uvs.Add(new Vector2(ParseF(p[1]), ParseF(p[2])));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 4)
                        normals.Add(new Vector3(ParseF(p[1]), ParseF(p[2]), ParseF(p[3])));
                }
                else if (line[0] == 'f' && line[1] == ' ')
                {
                    string[] p = SplitWs(line);
                    if (p.Length < 4)
                        continue;
                    int[] idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++)
                    {
                        bool gotUv;
                        bool gotNrm;
                        idx[i - 1] = AddVertex(p[i], positions, uvs, normals,
                            outPos, outUv, outNrm, remap, out gotUv, out gotNrm);
                        if (gotUv) anyUv = true;
                        if (gotNrm) anyNrm = true;
                    }
                    for (int i = 1; i + 1 < idx.Length; i++)
                    {
                        tris.Add(idx[0]);
                        tris.Add(idx[i]);
                        tris.Add(idx[i + 1]);
                    }
                }
            }

            if (outPos.Count == 0 || tris.Count == 0)
                return null;
            while (outUv.Count < outPos.Count)
                outUv.Add(Vector2.zero);
            while (outNrm.Count < outPos.Count)
                outNrm.Add(Vector3.up);

            Mesh mesh = new Mesh();
            if (outPos.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = outPos.ToArray();
            if (anyUv)
                mesh.uv = outUv.ToArray();
            if (anyNrm)
                mesh.normals = outNrm.ToArray();
            mesh.triangles = tris.ToArray();
            if (!anyNrm)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); }
            catch { }
            return mesh;
        }

        private static int AddVertex(
            string token,
            List<Vector3> positions,
            List<Vector2> uvs,
            List<Vector3> normals,
            List<Vector3> outPos,
            List<Vector2> outUv,
            List<Vector3> outNrm,
            Dictionary<string, int> remap,
            out bool gotUv,
            out bool gotNrm)
        {
            gotUv = false;
            gotNrm = false;
            int existing;
            if (remap.TryGetValue(token, out existing))
                return existing;

            int vi = -1;
            int ti = -1;
            int ni = -1;
            string[] bits = token.Split('/');
            if (bits.Length > 0 && bits[0].Length > 0)
                vi = ParseIndex(bits[0], positions.Count);
            if (bits.Length > 1 && bits[1].Length > 0)
                ti = ParseIndex(bits[1], uvs.Count);
            if (bits.Length > 2 && bits[2].Length > 0)
                ni = ParseIndex(bits[2], normals.Count);

            outPos.Add((vi >= 0 && vi < positions.Count) ? positions[vi] : Vector3.zero);
            if (ti >= 0 && ti < uvs.Count)
            {
                outUv.Add(uvs[ti]);
                gotUv = true;
            }
            else
                outUv.Add(Vector2.zero);
            if (ni >= 0 && ni < normals.Count)
            {
                outNrm.Add(normals[ni]);
                gotNrm = true;
            }
            else
                outNrm.Add(Vector3.up);

            int id = outPos.Count - 1;
            remap[token] = id;
            return id;
        }

        private static int ParseIndex(string s, int count)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return -1;
            if (v < 0)
                return count + v;
            return v - 1;
        }

        private static float ParseF(string s)
        {
            float f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                return f;
            return 0f;
        }

        private static string[] SplitWs(string line)
        {
            return line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
