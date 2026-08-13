using System.Globalization;

namespace SuperSocketLite.LoadTest.Report;

/// <summary>
/// 헤더가 있는 CSV를 이름으로 읽는 최소한의 표입니다.
/// </summary>
/// <remarks>
/// 컬럼을 이름으로 찾으므로 실행마다 컬럼이 늘어나도 읽는 쪽이 깨지지 않습니다.
/// phase처럼 나중에 추가된 컬럼이 없는 예전 실행도 그대로 읽힙니다.
/// </remarks>
public sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    private CsvTable(Dictionary<string, int> columns, List<string[]> rows)
    {
        _columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<string[]> Rows { get; }

    public bool HasColumn(string name) => _columns.ContainsKey(name);

    public static CsvTable? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        // 실행 중인 프로세스가 같은 파일에 쓰고 있을 수 있으므로 공유 모드로 연다.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var headerLine = reader.ReadLine();
        if (headerLine is null)
            return null;

        var header = SplitLine(headerLine);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
            columns[header[i]] = i;

        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            rows.Add(SplitLine(line));
        }

        return new CsvTable(columns, rows);
    }

    public string GetString(string[] row, string column, string fallback = "")
    {
        if (!_columns.TryGetValue(column, out var index) || index >= row.Length)
            return fallback;

        return row[index];
    }

    public double GetDouble(string[] row, string column, double fallback = 0)
    {
        var text = GetString(row, column);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public long GetLong(string[] row, string column, long fallback = 0)
    {
        var text = GetString(row, column);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
