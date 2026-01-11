using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MyChat.Server.Service // 注意命名空间，保持和原来一致
{
    public class AIService
    {
        public static AIService Instance { get; } = new AIService();

        private static readonly HttpClient _httpClient = new HttpClient();

        // 您的 API Key
        private const string ApiKey = "sk-xafpxciyjwlftzpiitduepomhpgltcdvmmqvljdxrgiynpfl";
        // API 地址
        private const string ApiUrl = "https://api.siliconflow.cn/v1/chat/completions";

        private AIService() { }

        /// <summary>
        /// ★★★ 新增：流式获取 AI 回复 ★★★
        /// </summary>
        /// <param name="question">用户的问题</param>
        /// <param name="onSegmentReceived">回调函数：每生成一个字，就调用一次这个函数，推给客户端</param>
        /// <returns>返回完整的回复内容（用于最后存数据库）</returns>
        public async Task<string> GetReplyStreamAsync(string question, Func<string, Task> onSegmentReceived)
        {
            var fullContent = new StringBuilder();

            try
            {
                // 模型名称 (Qwen2.5-7B 速度快，适合流式演示)
                string modelName = "Qwen/Qwen2.5-7B-Instruct";

                var requestData = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = "你是一个由 MyChat 开发的 AI 助手，说话风趣幽默。" },
                        new { role = "user", content = question }
                    },
                    stream = true, // ★★★ 关键点：开启流式模式 ★★★
                    temperature = 0.7
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Headers.Add("Authorization", $"Bearer {ApiKey}");
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // ★★★ 关键点：ResponseHeadersRead 表示只读完头部就开始处理，不等待包体下载完
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    string err = $"[AI API 错误] {response.StatusCode}: {errorMsg}";
                    Console.WriteLine(err);
                    // 如果出错，把错误信息推给前端
                    if (onSegmentReceived != null) await onSegmentReceived(err);
                    return err;
                }

                // 获取响应流
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();

                    // SSE 格式通常是以 "data: " 开头
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                    var data = line.Substring(6).Trim(); // 去掉 "data: " 前缀

                    if (data == "[DONE]") break; // 结束标志

                    try
                    {
                        var jsonNode = JsonNode.Parse(data);
                        // 提取增量内容 (delta.content)
                        var contentChunk = jsonNode?["choices"]?[0]?["delta"]?["content"]?.ToString();

                        if (!string.IsNullOrEmpty(contentChunk))
                        {
                            // 1. 拼接到完整内容里 (用于存库)
                            fullContent.Append(contentChunk);

                            // 2. ★★★ 实时推送：把这个字推给 SocketServer ★★★
                            if (onSegmentReceived != null)
                            {
                                await onSegmentReceived(contentChunk);
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Console.WriteLine($"[解析错误] {parseEx.Message} 数据: {data}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI 异常] {ex.Message}");
                string errMsg = "\n[系统] AI 连接中断。";
                if (onSegmentReceived != null) await onSegmentReceived(errMsg);
                fullContent.Append(errMsg);
            }

            return fullContent.ToString();
        }

        // 保留原有的非流式方法（为了兼容旧代码）
        public async Task<string> GetReplyAsync(string question)
        {
            return await GetReplyStreamAsync(question, null);
        }
    }
}