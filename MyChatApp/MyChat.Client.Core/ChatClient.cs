using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using MyChat.Protocol;
using MyChat.Protocol.Helper;

namespace MyChat.Client.Core
{
    public class ChatClient
    {
        // 单例模式：整个APP只需要一个客户端连接
        public static ChatClient Instance { get; } = new ChatClient();

        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected;

        // ==================== 事件定义 ====================

        // 登录结果: (是否成功, 消息)
        public event Action<bool, string> OnLoginResult;

        // 收到聊天消息
        public event Action<ChatMsg> OnMessageReceived;

        // 注册结果
        public event Action<bool, string> OnRegisterResult;

        // 好友列表结果
        public event Action<List<FriendDto>> OnGetFriendListResult;

        // 搜索结果: (成功?, 用户ID, 昵称, 账号)
        public event Action<bool, string, string, string> OnSearchUserResult;

        // 添加好友结果: (成功?, 消息)
        public event Action<bool, string> OnAddFriendResult;

        // 好友状态变更: (好友ID, 是否上线)
        public event Action<string, bool> OnFriendStatusChange;

        // 建群结果: (成功?, 群ID, 群名, 消息)
        public event Action<bool, string, string, string> OnCreateGroupResult;

        // 群列表结果
        public event Action<List<GroupDto>> OnGetGroupListResult;

        // 群成员列表结果: (群ID, 成员列表)
        public event Action<string, List<GroupMemberDto>> OnGetGroupMembersResult;

        // 重置密码结果: (成功?, 消息)
        public event Action<bool, string> OnResetPasswordResult;
        //验证好友
        public event Action<string, string> OnFriendRequestReceived; // (id, nickname)
        public event Action<bool, string, string> OnHandleFriendRequestResult; // (success, msg, friendId)
        // ==================== 属性 ====================

        // 存储当前登录用户的信息
        public string CurrentUserId { get; private set; }
        public string CurrentNickname { get; private set; }

        private ChatClient() { }

        // ==================== 连接方法 ====================

        /// <summary>
        /// 1. 异步连接服务器 (登录页使用)
        /// </summary>
        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                if (_client != null && _client.Connected) return true;

                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();
                _isConnected = true;

                // 连接成功后，立刻开启后台接收循环
                _ = ReceiveLoopAsync();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ★★★ 1.1 同步连接服务器 (新增，用于忘记密码页面) ★★★
        /// </summary>
        public void Connect(string ip, int port)
        {
            // 如果已连接，直接返回
            if (_client != null && _client.Connected) return;

            try
            {
                _client = new TcpClient();
                _client.Connect(ip, port); // 同步连接
                _stream = _client.GetStream();
                _isConnected = true;

                // 启动接收循环 (Fire and forget)
                _ = ReceiveLoopAsync();

                Console.WriteLine("服务器连接成功！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接失败: {ex.Message}");
                throw; // 抛出异常供 ViewModel 捕获
            }
        }
        /// <summary>
        /// 断开连接 (用于清理状态)
        /// </summary>
        public void Disconnect()
        {
            _isConnected = false;

            try
            {
                if (_stream != null)
                {
                    _stream.Close();
                    _stream.Dispose();
                    _stream = null;
                }

                if (_client != null)
                {
                    _client.Close();
                    _client.Dispose();
                    _client = null;
                }

                Console.WriteLine("已断开连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开连接出错: {ex.Message}");
            }
        }

        // ==================== 业务请求方法 ====================

        /// <summary>
        /// 2. 发送登录请求
        /// </summary>
        public void Login(string account, string password)
        {
            var loginReq = new LoginReq
            {
                Account = account,
                Password = password
            };

            var packet = new NetworkPacket
            {
                Type = PacketType.LoginRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(loginReq))
            };

            Send(packet);
        }

        /// <summary>
        /// 发送注册请求
        /// </summary>
        public void Register(string account, string password, string nickname)
        {
            var req = new RegisterReq
            {
                Account = account,
                Password = password,
                Nickname = nickname
            };

            var packet = new NetworkPacket
            {
                Type = PacketType.RegisterRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };

            Send(packet);
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        /// <param name="receiverId">接收者ID</param>
        /// <param name="content">内容</param>
        /// <param name="type">消息类型</param>
        /// <param name="isGroup">是否群聊</param>
        /// <param name="fileName">文件名(若是文件)</param>
        /// <param name="fileSize">文件大小</param>
        public void SendChat(string receiverId, string content, MsgType type = MsgType.Text, bool isGroup = false,
                             string fileName = "", long fileSize = 0)
        {
            if (string.IsNullOrEmpty(CurrentUserId)) return;

            var msg = new ChatMsg
            {
                Id = Guid.NewGuid().ToString(),
                SenderId = CurrentUserId,
                ReceiverId = receiverId,
                Content = content,
                SendTime = DateTime.UtcNow.Ticks,
                IsGroup = isGroup,
                Type = type,
                SenderName = string.IsNullOrEmpty(CurrentNickname) ? CurrentUserId : CurrentNickname,
                SenderAvatar = "", // 暂时留空

                FileName = fileName,
                FileSize = fileSize
            };

            var packet = new NetworkPacket
            {
                Type = PacketType.ChatMessage,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(msg))
            };

            Send(packet);
        }

        /// <summary>
        /// 搜索用户
        /// </summary>
        public void SearchUser(string account)
        {
            var req = new SearchUserReq { Account = account };
            var packet = new NetworkPacket
            {
                Type = PacketType.SearchUserRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }

        /// <summary>
        /// 添加好友
        /// </summary>
        public void AddFriend(string myId, string friendId)
        {
            var req = new AddFriendReq { MyUserId = myId, FriendUserId = friendId };
            var packet = new NetworkPacket
            {
                Type = PacketType.AddFriendRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }

        /// <summary>
        /// 获取好友列表
        /// </summary>
        public void GetFriendList()
        {
            if (string.IsNullOrEmpty(CurrentUserId)) return;

            var req = new GetFriendListReq { UserId = CurrentUserId };
            var packet = new NetworkPacket
            {
                Type = PacketType.GetFriendListRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }
        //好友验证
        public void HandleFriendRequest(string requesterId, bool isAccept)
        {
            var req = new HandleFriendRequestReq
            {
                RequesterId = requesterId,
                IsAccept = isAccept
            };
            var packet = new NetworkPacket
            {
                Type = PacketType.HandleFriendRequestRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }
        /// <summary>
        /// 创建群聊
        /// </summary>
        public void CreateGroup(string groupName, List<string> memberIds)
        {
            var req = new CreateGroupReq { GroupName = groupName };
            req.MemberIds.AddRange(memberIds);

            var packet = new NetworkPacket
            {
                Type = PacketType.CreateGroupRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }

        /// <summary>
        /// 获取群列表
        /// </summary>
        public void GetGroupList()
        {
            if (string.IsNullOrEmpty(CurrentUserId)) return;

            var req = new GetGroupListReq { UserId = CurrentUserId };
            var packet = new NetworkPacket
            {
                Type = PacketType.GetGroupListRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }

        /// <summary>
        /// 获取群成员列表
        /// </summary>
        public void GetGroupMembers(string groupId)
        {
            var req = new GetGroupMembersReq { GroupId = groupId };
            var packet = new NetworkPacket
            {
                Type = PacketType.GetGroupMembersRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }

        /// <summary>
        /// 重置密码请求
        /// </summary>
        public void ResetPassword(string account, string nickname, string newPwd)
        {
            var req = new ResetPasswordReq
            {
                Account = account,
                Nickname = nickname,
                NewPassword = newPwd
            };
            var packet = new NetworkPacket
            {
                Type = PacketType.ResetPasswordRequest,
                Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(req))
            };
            Send(packet);
        }

        // ==================== 核心底层方法 ====================

        /// <summary>
        /// 底层发送方法
        /// </summary>
        private void Send(NetworkPacket packet)
        {
            if (!_isConnected || _stream == null) return;

            try
            {
                byte[] bodyBytes = ProtobufHelper.Serialize(packet);
                byte[] headerBytes = BitConverter.GetBytes(bodyBytes.Length); // 4字节长度头

                // 加锁防止并发写入
                lock (_stream)
                {
                    _stream.Write(headerBytes, 0, headerBytes.Length);
                    _stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送失败: {ex.Message}");
                _isConnected = false;
            }
        }

        /// <summary>
        /// 后台接收循环 (处理粘包)
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (_isConnected)
                {
                    // 先读4字节头
                    byte[] headerBuffer = new byte[4];
                    int read = await _stream.ReadAsync(headerBuffer, 0, 4);
                    if (read == 0) break;

                    int bodyLen = BitConverter.ToInt32(headerBuffer, 0);
                    if (bodyLen > 0)
                    {
                        byte[] bodyBuffer = new byte[bodyLen];
                        int totalRead = 0;
                        while (totalRead < bodyLen)
                        {
                            int r = await _stream.ReadAsync(bodyBuffer, totalRead, bodyLen - totalRead);
                            if (r == 0) break;
                            totalRead += r;
                        }

                        // 反序列化
                        var packet = ProtobufHelper.Deserialize<NetworkPacket>(bodyBuffer);
                        HandlePacket(packet);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开连接: {ex.Message}");
                _isConnected = false;
            }
        }

        /// <summary>
        /// 路由分发：根据包类型，触发不同的事件
        /// </summary>
        private void HandlePacket(NetworkPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.LoginResponse:
                    var loginResp = ProtobufHelper.Deserialize<LoginResp>(packet.Body.ToByteArray());
                    if (loginResp.IsSuccess)
                    {
                        CurrentUserId = loginResp.UserId;
                        CurrentNickname = loginResp.Nickname;
                    }
                    OnLoginResult?.Invoke(loginResp.IsSuccess, loginResp.Message);
                    break;

                case PacketType.ChatMessage:
                    var chatMsg = ProtobufHelper.Deserialize<ChatMsg>(packet.Body.ToByteArray());
                    OnMessageReceived?.Invoke(chatMsg);
                    break;

                case PacketType.RegisterResponse:
                    try
                    {
                        var regResp = ProtobufHelper.Deserialize<RegisterResp>(packet.Body.ToByteArray());
                        OnRegisterResult?.Invoke(regResp.IsSuccess, regResp.Message);
                    }
                    catch (Exception ex) { Console.WriteLine($"注册回包错误: {ex.Message}"); }
                    break;

                case PacketType.SearchUserResponse:
                    var searchResp = ProtobufHelper.Deserialize<SearchUserResp>(packet.Body.ToByteArray());
                    OnSearchUserResult?.Invoke(searchResp.IsSuccess, searchResp.UserId, searchResp.Nickname, searchResp.Account);
                    break;

                case PacketType.AddFriendResponse:
                    var addResp = ProtobufHelper.Deserialize<AddFriendResp>(packet.Body.ToByteArray());
                    OnAddFriendResult?.Invoke(addResp.IsSuccess, addResp.Message);
                    break;

                case PacketType.GetFriendListResponse:
                    var friendListResp = ProtobufHelper.Deserialize<GetFriendListResp>(packet.Body.ToByteArray());
                    OnGetFriendListResult?.Invoke(friendListResp.Friends.ToList());
                    break;
                case PacketType.FriendRequestNotification:
                    var noti = ProtobufHelper.Deserialize<FriendRequestNotification>(packet.Body.ToByteArray());
                    OnFriendRequestReceived?.Invoke(noti.SenderId, noti.SenderNickname);
                    break;

                case PacketType.HandleFriendRequestResponse:
                    var handleResp = ProtobufHelper.Deserialize<HandleFriendRequestResp>(packet.Body.ToByteArray());
                    OnHandleFriendRequestResult?.Invoke(handleResp.IsSuccess, handleResp.Message, handleResp.FriendId);
                    break;
                case PacketType.FriendStatusNotice:
                    var notice = ProtobufHelper.Deserialize<FriendStatusNotice>(packet.Body.ToByteArray());
                    OnFriendStatusChange?.Invoke(notice.FriendId, notice.IsOnline);
                    break;

                case PacketType.CreateGroupResponse:
                    var createGroupResp = ProtobufHelper.Deserialize<CreateGroupResp>(packet.Body.ToByteArray());
                    OnCreateGroupResult?.Invoke(createGroupResp.IsSuccess, createGroupResp.GroupId, createGroupResp.GroupName, createGroupResp.Message);
                    break;

                case PacketType.GetGroupListResponse:
                    var groupListResp = ProtobufHelper.Deserialize<GetGroupListResp>(packet.Body.ToByteArray());
                    OnGetGroupListResult?.Invoke(groupListResp.Groups.ToList());
                    break;

                case PacketType.GetGroupMembersResponse:
                    var membersResp = ProtobufHelper.Deserialize<GetGroupMembersResp>(packet.Body.ToByteArray());
                    OnGetGroupMembersResult?.Invoke(membersResp.GroupId, membersResp.Members.ToList());
                    break;

                case PacketType.ResetPasswordResponse:
                    var resetResp = ProtobufHelper.Deserialize<ResetPasswordResp>(packet.Body.ToByteArray());
                    OnResetPasswordResult?.Invoke(resetResp.IsSuccess, resetResp.Message);
                    break;
            }
        }
    }
}