namespace Aquarium.TextEditor;

internal static class SyntaxHighlight
{
    // Lexical preview only: it neither validates nor executes configuration or code.
    internal static void Apply(RichTextBox view, string extension)
    {
        string text = view.Text; if (text.Length > 65536) return;
        view.SelectAll(); view.SelectionColor = Color.FromArgb(241, 247, 255);
        int spans = 0;
        void ColorSpan(int start, int length, Color color)
        { if (++spans > 4096) return; view.Select(start, length); view.SelectionColor = color; }
        var teal = Color.FromArgb(103, 232, 211); var purple = Color.FromArgb(211, 172, 255); var blue = Color.FromArgb(146, 206, 255);
        for (int i = 0; i < text.Length && spans < 4096; i++)
        {
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (text[i] == '"')
                {
                    int start = i++;
                    while (i < text.Length) { if (text[i] == '\\') { i += 2; continue; } if (text[i++] == '"') break; }
                    i = Math.Min(i, text.Length); ColorSpan(start, i - start, purple); i--;
                }
                else if (char.IsDigit(text[i]) || text[i] == '-')
                { int start = i++; while (i < text.Length && "0123456789.eE+-".Contains(text[i])) i++; ColorSpan(start, i - start, blue); i--; }
                else if (char.IsLetter(text[i]))
                { int start = i++; while (i < text.Length && char.IsLetter(text[i])) i++; if (text[start..i] is "true" or "false" or "null") ColorSpan(start, i - start, teal); i--; }
            }
            else if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) && text[i] == '<')
            { int start = i++; while (i < text.Length && text[i] != '>') i++; i = Math.Min(i + 1, text.Length); ColorSpan(start, i - start, teal); i--; }
            else if ((extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase)) && (i == 0 || text[i - 1] == '\n'))
            {
                int end = text.IndexOf('\n', i); if (end < 0) end = text.Length;
                int first = i; while (first < end && char.IsWhiteSpace(text[first])) first++;
                if (first < end && text[first] is ';' or '#') ColorSpan(first, end - first, Color.Gray);
                else if (first < end && text[first] == '[') ColorSpan(first, end - first, purple);
                else { int equals = text.IndexOf('=', i, end - i); if (equals >= 0) ColorSpan(i, equals - i, blue); }
                i = end;
            }
        }
        view.Select(0, 0); view.ClearUndo();
    }
}
