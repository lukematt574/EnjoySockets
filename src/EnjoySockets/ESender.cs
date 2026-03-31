// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal class ESender : IValueTaskSource<long>
    {
        public ulong Session { get; set; }
        public ulong Target { get; set; }
        public long Instance { get; set; }
        public int TotalBytes { get; set; }
        public EMemorySegment? Msg { get; set; }
        public bool Repeat { get; set; }

        ManualResetValueTaskSourceCore<long> _core;

        public ESender()
        {
            _core = new ManualResetValueTaskSourceCore<long> { RunContinuationsAsynchronously = true };
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
