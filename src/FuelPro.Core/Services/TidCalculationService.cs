using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;

namespace FuelPro.Core.Services;

public class TidCalculationService : ITidCalculationService
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;

    public TidCalculationService(IShiftRepository shiftRepo, IDsmEntryRepository dsmRepo)
    {
        _shiftRepo = shiftRepo;
        _dsmRepo = dsmRepo;
    }

    public async Task<BusinessDayTidSheet> GetTidSheetAsync(DateTime date)
    {
        var sheet = new BusinessDayTidSheet { Date = date.Date };

        // 1. Morning slot: Shift B of D-1 (yesterday)
        var prevDate = date.Date.AddDays(-1);
        var morningShiftRes = await _shiftRepo.GetShiftAsync(prevDate, "B");
        if (morningShiftRes.Success && morningShiftRes.Data != null)
        {
            var entriesRes = await _dsmRepo.GetEntriesForShiftAsync(morningShiftRes.Data.ShiftId);
            if (entriesRes.Success && entriesRes.Data != null)
            {
                foreach (var entry in entriesRes.Data.OrderBy(e => e.PumpId))
                {
                    var pc = entry.PaymentCollection;
                    if (pc == null) continue;
                    pc.DsmEntry = entry;

                    // PhonePe Direct Morning
                    double ppVal = pc.PhonePeMorning;
                    if (ppVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppVal,
                            Tid = pc.PhonePeTidMorning ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchMorning ?? pc.PhonePeBatch,
                            Slot = "Morning",
                            PaymentCollection = pc,
                            ShiftLabel = "Morning (12am - 8am)"
                        });
                        sheet.PhonePeDirectMorning += ppVal;
                    }

                    // PhonePe Card Morning
                    double ppCardVal = pc.PhonePeCardMorning;
                    if (ppCardVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppCardVal,
                            Tid = pc.PhonePeTidMorning ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchMorning ?? pc.PhonePeBatch,
                            Slot = "Morning",
                            PaymentCollection = pc,
                            ShiftLabel = "Morning (12am - 8am)"
                        });
                        sheet.PhonePeCardMorning += ppCardVal;
                    }

                    // Credit Card Morning
                    double ccVal = pc.CreditCardMorning;
                    if (ccVal > 0)
                    {
                        sheet.CardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ccVal,
                            Tid = pc.CreditCardTidMorning ?? pc.CardTid,
                            Batch = pc.CreditCardBatchMorning ?? pc.CardBatch,
                            Slot = "Morning",
                            PaymentCollection = pc,
                            ShiftLabel = "Morning (12am - 8am)"
                        });
                        sheet.PineLabsCardMorning += ccVal;
                    }

                    // Petro Card Morning
                    double petroVal = pc.PetroCardMorning;
                    if (petroVal > 0)
                    {
                        sheet.PetroCardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = petroVal,
                            Tid = pc.PetroCardTidMorning ?? pc.PetroCardTid,
                            Batch = pc.PetroCardBatchMorning ?? pc.PetroCardBatch,
                            Slot = "Morning",
                            PaymentCollection = pc,
                            ShiftLabel = "Morning (12am - 8am)"
                        });
                        sheet.PetroCardMorning += petroVal;
                    }
                }
            }
        }

        // 2. Day slot: Shift A of D (today)
        var dayShiftRes = await _shiftRepo.GetShiftAsync(date.Date, "A");
        if (dayShiftRes.Success && dayShiftRes.Data != null)
        {
            var entriesRes = await _dsmRepo.GetEntriesForShiftAsync(dayShiftRes.Data.ShiftId);
            if (entriesRes.Success && entriesRes.Data != null)
            {
                foreach (var entry in entriesRes.Data.OrderBy(e => e.PumpId))
                {
                    var pc = entry.PaymentCollection;
                    if (pc == null) continue;
                    pc.DsmEntry = entry;

                    // PhonePe Direct Day (Shift A uses Morning columns)
                    double ppVal = pc.PhonePeMorning;
                    if (ppVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppVal,
                            Tid = pc.PhonePeTidMorning ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchMorning ?? pc.PhonePeBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)"
                        });
                        sheet.PhonePeDirectDay += ppVal;
                    }

                    // PhonePe Card Day (Shift A uses Morning columns)
                    double ppCardVal = pc.PhonePeCardMorning;
                    if (ppCardVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppCardVal,
                            Tid = pc.PhonePeTidMorning ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchMorning ?? pc.PhonePeBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)"
                        });
                        sheet.PhonePeCardDay += ppCardVal;
                    }

                    // Credit Card Day (Shift A uses Morning columns)
                    double ccVal = pc.CreditCardMorning;
                    if (ccVal > 0)
                    {
                        sheet.CardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ccVal,
                            Tid = pc.CreditCardTidMorning ?? pc.CardTid,
                            Batch = pc.CreditCardBatchMorning ?? pc.CardBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)"
                        });
                        sheet.PineLabsCardDay += ccVal;
                    }

                    // Petro Card Day (Shift A uses Morning columns)
                    double petroVal = pc.PetroCardMorning;
                    if (petroVal > 0)
                    {
                        sheet.PetroCardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = petroVal,
                            Tid = pc.PetroCardTidMorning ?? pc.PetroCardTid,
                            Batch = pc.PetroCardBatchMorning ?? pc.PetroCardBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)"
                        });
                        sheet.PetroCardDay += petroVal;
                    }
                }
            }
        }

        // 3. Night slot: Shift B of D (today)
        var nightShiftRes = await _shiftRepo.GetShiftAsync(date.Date, "B");
        if (nightShiftRes.Success && nightShiftRes.Data != null)
        {
            var entriesRes = await _dsmRepo.GetEntriesForShiftAsync(nightShiftRes.Data.ShiftId);
            if (entriesRes.Success && entriesRes.Data != null)
            {
                foreach (var entry in entriesRes.Data.OrderBy(e => e.PumpId))
                {
                    var pc = entry.PaymentCollection;
                    if (pc == null) continue;
                    pc.DsmEntry = entry;

                    // PhonePe Direct Night
                    double ppVal = pc.PhonePeNight;
                    if (ppVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppVal,
                            Tid = pc.PhonePeTidNight ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchNight ?? pc.PhonePeBatch,
                            Slot = "Night",
                            PaymentCollection = pc,
                            ShiftLabel = "Night (8pm - 12am)"
                        });
                        sheet.PhonePeDirectNight += ppVal;
                    }

                    // PhonePe Card Night
                    double ppCardVal = pc.PhonePeCardNight;
                    if (ppCardVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppCardVal,
                            Tid = pc.PhonePeTidNight ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchNight ?? pc.PhonePeBatch,
                            Slot = "Night",
                            PaymentCollection = pc,
                            ShiftLabel = "Night (8pm - 12am)"
                        });
                        sheet.PhonePeCardNight += ppCardVal;
                    }

                    // Credit Card Night
                    double ccVal = pc.CreditCardNight;
                    if (ccVal > 0)
                    {
                        sheet.CardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ccVal,
                            Tid = pc.CreditCardTidNight ?? pc.CardTid,
                            Batch = pc.CreditCardBatchNight ?? pc.CardBatch,
                            Slot = "Night",
                            PaymentCollection = pc,
                            ShiftLabel = "Night (8pm - 12am)"
                        });
                        sheet.PineLabsCardNight += ccVal;
                    }

                    // Petro Card Night
                    double petroVal = pc.PetroCardNight;
                    if (petroVal > 0)
                    {
                        sheet.PetroCardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = petroVal,
                            Tid = pc.PetroCardTidNight ?? pc.PetroCardTid,
                            Batch = pc.PetroCardBatchNight ?? pc.PetroCardBatch,
                            Slot = "Night",
                            PaymentCollection = pc,
                            ShiftLabel = "Night (8pm - 12am)"
                        });
                        sheet.PetroCardNight += petroVal;
                    }
                }
            }
        }

        return sheet;
    }

    public async Task<Dictionary<DateTime, BusinessDayTidSheet>> GetTidSheetsForRangeAsync(DateTime startDate, DateTime endDate)
    {
        var result = new Dictionary<DateTime, BusinessDayTidSheet>();
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            result[date] = await GetTidSheetAsync(date);
        }
        return result;
    }

    private static string ToRoman(int number)
    {
        if (number <= 0) return number.ToString();
        if (number >= 1000) return "M" + ToRoman(number - 1000);
        if (number >= 900) return "CM" + ToRoman(number - 900);
        if (number >= 500) return "D" + ToRoman(number - 500);
        if (number >= 400) return "CD" + ToRoman(number - 400);
        if (number >= 100) return "C" + ToRoman(number - 100);
        if (number >= 90) return "XC" + ToRoman(number - 90);
        if (number >= 50) return "L" + ToRoman(number - 50);
        if (number >= 40) return "XL" + ToRoman(number - 40);
        if (number >= 10) return "X" + ToRoman(number - 10);
        if (number >= 9) return "IX" + ToRoman(number - 9);
        if (number >= 5) return "V" + ToRoman(number - 5);
        if (number >= 4) return "IV" + ToRoman(number - 4);
        if (number >= 1) return "I" + ToRoman(number - 1);
        return string.Empty;
    }
}
