namespace Recon.Web.Domain.Reference;

/// <summary>ISO 4217 numeric currency reference, used to make source-file codes readable.</summary>
public class IsoCurrency
{
    public int NumericCode { get; set; }
    public string AlphaCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public static class IsoCurrencyCatalog
{
    // Includes every numeric currency observed in the March interchange export.
    public static readonly (int Numeric, string Alpha, string Name)[] Entries =
    [
        (8,"ALL","Albanian lek"),(36,"AUD","Australian dollar"),(72,"BWP","Botswana pula"),(116,"KHR","Cambodian riel"),(124,"CAD","Canadian dollar"),(144,"LKR","Sri Lankan rupee"),(152,"CLP","Chilean peso"),(156,"CNY","Chinese yuan"),(188,"CRC","Costa Rican colón"),(208,"DKK","Danish krone"),(230,"ETB","Ethiopian birr"),(242,"FJD","Fijian dollar"),(344,"HKD","Hong Kong dollar"),(356,"INR","Indian rupee"),(360,"IDR","Indonesian rupiah"),(376,"ILS","Israeli new shekel"),(392,"JPY","Japanese yen"),(398,"KZT","Kazakhstani tenge"),(404,"KES","Kenyan shilling"),(410,"KRW","South Korean won"),(454,"MWK","Malawian kwacha"),(458,"MYR","Malaysian ringgit"),(480,"MUR","Mauritian rupee"),(504,"MAD","Moroccan dirham"),(516,"NAD","Namibian dollar"),(554,"NZD","New Zealand dollar"),(586,"PKR","Pakistani rupee"),(608,"PHP","Philippine peso"),(634,"QAR","Qatari riyal"),(646,"RWF","Rwandan franc"),(682,"SAR","Saudi riyal"),(690,"SCR","Seychellois rupee"),(702,"SGD","Singapore dollar"),(704,"VND","Vietnamese đồng"),(710,"ZAR","South African rand"),(756,"CHF","Swiss franc"),(764,"THB","Thai baht"),(784,"AED","UAE dirham"),(788,"TND","Tunisian dinar"),(800,"UGX","Ugandan shilling"),(818,"EGP","Egyptian pound"),(826,"GBP","Pound sterling"),(834,"TZS","Tanzanian shilling"),(840,"USD","US dollar"),(936,"GHS","Ghanaian cedi"),(943,"AOA","Angolan kwanza"),(946,"RON","Romanian leu"),(949,"TRY","Turkish lira"),(950,"XAF","Central African CFA franc"),(952,"XOF","West African CFA franc"),(967,"ZMW","Zambian kwacha"),(969,"MGA","Malagasy ariary"),(976,"CDF","Congolese franc"),(978,"EUR","Euro"),(986,"BRL","Brazilian real")
    ];

    public static string Display(int? numeric) => numeric is null ? "—" : Entries.FirstOrDefault(x => x.Numeric == numeric.Value) is var c && c.Numeric != 0 ? $"{c.Alpha} ({numeric})" : numeric.Value.ToString();
}
