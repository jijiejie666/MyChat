using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Desktop.LocalData
{
    // 本地存的消息表
    [Table("LocalMessages")]
    public class LocalMessageEntity
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Content { get; set; }
        public bool IsMe { get; set; }
        public long TimeTicks { get; set; }

        // 【新增】 0=Text, 1=Image
        public int Type { get; set; }
        // 【新增】冗余存储发送者信息，避免历史记录变成空白头像
        public string SenderName { get; set; }
        public string SenderAvatar { get; set; }
        // 【新增】
        public string FileName { get; set; }
        public long FileSize { get; set; }
    }
}