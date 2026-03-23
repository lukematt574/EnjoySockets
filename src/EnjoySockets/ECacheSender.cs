// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
            obj.Session = 0;
            obj.Target = 0;
            obj.Instance = 0;
            obj.TotalBytes = 0;
            obj.Repeat = false;
            _pool.Push(obj);
        }

        ConcurrentDictionary<ulong, ESender> _buffer = new();
        internal ESender? Get(ulong session, int totalBytes)
        {
            var obj = Rent();
            obj.Reset();

            obj.Session = session;
            obj.TotalBytes = totalBytes;
            obj.Repeat = false;

            if (!_buffer.TryAdd(session, obj))
            {
                Return(obj);
                return null;
            }
            return obj;
        }

        internal ESender? Get(ulong session, ulong target, long instance, EMemorySegment? msg)
        {
            var obj = Rent();
            obj.Reset();

            obj.Session = session;
            obj.Target = target;
            obj.Msg = msg;
            obj.Instance = obj.Instance;
            obj.TotalBytes = msg?.WrittenBytes ?? 0;
            obj.Repeat = true;

            if (!_buffer.TryAdd(session, obj))
            {
                Return(obj);
                return null;
            }
            return obj;
        }

        internal List<ulong> GetNonReceivedSessions()
        {
            return [.. _buffer.Select(x => x.Key)];
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
                if (!sender.Repeat || offset < 0 || sender.Target == 0)
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
            else
            {
                if (_buffer.ContainsKey(session))
                {
                    while (!_buffer.TryGetValue(session, out sender))
                        Thread.SpinWait(1);

                    sender.SetResult(msg ?? 0);
                }
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
