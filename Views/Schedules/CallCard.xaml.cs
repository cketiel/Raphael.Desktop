using System.Windows;
using System.Windows.Controls;

namespace Raphael.Desktop.Views.Schedules
{
    public partial class CallCard : UserControl
    {
        public CallCard()
        {
            InitializeComponent();
        }

        /// <summary>
        /// The help for calls, not for Schedule. F1 inside this tab answers for Schedule — the
        /// shallowest topic wins — so the card carries its own way in.
        /// </summary>
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string topic } && !string.IsNullOrWhiteSpace(topic))
                Services.Help.HelpService.Instance.Open(topic);
        }
    }
}
