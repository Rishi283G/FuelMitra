namespace FuelPro.Core.DTOs;

public class DsmEntryDto
{
    public int DSMEntryId { get; set; }
    public List<NozzleReadingDto> NozzleReadings { get; set; } = new();
    public PaymentCollectionDto PaymentCollection { get; set; } = new();
    public List<DebitEntryDto> DebitEntries { get; set; } = new();
    public List<ExpenseDto> Expenses { get; set; } = new();
    public List<TestingEntryDto> TestingEntries { get; set; } = new();
    public List<CashDenominationDto> CashDenominations { get; set; } = new();
}

public class NozzleReadingDto
{
    public decimal Amount { get; set; }
}

public class PaymentCollectionDto
{
    public decimal PhonePe { get; set; }
    public decimal CreditCard { get; set; }
    public decimal CashDeposit { get; set; }
    public decimal PhysicalCash { get; set; }
}

public class DebitEntryDto
{
    public decimal Amount { get; set; }
    public string? ChequeNo { get; set; }
}

public class ExpenseDto
{
    public decimal Amount { get; set; }
}

public class TestingEntryDto
{
    public string FuelType { get; set; } = string.Empty;
    public decimal Litres { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public class CashDenominationDto
{
    public decimal TotalAmount { get; set; }
}
