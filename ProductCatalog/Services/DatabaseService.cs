using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ProductCatalog.Models;

namespace ProductCatalog.Services;

/// <summary>
/// Слой за достъп до данни: SqliteConnection + ПАРАМЕТРИЗИРАНИ команди.
/// В SQL текста никога не се „слепват“ стойности, въведени от потребителя –
/// те се подават само чрез параметри ($name, $price, $id ...).
/// </summary>
public sealed class DatabaseService
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const string SelectColumns =
        "SELECT Id, Name, Category, Price, Quantity, Description, CreatedAt, UpdatedAt FROM Products";

    private readonly string _connectionString;

    public DatabaseService(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductCatalog",
            "products.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    /// <summary>Пълен път до файла на базата данни.</summary>
    public string DatabasePath { get; }

    /// <summary>True, ако при това стартиране са добавени примерни данни.</summary>
    public bool WasSeeded { get; private set; }

    // =====================================================================
    //  Връзка и създаване на схемата
    // =====================================================================

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // SQLite сравнява LIKE без значение от регистъра само за латиница.
        // Регистрираме собствена функция, за да работи търсенето и на кирилица
        // („клав“ намира „Клавиатура“).
        connection.CreateFunction<string?, string?>(
            "unicode_lower",
            value => value?.ToLowerInvariant(),
            isDeterministic: true);

        return connection;
    }

    /// <summary>Създава таблиците при стартиране (ако не съществуват) и добавя примерни данни.</summary>
    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Products (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name        TEXT    NOT NULL,
                    Category    TEXT    NOT NULL DEFAULT 'Други',
                    Price       REAL    NOT NULL CHECK (Price >= 0),
                    Quantity    INTEGER NOT NULL DEFAULT 0 CHECK (Quantity >= 0),
                    Description TEXT    NULL,
                    CreatedAt   TEXT    NOT NULL,
                    UpdatedAt   TEXT    NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Products_Category ON Products (Category);

                CREATE TABLE IF NOT EXISTS Settings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        SeedIfNeeded(connection);
    }

    private void SeedIfNeeded(SqliteConnection connection)
    {
        if (GetSetting(connection, "Seeded") is not null)
            return;

        using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM Products";
            var count = Convert.ToInt64(countCommand.ExecuteScalar());
            if (count > 0)
            {
                SetSetting(connection, "Seeded", "1");
                return;
            }
        }

        var samples = new (string Name, string Category, decimal Price, int Quantity, string Description)[]
        {
            ("Механична клавиатура", "Периферия", 89.90m, 14, "Механични суичове, RGB подсветка, БДС подредба"),
            ("Безжична мишка", "Периферия", 34.50m, 27, "Тиха, 2.4 GHz и Bluetooth, до 12 месеца батерия"),
            ("Монитор 27\" QHD", "Монитори", 289.00m, 6, "IPS панел, 165 Hz, 1 ms"),
            ("USB-C хъб 7 в 1", "Аксесоари", 39.90m, 3, "HDMI 4K, 3× USB-A, SD/microSD, PD 100 W"),
            ("SSD 1 TB NVMe", "Съхранение", 74.99m, 18, "PCIe 4.0, до 7000 MB/s четене"),
            ("Слушалки с микрофон", "Аудио", 59.00m, 9, "Затворен тип, активно шумопотискане"),
            ("Уеб камера Full HD", "Периферия", 45.00m, 0, "1080p, 60 fps, вграден микрофон"),
            ("Wi-Fi 6 рутер", "Мрежи", 119.00m, 4, "Двулентов, 4 антени, WPA3"),
            ("Видеокарта 12 GB", "Компоненти", 649.00m, 2, "12 GB GDDR6X, 3× DisplayPort"),
            ("RAM памет 32 GB DDR5", "Компоненти", 109.90m, 11, "2 × 16 GB, 6000 MHz"),
            ("Подложка за мишка XL", "Аксесоари", 14.90m, 40, "90 × 40 см, гумирана основа"),
            ("Bluetooth колонка", "Аудио", 49.90m, 7, "Водоустойчива IPX7, 12 ч. работа"),
        };

        using var transaction = connection.BeginTransaction();
        var now = DateTime.Now;
        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            var created = now.AddDays(-(samples.Length - i)).AddMinutes(-i * 17);

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Products (Name, Category, Price, Quantity, Description, CreatedAt, UpdatedAt)
                VALUES ($name, $category, $price, $quantity, $description, $createdAt, $updatedAt)
                """;
            insert.Parameters.AddWithValue("$name", s.Name);
            insert.Parameters.AddWithValue("$category", s.Category);
            insert.Parameters.AddWithValue("$price", (double)s.Price);
            insert.Parameters.AddWithValue("$quantity", s.Quantity);
            insert.Parameters.AddWithValue("$description", s.Description);
            insert.Parameters.AddWithValue("$createdAt", FormatDate(created));
            insert.Parameters.AddWithValue("$updatedAt", FormatDate(created));
            insert.ExecuteNonQuery();
        }
        SetSetting(connection, "Seeded", "1", transaction);
        transaction.Commit();

        WasSeeded = true;
    }

    // =====================================================================
    //  READ – зареждане и филтриране (Урок 13)
    // =====================================================================

    /// <summary>
    /// SELECT ... WHERE ... с параметри. Празен критерий = всички записи.
    /// </summary>
    public List<Product> GetProducts(ProductQuery query)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();

            // „#12“ → търсене по номер
            if (term.StartsWith('#') && int.TryParse(term[1..], out var id))
            {
                conditions.Add("Id = $id");
                command.Parameters.AddWithValue("$id", id);
            }
            else
            {
                conditions.Add(
                    "(unicode_lower(Name) LIKE $term ESCAPE '\\' " +
                    "OR unicode_lower(Category) LIKE $term ESCAPE '\\' " +
                    "OR unicode_lower(IFNULL(Description, '')) LIKE $term ESCAPE '\\')");
                // Стойността отива в параметъра, не в SQL текста.
                command.Parameters.AddWithValue("$term", "%" + EscapeLike(term.ToLowerInvariant()) + "%");
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            conditions.Add("Category = $category");
            command.Parameters.AddWithValue("$category", query.Category);
        }

        if (query.MinPrice is decimal min)
        {
            conditions.Add("Price >= $minPrice");
            command.Parameters.AddWithValue("$minPrice", (double)min);
        }

        if (query.MaxPrice is decimal max)
        {
            conditions.Add("Price <= $maxPrice");
            command.Parameters.AddWithValue("$maxPrice", (double)max);
        }

        if (query.LowStockOnly)
        {
            conditions.Add("Quantity < $threshold");
            command.Parameters.AddWithValue("$threshold", Product.LowStockThreshold);
        }

        var sql = SelectColumns;
        if (conditions.Count > 0)
            sql += " WHERE " + string.Join(" AND ", conditions);

        // ORDER BY не може да е параметър – затова избираме от фиксиран „бял списък“.
        sql += " ORDER BY " + GetOrderBy(query.Sort);

        command.CommandText = sql;

        var products = new List<Product>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            products.Add(ReadProduct(reader));

        return products;
    }

    public Product? GetProductById(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public List<string> GetCategories()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT Category FROM Products WHERE TRIM(Category) <> ''";

        var categories = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            categories.Add(reader.GetString(0));

        var comparer = StringComparer.Create(App.Bulgarian, ignoreCase: true);
        return categories.OrderBy(c => c, comparer).ToList();
    }

    /// <summary>Проверка дали вече има продукт със същото име (без значение от регистъра).</summary>
    public bool ProductNameExists(string name, int? excludeId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM Products
            WHERE unicode_lower(Name) = $name
              AND ($excludeId IS NULL OR Id <> $excludeId)
            """;
        command.Parameters.AddWithValue("$name", name.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$excludeId", (object?)excludeId ?? DBNull.Value);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public ProductStatistics GetStatistics()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),
                   COUNT(DISTINCT Category),
                   IFNULL(SUM(Price * Quantity), 0),
                   IFNULL(AVG(Price), 0),
                   IFNULL(MIN(Price), 0),
                   IFNULL(MAX(Price), 0),
                   IFNULL(SUM(Quantity), 0),
                   IFNULL(SUM(CASE WHEN Quantity < $threshold THEN 1 ELSE 0 END), 0),
                   IFNULL(SUM(CASE WHEN Quantity = 0 THEN 1 ELSE 0 END), 0)
            FROM Products
            """;
        command.Parameters.AddWithValue("$threshold", Product.LowStockThreshold);

        using var reader = command.ExecuteReader();
        reader.Read();
        return new ProductStatistics(
            Count: (int)reader.GetInt64(0),
            CategoryCount: (int)reader.GetInt64(1),
            TotalValue: Math.Round((decimal)reader.GetDouble(2), 2),
            AveragePrice: Math.Round((decimal)reader.GetDouble(3), 2),
            MinPrice: Math.Round((decimal)reader.GetDouble(4), 2),
            MaxPrice: Math.Round((decimal)reader.GetDouble(5), 2),
            TotalUnits: (int)reader.GetInt64(6),
            LowStockCount: (int)reader.GetInt64(7),
            OutOfStockCount: (int)reader.GetInt64(8));
    }

    // =====================================================================
    //  CREATE / UPDATE / DELETE (Урок 14)
    // =====================================================================

    /// <summary>INSERT – връща Id на новия запис.</summary>
    public int AddProduct(Product product)
    {
        var now = DateTime.Now;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Products (Name, Category, Price, Quantity, Description, CreatedAt, UpdatedAt)
            VALUES ($name, $category, $price, $quantity, $description, $createdAt, $updatedAt);
            SELECT last_insert_rowid();
            """;
        AddProductParameters(command, product);
        command.Parameters.AddWithValue("$createdAt", FormatDate(now));
        command.Parameters.AddWithValue("$updatedAt", FormatDate(now));

        var newId = Convert.ToInt32(command.ExecuteScalar());
        product.Id = newId;
        product.CreatedAt = now;
        product.UpdatedAt = now;
        return newId;
    }

    /// <summary>UPDATE ... WHERE Id = $id – връща броя променени редове (0 = записът липсва).</summary>
    public int UpdateProduct(Product product)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Products
            SET Name = $name,
                Category = $category,
                Price = $price,
                Quantity = $quantity,
                Description = $description,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        AddProductParameters(command, product);
        command.Parameters.AddWithValue("$updatedAt", FormatDate(DateTime.Now));
        command.Parameters.AddWithValue("$id", product.Id);
        return command.ExecuteNonQuery();
    }

    /// <summary>DELETE ... WHERE Id = $id – връща броя изтрити редове.</summary>
    public int DeleteProduct(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Products WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery();
    }

    /// <summary>Връща изтрит продукт със същия Id (за бутона „Отмени изтриването“).</summary>
    public void RestoreProduct(Product product)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Products (Id, Name, Category, Price, Quantity, Description, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $category, $price, $quantity, $description, $createdAt, $updatedAt)
            """;
        AddProductParameters(command, product);
        command.Parameters.AddWithValue("$id", product.Id);
        command.Parameters.AddWithValue("$createdAt", FormatDate(product.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatDate(product.UpdatedAt));
        command.ExecuteNonQuery();
    }

    // =====================================================================
    //  Настройки (напр. избраната тема)
    // =====================================================================

    public string? GetSetting(string key)
    {
        using var connection = OpenConnection();
        return GetSetting(connection, key);
    }

    public void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        SetSetting(connection, key, value);
    }

    private static string? GetSetting(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SetSetting(SqliteConnection connection, string key, string value, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    // =====================================================================
    //  Помощни методи
    // =====================================================================

    private static void AddProductParameters(SqliteCommand command, Product product)
    {
        command.Parameters.AddWithValue("$name", product.Name);
        command.Parameters.AddWithValue("$category", product.Category);
        // decimal се записва като TEXT в SQLite, затова подаваме double (REAL).
        command.Parameters.AddWithValue("$price", (double)product.Price);
        command.Parameters.AddWithValue("$quantity", product.Quantity);
        command.Parameters.AddWithValue("$description", (object?)product.Description ?? DBNull.Value);
    }

    private static Product ReadProduct(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Category = reader.GetString(2),
        Price = Math.Round((decimal)reader.GetDouble(3), 2),
        Quantity = reader.GetInt32(4),
        Description = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedAt = ParseDate(reader.GetString(6)),
        UpdatedAt = ParseDate(reader.GetString(7)),
    };

    private static string GetOrderBy(ProductSort sort) => sort switch
    {
        ProductSort.RecentlyUpdated => "UpdatedAt DESC, Id DESC",
        ProductSort.NameAsc => "unicode_lower(Name) ASC",
        ProductSort.NameDesc => "unicode_lower(Name) DESC",
        ProductSort.PriceAsc => "Price ASC, unicode_lower(Name) ASC",
        ProductSort.PriceDesc => "Price DESC, unicode_lower(Name) ASC",
        ProductSort.QuantityAsc => "Quantity ASC, unicode_lower(Name) ASC",
        _ => "CreatedAt DESC, Id DESC",
    };

    /// <summary>Екранира %, _ и \, за да се търсят буквално (напр. „100%“).</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string FormatDate(DateTime value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string value) =>
        DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : DateTime.MinValue;
}
