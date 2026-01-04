using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Server.Entities
{
    [Table("Friends")]
    public class FriendEntity
    {
        [Key]
        public int Id { get; set; } // 自增主键

        public string UserId { get; set; }      // 发起方 ID
        public string FriendId { get; set; }    // 接受方 (好友) ID

        // 状态：0=申请中, 1=已添加, 2=拒绝 (为了简单，我们暂时只做直接添加，默认存 1)
        public int State { get; set; }

        public DateTime CreateTime { get; set; }
    }
}