using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Server.Entities
{
    [Table("Groups")]
    public class GroupEntity
    {
        [Key]
        public string Id { get; set; } // 群ID
        public string Name { get; set; } // 群名
        public string OwnerId { get; set; } // 群主ID
        public DateTime CreateTime { get; set; }
    }
}