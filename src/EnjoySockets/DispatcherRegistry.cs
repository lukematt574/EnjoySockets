// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace EnjoySockets
{
    internal static class DispatcherRegistry
    {
        static readonly Dictionary<ulong, DispatchHandler> Handlers = [];
        static readonly ConcurrentDictionary<string, ulong> StringToUlongSend = [];
        static readonly Dictionary<(Type, int), TypeObjectPool> TypePools = [];

        static readonly EAttr DefAttr = new();

        static readonly Dictionary<Type, Dictionary<ulong, DispatchHandler>> HandlersInstanceId = [];

        static readonly Dictionary<ushort, EAttrChannel> PrivateChannels = [];
        static readonly Dictionary<ushort, ReceiveDispatcher> ShareChannels = [];
        static readonly Dictionary<ushort, EAttrPool> IdPoolMap = [];

        static DispatcherRegistry()
        {
            var eAssembly = Assembly.GetEntryAssembly();
            if (eAssembly == null)
                return;

            var allTypes = eAssembly.GetTypes();
            MapPoolObjs(eAssembly);

            var correctMethods = new List<(MethodInfo, Type)>();
            foreach (var type in allTypes)
            {
                if (!IsClass(type))
                    continue;

                var methodsWithAttr = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (methodsWithAttr.Length < 1)
                    continue;

                foreach (var method in methodsWithAttr)
                {
                    if (IsCorrectMethod(method, type))
                        correctMethods.Add((method, type));
                }
            }

            var autoMethods = new Dictionary<string, List<(MethodInfo, Type)>>();
            var autoInstanceClasses = new HashSet<Type>();
            foreach (var obj in correctMethods)
            {
                var methodName = obj.Item1.Name;
                if (autoInstanceClasses.Contains(obj.Item2) || obj.Item1.IsStatic || !IsNeverInstantiable(obj.Item2))
                {
                    if (autoMethods.TryGetValue(methodName, out List<(MethodInfo, Type)>? iList))
                        iList.Add(obj);
                    else
                        autoMethods.Add(methodName, [obj]);

                    if (!obj.Item1.IsStatic)
                        autoInstanceClasses.Add(obj.Item2);
                }
            }

            foreach (var obj in autoMethods)
                autoMethods[obj.Key] = [SelectBestAndLog(obj.Key, obj.Value)];

            foreach (var obj in autoMethods)
            {
                var target = GetUlongFromString(obj.Key);
                if (target == 0)
                {
#if DEBUG
                    Debug.WriteLine($"Unavailable method name: {obj.Key}");
#endif
                    continue;
                }

                var objVal = obj.Value.First();
                var classAttr = objVal.Item2.GetCustomAttribute(typeof(EAttr)) as EAttr;
                var handler = GetDHandler(objVal.Item1, classAttr, objVal.Item2);
                if (!Handlers.TryGetValue(target, out _))
                {
                    Handlers.Add(target, handler);
                }
                else
                {
#if DEBUG
                    Debug.WriteLine($"Repeated method name: {obj.Key}");
#endif
                }
            }

            foreach (var obj in correctMethods)
            {
                if (obj.Item1.IsStatic)
                    continue;

                var target = GetUlongFromString(obj.Item1.Name);
                if (target == 0)
                    continue;

                var classAttr = obj.Item2.GetCustomAttribute(typeof(EAttr)) as EAttr;
                var handler = GetDHandler(obj.Item1, classAttr, obj.Item2);
                if (!HandlersInstanceId.TryGetValue(obj.Item2, out Dictionary<ulong, DispatchHandler>? dict))
                    HandlersInstanceId.Add(obj.Item2, new() { { target, handler } });
                else
                {
                    if (dict.TryGetValue(target, out DispatchHandler? targetHandler))
                    {
                        var x = GetInheritanceDepth(targetHandler.MethodInfo.DeclaringType!);
                        var y = GetInheritanceDepth(handler.MethodInfo.DeclaringType!);
                        if (y > x)
                            dict[target] = handler;
                    }
                    else
                        dict.Add(target, handler);
                }
            }
            SetChannels(eAssembly);
        }

        static void SetChannels(Assembly assembly)
        {
            var list = assembly!
                        .GetTypes()
                        .SelectMany(t => t.GetFields(
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Static))
                        .Where(f =>
                            f.IsLiteral &&
                            f.FieldType == typeof(ushort))
                        .Select(f => new
                        {
                            Field = f,
                            Attr = f.GetCustomAttribute<EAttrChannel>()
                        })
                        .Where(x => x.Attr != null)
                        .Select(x => (
                            Value: (ushort)x.Field.GetRawConstantValue()!,
                            Attr: x.Attr!
                        ))
                        .ToList();

            foreach (var (Value, Attr) in list)
            {
                if (!ShareChannels.TryGetValue(Value, out _) && !PrivateChannels.TryGetValue(Value, out _))
                {
                    if (Attr.ChannelType == EChannelType.Private)
                    {
                        PrivateChannels.Add(Value, Attr);
                    }
                    else if (Attr.ChannelType == EChannelType.Share)
                    {
                        ShareChannels.Add(Value, new(false, Attr.ChannelTasks));
                    }
                }
                else
                {
#if DEBUG
                    Debug.WriteLine($"Repeated channel id: {Value}");
#endif
                }
            }
        }

        static void MapPoolObjs(Assembly assembly)
        {
            var list = assembly!
                        .GetTypes()
                        .SelectMany(t => t.GetFields(
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Static))
                        .Where(f =>
                            f.IsLiteral &&
                            f.FieldType == typeof(ushort))
                        .Select(f => new
                        {
                            Field = f,
                            Attr = f.GetCustomAttribute<EAttrPool>()
                        })
                        .Where(x => x.Attr != null)
                        .Select(x => (
                            Value: (ushort)x.Field.GetRawConstantValue()!,
                            Attr: x.Attr!
                        ))
                        .ToList();

            foreach (var (Value, Attr) in list)
            {
                if (!IdPoolMap.TryGetValue(Value, out _))
                {
                    IdPoolMap.Add(Value, Attr);
                }
                else
                {
#if DEBUG
                    Debug.WriteLine($"Repeated pool obj id: {Value}");
#endif
                }
            }
        }

        static (MethodInfo method, Type type) SelectBestAndLog(string key, List<(MethodInfo method, Type type)> list)
        {
            if (list.Count == 1)
                return list[0];

            var staticMethod = list.FirstOrDefault(x => x.method.IsStatic);
            if (staticMethod.method != null)
            {
#if DEBUG
                LogRejected(key, staticMethod, list);
#endif
                return staticMethod;
            }

            var overrides = list
                    .Where(x => x.method.GetBaseDefinition() != x.method)
                    .OrderByDescending(x => GetInheritanceDepth(x.type))
                    .ToList();

            if (overrides.Count != 0)
            {
                var overrideM = overrides.First();
#if DEBUG
                LogRejected(key, overrideM, list);
#endif
                return overrideM;
            }

            var first = list.First();
#if DEBUG
            LogRejected(key, first, list);
#endif
            return first;
        }

        static int GetInheritanceDepth(Type type)
        {
            int depth = 0;
            while (type.BaseType != null)
            {
                depth++;
                type = type.BaseType;
            }
            return depth;
        }

        static string BuildPrefix(MethodInfo method)
        {
            var prefixes = new List<string>();

            if (method.IsStatic)
            {
                prefixes.Add("[STATIC]");
                return string.Concat(prefixes);
            }

            if (method.GetBaseDefinition() == method)
                prefixes.Add("[ROOT]");

            if (method.GetBaseDefinition() != method && method.IsVirtual)
                prefixes.Add("[OVERRIDE]");

            return string.Concat(prefixes);
        }

        static void LogRejected(string key, (MethodInfo method, Type type) selected, List<(MethodInfo method, Type type)> all)
        {
            Debug.WriteLine(
                $"\n[{key}]\n Register:"
            );

            Debug.WriteLine($"  - {BuildPrefix(selected.method)} {Format(selected)}");
        }

        static string Format((MethodInfo method, Type type) entry)
        {
            var p = entry.method.GetParameters();
            var paramName = p.Length == 2 ? p[1].ParameterType.ToString() : "null";
            return $"{entry.type.FullName}.{entry.method.Name} | param: {paramName}";
        }

        static bool IsNeverInstantiable(Type type)
        {
            if (type.IsInterface)
                return true;

            if (type.IsAbstract && !type.IsSealed)
                return true;

            if (type.IsAbstract && type.IsSealed)
                return true;

            if (type.ContainsGenericParameters)
                return true;

            if (type.IsByRef || type.IsPointer)
                return true;

            try
            {
                _ = Activator.CreateInstance(type);
                return false;
            }
            catch
            {
                return true;
            }
        }

        static bool IsClass(Type? type)
        {
            if (type == null
                || !type.IsClass
                || typeof(EServerSession).IsAssignableFrom(type)
                || typeof(EClient).IsAssignableFrom(type))
                return false;

            return true;
        }

        static bool IsCorrectMethod(MethodInfo? methodInfo, Type type)
        {
            if (methodInfo == null)
                return false;

            var mParams = methodInfo.GetParameters();
            if (mParams.Length < 1 || mParams.Length > 2)
                return false;

            var userParam = mParams[0].ParameterType;
            ESocketRole socketRole;
            if (IsOrSubclassOf(userParam, typeof(EServerSession)))
                socketRole = ESocketRole.Server;
            else if (IsOrSubclassOf(userParam, typeof(EClient)))
                socketRole = ESocketRole.Client;
            else
                return false;

            if (methodInfo.ReturnType == typeof(void))
            {
                if (methodInfo.GetCustomAttribute(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute)) != null)
                    return false;
                else
                    return true;
            }
            else if (socketRole == ESocketRole.Server &&
                (methodInfo.ReturnType == typeof(Task) ||
               methodInfo.ReturnType == typeof(Task<long>) ||
               methodInfo.ReturnType == typeof(long)))
            {
                return true;
            }
            else if (socketRole == ESocketRole.Client &&
                (methodInfo.ReturnType == typeof(Task)))
            {
                return true;
            }

            return false;
        }

        static TypeObjectPool? GetPool(Type? type, int eId, uint maxObjs, bool allowNull)
        {
            if (type == null || eId == 0 || allowNull) return null;

            if (type == typeof(string))
            {
#if DEBUG
                Debug.WriteLine($"Cannot pooling type: {type.Name}");
#endif
            }

            var id = (type, eId);
            if (TypePools.TryGetValue(id, out TypeObjectPool? val))
            {
                return val;
            }
            else
            {
                if (TypeObjectPool.CheckType(type))
                {
                    var objP = new TypeObjectPool(type, maxObjs);
                    TypePools.Add(id, objP);
                    return objP;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine($"Cannot pooling type: {type.Name}");
#endif
                }
            }
            return null;
        }

        /// <summary>
        /// Create handler object
        /// </summary>
        static DispatchHandler GetDHandler(MethodInfo methodInfo, EAttr? classAttr, Type classType)
        {
            var mParams = methodInfo.GetParameters();

            var userParam = mParams[0].ParameterType;

            ESocketRole socketRole;
            if (userParam == typeof(EServerSession) || IsOrSubclassOf(userParam, typeof(EServerSession)))
                socketRole = ESocketRole.Server;
            else
                socketRole = ESocketRole.Client;

            Type? argType = null;
            bool allowNull = true;
            if (mParams.Length == 2)
            {
                argType = mParams[1].ParameterType;
                allowNull = CheckAllowNullParam(argType, mParams[1]);
            }

            var mAttr = methodInfo.GetCustomAttributes(true).FirstOrDefault(x => x is EAttr) as EAttr;
            var tempAttr = mAttr?.Clone() ?? DefAttr.Clone();
            tempAttr.Fill(classAttr);

            uint maxObjPool = 0;
            if (IdPoolMap.TryGetValue(tempAttr.PoolId, out EAttrPool? eattrpool))
                maxObjPool = eattrpool.MaxPoolObjs;

            return new DispatchHandler(socketRole, tempAttr, methodInfo, argType, allowNull, GetPool(argType, tempAttr.PoolId, maxObjPool, allowNull), classType);
        }

        static bool IsOrSubclassOf(Type type, Type baseType) => type == baseType || type.IsSubclassOf(baseType);

        static bool CheckAllowNullParam(Type? argType, ParameterInfo info)
        {
            if (argType != null)
            {
                var nT = Nullable.GetUnderlyingType(argType);
                if (nT != null)
                {
                    argType = nT;
                    return true;
                }
                else
                {
                    if (info.GetCustomAttributes().FirstOrDefault(attr => attr.GetType().Name == "NullableAttribute") != null)
                        return true;
                }
            }
            return false;
        }

        internal static ulong GetUlongToSend(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return 0;

            return StringToUlongSend.GetOrAdd(input, GetUlongFromString);
        }

        private const int MaxNameUtf8Bytes = 96;
        static ulong GetUlongFromString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return 0;

            if (input.Length > MaxNameUtf8Bytes)
                return 0;

            Span<byte> utf8 = stackalloc byte[MaxNameUtf8Bytes];
            int written;

            try
            {
                written = Encoding.UTF8.GetBytes(input.AsSpan(), utf8);
            }
            catch
            {
                return 0;
            }

            if ((uint)written > MaxNameUtf8Bytes)
                return 0;

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(utf8[..written], hash);

            return BinaryPrimitives.ReadUInt64LittleEndian(hash);
        }

        internal static void Initialize() { }

        internal static DispatchHandler? GetHandlerToBasic(ulong target)
        {
            Handlers.TryGetValue(target, out DispatchHandler? handler);
            return handler;
        }

        internal static DispatchHandler? GetHandlerToInstanceId(object? obj, ulong target)
        {
            if (obj == null) return null;

            var t = obj.GetType();
            if (HandlersInstanceId.TryGetValue(t, out Dictionary<ulong, DispatchHandler>? handler))
            {
                if (handler.TryGetValue(target, out DispatchHandler? handlerVal))
                    return handlerVal;
            }
            return null;
        }

        internal static bool ExistInstanceHandler(Type t, out Dictionary<ulong, DispatchHandler>? handlers)
        {
            return HandlersInstanceId.TryGetValue(t, out handlers);
        }

        internal static bool TryGetPrivateChannel(ushort id, out EAttrChannel? eAttrChannel)
        {
            return PrivateChannels.TryGetValue(id, out eAttrChannel);
        }

        internal static bool TryGetShareChannel(ushort id, out ReceiveDispatcher? channel)
        {
            return ShareChannels.TryGetValue(id, out channel);
        }
    }
}
