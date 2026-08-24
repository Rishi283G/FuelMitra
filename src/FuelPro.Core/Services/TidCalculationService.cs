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
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IDsmPersonalDebtorRepository _personalDebtorRepo;
    private readonly ICollectionTypeService? _collectionTypeService;

    public TidCalculationService(
        IShiftRepository shiftRepo,
        IDsmEntryRepository dsmRepo,
        ICreditorRepaymentRepository repaymentRepo,
        IDsmPersonalDebtorRepository personalDebtorRepo,
        ICollectionTypeService? collectionTypeService = null)
    {
        _shiftRepo = shiftRepo;
        _dsmRepo = dsmRepo;
        _repaymentRepo = repaymentRepo;
        _personalDebtorRepo = personalDebtorRepo;
        _collectionTypeService = collectionTypeService;
    }

    public async Task<BusinessDayTidSheet> GetTidSheetAsync(DateTime date)
    {
        var sheet = new BusinessDayTidSheet
        {
            Date = date.Date,
            MorningBusinessDate = date.Date.ToString("dd-MMM-yyyy"),
            DayBusinessDate = date.Date.ToString("dd-MMM-yyyy"),
            NightBusinessDate = date.Date.ToString("dd-MMM-yyyy")
        };

        var dynamicDsmItems = new List<TidItemDto>();
        var dynamicDebtorItems = new List<TidItemDto>();

        // 1. Morning slot: Shift A of D (today)
        var morningShiftRes = await _shiftRepo.GetShiftAsync(date.Date, "A");
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
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
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
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
                        });
                        sheet.PhonePeCardMorning += ppCardVal;
                    }

                    // Cross-DSM QR Payments Morning
                    if (entry.QrPayments != null)
                    {
                        foreach (var qr in entry.QrPayments.Where(q => q.Amount > 0))
                        {
                            sheet.PhonePePayments.Add(new TidItemDto
                            {
                                DsmName = $"{entry.DsmName} (QR: {qr.TargetDsmName})",
                                PumpId = entry.PumpId,
                                RomanIndex = ToRoman(entry.PumpId),
                                Amount = qr.Amount,
                                Tid = !string.IsNullOrWhiteSpace(qr.Tid) ? qr.Tid : (pc.PhonePeTidMorning ?? pc.PhonePeTid),
                                Batch = !string.IsNullOrWhiteSpace(qr.Batch) ? qr.Batch : (pc.PhonePeBatchMorning ?? pc.PhonePeBatch),
                                Slot = "Morning",
                                PaymentCollection = pc,
                                ShiftLabel = "Morning (12am - 8am)",
                                SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                                TimeWindow = "12:00 AM – 8:00 AM",
                                SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
                            });
                            sheet.PhonePeDirectMorning += qr.Amount;
                        }
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
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 12:00 AM – 8:00 AM)"
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
                            SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                            TimeWindow = "12:00 AM – 8:00 AM",
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 12:00 AM – 8:00 AM)",
                            CollectionTypeCode = "PETROCARD",
                            CollectionTypeName = "Petro Card"
                        });
                        sheet.PetroCardMorning += petroVal;
                    }

                    // Dynamic collection items Morning (e.g. SBI Redeem, Paytm, QR, Mobikwik)
                    if (pc.Items != null)
                    {
                        foreach (var item in pc.Items.Where(i => i.Amount > 0 || !string.IsNullOrWhiteSpace(i.Tid)))
                        {
                            dynamicDsmItems.Add(new TidItemDto
                            {
                                DsmName = entry.DsmName,
                                PumpId = entry.PumpId,
                                RomanIndex = ToRoman(entry.PumpId),
                                Amount = item.Amount,
                                Tid = !string.IsNullOrWhiteSpace(item.Tid) ? item.Tid : "—",
                                Batch = !string.IsNullOrWhiteSpace(item.Batch) ? item.Batch : "—",
                                Slot = "Morning",
                                PaymentCollection = pc,
                                ShiftLabel = "Morning (12am - 8am)",
                                SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                                TimeWindow = "12:00 AM – 8:00 AM",
                                SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 12:00 AM – 8:00 AM)",
                                CollectionTypeCode = item.CollectionTypeCode
                            });
                        }
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)",
                            CollectionTypeCode = "PHONEPE",
                            CollectionTypeName = "PhonePe"
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)",
                            CollectionTypeCode = "PHONEPE",
                            CollectionTypeName = "PhonePe"
                        });
                        sheet.PhonePeCardDay += ppCardVal;
                    }

                    // Cross-DSM QR Payments Day
                    if (entry.QrPayments != null)
                    {
                        foreach (var qr in entry.QrPayments.Where(q => q.Amount > 0))
                        {
                            sheet.PhonePePayments.Add(new TidItemDto
                            {
                                DsmName = $"{entry.DsmName} (QR: {qr.TargetDsmName})",
                                PumpId = entry.PumpId,
                                RomanIndex = ToRoman(entry.PumpId),
                                Amount = qr.Amount,
                                Tid = !string.IsNullOrWhiteSpace(qr.Tid) ? qr.Tid : (pc.PhonePeTidDay ?? pc.PhonePeTidMorning ?? pc.PhonePeTid),
                                Batch = !string.IsNullOrWhiteSpace(qr.Batch) ? qr.Batch : (pc.PhonePeBatchDay ?? pc.PhonePeBatchMorning ?? pc.PhonePeBatch),
                                Slot = "Day",
                                PaymentCollection = pc,
                                ShiftLabel = "Day (8am - 8pm)",
                                SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                                TimeWindow = "8:00 AM – 8:00 PM",
                                SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)",
                                CollectionTypeCode = "PHONEPE",
                                CollectionTypeName = "PhonePe"
                            });
                            sheet.PhonePeDirectDay += qr.Amount;
                        }
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)",
                            CollectionTypeCode = "PINELAB_CARD",
                            CollectionTypeName = "PINELAB CARD"
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)",
                            CollectionTypeCode = "PETROCARD",
                            CollectionTypeName = "Petro Card"
                        });
                        sheet.PetroCardDay += petroVal;
                    }

                    // Dynamic collection items Day
                    if (pc.Items != null)
                    {
                        foreach (var item in pc.Items.Where(i => i.Amount > 0 || !string.IsNullOrWhiteSpace(i.Tid)))
                        {
                            dynamicDsmItems.Add(new TidItemDto
                            {
                                DsmName = entry.DsmName,
                                PumpId = entry.PumpId,
                                RomanIndex = ToRoman(entry.PumpId),
                                Amount = item.Amount,
                                Tid = !string.IsNullOrWhiteSpace(item.Tid) ? item.Tid : "—",
                                Batch = !string.IsNullOrWhiteSpace(item.Batch) ? item.Batch : "—",
                                Slot = "Day",
                                PaymentCollection = pc,
                                ShiftLabel = "Day (8am - 8pm)",
                                SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                                TimeWindow = "8:00 AM – 8:00 PM",
                                SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 AM – 8:00 PM)",
                                CollectionTypeCode = item.CollectionTypeCode
                            });
                        }
                    }
                }
            }
        }

        // 3. Night slot: Shift A of D+1 (tomorrow)
        var tomorrowDate = date.Date.AddDays(1);
        var nightShiftRes = await _shiftRepo.GetShiftAsync(tomorrowDate, "A");
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)",
                            CollectionTypeCode = "PHONEPE",
                            CollectionTypeName = "PhonePe"
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)",
                            CollectionTypeCode = "PHONEPE",
                            CollectionTypeName = "PhonePe"
                        });
                        sheet.PhonePeCardNight += ppCardVal;
                    }

                    // Cross-DSM QR Payments Night
                    if (entry.QrPayments != null)
                    {
                        foreach (var qr in entry.QrPayments.Where(q => q.Amount > 0))
                        {
                            sheet.PhonePePayments.Add(new TidItemDto
                            {
                                DsmName = $"{entry.DsmName} (QR: {qr.TargetDsmName})",
                                PumpId = entry.PumpId,
                                RomanIndex = ToRoman(entry.PumpId),
                                Amount = qr.Amount,
                                Tid = !string.IsNullOrWhiteSpace(qr.Tid) ? qr.Tid : (pc.PhonePeTidNight ?? pc.PhonePeTid),
                                Batch = !string.IsNullOrWhiteSpace(qr.Batch) ? qr.Batch : (pc.PhonePeBatchNight ?? pc.PhonePeBatch),
                                Slot = "Night",
                                PaymentCollection = pc,
                                ShiftLabel = "Night (8pm - 12am)",
                                SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                                TimeWindow = "8:00 PM – 12:00 AM",
                                SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)",
                                CollectionTypeCode = "PHONEPE",
                                CollectionTypeName = "PhonePe"
                            });
                            sheet.PhonePeDirectNight += qr.Amount;
                        }
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)",
                            CollectionTypeCode = "PINELAB_CARD",
                            CollectionTypeName = "PINELAB CARD"
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
                            SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)",
                            CollectionTypeCode = "PETROCARD",
                            CollectionTypeName = "Petro Card"
                        });
                        sheet.PetroCardNight += petroVal;
                    }

                    // Dynamic collection items Night
                    if (pc.Items != null)
                    {
                        foreach (var item in pc.Items.Where(i => i.Amount > 0 || !string.IsNullOrWhiteSpace(i.Tid)))
                        {
                            dynamicDsmItems.Add(new TidItemDto
                            {
                                DsmName = entry.DsmName,
                                PumpId = entry.PumpId,
                                RomanIndex = ToRoman(entry.PumpId),
                                Amount = item.Amount,
                                Tid = !string.IsNullOrWhiteSpace(item.Tid) ? item.Tid : "—",
                                Batch = !string.IsNullOrWhiteSpace(item.Batch) ? item.Batch : "—",
                                Slot = "Night",
                                PaymentCollection = pc,
                                ShiftLabel = "Night (8pm - 12am)",
                                SlotDate = date.Date.ToString("dd-MMM-yyyy"),
                                TimeWindow = "8:00 PM – 12:00 AM",
                                SlotDisplaySubtitle = $"({date.Date.ToString("dd MMM")} | 8:00 PM – 12:00 AM)",
                                CollectionTypeCode = item.CollectionTypeCode
                            });
                        }
                    }
                }
            }
        }

        // Apply grouping and merging logic to legacy lists
        sheet.PhonePePayments = GroupAndMerge(sheet.PhonePePayments);
        sheet.CardPayments = GroupAndMerge(sheet.CardPayments);
        sheet.PetroCardPayments = GroupAndMerge(sheet.PetroCardPayments);

        // Populate debtor repayments
        var repDateRes  = await _repaymentRepo.GetByDateAsync(date.Date);
        var repNextRes  = await _repaymentRepo.GetByDateAsync(tomorrowDate);

        var allRepayments = new List<CreditorRepayment>();
        if (repDateRes.Success && repDateRes.Data != null)  allRepayments.AddRange(repDateRes.Data);
        if (repNextRes.Success && repNextRes.Data != null)  allRepayments.AddRange(repNextRes.Data);

        foreach (var r in allRepayments)
        {
            var classified = SettlementWindowResolver.Classify(r);
            if (!classified.IsValid || classified.BusinessDate != date.Date)
            {
                continue;
            }

            string slot = classified.SettlementWindow!;
            string shiftLabel = "";
            string slotDate = date.Date.ToString("dd-MMM-yyyy");
            string timeWindow = "";
            string slotSubtitle = "";

            if (slot == "Morning")
            {
                shiftLabel = "Morning (12am - 8am)";
                timeWindow = "12:00 AM – 8:00 AM";
                slotSubtitle = $"({date.Date:dd MMM} | 12:00 AM – 8:00 AM)";
            }
            else if (slot == "Day")
            {
                shiftLabel = "Day (8am - 8pm)";
                timeWindow = "8:00 AM – 8:00 PM";
                slotSubtitle = $"({date.Date:dd MMM} | 8:00 AM – 8:00 PM)";
            }
            else if (slot == "Night")
            {
                shiftLabel = "Night (8pm - 12am)";
                timeWindow = "8:00 PM – 12:00 AM";
                slotSubtitle = $"({date.Date:dd MMM} | 8:00 PM – 12:00 AM)";
            }
            else continue;

            var item = new TidItemDto
            {
                DsmName   = r.CreditorName,
                PumpId    = 0,
                RomanIndex = "Debtor",
                Amount    = r.Amount,
                Tid       = string.IsNullOrWhiteSpace(r.CardTid)   ? "—" : r.CardTid,
                Batch     = string.IsNullOrWhiteSpace(r.CardBatch) ? "—" : r.CardBatch,
                Slot      = slot,
                ShiftLabel = shiftLabel,
                SlotDate  = slotDate,
                TimeWindow = timeWindow,
                SlotDisplaySubtitle = slotSubtitle,
                CollectionTypeCode = r.PaymentMode
            };

            bool isPhonePe = string.Equals(r.PaymentMode, "PhonePe", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(r.PaymentMode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(r.PaymentMode, "UPI Terminal", StringComparison.OrdinalIgnoreCase);

            bool isPineLabs = string.Equals(r.PaymentMode, "PineLabs Card", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(r.PaymentMode, "Credit Card", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(r.PaymentMode, "PineLabs", StringComparison.OrdinalIgnoreCase);

            bool isPetro = string.Equals(r.PaymentMode, "Petro Card", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(r.PaymentMode, "PetroCard", StringComparison.OrdinalIgnoreCase);

            if (isPhonePe)
            {
                sheet.DebtorPhonePeRepayments.Add(item);
                switch (slot)
                {
                    case "Morning": sheet.DebtorPhonePeRepaymentMorning += r.Amount; break;
                    case "Day":     sheet.DebtorPhonePeRepaymentDay     += r.Amount; break;
                    case "Night":   sheet.DebtorPhonePeRepaymentNight   += r.Amount; break;
                }
            }
            else if (isPineLabs)
            {
                sheet.DebtorCardRepayments.Add(item);
                switch (slot)
                {
                    case "Morning": sheet.DebtorCardRepaymentMorning += r.Amount; break;
                    case "Day":     sheet.DebtorCardRepaymentDay     += r.Amount; break;
                    case "Night":   sheet.DebtorCardRepaymentNight   += r.Amount; break;
                }
            }
            else if (isPetro)
            {
                sheet.DebtorPetroCardRepayments.Add(item);
                switch (slot)
                {
                    case "Morning": sheet.DebtorPetroCardRepaymentMorning += r.Amount; break;
                    case "Day":     sheet.DebtorPetroCardRepaymentDay     += r.Amount; break;
                    case "Night":   sheet.DebtorPetroCardRepaymentNight   += r.Amount; break;
                }
            }
            else
            {
                dynamicDebtorItems.Add(item);
            }
        }

        // Populate DSM Loss (Personal Debtor) repayments
        if (_personalDebtorRepo != null)
        {
            var pdDateRes = await _personalDebtorRepo.GetRepaymentsByDateAsync(date.Date);
            var pdNextRes = await _personalDebtorRepo.GetRepaymentsByDateAsync(tomorrowDate);

            var allPdRepayments = new List<DsmPersonalDebtorRepayment>();
            if (pdDateRes.Success && pdDateRes.Data != null) allPdRepayments.AddRange(pdDateRes.Data);
            if (pdNextRes.Success && pdNextRes.Data != null) allPdRepayments.AddRange(pdNextRes.Data);

            foreach (var r in allPdRepayments)
            {
                var fakeCreditorRep = new CreditorRepayment
                {
                    CreditorRepaymentId = r.Id,
                    CreditorName = r.DsmPersonalDebtor?.DsmName ?? "DSM Loss",
                    RepaymentDate = r.Date,
                    ShiftNumber = r.Shift?.ShiftType ?? "A",
                    PaymentMode = r.PaymentMethod,
                    CardTid = r.CardTid ?? "",
                    CardBatch = r.CardBatch ?? "",
                    Amount = r.Amount,
                    CreatedAt = r.CreatedAt
                };

                var classified = SettlementWindowResolver.Classify(fakeCreditorRep);
                if (!classified.IsValid || classified.BusinessDate != date.Date)
                {
                    continue;
                }

                string slot = classified.SettlementWindow!;
                string shiftLabel = "";
                string slotDate = date.Date.ToString("dd-MMM-yyyy");
                string timeWindow = "";
                string slotSubtitle = "";

                if (slot == "Morning")
                {
                    shiftLabel = "Morning (12am - 8am)";
                    timeWindow = "12:00 AM – 8:00 AM";
                    slotSubtitle = $"({date.Date:dd MMM} | 12:00 AM – 8:00 AM)";
                }
                else if (slot == "Day")
                {
                    shiftLabel = "Day (8am - 8pm)";
                    timeWindow = "8:00 AM – 8:00 PM";
                    slotSubtitle = $"({date.Date:dd MMM} | 8:00 AM – 8:00 PM)";
                }
                else if (slot == "Night")
                {
                    shiftLabel = "Night (8pm - 12am)";
                    timeWindow = "8:00 PM – 12:00 AM";
                    slotSubtitle = $"({date.Date:dd MMM} | 8:00 PM – 12:00 AM)";
                }
                else continue;

                var item = new TidItemDto
                {
                    DsmName = r.DsmPersonalDebtor?.DsmName ?? "DSM Loss",
                    PumpId = 0,
                    RomanIndex = "DSM Loss",
                    Amount = r.Amount,
                    Tid = string.IsNullOrWhiteSpace(r.CardTid) ? "—" : r.CardTid,
                    Batch = string.IsNullOrWhiteSpace(r.CardBatch) ? "—" : r.CardBatch,
                    Slot = slot,
                    ShiftLabel = shiftLabel,
                    SlotDate = slotDate,
                    TimeWindow = timeWindow,
                    SlotDisplaySubtitle = slotSubtitle,
                    CollectionTypeCode = r.PaymentMethod
                };

                bool isPhonePe = string.Equals(r.PaymentMethod, "PhonePe", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(r.PaymentMethod, "PhonePe UPI", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(r.PaymentMethod, "UPI Terminal", StringComparison.OrdinalIgnoreCase);

                bool isPineLabs = string.Equals(r.PaymentMethod, "PineLabs Card", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(r.PaymentMethod, "Credit Card", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(r.PaymentMethod, "Card", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(r.PaymentMethod, "PineLabs", StringComparison.OrdinalIgnoreCase);

                bool isPetro = string.Equals(r.PaymentMethod, "Petro Card", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(r.PaymentMethod, "PetroCard", StringComparison.OrdinalIgnoreCase);

                if (isPhonePe)
                {
                    sheet.DebtorPhonePeRepayments.Add(item);
                    switch (slot)
                    {
                        case "Morning": sheet.DebtorPhonePeRepaymentMorning += r.Amount; break;
                        case "Day":     sheet.DebtorPhonePeRepaymentDay     += r.Amount; break;
                        case "Night":   sheet.DebtorPhonePeRepaymentNight   += r.Amount; break;
                    }
                }
                else if (isPineLabs)
                {
                    sheet.DebtorCardRepayments.Add(item);
                    switch (slot)
                    {
                        case "Morning": sheet.DebtorCardRepaymentMorning += r.Amount; break;
                        case "Day":     sheet.DebtorCardRepaymentDay     += r.Amount; break;
                        case "Night":   sheet.DebtorCardRepaymentNight   += r.Amount; break;
                    }
                }
                else if (isPetro)
                {
                    sheet.DebtorPetroCardRepayments.Add(item);
                    switch (slot)
                    {
                        case "Morning": sheet.DebtorPetroCardRepaymentMorning += r.Amount; break;
                        case "Day":     sheet.DebtorPetroCardRepaymentDay     += r.Amount; break;
                        case "Night":   sheet.DebtorPetroCardRepaymentNight   += r.Amount; break;
                    }
                }
                else
                {
                    dynamicDebtorItems.Add(item);
                }
            }
        }

        // Build Dynamic CollectionGroups for all enabled collection types
        var activeTypes = _collectionTypeService != null
            ? await _collectionTypeService.GetActiveCollectionTypesAsync()
            : new List<CollectionTypeMaster>();

        if (activeTypes == null || activeTypes.Count == 0)
        {
            activeTypes = new List<CollectionTypeMaster>
            {
                new() { Code = "PINELAB_CARD", DisplayName = "PINELAB CARD", Category = "Card", HasTidBatch = true, IsActive = true, DisplayOrder = 1 },
                new() { Code = "PHONEPE", DisplayName = "PhonePe", Category = "Online", HasTidBatch = true, IsActive = true, DisplayOrder = 2 },
                new() { Code = "PETROCARD", DisplayName = "PetroCard", Category = "Card", HasTidBatch = true, IsActive = true, DisplayOrder = 3 }
            };
        }

        var tidEnabledTypes = activeTypes
            .Where(t => t.IsActive && t.HasTidBatch)
            .OrderBy(t => t.DisplayOrder)
            .ToList();

        foreach (var type in tidEnabledTypes)
        {
            var group = new TidCollectionGroup
            {
                CollectionTypeCode = type.Code,
                DisplayName = type.DisplayName,
                Category = type.Category
            };

            var itemsForGroup = new List<TidItemDto>();

            bool isCreditCard = string.Equals(type.Code, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(type.Code, "PINELAB_CARD", StringComparison.OrdinalIgnoreCase) ||
                                (type.DisplayName.Contains("Card", StringComparison.OrdinalIgnoreCase) && !type.DisplayName.Contains("Petro", StringComparison.OrdinalIgnoreCase) && !type.DisplayName.Contains("PhonePe", StringComparison.OrdinalIgnoreCase));

            bool isPhonePe = string.Equals(type.Code, "PHONEPE", StringComparison.OrdinalIgnoreCase) ||
                             type.DisplayName.Contains("PhonePe", StringComparison.OrdinalIgnoreCase);

            bool isPetro = string.Equals(type.Code, "PETROCARD", StringComparison.OrdinalIgnoreCase) ||
                           type.DisplayName.Contains("Petro", StringComparison.OrdinalIgnoreCase);

            if (isCreditCard)
            {
                itemsForGroup.AddRange(sheet.CardPayments);
                itemsForGroup.AddRange(sheet.DebtorCardRepayments);
            }
            else if (isPhonePe)
            {
                itemsForGroup.AddRange(sheet.PhonePePayments);
                itemsForGroup.AddRange(sheet.DebtorPhonePeRepayments);
            }
            else if (isPetro)
            {
                itemsForGroup.AddRange(sheet.PetroCardPayments);
                itemsForGroup.AddRange(sheet.DebtorPetroCardRepayments);
            }

            // Match dynamic DSM items
            var matchedDynDsm = dynamicDsmItems.Where(d =>
                string.Equals(d.CollectionTypeCode, type.Code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.CollectionTypeCode, type.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.CollectionTypeCode, type.Code.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
            itemsForGroup.AddRange(matchedDynDsm);

            // Match dynamic debtor repayments
            var matchedDynDebtor = dynamicDebtorItems.Where(d =>
                string.Equals(d.CollectionTypeCode, type.Code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.CollectionTypeCode, type.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.CollectionTypeCode, type.Code.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
            itemsForGroup.AddRange(matchedDynDebtor);

            group.Items = GroupAndMerge(itemsForGroup);
            sheet.CollectionGroups.Add(group);
            sheet.DynamicTotals[type.DisplayName] = group.TotalAmount;
        }

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
