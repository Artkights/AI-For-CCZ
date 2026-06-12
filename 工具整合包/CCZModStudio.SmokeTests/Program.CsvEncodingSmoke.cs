using CCZModStudio.Core;
using CCZModStudio.Models;
using System.Data;
using System.Text;

internal partial class Program
{
    static void RunCsvEncodingSmoke()
    {
        var smokeDir = Path.Combine(Path.GetTempPath(), "CCZModStudio_CsvEncodingSmoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeDir);

        var table = new DataTable("CSV编码烟测");
        table.Columns.Add("ID");
        table.Columns.Add("名称");
        table.Columns.Add("武力");
        table.Rows.Add("0", "主角", "40");

        var gbkPath = Path.Combine(smokeDir, "gbk.csv");
        File.WriteAllBytes(gbkPath, EncodingService.Gbk.GetBytes("ID,名称,武力\r\n0,测试角色,99\r\n"));

        var importedGbk = CsvService.ImportInto(table, gbkPath, allowPartialColumns: true, matchByIdWhenPresent: true);
        if (importedGbk != 1 ||
            Convert.ToString(table.Rows[0]["名称"]) != "测试角色" ||
            Convert.ToString(table.Rows[0]["武力"]) != "99")
        {
            throw new InvalidOperationException("GBK CSV 导入烟测失败。");
        }

        var utf8Path = Path.Combine(smokeDir, "utf8.csv");
        File.WriteAllText(utf8Path, "ID,名称,武力\r\n0,UTF8角色,88\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var importedUtf8 = CsvService.ImportInto(table, utf8Path, allowPartialColumns: true, matchByIdWhenPresent: true);
        if (importedUtf8 != 1 ||
            Convert.ToString(table.Rows[0]["名称"]) != "UTF8角色" ||
            Convert.ToString(table.Rows[0]["武力"]) != "88")
        {
            throw new InvalidOperationException("UTF-8 CSV 导入烟测失败。");
        }

        var objectTypedTable = new DataTable("CSV对象列类型烟测");
        objectTypedTable.Columns.Add("ID", typeof(int));
        objectTypedTable.Columns.Add("职业", typeof(object));
        objectTypedTable.Columns["职业"]!.ExtendedProperties["FieldDefinition"] = new HexFieldDefinition
        {
            ColumnName = "职业",
            Kind = HexFieldKind.UInt8,
            Size = 1
        };
        objectTypedTable.Rows.Add(0, 78);

        var objectColumnPath = Path.Combine(smokeDir, "object-column.csv");
        File.WriteAllText(objectColumnPath, "ID,职业\r\n0,50\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var importedObjectColumn = CsvService.ImportInto(objectTypedTable, objectColumnPath, allowPartialColumns: true, matchByIdWhenPresent: true);
        if (importedObjectColumn != 1 ||
            objectTypedTable.Rows[0]["职业"] is not byte importedJob ||
            importedJob != 50)
        {
            throw new InvalidOperationException($"object 数值列 CSV 导入烟测失败：value={objectTypedTable.Rows[0]["职业"]} type={objectTypedTable.Rows[0]["职业"]?.GetType().FullName ?? "<null>"}");
        }

        var annotationTable = new DataTable("CSV说明行烟测");
        annotationTable.Columns.Add("ID", typeof(int));
        annotationTable.Columns.Add("名称", typeof(string));
        annotationTable.Columns.Add("职业", typeof(object));
        annotationTable.Columns["职业"]!.ExtendedProperties["FieldDefinition"] = new HexFieldDefinition
        {
            ColumnName = "职业",
            Kind = HexFieldKind.UInt8,
            Size = 1
        };
        annotationTable.Rows.Add(0, "旧名", (byte)1);
        annotationTable.Rows.Add(1, "旧名2", (byte)2);

        var annotationPath = Path.Combine(smokeDir, "annotation-row.csv");
        File.WriteAllText(
            annotationPath,
            "ID,名称,职业\r\n行号/编号；用于回查原始数据,显示名称/列表名,兵种/职业字段说明\r\n0,\"曹,操\",50\r\n\r\n1,\"含\"\"引号\"\"和\r\n换行\",51\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var importedAnnotation = CsvService.ImportInto(annotationTable, annotationPath, allowPartialColumns: true, matchByIdWhenPresent: true);
        if (importedAnnotation != 2 ||
            Convert.ToString(annotationTable.Rows[0]["名称"]) != "曹,操" ||
            annotationTable.Rows[0]["职业"] is not byte importedAnnotationJob ||
            importedAnnotationJob != 50 ||
            Convert.ToString(annotationTable.Rows[1]["名称"]) != "含\"引号\"和\r\n换行")
        {
            throw new InvalidOperationException("带说明行/空行/复杂文本 CSV 导入烟测失败。");
        }

        var duplicateIdPath = Path.Combine(smokeDir, "duplicate-id.csv");
        File.WriteAllText(duplicateIdPath, "ID,名称\r\n0,A\r\n0,B\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            CsvService.ImportInto(annotationTable, duplicateIdPath, allowPartialColumns: true, matchByIdWhenPresent: true);
            throw new InvalidOperationException("重复 ID CSV 未被拒绝。");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("重复", StringComparison.Ordinal))
        {
        }

        var roundTripTable = new DataTable("CSV导出导入闭环烟测");
        roundTripTable.Columns.Add("ID", typeof(int));
        roundTripTable.Columns.Add("名称", typeof(string));
        roundTripTable.Columns.Add("数值", typeof(byte));
        roundTripTable.Rows.Add(0, "逗号,引号\"换行\r\n文本", (byte)7);
        var roundTripPath = Path.Combine(smokeDir, "round-trip.csv");
        CsvService.Export(roundTripTable, roundTripPath);
        roundTripTable.Rows[0]["名称"] = string.Empty;
        roundTripTable.Rows[0]["数值"] = (byte)0;
        var importedRoundTrip = CsvService.ImportInto(roundTripTable, roundTripPath, allowPartialColumns: false, matchByIdWhenPresent: true);
        if (importedRoundTrip != 1 ||
            Convert.ToString(roundTripTable.Rows[0]["名称"]) != "逗号,引号\"换行\r\n文本" ||
            roundTripTable.Rows[0]["数值"] is not byte roundTripValue ||
            roundTripValue != 7)
        {
            throw new InvalidOperationException("CSV 导出导入闭环烟测失败。");
        }

        Console.WriteLine($"CSV_ENCODING_SMOKE_OK path={smokeDir}");
    }
}
