// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class ESendSpecial
    {
        static readonly ConcurrentStack<ESendSpecial> _pool = new();
        internal static ESendSpecial Rent()
        {
            if (_pool.TryPop(out var s))
                return s;
            else
                return new ESendSpecial();
        }

        internal static void Return(ESendSpecial? obj)
        {
            if (obj == null) return;
            _pool.Push(obj);
        }

        Func<ulong, ulong, long?, ValueTask<bool>>? Func;
        ulong Session, TypeMsg;
        long? Msg;

        internal void RunPrepare(Func<ulong, ulong, long?, ValueTask<bool>> _task, ulong session, ulong typeMsg, long? msg)
        {
            Func = _task;
            Session = session;
            Msg = msg;
            TypeMsg = typeMsg;
        }

        internal ValueTask<long> Run()
        {
            if (Func == null)
                return ValueTask.FromResult<long>(-1);

            ValueTask<bool> vt = Func.Invoke(Session, TypeMsg, Msg);

            if (vt.IsCompletedSuccessfully)
            {
                return ValueTask.FromResult<long>(vt.Result ? 0 : -1);
            }

            return RunAsync(vt);
        }

        async ValueTask<long> RunAsync(ValueTask<bool> vt)
        {
            bool result = await vt;
            return result ? 0 : -1;
        }
    }
}
