using CommunityToolkit.Mvvm.ComponentModel;
using MyChat.Protocol;
using System.Collections.ObjectModel;

namespace MyChat.Desktop.Models
{
    public partial class ChatContact : ObservableObject
    {
        public string Id { get; set; }
        public string Name { get; set; }

        [ObservableProperty]
        private string _lastMessage;

        [ObservableProperty]
        private string _avatarColor;

        // 聊天记录缓存
        public ObservableCollection<ChatMessage> MessageHistory { get; } = new();

        // 区分是群还是人
        public bool IsGroup { get; set; }

        // ★★★ 关键字段：标记这是不是一个好友申请 ★★★
        // 如果是 True，界面显示同意/拒绝；如果是 False，显示聊天框
        public bool IsRequest { get; set; }

        // 群成员缓存
        public ObservableCollection<GroupMemberDto> Members { get; } = new();
    }
}