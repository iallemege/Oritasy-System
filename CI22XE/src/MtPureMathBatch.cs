using System;
using System.Threading;

namespace Oritasy
{
    /// <summary>
    /// 259C: ThreadPool helpers for pure float math only — never Transform / GameObject / Physics.
    /// Callers copy blittable samples on the main thread, wait for score fill, apply Unity writes on main.
    /// </summary>
    internal static class MtPureMathBatch
    {
        internal const int ParallelThreshold = 16;
        private const int MinChunk = 8;

        internal static void FillScores(int count, float[] scoreOut, Action<int, int> scoreRange)
        {
            if (count <= 0 || scoreOut == null || scoreRange == null)
                return;
            if (count < ParallelThreshold || Environment.ProcessorCount <= 1)
            {
                scoreRange(0, count);
                return;
            }

            int workers = Environment.ProcessorCount;
            if (workers > 4)
                workers = 4;
            int chunk = count / workers;
            if (chunk < MinChunk)
            {
                scoreRange(0, count);
                return;
            }

            int remaining = workers;
            object gate = new object();
            Exception error = null;
            for (int w = 0; w < workers; w++)
            {
                int start = w * chunk;
                int end = (w == workers - 1) ? count : start + chunk;
                ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        int[] range = (int[])state;
                        scoreRange(range[0], range[1]);
                    }
                    catch (Exception ex)
                    {
                        lock (gate) { if (error == null) error = ex; }
                    }
                    finally
                    {
                        lock (gate)
                        {
                            remaining--;
                            Monitor.PulseAll(gate);
                        }
                    }
                }, new int[] { start, end });
            }

            lock (gate)
            {
                while (remaining > 0)
                    Monitor.Wait(gate, 50);
            }
            if (error != null && Plugin.Log != null)
                Plugin.Log.LogWarning("MtPureMathBatch: " + error.Message);
        }

        internal static int IndexOfMin(float[] scores, int count)
        {
            if (scores == null || count <= 0)
                return -1;
            int best = -1;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float s = scores[i];
                if (s < bestScore)
                {
                    bestScore = s;
                    best = i;
                }
            }
            return best;
        }

        internal static void ArgsortAscending(float[] scores, int count, int[] orderOut)
        {
            if (scores == null || orderOut == null || count <= 0)
                return;
            for (int i = 0; i < count; i++)
                orderOut[i] = i;
            for (int i = 1; i < count; i++)
            {
                int keyIdx = orderOut[i];
                float key = scores[keyIdx];
                int j = i - 1;
                while (j >= 0 && scores[orderOut[j]] > key)
                {
                    orderOut[j + 1] = orderOut[j];
                    j--;
                }
                orderOut[j + 1] = keyIdx;
            }
        }
    }
}