using Raphael.Desktop.DTOs;
using System;

namespace Raphael.Desktop.Models
{
    /// <summary>
    /// Names one trip in the open-trips grid, for a model that needs the view to do
    /// something to that row.
    /// </summary>
    public class UnscheduledTripEventArgs : EventArgs
    {
        public UnscheduledTripDto Trip { get; }

        public UnscheduledTripEventArgs(UnscheduledTripDto trip)
        {
            Trip = trip;
        }
    }
}
