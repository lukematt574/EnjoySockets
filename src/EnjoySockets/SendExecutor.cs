// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    internal class SendExecutor
    {
        readonly ESocketResource _socket;
        readonly SingleEntrySemaphore _semaphore;

        readonly SpecialSendOperationPool _specialMessagePool;
        readonly HeartbeatSendOperationPool _heartbeatMessagePool;
        internal MessageSendOperationPool _messagePool { get; private set; }

        internal SendExecutor(ESocketResource socket)
        {
            _socket = socket;
            _specialMessagePool = new SpecialSendOperationPool();
            _heartbeatMessagePool = new HeartbeatSendOperationPool();
            _messagePool = new MessageSendOperationPool();
            _semaphore = new();
        }

        internal ValueTask<ulong> TrySendMsgAndGetSession(Func<MessageSendOperation, ValueTask<bool>> _task, ulong target, MemorySegment? segments, long instance)
        {
            var obj = _messagePool.Rent();
            obj.RunPrepare(_task, target, segments, instance);
            return TrySendMsgRun(obj);
        }

        internal ValueTask<ulong> TrySendMsgAndGetSession(MessageSendOperation msg)
        {
            return TrySendMsgRun(msg);
        }

        async ValueTask<ulong> TrySendMsgRun(MessageSendOperation obj)
        {
            try
            {
                while (true)
                {
                    try
                    {
                        if (!_semaphore.TryWait())
                            await _semaphore.Wait();

                        if (!_socket.Running)
                            return 0;

                        long result;
                        var vr = obj.Run();
                        if (vr.IsCompletedSuccessfully)
                            result = vr.Result;
                        else
                            result = await vr;

                        if (result < 1)
                        {
                            if (result < 0)
                                return 0;
                            return obj.Session;
                        }
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
            }
            catch
            {
                return 0;
            }
            finally
            {
                _messagePool.Return(obj);
            }
        }

        internal async ValueTask<bool> TrySendSpecial(Func<ulong, ulong, long?, ValueTask<bool>> _task, ulong session, ulong typeMsg, long? msg)
        {
            var obj = _specialMessagePool.Rent();
            obj.RunPrepare(_task, session, typeMsg, msg);
            try
            {
                if (!_semaphore.TryWait())
                    await _semaphore.Wait();

                if (!_socket.Running)
                    return false;

                var vr = obj.Run();
                if (vr.IsCompletedSuccessfully)
                    return vr.Result >= 0;
                else
                    return await vr >= 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                _semaphore.Release();
                _specialMessagePool.Return(obj);
            }
        }

        internal async ValueTask<bool> TrySendHeartbeat(Func<ValueTask<bool>> _task)
        {
            var obj = _heartbeatMessagePool.Rent();
            obj.RunPrepare(_task);
            try
            {
                if (!_semaphore.TryWait())
                    await _semaphore.Wait();

                if (!_socket.Running)
                    return false;

                var vr = obj.Run();
                if (vr.IsCompletedSuccessfully)
                    return vr.Result >= 0;
                else
                    return await vr >= 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                _semaphore.Release();
                _heartbeatMessagePool.Return(obj);
            }
        }
    }
}
