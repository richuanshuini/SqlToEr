using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlToER.Helper;
using SqlToER.Model;
using SqlToER.Service;

namespace SqlToER.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        // ============ 绑定属性 ============

        [ObservableProperty] private string _sqlInput = string.Empty;
        [ObservableProperty] private string _jsonPreview = string.Empty;
        [ObservableProperty] private string _statusText = "就绪";
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string? _lastExportPath;
        [ObservableProperty] private bool _canOpenFile;

        // ===== 模型选择器 =====
        public ObservableCollection<ModelDisplayItem> AvailableModels { get; } = [];

        [ObservableProperty] private ModelDisplayItem? _selectedModelItem;

        // ===== 参考模板 =====

        /// <summary>
        /// 已加载的参考模板路径（显示在 UI 上）
        /// </summary>
        [ObservableProperty] private string _templatePath = "(无)";

        /// <summary>
        /// 已解析的模板布局信息
        /// </summary>
        private TemplateLayout? _templateLayout;

        // ===== 测试模板 =====

        public string[] TestTemplateItems => TestTemplates.Names;

        [ObservableProperty] private int _selectedTestTemplateIndex;

        public bool CanExportTest => SelectedTestTemplateIndex > 0;

        partial void OnSelectedTestTemplateIndexChanged(int value)
        {
            OnPropertyChanged(nameof(CanExportTest));
        }

        // ============ 内部状态 ============

        private ErDocument? _currentErDoc;
        private readonly LlmService _llmService = new();

        // ===== 迭代优化 =====
        [ObservableProperty] private int _optimizeRound;
        [ObservableProperty] private string _optimizeRoundText = "";
        private ErDocument? _lastExportedErDoc;
        private Dictionary<string, (double X, double Y)>? _lastExportCoords; // 坐标缓存

        // ===== 优化轮次下拉框 =====
        public int[] OptimizeRoundOptions => [1, 2, 3, 5, 10];
        [ObservableProperty] private int _selectedOptimizeRoundIndex = 2; // 默认 3 轮

        public MainWindowViewModel()
        {
            LoadAvailableModels();
        }

        /// <summary>
        /// 导出核心逻辑（不管理 IsLoading，供多处复用）
        /// </summary>
        private async Task DoExportCoreAsync(ErDocument erDoc, string savePath)
        {
            var service = new VisioExportService();
            var tpl = _templateLayout;
            Dictionary<string, (double X, double Y)>? coords = null;

            await RunOnStaThreadAsync(() =>
                coords = service.ExportToVsdx(erDoc, savePath, tpl, s => UpdateStatus(s)));

            LastExportPath = savePath;
            CanOpenFile = true;
            _lastExportedErDoc = erDoc;
            _lastExportCoords = coords; // 缓存坐标供优化轮使用
            OptimizeRound = 0;
            OptimizeRoundText = "";
        }

        // ============ 测试模板导出 ============

        [RelayCommand]
        private async Task ExportTestTemplateAsync()
        {
            var erDoc = TestTemplates.Create(SelectedTestTemplateIndex);
            if (erDoc == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Visio 文件 (*.vsdx)|*.vsdx",
                DefaultExt = ".vsdx",
                FileName = $"Test_{SelectedTestTemplateIndex}"
            };
            if (dialog.ShowDialog() != true) return;

            IsLoading = true;
            try
            {
                var savePath = dialog.FileName;
                var service = new VisioExportService();
                var tpl = _templateLayout;

                await RunOnStaThreadAsync(() =>
                    service.ExportToVsdx(erDoc, savePath, tpl, s => UpdateStatus(s)));

                LastExportPath = savePath;
                CanOpenFile = true;
                _lastExportedErDoc = erDoc;
                OptimizeRound = 0;
                OptimizeRoundText = "";
                UpdateStatus($"✅ 测试模板导出成功：{savePath}");
            }
            catch (Exception ex) { UpdateStatus($"❌ 测试导出失败：{ex.Message}"); }
            finally { IsLoading = false; }
        }

        // ============ 命令 ============

        /// <summary>
        /// 导入参考模板
        /// </summary>
        [RelayCommand]
        private async Task ImportTemplateAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Visio 文件 (*.vsdx)|*.vsdx|所有文件 (*.*)|*.*",
                Title = "选择参考 ER 图模板"
            };

            if (dialog.ShowDialog() != true)
                return;

            IsLoading = true;
            UpdateStatus("正在解析参考模板...");

            try
            {
                var filePath = dialog.FileName;
                var service = new TemplateParserService();

                // 模板解析需要 Visio COM，在 STA 线程执行
                TemplateLayout? layout = null;
                await RunOnStaThreadAsync(() =>
                    layout = service.ParseTemplate(filePath, s => UpdateStatus(s)));

                _templateLayout = layout;
                TemplatePath = System.IO.Path.GetFileName(filePath);
                UpdateStatus($"✅ 模板已加载：{TemplatePath}");
            }
            catch (Exception ex)
            {
                _templateLayout = null;
                TemplatePath = "(无)";
                UpdateStatus($"❌ 模板解析失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 清除已加载的模板
        /// </summary>
        [RelayCommand]
        private void ClearTemplate()
        {
            _templateLayout = null;
            TemplatePath = "(无)";
            UpdateStatus("模板已清除，将使用默认布局");
        }

        [RelayCommand]
        private async Task ParseSqlAsync()
        {
            if (string.IsNullOrWhiteSpace(SqlInput))
            {
                UpdateStatus("⚠ 请先输入 SQL / DDL");
                return;
            }

            if (SelectedModelItem is null)
            {
                UpdateStatus("⚠ 请先选择 AI 模型");
                return;
            }

            IsLoading = true;
            CanOpenFile = false;
            var modelName = SelectedModelItem.DisplayName;
            var hasTemplate = _templateLayout != null;
            UpdateStatus($"正在使用 {modelName} 分析 SQL{(hasTemplate ? "（参考模板布局）" : "")}...");
            JsonPreview = string.Empty;
            _currentErDoc = null;

            try
            {
                // 如果有模板，生成布局提示词
                var layoutPrompt = _templateLayout?.ToLayoutPrompt();

                var (doc, rawJson) = await _llmService.ParseSqlToErJsonAsync(
                    SqlInput,
                    SelectedModelItem.Provider,
                    layoutPrompt,
                    status => UpdateStatus(status));

                _currentErDoc = doc;
                JsonPreview = rawJson;
                UpdateStatus($"✅ 使用 {modelName} 解析完成{(hasTemplate ? "（含布局坐标）" : "")}");

                // 自动弹出保存对话框
                var saveDlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Visio 文件 (*.vsdx)|*.vsdx",
                    DefaultExt = ".vsdx",
                    FileName = "ER_Diagram"
                };

                if (saveDlg.ShowDialog() == true)
                {
                    UpdateStatus("正在调用 Visio 引擎生成图形...");
                    await DoExportCoreAsync(doc, saveDlg.FileName);
                    UpdateStatus($"✅ 导出成功：{saveDlg.FileName}");
                }
                else
                {
                    UpdateStatus("✅ 解析完成（未导出，可稍后手动点击导出按钮）");
                }
            }
            catch (HttpRequestException ex)
            {
                UpdateStatus($"❌ 网络错误：{ex.Message}");
            }
            catch (JsonParseException ex)
            {
                UpdateStatus($"❌ JSON 解析失败：{ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                UpdateStatus($"❌ {ex.Message}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ 未知错误：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ExportVsdxAsync()
        {
            if (_currentErDoc is null)
            {
                UpdateStatus("⚠ 请先解析 SQL 获取 ER 结构");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Visio 文件 (*.vsdx)|*.vsdx",
                DefaultExt = ".vsdx",
                FileName = "ER_Diagram"
            };

            if (dialog.ShowDialog() != true)
                return;

            IsLoading = true;
            CanOpenFile = false;
            UpdateStatus("正在调用 Visio 引擎生成图形...");

            try
            {
                await DoExportCoreAsync(_currentErDoc, dialog.FileName);
                UpdateStatus($"✅ 导出成功：{dialog.FileName}");
            }
            catch (InvalidOperationException ex) { UpdateStatus($"❌ {ex.Message}"); }
            catch (COMException ex) { UpdateStatus($"❌ Visio 错误：{ex.Message}"); }
            catch (Exception ex) { UpdateStatus($"❌ 导出失败：{ex.Message}"); }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private async Task OptimizeLayoutAsync()
        {
            if (_lastExportedErDoc == null || string.IsNullOrEmpty(LastExportPath)) return;

            int totalRounds = OptimizeRoundOptions[SelectedOptimizeRoundIndex];
            IsLoading = true;
            try
            {
                var erDoc = _lastExportedErDoc;
                var tpl = _templateLayout;
                var path = LastExportPath;

                for (int i = 0; i < totalRounds; i++)
                {
                    OptimizeRound++;
                    var round = OptimizeRound;
                    UpdateStatus($"🔄 正在优化第 {round} 轮（共 {OptimizeRound - 1 + totalRounds - i} 轮）...");

                    var seed = _lastExportCoords; // 传入上轮坐标
                    Dictionary<string, (double X, double Y)>? newCoords = null;

                    await RunOnStaThreadAsync(() =>
                        newCoords = LayoutOptimizer.OptimizeVsdx(path, erDoc, tpl, round, s => UpdateStatus(s), seed));

                    _lastExportCoords = newCoords; // 缓存本轮坐标
                    OptimizeRoundText = $"已优化 {OptimizeRound} 轮";
                }

                UpdateStatus($"✅ {totalRounds} 轮优化全部完成（累计 {OptimizeRound} 轮）：{path}");
            }
            catch (Exception ex) { UpdateStatus($"❌ 优化失败（第 {OptimizeRound} 轮）：{ex.Message}"); }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private void OpenExportedFile()
        {
            if (string.IsNullOrEmpty(LastExportPath)) return;
            try { Process.Start(new ProcessStartInfo(LastExportPath) { UseShellExecute = true }); }
            catch (Exception ex) { UpdateStatus($"❌ 打开文件失败：{ex.Message}"); }
        }

        [RelayCommand]
        private void OpenAiConfig()
        {
            var window = new View.AiConfigWindow { Owner = Application.Current.MainWindow };
            window.ShowDialog();
            LoadAvailableModels();
        }

        [RelayCommand]
        private void RefreshModels() => LoadAvailableModels();

        // ============ 工具方法 ============

        private void LoadAvailableModels()
        {
            var config = AiConfigHelper.LoadConfig();
            var previousSelection = SelectedModelItem?.DisplayName;
            AvailableModels.Clear();

            foreach (var p in config.Providers)
            {
                if (string.IsNullOrWhiteSpace(p.BaseUrl) || string.IsNullOrWhiteSpace(p.ApiKey))
                    continue;

                var modelName = string.IsNullOrEmpty(p.SelectedModel)
                    ? p.Models.FirstOrDefault() ?? "(未选模型)"
                    : p.SelectedModel;

                AvailableModels.Add(new ModelDisplayItem(p.Name, modelName, p));
            }

            SelectedModelItem = AvailableModels.FirstOrDefault(m => m.DisplayName == previousSelection)
                                ?? AvailableModels.FirstOrDefault();

            if (AvailableModels.Count == 0)
                UpdateStatus("⚠ 无可用模型，请先配置 AI 提供商");
        }

        private void UpdateStatus(string text)
        {
            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher.CheckAccess()) StatusText = text;
            else dispatcher.Invoke(() => StatusText = text);
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource();
            var thread = new Thread(() =>
            {
                try { action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }
    }

    public class ModelDisplayItem(string providerName, string modelName, AiProviderConfig provider)
    {
        public string ProviderName { get; } = providerName;
        public string ModelName { get; } = modelName;
        public AiProviderConfig Provider { get; } = provider;
        public string DisplayName => $"{ProviderName} / {ModelName}";
        public override string ToString() => DisplayName;
    }
}
