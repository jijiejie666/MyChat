using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Server.Entities
{
    [Table("ServerMessages")]
    public class ServerMessageEntity
    {
        [Key]
        public string Id { get; set; } // 消息ID

        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Content { get; set; }
        public long SendTime { get; set; }
        public bool IsGroup { get; set; }
        public int Type { get; set; } // 0=Text, 1=Image, 2=File

        // 发送者信息缓存
        public string SenderName { get; set; }
        public string SenderAvatar { get; set; }

        // 文件信息
        public string FileName { get; set; }
        public long FileSize { get; set; }

        // ★★★ 关键字段：是否已送达 ★★★
        // P2P聊天：发给B，如果B在线转发成功则为true，否则false
        // (注：群聊离线消息逻辑较复杂，本教程先实现私聊离线消息)
        public bool IsDelivered { get; set; }
    }
}