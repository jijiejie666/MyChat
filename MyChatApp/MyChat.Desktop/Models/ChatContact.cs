using CommunityToolkit.Mvvm.ComponentModel;
using MyChat.Protocol;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging; // ★★★ 必须引用 (Bitmap)
using MyChat.Desktop.Helpers; // ★★★ 必须引用 (ImageHelper)

namespace MyChat.Desktop.Models
{
    public partial class ChatContact : ObservableObject
    {
        public string Id { get; set; }

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _lastMessage;

        [ObservableProperty]
        private string _avatarColor = "#CCCCCC"; // 默认背景色

        // ★★★ 新增：在线状态 ★★★
        [ObservableProperty]
        private bool _isOnline;

        // ★★★ 新增：UI绑定的图片对象 ★★★
        [ObservableProperty]
        private Bitmap? _avatarBitmap;

        // ★★★ 新增：Base64 数据源 (核心逻辑) ★★★
        private string _avatarBase64;
        public string AvatarBase64
        {
            get => _avatarBase64;
            set
            {
                if (SetProperty(ref _avatarBase64, value))
                {
                    // 逻辑：如果字符串很长(大于20字符)，通常是 Base64 图片数据
                    if (!string.IsNullOrEmpty(value) && value.Length > 20)
                    {
                        // 调用工具类转成图片
                        AvatarBitmap = ImageHelper.Base64ToBitmap(value);
                    }
                    else
                    {
                        // 否则清空图片
                        AvatarBitmap = null;

                        // 如果是颜色代码，赋值给背景色
                        if (!string.IsNullOrEmpty(value) && value.StartsWith("#"))
                        {
                            AvatarColor = value;
                        }
                        else
                        {
                            AvatarColor = "#CCCCCC";
                        }
                    }
                }
            }
        }

        // 聊天记录缓存
        public ObservableCollection<ChatMessage> MessageHistory { get; } = new();

        // 区分是群还是人
        public bool IsGroup { get; set; }

        // 标记这是不是一个好友申请
        public bool IsRequest { get; set; }

        // 群成员缓存
        public ObservableCollection<GroupMemberDto> Members { get; } = new();
    }
}