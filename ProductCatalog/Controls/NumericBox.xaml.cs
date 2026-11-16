using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProductCatalog.Services;

namespace ProductCatalog.Controls;

/// <summary>
/// „Numeric“ поле (в WPF няма вграден NumericUpDown).
/// Пропуска само цифри и един десетичен знак, поддържа стрелки, ↑/↓ и колелцето на мишката.
/// </summary>
public partial class NumericBox : UserControl
{
    private int _decimals = 2;
    private bool _hasError;

    public NumericBox()
    {
        InitializeComponent();

        DataObject.AddPastingHandler(input, OnPaste);
        input.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        input.GotKeyboardFocus += (_, _) => UpdateFrame();
        input.LostKeyboardFocus += (_, _) =>
        {
            NormalizeText();
            UpdateFrame();
        };
    }

    /// <summary>Извиква се при всяка промяна на текста.</summary>
    public event EventHandler? ValueChanged;

    public decimal Step { get; set; } = 1m;
    public decimal Minimum { get; set; } = 0m;
    public decimal Maximum { get; set; } = 1_000_000m;

    /// <summary>Брой знаци след десетичната запетая (0 = само цели числа).</summary>
    public int Decimals
    {
        get => _decimals;
        set => _decimals = Math.Max(0, value);
    }

    public string Suffix
    {
        get => suffixText.Text;
        set
        {
            suffixText.Text = value;
            suffixText.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string Placeholder
    {
        get => input.Tag as string ?? string.Empty;
        set => input.Tag = value;
    }

    public bool ShowButtons
    {
        get => spinPanel.Visibility == Visibility.Visible;
        set => spinPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Червена рамка при грешка във валидацията.</summary>
    public bool HasError
    {
        get => _hasError;
        set
        {
            _hasError = value;
            UpdateFrame();
        }
    }

    public string Text
    {
        get => input.Text;
        set => input.Text = value ?? string.Empty;
    }

    public bool TryGetValue(out decimal value) => NumberParser.TryParse(input.Text, out value);

    public void SetNumber(decimal value) => input.Text = NumberParser.Format(value, Decimals);

    public void FocusInput()
    {
        input.Focus();
        input.SelectAll();
    }

    // ------------------------------------------------------------------

    private void UpdateFrame()
    {
        var key = _hasError ? "DangerBrush" : input.IsKeyboardFocused ? "AccentBrush" : "LineBrush";
        frame.SetResourceReference(Border.BorderBrushProperty, key);
    }

    private void NormalizeText()
    {
        if (!string.IsNullOrWhiteSpace(input.Text) && TryGetValue(out var value))
        {
            var formatted = NumberParser.Format(value, Decimals);
            if (formatted != input.Text)
                input.Text = formatted;
        }
    }

    private void Increment(int direction)
    {
        if (!TryGetValue(out var value))
            value = 0;

        value = Math.Clamp(value + direction * Step, Minimum, Maximum);
        input.Text = NumberParser.Format(value, Decimals);
        input.CaretIndex = input.Text.Length;
    }

    private string ProposedText(string inserted)
    {
        var text = input.Text;
        var start = input.SelectionStart;
        return text.Remove(start, input.SelectionLength).Insert(start, inserted);
    }

    private bool IsAllowed(string text)
    {
        var pattern = Decimals > 0
            ? $@"^\d{{0,9}}([.,]\d{{0,{Decimals}}})?$"
            : @"^\d{0,9}$";
        return Regex.IsMatch(text, pattern);
    }

    private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsAllowed(ProposedText(e.Text));
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText) &&
            e.DataObject.GetData(DataFormats.UnicodeText) is string pasted &&
            IsAllowed(ProposedText(pasted.Trim())))
        {
            return;
        }
        e.CancelCommand();
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                break;
            case Key.Up:
                Increment(+1);
                e.Handled = true;
                break;
            case Key.Down:
                Increment(-1);
                e.Handled = true;
                break;
        }
    }

    private void Input_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!input.IsKeyboardFocused)
            return;
        Increment(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void BtnUp_Click(object sender, RoutedEventArgs e) => Increment(+1);

    private void BtnDown_Click(object sender, RoutedEventArgs e) => Increment(-1);
}
