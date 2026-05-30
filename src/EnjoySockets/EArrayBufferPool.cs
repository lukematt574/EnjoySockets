// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class EArrayBufferPool
    {
        static ConcurrentDictionary<int, EArrayBufferPool> _pools = new();

        internal static EArrayBufferPool GetPool(int capacity)
        {
            return _pools.GetOrAdd(capacity, GetPoolRun);
        }

        static EArrayBufferPool GetPoolRun(int capacity)
        {
            return new EArrayBufferPool(capacity);
        }

        internal int Capacity { get; private set; }

        readonly ConcurrentStack<EArrayBufferWriter> _pool = new();

        EArrayBufferPool(int capacity)
        {
            Capacity = capacity;
        }

        internal EArrayBufferWriter Rent()
        {
            if (_pool.TryPop(out var buffer))
            {
                return buffer;
            }
            else
                return new(Capacity);
        }

        internal void Return(EArrayBufferWriter? buffer)
        {
            if (buffer == null || buffer.Capacity != Capacity)
                return;
            _pool.Push(buffer);
        }
    }
}
