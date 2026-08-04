using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel para un item en la lista de Fuentes de Financiamiento.
    /// Envuelve el modelo y añade una propiedad 'IsSelected' para el CheckBox.
    /// </summary>
    public class SelectableFundingSourceViewModel : BaseViewModel
    {
        public FundingSource FundingSource { get; }
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public SelectableFundingSourceViewModel(FundingSource fundingSource)
        {
            FundingSource = fundingSource;
        }
    }
}
