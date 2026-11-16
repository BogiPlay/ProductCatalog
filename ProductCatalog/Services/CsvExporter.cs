using System.Collections.Generic;
using System.IO;
using System.Text;
using ProductCatalog.Models;

namespace ProductCatalog.Services;

/// <summary>Експорт в CSV (разделител „;“ и UTF-8 с BOM – отваря се директно в Excel с кирилица).</summary>
public static class CsvExporter
{
    public static void Export(string filePath, IEnumerable<Product> products)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Номер;Име;Категория;Цена (€);Наличност (бр.);Стойност (€);Описание;Създаден;Обновен");

        foreach (var p in products)
        {
            sb.Append(p.Id).Append(';')
              .Append(Escape(p.Name)).Append(';')
              .Append(Escape(p.Category)).Append(';')
              .Append(p.Price.ToString("0.00", App.Bulgarian)).Append(';')
              .Append(p.Quantity).Append(';')
              .Append(p.TotalValue.ToString("0.00", App.Bulgarian)).Append(';')
              .Append(Escape(p.Description ?? string.Empty)).Append(';')
              .Append(p.CreatedAt.ToString("dd.MM.yyyy HH:mm")).Append(';')
              .Append(p.UpdatedAt.ToString("dd.MM.yyyy HH:mm"))
              .AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
