// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    internal class ESerializeMsg
    {
        object _lock = new();
        int _msgBuffer;
        readonly EArrayBufferWriter _bufferWriter;
        readonly EMemorySegmentPool _memorySegmentPool;

        public ESerializeMsg(ETCPConfig config, EMemorySegmentPool segmentPool)
        {
            _msgBuffer = config.MessageBuffer < 2 ? 2048 : config.MessageBuffer * 1024;
            _bufferWriter = new EArrayBufferWriter(_msgBuffer);
            _memorySegmentPool = segmentPool;
        }

        public EMemorySegment? ObjToSegments(object obj, Type t)
        {
            lock (_lock)
            {
                if (ESerial.Serialize(_bufferWriter, obj, t) == 0 || _bufferWriter.WrittenCount > _msgBuffer)
                    return null;

                var toWrite = _memorySegmentPool.Rent();
                toWrite.Append(_bufferWriter.WrittenSpan);

                return toWrite;
            }
        }
    }
}
