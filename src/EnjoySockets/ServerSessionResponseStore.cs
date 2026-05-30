// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    internal class ServerSessionResponseStore
    {
        readonly (ulong sessionId, (ulong, long?) response)[] _ring;
        readonly Dictionary<ulong, int> _indexMap;
        int _head;

        internal ServerSessionResponseStore(int maxSize)
        {
            _ring = new (ulong, (ulong, long?))[maxSize];
            _indexMap = new Dictionary<ulong, int>(maxSize);
        }

        internal void Store(ulong sessionId, ulong typeMsg, long? msg)
        {
            if (_indexMap.ContainsKey(sessionId))
                return;

            var old = _ring[_head];
            _indexMap.Remove(old.sessionId);

            _ring[_head] = (sessionId, (typeMsg, msg));
            _indexMap[sessionId] = _head;

            _head = (_head + 1) % _ring.Length;
        }

        internal bool TryGet(ulong sessionId, out (ulong, long?) response)
        {
            if (_indexMap.TryGetValue(sessionId, out var idx))
            {
                var entry = _ring[idx];
                if (entry.sessionId == sessionId)
                {
                    response = entry.response;
                    return true;
                }
            }

            response = default;
            return false;
        }

        internal void Clear()
        {
            _indexMap.Clear();
        }
    }
}
