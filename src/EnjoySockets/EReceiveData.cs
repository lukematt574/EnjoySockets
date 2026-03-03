// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using EnjoySockets.DTO;

namespace EnjoySockets
{
    internal abstract class EReceiveData
    {
        public EDataForm Form { get; protected set; }
        public ulong Session { get; private set; }
        public int TotalBytes { get; private set; }
        public long Instance { get; private set; }
        public object? InstanceObj { get; private set; }
        public ESocketResource? ESocketResourceObj { get; private set; }

        internal ERCell? Cell { get; private protected set; }
        internal object? User { get; private set; }
        internal bool InChannel { get; set; }
        internal long Response { get; set; } = -1;

        private protected EBufferControl? BufferControl { get; set; }
        private protected int _rentBytes = 0;

        protected object?[] _tab1Param = new object[1];
        protected object?[] _tab2Params = new object[2];
        protected object? FinalArg;

        internal void Initialize(object? user, ERCell cell, EBufferControl? buffer, object? instanceObj, int firstRentBytes, ESocketResource eSocketResource)
        {
            Cell = cell;
            User = user;
            BufferControl = buffer;
            InstanceObj = instanceObj;
            ESocketResourceObj = eSocketResource;
            _rentBytes = firstRentBytes;
            InChannel = false;
        }

        protected void SetBasicData(PartDataDTO part)
        {
            Session = part.Session;
            TotalBytes = part.TotalBytes;
            Instance = part.Instance;
        }

        internal virtual int TryPushPart(PartDataDTO part) { return 0; }
        internal ValueTask<long> Run()
        {
            var cell = Cell!;
            try
            {
                if (FinalArg == null && !cell.ArgAllowNull)
                    return ValueTask.FromResult(Response = -1);

                // TODO: Replace reflection with source-generated delegate invocation.
                var result = cell.MethodInfo.Invoke(InstanceObj, GetArgs());

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
                Cell?.ReturnArgToPool(FinalArg);

            FinalArg = null;

            BufferControl?.Return(_rentBytes);
            _rentBytes = 0;

            Response = -1;
            BufferControl = null;
            Cell = null;
            User = null;
            ESocketResourceObj = null;
            InstanceObj = null;
        }
    }
}
