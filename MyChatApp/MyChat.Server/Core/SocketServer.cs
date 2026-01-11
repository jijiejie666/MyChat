using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
                // 1. 初始化系统账号 (机器人 + 管理员)
                InitSystemAccounts();

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

        private void InitSystemAccounts()
        {
            using (var db = new MyChatContext())
            {
                // 1. 检查 AI 机器人 (9999)
                if (!db.Users.Any(u => u.Id == "9999"))
                {
                    db.Users.Add(new UserEntity
                    {
                        Id = "9999",
                        Account = "robot",
                        Password = "123",
                        Nickname = "AI 助手",
                        Avatar = "#00B894", // 绿色
                        CreateTime = DateTime.Now
                    });
                    Console.WriteLine("[系统] AI 机器人账号 (ID: 9999) 初始化完成");
                }

                // 2. 检查 超级管理员 (10000)
                if (!db.Users.Any(u => u.Id == "10000"))
                {
                    db.Users.Add(new UserEntity
                    {
                        Id = "10000",
                        Account = "superadmin",
                        Password = "admin",
                        Nickname = "系统管理员",
                        Avatar = "#FF5252",    // 红色 
                        CreateTime = DateTime.Now
                    });
                    Console.WriteLine("[系统] 超级管理员账号 (ID: 10000) 初始化完成");
                }

                db.SaveChanges();
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
        /// [功能增强版] 核心业务处理 (含头像更新逻辑)
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
                        string avatar = ""; // ★★★ 新增：准备接收头像 ★★★

                        using (var db = new MyChatContext())
                        {
                            var user = db.Users.FirstOrDefault(u => u.Account == req.Account);
                            if (user == null) msg = "账号不存在";
                            else if (user.Password != req.Password) msg = "密码错误";
                            else
                            {
                                isSuccess = true;
                                msg = "登录成功";
                                userId = user.Id;
                                nickname = user.Nickname;
                                avatar = user.Avatar; // ★★★ 从数据库读取头像 ★★★
                            }
                        }

                        if (isSuccess)
                        {
                            SessionManager.Instance.RegisterUser(userId, session);
                            Task.Run(() =>
                            {
                                try
                                {
                                    using (var db = new MyChatContext())
                                    {
                                        var myFriendIds = db.Friends.Where(f => f.UserId == userId).Select(f => f.FriendId).ToList();
                                        var notice = new FriendStatusNotice { FriendId = userId, IsOnline = true };
                                        var packetBody = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(notice));
                                        var noticePacket = new NetworkPacket { Type = PacketType.FriendStatusNotice, Body = packetBody };
                                        foreach (var friendId in myFriendIds) SessionManager.Instance.GetSessionByUserId(friendId)?.Send(noticePacket);

                                        var offlineMsgs = db.Messages.Where(m => m.ReceiverId == userId && !m.IsGroup && !m.IsDelivered).OrderBy(m => m.SendTime).ToList();
                                        if (offlineMsgs.Count > 0)
                                        {
                                            var mySession = SessionManager.Instance.GetSessionByUserId(userId);
                                            if (mySession != null)
                                            {
                                                foreach (var dbMsg in offlineMsgs)
                                                {
                                                    var chatMsg = new ChatMsg { Id = dbMsg.Id, SenderId = dbMsg.SenderId, ReceiverId = dbMsg.ReceiverId, Content = dbMsg.Content, SendTime = dbMsg.SendTime, IsGroup = dbMsg.IsGroup, Type = (MyChat.Protocol.MsgType)dbMsg.Type, SenderName = dbMsg.SenderName, SenderAvatar = dbMsg.SenderAvatar, FileName = dbMsg.FileName, FileSize = dbMsg.FileSize };
                                                    mySession.Send(new NetworkPacket { Type = PacketType.ChatMessage, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(chatMsg)) });
                                                    dbMsg.IsDelivered = true;
                                                }
                                                db.SaveChanges();
                                            }
                                        }

                                        var pendingReqs = db.FriendRequests.Where(r => r.ReceiverId == userId && r.Status == 0).ToList();
                                        if (pendingReqs.Count > 0)
                                        {
                                            var mySession = SessionManager.Instance.GetSessionByUserId(userId);
                                            if (mySession != null)
                                            {
                                                foreach (var preq in pendingReqs)
                                                {
                                                    var sender = db.Users.Find(preq.SenderId);
                                                    var noti = new FriendRequestNotification { SenderId = preq.SenderId, SenderNickname = sender?.Nickname ?? "未知" };
                                                    mySession.Send(new NetworkPacket { Type = PacketType.FriendRequestNotification, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(noti)) });
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine($"[登录后处理] 异常: {ex.Message}"); }
                            });
                        }
                        // ★★★ 把头像带回给客户端 ★★★
                        Send(session, PacketType.LoginResponse, new LoginResp { IsSuccess = isSuccess, Message = msg, UserId = userId, Nickname = nickname, Avatar = avatar });
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
                                    Avatar = "#" + new Random().Next(0x100000, 0xFFFFFF).ToString("X6"), // 默认随机色
                                    CreateTime = DateTime.Now
                                };
                                db.Users.Add(newUser);

                                if (newId != "10000")
                                {
                                    db.Friends.Add(new FriendEntity { UserId = newId, FriendId = "10000", CreateTime = DateTime.Now });
                                    db.Friends.Add(new FriendEntity { UserId = "10000", FriendId = newId, CreateTime = DateTime.Now });
                                }

                                db.SaveChanges();
                                isSuccess = true;
                                msg = "注册成功";

                                // 强制刷新管理员列表
                                var adminSession = SessionManager.Instance.GetSessionByUserId("10000");
                                if (adminSession != null)
                                {
                                    var adminFriendIds = db.Friends.Where(f => f.UserId == "10000").Select(f => f.FriendId).ToList();
                                    var adminFriends = db.Users.Where(u => adminFriendIds.Contains(u.Id)).ToList();
                                    var adminListResp = new GetFriendListResp();
                                    foreach (var u in adminFriends)
                                    {
                                        bool isOnline = (u.Id == "9999") || (SessionManager.Instance.GetSessionByUserId(u.Id) != null);
                                        adminListResp.Friends.Add(new FriendDto { UserId = u.Id, Nickname = u.Nickname, Avatar = u.Avatar, IsOnline = isOnline });
                                    }
                                    adminSession.Send(new NetworkPacket { Type = PacketType.GetFriendListResponse, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(adminListResp)) });
                                    Console.WriteLine($"[系统] 已向管理员推送最新花名册");
                                }
                            }
                        }
                        Send(session, PacketType.RegisterResponse, new RegisterResp { IsSuccess = isSuccess, Message = msg, NewUserId = newId });
                    }
                    catch (Exception ex) { Console.WriteLine($"[注册] 异常: {ex.Message}"); }
                    break;

                // ================================================================
                // 3. 聊天消息转发
                // ================================================================
                case PacketType.ChatMessage:
                    try
                    {
                        var chatMsg = ProtobufHelper.Deserialize<ChatMsg>(packet.Body.ToByteArray());
                        Console.WriteLine($"[消息] {chatMsg.SenderId} -> {chatMsg.ReceiverId} 内容: {chatMsg.Content}");

                        bool isAdmin = false;
                        if (chatMsg.SenderId == "10000" || chatMsg.SenderId == "8888") isAdmin = true;
                        else
                        {
                            if (!string.IsNullOrEmpty(chatMsg.SenderName) && chatMsg.SenderName.ToLower().Contains("admin")) isAdmin = true;
                            else
                            {
                                using (var db = new MyChatContext())
                                {
                                    var u = db.Users.Find(chatMsg.SenderId);
                                    if (u != null && u.Nickname.ToLower().Contains("admin")) isAdmin = true;
                                }
                            }
                        }

                        if (chatMsg.Content.StartsWith("/")) Console.WriteLine($"[指令调试] 用户:{chatMsg.SenderId} 尝试执行指令. IsAdmin结果: {isAdmin}");

                        if (isAdmin && chatMsg.Type == MyChat.Protocol.MsgType.Text)
                        {
                            if (chatMsg.Content.StartsWith("/broadcast "))
                            {
                                string broadcastContent = chatMsg.Content.Substring(11);
                                Console.WriteLine($"[Admin] 执行广播: {broadcastContent}");
                                var broadcastMsg = new ChatMsg { Id = Guid.NewGuid().ToString(), SenderId = "SYSTEM", SenderName = "📢 系统广播", Content = broadcastContent, SendTime = DateTime.UtcNow.Ticks, Type = MyChat.Protocol.MsgType.Text, SenderAvatar = "#FF5252" };
                                byte[] broadcastBytes = ProtobufHelper.Serialize(broadcastMsg);
                                var broadcastPacket = new NetworkPacket { Type = PacketType.ChatMessage, Body = Google.Protobuf.ByteString.CopyFrom(broadcastBytes) };
                                var allSessions = SessionManager.Instance.GetAllSessions();
                                foreach (var s in allSessions) s.Send(broadcastPacket);
                                return;
                            }
                            if (chatMsg.Content.StartsWith("/kick "))
                            {
                                string targetId = chatMsg.Content.Substring(6).Trim();
                                Console.WriteLine($"[Admin] 执行踢人: '{targetId}'");
                                var targetSession = SessionManager.Instance.GetSessionByUserId(targetId);
                                if (targetSession != null)
                                {
                                    var kickMsg = new ChatMsg { Id = Guid.NewGuid().ToString(), SenderId = "SYSTEM", SenderName = "系统", Content = "您已被管理员强制下线。", Type = MyChat.Protocol.MsgType.Text };
                                    targetSession.Send(new NetworkPacket { Type = PacketType.ChatMessage, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(kickMsg)) });
                                    Thread.Sleep(100);
                                    try { targetSession.ClientSocket?.Close(); } catch { }
                                    SessionManager.Instance.RemoveSession(targetId);
                                    Console.WriteLine($"[Admin] 用户 {targetId} 已成功踢出");
                                }
                                return;
                            }
                        }

                        using (var db = new MyChatContext())
                        {
                            var dbMsg = new ServerMessageEntity { Id = chatMsg.Id, SenderId = chatMsg.SenderId, ReceiverId = chatMsg.ReceiverId, Content = chatMsg.Content, SendTime = chatMsg.SendTime, IsGroup = chatMsg.IsGroup, Type = (int)chatMsg.Type, SenderName = chatMsg.SenderName, SenderAvatar = chatMsg.SenderAvatar, FileName = chatMsg.FileName, FileSize = chatMsg.FileSize, IsDelivered = false };

                            if (chatMsg.ReceiverId == "9999")
                            {
                                Console.WriteLine(">>> 正在调用 SiliconFlow AI 接口...");
                                dbMsg.IsDelivered = true;
                                _ = Task.Run(async () =>
                                {
                                    string replyMsgId = Guid.NewGuid().ToString();
                                    var userSession = SessionManager.Instance.GetSessionByUserId(chatMsg.SenderId);
                                    string fullContent = await MyChat.Server.Service.AIService.Instance.GetReplyStreamAsync(chatMsg.Content, async (segment) =>
                                    {
                                        if (userSession != null)
                                        {
                                            var streamMsg = new ChatMsg { Id = replyMsgId, SenderId = "9999", ReceiverId = chatMsg.SenderId, Content = segment, SendTime = DateTime.UtcNow.Ticks, Type = MyChat.Protocol.MsgType.Aistream, SenderName = "AI 助手", SenderAvatar = "#00B894" };
                                            userSession.Send(new NetworkPacket { Type = PacketType.ChatMessage, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(streamMsg)) });
                                        }
                                    });
                                    using (var replyDb = new MyChatContext())
                                    {
                                        replyDb.Messages.Add(new ServerMessageEntity { Id = replyMsgId, SenderId = "9999", ReceiverId = chatMsg.SenderId, Content = fullContent, SendTime = DateTime.UtcNow.Ticks, IsGroup = false, Type = (int)MyChat.Protocol.MsgType.Text, SenderName = "AI 助手", SenderAvatar = "#00B894", IsDelivered = true });
                                        replyDb.SaveChanges();
                                    }
                                });
                            }
                            else if (chatMsg.IsGroup)
                            {
                                dbMsg.IsDelivered = true;
                                var memberIds = db.GroupMembers.Where(g => g.GroupId == chatMsg.ReceiverId && g.UserId != chatMsg.SenderId).Select(g => g.UserId).ToList();
                                foreach (var memberId in memberIds) SessionManager.Instance.GetSessionByUserId(memberId)?.Send(packet);
                            }
                            else
                            {
                                var targetSession = SessionManager.Instance.GetSessionByUserId(chatMsg.ReceiverId);
                                if (targetSession != null) { targetSession.Send(packet); dbMsg.IsDelivered = true; }
                                else dbMsg.IsDelivered = false;
                            }
                            db.Messages.Add(dbMsg);
                            db.SaveChanges();
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"消息异常: {ex.Message}"); }
                    break;

                // ================================================================
                // 4. 用户相关 (搜索/加好友/更新信息)
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

                case PacketType.AddFriendRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<AddFriendReq>(packet.Body.ToByteArray());
                        using (var db = new MyChatContext())
                        {
                            if (db.Friends.Any(f => f.UserId == req.MyUserId && f.FriendId == req.FriendUserId)) Send(session, PacketType.AddFriendResponse, new AddFriendResp { IsSuccess = false, Message = "已经是好友了" });
                            else
                            {
                                if (!db.FriendRequests.Any(r => r.SenderId == req.MyUserId && r.ReceiverId == req.FriendUserId && r.Status == 0))
                                {
                                    db.FriendRequests.Add(new FriendRequestEntity { SenderId = req.MyUserId, ReceiverId = req.FriendUserId, Status = 0, CreateTime = DateTime.Now });
                                    db.SaveChanges();
                                    var targetSession = SessionManager.Instance.GetSessionByUserId(req.FriendUserId);
                                    if (targetSession != null)
                                    {
                                        var sender = db.Users.Find(req.MyUserId);
                                        var noti = new FriendRequestNotification { SenderId = req.MyUserId, SenderNickname = sender?.Nickname ?? "未知" };
                                        targetSession.Send(new NetworkPacket { Type = PacketType.FriendRequestNotification, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(noti)) });
                                    }
                                }
                                Send(session, PacketType.AddFriendResponse, new AddFriendResp { IsSuccess = true, Message = "好友申请已发送" });
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"加好友异常: {ex.Message}"); }
                    break;

                case PacketType.HandleFriendRequestRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<HandleFriendRequestReq>(packet.Body.ToByteArray());
                        string myId = session.UserId;
                        using (var db = new MyChatContext())
                        {
                            var friendReq = db.FriendRequests.FirstOrDefault(r => r.SenderId == req.RequesterId && r.ReceiverId == myId && r.Status == 0);
                            if (friendReq != null)
                            {
                                friendReq.Status = req.IsAccept ? 1 : 2;
                                if (req.IsAccept)
                                {
                                    if (!db.Friends.Any(f => f.UserId == req.RequesterId && f.FriendId == myId)) db.Friends.Add(new FriendEntity { UserId = req.RequesterId, FriendId = myId, CreateTime = DateTime.Now });
                                    if (!db.Friends.Any(f => f.UserId == myId && f.FriendId == req.RequesterId)) db.Friends.Add(new FriendEntity { UserId = myId, FriendId = req.RequesterId, CreateTime = DateTime.Now });
                                    var requesterSession = SessionManager.Instance.GetSessionByUserId(req.RequesterId);
                                    if (requesterSession != null)
                                    {
                                        var myName = db.Users.Find(myId)?.Nickname ?? "对方";
                                        requesterSession.Send(new NetworkPacket { Type = PacketType.HandleFriendRequestResponse, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(new HandleFriendRequestResp { IsSuccess = true, Message = $"{myName} 同意了请求", FriendId = myId })) });
                                    }
                                }
                                db.SaveChanges();
                            }
                            Send(session, PacketType.HandleFriendRequestResponse, new HandleFriendRequestResp { IsSuccess = true, Message = req.IsAccept ? "已添加" : "已拒绝", FriendId = req.RequesterId });
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"处理好友请求异常: {ex.Message}"); }
                    break;

                case PacketType.GetFriendListRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<GetFriendListReq>(packet.Body.ToByteArray());
                        var resp = new GetFriendListResp();
                        using (var db = new MyChatContext())
                        {
                            if (!db.Friends.Any(f => f.UserId == req.UserId && f.FriendId == "9999")) { db.Friends.Add(new FriendEntity { UserId = req.UserId, FriendId = "9999", CreateTime = DateTime.Now }); db.SaveChanges(); }
                            var friendIds = db.Friends.Where(f => f.UserId == req.UserId).Select(f => f.FriendId).ToList();
                            var users = db.Users.Where(u => friendIds.Contains(u.Id)).ToList();
                            foreach (var u in users)
                            {
                                bool isOnline = (u.Id == "9999") || (SessionManager.Instance.GetSessionByUserId(u.Id) != null);
                                resp.Friends.Add(new FriendDto { UserId = u.Id, Nickname = u.Nickname, Avatar = u.Avatar, IsOnline = isOnline });
                            }
                        }
                        Send(session, PacketType.GetFriendListResponse, resp);
                    }
                    catch (Exception ex) { Console.WriteLine($"获取好友列表异常: {ex.Message}"); }
                    break;

                // ================================================================
                // ★★★ 新增：更新用户信息 (头像/昵称) ★★★
                // ================================================================
                case PacketType.UpdateUserInfoRequest:
                    try
                    {
                        var req = ProtobufHelper.Deserialize<UpdateUserInfoReq>(packet.Body.ToByteArray());
                        Console.WriteLine($"[用户更新] 用户 {session.UserId} 请求更新信息");

                        if (req.UserId != session.UserId)
                        {
                            Send(session, PacketType.UpdateUserInfoResponse, new UpdateUserInfoResp { IsSuccess = false, Message = "非法操作" });
                            break;
                        }

                        string finalNickname = "";
                        string finalAvatar = "";
                        bool isUpdated = false;

                        using (var db = new MyChatContext())
                        {
                            var user = db.Users.Find(session.UserId);
                            if (user != null)
                            {
                                if (!string.IsNullOrEmpty(req.NewNickname) && user.Nickname != req.NewNickname)
                                {
                                    user.Nickname = req.NewNickname;
                                    isUpdated = true;
                                }
                                if (!string.IsNullOrEmpty(req.NewAvatar) && user.Avatar != req.NewAvatar)
                                {
                                    user.Avatar = req.NewAvatar;
                                    isUpdated = true;
                                }

                                if (isUpdated)
                                {
                                    db.SaveChanges(); // 1. 先保存到数据库

                                    // ===================================================================
                                    // ★★★ 新增核心功能：实时广播通知给所有在线好友 ★★★
                                    // ===================================================================

                                    // A. 查出该用户的所有好友ID
                                    var friendIds = db.Friends
                                        .Where(f => f.UserId == session.UserId)
                                        .Select(f => f.FriendId)
                                        .ToList();

                                    if (friendIds.Count > 0)
                                    {
                                        // B. 准备通知包 (告诉好友：我变了)
                                        var notice = new FriendInfoUpdateNotice
                                        {
                                            FriendId = session.UserId, // 谁变了？我变了
                                            Nickname = user.Nickname,  // 我的新昵称
                                            Avatar = user.Avatar       // 我的新头像(Base64)
                                        };

                                        // C. 序列化通知包
                                        byte[] noticeBytes = ProtobufHelper.Serialize(notice);
                                        var noticePacket = new NetworkPacket
                                        {
                                            Type = PacketType.FriendInfoUpdateNotice, // ID: 72
                                            Body = Google.Protobuf.ByteString.CopyFrom(noticeBytes),
                                            Timestamp = DateTime.UtcNow.Ticks
                                        };

                                        // D. 查找在线好友并推送
                                        int sentCount = 0;
                                        foreach (var fid in friendIds)
                                        {
                                            // 从 SessionManager 查找该好友是否在线
                                            var friendSession = SessionManager.Instance.GetSessionByUserId(fid);
                                            if (friendSession != null)
                                            {
                                                friendSession.Send(noticePacket);
                                                sentCount++;
                                            }
                                        }
                                        Console.WriteLine($"[系统] 头像更新广播: 已推送给 {sentCount} 位在线好友");
                                    }
                                    // ===================================================================
                                }

                                finalNickname = user.Nickname;
                                finalAvatar = user.Avatar;
                            }
                            else
                            {
                                Send(session, PacketType.UpdateUserInfoResponse, new UpdateUserInfoResp { IsSuccess = false, Message = "用户不存在" });
                                break;
                            }
                        }

                        // 最后回应给自己 (告诉自己更新成功了)
                        Send(session, PacketType.UpdateUserInfoResponse, new UpdateUserInfoResp
                        {
                            IsSuccess = true,
                            Message = isUpdated ? "更新成功" : "无变更",
                            UpdatedNickname = finalNickname,
                            UpdatedAvatar = finalAvatar
                        });
                        Console.WriteLine($"[用户更新] 结果: {(isUpdated ? "成功" : "无变更")}");
                    }
                    catch (Exception ex) { Console.WriteLine($"[用户更新] 异常: {ex.Message}"); }
                    break;

                // ================================================================
                // 5. 群组相关
                // ================================================================
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

                        var noti = new GroupInvitationNotification { GroupId = groupId, GroupName = req.GroupName, OwnerId = session.UserId };
                        var notiPacket = new NetworkPacket { Type = PacketType.GroupInvitationNotification, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(noti)) };
                        foreach (var uid in req.MemberIds) if (uid != session.UserId) SessionManager.Instance.GetSessionByUserId(uid)?.Send(notiPacket);
                    }
                    catch (Exception ex) { Console.WriteLine($"建群异常: {ex.Message}"); }
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
                    catch (Exception ex) { Console.WriteLine($"获取群列表异常: {ex.Message}"); }
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
                    catch (Exception ex) { Console.WriteLine($"获取群成员异常: {ex.Message}"); }
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
                    catch (Exception ex) { Console.WriteLine($"重置密码异常: {ex.Message}"); }
                    break;
            }
        }

        private void Send<T>(ClientSession session, PacketType type, T data) where T : IMessage
        {
            session.Send(new NetworkPacket { Type = type, Body = Google.Protobuf.ByteString.CopyFrom(ProtobufHelper.Serialize(data)) });
        }
    }
}