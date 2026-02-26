using System.Collections.Concurrent;

namespace EnjoySockets
{
    public sealed class EObjPool
    {
        public Type ElementType { get; }
        public uint MaxObjs { get; }

        readonly ConcurrentStack<object> _pool = new();
        long _current;

        public EObjPool(Type type, uint maxObjs)
        {
            ElementType = type;
            MaxObjs = maxObjs;
        }

        public object? Rent()
        {
            if (_pool.TryPop(out var s))
            {
                if (MaxObjs != 0)
                    Interlocked.Decrement(ref _current);
                return s;
            }

            try
            {
                return Activator.CreateInstance(ElementType);
            }
            catch { return null; }
        }

        public void Return(object? obj)
        {
            if (obj == null || obj.GetType() != ElementType)
                return;

            if (MaxObjs == 0)
            {
                _pool.Push(obj);
                return;
            }

            if (_current >= MaxObjs)
                return;

            Interlocked.Increment(ref _current);
            _pool.Push(obj);
        }

        /// <summary>
        /// Check type support
        /// </summary>
        /// <returns>false - no support, true - support</returns>
        public static bool CheckType(Type type)
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
