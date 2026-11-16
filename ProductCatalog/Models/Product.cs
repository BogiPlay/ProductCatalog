using System;

namespace ProductCatalog.Models;

/// <summary>
/// Модел на един ред от таблицата Products.
/// Основните полета от урока са Id, Name и Price; останалите са разширение.
/// </summary>
public class Product
{
    /// <summary>Под този брой бройки продуктът се смята за „ниска наличност“.</summary>
    public const int LowStockThreshold = 5;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public string Category { get; set; } = "Други";
    public int Quantity { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ---- Изчислени свойства (само за показване в интерфейса) ----

    /// <summary>Стойност на наличността = цена × брой.</summary>
    public decimal TotalValue => Price * Quantity;

    public bool IsOutOfStock => Quantity == 0;

    public bool IsLowStock => Quantity > 0 && Quantity < LowStockThreshold;

    public string StockStatus => IsOutOfStock ? "Изчерпан" : IsLowStock ? "Ниска" : "В наличност";

    /// <summary>Първата буква на името – за „аватара“ в таблицата.</summary>
    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : char.ToUpper(Name.Trim()[0]).ToString();

    public string DescriptionPreview => string.IsNullOrWhiteSpace(Description)
        ? "Без описание"
        : Description.ReplaceLineEndings(" ");

    public Product Clone() => (Product)MemberwiseClone();

    public override string ToString() => $"#{Id} {Name} ({Price:N2} €)";
}
