using Google.Protobuf;

namespace MyChat.Protocol.Helper
{
    public static class ProtobufHelper
    {
        /// <summary>
        /// 将对象序列化为字节数组
        /// </summary>
        public static byte[] Serialize<T>(T obj) where T : IMessage
        {
            return obj.ToByteArray();
        }

        /// <summary>
        /// 将字节数组反序列化为对象
        /// </summary>
        public static T Deserialize<T>(byte[] data) where T : IMessage<T>, new()
        {
            var parser = new MessageParser<T>(() => new T());
            return parser.ParseFrom(data);
        }
    }
}