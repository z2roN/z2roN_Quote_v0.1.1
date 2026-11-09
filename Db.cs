using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ZQuote;

/// 견적 데이터 저장소 (SQLite 1개 파일)
public class Db : IDisposable
{
    SqliteConnection conn;
    public string Path { get; private set; }

    public const int MaxOthers = 9;   // 타업체는 10개 미만

    // ---------- 위치 / 열기 ----------
    public static string AppDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "z2roN_Quote");
    static string AppJson => System.IO.Path.Combine(AppDir, "app.json");
    static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "z2roN_Quote", "견적자료.zqdb");

    public static Db OpenDefault()
    {
        string p = DefaultPath;
        try { if (File.Exists(AppJson)) { var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppJson)); if (d != null && d.TryGetValue("db", out var x) && File.Exists(x)) p = x; } } catch { }
        return Open(p);
    }

    public static Db Open(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var d = new Db { Path = path };
        d.conn = new SqliteConnection("Data Source=" + path);
        d.conn.Open();
        d.Exec("PRAGMA journal_mode=WAL");
        d.Exec("PRAGMA synchronous=NORMAL");
        d.CreateSchema();
        try { Directory.CreateDirectory(AppDir); File.WriteAllText(AppJson, JsonSerializer.Serialize(new Dictionary<string, string> { ["db"] = path })); } catch { }
        return d;
    }

    public void Dispose()
    {
        try { if (conn != null && conn.State == System.Data.ConnectionState.Open) Exec("PRAGMA wal_checkpoint(TRUNCATE)"); } catch { }
        try { conn?.Close(); conn?.Dispose(); } catch { }
        conn = null;
        SqliteConnection.ClearAllPools();
    }

    void CreateSchema()
    {
        Exec("CREATE TABLE IF NOT EXISTS settings (k TEXT PRIMARY KEY, v TEXT)");
        Exec("CREATE TABLE IF NOT EXISTS admin (id INTEGER PRIMARY KEY, salt TEXT, hash TEXT)");
        Exec(@"CREATE TABLE IF NOT EXISTS companies (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT, name TEXT, biz_no TEXT, ceo TEXT, biz_type TEXT, address TEXT, tel TEXT, fax TEXT, stamp BLOB, markup REAL, sort_no INTEGER)");
        Exec(@"CREATE TABLE IF NOT EXISTS quotes (id INTEGER PRIMARY KEY AUTOINCREMENT, recipient TEXT, qdate TEXT, status TEXT, note TEXT, source TEXT, created TEXT, updated TEXT)");
        Exec(@"CREATE TABLE IF NOT EXISTS qcompanies (quote_id INTEGER, role TEXT, orig_id INTEGER, name TEXT, biz_no TEXT, ceo TEXT, biz_type TEXT, address TEXT, tel TEXT, fax TEXT, stamp BLOB, markup REAL, sort_no INTEGER)");
        Exec("CREATE INDEX IF NOT EXISTS ix_qc ON qcompanies(quote_id)");
        Exec(@"CREATE TABLE IF NOT EXISTS qitems (quote_id INTEGER, seq INTEGER, barcode TEXT, name TEXT, unit TEXT, qty REAL, our_price REAL, note TEXT)");
        Exec("CREATE INDEX IF NOT EXISTS ix_qi ON qitems(quote_id)");
        Exec(@"CREATE TABLE IF NOT EXISTS qprices (quote_id INTEGER, seq INTEGER, company_id INTEGER, price REAL, manual INTEGER)");
        Exec("CREATE INDEX IF NOT EXISTS ix_qp ON qprices(quote_id)");
        Exec(@"CREATE TABLE IF NOT EXISTS products (barcode TEXT PRIMARY KEY, name TEXT, unit TEXT, price REAL, note TEXT, updated TEXT)");
        Exec(@"CREATE TABLE IF NOT EXISTS audit (id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT, action TEXT, detail TEXT)");
        // 우리 업체는 항상 1개
        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM companies WHERE kind='our'")) == 0)
            Exec("INSERT INTO companies (kind,name,biz_no,ceo,biz_type,address,tel,fax,markup,sort_no) VALUES ('our','','','','','','','',NULL,0)");
    }

    // ---------- 기본 ----------
    public int Exec(string sql, params (string, object)[] ps)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c.ExecuteNonQuery();
    }

    public object Scalar(string sql, params (string, object)[] ps)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var r = c.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    static void Run(SqliteConnection cn, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var c = cn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (k, v) in ps) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        c.ExecuteNonQuery();
    }

    public Microsoft.Data.Sqlite.SqliteConnection Conn() => conn;

    public void Audit(string action, string detail) =>
        Exec("INSERT INTO audit (at,action,detail) VALUES ($a,$b,$c)", ("$a", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), ("$b", action), ("$c", detail));

    // ---------- 설정 ----------
    public string Get(string key, string def = "")
    {
        var v = Scalar("SELECT v FROM settings WHERE k=$k", ("$k", key)) as string;
        return v ?? def;
    }
    public void Set(string key, string value) => Exec("INSERT OR REPLACE INTO settings (k,v) VALUES ($k,$v)", ("$k", key), ("$v", value));

    public double DefaultMarkup { get => double.TryParse(Get("markup", "10"), out var d) ? d : 10; set => Set("markup", value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    public int RoundUnit { get => int.TryParse(Get("round_unit", "10"), out var d) ? d : 10; set => Set("round_unit", value.ToString()); }
    public string RoundMode { get => Get("round_mode", "올림"); set => Set("round_mode", value); }
    public int PerPage { get => int.TryParse(Get("per_page", "29"), out var d) && d >= 10 && d <= 40 ? d : 29; set => Set("per_page", value.ToString()); }
    public string VatText { get => Get("vat_text", "(VAT 포함가격임)"); set => Set("vat_text", value); }
    public string BackupDir { get => Get("backup_dir", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "z2roN_Quote", "백업")); set => Set("backup_dir", value); }
    public bool AutoBackup { get => Get("auto_backup", "1") == "1"; set => Set("auto_backup", value ? "1" : "0"); }

    public double MarkupOf(Company c) => c.Markup ?? DefaultMarkup;
    public double PriceFor(Company c, double our) => Pricing.Apply(our, MarkupOf(c), RoundUnit, RoundMode);

    // ---------- 관리자 ----------
    public bool HasAdmin() => Convert.ToInt64(Scalar("SELECT COUNT(*) FROM admin")) > 0;
    static string HashPw(string pw, string salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pw, Convert.FromBase64String(salt), 100000, HashAlgorithmName.SHA256, 32));
    public void SetAdminPassword(string pw)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        Exec("INSERT OR REPLACE INTO admin (id,salt,hash) VALUES (1,$s,$h)", ("$s", salt), ("$h", HashPw(pw, salt)));
    }
    public bool VerifyAdmin(string pw)
    {
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT salt,hash FROM admin WHERE id=1";
        using var r = c.ExecuteReader();
        if (!r.Read()) return false;
        return HashPw(pw ?? "", r.GetString(0)) == r.GetString(1);
    }

    // ---------- 업체 ----------
    static Company ReadCompany(SqliteDataReader r) => new Company
    {
        Id = r.GetInt32(0), Kind = r.GetString(1), Name = r.IsDBNull(2) ? "" : r.GetString(2), BizNo = r.IsDBNull(3) ? "" : r.GetString(3),
        Ceo = r.IsDBNull(4) ? "" : r.GetString(4), BizType = r.IsDBNull(5) ? "" : r.GetString(5), Address = r.IsDBNull(6) ? "" : r.GetString(6),
        Tel = r.IsDBNull(7) ? "" : r.GetString(7), Fax = r.IsDBNull(8) ? "" : r.GetString(8),
        Stamp = r.IsDBNull(9) ? null : (byte[])r.GetValue(9), Markup = r.IsDBNull(10) ? null : r.GetDouble(10), SortNo = r.IsDBNull(11) ? 0 : r.GetInt32(11)
    };
    const string CompCols = "id,kind,name,biz_no,ceo,biz_type,address,tel,fax,stamp,markup,sort_no";

    public Company GetOur()
    {
        using var c = conn.CreateCommand();
        c.CommandText = $"SELECT {CompCols} FROM companies WHERE kind='our' LIMIT 1";
        using var r = c.ExecuteReader();
        r.Read();
        return ReadCompany(r);
    }

    public List<Company> GetOthers()
    {
        var l = new List<Company>();
        using var c = conn.CreateCommand();
        c.CommandText = $"SELECT {CompCols} FROM companies WHERE kind='other' ORDER BY sort_no,id";
        using var r = c.ExecuteReader();
        while (r.Read()) l.Add(ReadCompany(r));
        return l;
    }

    public Company GetCompany(int id)
    {
        using var c = conn.CreateCommand();
        c.CommandText = $"SELECT {CompCols} FROM companies WHERE id=$i";
        c.Parameters.AddWithValue("$i", id);
        using var r = c.ExecuteReader();
        return r.Read() ? ReadCompany(r) : null;
    }

    /// 저장 (Id==0 이면 새 타업체 추가, 10개 미만 제한)
    public int SaveCompany(Company c)
    {
        var ps = new (string, object)[] { ("$n", c.Name), ("$b", c.BizNo), ("$c", c.Ceo), ("$t", c.BizType), ("$a", c.Address), ("$tel", c.Tel), ("$f", c.Fax), ("$s", c.Stamp), ("$m", c.Markup) };
        if (c.Id == 0)
        {
            if (c.Kind == "other" && GetOthers().Count >= MaxOthers) throw new InvalidOperationException($"타업체는 최대 {MaxOthers}개까지 등록할 수 있습니다.");
            Exec("INSERT INTO companies (kind,name,biz_no,ceo,biz_type,address,tel,fax,stamp,markup,sort_no) VALUES ('other',$n,$b,$c,$t,$a,$tel,$f,$s,$m,(SELECT COALESCE(MAX(sort_no),0)+1 FROM companies))", ps);
            c.Id = Convert.ToInt32(Scalar("SELECT last_insert_rowid()"));
        }
        else Exec("UPDATE companies SET name=$n,biz_no=$b,ceo=$c,biz_type=$t,address=$a,tel=$tel,fax=$f,stamp=$s,markup=$m WHERE id=$i", ps.Append(("$i", (object)c.Id)).ToArray());
        return c.Id;
    }

    public void DeleteCompany(int id)
    {
        Exec("DELETE FROM companies WHERE id=$i AND kind='other'", ("$i", id));
        Audit("업체삭제", "id=" + id);
    }

    // ---------- 견적 ----------
    void SaveSnapshot(SqliteTransaction tx, long qid, string role, Company c)
    {
        Run(conn, tx, "INSERT INTO qcompanies (quote_id,role,orig_id,name,biz_no,ceo,biz_type,address,tel,fax,stamp,markup,sort_no) VALUES ($q,$r,$o,$n,$b,$c,$t,$a,$tel,$f,$s,$m,$so)",
            ("$q", qid), ("$r", role), ("$o", c.Id), ("$n", c.Name), ("$b", c.BizNo), ("$c", c.Ceo), ("$t", c.BizType), ("$a", c.Address), ("$tel", c.Tel), ("$f", c.Fax), ("$s", c.Stamp), ("$m", c.Markup), ("$so", c.SortNo));
    }

    public void SaveQuote(Quote q)
    {
        using var tx = conn.BeginTransaction();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var common = new (string, object)[] { ("$r", q.Recipient), ("$d", q.Date.ToString("yyyy-MM-dd")), ("$s", q.Status), ("$n", q.Note), ("$f", q.SourceFile), ("$u", now) };
        if (q.Id == 0)
        {
            Run(conn, tx, "INSERT INTO quotes (recipient,qdate,status,note,source,created,updated) VALUES ($r,$d,$s,$n,$f,$u,$u)", common);
            using var c = conn.CreateCommand(); c.Transaction = tx; c.CommandText = "SELECT last_insert_rowid()";
            q.Id = Convert.ToInt64(c.ExecuteScalar());
        }
        else
        {
            Run(conn, tx, "UPDATE quotes SET recipient=$r,qdate=$d,status=$s,note=$n,source=$f,updated=$u WHERE id=$i", common.Append(("$i", (object)q.Id)).ToArray());
            Run(conn, tx, "DELETE FROM qcompanies WHERE quote_id=$q", ("$q", q.Id));
            Run(conn, tx, "DELETE FROM qitems WHERE quote_id=$q", ("$q", q.Id));
            Run(conn, tx, "DELETE FROM qprices WHERE quote_id=$q", ("$q", q.Id));
        }
        SaveSnapshot(tx, q.Id, "our", q.Our);
        foreach (var o in q.Others) SaveSnapshot(tx, q.Id, "other", o);
        for (int i = 0; i < q.Items.Count; i++)
        {
            var it = q.Items[i];
            Run(conn, tx, "INSERT INTO qitems (quote_id,seq,barcode,name,unit,qty,our_price,note) VALUES ($q,$s,$b,$n,$u,$a,$p,$t)",
                ("$q", q.Id), ("$s", i + 1), ("$b", it.Barcode), ("$n", it.Name), ("$u", it.Unit), ("$a", it.Qty), ("$p", it.OurPrice), ("$t", it.Note));
            foreach (var kv in it.Prices)
                Run(conn, tx, "INSERT INTO qprices (quote_id,seq,company_id,price,manual) VALUES ($q,$s,$c,$p,$m)",
                    ("$q", q.Id), ("$s", i + 1), ("$c", kv.Key), ("$p", kv.Value), ("$m", it.Manual.Contains(kv.Key) ? 1 : 0));
        }
        tx.Commit();
        q.Updated = DateTime.Now;
    }

    public Quote LoadQuote(long id)
    {
        Quote q;
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id,recipient,qdate,status,note,source,created,updated FROM quotes WHERE id=$i";
            c.Parameters.AddWithValue("$i", id);
            using var r = c.ExecuteReader();
            if (!r.Read()) return null;
            q = new Quote { Id = r.GetInt64(0), Recipient = r.GetString(1), Status = r.GetString(3), Note = r.IsDBNull(4) ? "" : r.GetString(4), SourceFile = r.IsDBNull(5) ? "" : r.GetString(5) };
            DateTime.TryParse(r.GetString(2), out q.Date);
            DateTime.TryParse(r.GetString(6), out q.Created); DateTime.TryParse(r.GetString(7), out q.Updated);
        }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT role,orig_id,name,biz_no,ceo,biz_type,address,tel,fax,stamp,markup,sort_no FROM qcompanies WHERE quote_id=$i ORDER BY sort_no,orig_id";
            c.Parameters.AddWithValue("$i", id);
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                string S(int k) => r.IsDBNull(k) ? "" : r.GetString(k);
                var co = new Company { Kind = r.GetString(0), Id = r.GetInt32(1), Name = S(2), BizNo = S(3), Ceo = S(4), BizType = S(5), Address = S(6), Tel = S(7), Fax = S(8), Stamp = r.IsDBNull(9) ? null : (byte[])r.GetValue(9), Markup = r.IsDBNull(10) ? null : r.GetDouble(10), SortNo = r.IsDBNull(11) ? 0 : r.GetInt32(11) };
                if (co.Kind == "our") q.Our = co; else q.Others.Add(co);
            }
        }
        q.Our ??= GetOur();
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT seq,barcode,name,unit,qty,our_price,note FROM qitems WHERE quote_id=$i ORDER BY seq";
            c.Parameters.AddWithValue("$i", id);
            using var r = c.ExecuteReader();
            while (r.Read())
                q.Items.Add(new QItem { Barcode = r.IsDBNull(1) ? "" : r.GetString(1), Name = r.IsDBNull(2) ? "" : r.GetString(2), Unit = r.IsDBNull(3) ? "" : r.GetString(3), Qty = r.IsDBNull(4) ? 0 : r.GetDouble(4), OurPrice = r.IsDBNull(5) ? 0 : r.GetDouble(5), Note = r.IsDBNull(6) ? "" : r.GetString(6) });
        }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT seq,company_id,price,manual FROM qprices WHERE quote_id=$i";
            c.Parameters.AddWithValue("$i", id);
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                int seq = r.GetInt32(0) - 1;
                if (seq < 0 || seq >= q.Items.Count) continue;
                q.Items[seq].Prices[r.GetInt32(1)] = r.GetDouble(2);
                if (r.GetInt32(3) == 1) q.Items[seq].Manual.Add(r.GetInt32(1));
            }
        }
        return q;
    }

    public class QuoteRow { public long Id; public string Recipient, Date, Status, Updated; public long Items; public double Total; }

    public List<QuoteRow> ListQuotes(string filter)
    {
        var l = new List<QuoteRow>();
        using var c = conn.CreateCommand();
        c.CommandText = @"SELECT q.id,q.recipient,q.qdate,q.status,q.updated,
            (SELECT COUNT(*) FROM qitems i WHERE i.quote_id=q.id),
            (SELECT COALESCE(SUM(qty*our_price),0) FROM qitems i WHERE i.quote_id=q.id)
            FROM quotes q WHERE ($f='' OR q.recipient LIKE '%'||$f||'%' OR q.status=$f) ORDER BY q.id DESC";
        c.Parameters.AddWithValue("$f", filter ?? "");
        using var r = c.ExecuteReader();
        while (r.Read()) l.Add(new QuoteRow { Id = r.GetInt64(0), Recipient = r.GetString(1), Date = r.GetString(2), Status = r.GetString(3), Updated = r.GetString(4), Items = r.GetInt64(5), Total = r.GetDouble(6) });
        return l;
    }

    public void DeleteQuote(long id)
    {
        using var tx = conn.BeginTransaction();
        foreach (var t in new[] { "qcompanies", "qitems", "qprices" }) Run(conn, tx, $"DELETE FROM {t} WHERE quote_id=$q", ("$q", id));
        Run(conn, tx, "DELETE FROM quotes WHERE id=$q", ("$q", id));
        tx.Commit();
        Audit("견적삭제", "id=" + id);
    }

    // ---------- 바코드 상품 ----------
    public Product GetProduct(string barcode)
    {
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT barcode,name,unit,price,note,updated FROM products WHERE barcode=$b";
        c.Parameters.AddWithValue("$b", barcode);
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        return new Product { Barcode = r.GetString(0), Name = r.IsDBNull(1) ? "" : r.GetString(1), Unit = r.IsDBNull(2) ? "" : r.GetString(2), Price = r.IsDBNull(3) ? 0 : r.GetDouble(3), Note = r.IsDBNull(4) ? "" : r.GetString(4), Updated = r.IsDBNull(5) ? "" : r.GetString(5) };
    }

    public void SaveProduct(Product p) =>
        Exec("INSERT OR REPLACE INTO products (barcode,name,unit,price,note,updated) VALUES ($b,$n,$u,$p,$t,$d)",
            ("$b", p.Barcode), ("$n", p.Name), ("$u", p.Unit), ("$p", p.Price), ("$t", p.Note), ("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

    public List<Product> ListProducts(string filter)
    {
        var l = new List<Product>();
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT barcode,name,unit,price,note,updated FROM products WHERE ($f='' OR barcode LIKE '%'||$f||'%' OR name LIKE '%'||$f||'%') ORDER BY updated DESC LIMIT 5000";
        c.Parameters.AddWithValue("$f", filter ?? "");
        using var r = c.ExecuteReader();
        while (r.Read()) l.Add(new Product { Barcode = r.GetString(0), Name = r.IsDBNull(1) ? "" : r.GetString(1), Unit = r.IsDBNull(2) ? "" : r.GetString(2), Price = r.IsDBNull(3) ? 0 : r.GetDouble(3), Note = r.IsDBNull(4) ? "" : r.GetString(4), Updated = r.IsDBNull(5) ? "" : r.GetString(5) });
        return l;
    }

    public void DeleteProduct(string barcode) { Exec("DELETE FROM products WHERE barcode=$b", ("$b", barcode)); Audit("바코드삭제", barcode); }

    /// 견적 품목 중 바코드가 있는 것을 상품 목록에 수집(없으면 추가, 있으면 이름/단위/단가 갱신)
    public int CollectBarcodes(IEnumerable<QItem> items)
    {
        int n = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Barcode)) continue;
            SaveProduct(new Product { Barcode = it.Barcode.Trim(), Name = it.Name, Unit = it.Unit, Price = it.OurPrice, Note = it.Note });
            n++;
        }
        return n;
    }

    // ---------- 백업 / 복원 ----------
    public string Backup(string dir = null)
    {
        dir ??= BackupDir;
        Directory.CreateDirectory(dir);
        string f = System.IO.Path.Combine(dir, $"견적백업_{DateTime.Now:yyyyMMdd_HHmmss}.zqdb");
        Exec($"VACUUM INTO '{f.Replace("'", "''")}'");
        return f;
    }

    public static List<FileInfo> BackupFiles(string dir) =>
        Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("*.zqdb").OrderByDescending(f => f.LastWriteTime).ToList() : new List<FileInfo>();

    /// 백업 파일로 되돌리기 (현재 자료는 .before_restore 로 보관)
    public void Restore(string backupFile)
    {
        string path = Path;
        Dispose();
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".before_restore", true);
            foreach (var ext in new[] { "-wal", "-shm" }) { try { File.Delete(path + ext); } catch { } }
            File.Copy(backupFile, path, true);
        }
        finally
        {
            conn = new SqliteConnection("Data Source=" + path);
            conn.Open();
            Exec("PRAGMA journal_mode=WAL");
            CreateSchema();
        }
        Audit("복원", System.IO.Path.GetFileName(backupFile));
    }
}
