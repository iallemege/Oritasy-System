using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// 16-bit PCM WAV decode with no Unity APIs in Decode().
    /// AudioClip.Create / SetData run on the main thread after a worker decode.
    /// </summary>
    internal sealed class OritasyWavPcm
    {
        public float[] Samples;
        public int Channels;
        public int SampleRate;
        public int Frames;

        internal static OritasyWavPcm Decode(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch { return null; }
            return DecodeBytes(bytes);
        }

        internal static OritasyWavPcm DecodeBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 44)
                return null;
            if (bytes[0] != (byte)'R' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' || bytes[3] != (byte)'F')
                return null;

            int channels = BitConverter.ToInt16(bytes, 22);
            int sampleRate = BitConverter.ToInt32(bytes, 24);
            int bits = BitConverter.ToInt16(bytes, 34);
            if (channels <= 0 || sampleRate <= 0 || bits != 16)
                return null;

            int dataOffset = 0;
            int dataSize = 0;
            for (int i = 12; i + 8 < bytes.Length; )
            {
                string chunk = Encoding.ASCII.GetString(bytes, i, 4);
                int size = BitConverter.ToInt32(bytes, i + 4);
                if (chunk == "data")
                {
                    dataOffset = i + 8;
                    dataSize = size;
                    break;
                }
                i += 8 + size;
                if ((size & 1) != 0)
                    i++;
            }
            if (dataOffset <= 0 || dataSize <= 0 || dataOffset + dataSize > bytes.Length)
                return null;

            int sampleCount = dataSize / 2;
            if (sampleCount <= 0)
                return null;
            float[] samples = new float[sampleCount];
            int sp = dataOffset;
            for (int s = 0; s < sampleCount; s++)
            {
                short v = BitConverter.ToInt16(bytes, sp);
                sp += 2;
                samples[s] = v / 32768f;
            }

            OritasyWavPcm pcm = new OritasyWavPcm();
            pcm.Samples = samples;
            pcm.Channels = channels;
            pcm.SampleRate = sampleRate;
            pcm.Frames = sampleCount / channels;
            return pcm;
        }

        internal AudioClip CreateClip(string name)
        {
            if (Samples == null || Frames <= 0 || Channels <= 0 || SampleRate <= 0)
                return null;
            AudioClip clip = AudioClip.Create(name, Frames, Channels, SampleRate, false);
            clip.SetData(Samples, 0);
            return clip;
        }

        /// <summary>Decode on OritasyWorker, create AudioClip on main. Nested-yield from other coroutines.</summary>
        internal static IEnumerator LoadClip(string path, Action<AudioClip> done)
        {
            OritasyWavPcm pcm = null;
            bool finished = false;
            bool queued = OritasyWorker.TryEnqueue(
                delegate { pcm = Decode(path); },
                delegate { finished = true; });
            if (queued)
            {
                while (!finished)
                    yield return null;
            }
            else
            {
                pcm = Decode(path);
                yield return null;
            }

            AudioClip clip = null;
            if (pcm != null)
            {
                try
                {
                    clip = pcm.CreateClip(Path.GetFileNameWithoutExtension(path));
                }
                catch { clip = null; }
            }
            if (done != null)
                done(clip);
        }
    }
}
