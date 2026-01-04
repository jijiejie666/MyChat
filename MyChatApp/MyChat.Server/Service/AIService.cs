using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MyChat.Server.Services
{
    public class AIService
    {
        public static AIService Instance { get; } = new AIService();

        private static readonly HttpClient _httpClient = new HttpClient();

       
        private const string ApiKey = "sk-xafpxciyjwlftzpiitduepomhpgltcdvmmqvljdxrgiynpfl";

        // 2.  API 地址 
        private const string ApiUrl = "https://api.siliconflow.cn/v1/chat/completions";

        public async Task<string> GetReplyAsync(string question)
        {
            try
            {
               
                // 免费且强大的模型推荐: 
                // "Qwen/Qwen2.5-7B-Instruct" (速度快，完全免费)
                // "deepseek-ai/DeepSeek-V3" (如果你有赠金可以用这个)
                // "THUDM/glm-4-9b-chat" (完全免费)
                string modelName = "Qwen/Qwen2.5-7B-Instruct";

                var requestData = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "system", content = "你是一个由 MyChat 开发的 AI 助手，说话风趣幽默。" },
                        new { role = "user", content = question }
                    },
                    stream = false,
                    temperature = 0.7
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

                var response = await _httpClient.PostAsync(ApiUrl, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    var jsonNode = JsonNode.Parse(responseString);
                    string reply = jsonNode?["choices"]?[0]?["message"]?["content"]?.ToString();
                    return reply ?? "（AI 沉默了）";
                }
                else
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[AI API 错误] {response.StatusCode}: {errorMsg}");
                    return $"[系统] AI 接口报错: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI 异常] {ex.Message}");
                return "[系统] AI 响应超时或断网了。";
            }
        }
    }
}