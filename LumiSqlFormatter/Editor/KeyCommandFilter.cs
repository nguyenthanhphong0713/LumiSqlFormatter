using System;
using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LumiSqlFormatter.Models;
using LumiSqlFormatter.Services;
using LumiSqlFormatter.UI.Views;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace LumiSqlFormatter.Editor
{
    public class KeyCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView _textView;
        private readonly IVsTextView _vsTextView;
        private readonly SettingsService _settingsService;
        private readonly SqlTokenParsingService _parsingService;
        private readonly SnippetService _snippetService;

        private readonly Popup _suggestionPopup;
        private readonly SnippetSuggestionPopup _popupContent;

        public static KeyCommandFilter CurrentActiveFilter { get; private set; }
        public static IWpfTextView CurrentActiveTextView => CurrentActiveFilter?._textView;

        internal IOleCommandTarget NextCommandHandler { get; set; }

        public KeyCommandFilter(IWpfTextView textView, IVsTextView vsTextView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _vsTextView = vsTextView ?? throw new ArgumentNullException(nameof(vsTextView));
            _settingsService = SettingsService.Instance;
            _parsingService = SqlTokenParsingService.Instance;
            _snippetService = SnippetService.Instance;

            CurrentActiveFilter = this;
            _textView.GotAggregateFocus += (s, e) => CurrentActiveFilter = this;
            _textView.VisualElement.PreviewKeyDown += OnTextViewPreviewKeyDown;
            _textView.Closed += OnTextViewClosed;

            // Initialize Suggestion Popup
            _popupContent = new SnippetSuggestionPopup();
            _popupContent.SnippetSelected += OnSnippetSelectedFromPopup;

            _suggestionPopup = new Popup
            {
                Child = _popupContent,
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = PlacementMode.Relative
            };
        }

        public static void FormatTextView(IWpfTextView textView, BatchFormatMode mode)
        {
            if (textView == null)
                return;

            try
            {
                var selection = textView.Selection;
                if (selection != null && !selection.IsEmpty && selection.SelectedSpans.Count > 0)
                {
                    var span = selection.StreamSelectionSpan.SnapshotSpan;
                    string originalText = span.GetText();
                    string formattedText = SqlTokenParsingService.Instance.FormatBatch(originalText, mode);

                    if (!string.Equals(originalText, formattedText, StringComparison.Ordinal))
                    {
                        using (var edit = textView.TextBuffer.CreateEdit())
                        {
                            edit.Replace(span, formattedText);
                            edit.Apply();
                        }
                    }
                }
                else
                {
                    // No selection: format the whole document
                    var snapshot = textView.TextBuffer.CurrentSnapshot;
                    string originalText = snapshot.GetText();
                    string formattedText = SqlTokenParsingService.Instance.FormatBatch(originalText, mode);

                    if (!string.Equals(originalText, formattedText, StringComparison.Ordinal))
                    {
                        using (var edit = textView.TextBuffer.CreateEdit())
                        {
                            edit.Replace(new Span(0, snapshot.Length), formattedText);
                            edit.Apply();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently ignore editor formatting errors
            }
        }

        private void OnTextViewPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Check for Format Uppercase shortcut
                if (ShortcutHelper.Matches(e, _settingsService.Settings.FormatUppercaseShortcut))
                {
                    e.Handled = true;
                    FormatTextView(_textView, BatchFormatMode.UppercaseKeywords);
                    return;
                }

                // Check for Format Lowercase shortcut
                if (ShortcutHelper.Matches(e, _settingsService.Settings.FormatLowercaseShortcut))
                {
                    e.Handled = true;
                    FormatTextView(_textView, BatchFormatMode.LowercaseAllExceptPreserved);
                    return;
                }

                // Check for Refresh DB Cache shortcut
                if (ShortcutHelper.Matches(e, _settingsService.Settings.RefreshDbCacheShortcut))
                {
                    e.Handled = true;
                    _ = DatabaseMetadataCacheService.Instance.RefreshCacheAsync(true);
                    return;
                }

                // Check for Ctrl+Alt+S shortcut to open Quick Snippet Creator
                Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                if (actualKey == Key.S && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
                {
                    e.Handled = true;
                    OpenQuickSnippetCreator();
                    return;
                }
            }
            catch (Exception)
            {
                // Silently ignore
            }
        }

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            try
            {
                if (_textView != null && _textView.VisualElement != null)
                {
                    _textView.VisualElement.PreviewKeyDown -= OnTextViewPreviewKeyDown;
                    _textView.Closed -= OnTextViewClosed;
                }
            }
            catch (Exception)
            {
            }
        }

        private static void SetCmdText(IntPtr pCmdText, string text)
        {
            if (pCmdText == IntPtr.Zero || string.IsNullOrEmpty(text))
                return;

            try
            {
                uint cmdtextf = (uint)Marshal.ReadInt32(pCmdText, 0);
                if (cmdtextf == 0 || (cmdtextf & 1) != 0)
                {
                    int cwBuf = Marshal.ReadInt32(pCmdText, 8);
                    if (cwBuf > 1)
                    {
                        int copyLen = Math.Min(text.Length, cwBuf - 1);
                        char[] chars = new char[copyLen + 1];
                        text.CopyTo(0, chars, 0, copyLen);
                        chars[copyLen] = '\0';

                        IntPtr pStr = new IntPtr(pCmdText.ToInt64() + 12);
                        Marshal.Copy(chars, 0, pStr, chars.Length);
                        Marshal.WriteInt32(pCmdText, 4, copyLen);
                    }
                }
            }
            catch
            {
                // Ignore any native memory writing errors
            }
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == GuidList.guidLumiSqlCmdSet)
            {
                if (prgCmds != null && cCmds > 0)
                {
                    uint cmdId = prgCmds[0].cmdID;
                    if (cmdId == PkgCmdIDList.cmdidFormatUppercase || 
                        cmdId == PkgCmdIDList.cmdidFormatLowercase ||
                        cmdId == PkgCmdIDList.cmdidRefreshDbCache)
                    {
                        prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);

                        if (pCmdText != IntPtr.Zero)
                        {
                            var settings = _settingsService?.Settings ?? SettingsService.Instance.Settings;
                            string text = null;
                            if (cmdId == PkgCmdIDList.cmdidFormatUppercase)
                            {
                                string sc = !string.IsNullOrWhiteSpace(settings?.FormatUppercaseShortcut) ? $" ({settings.FormatUppercaseShortcut})" : "";
                                text = $"Lumi Format Uppercase{sc}";
                            }
                            else if (cmdId == PkgCmdIDList.cmdidFormatLowercase)
                            {
                                string sc = !string.IsNullOrWhiteSpace(settings?.FormatLowercaseShortcut) ? $" ({settings.FormatLowercaseShortcut})" : "";
                                text = $"Lumi Format Lowercase{sc}";
                            }
                            else if (cmdId == PkgCmdIDList.cmdidRefreshDbCache)
                            {
                                string sc = !string.IsNullOrWhiteSpace(settings?.RefreshDbCacheShortcut) ? $" ({settings.RefreshDbCacheShortcut})" : "";
                                text = $"Lumi: Refresh Database Objects Cache{sc}";
                            }

                            if (!string.IsNullOrEmpty(text))
                            {
                                SetCmdText(pCmdText, text);
                            }
                        }

                        return VSConstants.S_OK;
                    }
                }
            }

            if (NextCommandHandler != null)
            {
                return NextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
            return VSConstants.S_OK;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            CurrentActiveFilter = this;

            // Check if command is from Lumi SQL command set
            if (pguidCmdGroup == GuidList.guidLumiSqlCmdSet)
            {
                if (nCmdID == PkgCmdIDList.cmdidFormatUppercase)
                {
                    FormatTextView(_textView, BatchFormatMode.UppercaseKeywords);
                    return VSConstants.S_OK;
                }
                if (nCmdID == PkgCmdIDList.cmdidFormatLowercase)
                {
                    FormatTextView(_textView, BatchFormatMode.LowercaseAllExceptPreserved);
                    return VSConstants.S_OK;
                }
                if (nCmdID == PkgCmdIDList.cmdidRefreshDbCache)
                {
                    _ = DatabaseMetadataCacheService.Instance.RefreshCacheAsync(true);
                    return VSConstants.S_OK;
                }
            }

            // Check for configured Format Uppercase shortcut
            if (ShortcutHelper.IsShortcutActive(_settingsService.Settings.FormatUppercaseShortcut))
            {
                FormatTextView(_textView, BatchFormatMode.UppercaseKeywords);
                return VSConstants.S_OK;
            }

            // Check for configured Format Lowercase shortcut
            if (ShortcutHelper.IsShortcutActive(_settingsService.Settings.FormatLowercaseShortcut))
            {
                FormatTextView(_textView, BatchFormatMode.LowercaseAllExceptPreserved);
                return VSConstants.S_OK;
            }

            // Check for configured Refresh Database Objects Cache shortcut (default: Ctrl+Shift+R)
            if (ShortcutHelper.IsShortcutActive(_settingsService.Settings.RefreshDbCacheShortcut))
            {
                _ = DatabaseMetadataCacheService.Instance.RefreshCacheAsync(true);
                return VSConstants.S_OK;
            }

            // Check for Ctrl+Alt+S shortcut to open Quick Snippet Creator
            if (Keyboard.IsKeyDown(Key.S) && 
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                OpenQuickSnippetCreator();
                return VSConstants.S_OK;
            }

            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                // When suggestion popup is open, handle navigation keys
                if (_suggestionPopup.IsOpen)
                {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID)
                    {
                        case VSConstants.VSStd2KCmdID.UP:
                            _popupContent.SelectPrevious();
                            return VSConstants.S_OK;

                        case VSConstants.VSStd2KCmdID.DOWN:
                            _popupContent.SelectNext();
                            return VSConstants.S_OK;

                        case VSConstants.VSStd2KCmdID.CANCEL: // Esc
                            _suggestionPopup.IsOpen = false;
                            return VSConstants.S_OK;

                        case VSConstants.VSStd2KCmdID.TAB:
                        case VSConstants.VSStd2KCmdID.RETURN:
                            if (_popupContent.SelectedItem != null)
                            {
                                var item = _popupContent.SelectedItem;
                                _suggestionPopup.IsOpen = false;
                                ExpandSpecificSnippet(item);
                                return VSConstants.S_OK;
                            }
                            break;
                    }
                }

                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TAB:
                        _suggestionPopup.IsOpen = false;

                        // First attempt snippet expansion if enabled
                        if (_settingsService.Settings.EnableSnippets && TryExpandSnippetAtCaret())
                        {
                            return VSConstants.S_OK;
                        }

                        // Check if previous word before Tab needs capitalization
                        if (_settingsService.Settings.EnableAutoCapitalize)
                        {
                            FormatWordPrecedingCaret();
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.RETURN:
                        _suggestionPopup.IsOpen = false;

                        // Execute Return first to add the newline
                        int returnResult = NextCommandHandler != null
                            ? NextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                            : VSConstants.S_OK;

                        if (_settingsService.Settings.EnableAutoCapitalize)
                        {
                            FormatWordOnPreviousLineAfterReturn();
                        }

                        return returnResult;

                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        char typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

                        // If typing word separators: space, comma, semicolon, open/close parenthesis
                        if (typedChar == ' ' || typedChar == ',' || typedChar == ';' || typedChar == '(' || typedChar == ')')
                        {
                            _suggestionPopup.IsOpen = false;

                            if (_settingsService.Settings.EnableAutoCapitalize)
                            {
                                FormatWordPrecedingCaret();
                            }
                        }
                        else
                        {
                            // Pass typed char to editor first
                            int res = NextCommandHandler != null
                                ? NextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                                : VSConstants.S_OK;

                            // After character is inserted, evaluate suggestions
                            UpdateSuggestionPopup();
                            return res;
                        }
                        break;

                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                        int bResult = NextCommandHandler != null
                            ? NextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                            : VSConstants.S_OK;

                        UpdateSuggestionPopup();
                        return bResult;
                }
            }

            if (NextCommandHandler != null)
            {
                return NextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }

            return VSConstants.S_OK;
        }

        private void UpdateSuggestionPopup()
        {
            try
            {
                if (!_settingsService.Settings.EnableSnippets || !_settingsService.Settings.EnableSnippetSuggestions)
                {
                    _suggestionPopup.IsOpen = false;
                    return;
                }

                string currentWord = GetWordAtCaret();
                if (string.IsNullOrWhiteSpace(currentWord))
                {
                    _suggestionPopup.IsOpen = false;
                    return;
                }

                // Exclude comments, string literals, and bracketed identifiers
                var caretPoint = _textView.Caret.Position.BufferPosition;
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                int checkPos = caretPoint.Position > 0 ? caretPoint.Position - 1 : 0;
                if (_parsingService.IsExcludedContext(snapshot, checkPos))
                {
                    _suggestionPopup.IsOpen = false;
                    return;
                }

                var suggestions = _snippetService.GetSuggestions(currentWord);
                if (suggestions != null && suggestions.Count > 0)
                {
                    _popupContent.SetSuggestions(suggestions);

                    // Position popup right below caret
                    var textViewLine = _textView.GetTextViewLineContainingBufferPosition(caretPoint);
                    if (textViewLine != null)
                    {
                        var bounds = textViewLine.GetExtendedCharacterBounds(caretPoint);
                        _suggestionPopup.PlacementTarget = _textView.VisualElement;
                        _suggestionPopup.HorizontalOffset = bounds.Left - _textView.ViewportLeft;
                        _suggestionPopup.VerticalOffset = bounds.Bottom - _textView.ViewportTop + 4;
                        _suggestionPopup.IsOpen = true;
                    }
                }
                else
                {
                    _suggestionPopup.IsOpen = false;
                }
            }
            catch (Exception)
            {
                _suggestionPopup.IsOpen = false;
            }
        }

        private string GetWordAtCaret()
        {
            var caretPoint = _textView.Caret.Position.BufferPosition;
            var line = caretPoint.GetContainingLine();
            string lineText = line.GetText();
            int offset = caretPoint.Position - line.Start.Position;

            if (offset <= 0 || lineText.Length == 0)
                return string.Empty;

            int endIdx = offset - 1;
            if (char.IsWhiteSpace(lineText[endIdx]))
                return string.Empty;

            int startIdx = endIdx;
            while (startIdx >= 0 && (char.IsLetterOrDigit(lineText[startIdx]) || lineText[startIdx] == '_' || lineText[startIdx] == '#'))
            {
                startIdx--;
            }
            startIdx++;

            if (startIdx <= endIdx)
            {
                return lineText.Substring(startIdx, endIdx - startIdx + 1);
            }

            return string.Empty;
        }

        private void OnSnippetSelectedFromPopup(SnippetItem snippet)
        {
            _suggestionPopup.IsOpen = false;
            ExpandSpecificSnippet(snippet);
        }

        private void ExpandSpecificSnippet(SnippetItem snippet)
        {
            try
            {
                var caretPoint = _textView.Caret.Position.BufferPosition;
                var line = caretPoint.GetContainingLine();
                string textBeforeCaret = line.GetText().Substring(0, caretPoint.Position - line.Start.Position);

                string currentWord = GetWordAtCaret();
                int shortcutLength = currentWord.Length;

                var match = _snippetService.ExpandSnippet(snippet, currentWord);
                if (match == null)
                    return;

                int shortcutStartPosition = caretPoint.Position - shortcutLength;
                var replaceSpan = new Span(shortcutStartPosition, shortcutLength);

                using (var edit = _textView.TextBuffer.CreateEdit())
                {
                    edit.Replace(replaceSpan, match.ExpandedText);
                    edit.Apply();
                }

                int newCaretPosition = shortcutStartPosition + match.CaretOffsetInExpansion;
                if (newCaretPosition <= _textView.TextBuffer.CurrentSnapshot.Length)
                {
                    var newSnapshotPoint = new SnapshotPoint(_textView.TextBuffer.CurrentSnapshot, newCaretPosition);
                    _textView.Caret.MoveTo(newSnapshotPoint);
                }
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        private void OpenQuickSnippetCreator()
        {
            try
            {
                string selectedText = string.Empty;
                if (!_textView.Selection.IsEmpty)
                {
                    selectedText = _textView.Selection.StreamSelectionSpan.GetText();
                }

                var dialog = new QuickSnippetWindow(selectedText);
                dialog.ShowDialog();
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        private bool TryExpandSnippetAtCaret()
        {
            try
            {
                var caretPoint = _textView.Caret.Position.BufferPosition;
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                int checkPos = caretPoint.Position > 0 ? caretPoint.Position - 1 : 0;
                if (_parsingService.IsExcludedContext(snapshot, checkPos))
                    return false;

                var line = caretPoint.GetContainingLine();
                string textBeforeCaret = line.GetText().Substring(0, caretPoint.Position - line.Start.Position);

                var match = _snippetService.TryMatchAndExpand(textBeforeCaret);
                if (match == null)
                    return false;

                // Replace the typed shortcut with the expanded template
                int shortcutStartPosition = caretPoint.Position - match.ShortcutLength;
                var replaceSpan = new Span(shortcutStartPosition, match.ShortcutLength);

                using (var edit = _textView.TextBuffer.CreateEdit())
                {
                    edit.Replace(replaceSpan, match.ExpandedText);
                    edit.Apply();
                }

                // Place caret at $CURSOR$ position
                int newCaretPosition = shortcutStartPosition + match.CaretOffsetInExpansion;
                if (newCaretPosition <= _textView.TextBuffer.CurrentSnapshot.Length)
                {
                    var newSnapshotPoint = new SnapshotPoint(_textView.TextBuffer.CurrentSnapshot, newCaretPosition);
                    _textView.Caret.MoveTo(newSnapshotPoint);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void FormatWordPrecedingCaret()
        {
            try
            {
                var caretPoint = _textView.Caret.Position.BufferPosition;
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var line = caretPoint.GetContainingLine();

                int lineOffset = caretPoint.Position - line.Start.Position;
                string lineText = line.GetText();

                if (lineOffset <= 0 || lineText.Length == 0)
                    return;

                // Find candidate word right before caret
                int endIdx = lineOffset - 1;
                while (endIdx >= 0 && char.IsWhiteSpace(lineText[endIdx]))
                {
                    endIdx--;
                }

                if (endIdx < 0)
                    return;

                int startIdx = endIdx;
                while (startIdx >= 0 && (char.IsLetterOrDigit(lineText[startIdx]) || lineText[startIdx] == '_'))
                {
                    startIdx--;
                }
                startIdx++;

                if (startIdx > endIdx)
                    return;

                // Exclude T-SQL user variables: e.g. @Year, @Date, @Status (single @ prefix) and temporary tables (#Year)
                if (startIdx > 0 && ((lineText[startIdx - 1] == '@' && (startIdx < 2 || lineText[startIdx - 2] != '@')) || lineText[startIdx - 1] == '#'))
                    return;

                int wordLength = endIdx - startIdx + 1;
                string word = lineText.Substring(startIdx, wordLength);

                // Check formatting rules
                if (!_parsingService.TryFormatCandidateWord(word, _settingsService.Settings, out string formattedWord))
                    return;

                // Strict Exclusion Check: is this word inside a string, comment, or [delimited identifier]?
                int wordBufferStart = line.Start.Position + startIdx;
                if (_parsingService.IsExcludedContext(snapshot, wordBufferStart))
                    return;

                // Apply edit
                using (var edit = _textView.TextBuffer.CreateEdit())
                {
                    edit.Replace(new Span(wordBufferStart, wordLength), formattedWord);
                    edit.Apply();
                }
            }
            catch (Exception)
            {
                // Silently ignore to avoid interrupting user typing
            }
        }

        private void FormatWordOnPreviousLineAfterReturn()
        {
            try
            {
                var caretPoint = _textView.Caret.Position.BufferPosition;
                var line = caretPoint.GetContainingLine();

                if (line.LineNumber == 0)
                    return;

                // Examine the previous line
                var prevLine = _textView.TextBuffer.CurrentSnapshot.GetLineFromLineNumber(line.LineNumber - 1);
                string prevLineText = prevLine.GetText();

                if (string.IsNullOrWhiteSpace(prevLineText))
                    return;

                int endIdx = prevLineText.Length - 1;
                while (endIdx >= 0 && char.IsWhiteSpace(prevLineText[endIdx]))
                {
                    endIdx--;
                }

                if (endIdx < 0)
                    return;

                int startIdx = endIdx;
                while (startIdx >= 0 && (char.IsLetterOrDigit(prevLineText[startIdx]) || prevLineText[startIdx] == '_'))
                {
                    startIdx--;
                }
                startIdx++;

                if (startIdx > endIdx)
                    return;

                // Exclude T-SQL user variables: e.g. @Year, @Date, @Status (single @ prefix) and temporary tables (#Year)
                if (startIdx > 0 && ((prevLineText[startIdx - 1] == '@' && (startIdx < 2 || prevLineText[startIdx - 2] != '@')) || prevLineText[startIdx - 1] == '#'))
                    return;

                int wordLength = endIdx - startIdx + 1;
                string word = prevLineText.Substring(startIdx, wordLength);

                if (!_parsingService.TryFormatCandidateWord(word, _settingsService.Settings, out string formattedWord))
                    return;

                int wordBufferStart = prevLine.Start.Position + startIdx;
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                if (_parsingService.IsExcludedContext(snapshot, wordBufferStart))
                    return;
                using (var edit = _textView.TextBuffer.CreateEdit())
                {
                    edit.Replace(new Span(wordBufferStart, wordLength), formattedWord);
                    edit.Apply();
                }
            }
            catch (Exception)
            {
                // Ignore
            }
        }
    }
}
