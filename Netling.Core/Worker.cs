﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netling.Core.Models;
//using Netling.Core.Performance;
//using Netling.Core.Utils;

namespace Netling.Core
{
    public static class Worker
    {
        public static Task<WorkerResult> Run(Uri uri, int threads, bool threadAffinity, int pipelining, TimeSpan duration, CancellationToken cancellationToken)
        {
            return Run(uri, threads, threadAffinity, pipelining, duration, null, cancellationToken);
        }

        public static Task<WorkerResult> Run(Uri uri, int count, CancellationToken cancellationToken)
        {
            return Run(uri, 1, false, 1, TimeSpan.MaxValue, count, cancellationToken);
        }

        private static Task<WorkerResult> Run(Uri uri, int threads, bool threadAffinity, int pipelining, TimeSpan duration, int? count, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var combinedWorkerThreadResult = QueueWorkerThreads(uri, threads, threadAffinity, pipelining, duration, count, cancellationToken);
                var workerResult = new WorkerResult(uri, threads, threadAffinity, pipelining, combinedWorkerThreadResult.Elapsed);
                workerResult.Process(combinedWorkerThreadResult);
                return workerResult;
            });
        }

        private static CombinedWorkerThreadResult QueueWorkerThreads(Uri uri, int threads, bool threadAffinity, int pipelining, TimeSpan duration, int? count, CancellationToken cancellationToken)
        {
            var results = new ConcurrentQueue<WorkerThreadResult>();
            var events = new List<ManualResetEventSlim>();
            var sw = new Stopwatch();
            sw.Start();

            for (var i = 0; i < threads; i++)
            {
                var resetEvent = new ManualResetEventSlim(false);

                ThreadHelper.QueueThread(i, threadAffinity, (threadIndex) =>
                {
                    DoWork(uri, duration, count, pipelining, results, sw, cancellationToken, resetEvent, threadIndex);
                });

                events.Add(resetEvent);
            }

            for (var i = 0; i < events.Count; i += 50)
            {
                var group = events.Skip(i).Take(50).Select(r => r.WaitHandle).ToArray();
                WaitHandle.WaitAll(group);
            }
            sw.Stop();

            return new CombinedWorkerThreadResult(results, sw.Elapsed);
        }

        private static void DoWork(Uri uri, TimeSpan duration, int? count, int pipelining, ConcurrentQueue<WorkerThreadResult> results, Stopwatch sw, CancellationToken cancellationToken, ManualResetEventSlim resetEvent, int workerIndex)
        {
            var result = new WorkerThreadResult();
            var sw2 = new Stopwatch();
            var sw3 = new Stopwatch();
            var worker = new HttpWorker(uri);
            var current = 0;

            // To save memory we only track response times from the first 20 workers
            var trackResponseTime = workerIndex < 20;

            // Priming connection ...
            if (!count.HasValue)
            {
                try
                {
                    int tmpStatusCode;

                    if (pipelining > 1)
                    {
                        worker.WritePipelined(pipelining);
                        worker.Flush();
                        for (var j = 0; j < pipelining; j++)
                        {
                            worker.ReadPipelined(out tmpStatusCode);
                        }
                    }
                    else
                    {
                        worker.Write();
                        worker.Flush();
                        worker.Read(out tmpStatusCode);
                    }

                }
                catch (Exception) { }
            }

            if (pipelining == 1)
            {
                while (!cancellationToken.IsCancellationRequested && duration.TotalMilliseconds > sw.Elapsed.TotalMilliseconds && (!count.HasValue || current < count.Value))
                {
                    current++;
                    try
                    {
                        sw2.Restart();
                        worker.Write();
                        worker.Flush();
                        int statusCode;
                        var length = worker.Read(out statusCode);
                        result.Add((int)(sw.ElapsedTicks / Stopwatch.Frequency), length, (float) sw2.ElapsedTicks / Stopwatch.Frequency * 1000, statusCode, trackResponseTime);
                    }
                    catch (Exception ex)
                    {
                        result.AddError((int)(sw.ElapsedTicks / Stopwatch.Frequency), (float) sw2.ElapsedTicks / Stopwatch.Frequency * 1000, ex);
                    }
                }
            }
            else
            {
                try
                {
                    sw2.Restart();
                    worker.WritePipelined(pipelining);
                    worker.Flush();
                }
                catch (Exception ex)
                {
                    result.AddError((int)(sw.ElapsedTicks / Stopwatch.Frequency), (float)sw2.ElapsedTicks / Stopwatch.Frequency * 1000, ex);
                }

                while (!cancellationToken.IsCancellationRequested && duration.TotalMilliseconds > sw.Elapsed.TotalMilliseconds)
                {
                    try
                    {
                        for (var j = 0; j < pipelining; j++)
                        {
                            int statusCode;
                            var length = worker.ReadPipelined(out statusCode);
                            result.Add((int)Math.Floor((float)sw.ElapsedTicks / Stopwatch.Frequency), length, (float)sw2.ElapsedTicks / Stopwatch.Frequency * 1000, statusCode, trackResponseTime);

                            if (j == 0 && !cancellationToken.IsCancellationRequested && duration.TotalMilliseconds > sw.Elapsed.TotalMilliseconds)
                            {
                                sw3.Restart();
                                worker.WritePipelined(pipelining);
                                worker.Flush();
                            }
                        }

                        var tmp = sw2;
                        sw2 = sw3;
                        sw3 = tmp;
                    }
                    catch (Exception ex)
                    {
                        result.AddError((int)(sw.ElapsedTicks / Stopwatch.Frequency), (float)sw2.ElapsedTicks / Stopwatch.Frequency * 1000, ex);
                    }
                }
            }

            results.Enqueue(result);
            resetEvent.Set();
        }
    }
}
