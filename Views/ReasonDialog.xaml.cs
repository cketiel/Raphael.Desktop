using System.Windows;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// A small dialog that asks for a reason and will not close on an empty one. Returns the text
    /// in <see cref="Reason"/> when the user confirms.
    /// </summary>
    public partial class ReasonDialog : Window
    {
        private readonly string _requiredText;

        public string Reason => ReasonBox.Text.Trim();

        public ReasonDialog(string title, string prompt, string okText, string cancelText, string requiredText)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            OkButton.Content = okText;
            CancelButton.Content = cancelText;
            _requiredText = requiredText;
            Owner = Application.Current?.MainWindow;
            Loaded += (_, _) => ReasonBox.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Reason.Length == 0)
            {
                ErrorText.Text = _requiredText;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
