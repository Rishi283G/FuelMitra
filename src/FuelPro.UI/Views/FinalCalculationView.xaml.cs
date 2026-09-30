using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.UI.Views;

public partial class FinalCalculationView : UserControl
{
    private FinalCalculationViewModel? _vm;

    public FinalCalculationView()
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

        _vm = DataContext as FinalCalculationViewModel;
        if (_vm != null)
        {
            _vm.DynamicColumnsRefreshed += RebuildGridColumns;
            RebuildGridColumns();
        }
    }

    private void RebuildGridColumns()
    {
        if (_vm == null || DsmSummaryDataGrid == null) return;

        Dispatcher.InvokeAsync(() =>
        {
            var currencyConverter = TryFindResource("CurrencyConverter") as IValueConverter;

            bool IsDigitalMode(CollectionTypeMaster c) =>
                !string.Equals(c.Code?.Replace("_", ""), "CASHDEPOSIT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Code?.Replace("_", ""), "CASHINHAND", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Code?.Replace("_", ""), "BANKCASH", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Code?.Replace("_", ""), "OTHERS", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Code?.Replace("_", ""), "OTHER", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Category, "Cash", StringComparison.OrdinalIgnoreCase) &&
                !c.DisplayName.Contains("Cash", StringComparison.OrdinalIgnoreCase);

            var activeDigitalTypes = _vm.ActiveCollectionTypes?.Where(IsDigitalMode).ToList() ?? new List<CollectionTypeMaster>();

            // 1. Rebuild DsmSummaryDataGrid columns
            DsmSummaryDataGrid.Columns.Clear();

            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "DSM",
                Binding = new Binding("DsmName"),
                FontWeight = FontWeights.Bold
            });

            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Pump",
                Binding = new Binding("PumpLabel")
            });

            if (activeDigitalTypes.Count > 0)
            {
                foreach (var colType in activeDigitalTypes)
                {
                    DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = colType.DisplayName,
                        Binding = new Binding($"[{colType.Code}]") { Converter = currencyConverter }
                    });
                }
            }
            else
            {
                DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "PhonePe",
                    Binding = new Binding("PhonePeTotal") { Converter = currencyConverter }
                });
                DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Credit Card",
                    Binding = new Binding("CreditCardTotal") { Converter = currencyConverter }
                });
                DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "PetroCard",
                    Binding = new Binding("PetroCardTotal") { Converter = currencyConverter }
                });
            }

            // Single unified Bank Cash column
            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Bank Cash",
                Binding = new Binding("CashDeposit") { Converter = currencyConverter }
            });

            var debtorCol = new DataGridTextColumn
            {
                Header = "Debtors",
                Binding = new Binding("Debit") { Converter = currencyConverter }
            };
            var debtorStyle = new Style(typeof(TextBlock));
            debtorStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Red));
            debtorCol.ElementStyle = debtorStyle;
            DsmSummaryDataGrid.Columns.Add(debtorCol);

            var expCol = new DataGridTextColumn
            {
                Header = "Expenses",
                Binding = new Binding("Expenses") { Converter = currencyConverter }
            };
            var expStyle = new Style(typeof(TextBlock));
            expStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Red));
            expCol.ElementStyle = expStyle;
            DsmSummaryDataGrid.Columns.Add(expCol);

            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Testing",
                Binding = new Binding("Testing") { Converter = currencyConverter }
            });

            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Hand Cash",
                Binding = new Binding("CashInHand") { Converter = currencyConverter }
            });

            // Others column (record only)
            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Others",
                Binding = new Binding("Others") { Converter = currencyConverter }
            });

            var grossCol = new DataGridTextColumn
            {
                Header = "Gross Sale",
                Binding = new Binding("GrossSales") { Converter = currencyConverter },
                FontWeight = FontWeights.Bold
            };
            var grossStyle = new Style(typeof(TextBlock));
            grossStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Green));
            grossCol.ElementStyle = grossStyle;
            DsmSummaryDataGrid.Columns.Add(grossCol);

            DsmSummaryDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Short",
                Binding = new Binding("Mismatch") { Converter = currencyConverter },
                FontWeight = FontWeights.Bold
            });

            // 2. Rebuild DsmShiftTotalsDataGrid columns
            if (DsmShiftTotalsDataGrid != null)
            {
                DsmShiftTotalsDataGrid.Columns.Clear();

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "DSM",
                    Binding = new Binding("DsmName"),
                    FontWeight = FontWeights.Bold
                });

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Sessions",
                    Binding = new Binding("SessionsCount"),
                    Width = new DataGridLength(70)
                });

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Pumps",
                    Binding = new Binding("AssignedPumpsDisplay")
                });

                var stGrossCol = new DataGridTextColumn
                {
                    Header = "Gross Sale",
                    Binding = new Binding("GrossSales") { Converter = currencyConverter },
                    FontWeight = FontWeights.Bold
                };
                var stGrossStyle = new Style(typeof(TextBlock));
                stGrossStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Green));
                stGrossCol.ElementStyle = stGrossStyle;
                DsmShiftTotalsDataGrid.Columns.Add(stGrossCol);

                if (activeDigitalTypes.Count > 0)
                {
                    foreach (var colType in activeDigitalTypes)
                    {
                        DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                        {
                            Header = colType.DisplayName,
                            Binding = new Binding($"[{colType.Code}]") { Converter = currencyConverter }
                        });
                    }
                }
                else
                {
                    DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = "Digital Total",
                        Binding = new Binding("DigitalTotal") { Converter = currencyConverter }
                    });
                }

                // Single unified Bank Cash column
                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Bank Cash",
                    Binding = new Binding("CashDeposit") { Converter = currencyConverter }
                });

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Hand Cash",
                    Binding = new Binding("CashInHand") { Converter = currencyConverter }
                });

                var stDebtorCol = new DataGridTextColumn
                {
                    Header = "Debtors",
                    Binding = new Binding("Debit") { Converter = currencyConverter }
                };
                var stDebtorStyle = new Style(typeof(TextBlock));
                stDebtorStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Red));
                stDebtorCol.ElementStyle = stDebtorStyle;
                DsmShiftTotalsDataGrid.Columns.Add(stDebtorCol);

                var stExpCol = new DataGridTextColumn
                {
                    Header = "Expenses",
                    Binding = new Binding("Expenses") { Converter = currencyConverter }
                };
                var stExpStyle = new Style(typeof(TextBlock));
                stExpStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Red));
                stExpCol.ElementStyle = stExpStyle;
                DsmShiftTotalsDataGrid.Columns.Add(stExpCol);

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Testing",
                    Binding = new Binding("Testing") { Converter = currencyConverter }
                });

                // Others column (record only)
                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Others",
                    Binding = new Binding("Others") { Converter = currencyConverter }
                });

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Total Collection",
                    Binding = new Binding("TotalCollection") { Converter = currencyConverter },
                    FontWeight = FontWeights.Bold
                });

                DsmShiftTotalsDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Short",
                    Binding = new Binding("Mismatch") { Converter = currencyConverter },
                    FontWeight = FontWeights.Bold
                });
            }

            // 3. Rebuild DsmSummaryTotalsPanel items
            if (DsmSummaryTotalsPanel != null && _vm.DsmSummaryTotals != null)
            {
                DsmSummaryTotalsPanel.Children.Clear();

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = "TOTALS:",
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Gross: ₹{_vm.DsmSummaryTotals.GrossSales:N2}",
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 230, 201)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                if (activeDigitalTypes.Count > 0)
                {
                    foreach (var colType in activeDigitalTypes)
                    {
                        double amt = _vm.DsmSummaryTotals.GetAmount(colType.Code);
                        DsmSummaryTotalsPanel.Children.Add(new TextBlock
                        {
                            Text = $"{colType.DisplayName}: ₹{amt:N2}",
                            Foreground = System.Windows.Media.Brushes.White,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 12, 0)
                        });
                    }
                }
                else
                {
                    DsmSummaryTotalsPanel.Children.Add(new TextBlock
                    {
                        Text = $"PhonePe: ₹{_vm.DsmSummaryTotals.PhonePeTotal:N2}",
                        Foreground = System.Windows.Media.Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    });
                    DsmSummaryTotalsPanel.Children.Add(new TextBlock
                    {
                        Text = $"Card: ₹{_vm.DsmSummaryTotals.CreditCardTotal:N2}",
                        Foreground = System.Windows.Media.Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    });
                    DsmSummaryTotalsPanel.Children.Add(new TextBlock
                    {
                        Text = $"PetroCard: ₹{_vm.DsmSummaryTotals.PetroCardTotal:N2}",
                        Foreground = System.Windows.Media.Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    });
                }

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Bank Cash: ₹{_vm.DsmSummaryTotals.CashDeposit:N2}",
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Hand Cash: ₹{_vm.DsmSummaryTotals.CashInHand:N2}",
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Debtors: ₹{_vm.DsmSummaryTotals.Debit:N2}",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 205, 210)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Expenses: ₹{_vm.DsmSummaryTotals.Expenses:N2}",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 205, 210)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Testing: ₹{_vm.DsmSummaryTotals.Testing:N2}",
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Others: ₹{_vm.DsmSummaryTotals.Others:N2}",
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                var shortVal = _vm.DsmSummaryTotals.Mismatch;
                DsmSummaryTotalsPanel.Children.Add(new TextBlock
                {
                    Text = $"Short: {(shortVal < 0 ? "-" : (shortVal > 0 ? "+" : ""))}₹{Math.Abs(shortVal):N2}",
                    FontWeight = FontWeights.Bold,
                    Foreground = Math.Abs(shortVal) < 0.01 ? System.Windows.Media.Brushes.LightGreen : (shortVal < 0 ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.LightGreen),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });
            }
        });
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FinalCalculationViewModel vm && !vm.HasData)
        {
            vm.LoadShiftDataCommand.Execute(null);
        }
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FinalCalculationViewModel vm || !vm.HasData || vm.CurrentReport == null)
        {
            MessageBox.Show("No shift data loaded to print.",
                "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var printService = new PrintService();
            printService.PrintFinalCalculation(vm.CurrentReport);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Print failed:\n" + ex.Message,
                "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static double GetReconAmount(List<ReconciliationRowDto> rows, string description)
        => rows.FirstOrDefault(r => r.Description == description || r.Description.StartsWith(description + " ("))?.Amount ?? 0;

    /// <summary>
    /// Reconstructs a <see cref="CashAggregateDto"/> from the denomination display rows
    /// already computed by the ViewModel — zero recalculation.
    /// </summary>
    private static CashAggregateDto BuildCashAgg(FinalCalculationViewModel vm, string cashType)
    {
        var dto  = new CashAggregateDto();
        var rows = cashType == "Cash1" ? vm.Cash1Rows : vm.Cash2Rows;

        foreach (var row in rows)
        {
            switch (row.Denomination)
            {
                case "₹500": dto.Total500   = row.TotalCount; break;
                case "₹200": dto.Total200   = row.TotalCount; break;
                case "₹100": dto.Total100   = row.TotalCount; break;
                case "₹50":  dto.Total50    = row.TotalCount; break;
                case "₹20":  dto.Total20    = row.TotalCount; break;
                case "₹10":  dto.Total10    = row.TotalCount; break;
                case "Coin": dto.TotalCoins = row.TotalCount; break;
                case "Cash 1": dto.CashDepositTotal = row.TotalAmount; break;
            }
        }

        dto.GrandTotal = cashType == "Cash1" ? vm.Cash1Total : vm.Cash2Total;
        return dto;
    }
}
