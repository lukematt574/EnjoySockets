using System.Buffers;

namespace EnjoySockets
{
    public interface IESerializer
    {
        int IdSerializer { get; }
        T? Deserialize<T>(ReadOnlySpan<byte> data);
        T? Deserialize<T>(ReadOnlySequence<byte> data);
        object? Deserialize(ReadOnlySpan<byte> data, Type type);
        bool Deserialize(Type type, ReadOnlySpan<byte> data, ref object? obj);
        object? Deserialize(ReadOnlySequence<byte> data, Type type);
        bool Deserialize(ReadOnlySequence<byte> data, Type type, ref object? obj);
        byte[]? Serialize<T>(T? myObj);
        int Serialize<T>(EArrayBufferWriter buffer, T? myObj);
        int Serialize(EArrayBufferWriter buffer, object? myObj, Type? t);
    }
}