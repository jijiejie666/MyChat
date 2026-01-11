using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using MyChat.Protocol;
using MyChat.Protocol.Helper;
using System.Diagnostics;

namespace MyChat.Client.Core
{
    public class ChatClient
    {
        // 单例模式
        private static ChatClient _instance;
        public static ChatClient Instance => _instance ??= new ChatClient();

        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected;
        private Thread _receiveThread;

        // 当前用户信息
        public string CurrentUserId { get; private set; }
        public string CurrentNickname { get; private set; }
        public string CurrentUserAvatar { get; private set; } // 当前用户头像

        // ==================== 事件定义 ====================

        // 1. 登录/注册/注销
        public event Action<bool, string> OnLoginResult;
        public event Action<bool, string> OnRegisterResult;
        public event Action<bool, string> OnResetPasswordResult;

        // 2. 消息
        public event Action<ChatMsg> OnMessageReceived;

        // 3. 好友/搜索
        public event Action<List<FriendDto>> OnGetFriendListResult;
        public event Action<string, bool> OnFriendStatusChange;
        public event Action<bool, FriendDto?> OnSearchUserResult;
        public event Action<bool, string> OnAddFriendResult;
        public event Action<string, string> OnFriendRequestReceived;
        public event Action<bool, string, string> OnHandleFriendRequestResult;

        // 4. 群组
        public event Action<bool, string, string, string> OnCreateGroupResult;
        public event Action<List<GroupDto>> OnGetGroupListResult;
        public event Action<string, List<GroupMemberDto>> OnGetGroupMembersResult;
        public event Action<string, string, string> OnGroupInvitationReceived;

        // 5. 个人信息更新
        public event Action<bool, string, string, string> OnUpdateUserInfoResult;

        // ★★★ 新增：好友信息实时更新事件 (friendId, nickname, avatar) ★★★
        public event Action<string, string, string> OnFriendInfoUpdate;

        private ChatClient() { }

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            if (_isConnected) return true;
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();
                _isConnected = true;

                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"连接失败: {ex.Message}");
                return false;
            }
        }

        public void Connect(string ip, int port) => ConnectAsync(ip, port).Wait();

        public void Disconnect()
        {
            _isConnected = false;
            try { _client?.Close(); } catch { }
        }

        private void ReceiveLoop()
        {
            byte[] buffer = new byte[4096];
            while (_isConnected)
            {
                try
                {
                    byte[] lenBytes = new byte[4];
                    int read = _stream.Read(lenBytes, 0, 4);
                    if (read == 0) break;
                    int bodyLen = BitConverter.ToInt32(lenBytes, 0);

                    byte[] bodyBytes = new byte[bodyLen];
                    int totalRead = 0;
                    while (totalRead < bodyLen)
                    {
                        int r = _stream.Read(bodyBytes, totalRead, bodyLen - totalRead);
                        if (r == 0) break;
                        totalRead += r;
                    }

                    var packet = ProtobufHelper.Deserialize<NetworkPacket>(bodyBytes);
                    HandlePacket(packet);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"接收异常: {ex.Message}");
                    Disconnect();
                    break;
                }
            }
        }

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
                        CurrentUserAvatar = loginResp.Avatar;
                    }
                    OnLoginResult?.Invoke(loginResp.IsSuccess, loginResp.Message);
                    break;

                case PacketType.RegisterResponse:
                    var regResp = ProtobufHelper.Deserialize<RegisterResp>(packet.Body.ToByteArray());
                    OnRegisterResult?.Invoke(regResp.IsSuccess, regResp.Message);
                    break;

                // ★★★ 修复点：添加对重置密码响应的处理 ★★★
                case PacketType.ResetPasswordResponse:
                    var resetResp = ProtobufHelper.Deserialize<ResetPasswordResp>(packet.Body.ToByteArray());
                    OnResetPasswordResult?.Invoke(resetResp.IsSuccess, resetResp.Message);
                    break;

                case PacketType.ChatMessage:
                    var chatMsg = ProtobufHelper.Deserialize<ChatMsg>(packet.Body.ToByteArray());
                    OnMessageReceived?.Invoke(chatMsg);
                    break;

                case PacketType.GetFriendListResponse:
                    var friendListResp = ProtobufHelper.Deserialize<GetFriendListResp>(packet.Body.ToByteArray());
                    OnGetFriendListResult?.Invoke(new List<FriendDto>(friendListResp.Friends));
                    break;

                case PacketType.FriendStatusNotice:
                    var notice = ProtobufHelper.Deserialize<FriendStatusNotice>(packet.Body.ToByteArray());
                    OnFriendStatusChange?.Invoke(notice.FriendId, notice.IsOnline);
                    break;

                case PacketType.SearchUserResponse:
                    var searchResp = ProtobufHelper.Deserialize<SearchUserResp>(packet.Body.ToByteArray());
                    FriendDto foundUser = null;
                    if (searchResp.IsSuccess)
                    {
                        foundUser = new FriendDto
                        {
                            UserId = searchResp.UserId,
                            Nickname = searchResp.Nickname,
                            IsOnline = false
                        };
                    }
                    OnSearchUserResult?.Invoke(searchResp.IsSuccess, foundUser);
                    break;

                case PacketType.AddFriendResponse:
                    var addResp = ProtobufHelper.Deserialize<AddFriendResp>(packet.Body.ToByteArray());
                    OnAddFriendResult?.Invoke(addResp.IsSuccess, addResp.Message);
                    break;

                case PacketType.FriendRequestNotification:
                    var reqNoti = ProtobufHelper.Deserialize<FriendRequestNotification>(packet.Body.ToByteArray());
                    OnFriendRequestReceived?.Invoke(reqNoti.SenderId, reqNoti.SenderNickname);
                    break;

                case PacketType.HandleFriendRequestResponse:
                    var handleResp = ProtobufHelper.Deserialize<HandleFriendRequestResp>(packet.Body.ToByteArray());
                    OnHandleFriendRequestResult?.Invoke(handleResp.IsSuccess, handleResp.Message, handleResp.FriendId);
                    break;

                case PacketType.CreateGroupResponse:
                    var groupResp = ProtobufHelper.Deserialize<CreateGroupResp>(packet.Body.ToByteArray());
                    OnCreateGroupResult?.Invoke(groupResp.IsSuccess, groupResp.GroupId, groupResp.GroupName, groupResp.Message);
                    break;

                case PacketType.GetGroupListResponse:
                    var groupListResp = ProtobufHelper.Deserialize<GetGroupListResp>(packet.Body.ToByteArray());
                    OnGetGroupListResult?.Invoke(new List<GroupDto>(groupListResp.Groups));
                    break;

                case PacketType.GetGroupMembersResponse:
                    var memResp = ProtobufHelper.Deserialize<GetGroupMembersResp>(packet.Body.ToByteArray());
                    OnGetGroupMembersResult?.Invoke(memResp.GroupId, new List<GroupMemberDto>(memResp.Members));
                    break;

                case PacketType.GroupInvitationNotification:
                    try
                    {
                        var groupNoti = ProtobufHelper.Deserialize<GroupInvitationNotification>(packet.Body.ToByteArray());
                        OnGroupInvitationReceived?.Invoke(groupNoti.GroupId, groupNoti.GroupName, groupNoti.OwnerId);
                    }
                    catch (Exception ex) { Debug.WriteLine($"入群通知解析失败: {ex.Message}"); }
                    break;

                case PacketType.UpdateUserInfoResponse:
                    try
                    {
                        var updateResp = ProtobufHelper.Deserialize<UpdateUserInfoResp>(packet.Body.ToByteArray());
                        if (updateResp.IsSuccess)
                        {
                            if (!string.IsNullOrEmpty(updateResp.UpdatedNickname)) CurrentNickname = updateResp.UpdatedNickname;
                            if (!string.IsNullOrEmpty(updateResp.UpdatedAvatar)) CurrentUserAvatar = updateResp.UpdatedAvatar;
                        }
                        OnUpdateUserInfoResult?.Invoke(updateResp.IsSuccess, updateResp.Message, updateResp.UpdatedNickname, updateResp.UpdatedAvatar);
                    }
                    catch (Exception ex) { Debug.WriteLine($"更新信息解析失败: {ex.Message}"); }
                    break;

                // ★★★ 新增：处理好友信息实时变更通知 ★★★
                case PacketType.FriendInfoUpdateNotice:
                    try
                    {
                        var noticePacket = ProtobufHelper.Deserialize<FriendInfoUpdateNotice>(packet.Body.ToByteArray());
                        OnFriendInfoUpdate?.Invoke(noticePacket.FriendId, noticePacket.Nickname, noticePacket.Avatar);
                    }
                    catch (Exception ex) { Debug.WriteLine($"好友更新通知解析失败: {ex.Message}"); }
                    break;
            }
        }

        public void SendPacket<T>(PacketType type, T data) where T : IMessage
        {
            if (!_isConnected) return;
            try
            {
                byte[] body = ProtobufHelper.Serialize(data);
                var packet = new NetworkPacket
                {
                    Type = type,
                    Body = ByteString.CopyFrom(body),
                    Timestamp = DateTime.UtcNow.Ticks
                };
                byte[] packetBytes = ProtobufHelper.Serialize(packet);
                byte[] lenBytes = BitConverter.GetBytes(packetBytes.Length);

                lock (_stream)
                {
                    _stream.Write(lenBytes, 0, 4);
                    _stream.Write(packetBytes, 0, packetBytes.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"发送失败: {ex.Message}");
            }
        }

        // --- 业务封装方法 ---

        public void Login(string account, string password) => SendPacket(PacketType.LoginRequest, new LoginReq { Account = account, Password = password });

        public void Register(string account, string password, string nickname) => SendPacket(PacketType.RegisterRequest, new RegisterReq { Account = account, Password = password, Nickname = nickname });

        // ★★★ 确保此方法名与 ForgetPasswordViewModel 中调用的一致 (SendResetPassword) ★★★
        public void SendResetPassword(string account, string nickname, string newPassword)
        {
            SendPacket(PacketType.ResetPasswordRequest, new ResetPasswordReq
            {
                Account = account,
                Nickname = nickname,
                NewPassword = newPassword
            });
        }

        public void SendChat(string receiverId, string content, MsgType type, bool isGroup, string fileName = "", long fileSize = 0)
        {
            var msg = new ChatMsg
            {
                Id = Guid.NewGuid().ToString(),
                SenderId = CurrentUserId,
                ReceiverId = receiverId,
                Content = content,
                SendTime = DateTime.UtcNow.Ticks,
                Type = type,
                IsGroup = isGroup,
                SenderName = CurrentNickname,
                FileName = fileName,
                FileSize = fileSize
            };
            SendPacket(PacketType.ChatMessage, msg);
        }

        public void GetFriendList() => SendPacket(PacketType.GetFriendListRequest, new GetFriendListReq { UserId = CurrentUserId });

        public void SearchUser(string account) => SendPacket(PacketType.SearchUserRequest, new SearchUserReq { Account = account });

        public void AddFriend(string friendId)
        {
            SendPacket(PacketType.AddFriendRequest, new AddFriendReq { MyUserId = CurrentUserId, FriendUserId = friendId });
        }

        public void HandleFriendRequest(string requesterId, bool isAccept) => SendPacket(PacketType.HandleFriendRequestRequest, new HandleFriendRequestReq { RequesterId = requesterId, IsAccept = isAccept });

        public void CreateGroup(string name, List<string> members)
        {
            var req = new CreateGroupReq { GroupName = name };
            req.MemberIds.AddRange(members);
            SendPacket(PacketType.CreateGroupRequest, req);
        }

        public void GetGroupList() => SendPacket(PacketType.GetGroupListRequest, new GetGroupListReq { UserId = CurrentUserId });

        public void GetGroupMembers(string groupId) => SendPacket(PacketType.GetGroupMembersRequest, new GetGroupMembersReq { GroupId = groupId });

        public void UpdateUserInfo(string newNickname, string newAvatarBase64)
        {
            SendPacket(PacketType.UpdateUserInfoRequest, new UpdateUserInfoReq
            {
                UserId = CurrentUserId,
                NewNickname = newNickname ?? "",
                NewAvatar = newAvatarBase64 ?? ""
            });
        }
    }
}