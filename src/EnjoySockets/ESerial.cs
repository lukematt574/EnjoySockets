using MemoryPack;
using System.Buffers;

namespace EnjoySockets
{
    public static class ESerial
    {
        public static object? Deserialize(ReadOnlySpan<byte> data, Type type)
        {
            try
            {
                return MemoryPackSerializer.Deserialize(type, data);
            }
            catch { return default; }
        }

        public static object? Deserialize(ReadOnlySequence<byte> data, Type type)
        {
            try
            {
                return MemoryPackSerializer.Deserialize(type, data);
            }
            catch { return default; }
        }

        public static bool Deserialize(ReadOnlySequence<byte> data, Type type, ref object? obj)
        {
            try
            {
                MemoryPackSerializer.Deserialize(type, data, ref obj);
                return true;
            }
            catch { return false; }
        }

        public static T? Deserialize<T>(ReadOnlySpan<byte> data)
        {
            try
            {
                return MemoryPackSerializer.Deserialize<T>(data);
            }
            catch { return default; }
        }

        public static bool Deserialize<T>(ReadOnlySpan<byte> data, ref T? obj)
        {
            try
            {
                MemoryPackSerializer.Deserialize(data, ref obj);
                return true;
            }
            catch { return false; }
        }

        public static bool Deserialize(Type type, ReadOnlySpan<byte> data, ref object? obj)
        {
            try
            {
                MemoryPackSerializer.Deserialize(type, data, ref obj);
                return true;
            }
            catch { return false; }
        }

        public static byte[]? Serialize<T>(T? myObj)
        {
            try
            {
                return MemoryPackSerializer.Serialize(myObj);
            }
            catch { return null; }
        }

        public static int Serialize<T>(EArrayBufferWriter buffer, T? myObj)
        {
            try
            {
                MemoryPackSerializer.Serialize(buffer, myObj);
                return buffer.WrittenSpan.Length;
            }
            catch { return 0; }
        }

        public static int Serialize(EArrayBufferWriter buffer, object? myObj, Type? t)
        {
            try
            {
                if (t == null) return 0;
                MemoryPackSerializer.Serialize(t, buffer, myObj);
                return buffer.WrittenSpan.Length;
            }
            catch { return 0; }
        }
    }
}
