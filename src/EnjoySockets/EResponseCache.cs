namespace EnjoySockets
{
    internal class EResponseCache
    {
        readonly (ulong sessionId, (ulong, long?) response)[] _buffer;
        readonly Dictionary<ulong, int> _indexMap;
        int _head;

        internal EResponseCache(int maxSize)
        {
            _buffer = new (ulong, (ulong, long?))[maxSize];
            _indexMap = new Dictionary<ulong, int>(maxSize);
        }

        internal void Add(ulong sessionId, ulong typeMsg, long? msg)
        {
            if (_indexMap.ContainsKey(sessionId))
                return;

            var old = _buffer[_head];
            _indexMap.Remove(old.sessionId);

            _buffer[_head] = (sessionId, (typeMsg, msg));
            _indexMap[sessionId] = _head;

            _head = (_head + 1) % _buffer.Length;
        }

        internal bool TryGet(ulong sessionId, out (ulong, long?) response)
        {
            if (_indexMap.TryGetValue(sessionId, out var idx))
            {
                var entry = _buffer[idx];
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
