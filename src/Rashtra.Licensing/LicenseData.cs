using System;

namespace Rashtra.Licensing
{
    public class LicenseData
    {
        public string ProductName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string BusinessName { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string LicenseKey { get; set; } = string.Empty;
        public string LicenseType { get; set; } = "Lifetime"; // Lifetime, Annual, Trial, Demo
        public DateTime ActivationDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        
        // Saved hardware components to support matching tolerance if a component changes
        public string SavedComputerName { get; set; } = string.Empty;
        public string SavedMoboSerial { get; set; } = string.Empty;
        public string SavedCpuId { get; set; } = string.Empty;
        public string SavedDiskSerial { get; set; } = string.Empty;
        public string SavedMachineGuid { get; set; } = string.Empty;
    }
}
