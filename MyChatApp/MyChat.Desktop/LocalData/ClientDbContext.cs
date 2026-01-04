using Microsoft.EntityFrameworkCore;
using System.IO;
using System;

namespace MyChat.Desktop.LocalData
{
    public class ClientDbContext : DbContext
    {
        public DbSet<LocalMessageEntity> Messages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // 在用户的文档目录下生成数据库，避免权限问题
            // 例如: C:\Users\YourName\Documents\MyChat\client.db
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyChat");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, "client.db");
            optionsBuilder.UseSqlite($"Data Source={path}");
        }
    }
}