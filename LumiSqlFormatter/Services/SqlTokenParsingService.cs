using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LumiSqlFormatter.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.Text;

namespace LumiSqlFormatter.Services
{
    public enum SqlWordCategory
    {
        None = 0,
        ReservedKeyword = 1,
        DataType = 2,
        BuiltInFunction = 3,
        SystemProcedureOrFunction = 4
    }

    public class SqlTokenParsingService
    {
        private static readonly Lazy<SqlTokenParsingService> _instance = 
            new Lazy<SqlTokenParsingService>(() => new SqlTokenParsingService());
        public static SqlTokenParsingService Instance => _instance.Value;

        // Regex patterns for scanning stored procedures and functions in SQL script
        private static readonly Regex ProcFuncDefRegex = new Regex(
            @"\b(?:CREATE|ALTER)\s+(?:OR\s+ALTER\s+)?(?:PROCEDURE|PROC|FUNCTION)\s+(?:\[?([A-Za-z0-9_]+)\]?\s*\.\s*)?\[?([A-Za-z0-9_]+)\]?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ExecRegex = new Regex(
            @"\b(?:EXEC|EXECUTE)\s+(?:@[A-Za-z0-9_]+\s*=\s*)?(?:\[?([A-Za-z0-9_]+)\]?\s*\.\s*)?\[?([A-Za-z0-9_]+)\]?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FuncCallRegex = new Regex(
            @"(?:\[?[A-Za-z0-9_]+\]?\s*\.\s*)\[?([A-Za-z0-9_]+)\]?\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Fast lookup dictionary: lower-case word -> (Category, Canonical PascalCase)
        private readonly Dictionary<string, (SqlWordCategory Category, string CanonicalPascalCase)> _tokenDictionary =
            new Dictionary<string, (SqlWordCategory, string)>(StringComparer.OrdinalIgnoreCase);

        private SqlTokenParsingService()
        {
            InitializeTokenDictionary();
        }

        public bool TryGetWordInfo(string word, out SqlWordCategory category, out string canonicalPascalCase)
        {
            category = SqlWordCategory.None;
            canonicalPascalCase = null;

            if (string.IsNullOrEmpty(word))
                return false;

            if (_tokenDictionary.TryGetValue(word, out var info))
            {
                category = info.Category;
                canonicalPascalCase = info.CanonicalPascalCase;
                return true;
            }

            // Check dynamic Database Metadata Cache (Stored Procedures & Functions from active database)
            if (DatabaseMetadataCacheService.Instance.TryGetCanonicalName(word, out string dbCanonicalName))
            {
                category = SqlWordCategory.SystemProcedureOrFunction;
                canonicalPascalCase = dbCanonicalName;
                return true;
            }

            return false;
        }

        public string FormatWord(string word, CasingStyle style, string canonicalPascalCase)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            switch (style)
            {
                case CasingStyle.Uppercase:
                    return word.ToUpperInvariant();
                case CasingStyle.Lowercase:
                    return word.ToLowerInvariant();
                case CasingStyle.PascalCase:
                    return !string.IsNullOrEmpty(canonicalPascalCase) ? canonicalPascalCase : ToDefaultPascalCase(word);
                default:
                    return word;
            }
        }

        public bool TryFormatCandidateWord(string word, FormatterSettings settings, out string formattedWord)
        {
            formattedWord = word;
            if (string.IsNullOrEmpty(word) || settings == null || !settings.EnableAutoCapitalize)
                return false;

            if (TryGetWordInfo(word, out var category, out var canonicalPascalCase))
            {
                if (category == SqlWordCategory.SystemProcedureOrFunction)
                {
                    // System procedures and functions strictly retain their canonical definition format
                    formattedWord = canonicalPascalCase;
                    return !string.Equals(word, formattedWord, StringComparison.Ordinal);
                }

                CasingStyle targetStyle;
                switch (category)
                {
                    case SqlWordCategory.ReservedKeyword:
                        targetStyle = settings.KeywordCasing;
                        break;
                    case SqlWordCategory.DataType:
                        targetStyle = settings.DataTypeCasing;
                        break;
                    case SqlWordCategory.BuiltInFunction:
                        targetStyle = settings.FunctionCasing;
                        break;
                    default:
                        return false;
                }

                formattedWord = FormatWord(word, targetStyle, canonicalPascalCase);
                return !string.Equals(word, formattedWord, StringComparison.Ordinal);
            }

            return false;
        }

        /// <summary>
        /// Checks whether a specific buffer position in a document snapshot is inside
        /// comments, string literals, or delimited identifiers (multi-line aware).
        /// </summary>
        public bool IsExcludedContext(ITextSnapshot snapshot, int bufferPosition)
        {
            if (snapshot == null || bufferPosition < 0 || bufferPosition > snapshot.Length)
                return false;

            try
            {
                int scanStart = 0;
                if (bufferPosition > 200000)
                {
                    scanStart = Math.Max(0, bufferPosition - 50000);
                }

                int scanLength = bufferPosition - scanStart;
                if (scanLength <= 0)
                    return false;

                string text = snapshot.GetText(scanStart, scanLength);
                return IsLexicallyExcluded(text, text.Length);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether a specific offset in a text string is inside
        /// comments, string literals, or delimited identifiers.
        /// </summary>
        public bool IsExcludedContext(string text, int offsetInText)
        {
            if (string.IsNullOrEmpty(text) || offsetInText < 0 || offsetInText > text.Length)
                return false;

            try
            {
                return IsLexicallyExcluded(text, offsetInText);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsLexicallyExcluded(string text, int targetOffset)
        {
            if (string.IsNullOrEmpty(text) || targetOffset <= 0)
                return false;

            bool inString = false;
            bool inSingleLineComment = false;
            int multilineCommentDepth = 0;
            bool inBracket = false;
            bool inQuotedIdentifier = false;

            int length = Math.Min(targetOffset, text.Length);

            for (int i = 0; i < length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (c == '\'')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            // Escaped single quote '' inside string literal
                            i++;
                        }
                        else
                        {
                            inString = false;
                        }
                    }
                }
                else if (inSingleLineComment)
                {
                    if (c == '\r' || c == '\n')
                    {
                        inSingleLineComment = false;
                    }
                }
                else if (multilineCommentDepth > 0)
                {
                    if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                    {
                        multilineCommentDepth++;
                        i++;
                    }
                    else if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        multilineCommentDepth--;
                        i++;
                    }
                }
                else if (inBracket)
                {
                    if (c == ']')
                    {
                        if (i + 1 < text.Length && text[i + 1] == ']')
                        {
                            // Escaped bracket ]] inside delimited identifier
                            i++;
                        }
                        else
                        {
                            inBracket = false;
                        }
                    }
                }
                else if (inQuotedIdentifier)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            // Escaped quote "" inside delimited identifier
                            i++;
                        }
                        else
                        {
                            inQuotedIdentifier = false;
                        }
                    }
                }
                else
                {
                    // Outside of string/comment/delimiter
                    if (c == '\'')
                    {
                        inString = true;
                    }
                    else if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                    {
                        inSingleLineComment = true;
                        i++;
                    }
                    else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                    {
                        multilineCommentDepth = 1;
                        i++;
                    }
                    else if (c == '[')
                    {
                        inBracket = true;
                    }
                    else if (c == '"')
                    {
                        inQuotedIdentifier = true;
                    }
                }
            }

            return inString || inSingleLineComment || multilineCommentDepth > 0 || inBracket || inQuotedIdentifier;
        }

        private static string ToDefaultPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            if (input.Length == 1)
                return input.ToUpperInvariant();
            return char.ToUpperInvariant(input[0]) + input.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// Strips single-line and multi-line comments as well as string literals by replacing
        /// their contents with spaces, preserving offsets and structure for regex scanning.
        /// </summary>
        private static string StripCommentsAndLiterals(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            char[] chars = text.ToCharArray();
            int n = chars.Length;
            int i = 0;

            while (i < n)
            {
                // 1. Single-line comment: --
                if (chars[i] == '-' && i + 1 < n && chars[i + 1] == '-')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                    while (i < n && chars[i] != '\r' && chars[i] != '\n')
                    {
                        chars[i] = ' ';
                        i++;
                    }
                    continue;
                }

                // 2. Multi-line comment: /* ... */
                if (chars[i] == '/' && i + 1 < n && chars[i + 1] == '*')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                    int depth = 1;
                    while (i < n && depth > 0)
                    {
                        if (chars[i] == '/' && i + 1 < n && chars[i + 1] == '*')
                        {
                            chars[i] = ' ';
                            chars[i + 1] = ' ';
                            depth++;
                            i += 2;
                        }
                        else if (chars[i] == '*' && i + 1 < n && chars[i + 1] == '/')
                        {
                            chars[i] = ' ';
                            chars[i + 1] = ' ';
                            depth--;
                            i += 2;
                        }
                        else
                        {
                            if (chars[i] != '\r' && chars[i] != '\n')
                                chars[i] = ' ';
                            i++;
                        }
                    }
                    continue;
                }

                // 3. String literal: '...' or N'...'
                if (chars[i] == '\'' || ((chars[i] == 'N' || chars[i] == 'n') && i + 1 < n && chars[i + 1] == '\''))
                {
                    if (chars[i] == 'N' || chars[i] == 'n')
                    {
                        chars[i] = ' ';
                        i++;
                    }
                    chars[i] = ' ';
                    i++;
                    while (i < n)
                    {
                        if (chars[i] == '\'')
                        {
                            chars[i] = ' ';
                            i++;
                            if (i < n && chars[i] == '\'')
                            {
                                chars[i] = ' ';
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (chars[i] != '\r' && chars[i] != '\n')
                                chars[i] = ' ';
                            i++;
                        }
                    }
                    continue;
                }

                i++;
            }

            return new string(chars);
        }

        /// <summary>
        /// Scans a SQL script for user-defined and referenced stored procedures and functions.
        /// Extracts their declared or referenced names to preserve exact casing during batch formatting.
        /// </summary>
        public Dictionary<string, string> ScanProceduresAndFunctions(string text)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text))
                return dict;

            string cleanText = StripCommentsAndLiterals(text);

            // 1. Scan procedure / function declarations: CREATE / ALTER [PROCEDURE|PROC|FUNCTION] [schema.]name
            foreach (Match m in ProcFuncDefRegex.Matches(cleanText))
            {
                if (m.Groups.Count > 2 && !string.IsNullOrEmpty(m.Groups[2].Value))
                {
                    string name = m.Groups[2].Value;
                    dict[name] = name; // Definition has highest priority
                }
            }

            // 2. Scan EXEC / EXECUTE calls
            foreach (Match m in ExecRegex.Matches(cleanText))
            {
                if (m.Groups.Count > 2 && !string.IsNullOrEmpty(m.Groups[2].Value))
                {
                    string name = m.Groups[2].Value;
                    if (!dict.ContainsKey(name))
                    {
                        dict[name] = name;
                    }
                }
            }

            // 3. Scan schema-qualified function calls: [schema].[function_name](...)
            foreach (Match m in FuncCallRegex.Matches(cleanText))
            {
                if (m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                {
                    string name = m.Groups[1].Value;
                    if (!dict.ContainsKey(name))
                    {
                        dict[name] = name;
                    }
                }
            }

            return dict;
        }

        /// <summary>
        /// Performs batch formatting on an entire SQL script or selected text span:
        /// - UppercaseKeywords: converts all reserved keywords, data types, and built-in functions to UPPERCASE,
        ///   while preserving comments, string literals, bracketed identifiers, user variables, temp tables,
        ///   and system/declared stored procedures and functions.
        /// - LowercaseAllExceptPreserved: converts all code text to lowercase,
        ///   while preserving comments, string literals, bracketed identifiers, user variables (@variables, table variables),
        ///   temp tables (#temp, ##temp), and system/declared stored procedures and functions.
        /// </summary>
        public string FormatBatch(string text, BatchFormatMode mode)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Trigger background database change detection and re-cache if needed
            _ = DatabaseMetadataCacheService.Instance.CheckAndRefreshIfDatabaseChangedAsync();

            var scannedProcs = ScanProceduresAndFunctions(text);

            int n = text.Length;
            var sb = new System.Text.StringBuilder(n);
            int i = 0;

            while (i < n)
            {
                char c = text[i];

                // 1. Single-line comment: --
                if (c == '-' && i + 1 < n && text[i + 1] == '-')
                {
                    sb.Append("--");
                    i += 2;
                    while (i < n && text[i] != '\r' && text[i] != '\n')
                    {
                        sb.Append(text[i++]);
                    }
                    continue;
                }

                // 2. Multi-line comment: /* ... */
                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    sb.Append("/*");
                    i += 2;
                    int depth = 1;
                    while (i < n && depth > 0)
                    {
                        if (text[i] == '/' && i + 1 < n && text[i + 1] == '*')
                        {
                            depth++;
                            sb.Append("/*");
                            i += 2;
                        }
                        else if (text[i] == '*' && i + 1 < n && text[i + 1] == '/')
                        {
                            depth--;
                            sb.Append("*/");
                            i += 2;
                        }
                        else
                        {
                            sb.Append(text[i++]);
                        }
                    }
                    continue;
                }

                // 3. String literal: '...' or N'...'
                if (c == '\'' || ((c == 'N' || c == 'n') && i + 1 < n && text[i + 1] == '\''))
                {
                    if (c == 'N' || c == 'n')
                    {
                        sb.Append(c);
                        i++;
                    }
                    sb.Append('\'');
                    i++;
                    while (i < n)
                    {
                        if (text[i] == '\'')
                        {
                            sb.Append('\'');
                            i++;
                            if (i < n && text[i] == '\'')
                            {
                                // Escaped single quote inside string
                                sb.Append('\'');
                                i++;
                            }
                            else
                            {
                                break; // End of string literal
                            }
                        }
                        else
                        {
                            sb.Append(text[i++]);
                        }
                    }
                    continue;
                }

                // 4. Bracketed identifier: [...]
                if (c == '[')
                {
                    sb.Append('[');
                    i++;
                    while (i < n)
                    {
                        if (text[i] == ']')
                        {
                            sb.Append(']');
                            i++;
                            if (i < n && text[i] == ']')
                            {
                                // Escaped bracket ]]
                                sb.Append(']');
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(text[i++]);
                        }
                    }
                    continue;
                }

                // 5. Double-quoted identifier: "..."
                if (c == '"')
                {
                    sb.Append('"');
                    i++;
                    while (i < n)
                    {
                        if (text[i] == '"')
                        {
                            sb.Append('"');
                            i++;
                            if (i < n && text[i] == '"')
                            {
                                sb.Append('"');
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(text[i++]);
                        }
                    }
                    continue;
                }

                // 6. User variables & table variables: @variable, @TempTable
                if (c == '@')
                {
                    if (i + 1 < n && text[i + 1] == '@')
                    {
                        // System variable/function: @@VERSION, @@ROWCOUNT, @@ERROR
                        int start = i;
                        i += 2;
                        while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        {
                            i++;
                        }
                        string sysVar = text.Substring(start, i - start);
                        if (mode == BatchFormatMode.UppercaseKeywords)
                            sb.Append(sysVar.ToUpperInvariant());
                        else
                            sb.Append(sysVar.ToLowerInvariant());
                        continue;
                    }
                    else
                    {
                        // User variable or table variable: preserve original casing!
                        int start = i;
                        i++;
                        while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                        {
                            i++;
                        }
                        string userVar = text.Substring(start, i - start);
                        sb.Append(userVar);
                        continue;
                    }
                }

                // 7. Temp tables: #temp, ##globalTemp
                if (c == '#')
                {
                    int start = i;
                    i++;
                    if (i < n && text[i] == '#')
                    {
                        i++;
                    }
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                    {
                        i++;
                    }
                    string tempTable = text.Substring(start, i - start);
                    sb.Append(tempTable); // preserve original casing!
                    continue;
                }

                // 8. Word / Identifier
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    {
                        i++;
                    }
                    string word = text.Substring(start, i - start);

                    // 8.1 Check pre-loaded system procedure or function (canonical format from system)
                    if (TryGetWordInfo(word, out var category, out var canonicalName) && category == SqlWordCategory.SystemProcedureOrFunction)
                    {
                        sb.Append(canonicalName);
                        continue;
                    }

                    // 8.2 Check database metadata cache (Stored Procedures & Functions from current database)
                    if (DatabaseMetadataCacheService.Instance.TryGetCanonicalName(word, out var dbCanonicalName))
                    {
                        sb.Append(dbCanonicalName);
                        continue;
                    }

                    // 8.3 Check scanned procedure or function defined/called in script (preserve declared format)
                    if (scannedProcs.TryGetValue(word, out var declaredName))
                    {
                        sb.Append(declaredName);
                        continue;
                    }

                    // 8.4 Check general system procedure/function naming convention (sp_*, xp_*, fn_*)
                    if (word.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) ||
                        word.StartsWith("xp_", StringComparison.OrdinalIgnoreCase) ||
                        word.StartsWith("fn_", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(word);
                        continue;
                    }

                    if (mode == BatchFormatMode.UppercaseKeywords)
                    {
                        if (category == SqlWordCategory.ReservedKeyword ||
                            category == SqlWordCategory.DataType ||
                            category == SqlWordCategory.BuiltInFunction)
                        {
                            sb.Append(word.ToUpperInvariant());
                        }
                        else
                        {
                            sb.Append(word);
                        }
                    }
                    else // LowercaseAllExceptPreserved
                    {
                        sb.Append(word.ToLowerInvariant());
                    }
                    continue;
                }

                // 9. Other characters (whitespace, punctuation, numbers, operators)
                if (mode == BatchFormatMode.LowercaseAllExceptPreserved)
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
                i++;
            }

            return sb.ToString();
        }

        private void InitializeTokenDictionary()
        {
            // 1. RESERVED KEYWORDS & SYNTAX CLAUSES
            var keywords = new (string word, string pascal)[]
            {
                // DQL, DML, DDL
                ("SELECT", "Select"),
                ("FROM", "From"),
                ("WHERE", "Where"),
                ("GROUP", "Group"),
                ("BY", "By"),
                ("HAVING", "Having"),
                ("ORDER", "Order"),
                ("ASC", "Asc"),
                ("DESC", "Desc"),
                ("INSERT", "Insert"),
                ("INTO", "Into"),
                ("VALUES", "Values"),
                ("VALUE", "Value"),
                ("UPDATE", "Update"),
                ("SET", "Set"),
                ("DELETE", "Delete"),
                ("MERGE", "Merge"),
                ("MATCHED", "Matched"),
                ("TARGET", "Target"),
                ("SOURCE", "Source"),
                ("USING", "Using"),
                ("OUTPUT", "Output"),
                ("DELETED", "Deleted"),
                ("INSERTED", "Inserted"),
                ("ACTION", "Action"),

                // Joins & Set Operators
                ("JOIN", "Join"),
                ("INNER", "Inner"),
                ("LEFT", "Left"),
                ("RIGHT", "Right"),
                ("FULL", "Full"),
                ("CROSS", "Cross"),
                ("OUTER", "Outer"),
                ("APPLY", "Apply"),
                ("ON", "On"),
                ("UNION", "Union"),
                ("ALL", "All"),
                ("EXCEPT", "Except"),
                ("INTERSECT", "Intersect"),

                // Predicates & Logic
                ("DISTINCT", "Distinct"),
                ("TOP", "Top"),
                ("PERCENT", "Percent"),
                ("TIES", "Ties"),
                ("CASE", "Case"),
                ("WHEN", "When"),
                ("THEN", "Then"),
                ("ELSE", "Else"),
                ("END", "End"),
                ("AND", "And"),
                ("OR", "Or"),
                ("NOT", "Not"),
                ("IN", "In"),
                ("IS", "Is"),
                ("NULL", "Null"),
                ("LIKE", "Like"),
                ("BETWEEN", "Between"),
                ("EXISTS", "Exists"),
                ("SOME", "Some"),
                ("ANY", "Any"),
                ("COLLATE", "Collate"),
                ("ESCAPE", "Escape"),

                // Window Functions & Pagination
                ("OVER", "Over"),
                ("PARTITION", "Partition"),
                ("ROWS", "Rows"),
                ("RANGE", "Range"),
                ("UNBOUNDED", "Unbounded"),
                ("PRECEDING", "Preceding"),
                ("FOLLOWING", "Following"),
                ("CURRENT", "Current"),
                ("ROW", "Row"),
                ("OFFSET", "Offset"),
                ("FETCH", "Fetch"),
                ("NEXT", "Next"),
                ("ONLY", "Only"),
                ("FIRST", "First"),
                ("LAST", "Last"),

                // Pivot & Transform
                ("PIVOT", "Pivot"),
                ("UNPIVOT", "Unpivot"),
                ("TABLESAMPLE", "TableSample"),

                // Transactions & Flow Control
                ("BEGIN", "Begin"),
                ("TRAN", "Tran"),
                ("TRANSACTION", "Transaction"),
                ("COMMIT", "Commit"),
                ("ROLLBACK", "Rollback"),
                ("SAVE", "Save"),
                ("EXEC", "Exec"),
                ("EXECUTE", "Execute"),
                ("DECLARE", "Declare"),
                ("AS", "As"),
                ("WITH", "With"),
                ("IF", "If"),
                ("WHILE", "While"),
                ("BREAK", "Break"),
                ("CONTINUE", "Continue"),
                ("RETURN", "Return"),
                ("RETURNS", "Returns"),
                ("GOTO", "GoTo"),
                ("WAITFOR", "WaitFor"),
                ("DELAY", "Delay"),

                // Error Handling
                ("TRY", "Try"),
                ("CATCH", "Catch"),
                ("THROW", "Throw"),
                ("RAISERROR", "Raiserror"),
                ("PRINT", "Print"),

                // Database Objects & DDL
                ("CREATE", "Create"),
                ("ALTER", "Alter"),
                ("DROP", "Drop"),
                ("TRUNCATE", "Truncate"),
                ("TABLE", "Table"),
                ("COLUMN", "Column"),
                ("VIEW", "View"),
                ("PROCEDURE", "Procedure"),
                ("PROC", "Proc"),
                ("FUNCTION", "Function"),
                ("TRIGGER", "Trigger"),
                ("INDEX", "Index"),
                ("DATABASE", "Database"),
                ("SCHEMA", "Schema"),
                ("SEQUENCE", "Sequence"),
                ("SYNONYM", "Synonym"),
                ("TYPE", "Type"),
                ("RULE", "Rule"),
                ("DEFAULT", "Default"),
                ("USER", "User"),
                ("ROLE", "Role"),
                ("LOGIN", "Login"),
                ("CERTIFICATE", "Certificate"),
                ("SYMMETRIC", "Symmetric"),
                ("ASYMMETRIC", "Asymmetric"),
                ("KEY", "Key"),
                ("CREDENTIAL", "Credential"),

                // Constraints & Keys
                ("PRIMARY", "Primary"),
                ("FOREIGN", "Foreign"),
                ("REFERENCES", "References"),
                ("CONSTRAINT", "Constraint"),
                ("CHECK", "Check"),
                ("UNIQUE", "Unique"),
                ("CLUSTERED", "Clustered"),
                ("NONCLUSTERED", "NonClustered"),
                ("COLUMNSTORE", "ColumnStore"),
                ("CASCADE", "Cascade"),
                ("RESTRICT", "Restrict"),
                ("NOCHECK", "NoCheck"),
                ("CHECKPOINT", "Checkpoint"),

                // Index & Table Options
                ("INCLUDE", "Include"),
                ("FILLFACTOR", "FillFactor"),
                ("PAD_INDEX", "Pad_Index"),
                ("STATISTICS_NORECOMPUTE", "Statistics_NoRecompute"),
                ("IGNORE_DUP_KEY", "Ignore_Dup_Key"),
                ("ALLOW_ROW_LOCKS", "Allow_Row_Locks"),
                ("ALLOW_PAGE_LOCKS", "Allow_Page_Locks"),
                ("DATA_COMPRESSION", "Data_Compression"),
                ("PAGE", "Page"),
                ("NONE", "None"),
                ("ONLINE", "Online"),
                ("OFFLINE", "Offline"),
                ("SORT_IN_TEMPDB", "Sort_In_Tempdb"),
                ("MAXDOP", "MaxDop"),
                ("PERSISTED", "Persisted"),
                ("SPARSE", "Sparse"),
                ("FILEGROUP", "FileGroup"),
                ("IDENTITY", "Identity"),
                ("IDENTITY_INSERT", "Identity_Insert"),
                ("ROWGUIDCOL", "RowGuidCol"),

                // Hints
                ("OPTION", "Option"),
                ("RECOMPILE", "Recompile"),
                ("MAXRECURSION", "MaxRecursion"),
                ("OPTIMIZE", "Optimize"),
                ("FOR", "For"),
                ("UNKNOWN", "Unknown"),
                ("FAST", "Fast"),
                ("FORCE", "Force"),
                ("EXPAND", "Expand"),
                ("VIEWS", "Views"),
                ("LOOP", "Loop"),
                ("HASH", "Hash"),
                ("NOLOCK", "NoLock"),
                ("READUNCOMMITTED", "ReadUncommitted"),
                ("READCOMMITTED", "ReadCommitted"),
                ("REPEATABLEREAD", "RepeatableRead"),
                ("SERIALIZABLE", "Serializable"),
                ("READCOMMITTEDLOCK", "ReadCommittedLock"),
                ("READPAST", "ReadPast"),
                ("XLOCK", "XLock"),
                ("UPDLOCK", "UpdLock"),
                ("ROWLOCK", "RowLock"),
                ("PAGLOCK", "PagLock"),
                ("TABLOCK", "TabLock"),
                ("TABLOCKX", "TabLockX"),
                ("HOLDLOCK", "HoldLock"),
                ("NOWAIT", "NoWait"),
                ("KEEPIDENTITY", "KeepIdentity"),
                ("KEEPDEFAULTS", "KeepDefaults"),

                // Security & Permissions
                ("GRANT", "Grant"),
                ("DENY", "Deny"),
                ("REVOKE", "Revoke"),
                ("TO", "To"),
                ("AUTHORIZATION", "Authorization"),
                ("REVERT", "Revert"),
                ("SECURITYAUDIT", "SecurityAudit"),
                ("ENCRYPTION", "Encryption"),
                ("SCHEMABINDING", "SchemaBinding"),
                ("VIEW_METADATA", "View_Metadata"),
                ("CALLER", "Caller"),
                ("SELF", "Self"),
                ("OWNER", "Owner"),

                // Triggers & Sequences
                ("AFTER", "After"),
                ("INSTEAD", "Instead"),
                ("OF", "Of"),
                ("RESTART", "Restart"),
                ("INCREMENT", "Increment"),
                ("MINVALUE", "MinValue"),
                ("MAXVALUE", "MaxValue"),
                ("CYCLE", "Cycle"),
                ("NO", "No"),
                ("CACHE", "Cache"),

                // Settings & Session
                ("NOCOUNT", "NoCount"),
                ("XACT_ABORT", "Xact_Abort"),
                ("ANSI_NULLS", "Ansi_Nulls"),
                ("QUOTED_IDENTIFIER", "Quoted_Identifier"),
                ("ARITHABORT", "ArithAbort"),
                ("NUMERIC_ROUNDABORT", "Numeric_RoundAbort"),
                ("CONCAT_NULL_YIELDS_NULL", "Concat_Null_Yields_Null"),
                ("ANSI_PADDING", "Ansi_Padding"),
                ("ANSI_WARNINGS", "Ansi_Warnings"),
                ("CURSOR_CLOSE_ON_COMMIT", "Cursor_Close_On_Commit"),
                ("IMPLICIT_TRANSACTIONS", "Implicit_Transactions"),
                ("ISOLATION", "Isolation"),
                ("LEVEL", "Level"),
                ("SNAPSHOT", "Snapshot"),
                ("FORCEPLAN", "ForcePlan"),
                ("SHOWPLAN_ALL", "ShowPlan_All"),
                ("SHOWPLAN_TEXT", "ShowPlan_Text"),
                ("SHOWPLAN_XML", "ShowPlan_Xml"),
                ("STATISTICS", "Statistics"),
                ("IO", "Io"),
                ("PROFILE", "Profile"),
                ("OFF", "Off"),
                ("ON", "On"),
                ("GO", "GO"),
                ("USE", "Use"),
                ("RECONFIGURE", "Reconfigure"),

                // Cursor
                ("CURSOR", "Cursor"),
                ("OPEN", "Open"),
                ("CLOSE", "Close"),
                ("DEALLOCATE", "Deallocate"),
                ("GLOBAL", "Global"),
                ("LOCAL", "Local"),
                ("FORWARD_ONLY", "Forward_Only"),
                ("SCROLL", "Scroll"),
                ("STATIC", "Static"),
                ("KEYSET", "KeySet"),
                ("DYNAMIC", "Dynamic"),
                ("FAST_FORWARD", "Fast_Forward"),
                ("READ_ONLY", "Read_Only"),
                ("SCROLL_LOCKS", "Scroll_Locks"),
                ("OPTIMISTIC", "Optimistic"),
                ("TYPE_WARNING", "Type_Warning"),

                // Admin
                ("BACKUP", "Backup"),
                ("RESTORE", "Restore"),
                ("DBCC", "Dbcc"),
                ("BULK", "Bulk"),
                ("SHUTDOWN", "Shutdown"),
                ("KILL", "Kill"),
                ("ENABLE", "Enable"),
                ("DISABLE", "Disable"),
                ("EXTERNAL", "External"),
                ("DISK", "Disk"),

                // XML, JSON, Full-Text
                ("PATH", "Path"),
                ("AUTO", "Auto"),
                ("EXPLICIT", "Explicit"),
                ("RAW", "Raw"),
                ("ELEMENTS", "Elements"),
                ("ROOT", "Root"),
                ("OPENROWSET", "OpenRowSet"),
                ("OPENDATASOURCE", "OpenDataSource"),
                ("OPENQUERY", "OpenQuery"),
                ("OPENXML", "OpenXml"),
                ("OPENJSON", "OpenJson"),
                ("CONTAINS", "Contains"),
                ("FREETEXT", "FreeText"),
                ("CONTAINSTABLE", "ContainsTable"),
                ("FREETEXTTABLE", "FreeTextTable"),
                ("READTEXT", "ReadText"),
                ("UPDATETEXT", "UpdateText"),
                ("WRITETEXT", "WriteText"),
                ("TEXTSIZE", "TextSize"),

                // Parameters & Modes
                ("OUT", "Out"),
                ("READONLY", "ReadOnly"),
                ("VARYING", "Varying")
            };

            foreach (var kw in keywords)
            {
                _tokenDictionary[kw.word] = (SqlWordCategory.ReservedKeyword, kw.pascal);
            }

            // 2. BUILT-IN DATA TYPES
            var dataTypes = new (string word, string pascal)[]
            {
                ("INT", "Int"),
                ("INTEGER", "Integer"),
                ("BIGINT", "BigInt"),
                ("SMALLINT", "SmallInt"),
                ("TINYINT", "TinyInt"),
                ("BIT", "Bit"),
                ("DECIMAL", "Decimal"),
                ("NUMERIC", "Numeric"),
                ("MONEY", "Money"),
                ("SMALLMONEY", "SmallMoney"),
                ("FLOAT", "Float"),
                ("REAL", "Real"),
                ("DATE", "Date"),
                ("DATETIME", "DateTime"),
                ("DATETIME2", "DateTime2"),
                ("DATETIMEOFFSET", "DateTimeOffset"),
                ("SMALLDATETIME", "SmallDateTime"),
                ("TIME", "Time"),
                ("CHAR", "Char"),
                ("CHARACTER", "Character"),
                ("VARCHAR", "VarChar"),
                ("NCHAR", "NChar"),
                ("NVARCHAR", "NVarChar"),
                ("TEXT", "Text"),
                ("NTEXT", "NText"),
                ("BINARY", "Binary"),
                ("VARBINARY", "VarBinary"),
                ("IMAGE", "Image"),
                ("UNIQUEIDENTIFIER", "UniqueIdentifier"),
                ("XML", "Xml"),
                ("SQL_VARIANT", "Sql_Variant"),
                ("GEOMETRY", "Geometry"),
                ("GEOGRAPHY", "Geography"),
                ("HIERARCHYID", "HierarchyId"),
                ("ROWVERSION", "RowVersion"),
                ("TIMESTAMP", "Timestamp"),
                ("SYSNAME", "SysName"),
                ("JSON", "Json"),
                ("VECTOR", "Vector")
            };

            foreach (var dt in dataTypes)
            {
                if (!_tokenDictionary.ContainsKey(dt.word))
                {
                    _tokenDictionary[dt.word] = (SqlWordCategory.DataType, dt.pascal);
                }
            }

            // 3. BUILT-IN FUNCTIONS
            var functions = new (string word, string pascal)[]
            {
                // Aggregates
                ("COUNT", "Count"),
                ("COUNT_BIG", "Count_Big"),
                ("SUM", "Sum"),
                ("AVG", "Avg"),
                ("MIN", "Min"),
                ("MAX", "Max"),
                ("STRING_AGG", "String_Agg"),
                ("APPROX_COUNT_DISTINCT", "Approx_Count_Distinct"),
                ("CHECKSUM_AGG", "Checksum_Agg"),
                ("STDEV", "StDev"),
                ("STDEVP", "StDevP"),
                ("VAR", "Var"),
                ("VARP", "VarP"),
                ("GROUPING", "Grouping"),
                ("GROUPING_ID", "Grouping_Id"),

                // Window & Ranking
                ("ROW_NUMBER", "Row_Number"),
                ("RANK", "Rank"),
                ("DENSE_RANK", "Dense_Rank"),
                ("NTILE", "Ntile"),
                ("LEAD", "Lead"),
                ("LAG", "Lag"),
                ("FIRST_VALUE", "First_Value"),
                ("LAST_VALUE", "Last_Value"),
                ("PERCENT_RANK", "Percent_Rank"),
                ("CUME_DIST", "Cume_Dist"),
                ("PERCENTILE_CONT", "Percentile_Cont"),
                ("PERCENTILE_DISC", "Percentile_Disc"),

                // String
                ("ASCII", "Ascii"),
                ("CHAR", "Char"),
                ("CHARINDEX", "CharIndex"),
                ("CONCAT", "Concat"),
                ("CONCAT_WS", "Concat_Ws"),
                ("DIFFERENCE", "Difference"),
                ("FORMAT", "Format"),
                ("LEFT", "Left"),
                ("LEN", "Len"),
                ("LOWER", "Lower"),
                ("LTRIM", "LTrim"),
                ("NCHAR", "NChar"),
                ("PATINDEX", "PatIndex"),
                ("QUOTENAME", "QuoteName"),
                ("REPLACE", "Replace"),
                ("REPLICATE", "Replicate"),
                ("REVERSE", "Reverse"),
                ("RIGHT", "Right"),
                ("RTRIM", "RTrim"),
                ("SOUNDEX", "Soundex"),
                ("SPACE", "Space"),
                ("STR", "Str"),
                ("STRING_ESCAPE", "String_Escape"),
                ("STRING_SPLIT", "String_Split"),
                ("STUFF", "Stuff"),
                ("SUBSTRING", "Substring"),
                ("TRANSLATE", "Translate"),
                ("TRIM", "Trim"),
                ("UNICODE", "Unicode"),
                ("UPPER", "Upper"),
                ("DATALENGTH", "DataLength"),

                // Date & Time
                ("GETDATE", "GetDate"),
                ("GETUTCDATE", "GetUtcDate"),
                ("SYSDATETIME", "SysDateTime"),
                ("SYSUTCDATETIME", "SysUtcDateTime"),
                ("SYSDATETIMEOFFSET", "SysDateTimeOffset"),
                ("DATEADD", "DateAdd"),
                ("DATEDIFF", "DateDiff"),
                ("DATEDIFF_BIG", "DateDiff_Big"),
                ("DATEPART", "DatePart"),
                ("DATENAME", "DateName"),
                ("DATEFROMPARTS", "DateFromParts"),
                ("DATETIME2FROMPARTS", "DateTime2FromParts"),
                ("DATETIMEFROMPARTS", "DateTimeFromParts"),
                ("DATETIMEOFFSETFROMPARTS", "DateTimeOffsetFromParts"),
                ("SMALLDATETIMEFROMPARTS", "SmallDateTimeFromParts"),
                ("TIMEFROMPARTS", "TimeFromParts"),
                ("EOMONTH", "EoMonth"),
                ("ISDATE", "IsDate"),
                ("SWITCHOFFSET", "SwitchOffset"),
                ("TODATETIMEOFFSET", "ToDateTimeOffset"),
                ("YEAR", "Year"),
                ("MONTH", "Month"),
                ("DAY", "Day"),

                // Conversion
                ("CAST", "Cast"),
                ("CONVERT", "Convert"),
                ("PARSE", "Parse"),
                ("TRY_CAST", "Try_Cast"),
                ("TRY_CONVERT", "Try_Convert"),
                ("TRY_PARSE", "Try_Parse"),

                // Logical & Conditional
                ("ISNULL", "IsNull"),
                ("COALESCE", "Coalesce"),
                ("NULLIF", "NullIf"),
                ("IIF", "Iif"),
                ("CHOOSE", "Choose"),
                ("GREATEST", "Greatest"),
                ("LEAST", "Least"),

                // Mathematical
                ("ABS", "Abs"),
                ("ACOS", "Acos"),
                ("ASIN", "Asin"),
                ("ATAN", "Atan"),
                ("ATN2", "Atn2"),
                ("CEILING", "Ceiling"),
                ("COS", "Cos"),
                ("COT", "Cot"),
                ("DEGREES", "Degrees"),
                ("EXP", "Exp"),
                ("FLOOR", "Floor"),
                ("LOG", "Log"),
                ("LOG10", "Log10"),
                ("PI", "Pi"),
                ("POWER", "Power"),
                ("RADIANS", "Radians"),
                ("RAND", "Rand"),
                ("ROUND", "Round"),
                ("SIGN", "Sign"),
                ("SIN", "Sin"),
                ("SQRT", "Sqrt"),
                ("SQUARE", "Square"),
                ("TAN", "Tan"),

                // System & Metadata
                ("SCOPE_IDENTITY", "Scope_Identity"),
                ("IDENT_CURRENT", "Ident_Current"),
                ("IDENT_INCR", "Ident_Incr"),
                ("IDENT_SEED", "Ident_Seed"),
                ("OBJECT_ID", "Object_Id"),
                ("OBJECT_NAME", "Object_Name"),
                ("OBJECT_SCHEMA_NAME", "Object_Schema_Name"),
                ("OBJECT_DEFINITION", "Object_Definition"),
                ("OBJECTPROPERTY", "ObjectProperty"),
                ("OBJECTPROPERTYEX", "ObjectPropertyEx"),
                ("COLUMNPROPERTY", "ColumnProperty"),
                ("COL_LENGTH", "Col_Length"),
                ("COL_NAME", "Col_Name"),
                ("INDEXPROPERTY", "IndexProperty"),
                ("INDEXKEY_PROPERTY", "IndexKey_Property"),
                ("INDEX_COL", "Index_Col"),
                ("DATABASEPROPERTYEX", "DatabasePropertyEx"),
                ("DB_ID", "Db_Id"),
                ("DB_NAME", "Db_Name"),
                ("SCHEMA_ID", "Schema_Id"),
                ("SCHEMA_NAME", "Schema_Name"),
                ("TYPE_ID", "Type_Id"),
                ("TYPE_NAME", "Type_Name"),
                ("SERVERPROPERTY", "ServerProperty"),
                ("STATS_DATE", "Stats_Date"),
                ("USER_ID", "User_Id"),
                ("USER_NAME", "User_Name"),
                ("SUSER_ID", "SUser_Id"),
                ("SUSER_NAME", "SUser_Name"),
                ("SUSER_SID", "SUser_Sid"),
                ("SUSER_SNAME", "SUser_Sname"),
                ("ORIGINAL_LOGIN", "Original_Login"),
                ("ORIGINAL_DB_NAME", "Original_Db_Name"),
                ("HAS_PERMS_BY_NAME", "Has_Perms_By_Name"),
                ("IS_SRVROLEMEMBER", "Is_SrvRoleMember"),
                ("IS_MEMBER", "Is_Member"),
                ("IS_ROLEMEMBER", "Is_RoleMember"),
                ("APP_NAME", "App_Name"),
                ("HOST_NAME", "Host_Name"),
                ("HOST_ID", "Host_Id"),
                ("NEWID", "NewId"),
                ("NEWSEQUENTIALID", "NewSequentialId"),
                ("XACT_STATE", "Xact_State"),
                ("ERROR_MESSAGE", "Error_Message"),
                ("ERROR_NUMBER", "Error_Number"),
                ("ERROR_SEVERITY", "Error_Severity"),
                ("ERROR_STATE", "Error_State"),
                ("ERROR_LINE", "Error_Line"),
                ("ERROR_PROCEDURE", "Error_Procedure"),
                ("CURRENT_TIMESTAMP", "Current_Timestamp"),
                ("CURRENT_USER", "Current_User"),
                ("SESSION_USER", "Session_User"),
                ("SYSTEM_USER", "System_User"),
                ("CHECKSUM", "Checksum"),
                ("BINARY_CHECKSUM", "Binary_Checksum"),

                // JSON
                ("ISJSON", "IsJson"),
                ("JSON_VALUE", "Json_Value"),
                ("JSON_QUERY", "Json_Query"),
                ("JSON_MODIFY", "Json_Modify"),
                ("JSON_PATH_EXISTS", "Json_Path_Exists"),
                ("JSON_ARRAY", "Json_Array"),
                ("JSON_OBJECT", "Json_Object"),

                // System Stat & Scalar Variables
                ("ROWCOUNT", "RowCount"),
                ("TRANCOUNT", "TranCount"),
                ("SPID", "Spid"),
                ("ERROR", "Error"),
                ("SERVERNAME", "ServerName"),
                ("SERVICENAME", "ServiceName"),
                ("VERSION", "Version"),
                ("FETCH_STATUS", "Fetch_Status"),
                ("CURSOR_ROWS", "Cursor_Rows")
            };

            foreach (var fn in functions)
            {
                if (!_tokenDictionary.ContainsKey(fn.word))
                {
                    _tokenDictionary[fn.word] = (SqlWordCategory.BuiltInFunction, fn.pascal);
                }
            }

            // 4. PRE-LOADED SYSTEM STORED PROCEDURES & SYSTEM FUNCTIONS (sp_*, xp_*, fn_*)
            // Preserves standard canonical SQL Server system definitions without altering casing
            var systemProcsAndFuncs = new[]
            {
                // Standard System Stored Procedures
                "sp_executesql", "sp_send_dbmail", "sp_help", "sp_helptext", "sp_who", "sp_who2",
                "sp_spaceused", "sp_updatestats", "sp_rename", "sp_recompile", "sp_refreshview",
                "sp_refreshtrigger", "sp_refreshsqlmodule", "sp_depends", "sp_tables", "sp_columns",
                "sp_fkeys", "sp_pkeys", "sp_statistics", "sp_server_info", "sp_databases",
                "sp_datatype_info", "sp_special_columns", "sp_sproc_columns", "sp_stored_procedures",
                "sp_table_privileges", "sp_column_privileges", "sp_addextendedproperty",
                "sp_updateextendedproperty", "sp_dropextendedproperty", "sp_configure",
                "sp_msforeachtable", "sp_msforeachdb", "sp_add_job", "sp_add_jobstep",
                "sp_add_jobserver", "sp_add_jobschedule", "sp_delete_job", "sp_delete_jobstep",
                "sp_delete_jobserver", "sp_delete_jobschedule", "sp_start_job", "sp_stop_job",
                "sp_reset_connection", "sp_attach_db", "sp_detach_db", "sp_attach_single_file_db",
                "sp_cycle_errorlog", "sp_autostats", "sp_clean_db_file_free_space", "sp_clean_db_free_space",
                "sp_create_plan_guide", "sp_control_plan_guide", "sp_control_dbmasterkey_password",
                "sp_change_users_login", "sp_changeobjectowner", "sp_changedbowner", "sp_grantlogin",
                "sp_revokelogin", "sp_denylogin", "sp_defaultdb", "sp_defaultlanguage", "sp_addlogin",
                "sp_droplogin", "sp_addrole", "sp_droprole", "sp_addrolemember", "sp_droprolemember",
                "sp_addsrvrolemember", "sp_dropsrvrolemember", "sp_addapprole", "sp_setapprole",
                "sp_dropapprole", "sp_approlepassword", "sp_helpuser", "sp_helprole", "sp_helprolemember",
                "sp_helpsrvrole", "sp_helpsrvrolemember", "sp_helplogins", "sp_helpdb", "sp_helpfile",
                "sp_helpfilegroup", "sp_helpindex", "sp_helpconstraint", "sp_helptrigger", "sp_helpstats",
                "sp_xml_preparedocument", "sp_xml_removedocument", "sp_cursoropen", "sp_cursorclose",
                "sp_cursorfetch", "sp_cursor", "sp_cursoroption", "sp_cursorexecute", "sp_cursorprepare",
                "sp_cursorunprepare", "sp_prepare", "sp_execute", "sp_unprepare", "sp_trace_create",
                "sp_trace_setevent", "sp_trace_setfilter", "sp_trace_setstatus", "sp_trace_generateevent",
                "sp_fulltext_database", "sp_fulltext_catalog", "sp_fulltext_table", "sp_fulltext_column",
                "sp_fulltext_service", "sp_indexoption", "sp_tableoption", "sp_bindrule", "sp_unbindrule",
                "sp_binddefault", "sp_unbinddefault", "sp_resync_targetserver", "sp_post_msx_hook",
                "sp_grant_login_to_subsystem", "sp_revoke_login_from_subsystem", "sp_verify_job_identifiers",
                "sp_manage_jobs_by_login", "sp_help_job", "sp_help_jobstep", "sp_help_jobserver",
                "sp_help_jobschedule", "sp_help_jobhistory", "sp_help_jobactivity", "sp_help_alert",
                "sp_help_notification", "sp_help_operator", "sp_help_targetserver", "sp_help_targetservergroup",
                "sp_add_alert", "sp_delete_alert", "sp_update_alert", "sp_add_notification",
                "sp_delete_notification", "sp_update_notification", "sp_add_operator", "sp_delete_operator",
                "sp_update_operator", "sp_add_category", "sp_delete_category", "sp_update_category",
                "sp_add_targetservergroup", "sp_delete_targetservergroup", "sp_add_targetsvr_to_multiserver",
                "sp_delete_targetserver",

                // Extended Stored Procedures
                "xp_cmdshell", "xp_fixeddrives", "xp_fileexist", "xp_dirtree", "xp_subdirs",
                "xp_logininfo", "xp_readerrorlog", "xp_enumerrorlogs", "xp_instance_regread",
                "xp_instance_regwrite", "xp_regread", "xp_regwrite", "xp_regdeletevalue",
                "xp_regdeletekey", "xp_ss_sendmail", "xp_sprintf", "xp_msver", "xp_logevent",
                "xp_sqlagent_notify", "xp_sqlagent_enum_jobs", "xp_sqlagent_is_starting", "xp_qv",

                // System Functions
                "fn_my_permissions", "fn_virtualfilestats", "fn_trace_gettable", "fn_trace_getinfo",
                "fn_trace_geteventinfo", "fn_trace_getfilterinfo", "fn_dblog", "fn_dump_dblog",
                "fn_get_sql", "fn_listextendedproperty", "fn_servershareddrives", "fn_helpcollations",
                "fn_validate_plan_guide", "fn_get_audit_file", "fn_cdc_get_all_changes",
                "fn_cdc_get_net_changes", "fn_cdc_map_lsn_to_time", "fn_cdc_map_time_to_lsn",
                "fn_cdc_get_min_lsn", "fn_cdc_get_max_lsn", "fn_cdc_increment_lsn",
                "fn_cdc_decrement_lsn", "fn_cdc_is_bit_set", "fn_cdc_has_column_changed",
                "fn_syscollector_get_package_parameters"
            };

            foreach (var proc in systemProcsAndFuncs)
            {
                _tokenDictionary[proc] = (SqlWordCategory.SystemProcedureOrFunction, proc);
            }
        }
    }
}
