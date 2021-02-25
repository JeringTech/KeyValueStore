using FASTER.core;
using MessagePack;

namespace Jering.KeyValueStore
{
    public class DefaultBinaryObjectSerializer<T> : BinaryObjectSerializer<T>
    {
        private static readonly MessagePackSerializerOptions _messagePackOptions = MessagePackSerializerOptions.
            Standard.
            WithCompression(MessagePackCompression.Lz4BlockArray);

        public override void Deserialize(out T obj)
        {
            obj = MessagePackSerializer.Deserialize<T>(reader.BaseStream, _messagePackOptions);
        }

        public override void Serialize(ref T obj)
        {
            MessagePackSerializer.Serialize(writer.BaseStream, obj, _messagePackOptions);
        }
    }
}
