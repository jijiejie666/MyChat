using System.Collections.Concurrent;

namespace MyChat.Server.Core
{
    public class SessionManager
    {
        // 单例模式
        public static SessionManager Instance { get; } = new SessionManager();

        // 存储所有连接：Key=SessionId, Value=ClientSession
        private ConcurrentDictionary<string, ClientSession> _sessions = new();

        // 存储已登录用户：Key=UserId, Value=ClientSession
        // 实际聊天时，我们通过 UserId 查找接收者的 Session
        private ConcurrentDictionary<string, ClientSession> _userSessions = new();

        private SessionManager() { }

        public void AddSession(ClientSession session)
        {
            _sessions.TryAdd(session.SessionId, session);
        }

        public void RemoveSession(ClientSession session)
        {
            _sessions.TryRemove(session.SessionId, out _);
            if (!string.IsNullOrEmpty(session.UserId))
            {
                _userSessions.TryRemove(session.UserId, out _);
            }
        }

        // 当用户登录成功后，注册 UserId
        public void RegisterUser(string userId, ClientSession session)
        {
            session.UserId = userId;
            // 如果该用户之前有连接，踢掉旧连接 (互踢)
            if (_userSessions.TryGetValue(userId, out var oldSession))
            {
                oldSession.Send(new Protocol.NetworkPacket
                {
                    Type = Protocol.PacketType.Unknown, // 可以定义一个 KICK_OFF 类型
                    // Body 可以放 "您的账号在别处登录"
                });
                oldSession.Disconnect();
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