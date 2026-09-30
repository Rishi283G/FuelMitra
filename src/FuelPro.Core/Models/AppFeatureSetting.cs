using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Controls the availability and behavior of individual features, pages, and integrations
/// across Manager (Admin), Owner, and Global roles.
/// </summary>
public class AppFeatureSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Unique key identifying the feature or page (e.g., "Admin_DsmApprovalQueue", "Owner_ProfitLoss", "Integration_DsmPwa").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>
    /// Target role: "Manager", "Owner", "Global", "Collection", "Station".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TargetRole { get; set; } = "Global";

    /// <summary>
    /// User-friendly name displayed in Developer configuration.
    /// </summary>
    [Required]
    [MaxLength(150)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Explanatory description of what the feature enables or disables.
    /// </summary>
    [MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Visual grouping category in Developer Tools (e.g. "Operations", "Financials", "Reports", "Integrations").
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = "General";

    /// <summary>
    /// Whether this feature/page is currently active and available for the client.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Display sort order within its category.
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Optional JSON configuration string for advanced settings.
    /// </summary>
    public string? ConfigurationJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Returns the single canonical source of truth for all default application feature definitions.
    /// Total: 34 features (16 Manager, 15 Owner, 3 Global).
    /// </summary>
    public static List<AppFeatureSetting> GetCanonicalDefaults(DateTime? timestamp = null)
    {
        var now = timestamp ?? DateTime.Now;
        return new List<AppFeatureSetting>
        {
            // ───────────────────────────────────────────────
            // MANAGER (ADMIN) FEATURES (16 items)
            // ───────────────────────────────────────────────
            new() { FeatureKey = "Admin_DsmEntry", TargetRole = "Manager", DisplayName = "DSM Entry", Category = "Operations", DisplayOrder = 1, IsEnabled = true, Description = "Physical nozzle meter readings, cash/digital remittances, and daily shift entries.", UpdatedAt = now },
            new() { FeatureKey = "Admin_OilDefDailyLog", TargetRole = "Manager", DisplayName = "Oil & DEF Daily Log", Category = "Operations", DisplayOrder = 2, IsEnabled = true, Description = "Daily logs and tracking for Lubricant Oils and DEF sales.", UpdatedAt = now },
            new() { FeatureKey = "Admin_FinalCalculation", TargetRole = "Manager", DisplayName = "Final Calculation", Category = "Operations", DisplayOrder = 3, IsEnabled = true, Description = "Shift total aggregation, collection balancing, and reconciliation summaries.", UpdatedAt = now },
            new() { FeatureKey = "Admin_DayTotal", TargetRole = "Manager", DisplayName = "Day Total", Category = "Operations", DisplayOrder = 4, IsEnabled = true, Description = "Full day sales, collection consolidation, and day book reporting.", UpdatedAt = now },
            new() { FeatureKey = "Admin_DebtorManagement", TargetRole = "Manager", DisplayName = "Debtor Management", Category = "Debtors", DisplayOrder = 5, IsEnabled = true, Description = "Credit customer ledgers, repayments, and outstanding balance tracking.", UpdatedAt = now },
            new() { FeatureKey = "Admin_DsmPersonalDebtor", TargetRole = "Manager", DisplayName = "DSM Loss / Personal Debtors", Category = "Debtors", DisplayOrder = 6, IsEnabled = true, Description = "Tracking personal shortage deductions, recovery advances, and salary loans.", UpdatedAt = now },
            new() { FeatureKey = "Admin_DsmApprovalQueue", TargetRole = "Manager", DisplayName = "DSM Approval Queue", Category = "DSM Management", DisplayOrder = 7, IsEnabled = true, Description = "Review and approve mobile PWA shift submissions submitted by operators.", UpdatedAt = now },
            new() { FeatureKey = "Admin_DsmManagement", TargetRole = "Manager", DisplayName = "DSM & Device Management", Category = "DSM Management", DisplayOrder = 8, IsEnabled = true, Description = "Operator roster, pump allocation, and device authorization controls.", UpdatedAt = now },
            new() { FeatureKey = "Admin_CardSettlement", TargetRole = "Manager", DisplayName = "TID Sheet / Card Settlement", Category = "Financials", DisplayOrder = 9, IsEnabled = true, Description = "POS terminal card settlement, batch audit, and TID reconciliation.", UpdatedAt = now },
            new() { FeatureKey = "Admin_AgsImport", TargetRole = "Manager", DisplayName = "AGS Import", Category = "Integrations", DisplayOrder = 10, IsEnabled = true, Description = "Automated pump automation import integration.", UpdatedAt = now },
            new() { FeatureKey = "Admin_PettyCash", TargetRole = "Manager", DisplayName = "Petty Cash", Category = "Financials", DisplayOrder = 11, IsEnabled = true, Description = "Petty cash ledger, day-to-day voucher payments, and cash float balancing.", UpdatedAt = now },
            new() { FeatureKey = "Admin_FuelTankerEntry", TargetRole = "Manager", DisplayName = "Fuel Tanker Entry", Category = "Stock", DisplayOrder = 12, IsEnabled = true, Description = "Fuel delivery invoice recording, tanker decantation, and underground dip validation.", UpdatedAt = now },
            new() { FeatureKey = "Admin_TankStockHistory", TargetRole = "Manager", DisplayName = "Tank Stock History", Category = "Stock", DisplayOrder = 13, IsEnabled = true, Description = "Historical tank dipping, book stock vs physical stock variance audit.", UpdatedAt = now },
            new() { FeatureKey = "Admin_Settings", TargetRole = "Manager", DisplayName = "Settings", Category = "General", DisplayOrder = 14, IsEnabled = true, Description = "General station preferences, fuel rates, printer settings, and local database management.", UpdatedAt = now },
            new() { FeatureKey = "Operations_CrossDsmQr", TargetRole = "Manager", DisplayName = "Cross-DSM QR Payments", Category = "Collections", DisplayOrder = 15, IsEnabled = true, Description = "Allow shift remittances to record QR payments accepted on behalf of other operators.", UpdatedAt = now },
            new() { FeatureKey = "Operations_PersonalLedger", TargetRole = "Manager", DisplayName = "Personal Ledger", Category = "Operations", DisplayOrder = 16, IsEnabled = true, Description = "Station personal drawing ledger and miscellaneous staff advances.", UpdatedAt = now },

            // ───────────────────────────────────────────────
            // OWNER FEATURES (15 items)
            // ───────────────────────────────────────────────
            new() { FeatureKey = "Owner_Dashboard", TargetRole = "Owner", DisplayName = "Dashboard", Category = "Overview", DisplayOrder = 1, IsEnabled = true, Description = "Executive business dashboard with sales trends, active pump KPIs, and live status.", UpdatedAt = now },
            new() { FeatureKey = "Owner_DailyPerformance", TargetRole = "Owner", DisplayName = "Daily Performance", Category = "Performance", DisplayOrder = 2, IsEnabled = true, Description = "Daily sales performance, fuel volume breakdown, and operator productivity.", UpdatedAt = now },
            new() { FeatureKey = "Owner_MonthlyPerformance", TargetRole = "Owner", DisplayName = "Monthly Performance", Category = "Performance", DisplayOrder = 3, IsEnabled = true, Description = "Month-over-month volume comparisons and profitability metrics.", UpdatedAt = now },
            new() { FeatureKey = "Owner_ProfitLoss", TargetRole = "Owner", DisplayName = "Profit & Loss", Category = "Financials", DisplayOrder = 4, IsEnabled = true, Description = "Operating margin analysis, gross margin, and net profitability reports.", UpdatedAt = now },
            new() { FeatureKey = "Owner_ExpenseAnalysis", TargetRole = "Owner", DisplayName = "Expense Analysis", Category = "Financials", DisplayOrder = 5, IsEnabled = true, Description = "Operational overhead tracking, category expense trends, and outlier audits.", UpdatedAt = now },
            new() { FeatureKey = "Owner_MismatchLedger", TargetRole = "Owner", DisplayName = "Mismatch Ledger", Category = "Audit", DisplayOrder = 6, IsEnabled = true, Description = "Comprehensive historical log of cash variances and remittance differences.", UpdatedAt = now },
            new() { FeatureKey = "Owner_CollectionSummary", TargetRole = "Owner", DisplayName = "Collection Summary", Category = "Financials", DisplayOrder = 7, IsEnabled = true, Description = "Summary of payment channels (Cash, Cards, UPI, QR, Credit).", UpdatedAt = now },
            new() { FeatureKey = "Owner_SalaryCalculation", TargetRole = "Owner", DisplayName = "DSM Salary & Payroll", Category = "Payroll", DisplayOrder = 8, IsEnabled = true, Description = "Monthly staff payroll generation, attendance tracking, and shortage adjustments.", UpdatedAt = now },
            new() { FeatureKey = "Owner_OilDefInventory", TargetRole = "Owner", DisplayName = "Oil & DEF Summary", Category = "Stock", DisplayOrder = 9, IsEnabled = true, Description = "Stock valuation, reorder alerts, and retail package sales analysis.", UpdatedAt = now },
            new() { FeatureKey = "Owner_CardSettlement", TargetRole = "Owner", DisplayName = "Card Settlement / TID", Category = "Financials", DisplayOrder = 10, IsEnabled = true, Description = "Bank terminal batch reconciliation and card settlement overview.", UpdatedAt = now },
            new() { FeatureKey = "Owner_DebtorManagement", TargetRole = "Owner", DisplayName = "Debtor Management", Category = "Debtors", DisplayOrder = 11, IsEnabled = true, Description = "Owner debtor overview, credit limits, and aging ledger.", UpdatedAt = now },
            new() { FeatureKey = "Owner_PumpExpenses", TargetRole = "Owner", DisplayName = "Pump Expenses", Category = "Financials", DisplayOrder = 12, IsEnabled = true, Description = "Station maintenance, electricity, municipal taxes, and equipment servicing.", UpdatedAt = now },
            new() { FeatureKey = "Owner_PettyCash", TargetRole = "Owner", DisplayName = "Petty Cash", Category = "Financials", DisplayOrder = 13, IsEnabled = true, Description = "Owner review of station petty cash disbursements.", UpdatedAt = now },
            new() { FeatureKey = "Owner_DsmPersonalDebtor", TargetRole = "Owner", DisplayName = "DSM Loss", Category = "Debtors", DisplayOrder = 14, IsEnabled = true, Description = "Staff loss ledger and pending deductions.", UpdatedAt = now },
            new() { FeatureKey = "Owner_Reports", TargetRole = "Owner", DisplayName = "Reports & Analytics", Category = "Reports", DisplayOrder = 15, IsEnabled = true, Description = "Comprehensive print and PDF reporting suite.", UpdatedAt = now },

            // ───────────────────────────────────────────────
            // GLOBAL & WORKFLOW FEATURES (3 items)
            // ───────────────────────────────────────────────
            new() { FeatureKey = "Integration_DsmPwa", TargetRole = "Global", DisplayName = "DSM PWA Mobile App Integration", Category = "Integrations", DisplayOrder = 1, IsEnabled = true, Description = "Enable mobile PWA nozzle entry and operator shift synchronization via cloud.", UpdatedAt = now },
            new() { FeatureKey = "Collection_UseMorningNight", TargetRole = "Global", DisplayName = "Split Collections into Morning / Night", Category = "Collections", DisplayOrder = 2, IsEnabled = false, Description = "Split collection entry fields into morning/day/night shifts vs combined daily amounts.", UpdatedAt = now },
            new() { FeatureKey = "UI_DebtorManagement_AsNewPage", TargetRole = "Global", DisplayName = "Debtor Management on Dedicated Page (vs Shift Total)", Category = "Navigation", DisplayOrder = 5, IsEnabled = true, Description = "When enabled, Debtor Management appears as a dedicated page below Oil & DEF Log. When disabled, it embeds inside Shift Total (Final Calculation).", UpdatedAt = now }
        };
    }
}
