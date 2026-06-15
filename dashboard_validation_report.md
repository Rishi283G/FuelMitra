# Owner Dashboard & Profit & Loss Validation Report

This report compares calculations across the **Owner Dashboard**, **Collection Summary**, **Financial Summary**, **Monthly Performance**, **DSM Performance**, and **Profit & Loss** screens for date **2040-10-22**.

---

## 1. Metric Comparison Summary

| Metric / Category | Owner Dashboard | Collection Summary | Financial Summary | Profit & Loss | Difference | Status / Match |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Gross Sales** | 903.50 | N/A | N/A | 903.50 | 0.00 | MATCH |
| **Total Collection** | 1451.75 | 900.00 | N/A | 1451.75 | 551.75 | MISMATCH |
| **Total Expenses** | 100.00 | N/A | 100.00 | 100.00 | 0.00 | MATCH |
| **Cash (Deposit + In Hand)** | 500.00 | 500.00 | 500.00 | N/A | 0.00 | MATCH |
| **PhonePe (Direct + Card)** | 300.00 | 300.00 | N/A | N/A | 0.00 | MATCH |
| **Credit Card** | 100.00 | 100.00 | N/A | N/A | 0.00 | MATCH |
| **Petro Card** | 0.00 | 0.00 | N/A | N/A | 0.00 | MATCH |
| **Debit (Creditors)** | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | MATCH |
| **Mismatch** | 548.25 | N/A | N/A | 548.25 | 0.00 | MATCH |

---

## 2. Performance Summaries Validation

### Monthly Performance (Day Row for 2040-10-22)
- **Monthly Perf Day Sales**: `903.50` vs **Dashboard Sales**: `903.50` (MATCH)
- **Monthly Perf Day Collection**: `1451.75` vs **Dashboard Collection**: `1451.75` (MATCH)
- **Monthly Perf Day Mismatch**: `548.25` vs **Dashboard Mismatch**: `548.25` (MATCH)

### DSM Performance (Row for `E2E Test DSM`)
- **DSM Total Sales**: `903.50`
- **DSM Total Collection**: `1451.75`
- **DSM Mismatch**: `548.25`

---

## 3. Detailed Mismatch Investigation

### Digital / PhonePe Card Tracking Mismatch
- **Owner Dashboard PhonePe**: `300`
- **Collection Summary PhonePe + PhonePeCard**: `200 + 100 = 300`
- **Discrepancy**: `0`. The Owner Dashboard currently ignores PhonePe Card transactions in the payment breakdown.

### Total Collection Calculation Difference
- **Owner Dashboard Collection**: `1451.75` (Includes cash, digital, debit, testing, and expenses)
- **Collection Summary Grand Total**: `900` (Includes cash, digital, and debit, but excludes testing and expenses)
- **Discrepancy**: `551.75`.

---

## 4. Profit & Loss Verification
- **P&L Net Position (Sales - Expenses)**: `803.50`
- **P&L Mismatch**: `548.25`
- **Status**: VERIFIED

