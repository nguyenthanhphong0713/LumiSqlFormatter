using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LumiSqlFormatter.Models;
using LumiSqlFormatter.Services;
using LumiSqlFormatter.UI.ViewModels;

namespace LumiSqlFormatter.UI.Views
{
    public partial class SettingsView : UserControl
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        private HwndSource _hwndSource;

        public SettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
            SubscribeViewModel(DataContext as SettingsViewModel);
        }

        private HwndSource GetHwndSource()
        {
            if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
            {
                return _hwndSource;
            }

            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndSourceHook);
                _hwndSource.AddHook(HwndSourceHook);
            }
            return _hwndSource;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            GetHwndSource();
            UpdatePreviewHighlighter();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SettingsViewModel oldVm)
            {
                oldVm.SnippetAdded -= OnSnippetAdded;
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (e.NewValue is SettingsViewModel newVm)
            {
                SubscribeViewModel(newVm);
            }
        }

        private void SubscribeViewModel(SettingsViewModel vm)
        {
            if (vm != null)
            {
                vm.SnippetAdded -= OnSnippetAdded;
                vm.SnippetAdded += OnSnippetAdded;
                vm.PropertyChanged -= OnViewModelPropertyChanged;
                vm.PropertyChanged += OnViewModelPropertyChanged;
                UpdatePreviewHighlighter();
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.PreviewSql) ||
                e.PropertyName == nameof(SettingsViewModel.SnippetTheme) ||
                e.PropertyName == nameof(SettingsViewModel.EnableAutoCapitalize) ||
                e.PropertyName == nameof(SettingsViewModel.KeywordCasing) ||
                e.PropertyName == nameof(SettingsViewModel.DataTypeCasing) ||
                e.PropertyName == nameof(SettingsViewModel.FunctionCasing))
            {
                UpdatePreviewHighlighter();
            }
        }

        private void EnsureWin32Focus()
        {
            try
            {
                var src = GetHwndSource();
                if (src != null && src.Handle != IntPtr.Zero)
                {
                    if (GetFocus() != src.Handle)
                    {
                        SetFocus(src.Handle);
                    }
                }
            }
            catch
            {
            }
        }

        private void OnSnippetAdded()
        {
#pragma warning disable VSTHRD001, VSTHRD110
            Action focusAction = () =>
            {
                try
                {
                    if (SnippetsGrid != null && SnippetsGrid.SelectedItem != null)
                    {
                        SnippetsGrid.ScrollIntoView(SnippetsGrid.SelectedItem);
                    }

                    EnsureWin32Focus();

                    if (PrefixTextBox != null)
                    {
                        PrefixTextBox.Focus();
                        Keyboard.Focus(PrefixTextBox);
                        PrefixTextBox.SelectAll();
                    }
                }
                catch
                {
                }
            };

            // Dispatch at Input priority first, then ContextIdle to ensure focus stays after layout/rendering
            _ = Dispatcher.BeginInvoke(focusAction, DispatcherPriority.Input);
            _ = Dispatcher.BeginInvoke(focusAction, DispatcherPriority.ContextIdle);
#pragma warning restore VSTHRD001, VSTHRD110
        }

        public void OnUserControlPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            EnsureWin32Focus();
        }

        public void OnTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            EnsureWin32Focus();
            if (sender is TextBox textBox)
            {
                if (!textBox.IsKeyboardFocusWithin)
                {
                    textBox.Focus();
                    Keyboard.Focus(textBox);
                }
            }
        }

        public void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            EnsureWin32Focus();
        }

        private IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETDLGCODE = 0x0087;
            const uint DLGC_WANTARROWS = 0x0001;
            const uint DLGC_WANTTAB = 0x0002;
            const uint DLGC_WANTALLKEYS = 0x0004;
            const uint DLGC_HASSETSEL = 0x0008;
            const uint DLGC_WANTCHARS = 0x0080;

            if (msg == WM_GETDLGCODE)
            {
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused is TextBox tb)
                {
                    // For TextBoxes: request characters, selection, and arrow keys.
                    // CRITICAL: Do NOT return DLGC_WANTALLKEYS unconditionally!
                    // Returning DLGC_WANTALLKEYS causes IsDialogMessage to consume character
                    // keystrokes without calling TranslateMessage, suppressing WM_CHAR and breaking typing!
                    uint flags = DLGC_WANTCHARS | DLGC_HASSETSEL | DLGC_WANTARROWS;

                    if (tb.AcceptsTab)
                    {
                        flags |= DLGC_WANTTAB;
                    }

                    // Multiline textbox wants Return key so Enter doesn't trigger the dialog's default button
                    if (tb.AcceptsReturn && lParam != IntPtr.Zero)
                    {
                        try
                        {
                            // In Win32, lParam of WM_GETDLGCODE points to MSG struct when sent by IsDialogMessage
                            // Check if the current message key is VK_RETURN (0x0D)
                            int wParamOffset = IntPtr.Size == 8 ? 16 : IntPtr.Size + 4;
                            int vk = Marshal.ReadInt32(lParam, wParamOffset);
                            if (vk == 0x0D)
                            {
                                flags |= DLGC_WANTALLKEYS;
                            }
                        }
                        catch
                        {
                            flags |= DLGC_WANTALLKEYS;
                        }
                    }

                    handled = true;
                    return new IntPtr(flags);
                }
            }
            return IntPtr.Zero;
        }

        private void InsertCursor_Click(object sender, RoutedEventArgs e)
        {
            EnsureWin32Focus();
            if (TemplateTextBox != null)
            {
                int insertIndex = TemplateTextBox.SelectionStart;
                string text = TemplateTextBox.Text ?? string.Empty;
                if (insertIndex < 0) insertIndex = 0;
                if (insertIndex > text.Length) insertIndex = text.Length;

                const string cursorTag = "$CURSOR$";
                TemplateTextBox.SelectedText = cursorTag;
                TemplateTextBox.SelectionLength = 0;
                TemplateTextBox.SelectionStart = insertIndex + cursorTag.Length;

                TemplateTextBox.Focus();
                Keyboard.Focus(TemplateTextBox);
            }
        }

        private static readonly Regex SqlTokenRegex = new Regex(
            @"(?<comment>--[^\r\n]*)" +
            @"|(?<string>'([^']|'')*')" +
            @"|(?<bracket>\[[^\]]*\])" +
            @"|(?<number>\b\d+(\.\d+)?\b)" +
            @"|(?<word>[A-Za-z_@#][A-Za-z0-9_@#]*)" +
            @"|(?<other>[^\sA-Za-z0-9_@#]+)",
            RegexOptions.Compiled);

        public void UpdatePreviewHighlighter()
        {
            if (PreviewRichTextBox == null || !(DataContext is SettingsViewModel vm))
                return;

            bool isDark = vm.SnippetTheme == SnippetTheme.Dark;
            var doc = CreateSyntaxHighlightedDocument(vm.PreviewSql, isDark);
            PreviewRichTextBox.Document = doc;
        }

        private static FlowDocument CreateSyntaxHighlightedDocument(string sql, bool isDark)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent
            };

            var defaultBrush = new SolidColorBrush(isDark ? Color.FromRgb(212, 212, 212) : Color.FromRgb(30, 30, 30));
            var commentBrush = new SolidColorBrush(isDark ? Color.FromRgb(106, 153, 85) : Color.FromRgb(0, 128, 0));
            var stringBrush = new SolidColorBrush(isDark ? Color.FromRgb(206, 145, 120) : Color.FromRgb(163, 21, 21));
            var keywordBrush = new SolidColorBrush(isDark ? Color.FromRgb(86, 156, 214) : Color.FromRgb(0, 0, 255));
            var dataTypeBrush = new SolidColorBrush(isDark ? Color.FromRgb(78, 201, 176) : Color.FromRgb(38, 127, 153));
            var functionBrush = new SolidColorBrush(isDark ? Color.FromRgb(220, 220, 170) : Color.FromRgb(121, 94, 38));
            var numberBrush = new SolidColorBrush(isDark ? Color.FromRgb(181, 206, 168) : Color.FromRgb(9, 134, 88));
            var bracketBrush = new SolidColorBrush(isDark ? Color.FromRgb(220, 220, 220) : Color.FromRgb(40, 40, 40));

            defaultBrush.Freeze();
            commentBrush.Freeze();
            stringBrush.Freeze();
            keywordBrush.Freeze();
            dataTypeBrush.Freeze();
            functionBrush.Freeze();
            numberBrush.Freeze();
            bracketBrush.Freeze();

            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };

            if (string.IsNullOrEmpty(sql))
            {
                doc.Blocks.Add(paragraph);
                return doc;
            }

            string[] lines = sql.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("--"))
                {
                    paragraph.Inlines.Add(new Run(line) { Foreground = commentBrush });
                }
                else
                {
                    int lastIndex = 0;
                    var matches = SqlTokenRegex.Matches(line);
                    foreach (Match match in matches)
                    {
                        if (match.Index > lastIndex)
                        {
                            string ws = line.Substring(lastIndex, match.Index - lastIndex);
                            paragraph.Inlines.Add(new Run(ws) { Foreground = defaultBrush });
                        }

                        string value = match.Value;
                        if (match.Groups["comment"].Success)
                        {
                            paragraph.Inlines.Add(new Run(value) { Foreground = commentBrush });
                        }
                        else if (match.Groups["string"].Success)
                        {
                            paragraph.Inlines.Add(new Run(value) { Foreground = stringBrush });
                        }
                        else if (match.Groups["bracket"].Success)
                        {
                            paragraph.Inlines.Add(new Run(value) { Foreground = bracketBrush });
                        }
                        else if (match.Groups["number"].Success)
                        {
                            paragraph.Inlines.Add(new Run(value) { Foreground = numberBrush });
                        }
                        else if (match.Groups["word"].Success)
                        {
                            if (SqlTokenParsingService.Instance.TryGetWordInfo(value, out var cat, out _))
                            {
                                switch (cat)
                                {
                                    case SqlWordCategory.ReservedKeyword:
                                        paragraph.Inlines.Add(new Run(value) { Foreground = keywordBrush, FontWeight = FontWeights.SemiBold });
                                        break;
                                    case SqlWordCategory.DataType:
                                        paragraph.Inlines.Add(new Run(value) { Foreground = dataTypeBrush });
                                        break;
                                    case SqlWordCategory.BuiltInFunction:
                                        paragraph.Inlines.Add(new Run(value) { Foreground = functionBrush });
                                        break;
                                    default:
                                        paragraph.Inlines.Add(new Run(value) { Foreground = defaultBrush });
                                        break;
                                }
                            }
                            else
                            {
                                paragraph.Inlines.Add(new Run(value) { Foreground = defaultBrush });
                            }
                        }
                        else
                        {
                            paragraph.Inlines.Add(new Run(value) { Foreground = defaultBrush });
                        }

                        lastIndex = match.Index + match.Length;
                    }

                    if (lastIndex < line.Length)
                    {
                        string trailing = line.Substring(lastIndex);
                        paragraph.Inlines.Add(new Run(trailing) { Foreground = defaultBrush });
                    }
                }

                if (i < lines.Length - 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                }
            }

            doc.Blocks.Add(paragraph);
            return doc;
        }

        private void OnShortcutTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ignore lone modifier keys
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)))
            {
                return;
            }

            var textBox = sender as TextBox;
            if (textBox == null)
                return;

            e.Handled = true;

            // Backspace or Delete to clear
            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                textBox.Text = string.Empty;
                var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                return;
            }

            Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys modifiers = Keyboard.Modifiers;

            string formatted = ShortcutHelper.FormatShortcut(modifiers, actualKey);
            if (!string.IsNullOrEmpty(formatted))
            {
                textBox.Text = formatted;
                var binding = textBox.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
            }
        }
    }
}
