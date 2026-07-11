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
        var sheet = new BusinessDayTidSheet
        {
            Date = date.Date,
            MorningBusinessDate = date.Date.AddDays(-1).ToString("dd-MMM-yyyy"),
            DayBusinessDate = date.Date.ToString("dd-MMM-yyyy"),
            NightBusinessDate = date.Date.ToString("dd-MMM-yyyy")
        };

        // 1. Morning slot: Shift A of D-1 (yesterday)
        var prevDate = date.Date.AddDays(-1);
        var morningShiftRes = await _shiftRepo.GetShiftAsync(prevDate, "A");
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
                            ShiftLabel = "Morning (12am - 8am)",
                            SlotDate = prevDate.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({prevDate.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
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
                            ShiftLabel = "Morning (12am - 8am)",
                            SlotDate = prevDate.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({prevDate.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
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
                            ShiftLabel = "Morning (12am - 8am)",
                            SlotDate = prevDate.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({prevDate.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
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
                            ShiftLabel = "Morning (12am - 8am)",
                            SlotDate = prevDate.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({prevDate.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
                        });
                        sheet.PetroCardMorning += petroVal;
                    }
                }
            }
        }

        // 2. Day slot: Shift B of D (today)
        var dayShiftRes = await _shiftRepo.GetShiftAsync(date.Date, "B");
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

                    // PhonePe Direct Day (prefer Day field, fall back to Morning for old records)
                    double ppVal = pc.PhonePeDay > 0 ? pc.PhonePeDay : pc.PhonePeMorning;
                    if (ppVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppVal,
                            Tid = pc.PhonePeTidDay ?? pc.PhonePeTidMorning ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchDay ?? pc.PhonePeBatchMorning ?? pc.PhonePeBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 AM – 8:00 PM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)"
                        });
                        sheet.PhonePeDirectDay += ppVal;
                    }

                    // PhonePe Card Day (prefer Day field, fall back to Morning for old records)
                    double ppCardVal = pc.PhonePeCardDay > 0 ? pc.PhonePeCardDay : pc.PhonePeCardMorning;
                    if (ppCardVal > 0)
                    {
                        sheet.PhonePePayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ppCardVal,
                            Tid = pc.PhonePeTidDay ?? pc.PhonePeTidMorning ?? pc.PhonePeTid,
                            Batch = pc.PhonePeBatchDay ?? pc.PhonePeBatchMorning ?? pc.PhonePeBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 AM – 8:00 PM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)"
                        });
                        sheet.PhonePeCardDay += ppCardVal;
                    }

                    // Credit Card Day (prefer Day field, fall back to Morning for old records)
                    double ccVal = pc.CreditCardDay > 0 ? pc.CreditCardDay : pc.CreditCardMorning;
                    if (ccVal > 0)
                    {
                        sheet.CardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = ccVal,
                            Tid = pc.CreditCardTidDay ?? pc.CreditCardTidMorning ?? pc.CardTid,
                            Batch = pc.CreditCardBatchDay ?? pc.CreditCardBatchMorning ?? pc.CardBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 AM – 8:00 PM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)"
                        });
                        sheet.PineLabsCardDay += ccVal;
                    }

                    // Petro Card Day (prefer Day field, fall back to Morning for old records)
                    double petroVal = pc.PetroCardDay > 0 ? pc.PetroCardDay : pc.PetroCardMorning;
                    if (petroVal > 0)
                    {
                        sheet.PetroCardPayments.Add(new TidItemDto
                        {
                            DsmName = entry.DsmName,
                            PumpId = entry.PumpId,
                            RomanIndex = ToRoman(entry.PumpId),
                            Amount = petroVal,
                            Tid = pc.PetroCardTidDay ?? pc.PetroCardTidMorning ?? pc.PetroCardTid,
                            Batch = pc.PetroCardBatchDay ?? pc.PetroCardBatchMorning ?? pc.PetroCardBatch,
                            Slot = "Day",
                            PaymentCollection = pc,
                            ShiftLabel = "Day (8am - 8pm)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 AM – 8:00 PM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)"
                        });
                        sheet.PetroCardDay += petroVal;
                    }
                }
            }
        }

        // 3. Night slot: Shift A of D (today)
        var nightShiftRes = await _shiftRepo.GetShiftAsync(date.Date, "A");
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
                            ShiftLabel = "Night (8pm - 12am)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 PM – 12:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)"
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
                            ShiftLabel = "Night (8pm - 12am)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 PM – 12:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)"
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
                            ShiftLabel = "Night (8pm - 12am)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 PM – 12:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)"
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
                            ShiftLabel = "Night (8pm - 12am)",
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "8:00 PM – 12:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)"
                        });
                        sheet.PetroCardNight += petroVal;
                    }
                }
            }
        }

        // Apply grouping and merging logic
        sheet.PhonePePayments = GroupAndMerge(sheet.PhonePePayments);
        sheet.CardPayments = GroupAndMerge(sheet.CardPayments);
        sheet.PetroCardPayments = GroupAndMerge(sheet.PetroCardPayments);

        return sheet;
    }

    private List<TidItemDto> GroupAndMerge(List<TidItemDto> items)
    {
        var result = new List<TidItemDto>();
        
        var grouped = items
            .GroupBy(x => new 
            { 
                Slot = x.Slot, 
                Tid = (x.Tid ?? "").Trim(), 
                Batch = (x.Batch ?? "").Trim() 
            });

        foreach (var g in grouped)
        {
            var first = g.First();
            var mergedDsmName = string.Join(", ", g.Select(x => x.DsmName).Distinct());
            var mergedRomanIndex = string.Join(", ", g.Select(x => x.RomanIndex).Distinct());
            
            result.Add(new TidItemDto
            {
                DsmName = mergedDsmName,
                PumpId = first.PumpId,
                RomanIndex = mergedRomanIndex,
                Amount = g.Sum(x => x.Amount),
                Tid = string.IsNullOrEmpty(g.Key.Tid) ? "—" : g.Key.Tid,
                Batch = string.IsNullOrEmpty(g.Key.Batch) ? "—" : g.Key.Batch,
                Slot = g.Key.Slot,
                ShiftLabel = first.ShiftLabel,
                PaymentCollection = first.PaymentCollection,
                SlotDate = first.SlotDate,
                TimeWindow = first.TimeWindow,
                SlotDisplaySubtitle = first.SlotDisplaySubtitle
            });
        }
        
        return result.OrderBy(x => GetSlotOrder(x.Slot)).ToList();
    }

    private static int GetSlotOrder(string slot)
    {
        return slot switch
        {
            "Morning" => 1,
            "Day" => 2,
            "Night" => 3,
            _ => 4
        };
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
