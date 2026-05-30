// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class SingleEntrySemaphore
    {
        readonly object _lock = new();

        readonly Queue<SingleSlotWaiter> _queueWaiters = new();
        readonly Stack<SingleSlotWaiter> _pool = new();

        bool _busy;

        /// <summary>
        /// Lightweight semaphore (single running)
        /// </summary>
        internal SingleEntrySemaphore() { }

        internal bool TryWait()
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

        internal ValueTask<bool> Wait()
        {
            SingleSlotWaiter? waiter = null;
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

        internal void Release()
        {
            SingleSlotWaiter? toRelease = null;
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

        SingleSlotWaiter Rent()
        {
            if (_pool.TryPop(out var waiter))
                return waiter;
            else
                return new SingleSlotWaiter();
        }

        void Return(SingleSlotWaiter waiter)
        {
            lock (_lock)
            {
                if (_pool.Count < 5)
                    _pool.Push(waiter);
            }
        }

        private sealed class SingleSlotWaiter : IValueTaskSource<bool>
        {
            ManualResetValueTaskSourceCore<bool> _core;
            Action<SingleSlotWaiter>? _onCompleted;

            public SingleSlotWaiter()
            {
                _core = new ManualResetValueTaskSourceCore<bool> { RunContinuationsAsynchronously = true };
            }

            public void Reset(Action<SingleSlotWaiter>? onCompleted)
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
