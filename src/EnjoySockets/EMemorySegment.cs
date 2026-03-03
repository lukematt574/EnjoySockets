// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers;
using System.Collections.Concurrent;

namespace EnjoySockets
{
    /// <summary>
    /// Internal pooled buffer. Single-owner, NOT thread-safe.
    /// After calling Clear(), the instance must not be used.
    /// </summary>
    internal class EMemorySegment : ReadOnlySequenceSegment<byte>
    {
        const int _segmentSize = 2048;

        public EMemorySegment? ENext { get; protected set; }
        public int RentBytes { get; private set; } = _segmentSize;
        public int WrittenBytes { get; private set; }

        readonly byte[] _buffer = new byte[_segmentSize];
        int _freeSpaceBuffer = _segmentSize;
        int _usedBytes = 0;

        EMemorySegment()
        {
            Memory = _buffer.AsMemory();
        }

        static readonly ConcurrentStack<EMemorySegment> _pool = new();

        static EMemorySegment Rent() => _pool.TryPop(out var s) ? s : new EMemorySegment();
        static void Return(EMemorySegment obj) => _pool.Push(obj);

        /// <summary>
        /// Return initial segment
        /// </summary>
        internal static EMemorySegment GetFirstSegment()
        {
            return Rent();
        }

        internal static EMemorySegment? FillToList(List<byte> list, EMemorySegment? segment, ref int offset, int maxRead)
        {
            var current = segment;
            int bytesRead = 0;

            while (current != null && bytesRead < maxRead)
            {
                int available = current._usedBytes - offset;
                if (available > 0)
                {
                    int toCopy = Math.Min(maxRead - bytesRead, available);
#if NET8_0
                    list.AddRange(current._buffer.AsSpan(offset, toCopy));
#else
                    var span = current._buffer.AsSpan(offset, toCopy);
                    for (int i = 0; i < span.Length; i++)
                        list.Add(span[i]);
#endif
                    bytesRead += toCopy;

                    if (bytesRead == maxRead)
                    {
                        offset += toCopy;
                        break;
                    }
                }

                current = current.ENext;
                offset = 0;
            }
            return current;
        }

        internal void Append(ReadOnlySpan<byte> chunk)
        {
            var tail = GetTail();
            while (!chunk.IsEmpty)
            {
                var appended = tail.TryAppend(chunk);
                WrittenBytes += appended;
                if (appended < 1)
                {
                    var next = GetFirstSegment();
                    RentBytes += _segmentSize;
                    next.RunningIndex = tail.RunningIndex + tail.Memory.Length;
                    tail.SetNext(next);
                    tail = next;
                }
                else
                {
                    chunk = chunk.Slice(appended);
                }
            }
        }

        /// <summary>
        /// Append span to buffer
        /// </summary>
        /// <returns>number of append bytes</returns>
        int TryAppend(ReadOnlySpan<byte> span)
        {
            int toAppend = _freeSpaceBuffer < span.Length ? _freeSpaceBuffer : span.Length;
            if (toAppend > 0)
            {
                span.Slice(0, toAppend).CopyTo(_buffer.AsSpan(_usedBytes, toAppend));
                _usedBytes += toAppend;
                _freeSpaceBuffer -= toAppend;
                return toAppend;
            }
            return 0;
        }

        /// <summary>
        /// Builds ReadOnlySequence{byte} from append bytes
        /// </summary>
        internal ReadOnlySequence<byte> Read()
        {
            var tail = GetTail();
            return new ReadOnlySequence<byte>(this, 0, tail, tail._usedBytes);
        }

        EMemorySegment GetTail()
        {
            var current = this;
            while (current.ENext is not null)
                current = current.ENext;
            return current;
        }

        void SetNext(EMemorySegment? segment)
        {
            Next = ENext = segment;
        }

        /// <summary>
        /// Returns all segments with self (Don't use this object any more, just get a new one by 'GetFirstSegment')
        /// </summary>
        internal void Clear()
        {
            var current = this;
            while (current != null)
            {
                var next = current.ENext;
                current._freeSpaceBuffer = _segmentSize;
                current._usedBytes = 0;
                current.RentBytes = _segmentSize;
                current.WrittenBytes = 0;
                current.SetNext(null);
                current.RunningIndex = 0;

                var toReturn = current;
                current = next;

                Return(toReturn);
            }
        }
    }
}
