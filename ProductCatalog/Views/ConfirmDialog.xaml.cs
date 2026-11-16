using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProductCatalog.Views;

/// <summary>Стилизиран диалог за потвърждение (вместо стандартния MessageBox).</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Показва диалога и връща true, ако потребителят потвърди.</summary>
    public static bool Ask(Window owner, string title, string message, string confirmText = "Потвърди", bool isDanger = false)
    {
        var dialog = new ConfirmDialog { Owner = owner };
        dialog.txtTitle.Text = title;
        dialog.txtMessage.Text = message;
        dialog.btnConfirm.Content = confirmText;

        if (!isDanger)
        {
            dialog.btnConfirm.Style = (Style)dialog.FindResource("BtnPrimary");
            dialog.iconBorder.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            dialog.iconText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            dialog.iconText.Text = "";
            dialog.Loaded += (_, _) => dialog.btnConfirm.Focus();
        }
        else
        {
            // При опасно действие фокусът е върху „Отказ“.
            dialog.Loaded += (_, _) => dialog.btnCancel.Focus();
        }

        return dialog.ShowDialog() == true;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
