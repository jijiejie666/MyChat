using Avalonia.Media.Imaging;
using System;
using System.IO;

namespace MyChat.Desktop.Helpers // 命名空间改为 Helpers
{
    public static class ImageHelper
    {
        /// <summary>
        /// Base64 字符串转 Avalonia Bitmap (用于显示)
        /// </summary>
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
                return null;
            }
        }

        /// <summary>
        /// 本地文件转 Base64 字符串 (用于上传)
        /// </summary>
        public static string FileToBase64(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                return "";
            }
        }
    }
}