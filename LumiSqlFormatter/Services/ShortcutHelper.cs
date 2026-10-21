using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace LumiSqlFormatter.Services
{
    public static class ShortcutHelper
    {
        public static bool TryParseShortcut(string shortcutText, out ModifierKeys modifiers, out Key key)
        {
            modifiers = ModifierKeys.None;
            key = Key.None;

            if (string.IsNullOrWhiteSpace(shortcutText))
                return false;

            string[] parts = shortcutText.Split(new[] { '+', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                }
                else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                }
                else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                }
                else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(part, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                }
                else
                {
                    if (Enum.TryParse<Key>(part, true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                    {
                        if (Enum.TryParse<Key>(part.ToUpperInvariant(), true, out var letterKey))
                        {
                            key = letterKey;
                        }
                    }
                }
            }

            return key != Key.None;
        }

        public static string FormatShortcut(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None)
                return string.Empty;

            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                parts.Add("Win");

            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        public static bool IsShortcutActive(string shortcutText)
        {
            if (!TryParseShortcut(shortcutText, out var reqModifiers, out var reqKey))
                return false;

            if (reqKey == Key.None)
                return false;

            var currentModifiers = Keyboard.Modifiers;
            if (currentModifiers != reqModifiers)
                return false;

            return Keyboard.IsKeyDown(reqKey);
        }
    }
}
