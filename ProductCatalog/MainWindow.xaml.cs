using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using ProductCatalog.Models;
using ProductCatalog.Services;
using ProductCatalog.Views;

namespace ProductCatalog;

/// <summary>
/// UI слой: обработчиците на събития извикват методите
/// LoadProducts / AddProduct / UpdateProduct / DeleteProduct,
/// които от своя страна използват DatabaseService (параметризиран SQL).
/// </summary>
public partial class MainWindow : Window
{
    private const string AllCategoriesLabel = "Всички категории";

    private static readonly string[] DefaultCategories =
    {
        "Аксесоари", "Аудио", "Компоненти", "Монитори", "Мрежи", "Периферия", "Съхранение", "Други"
    };

    private readonly DatabaseService _db = new();
    private readonly DispatcherTimer _searchTimer;

    private List<Product> _products = new();
    private int? _selectedId;           // Id на реда, който се редактира (null = нов продукт)
    private int _totalCount;
    private bool _suppressSelection;    // спира SelectionChanged при програмно презареждане
    private bool _suppressFilters = true;
    private Exception? _startupError;
    private string _theme = "Dark";

    private enum ToastKind { Success, Error, Warning, Info }

    public MainWindow()
    {
        InitializeComponent();

        // Търсене „на живо“ с малко забавяне, за да не се пуска заявка на всеки клавиш.
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadProducts();
        };

        cmbSort.ItemsSource = SortOption.All;
        cmbSort.SelectedIndex = 0;

        try
        {
            // Създаване на таблицата при стартиране (Урок 14, стъпка 2).
            _db.Initialize();
            _theme = _db.GetSetting("Theme") ?? "Dark";
            App.ApplyTheme(_theme);
        }
        catch (Exception ex)
        {
            _startupError = ex;
        }

        UpdateThemeIcon();
    }

    // =====================================================================
    //  Стартиране
    // =====================================================================

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupError is not null)
        {
            MessageBox.Show(this,
                $"Базата данни не може да бъде отворена:\n\n{_startupError.Message}\n\nПът: {_db.DatabasePath}",
                "Каталог на продукти", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        txtDbPath.Text = _db.DatabasePath;
        RefreshCategories();
        _suppressFilters = false;

        LoadProducts();
        ClearEditor(focus: false);
        SetLastOperation("Приложението е стартирано");
        txtSearch.Focus();

        if (_db.WasSeeded)
        {
            ShowToast(ToastKind.Info, "Добре дошли!",
                "Създадена е нова база данни с примерни продукти. Опитайте търсене, редакция и изтриване.");
        }
    }

    // =====================================================================
    //  READ: зареждане + филтриране (Урок 13)
    // =====================================================================

    /// <summary>
    /// Зарежда продуктите според критерия и филтрите и ги показва в DataGrid-а.
    /// </summary>
    private void LoadProducts(string? term = null)
    {
        term ??= txtSearch.Text;

        try
        {
            _products = _db.GetProducts(BuildQuery(term));

            _suppressSelection = true;
            gridProducts.ItemsSource = _products;

            // Запазваме избрания ред след презареждане.
            if (_selectedId is int id)
            {
                var match = _products.FirstOrDefault(p => p.Id == id);
                gridProducts.SelectedItem = match;
                if (match is not null)
                    gridProducts.ScrollIntoView(match);
            }

            UpdateStatistics();
            UpdateResultInfo();
        }
        catch (Exception ex)
        {
            ShowError("Грешка при зареждане на данните", ex);
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private ProductQuery BuildQuery(string? term)
    {
        decimal? min = numMinPrice.TryGetValue(out var minValue) ? minValue : null;
        decimal? max = numMaxPrice.TryGetValue(out var maxValue) ? maxValue : null;
        if (min is not null && max is not null && min > max)
            (min, max) = (max, min);

        return new ProductQuery
        {
            SearchTerm = term?.Trim(),
            Category = cmbFilterCategory.SelectedItem is string category && category != AllCategoriesLabel
                ? category
                : null,
            MinPrice = min,
            MaxPrice = max,
            LowStockOnly = chkLowStock.IsChecked == true,
            Sort = (cmbSort.SelectedItem as SortOption)?.Value ?? ProductSort.Newest,
        };
    }

    private bool HasActiveFilters() =>
        !string.IsNullOrWhiteSpace(txtSearch.Text)
        || cmbFilterCategory.SelectedIndex > 0
        || !string.IsNullOrWhiteSpace(numMinPrice.Text)
        || !string.IsNullOrWhiteSpace(numMaxPrice.Text)
        || chkLowStock.IsChecked == true;

    private void UpdateResultInfo()
    {
        txtResultCount.Text = _products.Count == _totalCount
            ? Plural(_totalCount, "запис", "записа")
            : $"{_products.Count} от {_totalCount}";

        var empty = _products.Count == 0;
        emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
            return;

        if (HasActiveFilters())
        {
            emptyIcon.Text = "";
            txtEmptyTitle.Text = "Няма намерени продукти";
            txtEmptyHint.Text = "Няма записи, които отговарят на критериите. Опитайте с друга дума или изчистете филтрите.";
            btnEmptyClear.Visibility = Visibility.Visible;
        }
        else
        {
            emptyIcon.Text = "";
            txtEmptyTitle.Text = "Каталогът е празен";
            txtEmptyHint.Text = "Добавете първия продукт от панела вдясно.";
            btnEmptyClear.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStatistics()
    {
        var s = _db.GetStatistics();
        _totalCount = s.Count;

        txtStatCount.Text = s.Count.ToString("N0", App.Bulgarian);
        txtStatCountSub.Text = $"в {Plural(s.CategoryCount, "категория", "категории")}";

        txtStatValue.Text = Money(s.TotalValue);
        txtStatValueSub.Text = $"{s.TotalUnits.ToString("N0", App.Bulgarian)} бр. на склад";

        txtStatAvg.Text = Money(s.AveragePrice);
        txtStatAvgSub.Text = s.Count > 0 ? $"от {Money(s.MinPrice)} до {Money(s.MaxPrice)}" : "няма данни";

        txtStatLow.Text = s.LowStockCount.ToString(App.Bulgarian);
        txtStatLowSub.Text = s.OutOfStockCount > 0
            ? $"{Plural(s.OutOfStockCount, "изчерпан", "изчерпани")}"
            : $"под {Product.LowStockThreshold} бр. в наличност";
    }

    private void RefreshCategories()
    {
        var categories = _db.GetCategories();

        // Филтър: „Всички категории“ + категориите от базата
        var previous = cmbFilterCategory.SelectedItem as string;
        var wasSuppressed = _suppressFilters;
        _suppressFilters = true;

        var filterItems = new List<string> { AllCategoriesLabel };
        filterItems.AddRange(categories);
        cmbFilterCategory.ItemsSource = filterItems;
        cmbFilterCategory.SelectedItem = previous is not null && filterItems.Contains(previous)
            ? previous
            : AllCategoriesLabel;

        _suppressFilters = wasSuppressed;

        // Редактор: категориите от базата + стандартни предложения
        var editorText = cmbCategory.Text;
        var comparer = StringComparer.Create(App.Bulgarian, ignoreCase: true);
        cmbCategory.ItemsSource = categories
            .Concat(DefaultCategories)
            .Distinct(comparer)
            .OrderBy(c => c, comparer)
            .ToList();
        cmbCategory.Text = editorText;
    }

    // =====================================================================
    //  Избор на ред → зареждане в полетата (Урок 13)
    // =====================================================================

    private void GridProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;

        if (gridProducts.SelectedItem is Product product)
            LoadIntoEditor(product);
    }

    private void LoadIntoEditor(Product product)
    {
        _selectedId = product.Id;

        txtName.Text = product.Name;
        cmbCategory.Text = product.Category;
        numPrice.SetNumber(product.Price);
        numQuantity.SetNumber(product.Quantity);
        txtDescription.Text = product.Description ?? string.Empty;

        txtMetaCreated.Text = product.CreatedAt.ToString("dd.MM.yyyy HH:mm");
        txtMetaUpdated.Text = product.UpdatedAt.ToString("dd.MM.yyyy HH:mm");
        txtMetaValue.Text = Money(product.TotalValue);

        ClearErrors();
        UpdateEditorMode();
    }

    /// <summary>Избира даден Id в таблицата и го зарежда в редактора.</summary>
    private void SelectProduct(int id)
    {
        _selectedId = id;
        LoadProducts();
        var product = _products.FirstOrDefault(p => p.Id == id) ?? _db.GetProductById(id);
        if (product is not null)
            LoadIntoEditor(product);
        else
            ClearEditor(focus: false);
    }

    private void ClearEditor(bool focus = true)
    {
        _selectedId = null;

        _suppressSelection = true;
        gridProducts.SelectedItem = null;
        _suppressSelection = false;

        txtName.Text = string.Empty;
        cmbCategory.SelectedIndex = -1;
        cmbCategory.Text = string.Empty;
        numPrice.Text = string.Empty;
        numQuantity.Text = "1";
        txtDescription.Text = string.Empty;

        ClearErrors();
        UpdateEditorMode();

        if (focus)
            txtName.Focus();
    }

    private void UpdateEditorMode()
    {
        var editing = _selectedId is not null;

        if (editing)
        {
            txtEditorTitle.Text = "Редакция на продукт";
            txtEditorSubtitle.Text = $"Променяте запис #{_selectedId}. Натиснете „Запиши промените“, за да изпълните UPDATE.";
            txtMode.Text = $"#{_selectedId}";
            pillMode.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            txtMode.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            btnUpdate.Style = (Style)FindResource("BtnPrimary");
            btnAdd.Style = (Style)FindResource("Btn");
            txtAddLabel.Text = "Добави като нов продукт";
            metaPanel.Visibility = Visibility.Visible;
        }
        else
        {
            txtEditorTitle.Text = "Нов продукт";
            txtEditorSubtitle.Text = "Попълнете полетата и натиснете „Добави продукт“ или изберете ред от таблицата.";
            txtMode.Text = "НОВ";
            pillMode.SetResourceReference(Border.BackgroundProperty, "SuccessSoftBrush");
            txtMode.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            btnAdd.Style = (Style)FindResource("BtnPrimary");
            btnUpdate.Style = (Style)FindResource("Btn");
            txtAddLabel.Text = "Добави продукт";
            metaPanel.Visibility = Visibility.Collapsed;
        }
    }

    // =====================================================================
    //  CREATE
    // =====================================================================

    private void AddProduct()
    {
        var product = ReadEditor();
        if (product is null)
            return;

        try
        {
            if (_db.ProductNameExists(product.Name, excludeId: null) &&
                !Confirm("Продуктът вече съществува",
                    $"В каталога вече има продукт с име „{product.Name}“.\nДа бъде ли добавен още един?",
                    "Добави", isDanger: false))
            {
                return;
            }

            var newId = _db.AddProduct(product);

            RefreshCategories();
            SelectProduct(newId);
            SetLastOperation($"Добавен: {product.Name} (#{newId})");

            var visible = _products.Any(p => p.Id == newId);
            ShowToast(ToastKind.Success, "Продуктът е добавен",
                visible
                    ? $"„{product.Name}“ е записан с номер #{newId}."
                    : $"„{product.Name}“ (#{newId}) е записан, но не отговаря на текущите филтри.");
        }
        catch (Exception ex)
        {
            ShowError("Грешка при добавяне", ex);
        }
    }

    // =====================================================================
    //  UPDATE (Урок 13)
    // =====================================================================

    private void UpdateProduct()
    {
        if (_selectedId is not int id)
        {
            ShowToast(ToastKind.Warning, "Няма избран продукт",
                "Изберете ред от таблицата, който да редактирате.");
            gridProducts.Focus();
            return;
        }

        var product = ReadEditor();
        if (product is null)
            return;
        product.Id = id;

        try
        {
            var original = _products.FirstOrDefault(p => p.Id == id) ?? _db.GetProductById(id);
            if (original is not null && IsSame(original, product))
            {
                ShowToast(ToastKind.Info, "Няма промени", "Данните съвпадат със записаните в базата.");
                return;
            }

            if (_db.ProductNameExists(product.Name, excludeId: id) &&
                !Confirm("Дублирано име",
                    $"Друг продукт вече се казва „{product.Name}“.\nДа бъдат ли записани промените все пак?",
                    "Запиши", isDanger: false))
            {
                return;
            }

            var rows = _db.UpdateProduct(product);
            if (rows == 0)
            {
                ShowToast(ToastKind.Error, "Записът не е намерен",
                    $"Продукт #{id} вече не съществува в базата данни.");
                ClearEditor(focus: false);
                LoadProducts();
                return;
            }

            RefreshCategories();
            SelectProduct(id); // презареждане → редът показва новите стойности
            SetLastOperation($"Обновен: {product.Name} (#{id})");
            ShowToast(ToastKind.Success, "Промените са записани", $"„{product.Name}“ (#{id}) е обновен.");
        }
        catch (Exception ex)
        {
            ShowError("Грешка при записване", ex);
        }
    }

    private static bool IsSame(Product a, Product b) =>
        a.Name == b.Name
        && a.Category == b.Category
        && a.Price == b.Price
        && a.Quantity == b.Quantity
        && (a.Description ?? string.Empty) == (b.Description ?? string.Empty);

    // =====================================================================
    //  DELETE
    // =====================================================================

    private void DeleteProduct()
    {
        if (_selectedId is not int id)
        {
            ShowToast(ToastKind.Warning, "Няма избран продукт",
                "Изберете ред от таблицата, който да изтриете.");
            return;
        }

        try
        {
            var product = _products.FirstOrDefault(p => p.Id == id) ?? _db.GetProductById(id);
            if (product is null)
            {
                ShowToast(ToastKind.Error, "Продуктът не е намерен", "Записът вече е изтрит.");
                ClearEditor(focus: false);
                LoadProducts();
                return;
            }

            if (!Confirm("Изтриване на продукт",
                    $"Сигурни ли сте, че искате да изтриете „{product.Name}“ (#{product.Id})?",
                    "Изтрий", isDanger: true))
            {
                return;
            }

            var rows = _db.DeleteProduct(id);
            ClearEditor(focus: false);
            RefreshCategories();
            LoadProducts();

            if (rows == 0)
            {
                ShowToast(ToastKind.Warning, "Нищо не е изтрито", "Записът вече не съществуваше.");
                return;
            }

            SetLastOperation($"Изтрит: {product.Name} (#{product.Id})");
            ShowToast(ToastKind.Success, "Продуктът е изтрит",
                $"„{product.Name}“ беше премахнат от каталога.",
                "Отмени изтриването", () => RestoreProduct(product));
        }
        catch (Exception ex)
        {
            ShowError("Грешка при изтриване", ex);
        }
    }

    private void RestoreProduct(Product product)
    {
        try
        {
            _db.RestoreProduct(product);
            RefreshCategories();
            SelectProduct(product.Id);
            SetLastOperation($"Възстановен: {product.Name} (#{product.Id})");
            ShowToast(ToastKind.Success, "Изтриването е отменено", $"„{product.Name}“ е възстановен.");
        }
        catch (Exception ex)
        {
            ShowError("Продуктът не може да бъде възстановен", ex);
        }
    }

    // =====================================================================
    //  Допълнителни действия
    // =====================================================================

    private void DuplicateProduct()
    {
        if (_selectedId is null)
        {
            ShowToast(ToastKind.Warning, "Няма избран продукт", "Изберете ред, който да дублирате.");
            return;
        }

        var name = txtName.Text.Trim();
        _selectedId = null;

        _suppressSelection = true;
        gridProducts.SelectedItem = null;
        _suppressSelection = false;

        txtName.Text = name.Length > ProductValidator.NameMaxLength - 10 ? name : $"{name} (копие)";
        UpdateEditorMode();
        txtName.Focus();
        txtName.SelectAll();

        ShowToast(ToastKind.Info, "Създадено е копие",
            "Редактирайте данните и натиснете „Добави продукт“.");
    }

    private void ExportCsv()
    {
        if (_products.Count == 0)
        {
            ShowToast(ToastKind.Info, "Няма данни за експорт", "Списъкът с продукти е празен.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Експорт на продукти",
            Filter = "CSV файл (*.csv)|*.csv",
            FileName = $"produkti_{DateTime.Now:yyyy-MM-dd}.csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            CsvExporter.Export(dialog.FileName, _products);
            var path = dialog.FileName;
            SetLastOperation($"Експорт: {Plural(_products.Count, "продукт", "продукта")}");
            ShowToast(ToastKind.Success, "Експортът е готов",
                $"{Plural(_products.Count, "продукт е записан", "продукта са записани")} в CSV.",
                "Отвори файла", () => OpenWithShell(path));
        }
        catch (Exception ex)
        {
            ShowError("Грешка при експорт", ex);
        }
    }

    private void RefreshAll()
    {
        RefreshCategories();
        LoadProducts();
        ShowToast(ToastKind.Info, "Данните са обновени", "Списъкът е презареден от базата данни.");
    }

    // =====================================================================
    //  Валидация
    // =====================================================================

    /// <summary>Чете полетата и ги валидира. Връща null при грешка (и показва съобщения).</summary>
    private Product? ReadEditor()
    {
        ClearErrors();

        var result = ProductValidator.Validate(
            txtName.Text, cmbCategory.Text, numPrice.Text, numQuantity.Text, txtDescription.Text);

        if (result.IsValid)
            return result.Product;

        foreach (var (field, message) in result.Errors)
            ShowFieldError(field, message);

        FocusField(result.Errors.Keys.First());
        ShowToast(ToastKind.Warning, "Невалидни данни", "Поправете маркираните полета и опитайте отново.");
        return null;
    }

    private void ShowFieldError(string field, string? message)
    {
        switch (field)
        {
            case ProductValidator.NameField:
                SetError(errName, message);
                MarkControl(txtName, message);
                break;
            case ProductValidator.CategoryField:
                SetError(errCategory, message);
                MarkControl(cmbCategory, message);
                break;
            case ProductValidator.PriceField:
                SetError(errPrice, message);
                numPrice.HasError = message is not null;
                break;
            case ProductValidator.QuantityField:
                SetError(errQuantity, message);
                numQuantity.HasError = message is not null;
                break;
            case ProductValidator.DescriptionField:
                SetError(errDescription, message);
                MarkControl(txtDescription, message);
                break;
        }
    }

    private void ClearErrors()
    {
        ShowFieldError(ProductValidator.NameField, null);
        ShowFieldError(ProductValidator.CategoryField, null);
        ShowFieldError(ProductValidator.PriceField, null);
        ShowFieldError(ProductValidator.QuantityField, null);
        ShowFieldError(ProductValidator.DescriptionField, null);
    }

    private void FocusField(string field)
    {
        switch (field)
        {
            case ProductValidator.NameField: txtName.Focus(); txtName.SelectAll(); break;
            case ProductValidator.CategoryField: cmbCategory.Focus(); break;
            case ProductValidator.PriceField: numPrice.FocusInput(); break;
            case ProductValidator.QuantityField: numQuantity.FocusInput(); break;
            case ProductValidator.DescriptionField: txtDescription.Focus(); break;
        }
    }

    private static void SetError(TextBlock target, string? message)
    {
        target.Text = message ?? string.Empty;
        target.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void MarkControl(Control control, string? message) =>
        control.SetResourceReference(Control.BorderBrushProperty, message is null ? "LineBrush" : "DangerBrush");

    // Грешката изчезва веднага щом потребителят започне да поправя полето.
    private void TxtName_TextChanged(object sender, TextChangedEventArgs e) =>
        ShowFieldError(ProductValidator.NameField, null);

    private void CmbCategory_TextChanged(object sender, TextChangedEventArgs e) =>
        ShowFieldError(ProductValidator.CategoryField, null);

    private void NumPrice_ValueChanged(object? sender, EventArgs e) =>
        ShowFieldError(ProductValidator.PriceField, null);

    private void NumQuantity_ValueChanged(object? sender, EventArgs e) =>
        ShowFieldError(ProductValidator.QuantityField, null);

    private void TxtDescription_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtDescCounter.Text = $"{txtDescription.Text.Length} / {ProductValidator.DescriptionMaxLength}";
        ShowFieldError(ProductValidator.DescriptionField, null);
    }

    // =====================================================================
    //  Обработчици на бутони (UI → метод → SQL → DataGrid)
    // =====================================================================

    private void BtnSearch_Click(object sender, RoutedEventArgs e) => SearchNow();

    private void SearchNow()
    {
        _searchTimer.Stop();
        LoadProducts(txtSearch.Text);

        var term = txtSearch.Text.Trim();
        if (term.Length > 0 && _products.Count == 0)
            ShowToast(ToastKind.Info, "Няма резултати", $"Няма продукти, отговарящи на „{term}“.");
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        btnClearSearch.Visibility = txtSearch.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!IsLoaded || _suppressFilters)
            return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SearchNow();
            e.Handled = true;
        }
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        txtSearch.Clear();
        _searchTimer.Stop();
        LoadProducts();
        txtSearch.Focus();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressFilters)
            return;
        LoadProducts();
    }

    private void PriceFilter_ValueChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || _suppressFilters)
            return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilters = true;
        txtSearch.Text = string.Empty;
        cmbFilterCategory.SelectedIndex = 0;
        numMinPrice.Text = string.Empty;
        numMaxPrice.Text = string.Empty;
        chkLowStock.IsChecked = false;
        cmbSort.SelectedIndex = 0;
        _suppressFilters = false;

        _searchTimer.Stop();
        LoadProducts();
    }

    private void CardLowStock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        chkLowStock.IsChecked = chkLowStock.IsChecked != true;

    private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddProduct();

    private void BtnUpdate_Click(object sender, RoutedEventArgs e) => UpdateProduct();

    private void BtnDelete_Click(object sender, RoutedEventArgs e) => DeleteProduct();

    private void BtnDuplicate_Click(object sender, RoutedEventArgs e) => DuplicateProduct();

    private void BtnNew_Click(object sender, RoutedEventArgs e) => ClearEditor();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void BtnExport_Click(object sender, RoutedEventArgs e) => ExportCsv();

    private void GridProducts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (gridProducts.SelectedItem is Product)
        {
            txtName.Focus();
            txtName.SelectAll();
        }
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (gridProducts.SelectedItem is Product product)
        {
            LoadIntoEditor(product);
            txtName.Focus();
            txtName.SelectAll();
        }
    }

    private void MenuDuplicate_Click(object sender, RoutedEventArgs e) => DuplicateProduct();

    private void MenuDelete_Click(object sender, RoutedEventArgs e) => DeleteProduct();

    private void BtnDbPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{_db.DatabasePath}\"");
        }
        catch (Exception ex)
        {
            ShowError("Папката не може да бъде отворена", ex);
        }
    }

    // =====================================================================
    //  Клавишни комбинации
    // =====================================================================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (ctrl && e.Key == Key.F)
        {
            txtSearch.Focus();
            txtSearch.SelectAll();
        }
        else if (ctrl && e.Key == Key.N)
        {
            ClearEditor();
        }
        else if (ctrl && e.Key == Key.S)
        {
            if (_selectedId is null) AddProduct();
            else UpdateProduct();
        }
        else if (ctrl && e.Key == Key.D)
        {
            DuplicateProduct();
        }
        else if (ctrl && e.Key == Key.E)
        {
            ExportCsv();
        }
        else if (e.Key == Key.F5)
        {
            RefreshAll();
        }
        else if (e.Key == Key.Delete && gridProducts.IsKeyboardFocusWithin)
        {
            DeleteProduct();
        }
        else if (e.Key == Key.Escape)
        {
            if (cmbCategory.IsDropDownOpen || cmbFilterCategory.IsDropDownOpen || cmbSort.IsDropDownOpen)
                return; // Esc затваря падащото меню
            if (txtSearch.IsKeyboardFocused && txtSearch.Text.Length > 0)
                BtnClearSearch_Click(this, new RoutedEventArgs());
            else if (_selectedId is not null)
                ClearEditor(focus: false);
            else
                return;
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    // =====================================================================
    //  Заглавна лента и тема
    // =====================================================================

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        _theme = _theme == "Dark" ? "Light" : "Dark";
        App.ApplyTheme(_theme);
        UpdateThemeIcon();

        try
        {
            _db.SetSetting("Theme", _theme); // темата се помни в SQLite
        }
        catch
        {
            // не е критично
        }
    }

    private void UpdateThemeIcon() =>
        iconTheme.Text = _theme == "Dark" ? "" : "";

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        // При WindowChrome максимизираният прозорец излиза малко извън екрана – компенсираме.
        rootBorder.Margin = maximized ? new Thickness(7) : new Thickness(0);
        rootBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        iconMaximize.Text = maximized ? "" : "";
    }

    // =====================================================================
    //  Известия (toast) и диалози
    // =====================================================================

    private void ShowToast(ToastKind kind, string title, string message, string? actionText = null, Action? action = null)
    {
        var (icon, foregroundKey, backgroundKey) = kind switch
        {
            ToastKind.Success => ("", "SuccessBrush", "SuccessSoftBrush"),
            ToastKind.Error => ("", "DangerBrush", "DangerSoftBrush"),
            ToastKind.Warning => ("", "WarningBrush", "WarningSoftBrush"),
            _ => ("", "AccentBrush", "AccentSoftBrush"),
        };

        var toast = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 8, 12),
            Margin = new Thickness(0, 10, 0, 0),
            Opacity = 0,
            RenderTransform = new TranslateTransform(40, 0),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.25, Color = Colors.Black },
        };
        toast.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
        toast.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            VerticalAlignment = VerticalAlignment.Top,
        };
        iconBorder.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        var iconText = new TextBlock
        {
            Text = icon,
            FontSize = 15,
            FontFamily = (FontFamily)FindResource("IconFont"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconText.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        iconBorder.Child = iconText;
        grid.Children.Add(iconBorder);

        var textPanel = new StackPanel { Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        textPanel.Children.Add(titleBlock);

        if (!string.IsNullOrWhiteSpace(message))
        {
            var messageBlock = new TextBlock { Text = message, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            messageBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            textPanel.Children.Add(messageBlock);
        }

        if (actionText is not null && action is not null)
        {
            var actionButton = new Button
            {
                Content = actionText,
                Style = (Style)FindResource("BtnLink"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
            };
            actionButton.Click += (_, _) =>
            {
                CloseToast(toast);
                action();
            };
            textPanel.Children.Add(actionButton);
        }

        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        var closeButton = new Button
        {
            Style = (Style)FindResource("IconBtn"),
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new TextBlock { Text = "", FontSize = 9, FontFamily = (FontFamily)FindResource("IconFont") },
        };
        closeButton.Click += (_, _) => CloseToast(toast);
        Grid.SetColumn(closeButton, 2);
        grid.Children.Add(closeButton);

        toast.Child = grid;

        while (toastHost.Children.Count >= 4)
            toastHost.Children.RemoveAt(0);
        toastHost.Children.Add(toast);

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        ((TranslateTransform)toast.RenderTransform).BeginAnimation(
            TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(action is not null ? 7 : 4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CloseToast(toast);
        };
        toast.MouseEnter += (_, _) => timer.Stop();     // пауза, докато мишката е върху известието
        toast.MouseLeave += (_, _) => timer.Start();
        timer.Start();
    }

    private void CloseToast(Border toast)
    {
        if (!toastHost.Children.Contains(toast) || Equals(toast.Tag, "closing"))
            return;
        toast.Tag = "closing";

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => toastHost.Children.Remove(toast);
        toast.BeginAnimation(OpacityProperty, fade);
    }

    private void ShowError(string title, Exception ex)
    {
        ShowToast(ToastKind.Error, title, ex.Message);
        SetLastOperation($"Грешка: {title}");
    }

    private bool Confirm(string title, string message, string confirmText, bool isDanger)
    {
        dimOverlay.Visibility = Visibility.Visible;
        try
        {
            return ConfirmDialog.Ask(this, title, message, confirmText, isDanger);
        }
        finally
        {
            dimOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // =====================================================================
    //  Помощни
    // =====================================================================

    private void SetLastOperation(string text) =>
        txtLastOperation.Text = $"{DateTime.Now:HH:mm:ss}  ·  {text}";

    private static string Money(decimal value) => value.ToString("N2", App.Bulgarian) + " €";

    private static string Plural(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count.ToString("N0", App.Bulgarian)} {many}";

    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // няма програма за CSV – игнорираме
        }
    }
}
