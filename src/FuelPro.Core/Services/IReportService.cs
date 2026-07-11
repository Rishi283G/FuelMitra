using System;
using System.Collections.Generic;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

public interface IReportService
{
    ShiftReportDto CalculateShiftReport(
        DateTime date,
        string shiftType,
        List<DsmEntry> entries,
        List<Expense> shiftExpenses,
        List<ShiftOtherCash> otherCashList,
        List<CreditorRepayment> repayments,
        double hsdRate, double msIRate, double msIIRate, double cngRate,
        BusinessDayTidSheet todayTid, BusinessDayTidSheet tomorrowTid,
        string stationName);

    DayReportDto CalculateDayReport(
        DateTime startDate,
        DateTime endDate,
        List<DsmEntry> entries,
        List<Expense> allExpenses,
        List<CreditorRepayment> repayments,
        double hsdRate, double msIRate, double msIIRate, double cngRate,
        string stationName);
}
