using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Rashtra.Licensing
{
    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public LicenseData? License { get; set; }
    }

    public class LicenseManager
    {
        private const string SecretSalt = "RashtraTechnologiesLicensingSalt_2026";
        private const string AesSecretKey = "R4shtr4_T3chn0l0gi3s_K3y_3ncr1pt"; // 32 characters for AES-256
        private const string AesSecretIv = "R4shtr4_Iv_St4bl"; // 16 characters for AES-256 IV

        private readonly string _productCode;
        private readonly string _licenseFilePath;

        public string ProductCode => _productCode;

        public LicenseManager(string productCode, string folderName)
        {
            _productCode = productCode;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RashtraTechnologies",
                folderName);
            Directory.CreateDirectory(directory);
            _licenseFilePath = Path.Combine(directory, "license.lic");
        }

        public string GetLicenseFilePath() => _licenseFilePath;

        /// <summary>
        /// Generates the unique license key from Product Code, Customer Name, and Device ID.
        /// Format: RFP-XXXX-XXXX-XXXX or FPL-XXXX-XXXX-XXXX
        /// </summary>
        public static string GenerateLicenseKey(string productCode, string customerName, string deviceId)
        {
            var rawInput = $"{productCode.Trim().ToUpper()}|{customerName.Trim().ToLower()}|{deviceId.Trim().ToUpper()}|{SecretSalt}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawInput));

            // Generate three 4-character alphanumeric blocks from the hash
            var p1 = GetKeyBlock(hashBytes, 0);
            var p2 = GetKeyBlock(hashBytes, 4);
            var p3 = GetKeyBlock(hashBytes, 8);

            return $"{productCode.Trim().ToUpper()}-{p1}-{p2}-{p3}";
        }

        private static string GetKeyBlock(byte[] bytes, int startIndex)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Alphanumeric excluding confusing chars (I, O, 0, 1)
            var sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++)
            {
                var val = bytes[startIndex + i];
                sb.Append(chars[val % chars.Length]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Validates key matching.
        /// </summary>
        public bool VerifyKey(string customerName, string deviceId, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var parts = key.Trim().Split('-');
            if (parts.Length < 4) return false;
            var verifyProductCode = _productCode;

            if (parts.Length > 4)
            {
                var secondPart = parts[1].ToUpperInvariant();
                if (secondPart == "T" || secondPart == "A" || secondPart == "D")
                {
                    if (parts.Length >= 6 && parts[2].Length == 8 && int.TryParse(parts[2], out _))
                    {
                        verifyProductCode = _productCode + "-" + secondPart + "-" + parts[2];
                    }
                    else
                    {
                        verifyProductCode = _productCode + "-" + secondPart;
                    }
                }
            }
            else
            {
                if (key.Contains("-T-") || key.StartsWith(_productCode + "-T-", StringComparison.OrdinalIgnoreCase))
                {
                    verifyProductCode = _productCode + "-T";
                }
                else if (key.Contains("-A-") || key.StartsWith(_productCode + "-A-", StringComparison.OrdinalIgnoreCase))
                {
                    verifyProductCode = _productCode + "-A";
                }
                else if (key.Contains("-D-") || key.StartsWith(_productCode + "-D-", StringComparison.OrdinalIgnoreCase))
                {
                    verifyProductCode = _productCode + "-D";
                }
            }

            var expected = GenerateLicenseKey(verifyProductCode, customerName, deviceId);
            return string.Equals(expected, key.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Saves an encrypted license locally after successful activation.
        /// </summary>
        public bool ActivateLicense(string customerName, string businessName, string mobileNumber, string key)
        {
            var deviceId = DeviceIdentifier.GetDeviceId();
            if (!VerifyKey(customerName, deviceId, key))
            {
                return false;
            }

            var hw = DeviceIdentifier.GetComponents();
            var license = new LicenseData
            {
                ProductName = _productCode,
                CustomerName = customerName.Trim(),
                BusinessName = businessName.Trim(),
                MobileNumber = mobileNumber.Trim(),
                DeviceId = deviceId,
                LicenseKey = key.Trim().ToUpper(),
                LicenseType = "Lifetime", // Default
                ActivationDate = DateTime.Now,
                SavedComputerName = hw.ComputerName,
                SavedMoboSerial = hw.MoboSerial,
                SavedCpuId = hw.CpuId,
                SavedDiskSerial = hw.DiskSerial,
                SavedMachineGuid = hw.MachineGuid
            };

            // Parse suffix if present (e.g. FPL-T-XXXX-XXXX-XXXX)
            var parts = key.Trim().Split('-');
            string suffix = string.Empty;
            DateTime? expiryDate = null;

            if (parts.Length > 4)
            {
                var secondPart = parts[1].ToUpperInvariant();
                if (secondPart == "T" || secondPart == "A" || secondPart == "D")
                {
                    suffix = secondPart;
                    if (parts.Length >= 6 && parts[2].Length == 8 && int.TryParse(parts[2], out _))
                    {
                        if (DateTime.TryParseExact(parts[2], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                        {
                            expiryDate = parsedDate;
                        }
                    }
                }
                else
                {
                    // Fallback to old behavior for FPL-XXXX-XXXX-XXXX style or standard 5-part keys
                    suffix = parts[parts.Length - 4].ToUpperInvariant();
                }
            }

            if (suffix == "T")
            {
                license.LicenseType = "Trial";
                license.ExpiryDate = expiryDate ?? DateTime.Now.AddDays(15);
            }
            else if (suffix == "A")
            {
                license.LicenseType = "Annual";
                license.ExpiryDate = expiryDate ?? DateTime.Now.AddYears(1);
            }
            else if (suffix == "D")
            {
                license.LicenseType = "Demo";
                license.ExpiryDate = expiryDate ?? DateTime.Now.AddDays(7);
            }

            try
            {
                var json = JsonConvert.SerializeObject(license);
                var encrypted = Encrypt(json);
                File.WriteAllText(_licenseFilePath, encrypted);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Performs startup license validation: checks existence, integrity, hardware signature, and expiry.
        /// </summary>
        public LicenseValidationResult ValidateLicense()
        {
            var result = new LicenseValidationResult();

            if (!File.Exists(_licenseFilePath))
            {
                result.IsValid = false;
                result.ErrorMessage = "License file not found. Activation Required.";
                return result;
            }

            try
            {
                var encrypted = File.ReadAllText(_licenseFilePath);
                var decrypted = Decrypt(encrypted);
                var license = JsonConvert.DeserializeObject<LicenseData>(decrypted);
                if (license == null)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "License Invalid. Contact Rashtra Technologies.";
                    return result;
                }

                result.License = license;

                // 1. Verify Key Integrity
                if (!VerifyKey(license.CustomerName, license.DeviceId, license.LicenseKey))
                {
                    result.IsValid = false;
                    result.ErrorMessage = "License Invalid (Signature mismatch). Contact Rashtra Technologies.";
                    return result;
                }

                // 2. Verify Hardware Matching with Tolerance (at least 3 of 5 components must match)
                var currentHw = DeviceIdentifier.GetComponents();
                int matches = 0;
                if (string.Equals(currentHw.ComputerName, license.SavedComputerName, StringComparison.OrdinalIgnoreCase)) matches++;
                if (string.Equals(currentHw.MoboSerial, license.SavedMoboSerial, StringComparison.OrdinalIgnoreCase)) matches++;
                if (string.Equals(currentHw.CpuId, license.SavedCpuId, StringComparison.OrdinalIgnoreCase)) matches++;
                if (string.Equals(currentHw.DiskSerial, license.SavedDiskSerial, StringComparison.OrdinalIgnoreCase)) matches++;
                if (string.Equals(currentHw.MachineGuid, license.SavedMachineGuid, StringComparison.OrdinalIgnoreCase)) matches++;

                if (matches < 3)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "License Invalid (Hardware mismatch). Contact Rashtra Technologies.";
                    return result;
                }

                // 3. Expiry Checks
                if (license.ExpiryDate.HasValue && DateTime.Now > license.ExpiryDate.Value)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"License Expired on {license.ExpiryDate.Value:dd-MMM-yyyy}. Please renew.";
                    return result;
                }

                result.IsValid = true;
                return result;
            }
            catch
            {
                result.IsValid = false;
                result.ErrorMessage = "License Invalid (Decryption failed). Contact Rashtra Technologies.";
                return result;
            }
        }

        #region Encryption Helpers
        private static string Encrypt(string plainText)
        {
            var key = Encoding.UTF8.GetBytes(AesSecretKey);
            var iv = Encoding.UTF8.GetBytes(AesSecretIv);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(plainText);
                }
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        private static string Decrypt(string cipherText)
        {
            var key = Encoding.UTF8.GetBytes(AesSecretKey);
            var iv = Encoding.UTF8.GetBytes(AesSecretIv);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
            {
                using (var sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }
        #endregion
    }
}
