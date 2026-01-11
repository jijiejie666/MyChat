using System;
using System.Collections.Concurrent;
using System.Collections.Generic; // 必须引用
using System.Linq;              // 必须引用

namespace MyChat.Server.Core
{
    public class SessionManager
    {
        // 单例模式
        public static SessionManager Instance { get; } = new SessionManager();

        // 存储所有连接：Key=SessionId, Value=ClientSession
        private ConcurrentDictionary<string, ClientSession> _sessions = new();

        // 存储已登录用户：Key=UserId, Value=ClientSession
        private ConcurrentDictionary<string, ClientSession> _userSessions = new();

        private SessionManager() { }

        public void AddSession(ClientSession session)
        {
            _sessions.TryAdd(session.SessionId, session);
        }

        // 旧方法：通过 Session 对象移除 (SocketServer 可能还在用，保留它)
        public void RemoveSession(ClientSession session)
        {
            _sessions.TryRemove(session.SessionId, out _);
            if (!string.IsNullOrEmpty(session.UserId))
            {
                _userSessions.TryRemove(session.UserId, out _);
            }
        }

        // ★★★ 修复核心 1：新增通过 UserId 移除会话 (用于踢人指令) ★★★
        // 解决了 "参数 1: 无法从 string 转换为 ClientSession" 的报错
        public void RemoveSession(string userId)
        {
            if (_userSessions.TryRemove(userId, out var session))
            {
                // 同时从总连接池中移除
                if (session != null)
                {
                    _sessions.TryRemove(session.SessionId, out _);
                }
                Console.WriteLine($"[Session] 用户 {userId} 已强制移除");
            }
        }

        // ★★★ 修复核心 2：新增获取所有在线 Session (用于广播指令) ★★★
        // 解决了 "SessionManager 未包含 GetAllSessions 的定义" 的报错
        public List<ClientSession> GetAllSessions()
        {
            return _userSessions.Values.ToList();
        }

        // 当用户登录成功后，注册 UserId
        public void RegisterUser(string userId, ClientSession session)
        {
            session.UserId = userId;
            // 如果该用户之前有连接，踢掉旧连接 (互踢)
            if (_userSessions.TryGetValue(userId, out var oldSession))
            {
                // 只有当旧连接和新连接不是同一个 Socket 时才踢
                if (oldSession != session)
                {
                    try
                    {
                        oldSession.Send(new Protocol.NetworkPacket
                        {
                            Type = Protocol.PacketType.Unknown,
                        });
                        oldSession.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Session] 互踢异常: {ex.Message}");
                    }
                }
            }

            _userSessions[userId] = session;
            Console.WriteLine($"用户 {userId} 上线了！当前在线: {_userSessions.Count}");
        }

        public ClientSession? GetSessionByUserId(string userId)
        {
            _userSessions.TryGetValue(userId, out var session);
            return session;
        }
    }
}