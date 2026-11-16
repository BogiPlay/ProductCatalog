using System.Collections.Generic;
using System.Text.RegularExpressions;
using ProductCatalog.Models;

namespace ProductCatalog.Services;

/// <summary>
/// Валидация на въведените данни ПРЕДИ да се изпълни INSERT/UPDATE.
/// При грешка връща съобщение за всяко невалидно поле.
/// </summary>
public static class ProductValidator
{
    public const string NameField = "Name";
    public const string CategoryField = "Category";
    public const string PriceField = "Price";
    public const string QuantityField = "Quantity";
    public const string DescriptionField = "Description";

    public const int NameMaxLength = 100;
    public const int CategoryMaxLength = 40;
    public const int DescriptionMaxLength = 500;
    public const decimal PriceMax = 1_000_000m;
    public const int QuantityMax = 1_000_000;

    public static ProductValidationResult Validate(
        string? name, string? category, string? priceText, string? quantityText, string? description)
    {
        var errors = new Dictionary<string, string>();

        // --- Име ---
        name = Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
        if (name.Length == 0)
            errors[NameField] = "Името е задължително.";
        else if (name.Length < 2)
            errors[NameField] = "Името трябва да е поне 2 символа.";
        else if (name.Length > NameMaxLength)
            errors[NameField] = $"Името може да е най-много {NameMaxLength} символа.";

        // --- Категория ---
        category = Regex.Replace(category ?? string.Empty, @"\s+", " ").Trim();
        if (category.Length == 0)
            errors[CategoryField] = "Изберете или въведете категория.";
        else if (category.Length > CategoryMaxLength)
            errors[CategoryField] = $"Категорията може да е най-много {CategoryMaxLength} символа.";

        // --- Цена ---
        decimal price = 0;
        if (string.IsNullOrWhiteSpace(priceText))
            errors[PriceField] = "Въведете цена.";
        else if (!NumberParser.TryParse(priceText, out price))
            errors[PriceField] = "Невалидна цена. Въведете число, напр. 49,90.";
        else if (price <= 0)
            errors[PriceField] = "Цената трябва да е по-голяма от 0.";
        else if (price > PriceMax)
            errors[PriceField] = $"Цената не може да надвишава {PriceMax:N0} €.";
        else if (decimal.Round(price, 2) != price)
            errors[PriceField] = "Допускат се най-много 2 знака след запетаята.";

        // --- Наличност ---
        int quantity = 0;
        if (string.IsNullOrWhiteSpace(quantityText))
            errors[QuantityField] = "Въведете наличност (0 или повече).";
        else if (!NumberParser.TryParse(quantityText, out var q) || q != decimal.Truncate(q))
            errors[QuantityField] = "Наличността трябва да е цяло число.";
        else if (q < 0)
            errors[QuantityField] = "Наличността не може да е отрицателна.";
        else if (q > QuantityMax)
            errors[QuantityField] = $"Наличността не може да надвишава {QuantityMax:N0} бр.";
        else
            quantity = (int)q;

        // --- Описание (по избор) ---
        description = description?.Trim();
        if (description is { Length: > DescriptionMaxLength })
            errors[DescriptionField] = $"Описанието може да е най-много {DescriptionMaxLength} символа.";

        if (errors.Count > 0)
            return new ProductValidationResult(errors, null);

        var product = new Product
        {
            Name = name,
            Category = char.ToUpper(category[0], App.Bulgarian) + category[1..],
            Price = price,
            Quantity = quantity,
            Description = string.IsNullOrEmpty(description) ? null : description,
        };
        return new ProductValidationResult(errors, product);
    }
}

public sealed class ProductValidationResult
{
    public ProductValidationResult(IReadOnlyDictionary<string, string> errors, Product? product)
    {
        Errors = errors;
        Product = product;
    }

    public IReadOnlyDictionary<string, string> Errors { get; }
    public Product? Product { get; }
    public bool IsValid => Errors.Count == 0 && Product is not null;
}
