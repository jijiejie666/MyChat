using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyChat.Server.Entities
{
    // 这代表数据库里的一张表，表名叫 "Users"
    [Table("Users")]
    public class UserEntity
    {
        [Key] // 主键
        public string Id { get; set; }

        public string Account { get; set; }    // 账号
        public string Password { get; set; }   // 密码 (实际生产中应存Hash，这里演示存明文)
        public string Nickname { get; set; }   // 昵称
        public string Avatar { get; set; }     // 头像 (存颜色代码或图片URL)

        public DateTime CreateTime { get; set; }
    }
}