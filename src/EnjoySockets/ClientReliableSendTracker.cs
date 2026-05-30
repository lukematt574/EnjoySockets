// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Collections.Concurrent;

namespace EnjoySockets
{
    /// <summary>
    /// Tracks pending reliable client send operations and manages their lifecycle, completion, and retransmission state.
    /// </summary>
    internal class ClientReliableSendTracker
    {
        readonly ConcurrentStack<ClientReliableSendContext> _pool = new();
        internal ClientReliableSendContext Rent()
        {
            if (_pool.TryPop(out var context))
                return context;
            else
                return new ClientReliableSendContext();
        }

        internal void Return(ClientReliableSendContext? obj)
        {
            if (obj == null) return;
            obj.Msg?.Clear();
            obj.Msg = null;
            obj.Session = 0;
            obj.Target = 0;
            obj.Instance = 0;
            obj.TotalBytes = 0;
            obj.IsRetryable = false;
            _pool.Push(obj);
        }

        ConcurrentDictionary<ulong, ClientReliableSendContext> _buffer = new();
        internal ClientReliableSendContext? Get(ulong session, int totalBytes)
        {
            var obj = Rent();
            obj.Reset();

            obj.Session = session;
            obj.TotalBytes = totalBytes;
            obj.IsRetryable = false;

            if (!_buffer.TryAdd(session, obj))
            {
                Return(obj);
                return null;
            }
            return obj;
        }

        internal ClientReliableSendContext? Get(ulong session, ulong target, long instance, MemorySegment? msg)
        {
            var obj = Rent();
            obj.Reset();

            obj.Session = session;
            obj.Target = target;
            obj.Msg = msg;
            obj.Instance = instance;
            obj.TotalBytes = msg?.WrittenBytes ?? 0;
            obj.IsRetryable = true;

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
            if (_buffer.TryRemove(session, out ClientReliableSendContext? sender))
                Return(sender);
        }

        internal ClientReliableSendContext? SetBrokeMsgToSend(ulong session, long? offset)
        {
            if (offset == null)
                return null;

            if (_buffer.TryGetValue(session, out ClientReliableSendContext? context))
            {
                if (!context.IsRetryable || offset < 0 || context.Target == 0)
                {
                    return null;
                }
                return context;
            }
            return null;
        }

        internal void SetEndMsg(ulong session, long? msg)
        {
            if (_buffer.TryGetValue(session, out ClientReliableSendContext? context))
            {
                context.SetResult(msg ?? 0);
            }
            else
            {
                if (_buffer.ContainsKey(session))
                {
                    while (!_buffer.TryGetValue(session, out context))
                        Thread.SpinWait(1);

                    context.SetResult(msg ?? 0);
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
