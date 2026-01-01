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
        Skip,
        Grand_Total
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
        //"certificate of deposit",
        //"commercial paper",
        //"treasury bill",
        "others",
        "reit/invit instrument",
        "corporate debt market development fund",
        "exchange traded fund",
        "interest rate swaps",
        //"securitised debt",
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
        "riskometer",
        "note",
        "notes",
        "disclaimer",
        "footnote",
        "net receivable"
    };

        // =============================
        // ENTRY POINT
        // =============================

        public static SemanticRowResult Analyze(List<string> row, HashSet<string> validFundNames)
        {
            //Console.WriteLine("Row Data: " + string.Join(", ", row));
            var cleaned = row
                .Select(c => c?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            if (cleaned.Count == 0)
                return new SemanticRowResult { Type = SemanticRowType.Empty };

            string joined = string.Join(" ", cleaned).ToLower();

            // 1️⃣ FUND-LIKE ROW (AMC or Section decided later)
            //if (LooksLikeFundName(cleaned))
            //{
            //    return new SemanticRowResult
            //    {
            //        Type = SemanticRowType.AmcName, // tentative
            //        Value = cleaned[0],
            //        LooksLikeFund = true
            //    };
            //}

            string foundFundName = GetFundNameFromRow(row, validFundNames);
            if (foundFundName != null)
            {

                return new SemanticRowResult
                {
                    Type = SemanticRowType.AmcName,
                    Value = foundFundName // This will be "Bajaj Finserv..." (not "BFBPSU")
                };
            }

            // 2️⃣ AS-ON DATE
            if (IsAsOnDate(joined))
                return new SemanticRowResult
                {
                    Type = SemanticRowType.AsOnDate,
                    Value = cleaned[0]
                };

            if (IsGrandTotal(cleaned))
            {
                return new SemanticRowResult
                {
                    Type = SemanticRowType.Grand_Total,
                    Value = cleaned[0]
                };
            }

            // 6️⃣ SKIP (LAST)
            if (SkipKeywords.Any(k => joined.Contains(k)))
                return new SemanticRowResult { Type = SemanticRowType.Skip };

            // 5️⃣ DATA
            if (IsDataRow(cleaned))
                return new SemanticRowResult { Type = SemanticRowType.Data };

            // 3️⃣ SECTION
            foreach (var cell in cleaned)
            {
                // Check if THIS specific cell contains a section keyword
                foreach (var s in SectionKeywords)
                {
                    if (cell.ToLower().Contains(s))
                    {
                        return new SemanticRowResult
                        {
                            Type = SemanticRowType.Section,
                            Value = cell // ✅ Return "Debt Instruments", not "GOI4584"
                        };
                    }
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

        private static bool IsGrandTotal(List<string?> row)
        {
            // 1. Check if the text contains "grand total" (Case insensitive)
            string joined = string.Join(" ", row).ToLower();
            if (!joined.Contains("grand total"))
                return false;

            // 2. Ensure there is actually a numeric value in this row (the total amount)
            // This prevents matching a footer note text that just mentions "Grand Total"
            bool hasNumber = row.Any(c =>
                decimal.TryParse(c?.Replace(",", "").Replace("%", "").Trim(), out _)
            );

            return hasNumber;
        }

        // =============================
        // HELPERS
        // =============================

        //private static bool LooksLikeFundName(List<string> row)
        //{
        //    string value = row[0].ToLower();
        //    return row.Count == 1 &&
        //           (value.Contains("mutual fund") ||
        //            value.Contains("fund")); ;
        //}

        //private static string GetFundNameFromRow(List<string> row)
        //{
        //    var cells = row.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        //    // Safety: AMC Header usually doesn't have more than 2-3 bits of info (Code + Name)
        //    // If a row has 5+ columns, it's likely a Data Row, not a Fund Name header.
        //    if (cells.Count == 0 || cells.Count > 3) return null;

        //    foreach (var cell in cells)
        //    {
        //        //Console.WriteLine("Cell: " + cell);

        //        string val = cell.ToLower();

        //        // Check for keywords in THIS specific cell
        //        if (val.Contains("mutual fund") || val.Contains("fund"))
        //        {
        //            // Return the cell that actually holds the name (e.g. the 2nd column)
        //            return cell;
        //        }
        //    }

        //    return null; // No fund name found
        //}

        private static string GetFundNameFromRow(List<string> row, HashSet<string> validFundNames)
        {
            var cells = row.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            // Safety: Fund Name headers are usually short (1-3 cells max)
            if (cells.Count == 0 || cells.Count > 3) return null;

            foreach (var cell in cells)
            {

                string normalizedCell = NormalizeNameAggressive(cell);

                // 2. CHECK: Does this exist in our whitelist?
                if (validFundNames.Contains(normalizedCell))
                {
                    return cell; // ✅ FOUND IT! Return the original string.
                }

                // If exact match fails, check if the DB name contains this cell or vice versa
                // useful if Excel has "Axis Bluechip" but DB has "Axis Bluechip Fund"
                bool partialMatch = validFundNames.Any(dbName => dbName.Contains(normalizedCell) || normalizedCell.Contains(dbName));
                if (partialMatch) return cell;
            }

            return null;
        }

        public static string NormalizeNameAggressive(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string str = input.ToLower();

            // Standardize Months
            str = str.Replace("january", "jan").Replace("february", "feb").Replace("march", "mar")
                     .Replace("april", "apr").Replace("june", "jun").Replace("july", "jul")
                     .Replace("august", "aug").Replace("september", "sep").Replace("sept", "sep")
                     .Replace("october", "oct").Replace("november", "nov").Replace("december", "dec");

            // Replace separators
            str = str.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Replace(':', ' ');

            // Split letter/number (IBX50 -> IBX 50)
            str = System.Text.RegularExpressions.Regex.Replace(str, @"([a-z])([0-9])", "$1 $2");
            str = System.Text.RegularExpressions.Regex.Replace(str, @"([0-9])([a-z])", "$1 $2");

            // Remove special chars
            str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s]", "");

            // Collapse spaces
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ");

            return str.Trim();
        }
        public static bool IsAsOnDate(string value)
        {
            return TryParseAsOnDate(value, out _);
        }


        public static bool TryParseAsOnDate(string value, out DateTime date)
        {
            date = DateTime.MinValue;
            string lowerValue = value.ToLower();

            // Must contain as on / ended
            if (!lowerValue.Contains("as on") && !lowerValue.Contains("ended") && !lowerValue.Contains("date"))
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


        public static bool IsDataRow(List<string> row)
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

