using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;

namespace FuelPro.UI.Views;

public partial class DayTotalView : UserControl
{
    public DayTotalView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DayTotalViewModel vm && !vm.HasData)
            vm.RefreshCommand.Execute(null);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DayTotalViewModel vm || !vm.HasData)
        {
            MessageBox.Show("No data loaded to print.", "Print",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string stationName = "VKD Petroleum";
        try
        {
            var settingsRepo = App.Services.GetRequiredService<FuelPro.Core.Repositories.ISettingsRepository>();
            var s = settingsRepo.GetSettingsAsync().GetAwaiter().GetResult();
            if (s.Success && s.Data != null) stationName = s.Data.PumpStationName;
        }
        catch { }

        var payload = new
        {
            date                      = vm.SelectedDate.ToString("dd-MM-yyyy"),
            stationName,
            totalFuelSaleAmount       = vm.TotalDayFuelSaleAmount,
            totalDayLitres            = vm.TotalDayLitres,
            totalHsdLitres            = vm.TotalHsdLitres,
            totalMsILitres            = vm.TotalMsILitres,
            totalMsIILitres           = vm.TotalMsIILitres,
            totalMsLitres             = vm.TotalMsLitres,
            reconciliationTotalAmount = vm.ReconciliationTotalAmount,
            grossDaySaleTotal         = vm.GrossDaySaleTotal,
            difference                = vm.Difference,
            creditorsTotal            = vm.CreditorsTotal,
            expensesTotal             = vm.ExpensesTotal,
            phonePeTotal              = vm.PhonePeTotal,
            phonePeCardMorningTotal   = vm.PhonePeCardMorningTotal,
            phonePeCardNightTotal     = vm.PhonePeCardNightTotal,
            creditCardTotal           = vm.CreditCardTotal,
            petroCardTotal            = vm.PetroCardTotal,
            bankCashTotal             = vm.BankCashTotal,
            cashInHandTotal           = vm.CashInHandTotal,
            nozzleRows = vm.NozzleSaleRows.Select(r => new
            {
                pumpId         = r.PumpId,
                fuelType       = r.FuelType,
                openingReading = r.OpeningReading,
                closingReading = r.ClosingReading,
                grossLitres    = r.GrossLitres,
                netSaleLitres  = r.NetSaleLitres,
                rate           = r.Rate,
                amount         = r.Amount
            }).ToList(),
            dsmEntries = vm.DsmPrintRows.Select(r => new
            {
                shift       = r.Shift,
                dsmName     = r.Row.DsmName,
                pumpNo      = r.Row.PumpId,
                phonePe     = r.Row.PhonePe,
                phonePeCardMorning = r.Row.PhonePeCardMorning,
                phonePeCardNight   = r.Row.PhonePeCardNight,
                creditCard  = r.Row.CreditCard,
                petroCard   = r.Row.PetroCard,
                bankCash    = r.Row.CashDeposit,
                debit       = r.Row.Debit,
                expenses    = r.Row.Expenses,
                testing     = r.Row.Testing,
                cashInHand  = r.Row.CashInHand,
                grossSale   = r.Row.GrossSales,
                mismatch    = (r.Row.PhonePe + r.Row.PhonePeCardMorning + r.Row.PhonePeCardNight + r.Row.CreditCard + r.Row.PetroCard + r.Row.CashDeposit + r.Row.Debit + r.Row.Expenses + r.Row.Testing + r.Row.CashInHand) - r.Row.GrossSales
            }).ToList(),
            creditors = vm.CreditorRows.Select(c => new
            {
                dsmName    = c.DsmName,
                debtorName = c.DebtorName,
                chequeNo   = c.ChequeNo,
                amount     = c.Amount
            }).ToList(),
            expenses = vm.ExpenseRows.Select(ex => new
            {
                dsmName     = ex.DsmName,
                description = ex.Description,
                amount      = ex.Amount
            }).ToList()
        };

        new PrintService().PrintDayTotal(payload);
    }
}
