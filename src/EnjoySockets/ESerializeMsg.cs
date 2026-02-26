namespace EnjoySockets
{
    internal class ESerializeMsg
    {
        object _lock = new();
        int _msgBuffer;
        readonly EArrayBufferPool _bufferPool;

        public ESerializeMsg(ETCPConfig config)
        {
            _msgBuffer = config.MessageBuffer < 2 ? 2048 : config.MessageBuffer * 1024;
            _bufferPool = EArrayBufferPool.GetPool(_msgBuffer);
        }

        public EMemorySegment? ObjToSegments(object obj, Type t)
        {
            lock (_lock)
            {
                var _buffer = _bufferPool.Rent();
                _buffer.ResetWrittenCount();
                try
                {
                    if (ESerial.Serialize(_buffer, obj, t) == 0 || _buffer.WrittenCount > _msgBuffer)
                        return null;

                    var toWrite = EMemorySegment.GetFirstSegment();
                    toWrite.Append(_buffer.WrittenSpan);

                    return toWrite;
                }
                finally
                {
                    _bufferPool.Return(_buffer);
                }
            }
        }
    }
}
