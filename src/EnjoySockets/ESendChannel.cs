namespace EnjoySockets
{
    internal class ESendChannel
    {
        readonly ESocketResource _user;
        readonly ESingleTaskSemaphore _semaphore;

        internal ESendChannel(ESocketResource user)
        {
            _user = user;
            _semaphore = new();
        }

        internal ValueTask<ulong> TrySendMsgAndGetSession(Func<ESendMsg, ValueTask<bool>> _task, ulong target, EMemorySegment? segments, long instance)
        {
            var obj = ESendMsg.Rent();
            obj.RunPrepare(_task, target, segments, instance);
            return TrySendMsgRun(obj);
        }

        internal ValueTask<ulong> TrySendMsgAndGetSession(Func<ESendMsg, ValueTask<bool>> _task, ulong target, ReadOnlyMemory<byte>? segments, long instance)
        {
            var obj = ESendMsg.Rent();
            obj.RunPrepare(_task, target, segments, instance);
            return TrySendMsgRun(obj);
        }

        internal ValueTask<ulong> TrySendMsgAndGetSession(ESendMsg msg)
        {
            return TrySendMsgRun(msg);
        }

        async ValueTask<ulong> TrySendMsgRun(ESendMsg obj)
        {
            try
            {
                while (true)
                {
                    try
                    {
                        if (!_semaphore.TryWait())
                            await _semaphore.Wait();

                        if (!_user.Running)
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
                ESendMsg.Return(obj);
            }
        }

        internal async ValueTask<bool> TrySendSpecial(Func<ulong, ulong, long?, ValueTask<bool>> _task, ulong session, ulong typeMsg, long? msg)
        {
            var obj = ESendSpecial.Rent();
            obj.RunPrepare(_task, session, typeMsg, msg);
            try
            {
                if (!_semaphore.TryWait())
                    await _semaphore.Wait();

                if (!_user.Running)
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
                ESendSpecial.Return(obj);
            }
        }

        internal async ValueTask<bool> TrySendHeartbeat(Func<ValueTask<bool>> _task)
        {
            var obj = ESendHeartbeat.Rent();
            obj.RunPrepare(_task);
            try
            {
                if (!_semaphore.TryWait())
                    await _semaphore.Wait();

                if (!_user.Running)
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
                ESendHeartbeat.Return(obj);
            }
        }
    }
}
