// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
namespace EnjoySockets
{
    internal abstract class DataReceiveOperation
    {
        public DataForm Form { get; protected set; }
        internal ulong Session { get; private set; }
        internal int TotalBytes { get; private set; }
        internal long Instance { get; private set; }
        internal object? InstanceObj { get; private set; }
        internal ESocketResource? ESocketResourceObj { get; private set; }

        internal DispatchHandler? Handler { get; private protected set; }
        internal object? User { get; private set; }
        internal bool InChannel { get; set; }
        internal long Response { get; set; } = -1;
        internal bool CorruptedArg { get; set; }

        private protected ServerBufferQuota? _serverBufferQuota { get; set; }
        private protected int _rentBytes = 0;

        protected object?[] _tab1Param = new object[1];
        protected object?[] _tab2Params = new object[2];
        protected object? FinalArg;

        internal void Initialize(object? user, DispatchHandler handler, ServerBufferQuota? buffer, object? instanceObj, int firstRentBytes, ESocketResource eSocketResource)
        {
            Handler = handler;
            User = user;
            _serverBufferQuota = buffer;
            InstanceObj = instanceObj;
            ESocketResourceObj = eSocketResource;
            _rentBytes = firstRentBytes;
            InChannel = false;
        }

        protected void SetBasicData(ReadOnlySpan<byte> part, long instance)
        {
            Session = ESocketResource.ReadSession(part);
            TotalBytes = ESocketResource.ReadTotalBytes(part);
            Instance = instance;
        }

        internal virtual int TryPushPart(ReadOnlySpan<byte> part, MemorySegmentPool pool, IESerializer serializer) { return 0; }
        internal ValueTask<long> Run()
        {
            var handler = Handler!;
            try
            {
                if (FinalArg == null && !handler.ArgAllowNull)
                {
                    CorruptedArg = true;
                    return ValueTask.FromResult(Response = -1);
                }

                // TODO: Replace reflection with source-generated delegate invocation.
                object? result = null;
                if (handler.Execute != null)
                    result = handler.Execute.Invoke(InstanceObj!, GetArgs()!);
                else
                    result = handler.MethodInfo.Invoke(InstanceObj, GetArgs());

                return result switch
                {
                    long l => ValueTask.FromResult(Response = l),
                    Task<long> taskLong => AwaitTaskLong(taskLong),
                    Task task => AwaitTask(task),
                    _ => ValueTask.FromResult(Response = 0)
                };
            }
            catch
            {
                CorruptedArg = true;
                return ValueTask.FromResult(Response = -1);
            }
        }

        private async ValueTask<long> AwaitTaskLong(Task<long> task)
        {
            var result = await task;
            return Response = result;
        }

        private async ValueTask<long> AwaitTask(Task task)
        {
            await task;
            return Response = 0;
        }

        internal virtual object?[]? GetArgs() { return null; }
        internal virtual void Dispose()
        {
            if (FinalArg != null)
                Handler?.ReturnArgToPool(FinalArg);

            FinalArg = null;

            _serverBufferQuota?.Return(_rentBytes);
            _rentBytes = 0;

            Response = -1;
            _serverBufferQuota = null;
            Handler = null;
            User = null;
            ESocketResourceObj = null;
            InstanceObj = null;
            CorruptedArg = false;
        }
    }
}
