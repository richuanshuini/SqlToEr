using System.Net.Http;
using System.Text;
using System.Text.Json;
using SqlToER.Helper;
using SqlToER.Model;

namespace SqlToER.Service
{
    /// <summary>
    /// 调用 OpenAI 兼容 API，将 SQL DDL 解析为陈氏 ER 图 JSON
    /// </summary>
    public class LlmService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(300) };

        private static readonly JsonSerializerOptions _prettyJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private const int MaxRetries = 2; // 最多重试 2 次（共 3 次请求）

        private const string SystemPromptBase = """
            你是一个数据库架构师。你的唯一任务是：分析用户提供的 SQL DDL，提取陈氏 ER 图的三个要素。

            ## 输出规则
            1. 你必须且只能返回合法的 JSON，不要任何解释文字、Markdown 标记或代码块。
            2. JSON 根节点包含三个 key：Entities, Attributes, Relationships。

            ## 关系推断规则
            - 外键约束 (FOREIGN KEY ... REFERENCES ...) → Entity1 持有外键，Entity2 被引用
            - 若 Entity1 的外键同时也是其主键 → 1:1
            - 若 Entity1 外键非主键 → 1:N（Entity2 一端，Entity1 多端）
            - 中间表（表仅含两个外键且它们构成联合主键）→ M:N，Name 取中间表名
            - 若无外键约束，则 Relationships 为空数组
            """;

        // 无模板时的 JSON Schema
        private const string SchemaNoLayout = """
            ## JSON Schema
            {
              "Entities": [{ "Name": "表名" }],
              "Attributes": [{ "EntityName": "所属表名", "Name": "字段名", "IsPrimaryKey": true/false }],
              "Relationships": [{ "Name": "关系描述", "Entity1": "表1", "Entity2": "表2", "Cardinality": "1:N" }]
            }
            """;

        // 有模板时的 JSON Schema（含坐标）
        private const string SchemaWithLayout = """
            ## JSON Schema（含布局坐标）
            每个元素必须包含 X 和 Y 属性（单位：英寸），表示在 Visio 画布上的精确位置。
            注意：Visio 坐标系 Y 轴向上递增。

            {
              "Entities": [{ "Name": "表名", "X": 2.0, "Y": 5.0 }],
              "Attributes": [{ "EntityName": "所属表名", "Name": "字段名", "IsPrimaryKey": true/false, "X": 1.5, "Y": 7.0 }],
              "Relationships": [{ "Name": "关系描述", "Entity1": "表1", "Entity2": "表2", "Cardinality": "1:N", "X": 5.0, "Y": 5.0 }]
            }

            ## 坐标分配规则（必须严格遵守）
            1. **实体（矩形）**：沿水平方向排列在同一行（相同 Y），相邻实体 X 间距 ≥ 4 英寸。
               - 如果实体和关系菱形在同一行，关系菱形要放在两个实体的 X 中点。
            2. **关系（菱形）**：放在它连接的两个实体之间。
               - X = 两个实体 X 的中点
               - Y = 实体 Y - 2.0（即实体行的下方 2 英寸），不要和实体同一行。
               - 如果两实体不相邻（中间隔了其他实体），则 Y 可以再偏下一些（Y = 实体 Y - 3.0）。
            3. **属性（椭圆）**：扇形分布在所属实体的上方。
               - 主键属性：X = 实体 X，Y = 实体 Y + 2.0（正上方）。
               - 其他属性：在主键两侧均匀展开，相邻属性 X 间距 = 1.2 英寸。
               - 如果属性较多（>4 个），分两层：第一层 Y = 实体 Y + 2.0，第二层 Y = 实体 Y + 3.2。
            4. **防重叠**：任意两个形状的中心距离不得小于 1.0 英寸。
            """;

        // 带坐标的示例
        private const string ExamplePromptWithLayout = """
            ## 示例（含坐标）
            输入:
            CREATE TABLE Department (DeptId INT PRIMARY KEY, Name VARCHAR(50));
            CREATE TABLE Employee (EmpId INT PRIMARY KEY, Name VARCHAR(50), DeptId INT, FOREIGN KEY (DeptId) REFERENCES Department(DeptId));

            输出:
            {"Entities":[{"Name":"Department","X":3.0,"Y":5.0},{"Name":"Employee","X":9.0,"Y":5.0}],"Attributes":[{"EntityName":"Department","Name":"DeptId","IsPrimaryKey":true,"X":3.0,"Y":7.0},{"EntityName":"Department","Name":"Name","IsPrimaryKey":false,"X":4.2,"Y":7.0},{"EntityName":"Employee","Name":"EmpId","IsPrimaryKey":true,"X":9.0,"Y":7.0},{"EntityName":"Employee","Name":"Name","IsPrimaryKey":false,"X":10.2,"Y":7.0},{"EntityName":"Employee","Name":"DeptId","IsPrimaryKey":false,"X":7.8,"Y":7.0}],"Relationships":[{"Name":"BelongsTo","Entity1":"Employee","Entity2":"Department","Cardinality":"1:N","X":6.0,"Y":3.0}]}
            """;

        // 无坐标的示例
        private const string ExamplePrompt = """
            ## 示例
            输入:
            CREATE TABLE Department (DeptId INT PRIMARY KEY, Name VARCHAR(50));
            CREATE TABLE Employee (EmpId INT PRIMARY KEY, Name VARCHAR(50), DeptId INT, FOREIGN KEY (DeptId) REFERENCES Department(DeptId));

            输出:
            {"Entities":[{"Name":"Department"},{"Name":"Employee"}],"Attributes":[{"EntityName":"Department","Name":"DeptId","IsPrimaryKey":true},{"EntityName":"Department","Name":"Name","IsPrimaryKey":false},{"EntityName":"Employee","Name":"EmpId","IsPrimaryKey":true},{"EntityName":"Employee","Name":"Name","IsPrimaryKey":false},{"EntityName":"Employee","Name":"DeptId","IsPrimaryKey":false}],"Relationships":[{"Name":"BelongsTo","Entity1":"Employee","Entity2":"Department","Cardinality":"1:N"}]}
            """;

        /// <summary>
        /// 构建完整的 System Prompt
        /// </summary>
        private static string BuildSystemPrompt(string? templateLayoutPrompt)
        {
            var sb = new StringBuilder();
            sb.AppendLine(SystemPromptBase);

            if (!string.IsNullOrWhiteSpace(templateLayoutPrompt))
            {
                sb.AppendLine(SchemaWithLayout);
                sb.AppendLine();
                sb.AppendLine("## 参考模板的布局信息（请参考此布局规律分配坐标）");
                sb.AppendLine(templateLayoutPrompt);
                sb.AppendLine(ExamplePromptWithLayout);
            }
            else
            {
                sb.AppendLine(SchemaNoLayout);
            }

            sb.AppendLine(ExamplePrompt);
            return sb.ToString();
        }

        /// <summary>
        /// 将 SQL DDL 发送给 AI，解析为 ErDocument
        /// </summary>
        /// <param name="sqlText">用户输入的 SQL DDL</param>
        /// <param name="provider">指定的 AI 提供商配置（为 null 时从磁盘读取活跃配置）</param>
        /// <param name="onStatus">状态回调（用于更新 UI）</param>
        /// <returns>解析后的 ErDocument 和原始 JSON 文本</returns>
        public async Task<(ErDocument Document, string RawJson)> ParseSqlToErJsonAsync(
            string sqlText,
            AiProviderConfig? provider = null,
            string? templateLayoutPrompt = null,
            Action<string>? onStatus = null)
        {
            // 如果没有指定 provider，从配置文件读取
            if (provider is null)
            {
                var config = AiConfigHelper.LoadConfig();
                provider = config.Providers.FirstOrDefault(p => p.Name == config.ActiveProvider)
                           ?? config.Providers.FirstOrDefault();
            }

            if (provider is null)
                throw new InvalidOperationException("未配置任何 AI 提供商，请先在「大模型配置」中设置");

            if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(provider.ApiKey))
                throw new InvalidOperationException($"提供商 \"{provider.Name}\" 的请求地址或 API Key 为空，请先配置");

            var model = string.IsNullOrEmpty(provider.SelectedModel)
                ? provider.Models.FirstOrDefault() ?? ""
                : provider.SelectedModel;

            // 构建初始 messages
            var systemPrompt = BuildSystemPrompt(templateLayoutPrompt);

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = sqlText }
            };

            string lastRawResponse = string.Empty;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                onStatus?.Invoke(attempt == 0
                    ? "正在调用 AI 解析 SQL..."
                    : $"AI 返回格式有误，正在重试 ({attempt}/{MaxRetries})...");

                // 发送请求
                lastRawResponse = await CallApiAsync(provider.BaseUrl, provider.ApiKey, model, messages);

                // 清洗 + 反序列化
                ErDocument doc;
                try
                {
                    doc = JsonHelper.CleanAndDeserialize<ErDocument>(lastRawResponse);
                }
                catch (JsonParseException ex)
                {
                    if (attempt >= MaxRetries) throw;

                    // 追加纠错消息，让 AI 自我纠正
                    messages.Add(new { role = "assistant", content = lastRawResponse });
                    messages.Add(new { role = "user", content = $"你的返回不是合法 JSON：{ex.Message}。请严格按要求重新返回纯 JSON。" });
                    continue;
                }

                // 结构验证
                var errors = ErDocumentValidator.Validate(doc);
                if (errors.Count == 0)
                {
                    onStatus?.Invoke("解析完成");
                    var cleanJson = JsonSerializer.Serialize(doc, _prettyJsonOptions);
                    return (doc, cleanJson);
                }

                if (attempt >= MaxRetries)
                {
                    onStatus?.Invoke($"⚠ 验证有 {errors.Count} 个警告，已返回最佳结果");
                    var cleanJson = JsonSerializer.Serialize(doc, _prettyJsonOptions);
                    return (doc, cleanJson);
                }

                // 追加纠错消息
                messages.Add(new { role = "assistant", content = lastRawResponse });
                messages.Add(new { role = "user", content = $"JSON 结构验证失败：{string.Join("；", errors)}。请修正后重新返回。" });
            }

            throw new InvalidOperationException("AI 解析失败，请检查 SQL 内容或更换模型");
        }

        /// <summary>
        /// 发送 OpenAI 兼容格式的 Chat Completions 请求
        /// </summary>
        private static async Task<string> CallApiAsync(
            string baseUrl, string apiKey, string model, List<object> messages)
        {
            var url = $"{baseUrl.TrimEnd('/')}/chat/completions";

            var requestBody = new
            {
                model,
                messages,
                temperature = 0.0  // 尽可能确定性输出
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"API 请求失败 ({(int)response.StatusCode})：{responseBody}");

            // 防御式解析，兼容不同提供商的响应格式
            return ExtractContentFromResponse(responseBody);
        }

        /// <summary>
        /// 从 OpenAI 兼容格式的响应中提取 AI 回复文本（兼容 message/delta/reasoning_content）
        /// </summary>
        private static string ExtractContentFromResponse(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);

            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                throw new InvalidOperationException($"API 响应缺少 choices 字段：{responseJson[..Math.Min(200, responseJson.Length)]}");

            var firstChoice = choices[0];
            string? content = null;

            // 标准路径: choices[0].message.content
            if (firstChoice.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var contentEl))
                    content = contentEl.GetString();

                // 部分模型使用 reasoning_content
                if (string.IsNullOrEmpty(content) && message.TryGetProperty("reasoning_content", out var reasoning))
                    content = reasoning.GetString();
            }

            // 流式响应兜底: choices[0].delta.content
            if (string.IsNullOrEmpty(content) && firstChoice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var deltaContent))
                    content = deltaContent.GetString();
            }

            return content ?? string.Empty;
        }
    }
}
