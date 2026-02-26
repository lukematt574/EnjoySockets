using System.Collections.Concurrent;

namespace EnjoySockets
{
    /// <summary>
    /// Control send buffer on client socket
    /// </summary>
    internal class ECacheSender
    {
        static readonly ConcurrentStack<ESender> _pool = new();
        internal static ESender Rent()
        {
            if (_pool.TryPop(out var s))
                return s;
            else
                return new ESender();
        }

        internal static void Return(ESender? obj)
        {
            if (obj == null) return;
            obj.Msg?.Clear();
            obj.Msg = null;
            obj.MsgBytes = null;
            obj.Target = 0;
            obj.Instance = 0;
            _pool.Push(obj);
        }

        ConcurrentDictionary<ulong, ESender> _buffer = new();
        internal ESender Get(ulong session, int totalBytes)
        {
            var obj = _buffer.GetOrAdd(session, GetESenderRun);
            obj.TotalBytes = totalBytes;
            obj.Target = 0;
            obj.Instance = 0;
            return obj;
        }

        internal ESender Get(ulong session, ulong target, long instance, EMemorySegment? msg)
        {
            var obj = _buffer.GetOrAdd(session, GetESenderRun);
            obj.Target = target;
            obj.Msg = msg;
            obj.Instance = obj.Instance;
            obj.TotalBytes = msg?.WrittenBytes ?? 0;
            return obj;
        }

        internal List<ulong> GetNonReceivedSessions()
        {
            return [.. _buffer.Select(x => x.Key)];
        }

        internal static ESender GetESenderRun(ulong session)
        {
            var obj = Rent();
            obj.Reset();
            obj.Session = session;
            return obj;
        }

        internal void Remove(ulong session)
        {
            if (_buffer.TryRemove(session, out ESender? sender))
                Return(sender);
        }

        internal ESender? SetBrokeMsgToSend(ulong session, long? offset)
        {
            if (offset == null)
                return null;


            if (_buffer.TryGetValue(session, out ESender? sender))
            {
                if (offset >= sender.TotalBytes || offset < 0 || (sender.Msg == null && sender.MsgBytes == null) || sender.Target == 0)
                {
                    return null;
                }
                return sender;
            }
            return null;
        }

        internal void SetEndMsg(ulong session, long? msg)
        {
            if (_buffer.TryGetValue(session, out ESender? sender))
            {
                sender.SetResult(msg ?? 0);
            }
        }

        internal void Clear()
        {
            foreach (var item in _buffer.Values)
            {
                item.SetResult(-3);
            }
        }
    }
}
