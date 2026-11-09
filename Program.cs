namespace ZQuote;

static class Program
{
    public const string Version = "0.1.0";

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Db db;
        try { db = Db.OpenDefault(); }
        catch (Exception e) { MessageBox.Show("데이터 파일을 열 수 없습니다.\n" + e.Message, "오류"); return; }
        if (!db.HasAdmin())
        {
            using var f = new AdminSetupForm(db);
            f.ShowDialog();
        }
        Application.Run(new MainForm(db));
    }
}
