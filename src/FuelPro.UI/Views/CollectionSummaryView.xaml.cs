using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FuelPro.UI.ViewModels;

namespace FuelPro.UI.Views;

public partial class CollectionSummaryView : UserControl
{
    private CollectionSummaryViewModel? _vm;

    public CollectionSummaryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.DynamicColumnsRefreshed -= RebuildGridColumns;
        }

        _vm = DataContext as CollectionSummaryViewModel;
        if (_vm != null)
        {
            _vm.DynamicColumnsRefreshed += RebuildGridColumns;
            RebuildGridColumns();
        }
    }

    private void RebuildGridColumns()
    {
        if (_vm == null || DaySplitDataGrid == null) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (_vm.ActiveCollectionTypes.Count == 0) return;

            DaySplitDataGrid.Columns.Clear();

            var currencyConverter = TryFindResource("CurrencyConverter") as IValueConverter;

            // 1. Date
            DaySplitDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Date",
                Binding = new Binding("DateDisplay"),
                Width = new DataGridLength(90)
            });

            // 2. Bank Cash
            DaySplitDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Bank Cash",
                Binding = new Binding("CashDeposit") { Converter = currencyConverter },
                Width = new DataGridLength(110)
            });

            // 3. Cash In Hand
            DaySplitDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Cash In Hand",
                Binding = new Binding("CashInHand") { Converter = currencyConverter },
                Width = new DataGridLength(110)
            });

            // 4. Dynamic active collection types (e.g. PhonePe, PineLabs Card, PetroCard, SBI Redeem, Paytm, QR / Online, Mobikwik, etc.)
            foreach (var colType in _vm.ActiveCollectionTypes)
            {
                DaySplitDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = colType.DisplayName,
                    Binding = new Binding($"ModeAmounts[{colType.DisplayName}]") { Converter = currencyConverter },
                    Width = new DataGridLength(115)
                });
            }

            // 5. Debtors / Credit
            DaySplitDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Credit/Debit",
                Binding = new Binding("Debit") { Converter = currencyConverter },
                Width = new DataGridLength(110)
            });

            // 6. Day Total
            DaySplitDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Day Total",
                Binding = new Binding("DayTotal") { Converter = currencyConverter },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        });
    }
}
