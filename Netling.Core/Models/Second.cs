using System;
using System.Collections.Generic;

namespace Netling.Core.Models
{
    public class Second
    {
        public long Count { get; set; }
        public long Bytes { get; set; }
        public List<float> ResponseTimes { get;  set; }
        public Dictionary<int, int> StatusCodes { get;  set; }
        public Dictionary<Type, int> Exceptions { get;  set; }
        public int Elapsed { get; set; }

        public Second(int elapsed)
        {
            Elapsed = elapsed;
            ResponseTimes = new List<float>();
            StatusCodes = new Dictionary<int, int>();
            Exceptions = new Dictionary<Type, int>();
        }

        internal void ClearResponseTimes()
        {
            ResponseTimes = new List<float>();
        }

        public void Add(long bytes, float responseTime, int statusCode, bool trackResponseTime)
        {
            Count++;
            Bytes += bytes;

            if (trackResponseTime)
                ResponseTimes.Add(responseTime);

            if (StatusCodes.ContainsKey(statusCode))
                StatusCodes[statusCode]++;
            else
                StatusCodes.Add(statusCode, 1);
        }

        public void AddError(float responseTime, Exception exception)
        {
            Count++;
            ResponseTimes.Add(responseTime);

            var exceptionType = exception.GetType();
            if (Exceptions.ContainsKey(exceptionType))
                Exceptions[exceptionType]++;
            else
                Exceptions.Add(exceptionType, 1);
        }

        public void AddMerged(Second second)
        {
            Count += second.Count;
            Bytes += second.Bytes;

            foreach (var statusCode in second.StatusCodes)
            {
                if (StatusCodes.ContainsKey(statusCode.Key))
                    StatusCodes[statusCode.Key] += statusCode.Value;
                else
                    StatusCodes.Add(statusCode.Key, statusCode.Value);
            }

            foreach (var exception in second.Exceptions)
            {
                if (Exceptions.ContainsKey(exception.Key))
                    Exceptions[exception.Key] += exception.Value;
                else
                    Exceptions.Add(exception.Key, exception.Value);
            }
        }
    }
}