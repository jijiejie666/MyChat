using System.Net.Sockets;
using MyChat.Protocol;
using Google.Protobuf;
using MyChat.Protocol.Helper;

namespace MyChat.Server.Core
{
    public class ClientSession
    {
        public string SessionId { get; private set; } // 临时的唯一ID
        public string UserId { get; set; } // 登录成功后绑定的用户ID
        public TcpClient Client { get; private set; }
        private NetworkStream _stream;
        private bool _isConnected;

        // 定义一个事件，当收到完整包时通知服务器
        public event Action<ClientSession, NetworkPacket> OnPacketReceived;
        // 定义一个事件，当断开连接时通知服务器
        public event Action<ClientSession> OnDisconnected;

        public ClientSession(TcpClient client)
        {
            Client = client;
            SessionId = Guid.NewGuid().ToString();
            _stream = client.GetStream();
            _isConnected = true;
        }

        /// <summary>
        /// 开始接收数据循环
        /// </summary>
        public async Task StartReceiveAsync()
        {
            try
            {
                while (_isConnected)
                {
                    // 1. 先读取 4个字节 (包头)，代表包体长度
                    // 为什么是4？因为 int 是 4字节。这是解决粘包的关键！
                    byte[] headerBuffer = new byte[4];
                    int bytesRead = await ReadExactAsync(headerBuffer, 4);

                    if (bytesRead == 0) break; // 对方断开了

                    // 将 4字节 转为 int，得到包体长度
                    // 注意：网络字节序通常是大端，但这里我们假设客户端服务端都是 .NET (小端)，暂不通过 IPAddress.NetworkToHostOrder 转换，保持简单
                    int bodyLength = BitConverter.ToInt32(headerBuffer, 0);

                    // 2. 根据长度，读取具体的包体内容
                    if (bodyLength > 0)
                    {
                        byte[] bodyBuffer = new byte[bodyLength];
                        await ReadExactAsync(bodyBuffer, bodyLength);

                        // 3. 反序列化为 NetworkPacket 对象
                        try
                        {
                            var packet = ProtobufHelper.Deserialize<NetworkPacket>(bodyBuffer);
                            // 触发事件，交给上层处理
                            OnPacketReceived?.Invoke(this, packet);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"反序列化出错: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 忽略连接重置错误
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>
        /// 辅助方法：确保读取到指定长度的字节
        /// </summary>
        private async Task<int> ReadExactAsync(byte[] buffer, int length)
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await _stream.ReadAsync(buffer, totalRead, length - totalRead);
                if (read == 0) return 0; // 连接断开
                totalRead += read;
            }
            return totalRead;
        }

        /// <summary>
        /// 发送数据给客户端
        /// </summary>
        public void Send(NetworkPacket packet)
        {
            if (!_isConnected) return;

            try
            {
                // 1. 序列化包体
                byte[] bodyBytes = ProtobufHelper.Serialize(packet);

                // 2. 构造包头 (长度)
                byte[] headerBytes = BitConverter.GetBytes(bodyBytes.Length);

                // 3. 拼接 (包头 + 包体) 并发送
                // 为了性能，实际生产中会使用 Memory<byte> 或 Pipe，这里用简单数组拼接演示
                byte[] sendBuffer = new byte[headerBytes.Length + bodyBytes.Length];
                Array.Copy(headerBytes, 0, sendBuffer, 0, headerBytes.Length);
                Array.Copy(bodyBytes, 0, sendBuffer, headerBytes.Length, bodyBytes.Length);

                // 写入流 (加锁防止并发写入错乱)
                lock (_stream)
                {
                    _stream.Write(sendBuffer, 0, sendBuffer.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送失败: {ex.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
            Client.Close();
            OnDisconnected?.Invoke(this);
        }
    }
}