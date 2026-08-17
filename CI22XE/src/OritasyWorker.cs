using System;
using System.Collections.Generic;
using System.Threading;

namespace Oritasy
{
    /// <summary>
    /// Background workers for CPU / IO that must NOT touch Unity APIs
    /// (GameObject, Transform, FindObjects, Harmony patch apply, etc.).
    /// Completions run on the main thread via PumpMain.
    /// Four below-normal workers (capped to CPU count). Unity hunt / Transform stay main-thread.
    /// </summary>
    internal static class OritasyWorker
    {
        private const int MaxWorkers = 4;
        private const int MaxPending = 16;
        private static readonly object Gate = new object();
        private static readonly Queue<Job> Pending = new Queue<Job>(16);
        private static readonly Queue<Job> Completed = new Queue<Job>(16);
        private static Semaphore _signal;
        private static bool _started;
        private static int _workerCount;
        private static int _queued;
        private static int _finished;
        private static int _rejected;
        private static int _running;
        private static int _pumped;

        private sealed class Job
        {
            public WaitCallback Work;
            public Action OnMain;
            public Exception Error;
        }

        internal static int WorkerCount
        {
            get { return _workerCount; }
        }

        internal static int Queued
        {
            get { return _queued; }
        }

        internal static int Finished
        {
            get { return _finished; }
        }

        internal static int Rejected
        {
            get { return _rejected; }
        }

        internal static int Running
        {
            get { return _running; }
        }

        internal static int PendingCount
        {
            get { lock (Gate) { return Pending.Count; } }
        }

        internal static void EnsureStarted()
        {
            if (_started)
                return;
            lock (Gate)
            {
                if (_started)
                    return;
                int n = MaxWorkers;
                try
                {
                    int cpu = Environment.ProcessorCount;
                    if (cpu < 1)
                        n = 1;
                    else if (cpu < MaxWorkers)
                        n = cpu;
                    else
                        n = MaxWorkers;
                }
                catch { n = MaxWorkers; }
                _workerCount = n;
                _signal = new Semaphore(0, MaxPending + MaxWorkers);
                _started = true;
                for (int i = 0; i < n; i++)
                {
                    Thread t = new Thread(WorkerLoop);
                    t.IsBackground = true;
                    t.Name = "OritasyWorker-" + i.ToString();
                    t.Priority = ThreadPriority.BelowNormal;
                    t.Start();
                }
            }
        }

        /// <summary>
        /// Queue a Unity-free callback. Returns false if the queue is full (caller must do work inline).
        /// </summary>
        internal static bool TryEnqueue(WaitCallback work, Action onMain)
        {
            if (work == null)
                return false;
            EnsureStarted();
            Job job = new Job();
            job.Work = work;
            job.OnMain = onMain;
            lock (Gate)
            {
                if (Pending.Count >= MaxPending)
                {
                    _rejected++;
                    return false;
                }
                Pending.Enqueue(job);
                _queued++;
            }
            try { _signal.Release(); }
            catch { }
            return true;
        }

        internal static void PumpMain()
        {
            PumpMain(16);
        }

        internal static void PumpMain(int max)
        {
            if (max < 1)
                max = 1;
            if (max > 16)
                max = 16;
            for (int n = 0; n < max; n++)
            {
                Job job = null;
                lock (Gate)
                {
                    if (Completed.Count == 0)
                        break;
                    job = Completed.Dequeue();
                }
                if (job == null)
                    continue;
                _pumped++;
                if (job.Error != null && Plugin.Log != null)
                    Plugin.Log.LogWarning("OritasyWorker: " + job.Error.Message);
                if (job.OnMain == null)
                    continue;
                try { job.OnMain(); }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("OritasyWorker OnMain: " + ex.Message);
                }
            }
        }

        internal static string SnapshotLine()
        {
            return "workers=" + _workerCount.ToString()
                + "  queued=" + _queued.ToString()
                + "  done=" + _finished.ToString()
                + "  rejected=" + _rejected.ToString()
                + "  running=" + _running.ToString()
                + "  pending=" + PendingCount.ToString()
                + "  pumped=" + _pumped.ToString();
        }

        private static void WorkerLoop()
        {
            while (true)
            {
                Job job = null;
                lock (Gate)
                {
                    if (Pending.Count > 0)
                        job = Pending.Dequeue();
                }
                if (job == null)
                {
                    try { _signal.WaitOne(250); }
                    catch { }
                    continue;
                }
                Interlocked.Increment(ref _running);
                try
                {
                    job.Work(null);
                }
                catch (Exception ex)
                {
                    job.Error = ex;
                }
                Interlocked.Decrement(ref _running);
                Interlocked.Increment(ref _finished);
                lock (Gate)
                {
                    Completed.Enqueue(job);
                }
            }
        }
    }
}
