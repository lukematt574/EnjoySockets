// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    /// <summary>
    /// Represents a pending reliable send operation on the client side.
    /// Contains the data required to await completion and retransmit the message if necessary.
    /// </summary>
    internal class ClientReliableSendContext : IValueTaskSource<long>
    {
        internal ulong Session { get; set; }
        internal ulong Target { get; set; }
        internal long Instance { get; set; }
        internal int TotalBytes { get; set; }
        internal MemorySegment? Msg { get; set; }
        internal bool IsRetryable { get; set; }
        internal MemorySegment? ResponseMsg { get; set; }

        ManualResetValueTaskSourceCore<long> _core;

        internal ClientReliableSendContext()
        {
            _core = new ManualResetValueTaskSourceCore<long> { RunContinuationsAsynchronously = true };
        }

        internal T? GetResponseMsg<T>(IESerializer serializer)
        {
            if (ResponseMsg != null)
            {
                return serializer.Deserialize<T>(ResponseMsg.Read());
            }
            return default;
        }

        internal void Reset()
        {
            _core.Reset();
        }

        internal void SetResult(long result) => _core.SetResult(result);

        public ValueTask<long> AsValueTask() => new(this, _core.Version);

        public long GetResult(short token) => _core.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }
}
