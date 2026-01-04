using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Server.Entities
{
    [Table("GroupMembers")]
    public class GroupMemberEntity
    {
        [Key]
        public int Id { get; set; } // 自增ID

        public string GroupId { get; set; } // 哪个群
        public string UserId { get; set; }  // 哪个用户
    }
}