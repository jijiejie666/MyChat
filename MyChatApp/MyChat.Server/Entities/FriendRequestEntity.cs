using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Server.Entities
{
    [Table("FriendRequests")]
    public class FriendRequestEntity
    {
        [Key]
        public int Id { get; set; }

        public string SenderId { get; set; }   // 发起人
        public string ReceiverId { get; set; } // 被加的人
        public int Status { get; set; }        // 0=等待, 1=已同意, 2=已拒绝
        public DateTime CreateTime { get; set; }
    }
}