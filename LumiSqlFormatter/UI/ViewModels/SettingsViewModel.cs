using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms; // for OpenFileDialog/SaveFileDialog/FolderBrowserDialog
using System.Windows.Input;
using LumiSqlFormatter.Models;
using LumiSqlFormatter.Services;
using Microsoft.VisualStudio.Shell;

namespace LumiSqlFormatter.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private readonly SnippetService _snippetService;
        private readonly SqlTokenParsingService _parsingService;

        private bool _enableAutoCapitalize;
        private CasingStyle _keywordCasing;
        private CasingStyle _dataTypeCasing;
        private CasingStyle _functionCasing;
        private bool _enableSnippets;
        private bool _enableSnippetSuggestions;
        private string _snippetsDirectoryPath;
        private string _previewSql;
        private string _statusMessage;
        private string _currentDatabaseInfo;
        private string _cacheStatusText;

        private SnippetItem _selectedSnippet;
        private string _editingShortcut;
        private string _editingDescription;
        private string _editingTemplate;

        public bool EnableAutoCapitalize
        {
            get => _enableAutoCapitalize;
            set
            {
                if (SetProperty(ref _enableAutoCapitalize, value))
                {
                    UpdatePreviewSql();
                }
            }
        }

        public CasingStyle KeywordCasing
        {
            get => _keywordCasing;
            set
            {
                if (SetProperty(ref _keywordCasing, value))
                {
                    UpdatePreviewSql();
                }
            }
        }

        public CasingStyle DataTypeCasing
        {
            get => _dataTypeCasing;
            set
            {
                if (SetProperty(ref _dataTypeCasing, value))
                {
                    UpdatePreviewSql();
                }
            }
        }

        public CasingStyle FunctionCasing
        {
            get => _functionCasing;
            set
            {
                if (SetProperty(ref _functionCasing, value))
                {
                    UpdatePreviewSql();
                }
            }
        }

        public bool EnableSnippets
        {
            get => _enableSnippets;
            set => SetProperty(ref _enableSnippets, value);
        }

        public bool EnableSnippetSuggestions
        {
            get => _enableSnippetSuggestions;
            set => SetProperty(ref _enableSnippetSuggestions, value);
        }

        private SnippetTheme _snippetTheme = SnippetTheme.Dark;

        public string SnippetsDirectoryPath
        {
            get => _snippetsDirectoryPath;
            set => SetProperty(ref _snippetsDirectoryPath, value);
        }

        private string _formatUppercaseShortcut = "Ctrl+Alt+U";
        public string FormatUppercaseShortcut
        {
            get => _formatUppercaseShortcut;
            set => SetProperty(ref _formatUppercaseShortcut, value);
        }

        private string _formatLowercaseShortcut = "Ctrl+Alt+L";
        public string FormatLowercaseShortcut
        {
            get => _formatLowercaseShortcut;
            set => SetProperty(ref _formatLowercaseShortcut, value);
        }

        private string _refreshDbCacheShortcut = "Ctrl+Shift+R";
        public string RefreshDbCacheShortcut
        {
            get => _refreshDbCacheShortcut;
            set => SetProperty(ref _refreshDbCacheShortcut, value);
        }

        public SnippetTheme SnippetTheme
        {
            get => _snippetTheme;
            set
            {
                if (SetProperty(ref _snippetTheme, value))
                {
                    OnPropertyChanged(nameof(SnippetEditorBackground));
                    OnPropertyChanged(nameof(SnippetEditorForeground));
                    OnPropertyChanged(nameof(PreviewBackground));
                    OnPropertyChanged(nameof(PreviewForeground));
                    OnPropertyChanged(nameof(PreviewBorderBrush));
                }
            }
        }

        public IEnumerable<SnippetTheme> SnippetThemeOptions => Enum.GetValues(typeof(SnippetTheme)).Cast<SnippetTheme>();

        public string SnippetEditorBackground => SnippetTheme == SnippetTheme.Light ? "#FFFFFF" : "#1E1E1E";
        public string SnippetEditorForeground => SnippetTheme == SnippetTheme.Light ? "#111111" : "#D4D4D4";

        public string PreviewBackground => SnippetTheme == SnippetTheme.Light ? "#FFFFFF" : "#1E1E1E";
        public string PreviewForeground => SnippetTheme == SnippetTheme.Light ? "#111111" : "#D4D4D4";
        public string PreviewBorderBrush => SnippetTheme == SnippetTheme.Light ? "#CCCCCC" : "#3F3F46";

        public string PreviewSql
        {
            get => _previewSql;
            set => SetProperty(ref _previewSql, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<SnippetItem> Snippets { get; } = new ObservableCollection<SnippetItem>();

        public event Action SnippetAdded;
        private string _originalShortcut;

        public SnippetItem SelectedSnippet
        {
            get => _selectedSnippet;
            set
            {
                if (SetProperty(ref _selectedSnippet, value))
                {
                    if (value != null)
                    {
                        _originalShortcut = value.Shortcut;
                        EditingShortcut = value.Shortcut;
                        EditingDescription = value.Description;
                        EditingTemplate = value.Template;
                    }
                    else
                    {
                        _originalShortcut = null;
                        EditingShortcut = string.Empty;
                        EditingDescription = string.Empty;
                        EditingTemplate = string.Empty;
                    }
                }
            }
        }

        public string EditingShortcut
        {
            get => _editingShortcut;
            set
            {
                if (SetProperty(ref _editingShortcut, value))
                {
                    OnPropertyChanged(nameof(EditingPrefix));
                    if (SelectedSnippet != null)
                    {
                        SelectedSnippet.Prefix = value ?? string.Empty;
                    }
                }
            }
        }

        public string EditingPrefix
        {
            get => EditingShortcut;
            set => EditingShortcut = value;
        }

        public string EditingDescription
        {
            get => _editingDescription;
            set
            {
                if (SetProperty(ref _editingDescription, value))
                {
                    if (SelectedSnippet != null)
                    {
                        SelectedSnippet.Description = value ?? string.Empty;
                    }
                }
            }
        }

        public string EditingTemplate
        {
            get => _editingTemplate;
            set
            {
                if (SetProperty(ref _editingTemplate, value))
                {
                    OnPropertyChanged(nameof(EditingBody));
                    if (SelectedSnippet != null)
                    {
                        SelectedSnippet.Body = value ?? string.Empty;
                    }
                }
            }
        }

        public string EditingBody
        {
            get => EditingTemplate;
            set => EditingTemplate = value;
        }

        public string CurrentDatabaseInfo
        {
            get => _currentDatabaseInfo;
            set => SetProperty(ref _currentDatabaseInfo, value);
        }

        public string CacheStatusText
        {
            get => _cacheStatusText;
            set => SetProperty(ref _cacheStatusText, value);
        }

        public IEnumerable<CasingStyle> CasingStyleOptions => Enum.GetValues(typeof(CasingStyle)).Cast<CasingStyle>();

        public ICommand SaveSettingsCommand { get; }
        public ICommand AddNewSnippetCommand { get; }
        public ICommand SaveCurrentSnippetCommand { get; }
        public ICommand DeleteSnippetCommand { get; }
        public ICommand InsertCursorCommand { get; }
        public ICommand ImportSnippetsCommand { get; }
        public ICommand ExportSnippetsCommand { get; }
        public ICommand BrowseSnippetsFolderCommand { get; }
        public ICommand OpenSnippetsFolderCommand { get; }
        public ICommand ReloadSnippetsCommand { get; }
        public ICommand RefreshDbCacheCommand { get; }
        public ICommand UninstallExtensionCommand { get; }

        public SettingsViewModel()
        {
            _settingsService = SettingsService.Instance;
            _snippetService = SnippetService.Instance;
            _parsingService = SqlTokenParsingService.Instance;

            // Load initial settings
            var currentSettings = _settingsService.Settings;
            _enableAutoCapitalize = currentSettings.EnableAutoCapitalize;
            _keywordCasing = currentSettings.KeywordCasing;
            _dataTypeCasing = currentSettings.DataTypeCasing;
            _functionCasing = currentSettings.FunctionCasing;
            _enableSnippets = currentSettings.EnableSnippets;
            _enableSnippetSuggestions = currentSettings.EnableSnippetSuggestions;
            _snippetsDirectoryPath = _settingsService.GetSnippetsDirectory();
            _snippetTheme = currentSettings.SnippetTheme;
            _formatUppercaseShortcut = !string.IsNullOrWhiteSpace(currentSettings.FormatUppercaseShortcut) ? currentSettings.FormatUppercaseShortcut : "Ctrl+Alt+U";
            _formatLowercaseShortcut = !string.IsNullOrWhiteSpace(currentSettings.FormatLowercaseShortcut) ? currentSettings.FormatLowercaseShortcut : "Ctrl+Alt+L";
            _refreshDbCacheShortcut = !string.IsNullOrWhiteSpace(currentSettings.RefreshDbCacheShortcut) ? currentSettings.RefreshDbCacheShortcut : "Ctrl+Shift+R";

            // Initialize commands
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            AddNewSnippetCommand = new RelayCommand(AddNewSnippet);
            SaveCurrentSnippetCommand = new RelayCommand(SaveCurrentSnippet);
            DeleteSnippetCommand = new RelayCommand(DeleteSnippet);
            InsertCursorCommand = new RelayCommand(InsertCursor);
            ImportSnippetsCommand = new RelayCommand(ImportSnippets);
            ExportSnippetsCommand = new RelayCommand(ExportSnippets);
            BrowseSnippetsFolderCommand = new RelayCommand(BrowseSnippetsFolder);
            OpenSnippetsFolderCommand = new RelayCommand(OpenSnippetsFolder);
            ReloadSnippetsCommand = new RelayCommand(ReloadSnippets);
            RefreshDbCacheCommand = new RelayCommand(ExecuteRefreshDbCache);
            UninstallExtensionCommand = new RelayCommand(UninstallExtension);

            DatabaseMetadataCacheService.Instance.CacheUpdated += (s, e) =>
            {
                _ = Task.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UpdateDatabaseCacheInfo();
                });
            };

            UpdateDatabaseCacheInfo();
            ReloadSnippetsList();
            UpdatePreviewSql();

            if (Snippets.Count > 0)
            {
                SelectedSnippet = Snippets[0];
            }
        }

        public void ReloadSnippetsList()
        {
            Snippets.Clear();
            foreach (var item in _snippetService.GetAllSnippets())
            {
                Snippets.Add(item.Clone());
            }

            if (Snippets.Count == 0)
            {
                SelectedSnippet = null;
                EditingShortcut = string.Empty;
                EditingDescription = string.Empty;
                EditingTemplate = string.Empty;
            }
        }

        public void SaveSettings()
        {
            var settings = new FormatterSettings
            {
                EnableAutoCapitalize = EnableAutoCapitalize,
                KeywordCasing = KeywordCasing,
                DataTypeCasing = DataTypeCasing,
                FunctionCasing = FunctionCasing,
                EnableSnippets = EnableSnippets,
                EnableSnippetSuggestions = EnableSnippetSuggestions,
                SnippetsDirectoryPath = SnippetsDirectoryPath,
                SnippetTheme = SnippetTheme,
                FormatUppercaseShortcut = FormatUppercaseShortcut,
                FormatLowercaseShortcut = FormatLowercaseShortcut,
                RefreshDbCacheShortcut = RefreshDbCacheShortcut
            };

            _settingsService.SaveSettings(settings);
            SetStatus("Settings saved successfully!");
        }

        private void UninstallExtension()
        {
            var confirm = MessageBox.Show(
                "Bạn có chắc chắn muốn gỡ cài đặt Lumi SQL Formatter và các extension liên quan khỏi SSMS không?\n\n" +
                "Lưu ý: SSMS sẽ tự động đóng lại để xóa toàn bộ file thư viện DLL, thư mục extension và dọn sạch ComponentModelCache.",
                "Xác nhận gỡ cài đặt Extension",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            var deleteData = MessageBox.Show(
                "Bạn có muốn xóa luôn thư mục Snippets và cấu hình cá nhân (%APPDATA%\\LumiSqlFormatter) không?\n\n" +
                "• Chọn 'Yes' (Có): Xóa sạch toàn bộ cấu hình và snippets.\n" +
                "• Chọn 'No' (Không): Giữ lại snippets cá nhân để tái sử dụng sau này.",
                "Tùy chọn dữ liệu cá nhân",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            bool removeAppData = (deleteData == DialogResult.Yes);

            try
            {
                string tempPsPath = Path.Combine(Path.GetTempPath(), "LumiSql_Uninstall_" + Guid.NewGuid().ToString("N") + ".ps1");
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LumiSqlFormatter");

                var sb = new StringBuilder();
                sb.AppendLine("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
                sb.AppendLine("Write-Host '============================================================' -ForegroundColor Cyan");
                sb.AppendLine("Write-Host '         GO CAI DAT HOAN TOAN EXTENSION KHOI SSMS           ' -ForegroundColor Cyan");
                sb.AppendLine("Write-Host '============================================================' -ForegroundColor Cyan");
                sb.AppendLine("Write-Host '[1/4] Dang dong tien trinh SSMS de mo khoa file DLL...' -ForegroundColor Yellow");
                sb.AppendLine("Get-Process -Name 'Ssms' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("Start-Sleep -Seconds 2");
                sb.AppendLine();
                sb.AppendLine("Write-Host '[2/4] Dang quet va xoa thu muc extension tren toan bo he thong...' -ForegroundColor Yellow");
                sb.AppendLine("$roots = @(");
                sb.AppendLine("    \"$env:LOCALAPPDATA\\Microsoft\\SSMS\",");
                sb.AppendLine("    \"$env:LOCALAPPDATA\\Microsoft\\SQL Server Management Studio\",");
                sb.AppendLine("    \"$env:LOCALAPPDATA\\Microsoft\\VisualStudio\",");
                sb.AppendLine("    \"${env:ProgramFiles}\\Microsoft SQL Server Management Studio 18\",");
                sb.AppendLine("    \"${env:ProgramFiles}\\Microsoft SQL Server Management Studio 19\",");
                sb.AppendLine("    \"${env:ProgramFiles}\\Microsoft SQL Server Management Studio 20\",");
                sb.AppendLine("    \"${env:ProgramFiles}\\Microsoft SQL Server Management Studio 22\"");
                sb.AppendLine(")");
                sb.AppendLine("$targetDlls = @('LumiSqlFormatter.dll', 'SqlAutoUppercase.dll', 'SqlKeywordUpper.dll')");
                sb.AppendLine("foreach ($r in $roots) {");
                sb.AppendLine("    if (Test-Path $r) {");
                sb.AppendLine("        foreach ($d in $targetDlls) {");
                sb.AppendLine("            $found = Get-ChildItem -Path $r -Recurse -Filter $d -ErrorAction SilentlyContinue");
                sb.AppendLine("            foreach ($f in $found) {");
                sb.AppendLine("                if ($f.DirectoryName -like '*\\LumiSqlFormatter\\bin\\*' -or $f.DirectoryName -like '*\\LumiSqlFormatter\\obj\\*') { continue }");
                sb.AppendLine("                Write-Host \"  -> Xoa: $($f.DirectoryName)\" -ForegroundColor Green");
                sb.AppendLine("                Remove-Item -Path $f.DirectoryName -Recurse -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("Write-Host '[3/4] Lam moi ComponentModelCache va metadata cache cua SSMS...' -ForegroundColor Yellow");
                sb.AppendLine("if (Test-Path \"$env:LOCALAPPDATA\\Microsoft\\SSMS\") {");
                sb.AppendLine("    Get-ChildItem -Path \"$env:LOCALAPPDATA\\Microsoft\\SSMS\" -Directory -Filter '22.*' -ErrorAction SilentlyContinue | ForEach-Object {");
                sb.AppendLine("        $cmc = Join-Path $_.FullName 'ComponentModelCache'");
                sb.AppendLine("        if (Test-Path $cmc) { Remove-Item $cmc -Recurse -Force -ErrorAction SilentlyContinue }");
                sb.AppendLine("        $ext = Join-Path $_.FullName 'Extensions'");
                sb.AppendLine("        if (Test-Path $ext) {");
                sb.AppendLine("            Get-ChildItem -Path $ext -Include '*.mpack', '*.cache', 'CompatibilityList.xml' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("            Set-Content -Path (Join-Path $ext 'extensions.configurationchanged') -Value '' -ErrorAction SilentlyContinue");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                sb.AppendLine("if (Test-Path \"$env:LOCALAPPDATA\\Microsoft\\SQL Server Management Studio\") {");
                sb.AppendLine("    Get-ChildItem -Path \"$env:LOCALAPPDATA\\Microsoft\\SQL Server Management Studio\" -Directory -Filter '*IsoShell' -ErrorAction SilentlyContinue | ForEach-Object {");
                sb.AppendLine("        $cmc = Join-Path $_.FullName 'ComponentModelCache'");
                sb.AppendLine("        if (Test-Path $cmc) { Remove-Item $cmc -Recurse -Force -ErrorAction SilentlyContinue }");
                sb.AppendLine("        $ext = Join-Path $_.FullName 'Extensions'");
                sb.AppendLine("        if (Test-Path $ext) {");
                sb.AppendLine("            Get-ChildItem -Path $ext -Include '*.cache' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("            Set-Content -Path (Join-Path $ext 'extensions.configurationchanged') -Value '' -ErrorAction SilentlyContinue");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("Write-Host '[4/4] Xu ly thu muc du lieu...' -ForegroundColor Yellow");
                if (removeAppData)
                {
                    sb.AppendLine($"if (Test-Path \"{appDataDir}\") {{ Remove-Item -Path \"{appDataDir}\" -Recurse -Force -ErrorAction SilentlyContinue; Write-Host '  -> Da xoa du lieu AppData.' -ForegroundColor Green }}");
                }
                else
                {
                    sb.AppendLine($"Write-Host '  -> Giu lai thu muc Snippets tai: {appDataDir}' -ForegroundColor Cyan");
                }
                sb.AppendLine();
                sb.AppendLine("Write-Host '============================================================' -ForegroundColor Green");
                sb.AppendLine("Write-Host '[THANH CONG] Da go bo 100% sach se toan bo extension khoi SSMS!' -ForegroundColor Green");
                sb.AppendLine("Write-Host '============================================================' -ForegroundColor Green");
                sb.AppendLine("Write-Host 'Nhan phim bat ky de thoat...'");
                sb.AppendLine("[void][System.Console]::ReadKey()");
                sb.AppendLine("Remove-Item -Path $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue");

                File.WriteAllText(tempPsPath, sb.ToString(), new UTF8Encoding(true));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempPsPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi khởi chạy trình gỡ cài đặt: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BrowseSnippetsFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Chọn thư mục lưu trữ Snippets:";
                dlg.SelectedPath = SnippetsDirectoryPath;
                dlg.ShowNewFolderButton = true;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    SnippetsDirectoryPath = dlg.SelectedPath;
                    _settingsService.SetSnippetsDirectory(dlg.SelectedPath);
                    ReloadSnippetsList();
                    SelectedSnippet = Snippets.FirstOrDefault();
                    SetStatus($"Đã chuyển thư mục snippet sang: {dlg.SelectedPath}");
                }
            }
        }

        private void OpenSnippetsFolder()
        {
            try
            {
                string dir = _settingsService.GetSnippetsDirectory();
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Lỗi mở thư mục: {ex.Message}");
            }
        }

        private void ReloadSnippets()
        {
            _snippetService.ReloadSnippets();
            ReloadSnippetsList();
            SelectedSnippet = Snippets.FirstOrDefault();
            SetStatus($"Đã refresh {Snippets.Count} snippets từ thư mục: {_settingsService.GetSnippetsDirectory()}");
        }

        private void AddNewSnippet()
        {
            var newSnippet = new SnippetItem("new_prefix", "Snippet Description", "SELECT * FROM $TABLE_NAME$ WHERE $CURSOR$;", Guid.NewGuid().ToString());
            Snippets.Add(newSnippet);
            SelectedSnippet = newSnippet;
            SetStatus("Đã tạo mẫu snippet mới. Vui lòng chỉnh sửa và bấm Lưu.");
            SnippetAdded?.Invoke();
        }

        private void SaveCurrentSnippet()
        {
            if (string.IsNullOrWhiteSpace(EditingShortcut))
            {
                SetStatus("Error: Shortcut cannot be empty.");
                return;
            }

            if (SelectedSnippet != null)
            {
                string originalShortcut = !string.IsNullOrWhiteSpace(_originalShortcut) ? _originalShortcut : SelectedSnippet.Shortcut;
                SelectedSnippet.Shortcut = EditingShortcut.Trim();
                SelectedSnippet.Description = EditingDescription ?? string.Empty;
                SelectedSnippet.Template = EditingTemplate ?? string.Empty;

                _snippetService.UpdateSnippet(originalShortcut, SelectedSnippet);
                _originalShortcut = SelectedSnippet.Shortcut;
                ReloadSnippetsList();
                SelectedSnippet = Snippets.FirstOrDefault(s => s.Shortcut == EditingShortcut.Trim());
                SetStatus($"Snippet '{EditingShortcut}' saved!");
            }
        }

        private void DeleteSnippet()
        {
            if (SelectedSnippet != null)
            {
                string shortcut = SelectedSnippet.Shortcut;
                _snippetService.DeleteSnippet(shortcut);
                ReloadSnippetsList();
                SelectedSnippet = Snippets.FirstOrDefault();
                SetStatus($"Snippet '{shortcut}' deleted.");
            }
        }

        private void InsertCursor()
        {
            EditingTemplate = (EditingTemplate ?? string.Empty) + "$CURSOR$";
        }

        private void ImportSnippets()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dlg.Title = "Import Snippets from JSON";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _snippetService.ImportFromFile(dlg.FileName);
                    ReloadSnippetsList();
                    SelectedSnippet = Snippets.FirstOrDefault();
                    SetStatus("Snippets imported successfully!");
                }
            }
        }

        private void ExportSnippets()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dlg.FileName = "LumiSqlSnippets.json";
                dlg.Title = "Export Snippets to JSON";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _snippetService.ExportToFile(dlg.FileName);
                    SetStatus("Snippets exported successfully!");
                }
            }
        }

        private void SetStatus(string message)
        {
            StatusMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }

        public void UpdatePreviewSql()
        {
            string kw(string s) => FormatSampleWord(s, SqlWordCategory.ReservedKeyword);
            string dt(string s) => FormatSampleWord(s, SqlWordCategory.DataType);
            string fn(string s) => FormatSampleWord(s, SqlWordCategory.BuiltInFunction);

            PreviewSql =
$@"-- Live Preview (Formatting & Casing)
-- Notice strings, comments, and [bracketed identifiers] remain untouched:

{kw("SELECT")} 
    c.[Customer_ID],
    c.CustomerName {kw("AS")} [{dt("VARCHAR")}_Name],
    {fn("ISNULL")}(c.Age, 0) {kw("AS")} AgeValue,
    {fn("COUNT")}(o.OrderID) {kw("AS")} TotalOrders,
    {dt("DATETIME2")} {kw("AS")} OrderType
{kw("FROM")} dbo.Customers c
{kw("INNER JOIN")} dbo.Orders o {kw("ON")} c.CustomerID = o.CustomerID
{kw("WHERE")} c.Status = 'Active' 
  {kw("AND")} o.CreatedDate >= {fn("GETDATE")}()
{kw("GROUP BY")} c.[Customer_ID], c.CustomerName, c.Age
{kw("ORDER BY")} TotalOrders {kw("DESC")};";
        }

        private string FormatSampleWord(string word, SqlWordCategory category)
        {
            if (!EnableAutoCapitalize)
                return word;

            CasingStyle style = CasingStyle.Uppercase;
            switch (category)
            {
                case SqlWordCategory.ReservedKeyword:
                    style = KeywordCasing;
                    break;
                case SqlWordCategory.DataType:
                    style = DataTypeCasing;
                    break;
                case SqlWordCategory.BuiltInFunction:
                    style = FunctionCasing;
                    break;
            }

            _parsingService.TryGetWordInfo(word, out _, out string pascalCase);
            return _parsingService.FormatWord(word, style, pascalCase);
        }

        private void ExecuteRefreshDbCache()
        {
            _ = Task.Run(async () =>
            {
                await DatabaseMetadataCacheService.Instance.RefreshCacheAsync(true);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateDatabaseCacheInfo();
            });
        }

        public void UpdateDatabaseCacheInfo()
        {
            var cache = DatabaseMetadataCacheService.Instance;
            if (!string.IsNullOrEmpty(cache.CurrentDatabase))
            {
                CurrentDatabaseInfo = $"[{cache.CurrentDatabase}] {(string.IsNullOrEmpty(cache.CurrentServer) ? "" : $"({cache.CurrentServer})")}";
            }
            else
            {
                CurrentDatabaseInfo = "[Chưa có database active]";
            }

            if (cache.CachedCount > 0)
            {
                string timeStr = cache.LastRefreshedTime.HasValue ? cache.LastRefreshedTime.Value.ToString("HH:mm:ss") : "";
                CacheStatusText = $"Đã nạp {cache.CachedCount} thủ tục & hàm {(string.IsNullOrEmpty(timeStr) ? "" : $"({timeStr})")}";
            }
            else
            {
                CacheStatusText = "Chưa nạp hoặc database rỗng";
            }
        }
    }
}
