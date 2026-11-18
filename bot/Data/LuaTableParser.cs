using System.Globalization;
using System.Text;
namespace HerbBot.Data;

/// <summary>Только данные SavedVariables: присваивания и таблицы; вызовы, функции и выражения запрещены.</summary>
public sealed class LuaTableParser
{
    private string _text = "";
    private int _at;
    public Dictionary<string, object?> Parse(string text)
    {
        if (text.Length > 32 * 1024 * 1024) throw new FormatException("Lua-файл превышает 32 МиБ.");
        _text = text; _at = 0;
        var globals = new Dictionary<string, object?>();
        while (Skip())
        {
            string name = Identifier(); Expect('=');
            if (!globals.TryAdd(name, Value(0))) throw Error("Повторное присваивание " + name);
            Take(';');
        }
        return globals;
    }
    private object? Value(int depth)
    {
        if (depth > 64) throw Error("Слишком глубокая таблица");
        Skip();
        if (Peek() == '{') return Table(depth + 1);
        if (Peek() is '\'' or '"') return Quoted();
        if (char.IsDigit(Peek()) || Peek() is '-' or '+' or '.')
        {
            int start = _at;
            while (_at < _text.Length && "0123456789.eE+-".Contains(_text[_at])) _at++;
            if (!double.TryParse(_text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
                throw Error("Неверное число");
            return n;
        }
        return Identifier() switch { "true" => true, "false" => false, "nil" => null, _ => throw Error("Выражения Lua не поддерживаются") };
    }
    private Dictionary<string, object?> Table(int depth)
    {
        Expect('{'); var table = new Dictionary<string, object?>(); int index = 1;
        while (!Take('}'))
        {
            Skip(); string key; object? value;
            if (Take('['))
            {
                object? k = Value(depth); Expect(']'); Expect('=');
                key = k switch { string s => s, double n => n.ToString("R", CultureInfo.InvariantCulture), _ => throw Error("Неверный ключ") };
                value = Value(depth);
            }
            else if (char.IsLetter(Peek()) || Peek() == '_')
            {
                int saved = _at; string name = Identifier();
                if (Take('=')) { key = name; value = Value(depth); }
                else { _at = saved; key = (index++).ToString(CultureInfo.InvariantCulture); value = Value(depth); }
            }
            else { key = (index++).ToString(CultureInfo.InvariantCulture); value = Value(depth); }
            if (!table.TryAdd(key, value)) throw Error("Повторный ключ " + key);
            if (Take(',') || Take(';')) continue;
            Expect('}'); break;
        }
        return table;
    }
    private string Identifier()
    {
        Skip(); int start = _at;
        if (!char.IsLetter(Peek()) && Peek() != '_') throw Error("Ожидается идентификатор");
        while (char.IsLetterOrDigit(Peek()) || Peek() == '_') _at++;
        return _text[start.._at];
    }
    private string Quoted()
    {
        char quote = _text[_at++]; var b = new StringBuilder();
        while (_at < _text.Length)
        {
            char c = _text[_at++]; if (c == quote) return b.ToString();
            if (c == '\\')
            {
                if (_at == _text.Length) throw Error("Оборванная строка");
                c = _text[_at++];
                c = c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '\'' => '\'', '"' => '"', _ => throw Error("Неизвестная escape-последовательность") };
            }
            b.Append(c);
        }
        throw Error("Незакрытая строка");
    }
    private bool Skip()
    {
        while (_at < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_at]) || _text[_at] == '\uFEFF') { _at++; continue; }
            if (_at + 1 < _text.Length && _text[_at..(_at + 2)] == "--")
            {
                _at += 2;
                if (_at + 1 < _text.Length && _text[_at..(_at + 2)] == "[[")
                {
                    int end = _text.IndexOf("]]", _at + 2, StringComparison.Ordinal);
                    if (end < 0) throw Error("Незакрытый комментарий"); _at = end + 2;
                }
                else while (_at < _text.Length && _text[_at] != '\n') _at++;
                continue;
            }
            return true;
        }
        return false;
    }
    private char Peek() => _at < _text.Length ? _text[_at] : '\0';
    private bool Take(char c) { Skip(); if (Peek() != c) return false; _at++; return true; }
    private void Expect(char c) { if (!Take(c)) throw Error("Ожидается " + c); }
    private FormatException Error(string message) => new($"{message}, смещение {_at}.");
}
