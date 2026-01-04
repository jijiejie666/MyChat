using MyChat.Server.Core;
using MyChat.Server.Database;
using MyChat.Server.Entities;

namespace MyChat.Server
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "MyChat Server Console";

            // 1. 数据库初始化 (关键步骤)
            InitDatabase();

            // 2. 启动 Socket 服务
            var server = new SocketServer();
            server.Start(5555);

            Console.ReadLine();
        }

        private static void InitDatabase()
        {
            Console.WriteLine("[数据库] 正在检查数据库...");

            using (var db = new MyChatContext())
            {
                // 如果数据库不存在，则创建它 (包含建表)
                if (db.Database.EnsureCreated())
                {
                    Console.WriteLine("[数据库] 检测到新环境，正在创建 chat.db 并写入初始数据...");

                    // 写入两个初始用户，这样你不用注册就能测试
                    var admin = new UserEntity
                    {
                        Id = "8888",
                        Account = "admin",
                        Password = "123",
                        Nickname = "管理员大魔王",
                        Avatar = "#5B60F6",
                        CreateTime = DateTime.Now
                    };

                    var user = new UserEntity
                    {
                        Id = "1001",
                        Account = "user",
                        Password = "123",
                        Nickname = "摸鱼小能手",
                        Avatar = "#19C37D",
                        CreateTime = DateTime.Now
                    };

                    db.Users.AddRange(admin, user);
                    db.SaveChanges();
                    Console.WriteLine("[数据库] 初始数据写入完成！");
                }
                else
                {
                    Console.WriteLine("[数据库] 数据库已存在，跳过初始化。");
                }
            }
        }
    }
}