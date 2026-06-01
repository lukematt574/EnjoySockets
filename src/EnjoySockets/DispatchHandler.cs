// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace EnjoySockets
{
    internal enum DataForm : byte
    {
        Msg = 0,
        Special = 1,
        MsgResponse = 2
    }

    internal enum ResponseKind : byte
    {
        Void = 0,
        Long = 1,
        TaskLong = 2,
        Task = 3,
        TaskObject = 4,
        Object = 5
    }

    internal sealed class DispatchHandler
    {
        internal ESocketRole SocketRole { get; }
        internal EAttr MethodAttr { get; }
        internal Type ClassType { get; }
        internal MethodInfo MethodInfo { get; }
        internal Type? ArgType { get; }
        internal bool ArgAllowNull { get; }
        internal TypeObjectPool? ArgPool { get; }

        internal Type? ArgResponse { get; private set; }
        internal PropertyInfo? PIResponse { get; private set; }
        internal ResponseKind ResKind { get; private set; }

        internal Func<object, object[], object>? Execute { get; }

        internal DispatchHandler(ESocketRole socketRole, EAttr methodAttr, MethodInfo mInfo, Type? argType, bool argAllowNull, TypeObjectPool? pool, Type classType)
        {
            SocketRole = socketRole;
            MethodAttr = methodAttr;
            ArgType = argType;
            MethodInfo = mInfo;
            ClassType = classType;
            ArgAllowNull = argAllowNull;
            ArgPool = pool;
            SetResKind(MethodInfo.ReturnType);
            if (EServer.IsJIT)
                Execute = CreateInvoker(MethodInfo);
        }

        void SetResKind(Type type)
        {
            if (type == typeof(void))
            {
                ResKind = ResponseKind.Void;
            }
            else if (type == typeof(long))
            {
                ResKind = ResponseKind.Long;
            }
            else if (type == typeof(Task<long>))
            {
                ResKind = ResponseKind.TaskLong;
            }
            else if (type == typeof(Task))
            {
                ResKind = ResponseKind.Task;
            }
            else if (typeof(Task).IsAssignableFrom(type) && type.IsGenericType)
            {
                ResKind = ResponseKind.TaskObject;
                ArgResponse = type.GetGenericArguments()[0];
                PIResponse = type.GetProperty("Result");
#if DEBUG
                if (PIResponse == null)
                    Debug.WriteLine($"Cannot register response type: {type} for method: {MethodInfo.Name}");
#endif
            }
            else
            {
                ResKind = ResponseKind.Object;
                ArgResponse = type;
            }
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
                EServer.IsJIT = false;
                return null;
            }
        }

        internal bool Deserialize(ReadOnlySpan<byte> bytesData, ref object? obj, IESerializer serializer)
        {
            if (ArgType == null)
                return true;

            if (ArgPool != null)
            {
                obj = ArgPool.Rent();
                if (!serializer.Deserialize(ArgType, bytesData, ref obj))
                {
                    ArgPool.Return(obj);
                    return ArgAllowNull;
                }
                return true;
            }
            else
            {
                obj = serializer.Deserialize(bytesData, ArgType);
                if (obj == null)
                    return ArgAllowNull;
                else
                    return true;
            }
        }

        internal object? Deserialize(ReadOnlySequence<byte> bytesData, IESerializer serializer)
        {
            if (ArgType == null)
                return null;

            if (ArgPool != null)
            {
                var obj = ArgPool.Rent();
                if (!serializer.Deserialize(bytesData, ArgType, ref obj))
                {
                    ArgPool.Return(obj);
                    return null;
                }
                return obj;
            }
            else
                return serializer.Deserialize(bytesData, ArgType);
        }

        internal void ReturnArgToPool(object? obj)
        {
            ArgPool?.Return(obj);
        }
    }
}
