using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Models;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json;

namespace Core
{

    [DataContract]
    public class InvocationArgs
    {
        [DataMember]
        public int threads;
        [DataMember]
        public int runs;
        [DataMember]
        public TimeSpan duration;
    }
    
  //  http://stackoverflow.com/questions/30060974/how-to-convert-each-object-in-listexpandoobject-into-its-own-type
  
 

public class InvocationArgsConverter : Newtonsoft.Json.Converters.CustomCreationConverter<InvocationArgs>
{
    public override InvocationArgs Create(Type objectType)
    {
        return new InvocationArgs();
    }
}
    public class Job<T> where T : IResult
    {
        public delegate void ProgressEventHandler(double value);
        public ProgressEventHandler OnProgress { get; set; }
        public JobResult<T> Process(Stream argument_stream, Func<IEnumerable<Task<T>>> processAction, CancellationToken cancellationToken = default(CancellationToken))
        {

            argument_stream.Position = 0;
            DataContractJsonSerializer argument_serializer =
     new DataContractJsonSerializer(typeof(InvocationArgs));
            // TODO - better detect subnormal launch conditions
            
            InvocationArgs _invocation_arguments = (InvocationArgs)argument_serializer.ReadObject(argument_stream);
            _invocation_arguments = JsonConvert.DeserializeObject<InvocationArgs>( argument_stream, InvocationArgsConverter );
            #pragma warning disable 612
            Assert.IsInstanceOfType(typeof(TimeSpan), _invocation_arguments.duration);
            Assert.IsInstanceOfType(typeof(int), _invocation_arguments.runs);
            Assert.IsInstanceOfType(typeof(int), _invocation_arguments.threads);
            Assert.IsNotNull(_invocation_arguments.threads);
#pragma warning restore 612
            if (_invocation_arguments.runs == 0)
            {
                _invocation_arguments.runs = 10;
            }
            if (_invocation_arguments.duration.Seconds == 0)
            {
                _invocation_arguments.duration = TimeSpan.FromSeconds(10);
            }


            return Process(_invocation_arguments.threads, _invocation_arguments.runs, _invocation_arguments.duration, processAction, cancellationToken);

        }

        private JobResult<T> Process(int threads, int runs, TimeSpan duration, Func<IEnumerable<Task<T>>> processAction, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThreadPool.SetMinThreads(int.MaxValue, int.MaxValue);

            var results = new ConcurrentQueue<List<T>>();
            var events = new List<ManualResetEvent>();
            var sw = new Stopwatch();
            sw.Start();
            var totalRuntime = 0.0;

            for (int i = 0; i < threads; i++)
            {
                var resetEvent = new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem(async (state) =>
                {
                    var index = (int)state;
                    var result = new List<T>();

                    Debug.WriteLine(index);

                    for (int j = 0; j < runs; j++)
                    {
                        foreach (var actionResult in processAction.Invoke())
                        {
                            var tmp = await actionResult;

                            if (cancellationToken.IsCancellationRequested || duration.TotalMilliseconds < sw.ElapsedMilliseconds)
                            {
                                results.Enqueue(result);
                                resetEvent.Set();
                                return;
                            }

                            result.Add(tmp);
                            totalRuntime = sw.Elapsed.TotalMilliseconds;

                            if (index == 0 && OnProgress != null)
                            {
                                if (duration == TimeSpan.MaxValue)
                                    OnProgress(100.0 / runs * (j + 1));
                                else
                                    OnProgress(100.0 / duration.TotalMilliseconds * sw.ElapsedMilliseconds);
                            }
                        }
                    }

                    results.Enqueue(result);
                    resetEvent.Set();
                }, i);

                events.Add(resetEvent);
            }

            for (int i = 0; i < events.Count; i += 50)
            {
                var group = events.Skip(i).Take(50).ToArray();
                WaitHandle.WaitAll(group);
            }

            var finalResults = results.SelectMany(r => r, (a, b) => b).ToList();
            return new JobResult<T>(threads, totalRuntime, finalResults);
        }
    }
}