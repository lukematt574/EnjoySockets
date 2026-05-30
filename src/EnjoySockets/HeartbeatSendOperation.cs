// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class HeartbeatSendOperationPool
    {
        readonly ConcurrentStack<HeartbeatSendOperation> _pool = new();
        internal HeartbeatSendOperation Rent()
        {
            if (_pool.TryPop(out var operation))
                return operation;
            else
                return new HeartbeatSendOperation();
        }

        internal void Return(HeartbeatSendOperation? operation)
        {
            if (operation == null) return;
            _pool.Push(operation);
        }
    }

    internal class HeartbeatSendOperation
    {
        Func<ValueTask<bool>>? Func;

        internal void RunPrepare(Func<ValueTask<bool>> _task)
        {
            Func = _task;
        }

        internal ValueTask<long> Run()
        {
            if (Func == null)
                return ValueTask.FromResult<long>(-1);

            ValueTask<bool> vt = Func.Invoke();

            if (vt.IsCompletedSuccessfully)
                return ValueTask.FromResult<long>(vt.Result ? 0 : -1);

            return RunAsync(vt);
        }

        async ValueTask<long> RunAsync(ValueTask<bool> vt)
        {
            bool result = await vt;
            return result ? 0 : -1;
        }
    }
}
