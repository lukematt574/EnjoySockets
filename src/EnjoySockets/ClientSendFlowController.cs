// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Threading.Tasks.Sources;

namespace EnjoySockets
{
    internal sealed class ClientSendFlowController
    {
        readonly object _lock = new();

        readonly Queue<ClientSendPermit> _pendingPermits = new();
        readonly Stack<ClientSendPermit> _pool = new();

        int _reservedBufferBytes;
        int _activeMessageCount;

        const int maxUseMsges = ETCPSocket.MaxServerStoredResponsesPerSession;
        readonly int maxUseBuffer;

        internal ClientSendFlowController(int messagesBuffer)
        {
            maxUseBuffer = messagesBuffer;
        }

        internal ClientSendPermit? TryWait(int bytesToRent)
        {
            lock (_lock)
            {
                if (_pendingPermits.Count > 0 || !CheckSpace(bytesToRent))
                {
                    return null;
                }
                else
                {
                    return AllocateSpace(bytesToRent);
                }
            }
        }

        internal ValueTask<ClientSendPermit> Wait(int bytesToRent)
        {
            ClientSendPermit? permit = null;
            lock (_lock)
            {
                if (_pendingPermits.Count > 0 || !CheckSpace(bytesToRent))
                {
                    permit = Rent();
                    permit.Reset(Return, bytesToRent);
                    _pendingPermits.Enqueue(permit);
                }
                else
                {
                    return ValueTask.FromResult(AllocateSpace(bytesToRent));
                }
            }
            return permit.AsValueTask();
        }

        ClientSendPermit AllocateSpace(int bytesToRent)
        {
            var waiter = Rent();
            waiter.Reset(Return, bytesToRent);
            _reservedBufferBytes += bytesToRent;
            _activeMessageCount++;
            return waiter;
        }

        bool CheckSpace(int bytesToRent)
        {
            var b = _reservedBufferBytes + bytesToRent;
            var c = _activeMessageCount + 1;
            if (b > maxUseBuffer || c > maxUseMsges)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        internal void Release(ClientSendPermit permit)
        {
            List<ClientSendPermit>? toRelease = null;

            lock (_lock)
            {
                _reservedBufferBytes -= permit.RentBytes;
                _activeMessageCount--;

                while (_pendingPermits.Count > 0)
                {
                    var next = _pendingPermits.Peek();
                    if (!CheckSpace(next.RentBytes))
                        break;

                    _pendingPermits.Dequeue();
                    _reservedBufferBytes += next.RentBytes;
                    _activeMessageCount++;

                    (toRelease ??= []).Add(next);
                }
            }

            if (toRelease != null)
            {
                foreach (var permitItem in toRelease)
                    permitItem.SetResult(permitItem);
            }
        }

        internal void CancelAll()
        {
            lock (_lock)
            {
                foreach (var permit in _pendingPermits)
                {
                    permit.Cancel = true;
                }
            }
        }

        ClientSendPermit Rent()
        {
            if (_pool.TryPop(out var permit))
                return permit;
            else
                return new ClientSendPermit();
        }

        void Return(ClientSendPermit permit)
        {
            lock (_lock)
            {
                if (_pool.Count < maxUseMsges)
                    _pool.Push(permit);
            }
        }

        internal sealed class ClientSendPermit : IValueTaskSource<ClientSendPermit>
        {
            ManualResetValueTaskSourceCore<ClientSendPermit> _core;
            Action<ClientSendPermit>? _onCompleted;
            public int RentBytes { get; private set; } = 0;
            public bool Cancel { get; set; }

            public ClientSendPermit()
            {
                _core = new ManualResetValueTaskSourceCore<ClientSendPermit> { RunContinuationsAsynchronously = true };
            }

            public void Reset(Action<ClientSendPermit>? onCompleted, int bytesToRent)
            {
                _core.Reset();
                _onCompleted = onCompleted;
                RentBytes = bytesToRent;
                Cancel = false;
            }

            public void SetResult(ClientSendPermit result) => _core.SetResult(result);

            public ValueTask<ClientSendPermit> AsValueTask() => new(this, _core.Version);

            public ClientSendPermit GetResult(short token)
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
