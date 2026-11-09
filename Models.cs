namespace ZQuote;

public class Company
{
    public int Id;                 // 원본 업체 번호 (견적 안에서는 가격 열의 키)
    public string Kind = "other";  // our | other
    public string Name = "", BizNo = "", Ceo = "", BizType = "", Address = "", Tel = "", Fax = "";
    public byte[] Stamp;           // 도장 이미지(PNG)
    public double? Markup;         // 우리 단가 대비 인상률 %, null 이면 기본값
    public int SortNo;
    public Company Clone() => (Company)MemberwiseClone();
    public override string ToString() => Name;
}

public class QItem
{
    public string Name = "", Unit = "", Note = "", Barcode = "";
    public double Qty, OurPrice;
    public Dictionary<int, double> Prices = new();   // 타업체 번호 → 단가
    public HashSet<int> Manual = new();             // 직접 고친 타업체 단가
}

public class Quote
{
    public long Id;
    public string Recipient = "", Status = "작성중", Note = "", SourceFile = "";
    public DateTime Date = DateTime.Today, Created = DateTime.Now, Updated = DateTime.Now;
    public Company Our;                              // 견적 시점의 우리 업체 정보(복사본)
    public List<Company> Others = new();             // 견적 시점의 타업체 정보(복사본)
    public List<QItem> Items = new();
    public bool Completed => Status == "완료";
}

public class Product
{
    public string Barcode = "", Name = "", Unit = "", Note = "";
    public double Price;
    public string Updated = "";
}

public static class Pricing
{
    /// 우리 단가에 인상률을 적용하고 단위(1/10/100)로 올림/반올림/버림.
    public static double Apply(double our, double pct, int unit, string mode)
    {
        if (our <= 0) return 0;
        double v = our * (1 + pct / 100.0);
        if (unit < 1) unit = 1;
        double q = v / unit;
        q = mode == "올림" ? Math.Ceiling(q - 1e-9) : mode == "버림" ? Math.Floor(q + 1e-9) : Math.Round(q, MidpointRounding.AwayFromZero);
        return q * unit;
    }
}
