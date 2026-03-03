// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers;
using System.Reflection;

namespace EnjoySockets
{
    public enum EDataForm
    {
        Msg, Special
    }

    internal sealed class ERCell
    {
        public string Target { get; private set; }
        public ulong IdTarget { get; private set; }
        public ETCPSocketType SocketType { get; private set; }
        public EAttr AttrMethod { get; private set; }
        public Type ClassType { get; private set; }
        public MethodInfo MethodInfo { get; private set; }
        public Type? ArgType { get; private set; }
        public Type? ArgReturn { get; private set; }
        public bool ArgAllowNull { get; private set; }
        public EObjPool? ArgObjectsPool { get; private set; }

        internal ERCell(ulong idTarget, ETCPSocketType socketType, EAttr eReceive, MethodInfo mInfo, Type? argType, bool argAllowNull, EObjPool? pool, Type classType)
        {
            Target = mInfo.Name;
            IdTarget = idTarget;
            SocketType = socketType;
            AttrMethod = eReceive;
            ArgType = argType;
            MethodInfo = mInfo;
            ClassType = classType;
            ArgAllowNull = argAllowNull;
            ArgObjectsPool = pool;
            ArgReturn = MethodInfo.ReturnType;
        }

        internal bool Deserialize(Span<byte> bytesData, ref object? obj)
        {
            if (ArgType == null) return true;
            if (ArgObjectsPool != null)
            {
                obj = ArgObjectsPool.Rent();
                if (!ESerial.Deserialize(ArgType, bytesData, ref obj))
                {
                    ArgObjectsPool.Return(obj);
                    return ArgAllowNull;
                }
                return true;
            }
            else
            {
                obj = ESerial.Deserialize(bytesData, ArgType);
                if (obj == null)
                    return ArgAllowNull;
                else
                    return true;
            }
        }

        internal object? Deserialize(ReadOnlySequence<byte> bytesData)
        {
            if (ArgType == null) return null;
            if (ArgObjectsPool != null)
            {
                var obj = ArgObjectsPool.Rent();
                if (!ESerial.Deserialize(bytesData, ArgType, ref obj))
                {
                    ArgObjectsPool.Return(obj);
                    return null;
                }
                return obj;
            }
            else
                return ESerial.Deserialize(bytesData, ArgType);
        }

        internal void ReturnArgToPool(object? obj)
        {
            ArgObjectsPool?.Return(obj);
        }
    }
}
