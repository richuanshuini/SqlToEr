using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlToER.Helper;
using SqlToER.Model;

namespace SqlToER.ViewModel
{
    /// <summary>
    /// AI 配置窗口的 ViewModel
    /// </summary>
    public partial class AiConfigWindowViewModel : ObservableObject
    {
        // ============ 绑定属性 ============

        /// <summary>
        /// 提供商列表
        /// </summary>
        public ObservableCollection<AiProviderConfig> Providers { get; } = [];

        /// <summary>
        /// 当前选中的提供商
        /// </summary>
        [ObservableProperty]
        private AiProviderConfig? _selectedProvider;

        /// <summary>
        /// 当前选中提供商的 Base URL（双向编辑）
        /// </summary>
        [ObservableProperty]
        private string _editBaseUrl = string.Empty;

        /// <summary>
        /// 当前选中提供商的 API Key（双向编辑）
        /// </summary>
        [ObservableProperty]
        private string _editApiKey = string.Empty;

        /// <summary>
        /// 当前选中提供商的可用模型列表（下拉项）
        /// </summary>
        public ObservableCollection<string> AvailableModels { get; } = [];

        /// <summary>
        /// 当前选中的模型
        /// </summary>
        [ObservableProperty]
        private string? _selectedModel;

        /// <summary>
        /// 保存结果提示
        /// </summary>
        [ObservableProperty]
        private string _saveMessage = string.Empty;

        /// <summary>
        /// 测试结果提示
        /// </summary>
        [ObservableProperty]
        private string _testResult = string.Empty;

        /// <summary>
        /// 测试中状态
        /// </summary>
        [ObservableProperty]
        private bool _isTesting;

        // ============ 内部状态 ============

        private AiConfigRoot _configRoot = new();
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

        // ============ 构造 ============

        public AiConfigWindowViewModel()
        {
            LoadFromDisk();
        }

        // ============ 属性变更联动 ============

        /// <summary>
        /// 记录上一个选中的提供商，用于切换时写回编辑
        /// </summary>
        private AiProviderConfig? _previousProvider;

        partial void OnSelectedProviderChanged(AiProviderConfig? value)
        {
            // 切换前：将编辑写回上一个提供商
            if (_previousProvider is not null)
                ApplyEditsTo(_previousProvider);
            _previousProvider = value;

            if (value is null)
            {
                EditBaseUrl = string.Empty;
                EditApiKey = string.Empty;
                AvailableModels.Clear();
                SelectedModel = null;
                return;
            }

            EditBaseUrl = value.BaseUrl;
            EditApiKey = value.ApiKey;

            AvailableModels.Clear();
            foreach (var m in value.Models)
                AvailableModels.Add(m);

            SelectedModel = string.IsNullOrEmpty(value.SelectedModel)
                ? AvailableModels.FirstOrDefault()
                : value.SelectedModel;

            // 切换提供商时清空测试结果
            TestResult = string.Empty;
        }

        // ============ 命令 ============

        /// <summary>
        /// 保存配置
        /// </summary>
        [RelayCommand]
        private void Save()
        {
            ApplyEditsTo(SelectedProvider);
            _configRoot.ActiveProvider = SelectedProvider?.Name ?? string.Empty;
            AiConfigHelper.SaveConfig(_configRoot);
            SaveMessage = "✅ 配置已保存";
        }

        /// <summary>
        /// 测试连接：向 Base URL 发送一个最小的 OpenAI Chat Completions 请求
        /// </summary>
        [RelayCommand]
        private async Task TestConnectionAsync()
        {
            var baseUrl = EditBaseUrl.TrimEnd('/');
            var apiKey = EditApiKey;
            var model = SelectedModel;

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                TestResult = "❌ 请先填写请求地址";
                return;
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                TestResult = "❌ 请先填写 API Key";
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                TestResult = "❌ 请先选择模型";
                return;
            }

            IsTesting = true;
            TestResult = "⏳ 正在测试连接...";

            try
            {
                var url = $"{baseUrl}/chat/completions";

                var requestBody = new
                {
                    model,
                    messages = new[] { new { role = "user", content = "hello" } },
                    max_tokens = 10
                };

                var json = JsonSerializer.Serialize(requestBody);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var reply = ExtractReplyContent(responseBody);
                    TestResult = $"✅ 连接成功！模型回复：{reply}";
                }
                else
                {
                    TestResult = $"❌ 请求失败 ({(int)response.StatusCode})：{TruncateText(responseBody, 200)}";
                }
            }
            catch (TaskCanceledException)
            {
                TestResult = "❌ 请求超时（30 秒）";
            }
            catch (HttpRequestException ex)
            {
                TestResult = $"❌ 网络错误：{ex.Message}";
            }
            catch (Exception ex)
            {
                TestResult = $"❌ 异常：{ex.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        }

        // ============ 私有方法 ============

        private void LoadFromDisk()
        {
            _configRoot = AiConfigHelper.LoadConfig();
            Providers.Clear();
            foreach (var p in _configRoot.Providers)
                Providers.Add(p);

            SelectedProvider = Providers.FirstOrDefault();
        }

        /// <summary>
        /// 将界面编辑的值写回指定的 Provider 对象
        /// </summary>
        private void ApplyEditsTo(AiProviderConfig? provider)
        {
            if (provider is null) return;

            provider.BaseUrl = EditBaseUrl;
            provider.ApiKey = EditApiKey;
            provider.SelectedModel = SelectedModel ?? string.Empty;
        }

        /// <summary>
        /// 从 OpenAI 格式的 JSON 响应中提取第一条回复文本
        /// </summary>
        private static string ExtractReplyContent(string responseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var firstChoice = doc.RootElement.GetProperty("choices")[0];

                // 尝试多种字段路径（兼容不同提供商）
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

                if (!string.IsNullOrEmpty(content))
                    return TruncateText(content, 100);

                // 都拿不到则显示原始 JSON
                return TruncateText(responseJson, 150);
            }
            catch
            {
                return TruncateText(responseJson, 150);
            }
        }

        private static string TruncateText(string text, int maxLen)
        {
            return text.Length <= maxLen ? text : text[..maxLen] + "...";
        }
    }
}
