using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class ETCPControlSendingClient
    {
        readonly object _lock = new();

        readonly Queue<EControlSendingWaiter> _queueWaiters = new();
        readonly Stack<EControlSendingWaiter> _pool = new();

        int _currentUseBuffer;
        int _currentUseMsges;

        const int maxUseMsges = ETCPSocket.MaxCachedResponses;
        readonly int maxUseBuffer;

        internal ETCPControlSendingClient(int messagesBuffer)
        {
            maxUseBuffer = messagesBuffer;
        }

        public EControlSendingWaiter? TryWait(int bytesToRent)
        {
            lock (_lock)
            {
                if (_queueWaiters.Count > 0 || !CheckSpace(bytesToRent))
                {
                    return null;
                }
                else
                {
                    return AllocateSpace(bytesToRent);
                }
            }
        }

        public ValueTask<EControlSendingWaiter> Wait(int bytesToRent)
        {
            EControlSendingWaiter? waiter = null;
            lock (_lock)
            {
                if (_queueWaiters.Count > 0 || !CheckSpace(bytesToRent))
                {
                    waiter = Rent();
                    waiter.Reset(Return, bytesToRent);
                    _queueWaiters.Enqueue(waiter);
                }
                else
                {
                    return ValueTask.FromResult(AllocateSpace(bytesToRent));
                }
            }
            return waiter.AsValueTask();
        }

        EControlSendingWaiter AllocateSpace(int bytesToRent)
        {
            var waiter = Rent();
            waiter.Reset(Return, bytesToRent);
            _currentUseBuffer += bytesToRent;
            _currentUseMsges++;
            return waiter;
        }

        bool CheckSpace(int bytesToRent)
        {
            var b = _currentUseBuffer + bytesToRent;
            var c = _currentUseMsges + 1;
            if (b > maxUseBuffer || c > maxUseMsges)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public void Release(EControlSendingWaiter eControl)
        {
            List<EControlSendingWaiter>? toRelease = null;

            lock (_lock)
            {
                _currentUseBuffer -= eControl.RentBytes;
                _currentUseMsges--;

                while (_queueWaiters.Count > 0)
                {
                    var next = _queueWaiters.Peek();
                    if (!CheckSpace(next.RentBytes))
                        break;

                    _queueWaiters.Dequeue();
                    _currentUseBuffer += next.RentBytes;
                    _currentUseMsges++;

                    (toRelease ??= []).Add(next);
                }
            }

            if (toRelease != null)
            {
                foreach (var w in toRelease)
                    w.SetResult(w);
            }
        }

        public void CancelAll()
        {
            lock (_lock)
            {
                foreach (var waiter in _queueWaiters)
                {
                    waiter.Cancel = true;
                }
            }
        }

        EControlSendingWaiter Rent()
        {
            if (_pool.TryPop(out var s))
                return s;
            else
                return new EControlSendingWaiter();
        }

        void Return(EControlSendingWaiter w)
        {
            lock (_lock)
            {
                if (_pool.Count < 50)
                    _pool.Push(w);
            }
        }

        internal sealed class EControlSendingWaiter : IValueTaskSource<EControlSendingWaiter>
        {
            ManualResetValueTaskSourceCore<EControlSendingWaiter> _core;
            Action<EControlSendingWaiter>? _onCompleted;
            public int RentBytes { get; private set; } = 0;
            public bool Cancel { get; set; }

            public EControlSendingWaiter()
            {
                _core = new ManualResetValueTaskSourceCore<EControlSendingWaiter> { RunContinuationsAsynchronously = true };
            }

            public void Reset(Action<EControlSendingWaiter>? onCompleted, int bytesToRent)
            {
                _core.Reset();
                _onCompleted = onCompleted;
                RentBytes = bytesToRent;
                Cancel = false;
            }

            public void SetResult(EControlSendingWaiter result) => _core.SetResult(result);

            public ValueTask<EControlSendingWaiter> AsValueTask() => new(this, _core.Version);

            public EControlSendingWaiter GetResult(short token)
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
