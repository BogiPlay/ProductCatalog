using System.Collections.Generic;

namespace ProductCatalog.Models;

/// <summary>Критерии за филтриране (Урок 13): търсене, категория, ценови диапазон, наличност и подредба.</summary>
public sealed class ProductQuery
{
    public string? SearchTerm { get; init; }
    public string? Category { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool LowStockOnly { get; init; }
    public ProductSort Sort { get; init; } = ProductSort.Newest;
}

public enum ProductSort
{
    Newest,
    RecentlyUpdated,
    NameAsc,
    NameDesc,
    PriceAsc,
    PriceDesc,
    QuantityAsc
}

/// <summary>Елемент за падащото меню „Подреди по“.</summary>
public sealed record SortOption(string Label, ProductSort Value)
{
    public static IReadOnlyList<SortOption> All { get; } = new[]
    {
        new SortOption("Най-нови първо", ProductSort.Newest),
        new SortOption("Последно променени", ProductSort.RecentlyUpdated),
        new SortOption("Име (А → Я)", ProductSort.NameAsc),
        new SortOption("Име (Я → А)", ProductSort.NameDesc),
        new SortOption("Цена (възходящо)", ProductSort.PriceAsc),
        new SortOption("Цена (низходящо)", ProductSort.PriceDesc),
        new SortOption("Наличност (най-малко)", ProductSort.QuantityAsc),
    };

    public override string ToString() => Label;
}

/// <summary>Обобщени данни за картите в горната част на прозореца.</summary>
public sealed record ProductStatistics(
    int Count,
    int CategoryCount,
    decimal TotalValue,
    decimal AveragePrice,
    decimal MinPrice,
    decimal MaxPrice,
    int TotalUnits,
    int LowStockCount,
    int OutOfStockCount);
