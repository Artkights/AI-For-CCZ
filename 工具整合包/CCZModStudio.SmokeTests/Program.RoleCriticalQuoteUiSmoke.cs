using CCZModStudio;
using CCZModStudio.Core;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

internal partial class Program
{
    static void RunRoleCriticalQuoteUiSmoke()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                using var form = new MainForm();

                var genericRows = BuildCriticalQuoteRows((24, "一鼓作气"), (25, "乘胜追击"), (26, "破阵斩将"));
                var genericMapping = new RoleCriticalQuoteMapping(
                    RoleId: 100,
                    FieldValue: 1,
                    QuoteIds: genericRows.Select(row => Convert.ToInt32(row["ID"], CultureInfo.InvariantCulture)).ToArray(),
                    QuoteRows: genericRows,
                    IsSpecialRoleQuote: false,
                    Explanation: "generic smoke");

                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowCriticalQuoteEditor", genericMapping);
                var boxes = GetPrivateFieldForRoleCriticalQuoteSmoke<TextBox[]>(form, "_roleCriticalQuoteBoxes");
                var labels = GetPrivateFieldForRoleCriticalQuoteSmoke<Label[]>(form, "_roleCriticalQuoteLabels");
                AssertCriticalQuoteBox(boxes, labels, 0, "第1句 #24", "一鼓作气", enabled: true);
                AssertCriticalQuoteBox(boxes, labels, 1, "第2句 #25", "乘胜追击", enabled: true);
                AssertCriticalQuoteBox(boxes, labels, 2, "第3句 #26", "破阵斩将", enabled: true);

                boxes[0].Text = "改一";
                boxes[1].Text = "改二";
                boxes[2].Text = "改三";
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ApplyCriticalQuoteEditorToRows", genericMapping);
                AssertRowText(genericRows[0], "改一");
                AssertRowText(genericRows[1], "改二");
                AssertRowText(genericRows[2], "改三");

                var specialRows = BuildCriticalQuoteRows((7, "特殊一击"));
                var specialMapping = new RoleCriticalQuoteMapping(
                    RoleId: 7,
                    FieldValue: 0,
                    QuoteIds: new[] { 7 },
                    QuoteRows: specialRows,
                    IsSpecialRoleQuote: true,
                    Explanation: "special smoke");
                InvokePrivateForRoleCriticalQuoteSmoke(form, "ShowCriticalQuoteEditor", specialMapping);
                AssertCriticalQuoteBox(boxes, labels, 0, "特殊台词 #7", "特殊一击", enabled: true);
                AssertCriticalQuoteBox(boxes, labels, 1, "第2句", string.Empty, enabled: false);
                AssertCriticalQuoteBox(boxes, labels, 2, "第3句", string.Empty, enabled: false);

                Console.WriteLine("ROLE_CRITICAL_QUOTE_UI_SMOKE_OK generic=3 special=1");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new InvalidOperationException("Role critical quote UI smoke failed.", failure);
        }
    }

    private static List<DataRow> BuildCriticalQuoteRows(params (int Id, string Text)[] rows)
    {
        var table = new DataTable("CriticalQuoteSmoke");
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("介绍", typeof(string));

        var result = new List<DataRow>();
        foreach (var row in rows)
        {
            var dataRow = table.NewRow();
            dataRow["ID"] = row.Id;
            dataRow["介绍"] = row.Text;
            table.Rows.Add(dataRow);
            result.Add(dataRow);
        }

        table.AcceptChanges();
        return result;
    }

    private static void AssertCriticalQuoteBox(TextBox[] boxes, Label[] labels, int index, string expectedLabel, string expectedText, bool enabled)
    {
        if (!string.Equals(labels[index].Text, expectedLabel, StringComparison.Ordinal) ||
            !string.Equals(boxes[index].Text, expectedText, StringComparison.Ordinal) ||
            boxes[index].Enabled != enabled ||
            boxes[index].ReadOnly == enabled)
        {
            throw new InvalidOperationException(
                $"Critical quote box mismatch index={index}: label={labels[index].Text}, text={boxes[index].Text}, enabled={boxes[index].Enabled}, readOnly={boxes[index].ReadOnly}");
        }
    }

    private static void AssertRowText(DataRow row, string expected)
    {
        var actual = Convert.ToString(row["介绍"], CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Critical quote row mismatch: expected={expected}, actual={actual}");
        }
    }

    private static T GetPrivateFieldForRoleCriticalQuoteSmoke<T>(MainForm form, string fieldName)
    {
        var field = typeof(MainForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: " + fieldName);
        return (T)field.GetValue(form)!;
    }

    private static void InvokePrivateForRoleCriticalQuoteSmoke(MainForm form, string methodName, params object?[] args)
    {
        var method = typeof(MainForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        method.Invoke(form, args);
    }
}
