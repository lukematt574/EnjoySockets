// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class MessageSendOperationPool
    {
        readonly ConcurrentStack<MessageSendOperation> _pool = new();
        internal MessageSendOperation Rent()
        {
            if (_pool.TryPop(out var operation))
                return operation;
            else
                return new MessageSendOperation();
        }

        internal void Return(MessageSendOperation? operation)
        {
            if (operation == null) return;
            _pool.Push(operation);
        }
    }

    internal class MessageSendOperation
    {
        internal ulong Target { get; private set; }
        internal long Instance { get; private set; }
        internal int TotalBytes { get; private set; }
        internal ulong Session { get; set; }

        Func<MessageSendOperation, ValueTask<bool>>? Func;
        MemorySegment? CurrentSegment;
        int CurrentSegmentIndex;
        int ToWrite;

        internal void RunPrepare(Func<MessageSendOperation, ValueTask<bool>> _task, ulong target, MemorySegment? firstSegment, long instance)
        {
            Func = _task;
            Target = target;
            Session = 0;
            Instance = instance;
            CurrentSegment = firstSegment;
            CurrentSegmentIndex = 0;
            ToWrite = TotalBytes = firstSegment != null ? firstSegment.WrittenBytes : 0;
        }

        internal int FillSpan(Span<byte> data)
        {
            if (ToWrite < 1)
                return 0;

            int toCopy = Math.Min(data.Length, ToWrite);
            if (CurrentSegment != null)
                CurrentSegment = MemorySegment.FillSpan(data, CurrentSegment, ref CurrentSegmentIndex, toCopy);

            ToWrite -= toCopy;
            return toCopy;
        }

        internal void SetToWriteAndSession(long offset, ulong session)
        {
            int _offset = (int)offset;
            ToWrite = TotalBytes - _offset;
            ToWrite = ToWrite < 1 ? 0 : ToWrite;
            if (ToWrite > 0)
            {
                CurrentSegmentIndex = _offset;
                if (CurrentSegment != null)
                    CurrentSegment = CurrentSegment.GetOffset(ref CurrentSegmentIndex);
            }
            Session = session;
        }

        internal ValueTask<long> Run()
        {
            if (Func == null)
                return ValueTask.FromResult<long>(-1);

            ValueTask<bool> vt = Func.Invoke(this);

            if (vt.IsCompletedSuccessfully)
            {
                if (!vt.Result)
                    ToWrite = -1;

                Cleanup();
                return ValueTask.FromResult((long)ToWrite);
            }

            return RunAsync(vt);
        }

        async ValueTask<long> RunAsync(ValueTask<bool> vt)
        {
            if (!await vt)
                ToWrite = -1;

            Cleanup();
            return ToWrite;
        }

        void Cleanup()
        {
            if (ToWrite < 1)
            {
                CurrentSegment = null;
            }
        }
    }
}
