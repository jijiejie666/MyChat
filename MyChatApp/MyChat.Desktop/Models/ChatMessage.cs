using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MyChat.Desktop.Helpers;
using MyChat.Protocol;

namespace MyChat.Desktop.Models
{
    public partial class ChatMessage : ObservableObject
    {
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string SenderName { get; set; }

        // ★★★ 核心修复：把 Content 改为可通知的属性 ★★★
        [ObservableProperty]
        private string _content;

        public DateTime Time { get; set; }
        public bool IsMe { get; set; }
        public MsgType Type { get; set; }

        // 附件/图片消息专用
        public Bitmap? ImageContent { get; set; }
        public string FileName { get; set; }
        public string FileSizeStr { get; set; }

        public bool IsText => Type == MsgType.Text || Type == MsgType.Aistream;
        public bool IsImage => Type == MsgType.Image;
        public bool IsFile => Type == MsgType.File;

        // 头像
        [ObservableProperty]
        private Bitmap? _senderAvatarBitmap;
    }
}