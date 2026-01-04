using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Google.Protobuf;
using MyChat.Protocol;
using MyChat.Protocol.Helper;
using MyChat.Server.Database;
using MyChat.Server.Entities;

namespace MyChat.Server.Core
{
    public class SocketServer
    {
        private TcpListener _listener;
        private bool _isRunning;

        /// <summary>
        /// 启动服务器
        /// </summary>
        public void Start(int port)
        {
            try
            {
                // 1. 确保机器人账号存在
                InitRobotAccount();

                // 2. 启动监听
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _isRunning = true;
                Console.WriteLine($"[系统] 服务器启动成功，监听端口: {port}");

                ListenLoopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[系统] 启动失败: {ex.Message}");
            }
        }

        // 初始化机器人方法
        private void InitRobotAccount()
        {
            using (var db = new MyChatContext())
            {
                // 检查 ID 为 9999 的用户是否存在
                if (!db.Users.Any(u => u.Id == "9999"))
                {
                    db.Users.Add(new Entities.UserEntity
                    {
                        Id = "9999",
                        Account = "robot",
                        Password = "123", // 密码随便，反正它不登录
                        Nickname = "AI 助手",
                        Avatar = "#00B894", // 给个特别的绿色
                        CreateTime = DateTime.Now
                    });
                    db.SaveChanges();
                    Console.WriteLine("[系统] AI 机器人账号 (ID: 9999) 初始化完成");
                }
            }
        }

        /// <summary>
        /// 监听循环
        /// </summary>
        private async void ListenLoopAsync()
        {
            while (_isRunning)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    Console.WriteLine($"[连接] 新客户端接入: {tcpClient.Client.RemoteEndPoint}");

                    var session = new ClientSession(tcpClient);
                    session.OnPacketReceived += HandlePacket;
                    session.OnDisconnected += HandleDisconnect;

                    SessionManager.Instance.AddSession(session);
                    _ = session.StartReceiveAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[异常] 监听循环出错: {ex.Message}");
                }
            }
        }

        private void HandleDisconnect(ClientSession session)
        {
            Console.WriteLine($"[断开] 客户端断开连接: {session.SessionId} (用户: {session.UserId})");
            SessionManager.Instance.RemoveSession(session);
        }

        /// <summary>
        /// 核心业务处理
        /// </summary>
        private void HandlePacket(ClientSession session, NetworkPacket packet)
        {
            switch (packet.Type)
            {
                // ================================================================
                // 1. 登录业务
                // ================================================================
                case PacketType.LoginRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<LoginReq>(packet.Body.ToByteArray());
                        Console.WriteLine($"[登录] 尝试登录: {req.Account}");

                        bool isSuccess = false;
                        string msg = "";
                        string userId = "";
                        string nickname = "";

                        using (var db = new MyChatContext())
                        {
                            var user = db.Users.FirstOrDefault(u => u.Account == req.Account);
                            if (user == null)
                            {
                                msg = "账号不存在";
                            }
                            else if (user.Password != req.Password)
                            {
                                msg = "密码错误";
                            }
                            else
                            {
                                isSuccess = true;
                                msg = "登录成功";
                                userId = user.Id;
                                nickname = user.Nickname;
                            }
                        }

                        if (isSuccess)
                        {
                            // 注册 Session
                            SessionManager.Instance.RegisterUser(userId, session);

                            // 上线广播 & 离线消息推送 & 补发好友申请
                            Task.Run(() =>
                            {
                                try
                                {
                                    using (var db = new MyChatContext())
                                    {
                                        // --- 上线广播 ---
                                        var myFriendIds = db.Friends.Where(f => f.UserId == userId).Select(f => f.FriendId).ToList();
                                        var notice = new FriendStatusNotice { FriendId = userId, IsOnline = true };
                                        var packetBody = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(notice));
                                        var noticePacket = new NetworkPacket { Type = PacketType.FriendStatusNotice, Body = packetBody };

                                        foreach (var friendId in myFriendIds)
                                        {
                                            var friendSession = SessionManager.Instance.GetSessionByUserId(friendId);
                                            friendSession?.Send(noticePacket);
                                        }

                                        // --- 推送离线消息 ---
                                        var offlineMsgs = db.Messages
                                            .Where(m => m.ReceiverId == userId && !m.IsGroup && !m.IsDelivered)
                                            .OrderBy(m => m.SendTime).ToList();

                                        if (offlineMsgs.Count > 0)
                                        {
                                            Console.WriteLine($"[离线消息] 用户 {userId} 有 {offlineMsgs.Count} 条未读，正在推送...");
                                            var mySession = SessionManager.Instance.GetSessionByUserId(userId);
                                            if (mySession != null)
                                            {
                                                foreach (var dbMsg in offlineMsgs)
                                                {
                                                    var chatMsg = new ChatMsg
                                                    {
                                                        Id = dbMsg.Id,
                                                        SenderId = dbMsg.SenderId,
                                                        ReceiverId = dbMsg.ReceiverId,
                                                        Content = dbMsg.Content,
                                                        SendTime = dbMsg.SendTime,
                                                        IsGroup = dbMsg.IsGroup,
                                                        Type = (MyChat.Protocol.MsgType)dbMsg.Type,
                                                        SenderName = dbMsg.SenderName,
                                                        SenderAvatar = dbMsg.SenderAvatar,
                                                        FileName = dbMsg.FileName,
                                                        FileSize = dbMsg.FileSize
                                                    };
                                                    var chatPacket = new NetworkPacket { Type = PacketType.ChatMessage, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(chatMsg)) };
                                                    mySession.Send(chatPacket);
                                                    dbMsg.IsDelivered = true;
                                                }
                                                db.SaveChanges();
                                            }
                                        }

                                        // --- 补发未处理的好友申请 ---
                                        var pendingReqs = db.FriendRequests
                                            .Where(r => r.ReceiverId == userId && r.Status == 0) // 查发给我的、状态为0(等待)
                                            .ToList();

                                        if (pendingReqs.Count > 0)
                                        {
                                            Console.WriteLine($"[好友] 用户 {userId} 有 {pendingReqs.Count} 条待处理申请，正在推送...");
                                            var mySession = SessionManager.Instance.GetSessionByUserId(userId);
                                            if (mySession != null)
                                            {
                                                foreach (var preq in pendingReqs)
                                                {
                                                    var sender = db.Users.Find(preq.SenderId);
                                                    var noti = new FriendRequestNotification { SenderId = preq.SenderId, SenderNickname = sender?.Nickname ?? "未知用户" };
                                                    mySession.Send(new NetworkPacket { Type = PacketType.FriendRequestNotification, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(noti)) });
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine($"[登录后处理] 异常: {ex.Message}"); }
                            });
                        }

                        var resp = new LoginResp { IsSuccess = isSuccess, Message = msg, UserId = userId, Nickname = nickname };
                        Send(session, PacketType.LoginResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"[登录] 异常: {ex.Message}"); }
                    break;

                // ================================================================
                // 2. 注册业务
                // ================================================================
                case PacketType.RegisterRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<RegisterReq>(packet.Body.ToByteArray());
                        Console.WriteLine($"[注册] 新用户注册: {req.Account}");

                        bool isSuccess = false;
                        string msg = "";
                        string newId = "";

                        using (var db = new MyChatContext())
                        {
                            if (db.Users.Any(u => u.Account == req.Account))
                            {
                                msg = "账号已存在";
                            }
                            else
                            {
                                newId = Guid.NewGuid().ToString("N").Substring(0, 8);
                                var newUser = new UserEntity
                                {
                                    Id = newId,
                                    Account = req.Account,
                                    Password = req.Password,
                                    Nickname = req.Nickname,
                                    Avatar = "#" + new Random().Next(0x100000, 0xFFFFFF).ToString("X6"),
                                    CreateTime = DateTime.Now
                                };
                                db.Users.Add(newUser);
                                db.SaveChanges();
                                isSuccess = true;
                                msg = "注册成功";
                                Console.WriteLine($"[注册] 写入数据库成功 ID: {newId}");
                            }
                        }
                        var resp = new RegisterResp { IsSuccess = isSuccess, Message = msg, NewUserId = newId };
                        Send(session, PacketType.RegisterResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"[注册] 异常: {ex.Message}"); }
                    break;

                // ================================================================
                // 3. 聊天消息转发 (含 AI 拦截)
                // ================================================================
                case PacketType.ChatMessage:
                    try
                    {
                        var chatMsg = ProtobufHelper.Deserialize<ChatMsg>(packet.Body.ToByteArray());
                        Console.WriteLine($"[消息] {chatMsg.SenderId} -> {chatMsg.ReceiverId} (Type={chatMsg.Type})");

                        using (var db = new MyChatContext())
                        {
                            var dbMsg = new ServerMessageEntity
                            {
                                Id = chatMsg.Id,
                                SenderId = chatMsg.SenderId,
                                ReceiverId = chatMsg.ReceiverId,
                                Content = chatMsg.Content,
                                SendTime = chatMsg.SendTime,
                                IsGroup = chatMsg.IsGroup,
                                Type = (int)chatMsg.Type,
                                SenderName = chatMsg.SenderName,
                                SenderAvatar = chatMsg.SenderAvatar,
                                FileName = chatMsg.FileName,
                                FileSize = chatMsg.FileSize,
                                IsDelivered = false
                            };

                            // AI 拦截逻辑
                            if (chatMsg.ReceiverId == "9999")
                            {
                                Console.WriteLine($"[AI] 收到用户 {chatMsg.SenderId} 的消息，正在思考...");
                                dbMsg.IsDelivered = true;

                                _ = Task.Run(async () =>
                                {
                                    string replyContent = await Services.AIService.Instance.GetReplyAsync(chatMsg.Content);
                                    var replyMsg = new ChatMsg
                                    {
                                        Id = Guid.NewGuid().ToString(),
                                        SenderId = "9999",
                                        ReceiverId = chatMsg.SenderId,
                                        Content = replyContent,
                                        SendTime = DateTime.UtcNow.Ticks,
                                        IsGroup = false,
                                        Type = MyChat.Protocol.MsgType.Text,
                                        SenderName = "AI 助手",
                                        SenderAvatar = "#00B894"
                                    };

                                    var userSession = SessionManager.Instance.GetSessionByUserId(chatMsg.SenderId);
                                    userSession?.Send(new NetworkPacket { Type = PacketType.ChatMessage, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(replyMsg)) });

                                    using (var replyDb = new MyChatContext())
                                    {
                                        replyDb.Messages.Add(new ServerMessageEntity
                                        {
                                            Id = replyMsg.Id,
                                            SenderId = replyMsg.SenderId,
                                            ReceiverId = replyMsg.ReceiverId,
                                            Content = replyMsg.Content,
                                            SendTime = replyMsg.SendTime,
                                            IsGroup = false,
                                            Type = (int)replyMsg.Type,
                                            SenderName = replyMsg.SenderName,
                                            SenderAvatar = replyMsg.SenderAvatar,
                                            IsDelivered = true
                                        });
                                        replyDb.SaveChanges();
                                    }
                                    Console.WriteLine($"[AI] 已回复用户 {chatMsg.SenderId}");
                                });
                            }
                            // 群聊转发
                            else if (chatMsg.IsGroup)
                            {
                                dbMsg.IsDelivered = true;
                                var memberIds = db.GroupMembers.Where(g => g.GroupId == chatMsg.ReceiverId && g.UserId != chatMsg.SenderId).Select(g => g.UserId).ToList();
                                foreach (var memberId in memberIds)
                                {
                                    var memSession = SessionManager.Instance.GetSessionByUserId(memberId);
                                    memSession?.Send(packet);
                                }
                            }
                            // 私聊转发
                            else
                            {
                                var targetSession = SessionManager.Instance.GetSessionByUserId(chatMsg.ReceiverId);
                                if (targetSession != null)
                                {
                                    targetSession.Send(packet);
                                    dbMsg.IsDelivered = true;
                                }
                                else
                                {
                                    Console.WriteLine($"   -> 目标 {chatMsg.ReceiverId} 离线，已存入离线消息库");
                                    dbMsg.IsDelivered = false;
                                }
                            }
                            db.Messages.Add(dbMsg);
                            db.SaveChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"消息异常: {ex.Message}");
                        if (ex.InnerException != null) Console.WriteLine($"内部错误: {ex.InnerException.Message}");
                    }
                    break;

                // ================================================================
                // 4. 搜索用户
                // ================================================================
                case PacketType.SearchUserRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<SearchUserReq>(packet.Body.ToByteArray());
                        using (var db = new MyChatContext())
                        {
                            var user = db.Users.FirstOrDefault(u => u.Account == req.Account);
                            var resp = new SearchUserResp { IsSuccess = user != null };
                            if (user != null) { resp.UserId = user.Id; resp.Nickname = user.Nickname; resp.Account = user.Account; }
                            Send(session, PacketType.SearchUserResponse, resp);
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"[搜索] 异常: {ex.Message}"); }
                    break;

                // ================================================================
                // 5. 添加好友 (申请逻辑)
                // ================================================================
                case PacketType.AddFriendRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<AddFriendReq>(packet.Body.ToByteArray());
                        using (var db = new MyChatContext())
                        {
                            if (db.Friends.Any(f => f.UserId == req.MyUserId && f.FriendId == req.FriendUserId))
                            {
                                Send(session, PacketType.AddFriendResponse, new AddFriendResp { IsSuccess = false, Message = "已经是好友了" });
                                break;
                            }

                            if (!db.FriendRequests.Any(r => r.SenderId == req.MyUserId && r.ReceiverId == req.FriendUserId && r.Status == 0))
                            {
                                var sender = db.Users.Find(req.MyUserId);
                                db.FriendRequests.Add(new FriendRequestEntity
                                {
                                    SenderId = req.MyUserId,
                                    ReceiverId = req.FriendUserId,
                                    Status = 0,
                                    CreateTime = DateTime.Now
                                });
                                db.SaveChanges();
                                Console.WriteLine($"[数据库] 好友申请已存入: {req.MyUserId} -> {req.FriendUserId}");

                                var targetSession = SessionManager.Instance.GetSessionByUserId(req.FriendUserId);
                                if (targetSession != null)
                                {
                                    var noti = new FriendRequestNotification { SenderId = req.MyUserId, SenderNickname = sender?.Nickname ?? "未知" };
                                    targetSession.Send(new NetworkPacket { Type = PacketType.FriendRequestNotification, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(noti)) });
                                    Console.WriteLine($"[推送] 已向在线用户 {req.FriendUserId} 推送申请");
                                }
                            }
                            Send(session, PacketType.AddFriendResponse, new AddFriendResp { IsSuccess = true, Message = "好友申请已发送，等待对方验证" });
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"加好友申请失败: {ex.Message}"); }
                    break;

                // ================================================================
                // 6. 处理好友申请 (同意/拒绝)
                // ================================================================
                case PacketType.HandleFriendRequestRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<HandleFriendRequestReq>(packet.Body.ToByteArray());
                        string myId = session.UserId; // 操作人

                        using (var db = new MyChatContext())
                        {
                            var friendReq = db.FriendRequests.FirstOrDefault(r => r.SenderId == req.RequesterId && r.ReceiverId == myId && r.Status == 0);
                            if (friendReq != null)
                            {
                                friendReq.Status = req.IsAccept ? 1 : 2;
                                if (req.IsAccept)
                                {
                                    if (!db.Friends.Any(f => f.UserId == req.RequesterId && f.FriendId == myId))
                                        db.Friends.Add(new FriendEntity { UserId = req.RequesterId, FriendId = myId, CreateTime = DateTime.Now });
                                    if (!db.Friends.Any(f => f.UserId == myId && f.FriendId == req.RequesterId))
                                        db.Friends.Add(new FriendEntity { UserId = myId, FriendId = req.RequesterId, CreateTime = DateTime.Now });

                                    // 通知发起人刷新
                                    var requesterSession = SessionManager.Instance.GetSessionByUserId(req.RequesterId);
                                    if (requesterSession != null)
                                    {
                                        var myNickname = db.Users.Find(myId)?.Nickname ?? "对方";
                                        requesterSession.Send(new NetworkPacket
                                        {
                                            Type = PacketType.HandleFriendRequestResponse,
                                            Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(new HandleFriendRequestResp
                                            {
                                                IsSuccess = true,
                                                Message = $"{myNickname} 同意了你的请求",
                                                FriendId = myId
                                            }))
                                        });
                                    }
                                }
                                db.SaveChanges();
                            }
                            Send(session, PacketType.HandleFriendRequestResponse, new HandleFriendRequestResp { IsSuccess = true, Message = req.IsAccept ? "已添加" : "已拒绝", FriendId = req.RequesterId });
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"处理好友请求失败: {ex.Message}"); }
                    break;

                // ================================================================
                // 7. 获取好友列表 (★★★ 修改点：自动添加 AI 机器人 ★★★)
                // ================================================================
                case PacketType.GetFriendListRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<GetFriendListReq>(packet.Body.ToByteArray());
                        var resp = new GetFriendListResp();

                        using (var db = new MyChatContext())
                        {
                            // ★★★ 自动检测并添加机器人好友 (ID: 9999) ★★★
                            if (!db.Friends.Any(f => f.UserId == req.UserId && f.FriendId == "9999"))
                            {
                                db.Friends.Add(new FriendEntity
                                {
                                    UserId = req.UserId,
                                    FriendId = "9999",
                                    CreateTime = DateTime.Now
                                });
                                // 机器人不需要反向添加用户也能回复，单向即可
                                db.SaveChanges();
                                Console.WriteLine($"[系统] 已自动将机器人加为用户 {req.UserId} 的好友");
                            }

                            // 1. 查好友关系表
                            var friendIds = db.Friends.Where(f => f.UserId == req.UserId).Select(f => f.FriendId).ToList();

                            // 2. 查用户详情
                            var users = db.Users.Where(u => friendIds.Contains(u.Id)).ToList();

                            foreach (var u in users)
                            {
                                // ★★★ 核心修改：判断在线状态 ★★★
                                // 如果是机器人(9999)，强制返回 True (在线)
                                // 否则，去 SessionManager 查
                                bool isOnline = (u.Id == "9999") || (SessionManager.Instance.GetSessionByUserId(u.Id) != null);

                                resp.Friends.Add(new FriendDto
                                {
                                    UserId = u.Id,
                                    Nickname = u.Nickname,
                                    Avatar = u.Avatar,
                                    IsOnline = isOnline
                                });
                            }
                        }
                        Console.WriteLine($"[好友列表] 返回 {resp.Friends.Count} 个好友");
                        Send(session, PacketType.GetFriendListResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"[获取列表] 异常: {ex.Message}"); }
                    break;

                // 其他 (群聊、重置密码等，保持原样)
                case PacketType.CreateGroupRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<CreateGroupReq>(packet.Body.ToByteArray());
                        string groupId = Guid.NewGuid().ToString("N").Substring(0, 8);
                        using (var db = new MyChatContext())
                        {
                            db.Groups.Add(new GroupEntity { Id = groupId, Name = req.GroupName, OwnerId = session.UserId, CreateTime = DateTime.Now });
                            db.GroupMembers.Add(new GroupMemberEntity { GroupId = groupId, UserId = session.UserId });
                            foreach (var uid in req.MemberIds) db.GroupMembers.Add(new GroupMemberEntity { GroupId = groupId, UserId = uid });
                            db.SaveChanges();
                        }
                        Send(session, PacketType.CreateGroupResponse, new CreateGroupResp { IsSuccess = true, GroupId = groupId, GroupName = req.GroupName, Message = "建群成功" });
                    }
                    catch (Exception ex) { Console.WriteLine($"建群失败: {ex.Message}"); }
                    break;

                case PacketType.GetGroupListRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<GetGroupListReq>(packet.Body.ToByteArray());
                        var resp = new GetGroupListResp();
                        using (var db = new MyChatContext())
                        {
                            var groupIds = db.GroupMembers.Where(m => m.UserId == req.UserId).Select(m => m.GroupId).ToList();
                            var groups = db.Groups.Where(g => groupIds.Contains(g.Id)).ToList();
                            foreach (var g in groups) resp.Groups.Add(new GroupDto { GroupId = g.Id, GroupName = g.Name, OwnerId = g.OwnerId });
                        }
                        Send(session, PacketType.GetGroupListResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"群列表失败: {ex.Message}"); }
                    break;

                case PacketType.GetGroupMembersRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<GetGroupMembersReq>(packet.Body.ToByteArray());
                        var resp = new GetGroupMembersResp { GroupId = req.GroupId };
                        using (var db = new MyChatContext())
                        {
                            var memberIds = db.GroupMembers.Where(m => m.GroupId == req.GroupId).Select(m => m.UserId).ToList();
                            var users = db.Users.Where(u => memberIds.Contains(u.Id)).ToList();
                            foreach (var u in users)
                            {
                                bool isOnline = SessionManager.Instance.GetSessionByUserId(u.Id) != null;
                                resp.Members.Add(new GroupMemberDto { UserId = u.Id, Nickname = u.Nickname, Avatar = u.Avatar, IsOnline = isOnline });
                            }
                        }
                        Send(session, PacketType.GetGroupMembersResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"群成员失败: {ex.Message}"); }
                    break;

                case PacketType.ResetPasswordRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<ResetPasswordReq>(packet.Body.ToByteArray());
                        var resp = new ResetPasswordResp();
                        using (var db = new MyChatContext())
                        {
                            var user = db.Users.FirstOrDefault(u => u.Account == req.Account);
                            if (user == null) { resp.IsSuccess = false; resp.Message = "账号不存在"; }
                            else if (user.Nickname != req.Nickname) { resp.IsSuccess = false; resp.Message = "身份验证失败"; }
                            else { user.Password = req.NewPassword; db.SaveChanges(); resp.IsSuccess = true; resp.Message = "密码重置成功"; }
                        }
                        Send(session, PacketType.ResetPasswordResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"重置密码错误: {ex.Message}"); }
                    break;
            }
        }

        private void Send<T>(ClientSession session, PacketType type, T data) where T : IMessage
        {
            session.Send(new NetworkPacket { Type = type, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(data)) });
        }
    }
}