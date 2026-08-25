using System.Windows;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Notifications
{
    /// <summary>
    /// The alert that has to reach a dispatcher who is not looking at Raphael.
    /// </summary>
    /// <remarks>
    /// Only for what needs somebody to act — today, a Will Call. A patient has said they
    /// are ready and the office has an hour; an alert the dispatcher cannot see because
    /// they are in the phone system is an alert nobody answers.
    ///
    /// <para>
    /// ⚠️ It never takes the keyboard. <c>ShowActivated=false</c> covers the moment it
    /// appears and <c>WS_EX_NOACTIVATE</c> covers every click after that, so a dispatcher
    /// typing an address somewhere else does not lose a keystroke to it.
    /// </para>
    /// </remarks>
    public partial class NotificationAlertWindow : Window
    {
        private const double ScreenMargin = 8;

        private readonly Window _anchor;

        private readonly NotificationToastLaneViewModel _lane;

        /// <summary>
        /// How much room this window is taking in the corner, so the stack inside the main
        /// window can move up and the two never overlap. Zero when nothing is showing.
        /// </summary>
        public event EventHandler<double> FootprintChanged;

        public NotificationAlertWindow(
            Window anchor,
            NotificationToastLaneViewModel lane)
        {
            InitializeComponent();

            _anchor = anchor;
            _lane = lane;

            Host.DataContext = lane;

            _lane.Changed += OnLaneChanged;

            SourceInitialized += (_, _) =>
                NativeWindowInterop.MakeNonActivating(this);

            SizeChanged += (_, _) =>
            {
                Reposition();
                RaiseFootprint();
            };
        }

        private void OnLaneChanged(object sender, EventArgs e)
        {
            if (_lane.IsEmpty)
            {
                Hide();
                RaiseFootprint();
                return;
            }

            if (!IsVisible)
                Show();

            Reposition();
            RaiseFootprint();
        }

        /// <summary>
        /// Bottom-right of the screen the main window is on.
        /// </summary>
        /// <remarks>
        /// ⚠️ The screen of the main window, not the primary one. A dispatcher with Raphael
        /// on the second monitor would otherwise get the alert on the monitor they are not
        /// looking at, which is worse than not showing it at all — it would be an alert
        /// that appears to work.
        /// </remarks>
        private void Reposition()
        {
            try
            {
                var area = _anchor is null
                    ? SystemParameters.WorkArea
                    : NativeWindowInterop.WorkAreaFor(_anchor);

                if (area.Width <= 0 || area.Height <= 0)
                    area = SystemParameters.WorkArea;

                Left = area.Right - ActualWidth - ScreenMargin;
                Top = area.Bottom - ActualHeight - ScreenMargin;
            }
            catch
            {
                // Somewhere on screen beats an exception on the UI thread.
            }
        }

        private void RaiseFootprint()
        {
            FootprintChanged?.Invoke(
                this,
                IsVisible ? ActualHeight : 0);
        }

        /// <summary>
        /// Closes for good. Called when the application closes: with no owner, this window
        /// would otherwise keep the process alive.
        /// </summary>
        public void Shutdown()
        {
            _lane.Changed -= OnLaneChanged;

            Close();
        }
    }
}
