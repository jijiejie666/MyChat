using System;
using System.IO;

namespace MyChat.Desktop.Helpers
{
    public static class FileHelper
    {
        // 1. 读文件 -> Base64
        public static string FileToBase64(string path)
        {
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            return Convert.ToBase64String(bytes);
        }

        // 2. Base64 -> 存文件
        // 返回保存后的完整路径
        public static string SaveBase64ToFile(string base64, string fileName)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);

                // 保存到 "文档/MyChat/Files" 文件夹
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyChat", "Files");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                // 防止文件名冲突，加个时间戳
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                string newName = $"{nameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
                string fullPath = Path.Combine(folder, newName);

                File.WriteAllBytes(fullPath, bytes);
                return fullPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("保存文件失败: " + ex.Message);
                return null;
            }
        }

        // 3. 格式化文件大小 (比如把 1024000 变成 "1.0 MB")
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}