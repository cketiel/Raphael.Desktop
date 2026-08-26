using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Raphael.Desktop.Services.Notifications;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Lógica de interacción para SchedulesView.xaml
    /// </summary>
    public partial class SchedulesView : UserControl
    {
        private readonly SchedulesTabControlViewModel _viewModel;

        /// <param name="notificationService">
        /// The office inbox, so the schedule panel finds out when a trip it is offering as
        /// routable gets cancelled from the driver's app, the patient's, the Booking
        /// Portal, an integrator or the bot.
        /// </param>
        public SchedulesView(INotificationService notificationService = null)
        {
            InitializeComponent();

            _viewModel = new SchedulesTabControlViewModel(notificationService);
            DataContext = _viewModel;
        }

        /// <summary>
        /// Lets go of the inbox. Called when the tab is closed, not when it loses focus.
        /// </summary>
        public void ReleaseNotifications()
        {
            _viewModel?.ScheduleContentViewModel?.ReleaseNotifications();
        }
    }
}
