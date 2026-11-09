namespace ZQuote;

static class Ui
{
    public static readonly Color Blue = Color.FromArgb(43, 99, 217), BlueDark = Color.FromArgb(27, 63, 156), BlueSoft = Color.FromArgb(238, 243, 253), BlueLine = Color.FromArgb(217, 229, 251), Danger = Color.FromArgb(210, 62, 62);

    // kind: 0 보통, 1 주요(파랑), 2 삭제(빨강 글자)
    public static Button Btn(string text, EventHandler click, int kind = 0)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, Height = 34, Padding = new Padding(8, 0, 8, 0), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 1;
        if (kind == 1) { b.BackColor = Blue; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = Blue; b.FlatAppearance.MouseOverBackColor = BlueDark; b.Font = new Font("Malgun Gothic", 10f, FontStyle.Bold); }
        else { b.BackColor = Color.White; b.ForeColor = kind == 2 ? Danger : BlueDark; b.FlatAppearance.BorderColor = Color.FromArgb(180, 200, 240); b.FlatAppearance.MouseOverBackColor = BlueLine; }
        if (click != null) b.Click += click;
        return b;
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Color.White;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Color.FromArgb(230, 236, 247);
        g.EnableHeadersVisualStyles = false;
        g.RowHeadersVisible = true;
        g.RowHeadersWidth = 44;
        g.ColumnHeadersHeight = 34;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Blue, ForeColor = Color.White, Font = new Font("Malgun Gothic", 10f, FontStyle.Bold), Alignment = DataGridViewContentAlignment.MiddleLeft, SelectionBackColor = Blue, SelectionForeColor = Color.White };
        g.RowHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = BlueSoft, ForeColor = Color.Gray, SelectionBackColor = Color.FromArgb(170, 200, 250), SelectionForeColor = Color.Black };
        g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Color.FromArgb(40, 44, 52), SelectionBackColor = Color.FromArgb(205, 224, 255), SelectionForeColor = Color.Black, Padding = new Padding(3, 0, 3, 0) };
        g.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(246, 249, 255) };
        g.RowTemplate.Height = 26;
    }

    /// 관리자 비밀번호 확인. 맞으면 true.
    public static bool AskAdmin(Db db, IWin32Window owner, string reason)
    {
        if (!db.HasAdmin()) { MessageBox.Show("관리자 비밀번호가 아직 없습니다. 설정 > 관리자에서 먼저 만들어 주세요.", "관리자"); return false; }
        using var f = new Form { Text = "관리자 확인", Width = 380, Height = 190, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Malgun Gothic", 10f) };
        var lb = new Label { Left = 14, Top = 14, Width = 340, Height = 44, Text = reason + "\n관리자 비밀번호를 입력하세요." };
        var tb = new TextBox { Left = 14, Top = 64, Width = 340, UseSystemPasswordChar = true };
        var ok = Btn("확인", null, 1); ok.Left = 168; ok.Top = 104; ok.AutoSize = false; ok.Width = 90; ok.DialogResult = DialogResult.OK;
        var no = Btn("취소", null); no.Left = 264; no.Top = 104; no.AutoSize = false; no.Width = 90; no.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange(new Control[] { lb, tb, ok, no });
        f.AcceptButton = ok; f.CancelButton = no;
        if (f.ShowDialog(owner) != DialogResult.OK) return false;
        if (db.VerifyAdmin(tb.Text)) return true;
        MessageBox.Show("비밀번호가 맞지 않습니다.", "관리자");
        return false;
    }

    /// 한 줄 입력 대화상자
    public static string Ask(IWin32Window owner, string title, string label, string def = "")
    {
        using var f = new Form { Text = title, Width = 420, Height = 170, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Malgun Gothic", 10f) };
        var lb = new Label { Left = 14, Top = 14, Width = 380, Height = 24, Text = label };
        var tb = new TextBox { Left = 14, Top = 42, Width = 380, Text = def };
        var ok = Btn("확인", null, 1); ok.Left = 208; ok.Top = 82; ok.AutoSize = false; ok.Width = 90; ok.DialogResult = DialogResult.OK;
        var no = Btn("취소", null); no.Left = 304; no.Top = 82; no.AutoSize = false; no.Width = 90; no.DialogResult = DialogResult.Cancel;
        f.Controls.AddRange(new Control[] { lb, tb, ok, no });
        f.AcceptButton = ok; f.CancelButton = no;
        return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
    }

    public static string Won(double v) => v.ToString("#,##0");
}

/// 처음 실행할 때 관리자 비밀번호 만들기 / 설정에서 변경
class AdminSetupForm : Form
{
    public AdminSetupForm(Db db, bool change = false)
    {
        Text = change ? "관리자 비밀번호 변경" : "관리자 비밀번호 만들기";
        Width = 400; Height = change ? 290 : 240; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Malgun Gothic", 10f);
        int y = 14;
        TextBox old = null;
        if (change)
        {
            Controls.Add(new Label { Left = 14, Top = y, Width = 360, Text = "현재 비밀번호" });
            old = new TextBox { Left = 14, Top = y + 24, Width = 355, UseSystemPasswordChar = true }; Controls.Add(old); y += 58;
        }
        else
        {
            Controls.Add(new Label { Left = 14, Top = y, Width = 360, Height = 40, Text = "견적 삭제·업체 삭제·백업 복원은 관리자만 할 수 있습니다.\n관리자 비밀번호를 정해 주세요. (잊지 않게 보관하세요)" }); y += 52;
        }
        Controls.Add(new Label { Left = 14, Top = y, Width = 360, Text = "새 비밀번호 (4자 이상)" });
        var p1 = new TextBox { Left = 14, Top = y + 24, Width = 355, UseSystemPasswordChar = true }; Controls.Add(p1);
        Controls.Add(new Label { Left = 14, Top = y + 58, Width = 360, Text = "한 번 더 입력" });
        var p2 = new TextBox { Left = 14, Top = y + 82, Width = 355, UseSystemPasswordChar = true }; Controls.Add(p2);
        var ok = Ui.Btn("확인", null, 1); ok.AutoSize = false; ok.Left = 190; ok.Top = y + 122; ok.Width = 85;
        var no = Ui.Btn(change ? "취소" : "나중에", (_, _) => Close()); no.AutoSize = false; no.Left = 284; no.Top = y + 122; no.Width = 85;
        ok.Click += (_, _) =>
        {
            if (change && !db.VerifyAdmin(old.Text)) { MessageBox.Show("현재 비밀번호가 맞지 않습니다."); return; }
            if (p1.Text.Length < 4) { MessageBox.Show("비밀번호는 4자 이상으로 해주세요."); return; }
            if (p1.Text != p2.Text) { MessageBox.Show("두 번 입력한 비밀번호가 다릅니다."); return; }
            db.SetAdminPassword(p1.Text);
            db.Audit("관리자비밀번호", change ? "변경" : "생성");
            MessageBox.Show("관리자 비밀번호를 저장했습니다.");
            Close();
        };
        Controls.Add(ok); Controls.Add(no);
        AcceptButton = ok;
        Height = y + 220;
    }
}

/// 열 위치를 자동으로 못 찾았을 때 직접 고르는 대화상자
class MapDialog : Form
{
    readonly ColMap map;
    readonly NumericUpDown nHead = new() { Minimum = 1, Maximum = 1000, Width = 70 };
    readonly Dictionary<string, ComboBox> cbs = new();

    public MapDialog(List<string[]> rows, ColMap m)
    {
        map = m;
        Text = "엑셀 열 지정"; Width = 900; Height = 640; StartPosition = FormStartPosition.CenterParent; Font = new Font("Malgun Gothic", 10f);
        int cols = rows.Take(60).Select(r => r.Length).DefaultIfEmpty(1).Max();
        var info = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 8, 10, 0), Text = "머리글(품목, 수량 등)을 자동으로 찾지 못했습니다. 머리글이 있는 줄 번호와 각 항목이 들어 있는 열을 골라 주세요." };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersWidth = 50, ColumnHeadersHeight = 26 };
        for (int c = 0; c < cols; c++) grid.Columns.Add("c" + c, ColName(c));
        for (int r = 0; r < Math.Min(rows.Count, 60); r++)
        {
            var v = new object[cols];
            for (int c = 0; c < cols; c++) v[c] = c < rows[r].Length ? rows[r][c] : "";
            int idx = grid.Rows.Add(v);
            grid.Rows[idx].HeaderCell.Value = (r + 1).ToString();
        }
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(8), WrapContents = true };
        bottom.Controls.Add(new Label { Text = "머리글 줄", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        nHead.Value = Math.Max(1, m.HeaderRow + 1); bottom.Controls.Add(nHead);
        foreach (var (key, title, cur) in new[] { ("name", "품목(필수)", m.Name), ("unit", "단위", m.Unit), ("qty", "수량", m.Qty), ("price", "단가", m.Price), ("barcode", "바코드", m.Barcode), ("note", "비고", m.Note) })
        {
            bottom.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(10, 8, 3, 0) });
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            cb.Items.Add("없음");
            for (int c = 0; c < cols; c++) cb.Items.Add(ColName(c));
            cb.SelectedIndex = cur >= 0 && cur < cols ? cur + 1 : 0;
            cbs[key] = cb; bottom.Controls.Add(cb);
        }
        var ok = Ui.Btn("확인", null, 1); ok.DialogResult = DialogResult.OK; ok.Margin = new Padding(20, 0, 3, 0);
        var no = Ui.Btn("취소", null); no.DialogResult = DialogResult.Cancel;
        bottom.Controls.Add(ok); bottom.Controls.Add(no);
        Controls.Add(grid); Controls.Add(bottom); Controls.Add(info);
        AcceptButton = ok; CancelButton = no;
    }

    static string ColName(int c) { string s = ""; c++; while (c > 0) { c--; s = (char)('A' + c % 26) + s; c /= 26; } return s; }

    public ColMap Result()
    {
        int G(string k) => cbs[k].SelectedIndex - 1;
        return new ColMap { HeaderRow = (int)nHead.Value - 1, Name = G("name"), Unit = G("unit"), Qty = G("qty"), Price = G("price"), Barcode = G("barcode"), Note = G("note"), Spec = -1 };
    }
}
