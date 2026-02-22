namespace SqlToER.Model
{
    /// <summary>
    /// 单个 AI 提供商的配置
    /// </summary>
    public class AiProviderConfig
    {
        /// <summary>
        /// 提供商名称（如 "智谱 GLM"、"Kimi" 等）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// API 请求地址（OpenAI 兼容格式，如 https://xxx/v1）
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// API Key
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// 该提供商支持的模型列表
        /// </summary>
        public List<string> Models { get; set; } = [];

        /// <summary>
        /// 当前选中的模型
        /// </summary>
        public string SelectedModel { get; set; } = string.Empty;
    }

    /// <summary>
    /// AI 配置根对象（序列化/反序列化用）
    /// </summary>
    public class AiConfigRoot
    {
        /// <summary>
        /// 所有提供商配置
        /// </summary>
        public List<AiProviderConfig> Providers { get; set; } = [];

        /// <summary>
        /// 当前激活的提供商名称
        /// </summary>
        public string ActiveProvider { get; set; } = string.Empty;
    }
}
