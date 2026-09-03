using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> that can be refilled in one go.
    ///
    /// The plain collection raises one <c>CollectionChanged</c> per item, and a bound
    /// DataGrid answers every one of them: measure, arrange, re-evaluate the row style.
    /// Loading a day of four hundred trips that way is four hundred layout passes to show
    /// one list. <see cref="ReplaceAll"/> does it with a single Reset, which the grid
    /// answers once.
    ///
    /// The trade is that a Reset tells the view "everything changed", so it drops its
    /// selection and its scroll position. That is the right behaviour when the content is
    /// genuinely a different day or a different route; it is the wrong behaviour for a
    /// one-row change, and for those the ordinary Add/Remove/Move are still the way.
    /// </summary>
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public RangeObservableCollection()
        {
        }

        public RangeObservableCollection(IEnumerable<T> collection) : base(collection)
        {
        }

        /// <summary>
        /// Empties the collection and refills it from <paramref name="items"/>, notifying once.
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();

            Items.Clear();

            if (items != null)
            {
                foreach (var item in items)
                    Items.Add(item);
            }

            RaiseReset();
        }

        /// <summary>
        /// Appends <paramref name="items"/>, notifying once instead of once per item.
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;

            CheckReentrancy();

            var added = false;
            foreach (var item in items)
            {
                Items.Add(item);
                added = true;
            }

            if (added) RaiseReset();
        }

        private void RaiseReset()
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
