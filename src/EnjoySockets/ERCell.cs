// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers;
using System.Reflection;
using System.Reflection.Emit;

namespace EnjoySockets
{
    public enum EDataForm : byte
    {
        Msg = 0,
        Special = 1
    }

    internal sealed class ERCell
    {
        public ETCPSocketType SocketType { get; private set; }
        public EAttr AttrMethod { get; private set; }
        public Type ClassType { get; private set; }
        public MethodInfo MethodInfo { get; private set; }
        public Type? ArgType { get; private set; }
        public bool ArgAllowNull { get; private set; }
        public EObjPool? ArgObjectsPool { get; private set; }
        public Func<object, object[], object>? Execute { get; private set; }

        internal ERCell(ETCPSocketType socketType, EAttr eReceive, MethodInfo mInfo, Type? argType, bool argAllowNull, EObjPool? pool, Type classType)
        {
            SocketType = socketType;
            AttrMethod = eReceive;
            ArgType = argType;
            MethodInfo = mInfo;
            ClassType = classType;
            ArgAllowNull = argAllowNull;
            ArgObjectsPool = pool;
            if (ETCPServer.IsJIT)
                Execute = CreateInvoker(MethodInfo);
        }

        Func<object, object[], object>? CreateInvoker(MethodInfo method)
        {
            try
            {
                var dm = new DynamicMethod(
                    "Invoker",
                    typeof(object),
                    [typeof(object), typeof(object[])],
                    true
                );

                var il = dm.GetILGenerator();

                var parameters = method.GetParameters();

                if (!method.IsStatic)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, method.DeclaringType!);
                }

                for (int i = 0; i < parameters.Length; i++)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldelem_Ref);

                    var paramType = parameters[i].ParameterType;

                    if (paramType.IsValueType)
                        il.Emit(OpCodes.Unbox_Any, paramType);
                    else
                        il.Emit(OpCodes.Castclass, paramType);
                }

                if (method.IsVirtual)
                    il.Emit(OpCodes.Callvirt, method);
                else
                    il.Emit(OpCodes.Call, method);

                if (method.ReturnType == typeof(void))
                {
                    il.Emit(OpCodes.Ldnull);
                }
                else if (method.ReturnType.IsValueType)
                {
                    il.Emit(OpCodes.Box, method.ReturnType);
                }

                il.Emit(OpCodes.Ret);

                return (Func<object, object[], object>)dm.CreateDelegate(typeof(Func<object, object[], object>));
            }
            catch
            {
                ETCPServer.IsJIT = false;
                return null;
            }
        }

        internal bool Deserialize(ReadOnlySpan<byte> bytesData, ref object? obj)
        {
            if (ArgType == null)
                return true;

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
            if (ArgType == null)
                return null;

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
