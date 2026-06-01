// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using MemoryPack;
using System.Buffers;
using System.Diagnostics;

namespace EnjoySockets
{
    public class ESerialMemoryPack : IESerializer
    {
        public int IdSerializer => 0;

        public T? Deserialize<T>(ReadOnlySpan<byte> data)
        {
            try
            {
                return MemoryPackSerializer.Deserialize<T>(data);
            }
            catch (Exception ex)
            {
                Log("Deserialize<T>(ReadOnlySpan<byte> data)", ex);
                return default;
            }
        }

        public T? Deserialize<T>(ReadOnlySequence<byte> data)
        {
            try
            {
                return MemoryPackSerializer.Deserialize<T>(data);
            }
            catch (Exception ex)
            {
                Log("Deserialize<T>(ReadOnlySequence<byte> data)", ex);
                return default;
            }
        }

        public object? Deserialize(ReadOnlySpan<byte> data, Type type)
        {
            try
            {
                return MemoryPackSerializer.Deserialize(type, data);
            }
            catch (Exception ex)
            {
                Log("Deserialize(ReadOnlySpan<byte> data, Type type)", ex);
                return null;
            }
        }

        public bool Deserialize(Type type, ReadOnlySpan<byte> data, ref object? obj)
        {
            try
            {
                MemoryPackSerializer.Deserialize(type, data, ref obj);
                return true;
            }
            catch (Exception ex)
            {
                Log("Deserialize(Type type, ReadOnlySpan<byte> data, ref object? obj)", ex);
                return false;
            }
        }

        public object? Deserialize(ReadOnlySequence<byte> data, Type type)
        {
            try
            {
                return MemoryPackSerializer.Deserialize(type, data);
            }
            catch (Exception ex)
            {
                Log("Deserialize(ReadOnlySequence<byte> data, Type type)", ex);
                return null;
            }
        }

        public bool Deserialize(ReadOnlySequence<byte> data, Type type, ref object? obj)
        {
            try
            {
                MemoryPackSerializer.Deserialize(type, data, ref obj);
                return true;
            }
            catch (Exception ex)
            {
                Log("Deserialize(ReadOnlySequence<byte> data, Type type, ref object? obj)", ex);
                return false;
            }
        }

        public byte[]? Serialize<T>(T? myObj)
        {
            try
            {
                return MemoryPackSerializer.Serialize(myObj);
            }
            catch (Exception ex)
            {
                Log("Serialize<T>(T? myObj)", ex);
                return null;
            }
        }

        public int Serialize<T>(EArrayBufferWriter buffer, T? myObj)
        {
            try
            {
                buffer.ResetWrittenCount();
                MemoryPackSerializer.Serialize(buffer, myObj);
                return buffer.WrittenSpan.Length;
            }
            catch (Exception ex)
            {
                Log("Serialize<T>(EArrayBufferWriter buffer, T? myObj)", ex);
                return 0;
            }
        }

        public int Serialize(EArrayBufferWriter buffer, object? myObj, Type? t)
        {
            try
            {
                if (t == null) return 0;
                buffer.ResetWrittenCount();
                MemoryPackSerializer.Serialize(t, buffer, myObj);
                return buffer.WrittenSpan.Length;
            }
            catch (Exception ex)
            {
                Log("Serialize(EArrayBufferWriter buffer, object? myObj, Type? t)", ex);
                return 0;
            }
        }

        [Conditional("DEBUG")]
        public static void Log(string method, Exception ex)
        {
            Debug.WriteLine($"{method} - {ex}");
        }
    }
}
