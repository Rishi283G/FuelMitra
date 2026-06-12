using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

using System.Runtime.Versioning;

namespace Rashtra.Licensing
{
    [SupportedOSPlatform("windows")]
    public static class DeviceIdentifier
    {
        public static string GetDeviceId()
        {
            var compName = Environment.MachineName;
            var mobo = GetMoboSerial();
            var cpu = GetCpuId();
            var disk = GetDiskSerial();
            var machineGuid = GetMachineGuid();

            // Hash the combined string
            var hashStr = $"{compName}|{mobo}|{cpu}|{disk}|{machineGuid}";
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashStr));
            
            // Format as RT-XXXX-XXXX-XXXX (three blocks of 4 hex characters)
            var sb = new StringBuilder();
            sb.Append("RT-");
            sb.Append(BitConverter.ToString(bytes, 0, 2).Replace("-", ""));
            sb.Append("-");
            sb.Append(BitConverter.ToString(bytes, 2, 2).Replace("-", ""));
            sb.Append("-");
            sb.Append(BitConverter.ToString(bytes, 4, 2).Replace("-", ""));
            
            return sb.ToString().ToUpperInvariant();
        }

        public static HardwareComponents GetComponents()
        {
            return new HardwareComponents
            {
                ComputerName = Environment.MachineName,
                MoboSerial = GetMoboSerial(),
                CpuId = GetCpuId(),
                DiskSerial = GetDiskSerial(),
                MachineGuid = GetMachineGuid()
            };
        }

        private static string GetMoboSerial()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (var obj in searcher.Get())
                {
                    var val = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }
            return "UNKNOWN_MOBO";
        }

        private static string GetCpuId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    var val = obj["ProcessorId"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }
            return "UNKNOWN_CPU";
        }

        private static string GetDiskSerial()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMedia");
                foreach (var obj in searcher.Get())
                {
                    var val = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { }
            return "UNKNOWN_DISK";
        }

        private static string GetMachineGuid()
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var val = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }
            return "UNKNOWN_GUID";
        }
    }

    public class HardwareComponents
    {
        public string ComputerName { get; set; } = string.Empty;
        public string MoboSerial { get; set; } = string.Empty;
        public string CpuId { get; set; } = string.Empty;
        public string DiskSerial { get; set; } = string.Empty;
        public string MachineGuid { get; set; } = string.Empty;
    }
}
