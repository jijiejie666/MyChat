using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace MyChat.Desktop.Helpers
{
    public static class ImageHelper
    {
        // 1. 文件路径 -> Base64 (发送用)
        public static string FileToBase64(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            byte[] bytes = File.ReadAllBytes(filePath);
            return Convert.ToBase64String(bytes);
        }

        // 2. Base64 -> Bitmap (显示用)
        public static Bitmap? Base64ToBitmap(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var stream = new MemoryStream(bytes))
                {
                    return new Bitmap(stream);
                }
            }
            catch
            {
                return null; // 如果解析失败（比如是坏的Base64），返回空
            }
        }
    }
}