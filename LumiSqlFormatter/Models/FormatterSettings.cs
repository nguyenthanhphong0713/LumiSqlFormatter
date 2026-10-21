using System;

namespace LumiSqlFormatter.Models
{
    public enum SnippetTheme
    {
        Dark,
        Light
    }

    public enum BatchFormatMode
    {
        UppercaseKeywords,          // Lumi Format Uppercase: All keywords/types/functions uppercase
        LowercaseAllExceptPreserved // Lumi Format Lowercase: All code lower, keep @variables, temp table variables, #temp tables
    }

    [Serializable]
    public class FormatterSettings
    {
        public bool EnableAutoCapitalize { get; set; } = true;
        public CasingStyle KeywordCasing { get; set; } = CasingStyle.Uppercase;
        public CasingStyle DataTypeCasing { get; set; } = CasingStyle.Uppercase;
        public CasingStyle FunctionCasing { get; set; } = CasingStyle.Uppercase;
        public bool EnableSnippets { get; set; } = true;
        public bool EnableSnippetSuggestions { get; set; } = true;
        public string SnippetsDirectoryPath { get; set; } = string.Empty;
        public SnippetTheme SnippetTheme { get; set; } = SnippetTheme.Dark;

        public string FormatUppercaseShortcut { get; set; } = "Ctrl+Alt+U";
        public string FormatLowercaseShortcut { get; set; } = "Ctrl+Alt+L";
        public string RefreshDbCacheShortcut { get; set; } = "Ctrl+Shift+R";

        public FormatterSettings Clone()
        {
            return new FormatterSettings
            {
                EnableAutoCapitalize = this.EnableAutoCapitalize,
                KeywordCasing = this.KeywordCasing,
                DataTypeCasing = this.DataTypeCasing,
                FunctionCasing = this.FunctionCasing,
                EnableSnippets = this.EnableSnippets,
                EnableSnippetSuggestions = this.EnableSnippetSuggestions,
                SnippetsDirectoryPath = this.SnippetsDirectoryPath,
                SnippetTheme = this.SnippetTheme,
                FormatUppercaseShortcut = this.FormatUppercaseShortcut,
                FormatLowercaseShortcut = this.FormatLowercaseShortcut,
                RefreshDbCacheShortcut = this.RefreshDbCacheShortcut
            };
        }
    }
}
