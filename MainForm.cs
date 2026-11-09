using System.Globalization;

namespace ZQuote;

public class MainForm : Form
{
    readonly Db db;
    Quote cur;
    bool dirty, suppress, askedCompletedEdit;
    List<Company> regOthers = new();

    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly DataGridView grid = new();
    readonly DataGridView listGrid = new();
    readonly TextBox txtRecipient = new() { Width = 300 };
    readonly DateTimePicker dtp = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120 };
    readonly Label lblState = new() { AutoSize = true, Font = new Font("Malgun Gothic", 10f, FontStyle.Bold) };
    readonly Label lblTotals = new() { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 6, 10, 0), ForeColor = Ui.BlueDark, BackColor = Ui.BlueSoft };
    readonly CheckedListBox clbOthers = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    readonly TextBox txtScan = new() { Width = 230, PlaceholderText = "여기에 바코드 스캔 후 Enter" };
    readonly TextBox txtListFind = new() { Width = 220, PlaceholderText = "수신처 / 상태(작성중·완료) 검색" };
    readonly ToolStripStatusLabel lblStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm(Db db)
    {
        this.db = db;
        Text = $"z2roN 비교견적서 v{Program.Version}";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }
        Width = 1360; Height = 820; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Malgun Gothic", 10f); BackColor = Color.White;
        AllowDrop = true;
        BuildUi();
        Load += (_, _) => { NewQuote(false); CheckOur(); };
        DragEnter += (_, e) => { if (e.Data!.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data!.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) { tabs.SelectedIndex = 0; ImportFile(f[0]); } };
        FormClosing += (_, e) =>
        {
            if (dirty && MessageBox.Show("저장하지 않은 내용이 있습니다. 그래도 닫을까요?", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            db.Dispose();
        };
        FormClosed += (_, _) => Environment.Exit(0);
    }

    // ---------------- 화면 구성 ----------------
    void BuildUi()
    {
        var menu = new MenuStrip { BackColor = Color.White };
        var file = new ToolStripMenuItem("파일(&F)");
        file.DropDownItems.Add(new ToolStripMenuItem("새 견적", null, (_, _) => NewQuote(true)) { ShortcutKeys = Keys.Control | Keys.N });
        file.DropDownItems.Add(new ToolStripMenuItem("견적의뢰 엑셀 불러오기...", null, (_, _) => PickAndImport()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem("저장", null, (_, _) => Save(false)) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add(new ToolStripMenuItem("견적 완료", null, (_, _) => Save(true)));
        file.DropDownItems.Add(new ToolStripMenuItem("완료 해제 (관리자)", null, (_, _) => Uncomplete()));
        file.DropDownItems.Add(new ToolStripMenuItem("엑셀로 내려받기...", null, (_, _) => ExportCurrent()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("종료", null, (_, _) => Close()));
        var set = new ToolStripMenuItem("설정(&S)");
        set.DropDownItems.Add(new ToolStripMenuItem("업체 · 도장 · 인상률 · 백업 · 관리자...", null, (_, _) => OpenSettings()));
        var help = new ToolStripMenuItem("도움말(&H)");
        help.DropDownItems.Add(new ToolStripMenuItem("사용 방법", null, (_, _) => MessageBox.Show(
            "1) 설정에서 우리 업체와 타업체(최대 9개), 도장, 인상률을 미리 등록합니다.\n" +
            "2) [엑셀 불러오기]로 견적의뢰 엑셀을 고르거나 창에 끌어다 놓습니다.\n" +
            "3) 왼쪽 목록에서 이번 견적에 쓸 타업체를 체크하면 우리 단가 × 인상률로 업체별 단가가 채워집니다. 표에서 업체별 단가를 직접 고칠 수 있습니다.\n" +
            "4) 바코드 칸에 스캔하면 품목이 추가되고, 새 바코드는 이름·단가를 입력해 수집됩니다.\n" +
            "5) [저장] → [견적 완료] 후 [엑셀 내려받기]로 업체별 견적서를 받아 의뢰 업체에 전달합니다.\n" +
            "6) 완료된 견적은 관리자만 삭제할 수 있습니다.", "사용 방법")));
        menu.Items.AddRange(new ToolStripItem[] { file, set, help });

        var status = new StatusStrip { BackColor = Ui.BlueSoft, SizingGrip = false };
        status.Items.Add(lblStatus);

        var head = new Panel { Dock = DockStyle.Top, Height = 54 };
        head.Paint += (_, e) =>
        {
            using var br = new System.Drawing.Drawing2D.LinearGradientBrush(head.ClientRectangle, Ui.BlueDark, Color.FromArgb(59, 139, 255), 0f);
            e.Graphics.FillRectangle(br, head.ClientRectangle);
            int x = 16;
            try { if (Icon != null) { using var ic = new Icon(Icon, 34, 34); e.Graphics.DrawIcon(ic, new Rectangle(x, 10, 34, 34)); x += 44; } } catch { }
            using var f1 = new Font("Malgun Gothic", 14f, FontStyle.Bold);
            using var f2 = new Font("Malgun Gothic", 9f);
            TextRenderer.DrawText(e.Graphics, "z2roN 비교견적서", f1, new Point(x, 5), Color.White);
            TextRenderer.DrawText(e.Graphics, "견적의뢰 엑셀 → 우리 견적 + 타업체 견적 만들기", f2, new Point(x + 1, 31), Color.FromArgb(214, 228, 255));
        };

        // ----- 견적 작성 탭 -----
        var tpEdit = new TabPage("견적 작성");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(8, 6, 8, 0), BackColor = Ui.BlueSoft, WrapContents = true };
        Label L(string t) => new Label { Text = t, AutoSize = true, ForeColor = Ui.BlueDark, Margin = new Padding(6, 8, 2, 0) };
        txtRecipient.Margin = new Padding(3, 5, 3, 0); dtp.Margin = new Padding(3, 5, 3, 0); txtScan.Margin = new Padding(3, 5, 3, 0);
        txtRecipient.TextChanged += (_, _) => MarkDirty();
        dtp.ValueChanged += (_, _) => MarkDirty();
        top.Controls.Add(L("수신처(견적의뢰 업체)")); top.Controls.Add(txtRecipient); top.Controls.Add(L("견적일")); top.Controls.Add(dtp);
        top.Controls.Add(lblState); lblState.Margin = new Padding(14, 8, 0, 0);
        top.SetFlowBreak(lblState, true);
        top.Controls.Add(Ui.Btn("엑셀 불러오기", (_, _) => PickAndImport(), 1));
        top.Controls.Add(Ui.Btn("새 견적", (_, _) => NewQuote(true)));
        top.Controls.Add(Ui.Btn("저장", (_, _) => Save(false)));
        top.Controls.Add(Ui.Btn("견적 완료", (_, _) => Save(true)));
        top.Controls.Add(Ui.Btn("엑셀 내려받기", (_, _) => ExportCurrent(), 1));
        top.Controls.Add(L("바코드")); top.Controls.Add(txtScan);
        txtScan.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var c = txtScan.Text.Trim(); txtScan.Clear(); if (c != "") ScanBarcode(c); } };

        var left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(8), BackColor = Color.White };
        var lblL = new Label { Dock = DockStyle.Top, Height = 40, Text = "이번 견적에 쓸 타업체\n(체크하면 단가 열이 생깁니다)", ForeColor = Ui.BlueDark };
        var btnEdit = Ui.Btn("업체 정보 수정 (이 견적만)", (_, _) => EditCompanyMenu()); btnEdit.Dock = DockStyle.Bottom; btnEdit.AutoSize = false; btnEdit.Height = 36;
        var btnRe = Ui.Btn("인상률 다시 적용 (직접 고친 값도 초기화)", (_, _) => ReapplyMarkup()); btnRe.Dock = DockStyle.Bottom; btnRe.AutoSize = false; btnRe.Height = 50;
        var btnFresh = Ui.Btn("설정의 최신 업체 정보로 갱신", (_, _) => RefreshSnapshots()); btnFresh.Dock = DockStyle.Bottom; btnFresh.AutoSize = false; btnFresh.Height = 36;
        left.Controls.Add(clbOthers); left.Controls.Add(lblL); left.Controls.Add(btnFresh); left.Controls.Add(btnEdit); left.Controls.Add(btnRe);
        clbOthers.ItemCheck += (_, _) => { if (!suppress) BeginInvoke(new Action(ApplyOtherSelection)); };

        Ui.StyleGrid(grid);
        grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = true; grid.AllowUserToDeleteRows = true; grid.SelectionMode = DataGridViewSelectionMode.CellSelect; grid.MultiSelect = true;
        grid.RowHeadersSelectionModeHack();
        grid.CellEndEdit += Grid_CellEndEdit;
        grid.DataError += (_, e) => { e.ThrowException = false; e.Cancel = false; };
        grid.UserDeletedRow += (_, _) => { MarkDirty(); Renumber(); UpdateTotals(); };
        grid.RowsAdded += (_, _) => { if (!suppress) Renumber(); };
        tpEdit.Controls.Add(grid); tpEdit.Controls.Add(left); tpEdit.Controls.Add(lblTotals); tpEdit.Controls.Add(top);

        // ----- 견적 목록 탭 -----
        var tpList = new TabPage("견적 목록 (조회·수정·삭제)");
        var ltop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(8, 6, 8, 0), BackColor = Ui.BlueSoft };
        txtListFind.Margin = new Padding(3, 5, 8, 0);
        txtListFind.TextChanged += (_, _) => FillList();
        ltop.Controls.Add(txtListFind);
        ltop.Controls.Add(Ui.Btn("열기 / 수정", (_, _) => OpenSelected(), 1));
        ltop.Controls.Add(Ui.Btn("엑셀 내려받기", (_, _) => ExportSelected()));
        ltop.Controls.Add(Ui.Btn("삭제", (_, _) => DeleteSelected(), 2));
        ltop.Controls.Add(Ui.Btn("새로고침", (_, _) => FillList()));
        Ui.StyleGrid(listGrid);
        listGrid.Dock = DockStyle.Fill; listGrid.ReadOnly = true; listGrid.AllowUserToAddRows = false; listGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; listGrid.MultiSelect = false; listGrid.RowHeadersVisible = false;
        listGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        listGrid.Columns.Add("id", "번호"); listGrid.Columns.Add("rc", "수신처"); listGrid.Columns.Add("dt", "견적일"); listGrid.Columns.Add("st", "상태"); listGrid.Columns.Add("it", "품목 수"); listGrid.Columns.Add("tt", "우리 합계"); listGrid.Columns.Add("up", "수정일");
        listGrid.Columns["id"].FillWeight = 40; listGrid.Columns["rc"].FillWeight = 200; listGrid.Columns["it"].FillWeight = 50; listGrid.Columns["tt"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        listGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        tpList.Controls.Add(listGrid); tpList.Controls.Add(ltop);

        tabs.TabPages.Add(tpEdit); tabs.TabPages.Add(tpList);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex == 1) FillList(); };

        Controls.Add(tabs); Controls.Add(head); Controls.Add(menu); Controls.Add(status);
        MainMenuStrip = menu;
    }

    // ---------------- 상태 ----------------
    void MarkDirty() { if (suppress) return; dirty = true; UpdateState(); }

    void UpdateState()
    {
        if (cur == null) return;
        lblState.Text = (cur.Id == 0 ? "새 견적" : $"견적 #{cur.Id}") + " · " + cur.Status + (dirty ? " · ● 저장 안 됨" : "");
        lblState.ForeColor = cur.Completed ? Color.FromArgb(30, 130, 60) : Ui.BlueDark;
    }

    void CheckOur()
    {
        if (string.IsNullOrWhiteSpace(db.GetOur().Name) &&
            MessageBox.Show("우리 업체 정보(상호·사업자번호 등)가 아직 없습니다.\n지금 설정 화면을 열까요?", "처음 설정", MessageBoxButtons.YesNo) == DialogResult.Yes) OpenSettings();
    }

    void OpenSettings()
    {
        using var f = new SettingsForm(db);
        f.ShowDialog(this);
        if (f.Restored) { dirty = false; NewQuote(false); return; }
        LoadRegistered();
    }

    void LoadRegistered()
    {
        regOthers = db.GetOthers();
        suppress = true;
        clbOthers.Items.Clear();
        foreach (var c in regOthers) clbOthers.Items.Add($"{c.Name}  (+{db.MarkupOf(c):0.##}%)", cur != null && cur.Others.Any(o => o.Id == c.Id));
        suppress = false;
    }

    // ---------------- 새 견적 / 불러오기 ----------------
    void NewQuote(bool ask)
    {
        if (ask && dirty && MessageBox.Show("저장하지 않은 내용이 있습니다. 버리고 새 견적을 시작할까요?", "새 견적", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        cur = new Quote { Our = db.GetOur() };
        askedCompletedEdit = false;
        suppress = true; txtRecipient.Text = ""; dtp.Value = DateTime.Today; suppress = false;
        LoadRegistered();
        BuildGrid(new List<QItem>());
        dirty = false; UpdateState(); UpdateTotals();
        lblStatus.Text = "엑셀 파일을 끌어다 놓거나 [엑셀 불러오기]를 누르세요.";
    }

    void LoadQuoteToUi(Quote q)
    {
        cur = q; askedCompletedEdit = false;
        suppress = true; txtRecipient.Text = q.Recipient; dtp.Value = q.Date < dtp.MinDate ? DateTime.Today : q.Date; suppress = false;
        LoadRegistered();
        BuildGrid(q.Items);
        dirty = false; UpdateState(); UpdateTotals();
        lblStatus.Text = $"견적 #{q.Id} 을(를) 열었습니다.";
    }

    // ---------------- 표 ----------------
    void BuildGrid(List<QItem> items)
    {
        suppress = true;
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "bc", HeaderText = "바코드", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "nm", HeaderText = "품목", Width = 330 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "un", HeaderText = "단위", Width = 60 });
        grid.Columns.Add(NumCol("qty", "수량", "#,##0.##", 70));
        grid.Columns.Add(NumCol("our", "우리 단가", "#,##0", 100));
        foreach (var o in cur.Others)
        {
            var col = NumCol("p" + o.Id, $"{o.Name} (+{db.MarkupOf(o):0.##}%)", "#,##0", 120);
            col.Tag = o;
            col.HeaderCell.Style.BackColor = Color.FromArgb(70, 120, 200);
            grid.Columns.Add(col);
        }
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "nt", HeaderText = "비고", Width = 140 });
        foreach (var it in items)
        {
            var vals = new List<object> { it.Barcode, it.Name, it.Unit, it.Qty, it.OurPrice };
            foreach (var o in cur.Others) vals.Add(it.Prices.TryGetValue(o.Id, out var p) ? p : 0d);
            vals.Add(it.Note);
            int r = grid.Rows.Add(vals.ToArray());
            foreach (var o in cur.Others) if (it.Manual.Contains(o.Id)) grid.Rows[r].Cells["p" + o.Id].Tag = true;
        }
        suppress = false;
        Renumber(); UpdateTotals();
    }

    static DataGridViewTextBoxColumn NumCol(string name, string header, string fmt, int w) =>
        new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = w, ValueType = typeof(double), DefaultCellStyle = new DataGridViewCellStyle { Format = fmt, Alignment = DataGridViewContentAlignment.MiddleRight } };

    void Renumber()
    {
        for (int i = 0; i < grid.Rows.Count; i++) grid.Rows[i].HeaderCell.Value = grid.Rows[i].IsNewRow ? "" : (i + 1).ToString();
    }

    static double D(object v) => v == null ? 0 : v is double d ? d : RequestReader.Num(Convert.ToString(v, CultureInfo.CurrentCulture));

    List<QItem> ReadGrid()
    {
        var list = new List<QItem>();
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow) continue;
            string name = Convert.ToString(r.Cells["nm"].Value)?.Trim() ?? "";
            if (name == "") continue;
            var it = new QItem
            {
                Barcode = Convert.ToString(r.Cells["bc"].Value)?.Trim() ?? "", Name = name, Unit = Convert.ToString(r.Cells["un"].Value)?.Trim() ?? "",
                Qty = D(r.Cells["qty"].Value), OurPrice = D(r.Cells["our"].Value), Note = Convert.ToString(r.Cells["nt"].Value)?.Trim() ?? ""
            };
            foreach (var o in cur.Others)
            {
                var c = r.Cells["p" + o.Id];
                it.Prices[o.Id] = D(c.Value);
                if (c.Tag is bool b && b) it.Manual.Add(o.Id);
            }
            list.Add(it);
        }
        return list;
    }

    void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || suppress) return;
        var row = grid.Rows[e.RowIndex];
        string col = grid.Columns[e.ColumnIndex].Name;
        if (col == "our")
        {
            double our = D(row.Cells["our"].Value);
            foreach (var o in cur.Others)
            {
                var c = row.Cells["p" + o.Id];
                if (!(c.Tag is bool b && b)) c.Value = db.PriceFor(o, our);
            }
        }
        else if (col.StartsWith("p")) row.Cells[col].Tag = true;
        MarkDirty(); Renumber(); UpdateTotals();
    }

    void UpdateTotals()
    {
        double our = 0; var sums = cur?.Others.ToDictionary(o => o.Id, _ => 0d) ?? new Dictionary<int, double>();
        int n = 0, zero = 0;
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow || string.IsNullOrWhiteSpace(Convert.ToString(r.Cells["nm"].Value))) continue;
            n++;
            double q = D(r.Cells["qty"].Value), p = D(r.Cells["our"].Value);
            our += q * p; if (p <= 0) zero++;
            foreach (var id in sums.Keys.ToList()) sums[id] += q * D(r.Cells["p" + id].Value);
        }
        var parts = new List<string> { $"{(string.IsNullOrWhiteSpace(cur?.Our?.Name) ? "우리" : cur.Our.Name)}  {Ui.Won(our)}원" };
        if (cur != null) foreach (var o in cur.Others) parts.Add($"{o.Name}  {Ui.Won(sums[o.Id])}원");
        lblTotals.Text = $"품목 {n}개" + (zero > 0 ? $"  (우리 단가가 비어 있는 품목 {zero}개)" : "") + "   ·   합계(VAT 포함)\n" + string.Join("   |   ", parts);
    }

    // ---------------- 타업체 선택 / 인상률 ----------------
    void ApplyOtherSelection()
    {
        if (cur == null) return;
        var items = ReadGrid();
        var chosen = new List<Company>();
        for (int i = 0; i < regOthers.Count; i++)
        {
            if (!clbOthers.GetItemChecked(i)) continue;
            var old = cur.Others.FirstOrDefault(o => o.Id == regOthers[i].Id);
            chosen.Add(old ?? regOthers[i].Clone());
        }
        var added = chosen.Where(c => !cur.Others.Any(o => o.Id == c.Id)).ToList();
        cur.Others = chosen;
        var ids = new HashSet<int>(chosen.Select(c => c.Id));
        foreach (var it in items)
        {
            foreach (var k in it.Prices.Keys.Where(k => !ids.Contains(k)).ToList()) { it.Prices.Remove(k); it.Manual.Remove(k); }
            foreach (var c in added) it.Prices[c.Id] = db.PriceFor(c, it.OurPrice);
        }
        BuildGrid(items);
        MarkDirty();
    }

    void ReapplyMarkup()
    {
        if (cur.Others.Count == 0) { MessageBox.Show("먼저 왼쪽에서 타업체를 체크하세요."); return; }
        if (MessageBox.Show("모든 타업체 단가를 '우리 단가 × 인상률'로 다시 계산합니다.\n직접 고친 단가도 초기화됩니다. 계속할까요?", "인상률 다시 적용", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var items = ReadGrid();
        foreach (var it in items) { it.Manual.Clear(); foreach (var o in cur.Others) it.Prices[o.Id] = db.PriceFor(o, it.OurPrice); }
        BuildGrid(items); MarkDirty();
    }

    void RefreshSnapshots()
    {
        if (MessageBox.Show("이 견적의 우리·타업체 정보(상호, 사업자번호, 도장, 인상률 등)를 설정의 최신 정보로 바꿉니다.\n이 견적에서 따로 고친 업체 정보는 사라집니다. 계속할까요?", "업체 정보 갱신", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var items = ReadGrid();
        cur.Our = db.GetOur();
        cur.Others = cur.Others.Select(o => db.GetCompany(o.Id) ?? o).ToList();
        BuildGrid(items); MarkDirty();
    }

    /// 이 견적에 들어가는 업체 정보만 수정 (설정에는 영향 없음)
    void EditCompanyMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add($"우리: {cur.Our.Name}", null, (_, _) => EditCompany(cur.Our, false));
        foreach (var o in cur.Others) { var oo = o; m.Items.Add($"타업체: {o.Name}", null, (_, _) => EditCompany(oo, true)); }
        m.Show(Cursor.Position);
    }

    void EditCompany(Company c, bool other)
    {
        using var f = new Form { Text = "업체 정보 수정 - 이 견적에만 적용", Width = 700, Height = 460, StartPosition = FormStartPosition.CenterParent, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var ed = new CompanyEditor(other) { Left = 10, Top = 10 }; ed.LoadFrom(c);
        var ok = Ui.Btn("적용", null, 1); ok.Left = 10; ok.Top = 360;
        var no = Ui.Btn("취소", null); no.Left = 100; no.Top = 360; no.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => { var msg = ed.Apply(c); if (msg != null) { MessageBox.Show(msg); return; } f.DialogResult = DialogResult.OK; };
        f.Controls.AddRange(new Control[] { ed, ok, no }); f.CancelButton = no;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var items = ReadGrid();
        if (other)   // 인상률이 바뀌었을 수 있으므로 직접 고치지 않은 단가만 다시 계산
            foreach (var it in items) if (!it.Manual.Contains(c.Id)) it.Prices[c.Id] = db.PriceFor(c, it.OurPrice);
        BuildGrid(items); MarkDirty();
    }

    // ---------------- 엑셀 불러오기 ----------------
    void PickAndImport()
    {
        using var d = new OpenFileDialog { Filter = "견적의뢰 엑셀|*.xls;*.xlsx;*.csv|모든 파일|*.*", Title = "견적의뢰 엑셀 선택" };
        if (d.ShowDialog() == DialogResult.OK) ImportFile(d.FileName);
    }

    void ImportFile(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var rows = RequestReader.ReadRows(path);
            var m = RequestReader.Detect(rows);
            if (!m.Ok)
            {
                Cursor = Cursors.Default;
                using var dlg = new MapDialog(rows, m);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                m = dlg.Result();
                if (m.Name < 0) { MessageBox.Show("품목 열을 지정해 주세요."); return; }
                Cursor = Cursors.WaitCursor;
            }
            var items = RequestReader.Parse(rows, m);
            if (items.Count == 0) { MessageBox.Show("읽을 수 있는 품목이 없습니다. 머리글(품목/단위/수량) 위치를 확인하세요."); return; }
            int filled = 0;
            foreach (var it in items)   // 바코드로 우리 상품 목록에서 이름·단위·단가 보충
            {
                if (it.Barcode == "") continue;
                var p = db.GetProduct(it.Barcode); if (p == null) continue;
                if (it.OurPrice <= 0 && p.Price > 0) { it.OurPrice = p.Price; filled++; }
                if (it.Unit == "" && p.Unit != "") it.Unit = p.Unit;
            }
            var existing = ReadGrid();
            if (existing.Count > 0)
            {
                var a = MessageBox.Show($"이미 품목 {existing.Count}개가 있습니다.\n[예] 뒤에 이어 붙이기   [아니오] 모두 바꾸기", "엑셀 불러오기", MessageBoxButtons.YesNoCancel);
                if (a == DialogResult.Cancel) return;
                if (a == DialogResult.Yes) { existing.AddRange(items); items = existing; }
            }
            foreach (var it in items) foreach (var o in cur.Others) if (!it.Prices.ContainsKey(o.Id)) it.Prices[o.Id] = db.PriceFor(o, it.OurPrice);
            if (string.IsNullOrWhiteSpace(txtRecipient.Text)) { suppress = true; txtRecipient.Text = Path.GetFileNameWithoutExtension(path); suppress = false; }
            cur.SourceFile = Path.GetFileName(path);
            BuildGrid(items); MarkDirty();
            lblStatus.Text = $"{Path.GetFileName(path)} 에서 품목 {items.Count}개를 읽었습니다." + (filled > 0 ? $" (바코드로 단가 {filled}개 보충)" : "");
            if (items.Any(i => i.OurPrice <= 0)) lblStatus.Text += " · 단가가 비어 있는 품목은 '우리 단가'를 입력하세요.";
        }
        catch (Exception e) { MessageBox.Show("엑셀을 읽지 못했습니다.\n" + e.Message + "\n\n.xls 가 안 읽히면 엑셀에서 .xlsx 로 다시 저장해 보세요.", "오류"); }
        finally { Cursor = Cursors.Default; }
    }

    // ---------------- 바코드 수집 ----------------
    void ScanBarcode(string code)
    {
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (r.IsNewRow) continue;
            if (Convert.ToString(r.Cells["bc"].Value)?.Trim() == code)
            {
                r.Cells["qty"].Value = D(r.Cells["qty"].Value) + 1;
                grid.CurrentCell = r.Cells["qty"]; MarkDirty(); UpdateTotals();
                lblStatus.Text = $"{code}  수량 +1 → {D(r.Cells["qty"].Value):0.##}";
                txtScan.Focus(); return;
            }
        }
        var p = db.GetProduct(code);
        if (p == null || string.IsNullOrWhiteSpace(p.Name))
        {
            var np = AskNewProduct(code, p);
            if (np == null) { txtScan.Focus(); return; }
            db.SaveProduct(np); p = np;
        }
        var it = new QItem { Barcode = code, Name = p.Name, Unit = p.Unit, Qty = 1, OurPrice = p.Price, Note = p.Note };
        var vals = new List<object> { it.Barcode, it.Name, it.Unit, it.Qty, it.OurPrice };
        foreach (var o in cur.Others) vals.Add(db.PriceFor(o, it.OurPrice));
        vals.Add(it.Note);
        suppress = true; int idx = grid.Rows.Add(vals.ToArray()); suppress = false;
        grid.CurrentCell = grid.Rows[idx].Cells["qty"];
        Renumber(); MarkDirty(); UpdateTotals();
        lblStatus.Text = $"{code}  {p.Name} 추가";
        txtScan.Focus();
    }

    Product AskNewProduct(string code, Product old)
    {
        using var f = new Form { Text = "새 바코드 수집", Width = 440, Height = 280, StartPosition = FormStartPosition.CenterParent, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        f.Controls.Add(new Label { Left = 14, Top = 12, Width = 400, Text = "처음 보는 바코드입니다: " + code, ForeColor = Ui.BlueDark });
        TextBox T(string label, int y, string v) { f.Controls.Add(new Label { Left = 14, Top = y + 4, Width = 70, Text = label }); var t = new TextBox { Left = 90, Top = y, Width = 320, Text = v }; f.Controls.Add(t); return t; }
        var nm = T("품목", 44, old?.Name ?? ""); var un = T("단위", 80, old?.Unit ?? ""); var pr = T("우리 단가", 116, old != null && old.Price > 0 ? old.Price.ToString("0.##") : "");
        var ok = Ui.Btn("수집", null, 1); ok.Left = 224; ok.Top = 170; ok.DialogResult = DialogResult.OK;
        var no = Ui.Btn("건너뛰기", null); no.Left = 314; no.Top = 170; no.DialogResult = DialogResult.Cancel;
        f.Controls.Add(ok); f.Controls.Add(no); f.AcceptButton = ok; f.CancelButton = no;
        if (f.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(nm.Text)) return null;
        return new Product { Barcode = code, Name = nm.Text.Trim(), Unit = un.Text.Trim(), Price = RequestReader.Num(pr.Text) };
    }

    // ---------------- 저장 / 완료 ----------------
    bool Save(bool complete)
    {
        var items = ReadGrid();
        if (string.IsNullOrWhiteSpace(txtRecipient.Text)) { MessageBox.Show("수신처(견적의뢰 업체명)를 입력하세요."); txtRecipient.Focus(); return false; }
        if (items.Count == 0) { MessageBox.Show("품목이 없습니다."); return false; }
        if (string.IsNullOrWhiteSpace(cur.Our.Name)) { MessageBox.Show("우리 업체 정보가 비어 있습니다. 설정 > 우리 업체에서 먼저 등록하세요."); return false; }
        if (complete)
        {
            int zero = items.Count(i => i.OurPrice <= 0);
            if (zero > 0 && MessageBox.Show($"우리 단가가 0인 품목이 {zero}개 있습니다. 그래도 완료할까요?", "견적 완료", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            if (MessageBox.Show("견적을 완료 처리합니다.\n완료된 견적은 관리자만 삭제할 수 있습니다. (수정은 계속 가능)", "견적 완료", MessageBoxButtons.YesNo) != DialogResult.Yes) return false;
        }
        else if (cur.Completed && !askedCompletedEdit)
        {
            if (MessageBox.Show("완료된 견적을 수정해서 저장합니다. 계속할까요?", "완료 견적 수정", MessageBoxButtons.YesNo) != DialogResult.Yes) return false;
            askedCompletedEdit = true;
        }
        try
        {
            cur.Recipient = txtRecipient.Text.Trim(); cur.Date = dtp.Value.Date; cur.Items = items;
            if (complete) cur.Status = "완료";
            bool wasCompleted = cur.Completed && cur.Id != 0 && !complete;
            db.SaveQuote(cur);
            if (wasCompleted) db.Audit("완료견적수정", "id=" + cur.Id);
            int bc = db.CollectBarcodes(items);
            dirty = false; UpdateState();
            string msg = $"저장했습니다. (견적 #{cur.Id}" + (bc > 0 ? $", 바코드 {bc}개 수집" : "") + ")";
            if (complete && db.AutoBackup)
            {
                try { var f = db.Backup(); msg += "\n자동 백업: " + f; } catch (Exception e) { msg += "\n(자동 백업 실패: " + e.Message + ")"; }
            }
            lblStatus.Text = msg.Replace("\n", " · ");
            if (complete) MessageBox.Show(msg + "\n\n[엑셀 내려받기]로 견적서를 받으세요.", "견적 완료");
            return true;
        }
        catch (Exception e) { MessageBox.Show("저장하지 못했습니다.\n" + e.Message, "오류"); return false; }
    }

    void Uncomplete()
    {
        if (cur.Id == 0 || !cur.Completed) { MessageBox.Show("완료된 견적이 아닙니다."); return; }
        if (!Ui.AskAdmin(db, this, "완료된 견적을 '작성중'으로 되돌립니다.")) return;
        cur.Status = "작성중"; dirty = true; db.Audit("완료해제", "id=" + cur.Id); UpdateState();
        MessageBox.Show("작성중으로 바꿨습니다. [저장]을 눌러 반영하세요.");
    }

    // ---------------- 내려받기 ----------------
    void ExportCurrent()
    {
        var items = ReadGrid();
        if (items.Count == 0) { MessageBox.Show("내려받을 품목이 없습니다."); return; }
        if (string.IsNullOrWhiteSpace(txtRecipient.Text)) { MessageBox.Show("수신처(견적의뢰 업체명)를 입력하세요."); return; }
        if (dirty && MessageBox.Show("저장하지 않은 변경이 있습니다. 지금 화면 내용으로 내려받습니다.\n(먼저 저장하려면 [아니오] 후 저장하세요)", "내려받기", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        cur.Recipient = txtRecipient.Text.Trim(); cur.Date = dtp.Value.Date; cur.Items = items;
        ExportFlow(cur);
    }

    void ExportFlow(Quote q)
    {
        using var f = new Form { Text = "엑셀 내려받기", Width = 460, Height = 400, StartPosition = FormStartPosition.CenterParent, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        f.Controls.Add(new Label { Left = 14, Top = 12, Width = 420, Text = "내려받을 업체를 고르세요 (우리 견적 + 타업체 견적)" });
        var clb = new CheckedListBox { Left = 14, Top = 40, Width = 420, Height = 180, CheckOnClick = true };
        var all = new List<Company> { q.Our }; all.AddRange(q.Others);
        foreach (var c in all) clb.Items.Add((c.Kind == "our" ? "[우리] " : "[타업체] ") + c.Name, true);
        var rbOne = new RadioButton { Left = 14, Top = 232, Width = 420, Text = "한 파일에 업체별 시트로 저장", Checked = true };
        var rbSep = new RadioButton { Left = 14, Top = 260, Width = 420, Text = "업체별로 파일을 따로 저장 (업체에 각각 전달할 때)" };
        var ok = Ui.Btn("내려받기", null, 1); ok.Left = 240; ok.Top = 306; ok.DialogResult = DialogResult.OK;
        var no = Ui.Btn("취소", null); no.Left = 350; no.Top = 306; no.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange(new Control[] { clb, rbOne, rbSep, ok, no }); f.AcceptButton = ok; f.CancelButton = no;
        if (f.ShowDialog(this) != DialogResult.OK) return;
        var who = all.Where((c, i) => clb.GetItemChecked(i)).ToList();
        if (who.Count == 0) { MessageBox.Show("업체를 하나 이상 고르세요."); return; }
        try
        {
            Cursor = Cursors.WaitCursor;
            if (rbOne.Checked)
            {
                Cursor = Cursors.Default;
                using var d = new SaveFileDialog { Filter = "엑셀 (*.xlsx)|*.xlsx", FileName = $"{Exporter.SafeFile(q.Recipient)}_비교견적_{q.Date:yyyyMMdd}.xlsx", Title = "견적서 저장" };
                if (d.ShowDialog() != DialogResult.OK) return;
                Cursor = Cursors.WaitCursor;
                Exporter.ExportWorkbook(db, q, who, d.FileName);
                lblStatus.Text = "저장했습니다: " + d.FileName;
                MessageBox.Show("저장했습니다.\n" + d.FileName);
            }
            else
            {
                Cursor = Cursors.Default;
                using var d = new FolderBrowserDialog { Description = "견적서를 저장할 폴더 선택" };
                if (d.ShowDialog() != DialogResult.OK) return;
                Cursor = Cursors.WaitCursor;
                var files = Exporter.ExportSeparate(db, q, who, d.SelectedPath);
                lblStatus.Text = $"{files.Count}개 파일을 저장했습니다: {d.SelectedPath}";
                MessageBox.Show($"{files.Count}개 파일을 저장했습니다.\n" + d.SelectedPath);
            }
        }
        catch (Exception e) { MessageBox.Show("엑셀을 저장하지 못했습니다. 같은 이름의 파일이 열려 있지 않은지 확인하세요.\n" + e.Message, "오류"); }
        finally { Cursor = Cursors.Default; }
    }

    // ---------------- 견적 목록 ----------------
    void FillList()
    {
        listGrid.Rows.Clear();
        foreach (var r in db.ListQuotes(txtListFind.Text.Trim()))
            listGrid.Rows.Add(r.Id, r.Recipient, r.Date, r.Status, r.Items, Ui.Won(r.Total), r.Updated);
    }

    long SelectedId() => listGrid.CurrentRow == null ? 0 : Convert.ToInt64(listGrid.CurrentRow.Cells["id"].Value);

    void OpenSelected()
    {
        long id = SelectedId(); if (id == 0) return;
        if (dirty && MessageBox.Show("저장하지 않은 내용이 있습니다. 버리고 선택한 견적을 열까요?", "열기", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var q = db.LoadQuote(id); if (q == null) { MessageBox.Show("견적을 찾을 수 없습니다."); return; }
        LoadQuoteToUi(q); tabs.SelectedIndex = 0;
    }

    void ExportSelected()
    {
        long id = SelectedId(); if (id == 0) return;
        var q = db.LoadQuote(id); if (q != null) ExportFlow(q);
    }

    void DeleteSelected()
    {
        long id = SelectedId(); if (id == 0) return;
        var q = db.LoadQuote(id); if (q == null) return;
        if (q.Completed)
        {
            if (!Ui.AskAdmin(db, this, $"완료된 견적 #{id} ({q.Recipient}) 을(를) 삭제합니다. 되돌릴 수 없습니다.")) return;
        }
        else if (MessageBox.Show($"견적 #{id} ({q.Recipient}) 을(를) 삭제할까요? 되돌릴 수 없습니다.", "삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        db.DeleteQuote(id);
        if (cur != null && cur.Id == id) { dirty = false; NewQuote(false); }
        FillList();
    }
}

static class GridExt
{
    /// 행 번호를 눌러 줄 전체를 선택할 수 있게 한다 (셀 선택은 그대로 유지)
    public static void RowHeadersSelectionModeHack(this DataGridView g) => g.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
}
