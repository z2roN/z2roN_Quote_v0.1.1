namespace ZQuote;

/// 업체 정보(상호·사업자번호·대표·업태·주소·전화·팩스·도장·인상률) 입력 칸
class CompanyEditor : UserControl
{
    readonly TextBox tName = new(), tBiz = new(), tCeo = new(), tType = new(), tAddr = new(), tTel = new(), tFax = new(), tMarkup = new();
    readonly PictureBox pic = new() { Width = 150, Height = 150, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    readonly CheckBox cbWhite = new() { Text = "흰 배경 투명 처리", Checked = true, AutoSize = true };
    byte[] stamp;
    readonly bool showMarkup;
    public double DefaultMarkup = 10;

    public CompanyEditor(bool showMarkup)
    {
        this.showMarkup = showMarkup;
        Font = new Font("Malgun Gothic", 10f);
        Width = 640; Height = 330;
        int y = 8;
        void Row(string label, TextBox t, int w = 330)
        {
            Controls.Add(new Label { Text = label, Left = 4, Top = y + 4, Width = 100 });
            t.Left = 108; t.Top = y; t.Width = w; Controls.Add(t); y += 34;
        }
        Row("상호(업체명)", tName); Row("사업자번호", tBiz, 200); Row("대표자", tCeo, 200); Row("업태", tType); Row("주소", tAddr); Row("전화", tTel, 200); Row("팩스", tFax, 200);
        if (showMarkup)
        {
            Controls.Add(new Label { Text = "우리보다 인상률(%)", Left = 4, Top = y + 4, Width = 130 });
            tMarkup.Left = 140; tMarkup.Top = y; tMarkup.Width = 80; Controls.Add(tMarkup);
            Controls.Add(new Label { Text = "비워두면 기본 인상률", Left = 226, Top = y + 5, Width = 200, ForeColor = Color.Gray });
        }
        Controls.Add(new Label { Text = "도장", Left = 460, Top = 8, Width = 60 });
        pic.Left = 460; pic.Top = 34; Controls.Add(pic);
        var b1 = Ui.Btn("도장 불러오기", (_, _) => PickStamp()); b1.Left = 460; b1.Top = 192; Controls.Add(b1);
        var b2 = Ui.Btn("도장 지우기", (_, _) => { stamp = null; ShowStamp(); }); b2.Left = 460; b2.Top = 232; Controls.Add(b2);
        cbWhite.Left = 460; cbWhite.Top = 276; Controls.Add(cbWhite);
    }

    void ShowStamp()
    {
        pic.Image?.Dispose(); pic.Image = null;
        if (stamp != null) { try { using var ms = new MemoryStream(stamp); using var im = Image.FromStream(ms); pic.Image = new Bitmap(im); } catch { } }
    }

    void PickStamp()
    {
        using var d = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일|*.*", Title = "도장 이미지 선택" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try { stamp = ProcessStamp(d.FileName, cbWhite.Checked); ShowStamp(); }
        catch (Exception e) { MessageBox.Show("이미지를 읽을 수 없습니다.\n" + e.Message); }
    }

    /// 크기를 줄이고(최대 240px) 흰 배경을 투명하게 바꿔 PNG 로 만든다.
    public static byte[] ProcessStamp(string file, bool whiteToTransparent)
    {
        using var src = Image.FromFile(file);
        double k = Math.Min(1.0, 240.0 / Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, w, h); }
        if (whiteToTransparent)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.R > 235 && c.G > 235 && c.B > 235) bmp.SetPixel(x, y, Color.FromArgb(0, 255, 255, 255));
                }
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    public void LoadFrom(Company c)
    {
        tName.Text = c.Name; tBiz.Text = c.BizNo; tCeo.Text = c.Ceo; tType.Text = c.BizType; tAddr.Text = c.Address; tTel.Text = c.Tel; tFax.Text = c.Fax;
        tMarkup.Text = c.Markup.HasValue ? c.Markup.Value.ToString("0.##") : "";
        stamp = c.Stamp; ShowStamp();
    }

    /// 입력값을 업체 객체에 옮긴다. 문제가 있으면 메시지를 돌려줌(없으면 null).
    public string Apply(Company c)
    {
        if (string.IsNullOrWhiteSpace(tName.Text)) return "상호(업체명)를 입력하세요.";
        double? mk = null;
        if (showMarkup && !string.IsNullOrWhiteSpace(tMarkup.Text))
        {
            if (!double.TryParse(tMarkup.Text.Trim().TrimEnd('%'), out var d) || d < -50 || d > 500) return "인상률은 숫자로 입력하세요. (예: 10)";
            mk = d;
        }
        c.Name = tName.Text.Trim(); c.BizNo = tBiz.Text.Trim(); c.Ceo = tCeo.Text.Trim(); c.BizType = tType.Text.Trim();
        c.Address = tAddr.Text.Trim(); c.Tel = tTel.Text.Trim(); c.Fax = tFax.Text.Trim(); c.Stamp = stamp;
        if (showMarkup) c.Markup = mk;
        return null;
    }
}
