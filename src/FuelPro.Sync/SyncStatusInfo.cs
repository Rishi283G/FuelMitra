using System;

namespace FuelPro.Sync;

public class SyncStatusInfo
{
    public DateTime LastSyncTime { get; set; }
    public int PendingRecords { get; set; }
    public bool IsConnected { get; set; }
    public string StatusMessage { get; set; } = "Not Connected";
}
