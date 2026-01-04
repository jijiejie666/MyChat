using Microsoft.EntityFrameworkCore;
using MyChat.Server.Entities;

namespace MyChat.Server.Database { 
public class MyChatContext : DbContext
{
    public DbSet<UserEntity> Users { get; set; }
    public DbSet<FriendEntity> Friends { get; set; }

    // 【新增】
    public DbSet<GroupEntity> Groups { get; set; }
    public DbSet<GroupMemberEntity> GroupMembers { get; set; }
        // 【新增】
        public DbSet<ServerMessageEntity> Messages { get; set; }
        public DbSet<FriendRequestEntity> FriendRequests { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=chat.db");
    }

}}