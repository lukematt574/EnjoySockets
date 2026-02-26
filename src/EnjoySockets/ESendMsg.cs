using System.Collections.Concurrent;

namespace EnjoySockets
{
    internal class ESendMsg
    {
        static readonly ConcurrentStack<ESendMsg> _pool = new();
        internal static ESendMsg Rent()
        {
            if (_pool.TryPop(out var s))
                return s;
            else
                return new ESendMsg();
        }

        internal static void Return(ESendMsg? obj)
        {
            if (obj == null) return;
            _pool.Push(obj);
        }

        public ulong Target { get; private set; }
        public long Instance { get; private set; }
        public int TotalBytes { get; private set; }
        public ulong Session { get; set; }

        Func<ESendMsg, ValueTask<bool>>? Func;
        EMemorySegment? CurrentSegment;
        ReadOnlyMemory<byte>? CurrentSegmentBytes;
        int CurrentSegmentIndex;
        int ToWrite;

        internal void RunPrepare(Func<ESendMsg, ValueTask<bool>> _task, ulong target, EMemorySegment? firstSegment, long instance)
        {
            Func = _task;
            Target = target;
            Session = 0;
            Instance = instance;
            CurrentSegment = firstSegment;
            CurrentSegmentBytes = null;
            CurrentSegmentIndex = 0;
            ToWrite = TotalBytes = firstSegment != null ? firstSegment.WrittenBytes : 0;
        }

        internal void RunPrepare(Func<ESendMsg, ValueTask<bool>> _task, ulong target, ReadOnlyMemory<byte>? segment, long instance)
        {
            Func = _task;
            Target = target;
            Session = 0;
            Instance = instance;
            CurrentSegment = null;
            CurrentSegmentBytes = segment;
            CurrentSegmentIndex = 0;
            ToWrite = TotalBytes = CurrentSegmentBytes.HasValue ? CurrentSegmentBytes.Value.Length : 0;
        }

        internal void FillList(List<byte> list)
        {
            if (CurrentSegment == null)
            {
                if (CurrentSegmentBytes != null)
                {
                    int toCopy = Math.Min(ETCPSocket.MaxPayloadBytes, ToWrite);
#if NET8_0
                    list.AddRange(CurrentSegmentBytes.Value.Slice(CurrentSegmentIndex, toCopy).Span);
#else
                    var span = CurrentSegmentBytes.Value.Slice(CurrentSegmentIndex, toCopy).Span;
                    for (int i = 0; i < span.Length; i++)
                        list.Add(span[i]);
#endif
                    CurrentSegmentIndex += toCopy;
                }
            }
            else
                CurrentSegment = EMemorySegment.FillToList(list, CurrentSegment, ref CurrentSegmentIndex, ETCPSocket.MaxPayloadBytes);

            ToWrite -= list.Count;
        }

        internal void SetToWriteAndSession(long offset, ulong session)
        {
            ToWrite = TotalBytes - (int)offset;
            ToWrite = ToWrite < 1 ? 0 : ToWrite;
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
                CurrentSegmentBytes = null;
                CurrentSegment = null;
            }
        }
    }
}
