using System.Text.RegularExpressions;

namespace mutual_fund_backend.Services
{
    public enum SemanticRowType
    {
        Empty,
        AmcName,
        AsOnDate,
        Section,
        Header,
        Data,
        Skip
    }

    public class SemanticRowResult
    {
        public SemanticRowType Type { get; set; }
        public string Value { get; set; }

        public bool LooksLikeFund { get; set; }
    }

    public class ExcelSemanticStructureDetector
    {
        private static readonly string[] SectionKeywords =
        {
        "equity & equity related",
        "reverse repo",
        "debt instrument",
        "money market instrument",
        "certificate of deposit",
        "commercial paper",
        "net receivable / (payables)",
        "treasury bill",
        "others",
        "reit/invit instrument",
        "corporate debt market development fund",
        "exchange traded fund",
        "interest rate swaps",
        "securitised debt",
        "mutual fund units",
        "commodities related",
        "government securities"
    };

        private static readonly string[] HeaderKeywords =
        {
        "isin",
        "quantity",
        "market",
        "fair value",
        "instrument",
        "rating",
        "industry",
        "yield",
        "nav",
        "rounded % to net assets"
    };

        private static readonly string[] SkipKeywords =
        {
        "total",
        "sub total",
        "subtotal",
        "grand total",
        "riskometer",
        "note",
        "notes",
        "disclaimer",
        "footnote"
    };

        // =============================
        // ENTRY POINT
        // =============================

        public static SemanticRowResult Analyze(List<string> row)
        {
            var cleaned = row
                .Select(c => c?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            if (cleaned.Count == 0)
                return new SemanticRowResult { Type = SemanticRowType.Empty };

            string joined = string.Join(" ", cleaned).ToLower();

            // 1️⃣ FUND-LIKE ROW (AMC or Section decided later)
            if (LooksLikeFundName(cleaned))
            {
                return new SemanticRowResult
                {
                    Type = SemanticRowType.AmcName, // tentative
                    Value = cleaned[0],
                    LooksLikeFund = true
                };
            }


            // 2️⃣ AS-ON DATE
            if (IsAsOnDate(joined))
                return new SemanticRowResult
                {
                    Type = SemanticRowType.AsOnDate,
                    Value = cleaned[0]
                };

            // 6️⃣ SKIP (LAST)
            if (SkipKeywords.Any(k => joined.Contains(k)))
                return new SemanticRowResult { Type = SemanticRowType.Skip };

            // 5️⃣ DATA
            if (IsDataRow(cleaned))
                return new SemanticRowResult { Type = SemanticRowType.Data };

            // 3️⃣ SECTION
            foreach (var s in SectionKeywords)
            {
                if (joined.Contains(s))
                {
                    return new SemanticRowResult
                    {
                        Type = SemanticRowType.Section,
                        Value = cleaned[0]
                    };
                }
            }

            // 4️⃣ HEADER
            int headerMatches = cleaned.Count(cell =>
                HeaderKeywords.Any(h => cell.ToLower().Contains(h)));

            if (headerMatches >= 3)
                return new SemanticRowResult { Type = SemanticRowType.Header };

           

           

            // 7️⃣ DEFAULT
            return new SemanticRowResult { Type = SemanticRowType.Skip };
        }

        // =============================
        // HELPERS
        // =============================

        private static bool LooksLikeFundName(List<string> row)
        {
            string value = row[0].ToLower();
            return row.Count == 1 &&
                   (value.Contains("mutual fund") ||
                    value.Contains("fund")); ;
        }


        private static bool IsAsOnDate(string value)
        {
            return TryParseAsOnDate(value, out _);
        }


        private static bool TryParseAsOnDate(string value, out DateTime date)
        {
            date = DateTime.MinValue;

            // Must contain as on / ended
            if (!value.Contains("as on"))
                return false;

            // 🔍 Extract date-like part ONLY
            var match = Regex.Match(
                value,
                @"(\d{1,2}[-/\.]\d{1,2}[-/\.]\d{4})|" +          // 30-11-2025
                @"(\d{4}[-/\.]\d{1,2}[-/\.]\d{1,2})|" +          // 2025-11-30
                @"((jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\s+\d{1,2},?\s*\d{4})|" +
                @"(\d{1,2}\s+(jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\s+\d{4})",
                RegexOptions.IgnoreCase
            );


            if (!match.Success)
            {
                //Console.Write("As On Date:", value);

                return false;
            }

            string dateText = match.Value;

            string[] formats =
            {
        "dd-MM-yyyy",
        "dd/MM/yyyy",
        "dd.MM.yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd MMM yyyy",
        "dd MMMM yyyy",
        "MMM dd, yyyy",
        "MMMM dd, yyyy",
        "MMM dd yyyy",
        "MMMM dd yyyy"
    };

            return DateTime.TryParseExact(
                dateText,
                formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out date
            )
            || DateTime.TryParse(dateText, out date);
        }


        private static bool IsDataRow(List<string> row)
        {
            // Clean cells
            var cells = row
                .Select(c => c?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            // 1️ Must have enough columns
            if (cells.Count < 3)
                return false;

            // 2️ Must contain instrument name (text-heavy cell)
            bool hasTextCell = cells.Any(c =>
                c.Any(char.IsLetter) && c.Length > 3);

            if (!hasTextCell)
                return false;

            // 3️ Must contain numeric values (quantity / value / %)
            int numericCount = cells.Count(c =>
                decimal.TryParse(
                    c.Replace(",", "").Replace("%", ""),
                    out _));

            if (numericCount < 2)
                return false;

            return true;
        }



    }
}

