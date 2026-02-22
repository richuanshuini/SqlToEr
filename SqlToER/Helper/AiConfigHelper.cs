using System.IO;
using System.Text.Json;
using SqlToER.Model;

namespace SqlToER.Helper
{
    /// <summary>
    /// AI 配置的 JSON 读写工具
    /// </summary>
    public static class AiConfigHelper
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 配置文件路径（exe 所在目录下 Helper/AiConfig/ai_config.json）
        /// </summary>
        private static string ConfigFilePath
        {
            get
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Helper", "AiConfig");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "ai_config.json");
            }
        }

        /// <summary>
        /// 加载配置（文件不存在则返回默认配置）
        /// </summary>
        public static AiConfigRoot LoadConfig()
        {
            var path = ConfigFilePath;
            if (!File.Exists(path))
                return CreateDefaultConfig();

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AiConfigRoot>(json, _jsonOptions) ?? CreateDefaultConfig();
            }
            catch
            {
                return CreateDefaultConfig();
            }
        }

        /// <summary>
        /// 保存配置到 JSON 文件
        /// </summary>
        public static void SaveConfig(AiConfigRoot config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }

        /// <summary>
        /// 创建默认预置配置（4 个提供商，BaseUrl 和 ApiKey 留空由用户填入）
        /// </summary>
        private static AiConfigRoot CreateDefaultConfig()
        {
            return new AiConfigRoot
            {
                ActiveProvider = "智谱 GLM",
                Providers =
                [
                    new AiProviderConfig
                    {
                        Name = "智谱 GLM",
                        BaseUrl = "",
                        ApiKey = "",
                        Models = ["glm-5", "glm-4.7"],
                        SelectedModel = "glm-5"
                    },
                    new AiProviderConfig
                    {
                        Name = "MiniMax",
                        BaseUrl = "",
                        ApiKey = "",
                        Models = ["MiniMax-M2.5"],
                        SelectedModel = "MiniMax-M2.5"
                    },
                    new AiProviderConfig
                    {
                        Name = "Kimi",
                        BaseUrl = "",
                        ApiKey = "",
                        Models = ["kimi-k2.5"],
                        SelectedModel = "kimi-k2.5"
                    }
                ]
            };
        }
    }
}
