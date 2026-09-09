using CommunityToolkit.Mvvm.ComponentModel;

namespace Raphael.Desktop.Models.Import
{
    /// <summary>Where one request has got to.</summary>
    public enum ImportStepState
    {
        /// <summary>Declared, not started. It already counts towards the overall total.</summary>
        Waiting = 0,

        /// <summary>In flight.</summary>
        Running = 1,

        Done = 2,
        Failed = 3
    }

    /// <summary>
    /// One request the import makes, with its own bar.
    /// </summary>
    /// <remarks>
    /// The single overall percentage was lying. It ran 0 to 100 for the geocoding pass and then 0
    /// to 100 again for the trips, so on a file that costs three requests it reached the end
    /// twice before it was over. A bar that hits 100% and carries on is worse than no bar.
    ///
    /// <para>
    /// So every request is declared up front with the work it holds, and the overall figure is
    /// their sum. Nothing resets, nothing reaches the end early, and each request says what it is
    /// carrying — "batch 2 of 3 · 100 trips" — instead of the whole thing being one anonymous line.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <see cref="IsIndeterminate"/> is honest, not lazy. While a batch is in flight the server
    /// is working through a hundred trips in a hundred transactions and tells us nothing until it
    /// has finished all of them: there is no "15 of 100" to report, and inventing a number that
    /// creeps along would be a decoration that lies. The bar is indeterminate while it waits and
    /// exact the moment the answer lands.
    /// </para>
    /// </remarks>
    public partial class ImportRequestStep : ObservableObject
    {
        public ImportRequestStep(string key, string label, int total)
        {
            Key = key;
            Label = label;
            Total = total;
        }

        /// <summary>Identity, so a later report updates this step instead of adding another.</summary>
        public string Key { get; }

        [ObservableProperty] private string _label;

        [ObservableProperty] private int _total;

        [ObservableProperty] private int _completed;

        [ObservableProperty] private ImportStepState _state = ImportStepState.Waiting;

        /// <summary>What it came to, once it has. Null until then.</summary>
        [ObservableProperty] private string _summary;

        public bool IsIndeterminate => State == ImportStepState.Running && Completed == 0;

        public bool IsDone => State == ImportStepState.Done;

        public double Percent => Total <= 0 ? 0 : (double)Completed / Total * 100;

        /// <summary>"15 of 100" — what this request is carrying and how far through it is.</summary>
        public string Progress => $"{Completed} / {Total}";

        partial void OnStateChanged(ImportStepState value) => Raise();
        partial void OnCompletedChanged(int value) => Raise();
        partial void OnTotalChanged(int value) => Raise();

        private void Raise()
        {
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(Percent));
            OnPropertyChanged(nameof(Progress));
        }
    }
}
