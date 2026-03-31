// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class ESendHeartbeatPool
    {
        readonly ConcurrentStack<ESendHeartbeat> _pool = new();
        internal ESendHeartbeat Rent()
        {
            if (_pool.TryPop(out var s))
                return s;
            else
                return new ESendHeartbeat();
        }

        internal void Return(ESendHeartbeat? obj)
        {
            if (obj == null) return;
            _pool.Push(obj);
        }
    }

    internal class ESendHeartbeat
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
