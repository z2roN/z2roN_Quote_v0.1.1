using System.Data;
using ClosedXML.Excel;

namespace ZQuote;

public partial class MainForm
{
    readonly DataGridView gList = new();
    readonly TextBox tSearch = new() { Width = 220, PlaceholderText = "수신처/파일명 검색" };
    readonly DataGridView gMaster = new();
    readonly TextBox tMasterSearch = new() { Width = 240, PlaceholderText = "바코드 또는 품목 검색" };
    readonly Label lblMaster = new() { AutoSize = true, ForeColor = Ui.BlueDark };
    bool masterLoading;

    // ---------------- 견적 목록 ----------------
    TabPage BuildListPage()
    {
        var page = new TabPage("견적 목록 (조회·수정·삭제)");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8, 7, 8, 5), BackColor = Ui.BlueSoft, WrapContents = false };
        tSearch.BorderStyle = BorderStyle.FixedSingle; tSearch.Margin = new Padding(3, 5, 8, 0);
        tSearch.TextChanged += (_, _) => RefreshList();
        bar.Controls.Add(tSearch);
        Button B(string t, EventHandler h, int k = 0) { var b = Ui.Btn(t, h, k); b.Margin = new Padding(3, 0, 3, 0); return b; }
        bar.Controls.Add(B("열기 / 수정", (_, _) => OpenSelectedQuote(), 1));
        bar.Controls.Add(B("엑셀 내려받기", (_, _) => ExportSelectedQuote()));
        bar.Controls.Add(B("삭제", (_, _) => DeleteSelectedQuote(), 2));
        bar.Controls.Add(B("새로 고침", (_, _) => RefreshList()));
        Ui.StyleGrid(gList);
        gList.Dock = DockStyle.Fill; gList.ReadOnly = true; gList.AllowUserToAddRows = false; gList.AllowUserToDeleteRows = false;
        gList.SelectionMode = DataGridViewSelectionMode.FullRowSelect; gList.MultiSelect = false; gList.RowHeadersVisible = false;
        gList.DataError += (_, e) => e.ThrowException = false;
        gList.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedQuote(); };
        page.Controls.Add(gList); page.Controls.Add(bar);
        return page;
    }

    void RefreshList()
    {
        try
        {
            gList.DataSource = db.ListQuotes(tSearch.Text.Trim());
            foreach (DataGridViewColumn c in gList.Columns)
            {
                if (c.Name == "우리합계") { c.DefaultCellStyle.Format = "#,##0"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
                if (c.Name == "수신처") c.Width = 280; else if (c.Name == "번호" || c.Name == "품목수" || c.Name == "상태") c.Width = 70; else c.Width = 140;
            }
        }
        catch (Exception e) { MessageBox.Show(e.Message); }
    }

    long? SelectedQuoteId()
    {
        if (gList.CurrentRow == null) { MessageBox.Show("목록에서 견적을 고르세요."); return null; }
        return Convert.ToInt64(gList.CurrentRow.Cells["번호"].Value);
    }

    void OpenSelectedQuote()
    {
        var id = SelectedQuoteId(); if (id == null) return;
        if (!ConfirmDiscard()) return;
        var q = db.LoadQuote(id.Value);
        if (q == null) { MessageBox.Show("견적을 찾을 수 없습니다."); return; }
        BindQuote(q, !q.Completed);   // 작성중이면 최신 업체 정보로, 완료된 견적은 만든 당시 그대로
        dirty = false;
        tabs.SelectedIndex = 0;
    }

    void ExportSelectedQuote()
    {
        var id = SelectedQuoteId(); if (id == null) return;
        var q = db.LoadQuote(id.Value);
        if (q == null) return;
        DoExport(q);
    }

    void DeleteSelectedQuote()
    {
        var id = SelectedQuoteId(); if (id == null) return;
        string status = Convert.ToString(gList.CurrentRow.Cells["상태"].Value);
        string name = Convert.ToString(gList.CurrentRow.Cells["수신처"].Value);
        if (status == "완료")
        {
            if (!Ui.AskAdmin(db, this, $"완료된 견적 '{name}' 을(를) 삭제합니다. 관리자만 삭제할 수 있습니다.")) return;
        }
        if (MessageBox.Show($"'{name}' 견적을 삭제할까요? 되돌릴 수 없습니다.", "삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { db.DeleteQuote(id.Value); if (cur.Id == id.Value) { dirty = false; NewQuote(true); } RefreshList(); }
        catch (Exception e) { MessageBox.Show(e.Message); }
    }

    // ---------------- 품목 · 바코드 수집 ----------------
    TabPage BuildMasterPage()
    {
        var page = new TabPage("품목 · 바코드");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8, 7, 8, 5), BackColor = Ui.BlueSoft, WrapContents = false };
        tMasterSearch.BorderStyle = BorderStyle.FixedSingle; tMasterSearch.Margin = new Padding(3, 5, 8, 0);
        tMasterSearch.TextChanged += (_, _) => RefreshMaster();
        bar.Controls.Add(tMasterSearch);
        Button B(string t, EventHandler h, int k = 0) { var b = Ui.Btn(t, h, k); b.Margin = new Padding(3, 0, 3, 0); return b; }
        bar.Controls.Add(B("엑셀에서 수집...", (_, _) => CollectFromExcel(), 1));
        bar.Controls.Add(B("엑셀로 내보내기", (_, _) => ExportMaster()));
        bar.Controls.Add(B("선택 삭제", (_, _) => DeleteMaster(), 2));
        bar.Controls.Add(B("새로 고침", (_, _) => RefreshMaster()));
        lblMaster.Margin = new Padding(12, 9, 3, 0); bar.Controls.Add(lblMaster);
        Ui.StyleGrid(gMaster);
        gMaster.Dock = DockStyle.Fill; gMaster.AllowUserToAddRows = false; gMaster.AllowUserToDeleteRows = false;
        gMaster.SelectionMode = DataGridViewSelectionMode.FullRowSelect; gMaster.MultiSelect = true;
        gMaster.AllowDrop = true;
        gMaster.DataError += (_, e) => e.ThrowException = false;
        gMaster.DragEnter += OnDragEnter;
        gMaster.DragDrop += (_, e) => { if (e.Data!.GetData(DataFormats.FileDrop) is string[] f) BeginInvoke(new Action(() => CollectFiles(f))); };
        gMaster.CellEndEdit += (_, e) =>
        {
            if (masterLoading || e.RowIndex < 0) return;
            var r = gMaster.Rows[e.RowIndex];
            try
            {
                db.UpdateMaster(Convert.ToInt64(r.Cells["번호"].Value), S(r.Cells["바코드"].Value), S(r.Cells["품목"].Value), S(r.Cells["단위"].Value), RequestReader.Num(S(r.Cells["우리단가"].Value)));
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };
        page.Controls.Add(gMaster); page.Controls.Add(bar);
        return page;
    }

    void RefreshMaster()
    {
        try
        {
            masterLoading = true;
            gMaster.DataSource = db.ListMaster(tMasterSearch.Text.Trim());
            foreach (DataGridViewColumn c in gMaster.Columns)
            {
                c.ReadOnly = c.Name == "번호" || c.Name == "수정일";
                if (c.Name == "우리단가") { c.DefaultCellStyle.Format = "#,##0"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
                c.Width = c.Name == "품목" ? 420 : c.Name == "바코드" ? 160 : c.Name == "수정일" ? 150 : 90;
            }
            lblMaster.Text = $"수집된 품목 {db.MasterCount():N0}개  (표에서 직접 고칠 수 있습니다)";
        }
        catch (Exception e) { MessageBox.Show(e.Message); }
        finally { masterLoading = false; }
    }

    void CollectFromExcel()
    {
        using var d = new OpenFileDialog { Title = "품목/바코드가 들어 있는 엑셀 선택", Filter = "엑셀/CSV|*.xls;*.xlsx;*.csv|모든 파일|*.*", Multiselect = true };
        if (d.ShowDialog(this) == DialogResult.OK) CollectFiles(d.FileNames);
    }

    void CollectFiles(string[] files)
    {
        int total = 0;
        try
        {
            Cursor = Cursors.WaitCursor;
            foreach (var f in files)
            {
                var items = ReadRequestItems(f);
                if (items == null) continue;
                db.CollectMaster(items); total += items.Count;
            }
        }
        catch (Exception e) { MessageBox.Show("엑셀을 읽을 수 없습니다.\n" + e.Message); }
        finally { Cursor = Cursors.Default; }
        tabs.SelectedIndex = 2; RefreshMaster();
        if (total > 0) MessageBox.Show($"품목 {total:N0}개를 수집했습니다. 같은 바코드(또는 같은 품목명)는 최신 정보로 갱신됩니다.");
    }

    void ExportMaster()
    {
        using var d = new SaveFileDialog { Filter = "엑셀 (*.xlsx)|*.xlsx", FileName = $"품목바코드_{DateTime.Today:yyyyMMdd}.xlsx" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var dt = db.ListMaster("");
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(dt, "품목");
            ws.Columns().AdjustToContents();
            wb.SaveAs(d.FileName);
            MessageBox.Show("저장했습니다.\n" + d.FileName);
        }
        catch (Exception e) { MessageBox.Show(e.Message); }
    }

    void DeleteMaster()
    {
        if (gMaster.SelectedRows.Count == 0) { MessageBox.Show("삭제할 행의 왼쪽 끝을 눌러 선택하세요."); return; }
        if (MessageBox.Show($"선택한 {gMaster.SelectedRows.Count}개 품목을 수집 목록에서 삭제할까요?\n(이미 만든 견적에는 영향이 없습니다)", "삭제", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        foreach (DataGridViewRow r in gMaster.SelectedRows) db.DeleteMaster(Convert.ToInt64(r.Cells["번호"].Value));
        RefreshMaster();
    }
}
