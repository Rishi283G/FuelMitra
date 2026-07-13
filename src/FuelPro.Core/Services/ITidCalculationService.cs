using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

public class TidItemDto
{
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public string RomanIndex { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string? Tid { get; set; }
    public string? Batch { get; set; }
    public string Slot { get; set; } = string.Empty; // "Morning", "Day", "Night"
    [System.Text.Json.Serialization.JsonIgnore]
    public PaymentCollection PaymentCollection { get; set; } = null!;
    public string ShiftLabel { get; set; } = string.Empty;
    public string SlotDate { get; set; } = string.Empty;
    public string TimeWindow { get; set; } = string.Empty;
    public string SlotDisplaySubtitle { get; set; } = string.Empty;
}

public class BusinessDayTidSheet
{
    public DateTime Date { get; set; }
    public string MorningBusinessDate { get; set; } = string.Empty;
    public string DayBusinessDate { get; set; } = string.Empty;
    public string NightBusinessDate { get; set; } = string.Empty;
    
    // PhonePe Direct
    public double PhonePeDirectMorning { get; set; }
    public double PhonePeDirectDay { get; set; }
    public double PhonePeDirectNight { get; set; }
    public double PhonePeDirectTotal => PhonePeDirectMorning + PhonePeDirectDay + PhonePeDirectNight;

    // PhonePe Card (UPI QR Swipes)
    public double PhonePeCardMorning { get; set; }
    public double PhonePeCardDay { get; set; }
    public double PhonePeCardNight { get; set; }
    public double PhonePeCardTotal => PhonePeCardMorning + PhonePeCardDay + PhonePeCardNight;

    // Combined PhonePe (TID Sheet aggregation)
    public double PhonePeTotal => PhonePeDirectTotal + PhonePeCardTotal;

    // PineLabs Card
    public double PineLabsCardMorning { get; set; }
    public double PineLabsCardDay { get; set; }
    public double PineLabsCardNight { get; set; }
    public double PineLabsCardTotal => PineLabsCardMorning + PineLabsCardDay + PineLabsCardNight;

    // Petro Card
    public double PetroCardMorning { get; set; }
    public double PetroCardDay { get; set; }
    public double PetroCardNight { get; set; }
    public double PetroCardTotal => PetroCardMorning + PetroCardDay + PetroCardNight;

    // Grand Total Digital (DSM payments + debtor repayments)
    public double GrandTotal =>
        PhonePeTotal + PineLabsCardTotal + PetroCardTotal
        + DebtorPhonePeRepaymentTotal + DebtorCardRepaymentTotal + DebtorPetroCardRepaymentTotal;

    public List<TidItemDto> PhonePePayments { get; set; } = new();
    public List<TidItemDto> CardPayments { get; set; } = new();
    public List<TidItemDto> PetroCardPayments { get; set; } = new();

    // Debtor Repayments — PhonePe
    public List<TidItemDto> DebtorPhonePeRepayments { get; set; } = new();
    public double DebtorPhonePeRepaymentMorning { get; set; }
    public double DebtorPhonePeRepaymentDay     { get; set; }
    public double DebtorPhonePeRepaymentNight   { get; set; }
    public double DebtorPhonePeRepaymentTotal =>
        DebtorPhonePeRepaymentMorning + DebtorPhonePeRepaymentDay + DebtorPhonePeRepaymentNight;

    // Debtor Repayments — PineLabs Card
    public List<TidItemDto> DebtorCardRepayments { get; set; } = new();
    public double DebtorCardRepaymentMorning { get; set; }
    public double DebtorCardRepaymentDay     { get; set; }
    public double DebtorCardRepaymentNight   { get; set; }
    public double DebtorCardRepaymentTotal =>
        DebtorCardRepaymentMorning + DebtorCardRepaymentDay + DebtorCardRepaymentNight;

    // Debtor Repayments — Petro Card
    public List<TidItemDto> DebtorPetroCardRepayments { get; set; } = new();
    public double DebtorPetroCardRepaymentMorning { get; set; }
    public double DebtorPetroCardRepaymentDay     { get; set; }
    public double DebtorPetroCardRepaymentNight   { get; set; }
    public double DebtorPetroCardRepaymentTotal =>
        DebtorPetroCardRepaymentMorning + DebtorPetroCardRepaymentDay + DebtorPetroCardRepaymentNight;
}

public interface ITidCalculationService
{
    Task<BusinessDayTidSheet> GetTidSheetAsync(DateTime date);
    Task<Dictionary<DateTime, BusinessDayTidSheet>> GetTidSheetsForRangeAsync(DateTime startDate, DateTime endDate);
}
