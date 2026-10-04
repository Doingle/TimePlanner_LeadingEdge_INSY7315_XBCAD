using ClosedXML.Excel;
using TimePlanner.Core.Services;

namespace TimePlanner.Dashboard.Services.Reports
{
    //-----------------------------
    //builds an excel workbook in the company timesheet layout
    public static class TimesheetWorkbook
    {
        private static readonly string[] Palette = ["#FFF2CC", "#E2EFDA", "#E4DFEC", "#DDEBF7", "#FCE4D6", "#EDEDED"];

        //-----------------------------
        //builds excel workbook bytes for a user and date range
        public static byte[] Build(string personName, DateTime from, DateTime to, IReadOnlyList<ReportService.ReportEntry> entries, ActivityLookup activities)
        {
            using var workbook = new XLWorkbook();
            var name = string.IsNullOrWhiteSpace(personName) ? "Timesheet" : personName.Trim();

            var startMonth = new DateTime(from.Year, from.Month, 1);
            var endMonth = new DateTime(to.Year, to.Month, 1);
            var spansTwoYears = from.Year != to.Year;

            var monthDates = new List<DateTime>();
            var current = startMonth;

            //builds list of months in range
            while (current <= endMonth)
            {
                monthDates.Add(current);
                current = current.AddMonths(1);
            }

            foreach (var monthDate in monthDates)
            {
                var monthName = spansTwoYears ? monthDate.ToString("MMMM yyyy") : monthDate.ToString("MMMM");
                var cleanName = CleanSheetName(monthName);
                var sheet = workbook.Worksheets.Add(cleanName);

                //title row merged across B1:I1
                var titleRange = sheet.Range("B1:I1");
                titleRange.Merge();
                titleRange.Value = $"{name} Time Log";
                titleRange.Style.Font.Bold = true;
                titleRange.Style.Font.FontSize = 16;
                titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                sheet.Row(1).Height = 21;

                //header row in B2:I2
                string[] headers = ["Date", "Activity/Task", "Client / Project", "Start Time", "End Time", "Duration (hours)", "Notes", "Billable"];
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = sheet.Cell(2, i + 2);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Alignment.WrapText = true;
                }
                sheet.Row(2).Height = 30;

                var monthEntries = entries
                    .Where(e => e.Start.Year == monthDate.Year && e.Start.Month == monthDate.Month)
                    .OrderBy(e => e.Start)
                    .ToList();

                //assign company fills in order of appearance
                var companyFills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var companyOrder = new List<string>();
                var paletteIndex = 0;

                foreach (var e in monthEntries)
                {
                    //tracks new companies and assigns colors
                    if (!companyOrder.Contains(e.Company, StringComparer.OrdinalIgnoreCase))
                    {
                        companyOrder.Add(e.Company);

                        //internal company gets no fill
                        if (!string.Equals(e.Company, LocalSetupService.InternalCompanyName, StringComparison.OrdinalIgnoreCase))
                        {
                            companyFills[e.Company] = Palette[paletteIndex % Palette.Length];
                            paletteIndex++;
                        }
                    }
                }

                //write entries starting at row 3
                var row = 3;
                foreach (var e in monthEntries)
                {
                    var path = activities.Path(e.CategoryId);
                    var taskOrNote = string.IsNullOrWhiteSpace(e.Note) ? path : e.Note;
                    var clientProject = ReportService.ClientProjectText(e);
                    var billable = ReportService.BillableLabel(e, activities);
                    var duration = e.End - e.Start;

                    var cellB = sheet.Cell(row, 2);
                    cellB.SetValue(e.Start.Date);
                    cellB.Style.DateFormat.Format = "dd/mm/yy";

                    sheet.Cell(row, 3).SetValue(taskOrNote);
                    sheet.Cell(row, 4).SetValue(clientProject);

                    var cellE = sheet.Cell(row, 5);
                    cellE.Value = e.Start.TimeOfDay;
                    cellE.Style.DateFormat.Format = "h:mm";

                    var cellF = sheet.Cell(row, 6);
                    cellF.Value = e.End.TimeOfDay;
                    cellF.Style.DateFormat.Format = "h:mm";

                    var cellG = sheet.Cell(row, 7);
                    cellG.Value = duration;
                    cellG.Style.DateFormat.Format = "[h]:mm";

                    sheet.Cell(row, 8).SetValue(path);
                    sheet.Cell(row, 9).SetValue(billable);

                    //applies company fill color
                    if (companyFills.TryGetValue(e.Company, out var fillColor))
                    {
                        sheet.Range(row, 2, row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                    }

                    row++;
                }

                //legend in column K
                sheet.Cell("K1").Value = "Legend";
                sheet.Cell("K1").Style.Font.Bold = true;
                var legendRow = 2;
                foreach (var comp in companyOrder)
                {
                    var cell = sheet.Cell(legendRow, 11);
                    cell.SetValue(comp);

                    //applies company fill to legend cell
                    if (companyFills.TryGetValue(comp, out var fillColor))
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(fillColor);
                    }
                    legendRow++;
                }

                //column widths
                sheet.Column("B").Width = 11;
                sheet.Column("C").Width = 60;
                sheet.Column("D").Width = 30;
                sheet.Column("E").Width = 8;
                sheet.Column("F").Width = 8;
                sheet.Column("G").Width = 10;
                sheet.Column("H").Width = 40;
                sheet.Column("I").Width = 10;
                sheet.Column("K").Width = 28;

                //freeze panes below row 2
                sheet.SheetView.FreezeRows(2);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        //-----------------------------
        //trims sheet name and strips invalid characters
        private static string CleanSheetName(string name)
        {
            var invalidChars = new[] { '[', ']', ':', '*', '?', '/', '\\' };
            var cleaned = new string(name.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();

            //truncates to 31 characters
            if (cleaned.Length > 31)
            {
                return cleaned[..31];
            }

            //empty name falls back to sheet
            if (cleaned.Length == 0)
            {
                return "Sheet";
            }

            return cleaned;
        }
    }
}
//------------------------------EOF-----------------------------\\
