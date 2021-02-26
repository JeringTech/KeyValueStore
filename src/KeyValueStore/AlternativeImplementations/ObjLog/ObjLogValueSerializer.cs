using FASTER.core;
using MessagePack;

namespace Jering.KeyValueStore
{
    public class ObjLogValueSerializer<TValue> : BinaryObjectSerializer<TValue>
    {
        public override void Deserialize(out TValue obj)
        {
            obj = MessagePackSerializer.Deserialize<TValue>(reader.BaseStream);
        }

        public override void Serialize(ref TValue obj)
        {
            MessagePackSerializer.Serialize(writer.BaseStream, obj);
        }
    }
}
