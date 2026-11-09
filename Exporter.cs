using ClosedXML.Excel;

namespace ZQuote;

/// 견적서(見積書) 엑셀 만들기. 업체마다 한 시트(또는 한 파일).
public static class Exporter
{
    static string SafeSheet(string n, HashSet<string> used)
    {
        var bad = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        string s = new string((n ?? "").Select(ch => bad.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (s == "") s = "견적";
        if (s.Length > 28) s = s.Substring(0, 28);
        string r = s; int i = 2;
        while (!used.Add(r.ToLowerInvariant())) r = s + "_" + i++;
        return r;
    }

    public static string SafeFile(string n)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string((n ?? "").Select(ch => bad.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return s == "" ? "견적" : s;
    }

    static Func<QItem, double> PriceOf(Company c) =>
        c.Kind == "our" ? (it => it.OurPrice) : (it => it.Prices.TryGetValue(c.Id, out var p) ? p : 0);

    /// who: 내보낼 업체들 (우리 업체 포함 가능). 한 파일에 업체별 시트.
    public static void ExportWorkbook(Db db, Quote q, IEnumerable<Company> who, string path)
    {
        var keep = new List<IDisposable>();
        try
        {
            using var wb = new XLWorkbook();
            var used = new HashSet<string>();
            foreach (var c in who)
                BuildSheet(wb.Worksheets.Add(SafeSheet(c.Name, used)), q, c, db.PerPage, db.VatText, keep);
            wb.SaveAs(path);
        }
        finally { foreach (var d in keep) d.Dispose(); }
    }

    /// 업체마다 따로 파일로 저장. 만든 파일 경로 목록을 돌려줌.
    public static List<string> ExportSeparate(Db db, Quote q, IEnumerable<Company> who, string dir)
    {
        var res = new List<string>();
        foreach (var c in who)
        {
            string p = Path.Combine(dir, $"{SafeFile(q.Recipient)}_{SafeFile(c.Name)}_{q.Date:yyyyMMdd}.xlsx");
            int n = 2;
            while (File.Exists(p)) p = Path.Combine(dir, $"{SafeFile(q.Recipient)}_{SafeFile(c.Name)}_{q.Date:yyyyMMdd}_{n++}.xlsx");
            ExportWorkbook(db, q, new[] { c }, p);
            res.Add(p);
        }
        return res;
    }

    static void BuildSheet(IXLWorksheet ws, Quote q, Company c, int perPage, string vatText, List<IDisposable> keep)
    {
        var price = PriceOf(c);
        ws.Style.Font.FontName = "맑은 고딕";
        ws.Style.Font.FontSize = 11;
        double[] widths = { 5.5, 40, 7, 8, 14, 15, 17 };
        for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];

        int n = q.Items.Count;
        int pages = Math.Max(1, (n + perPage - 1) / perPage);
        int pageRows(int p) => 11 + perPage + (pages == 1 ? 1 : (p == pages - 1 ? 2 : 1));
        int start(int p) { int r = 1; for (int i = 0; i < p; i++) r += pageRows(i); return r; }
        int lastRow = start(pages - 1) + pageRows(pages - 1) - 1;
        string totalCell = "F" + lastRow;                      // 마지막 쪽의 맨 아래 줄이 합계
        var subCells = new List<string>();
        for (int p = 0; p < pages; p++) subCells.Add("F" + (start(p) + 10 + perPage + 1));

        for (int p = 0; p < pages; p++)
        {
            int r = start(p);
            int R1 = r + 2, hdr = r + 10;
            // 제목
            ws.Range(r, 2, r, 7).Merge();
            var t = ws.Cell(r, 2); t.Value = "見   積   書";
            t.Style.Font.FontSize = 36; t.Style.Font.Bold = true; t.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; t.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(r).Height = 51;
            ws.Row(r + 1).Height = 8;
            // 수신처
            ws.Range(R1, 1, R1 + 1, 3).Merge();
            var rc = ws.Cell(R1, 1); rc.Value = q.Recipient;
            rc.Style.Font.FontSize = 14; rc.Style.Font.Bold = true; rc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; rc.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Range(R1, 1, R1 + 1, 3).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            // 공급자
            ws.Range(R1, 5, R1, 7).Merge();
            var nm = ws.Cell(R1, 5); nm.Value = c.Name; nm.Style.Font.FontSize = 14; nm.Style.Font.Bold = true;
            ws.Cell(R1 + 1, 5).Value = "사업자등록번호 " + c.BizNo; ws.Cell(R1 + 1, 5).Style.Font.Bold = true;
            var ceo = ws.Cell(R1 + 1, 7); ceo.Value = string.IsNullOrWhiteSpace(c.Ceo) ? "" : "대표:" + c.Ceo; ceo.Style.Font.Bold = true; ceo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(R1 + 2, 5, R1 + 2, 7).Merge(); ws.Cell(R1 + 2, 5).Value = string.IsNullOrWhiteSpace(c.BizType) ? "" : "업태 : " + c.BizType;
            ws.Range(R1 + 3, 5, R1 + 3, 7).Merge(); ws.Cell(R1 + 3, 5).Value = c.Address ?? "";
            string tf = (string.IsNullOrWhiteSpace(c.Tel) ? "" : "T " + c.Tel) + (string.IsNullOrWhiteSpace(c.Fax) ? "" : (string.IsNullOrWhiteSpace(c.Tel) ? "" : " / ") + "F " + c.Fax);
            ws.Range(R1 + 4, 5, R1 + 4, 7).Merge(); ws.Cell(R1 + 4, 5).Value = tf;
            // 인사말 / 날짜
            ws.Range(R1 + 2, 2, R1 + 2, 3).Merge(); ws.Cell(R1 + 2, 2).Value = "귀사를 위하여 다음과 같이";
            ws.Range(R1 + 3, 2, R1 + 3, 3).Merge(); ws.Cell(R1 + 3, 2).Value = "견적을 제시합니다.";
            ws.Range(R1 + 4, 2, R1 + 4, 3).Merge(); ws.Cell(R1 + 4, 2).Value = q.Date.ToString("yyyy년  M월  d일");
            ws.Row(R1 + 5).Height = 8;
            // 합계금액 (모든 쪽에 총 합계 표시)
            var lb = ws.Cell(R1 + 6, 2); lb.Value = "합계금액"; lb.Style.Font.Bold = true; lb.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(R1 + 6, 3, R1 + 6, 5).Merge();
            var am = ws.Cell(R1 + 6, 3); am.FormulaA1 = totalCell; am.Style.NumberFormat.Format = "[DBNum4][$-412]General"; am.Style.Font.Bold = true; am.Style.Font.FontSize = 12; am.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(R1 + 6, 6).Value = "원정"; ws.Cell(R1 + 6, 6).Style.Font.Bold = true;
            ws.Cell(R1 + 6, 7).Value = vatText; ws.Cell(R1 + 6, 7).Style.Font.FontSize = 9;
            ws.Row(R1 + 7).Height = 7;
            // 도장
            if (c.Stamp != null && c.Stamp.Length > 0)
            {
                try
                {
                    var ms = new MemoryStream(c.Stamp); keep.Add(ms);
                    ws.AddPicture(ms, XLPictureFormat.Png).MoveTo(ws.Cell(R1, 7)).WithSize(52, 52);
                }
                catch { }
            }
            // 품목 표
            string[] heads = { "순번", "품   목", "단위", "수량", "단  가", "금  액", "비  고" };
            for (int i = 0; i < 7; i++)
            {
                var h = ws.Cell(hdr, i + 1); h.Value = heads[i]; h.Style.Font.Bold = true;
                h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; h.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEE, 0xEE, 0xEE);
            }
            ws.Row(hdr).Height = 22;
            for (int i = 0; i < perPage; i++)
            {
                int row = hdr + 1 + i, idx = p * perPage + i;
                ws.Row(row).Height = 20;
                ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.##";
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 2).Style.Alignment.WrapText = true; ws.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                if (idx < n)
                {
                    var it = q.Items[idx];
                    ws.Cell(row, 1).Value = idx + 1;
                    ws.Cell(row, 2).Value = it.Name;
                    ws.Cell(row, 3).Value = it.Unit;
                    ws.Cell(row, 4).Value = it.Qty;
                    ws.Cell(row, 5).Value = price(it);
                    ws.Cell(row, 6).FormulaA1 = $"D{row}*E{row}";
                    ws.Cell(row, 7).Value = it.Note;
                    if ((it.Name ?? "").Length > 34) ws.Row(row).Height = 32;   // 긴 품목명은 두 줄로
                }
            }
            int sub = hdr + perPage + 1;
            ws.Row(sub).Height = 24;
            ws.Range(sub, 1, sub, 5).Merge();
            var sl = ws.Cell(sub, 1); sl.Value = pages == 1 ? "합            계" : "소            계";
            sl.Style.Font.Bold = true; sl.Style.Font.FontSize = 13; sl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(sub, 6).FormulaA1 = $"SUM(F{hdr + 1}:F{hdr + perPage})";
            ws.Cell(sub, 6).Style.NumberFormat.Format = "#,##0"; ws.Cell(sub, 6).Style.Font.Bold = true;
            int last = sub;
            if (pages > 1 && p == pages - 1)
            {
                last = sub + 1;
                ws.Row(last).Height = 26;
                ws.Range(last, 1, last, 5).Merge();
                var tl = ws.Cell(last, 1); tl.Value = "합            계"; tl.Style.Font.Bold = true; tl.Style.Font.FontSize = 14; tl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(last, 6).FormulaA1 = string.Join("+", subCells);
                ws.Cell(last, 6).Style.NumberFormat.Format = "#,##0"; ws.Cell(last, 6).Style.Font.Bold = true;
            }
            // 표 테두리
            var tbl = ws.Range(hdr, 1, last, 7);
            tbl.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tbl.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            ws.Range(hdr + 1, 1, hdr + perPage, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(sub, 1, sub, 7).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            if (p < pages - 1) ws.PageSetup.AddHorizontalPageBreak(last);
        }
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.Margins.Top = 0.5; ws.PageSetup.Margins.Bottom = 0.5; ws.PageSetup.Margins.Left = 0.5; ws.PageSetup.Margins.Right = 0.5;
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.Scale = 85;   // 수동 쪽 나눔이 유지되도록 '배율'만 사용
        ws.ShowGridLines = false;
    }
}
