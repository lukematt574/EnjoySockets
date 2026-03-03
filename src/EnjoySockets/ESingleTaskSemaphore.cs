// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class ESingleTaskSemaphore
    {
        readonly object _lock = new();

        readonly Queue<ESemaphoreWaiter> _queueWaiters = new();
        readonly Stack<ESemaphoreWaiter> _pool = new();

        bool _busy;

        /// <summary>
        /// Lightweight semaphore (single running)
        /// </summary>
        internal ESingleTaskSemaphore() { }

        public bool TryWait()
        {
            lock (_lock)
            {
                if (_busy)
                {
                    return false;
                }
                else
                {
                    _busy = true;
                    return true;
                }
            }
        }

        public ValueTask<bool> Wait()
        {
            ESemaphoreWaiter? waiter = null;
            lock (_lock)
            {
                if (_busy)
                {
                    waiter = Rent();
                    waiter.Reset(Return);
                    _queueWaiters.Enqueue(waiter);
                }
                else
                {
                    _busy = true;
                    return ValueTask.FromResult(true);
                }
            }
            return waiter.AsValueTask();
        }

        public void Release()
        {
            ESemaphoreWaiter? toRelease = null;
            lock (_lock)
            {
                if (_queueWaiters.Count > 0)
                {
                    toRelease = _queueWaiters.Dequeue();
                    _busy = true;
                }
                else
                    _busy = false;
            }
            toRelease?.SetResult(true);
        }

        ESemaphoreWaiter Rent()
        {
            if (_pool.TryPop(out var s))
                return s;
            else
                return new ESemaphoreWaiter();
        }

        void Return(ESemaphoreWaiter w)
        {
            lock (_lock)
            {
                if (_pool.Count < 5)
                    _pool.Push(w);
            }
        }

        private sealed class ESemaphoreWaiter : IValueTaskSource<bool>
        {
            ManualResetValueTaskSourceCore<bool> _core;
            Action<ESemaphoreWaiter>? _onCompleted;

            public ESemaphoreWaiter()
            {
                _core = new ManualResetValueTaskSourceCore<bool> { RunContinuationsAsynchronously = true };
            }

            public void Reset(Action<ESemaphoreWaiter>? onCompleted)
            {
                _core.Reset();
                _onCompleted = onCompleted;
            }

            public void SetResult(bool result) => _core.SetResult(result);

            public ValueTask<bool> AsValueTask() => new(this, _core.Version);

            public bool GetResult(short token)
            {
                try
                {
                    return _core.GetResult(token);
                }
                finally
                {
                    _onCompleted?.Invoke(this);
                }
            }
            public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
                => _core.OnCompleted(continuation, state, token, flags);
        }
    }
}
