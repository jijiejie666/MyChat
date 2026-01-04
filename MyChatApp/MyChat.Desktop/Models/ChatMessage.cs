using Avalonia.Media.Imaging; // 必须引用

namespace MyChat.Desktop.Models
{
    public class ChatMessage
    {
        public string Id { get; set; }
        public string SenderName { get; set; }
        public string Content { get; set; }
        public System.DateTime Time { get; set; }
        public bool IsMe { get; set; }

        // 【新增】消息类型
        public MyChat.Protocol.MsgType Type { get; set; }

        // 【新增】图片对象 (如果是文本消息，这里为 null)
        public Bitmap? ImageContent { get; set; }
        // 【新增】
        public string FileName { get; set; }
        public string FileSizeStr { get; set; } // 格式化后的大小 (如 "2.5 MB")

        // 我们还可以加一个属性，用来绑定“是否是文件”
        // (虽然可以用 Type 判断，但这样方便 XAML 绑定)
        public bool IsFile => Type == MyChat.Protocol.MsgType.File;
        public bool IsImage => Type == MyChat.Protocol.MsgType.Image;
        public bool IsText => Type == MyChat.Protocol.MsgType.Text;
    }
}