using System.Text.Json;
using System.Text.RegularExpressions;

namespace SqlToER.Helper
{
    /// <summary>
    /// JSON 解析异常（携带原始文本和内部异常）
    /// </summary>
    public class JsonParseException : Exception
    {
        public string RawText { get; }

        public JsonParseException(string message, string rawText, Exception? inner = null)
            : base(message, inner)
        {
            RawText = rawText;
        }
    }

    /// <summary>
    /// JSON 清理与反序列化工具
    /// </summary>
    public static class JsonHelper
    {
        // 匹配 ```json ... ``` 或 ``` ... ``` 包裹的代码块
        private static readonly Regex MarkdownCodeBlock =
            new(@"```\w*\s*\n?([\s\S]*?)\n?\s*```", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// 清理大模型返回的原始文本（剥离 Markdown 标记），然后反序列化为 T。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="rawOutput">大模型原始输出</param>
        /// <returns>反序列化后的对象</returns>
        /// <exception cref="JsonParseException">清理或反序列化失败时抛出</exception>
        public static T CleanAndDeserialize<T>(string rawOutput)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
                throw new JsonParseException("AI 返回内容为空", rawOutput);

            var cleaned = StripMarkdown(rawOutput);

            try
            {
                var result = JsonSerializer.Deserialize<T>(cleaned, Options);
                if (result is null)
                    throw new JsonParseException("反序列化结果为 null，请检查 JSON 结构是否匹配目标类型", cleaned);
                return result;
            }
            catch (JsonException ex)
            {
                throw new JsonParseException(
                    $"JSON 反序列化失败：{ex.Message}",
                    cleaned,
                    ex);
            }
        }

        /// <summary>
        /// 剥离 Markdown 代码块标记，提取纯 JSON 文本
        /// </summary>
        private static string StripMarkdown(string text)
        {
            var trimmed = text.Trim();

            // 尝试提取 ```json ... ``` 代码块内容
            var match = MarkdownCodeBlock.Match(trimmed);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // 没有代码块标记，尝试找到第一个 { 或 [ 开始的 JSON
            var jsonStart = trimmed.IndexOfAny(['{', '[']);
            if (jsonStart >= 0)
            {
                var openChar = trimmed[jsonStart];
                var closeChar = openChar == '{' ? '}' : ']';
                var jsonEnd = trimmed.LastIndexOf(closeChar);
                if (jsonEnd > jsonStart)
                    return trimmed[jsonStart..(jsonEnd + 1)];
            }

            // 原样返回，交给反序列化报错
            return trimmed;
        }
    }
}
