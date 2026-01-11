using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets; // ★★★ 必须引用
using System.Threading.Tasks;
using Google.Protobuf;
using MyChat.Protocol;
using MyChat.Protocol.Helper;

namespace MyChat.Server.Core
{
    public class ClientSession
    {
        public string SessionId { get; private set; }
        public string UserId { get; set; }
        public TcpClient Client { get; private set; }

        // ★★★ 新增：暴露底层的 Socket，以便 SessionManager 进行操作 (如踢人关闭连接) ★★★
        public Socket ClientSocket => Client?.Client;

        private NetworkStream _stream;
        private bool _isConnected;

        // 事件定义
        public event Action<ClientSession, NetworkPacket> OnPacketReceived;
        public event Action<ClientSession> OnDisconnected;

        public ClientSession(TcpClient client)
        {
            Client = client;
            SessionId = Guid.NewGuid().ToString();
            _stream = client.GetStream();
            _isConnected = true;
        }

        /// <summary>
        /// 启动管道处理流程 (零拷贝核心)
        /// </summary>
        public async Task StartReceiveAsync()
        {
            // 创建一个管道，用于连接 Socket 网络流和我们的解析逻辑
            var pipe = new Pipe();

            // 启动写入任务：从 Socket 读取数据 -> 写入管道
            Task writing = FillPipeAsync(_stream, pipe.Writer);

            // 启动读取任务：从管道读取数据 -> 解析 Protobuf -> 触发业务
            Task reading = ReadPipeAsync(pipe.Reader);

            await Task.WhenAll(reading, writing);
        }

        /// <summary>
        /// 任务1：从 Socket 填充数据到管道 (Producer)
        /// </summary>
        private async Task FillPipeAsync(NetworkStream stream, PipeWriter writer)
        {
            const int minimumBufferSize = 512;

            while (_isConnected)
            {
                try
                {
                    // 向 PipeWriter 申请一段内存 (Zero Allocation)
                    Memory<byte> memory = writer.GetMemory(minimumBufferSize);

                    // 直接把 Socket 数据读到这就绪的内存中
                    int bytesRead = await stream.ReadAsync(memory);

                    if (bytesRead == 0) break; // 对方断开

                    // 告诉管道我们写了多少数据
                    writer.Advance(bytesRead);

                    // 刷新数据，让 Reader 能看到
                    FlushResult result = await writer.FlushAsync();

                    if (result.IsCompleted) break;
                }
                catch
                {
                    break;
                }
            }

            // 完成写入
            await writer.CompleteAsync();
        }

        /// <summary>
        /// 任务2：从管道解析数据帧 (Consumer)
        /// </summary>
        private async Task ReadPipeAsync(PipeReader reader)
        {
            while (true)
            {
                // 等待数据写入
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                // 循环解析完整的数据包 (处理粘包的关键)
                while (TryReadPacket(ref buffer, out NetworkPacket packet))
                {
                    // 触发业务逻辑
                    try
                    {
                        OnPacketReceived?.Invoke(this, packet);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Session] 业务处理异常: {ex.Message}");
                    }
                }

                // 告诉管道我们处理到了哪里，没处理完的数据保留到下一次
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }

            // 全部结束，触发断开
            Disconnect();
        }

        /// <summary>
        /// 尝试解析一个完整的包 (Length-Field Based Frame Decoder)
        /// </summary>
        private bool TryReadPacket(ref ReadOnlySequence<byte> buffer, out NetworkPacket packet)
        {
            packet = null;

            // 1. 检查包头长度 (int = 4字节)
            if (buffer.Length < 4) return false; // 数据不够读长度，等待下一次

            // 读取前4个字节，解析出包体长度
            // 使用切片操作，不产生新数组
            var lengthSlice = buffer.Slice(0, 4);
            int bodyLength = ParseHeader(lengthSlice);

            // 2. 检查总长度是否足够 (包头4 + 包体N)
            if (buffer.Length < 4 + bodyLength) return false; // 数据不够读包体，等待下一次

            // 3. 提取包体数据
            // Slice(start, length) -> 零拷贝切片
            var bodySlice = buffer.Slice(4, bodyLength);

            // 4. 反序列化
            // 注意：Protobuf 需要 byte[] 或 Stream，这里为了兼容现有 ProtobufHelper，
            // 我们不得不进行一次 ToArray。在极致优化中，可以让 Protobuf 直接读 ReadOnlySequence。
            packet = ProtobufHelper.Deserialize<NetworkPacket>(bodySlice.ToArray());

            // 5. 移动 buffer 指针，跳过已处理的数据
            buffer = buffer.Slice(4 + bodyLength);
            return true;
        }

        private int ParseHeader(ReadOnlySequence<byte> headerSlice)
        {
            if (headerSlice.IsSingleSegment)
            {
                return BitConverter.ToInt32(headerSlice.First.Span);
            }
            else
            {
                // 如果4字节跨越了内存段，需要拷贝到临时数组解析 (极少情况)
                Span<byte> temp = stackalloc byte[4];
                headerSlice.CopyTo(temp);
                return BitConverter.ToInt32(temp);
            }
        }

        public void Send(NetworkPacket packet)
        {
            if (!_isConnected) return;
            try
            {
                byte[] bodyBytes = ProtobufHelper.Serialize(packet);
                byte[] headerBytes = BitConverter.GetBytes(bodyBytes.Length);

                lock (_stream)
                {
                    _stream.Write(headerBytes);
                    _stream.Write(bodyBytes);
                }
            }
            catch { Disconnect(); }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
            try { Client.Close(); } catch { }
            OnDisconnected?.Invoke(this);
        }
    }
}