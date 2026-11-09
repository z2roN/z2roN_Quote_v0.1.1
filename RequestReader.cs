using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace ZQuote;

public class ColMap
{
    public int HeaderRow = -1, Name = -1, Spec = -1, Unit = -1, Qty = -1, Price = -1, Note = -1, Barcode = -1;
    public bool Ok => Name >= 0 && (Qty >= 0 || Unit >= 0);
}

/// 견적의뢰 엑셀(.xls/.xlsx/.csv)의 첫 시트를 읽어 품목 목록으로 바꾼다.
public static class RequestReader
{
    public static List<string[]> ReadRows(string file)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var rows = new List<string[]>();
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var cfg = new ExcelReaderConfiguration { FallbackEncoding = Encoding.GetEncoding(949) };
        using IExcelDataReader r = file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ExcelReaderFactory.CreateCsvReader(fs, cfg) : ExcelReaderFactory.CreateReader(fs, cfg);
        while (r.Read() && rows.Count < 20000)
        {
            var a = new string[r.FieldCount];
            for (int i = 0; i < a.Length; i++) a[i] = Str(r.GetValue(i));
            rows.Add(a);
        }
        return rows;
    }

    static string Str(object v)
    {
        if (v == null) return "";
        if (v is double d) return d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("0.####", CultureInfo.InvariantCulture);
        if (v is DateTime t) return t.ToString("yyyy-MM-dd");
        return Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
    }

    public static string Norm(string s) => (s ?? "").Replace(" ", "").Replace("　", "").Replace("\n", "").ToLowerInvariant();

    static readonly string[] NameKeys = { "품목명", "품명", "상품명", "제품명", "물품명", "자재명", "품목", "상품", "제품", "이름" };
    static readonly string[] UnitKeys = { "단위" };
    static readonly string[] QtyKeys = { "주문수량", "수량" };
    static readonly string[] PriceKeys = { "우리단가", "견적단가", "판매단가", "판매가", "단가" };
    static readonly string[] NoteKeys = { "비고", "메모" };
    static readonly string[] BarKeys = { "바코드", "barcode", "barcd" };
    static readonly string[] SpecKeys = { "규격", "사양" };

    static int Find(string[] cells, string[] keys, HashSet<int> used)
    {
        foreach (var k in keys)
            for (int i = 0; i < cells.Length; i++)
                if (!used.Contains(i) && Norm(cells[i]) == k) return i;
        foreach (var k in keys)
            for (int i = 0; i < cells.Length; i++)
                if (!used.Contains(i) && Norm(cells[i]).Contains(k)) return i;
        return -1;
    }

    /// 머리글 행과 열 위치를 자동으로 찾는다. (위쪽 40행 안에서)
    public static ColMap Detect(List<string[]> rows)
    {
        for (int r = 0; r < Math.Min(rows.Count, 40); r++)
        {
            var c = rows[r];
            var used = new HashSet<int>();
            var m = new ColMap { HeaderRow = r };
            m.Barcode = Find(c, BarKeys, used); if (m.Barcode >= 0) used.Add(m.Barcode);
            m.Name = Find(c, NameKeys, used); if (m.Name >= 0) used.Add(m.Name);
            m.Spec = Find(c, SpecKeys, used); if (m.Spec >= 0) used.Add(m.Spec);
            m.Unit = Find(c, UnitKeys, used); if (m.Unit >= 0) used.Add(m.Unit);
            m.Qty = Find(c, QtyKeys, used); if (m.Qty >= 0) used.Add(m.Qty);
            m.Price = Find(c, PriceKeys, used); if (m.Price >= 0) used.Add(m.Price);
            m.Note = Find(c, NoteKeys, used);
            if (m.Ok) return m;
        }
        return new ColMap();
    }

    static string Cell(string[] r, int i) => i >= 0 && i < r.Length ? r[i] : "";

    public static double Num(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = new string(s.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    static bool IsNoise(string[] r, int nameCol)
    {
        for (int i = 0; i < Math.Min(r.Length, 4); i++)
        {
            var n = Norm(r[i]);
            if (n == "합계" || n == "소계" || n == "총계" || n == "합계금액" || n.Contains("見積") || n == "견적서") return true;
        }
        var nm = Norm(Cell(r, nameCol));
        return nm == "품목" || nm == "품명" || nm == "상품명" || nm == "품목명";   // 반복되는 머리글
    }

    public static List<QItem> Parse(List<string[]> rows, ColMap m)
    {
        var list = new List<QItem>();
        for (int r = m.HeaderRow + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string name = Cell(row, m.Name).Trim();
            if (name == "" || name == "0") continue;
            if (IsNoise(row, m.Name)) continue;
            string spec = Cell(row, m.Spec).Trim();
            if (spec != "" && spec != "0" && !name.Contains(spec)) name += " " + spec;
            string unit = Cell(row, m.Unit).Trim(); if (unit == "0") unit = "";
            string note = Cell(row, m.Note).Trim(); if (note == "0") note = "";
            string bc = Cell(row, m.Barcode).Trim(); if (bc == "0") bc = "";
            list.Add(new QItem { Name = name, Unit = unit, Qty = Num(Cell(row, m.Qty)), OurPrice = Num(Cell(row, m.Price)), Note = note, Barcode = bc });
        }
        return list;
    }
}
