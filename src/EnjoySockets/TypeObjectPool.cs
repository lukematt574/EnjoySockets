// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal sealed class TypeObjectPool
    {
        internal Type ObjectType { get; }
        internal uint MaxCapacity { get; }

        readonly ConcurrentStack<object> _pool = new();
        long _current;

        internal TypeObjectPool(Type type, uint maxCapacity)
        {
            ObjectType = type;
            MaxCapacity = maxCapacity;
        }

        internal object? Rent()
        {
            if (_pool.TryPop(out var obj))
            {
                if (MaxCapacity != 0)
                    Interlocked.Decrement(ref _current);
                return obj;
            }

            try
            {
                return Activator.CreateInstance(ObjectType);
            }
            catch { return null; }
        }

        internal void Return(object? obj)
        {
            if (obj == null || obj.GetType() != ObjectType)
                return;

            if (MaxCapacity == 0)
            {
                _pool.Push(obj);
                return;
            }

            if (_current >= MaxCapacity)
                return;

            Interlocked.Increment(ref _current);
            _pool.Push(obj);
        }

        /// <summary>
        /// Check type support
        /// </summary>
        /// <returns>false - no support, true - support</returns>
        internal static bool CheckType(Type type)
        {
            try
            {
                _ = Activator.CreateInstance(type);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
