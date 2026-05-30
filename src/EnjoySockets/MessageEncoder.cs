// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    internal class MessageEncoder
    {
        readonly EArrayBufferWriter _bufferWriter;
        readonly MemorySegmentPool _memorySegmentPool;
        readonly object _lock = new();

        int _messageBufferSize;

        internal MessageEncoder(EConfig config, MemorySegmentPool segmentPool)
        {
            _messageBufferSize = config.MessageBuffer < 2 ? 2048 : config.MessageBuffer * 1024;
            _bufferWriter = new EArrayBufferWriter(_messageBufferSize);
            _memorySegmentPool = segmentPool;
        }

        internal MemorySegment? EncodeToSegments(object obj, Type t, IESerializer serializer)
        {
            lock (_lock)
            {
                if (serializer.Serialize(_bufferWriter, obj, t) == 0 || _bufferWriter.WrittenCount > _messageBufferSize)
                    return null;

                var toWrite = _memorySegmentPool.Rent();
                toWrite.Append(_bufferWriter.WrittenSpan);

                return toWrite;
            }
        }
    }
}
