using Xunit;
using Rashtra.Licensing;
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Microsoft.Data.Sqlite;

namespace FuelPro.Tests
{
    public class LicensingTests
    {
        private const string TestProductCode = "FPL";
        private const string TestFolderName = "FuelProLite_Tests";
        private const string TestCustomerName = "Test Petrol Pump";
        private const string TestBusinessName = "Test Station";
        private const string TestMobileNumber = "9876543210";

        [Fact]
        public void DeviceIdentifier_GetDeviceId_Returns_RT_Prefix()
        {
            var deviceId = DeviceIdentifier.GetDeviceId();
            Assert.NotNull(deviceId);
            Assert.StartsWith("RT-", deviceId);
        }

        [Fact]
        public void LicenseManager_GenerateLicenseKey_ReturnsFormattedKey()
        {
            var deviceId = DeviceIdentifier.GetDeviceId();
            var key = LicenseManager.GenerateLicenseKey(TestProductCode, TestCustomerName, deviceId);
            
            Assert.NotNull(key);
            Assert.StartsWith($"{TestProductCode}-", key);
            
            var parts = key.Split('-');
            Assert.Equal(4, parts.Length); // FPL-XXXX-XXXX-XXXX
        }

        [Fact]
        public void LicenseManager_VerifyKey_Succeeds_For_Valid_Inputs()
        {
            var licenseManager = new LicenseManager(TestProductCode, TestFolderName);
            var deviceId = DeviceIdentifier.GetDeviceId();
            var key = LicenseManager.GenerateLicenseKey(TestProductCode, TestCustomerName, deviceId);

            var result = licenseManager.VerifyKey(TestCustomerName, deviceId, key);
            Assert.True(result);
        }

        [Fact]
        public void LicenseManager_VerifyKey_Succeeds_For_PSC_Lifetime_Key_With_First_Char_T()
        {
            var licenseManager = new LicenseManager("PSC", TestFolderName);
            var customerName = "rushikesh jadhav";
            var deviceId = "RT-DD9D-6C54-B8CE";
            var key = "PSC-T4VC-SRPJ-4RGD";

            var result = licenseManager.VerifyKey(customerName, deviceId, key);
            Assert.True(result);
        }

        [Fact]
        public void LicenseManager_VerifyKey_Fails_For_Mismatched_Customer()
        {
            var licenseManager = new LicenseManager(TestProductCode, TestFolderName);
            var deviceId = DeviceIdentifier.GetDeviceId();
            var key = LicenseManager.GenerateLicenseKey(TestProductCode, TestCustomerName, deviceId);

            var result = licenseManager.VerifyKey("Different Customer", deviceId, key);
            Assert.False(result);
        }

        [Fact]
        public void LicenseManager_VerifyKey_Supports_Trial_And_Annual_Keys()
        {
            var licenseManager = new LicenseManager(TestProductCode, TestFolderName);
            var deviceId = DeviceIdentifier.GetDeviceId();

            // Trial Key
            var trialProductCode = TestProductCode + "-T";
            var trialKey = LicenseManager.GenerateLicenseKey(trialProductCode, TestCustomerName, deviceId);
            Assert.Contains("-T-", trialKey);
            var verifyTrialResult = licenseManager.VerifyKey(TestCustomerName, deviceId, trialKey);
            Assert.True(verifyTrialResult);

            // Annual Key
            var annualProductCode = TestProductCode + "-A";
            var annualKey = LicenseManager.GenerateLicenseKey(annualProductCode, TestCustomerName, deviceId);
            Assert.Contains("-A-", annualKey);
            var verifyAnnualResult = licenseManager.VerifyKey(TestCustomerName, deviceId, annualKey);
            Assert.True(verifyAnnualResult);
        }

        [Fact]
        public void LicenseManager_Activate_And_ValidateLicense_Flow()
        {
            var testProduct = "FPL_TEST_SUITE";
            var manager = new LicenseManager(testProduct, testProduct);
            
            try
            {
                var deviceId = DeviceIdentifier.GetDeviceId();
                var key = LicenseManager.GenerateLicenseKey(testProduct, TestCustomerName, deviceId);

                // Try activation
                var activated = manager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                var verifyResult = manager.VerifyKey(TestCustomerName, deviceId, key);
                Assert.True(activated, $"Key={key}\nVerifyResult={verifyResult}\nDeviceId={deviceId}\nProduct={testProduct}");

                // Try validation
                var validation = manager.ValidateLicense();
                Assert.True(validation.IsValid);
                Assert.NotNull(validation.License);
                Assert.Equal(TestCustomerName, validation.License.CustomerName);
                Assert.Equal(TestBusinessName, validation.License.BusinessName);
                Assert.Equal("Lifetime", validation.License.LicenseType);
            }
            finally
            {
                // Clean up the generated test license file
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void LicenseManager_VerifyKey_Supports_Demo_Keys()
        {
            var licenseManager = new LicenseManager(TestProductCode, TestFolderName);
            var deviceId = DeviceIdentifier.GetDeviceId();

            var demoProductCode = TestProductCode + "-D";
            var demoKey = LicenseManager.GenerateLicenseKey(demoProductCode, TestCustomerName, deviceId);
            Assert.Contains("-D-", demoKey);
            var verifyDemoResult = licenseManager.VerifyKey(TestCustomerName, deviceId, demoKey);
            Assert.True(verifyDemoResult);
        }

        [Fact]
        public void LicenseManager_VerifyKey_Supports_Custom_Expiry_Dates_Annual()
        {
            var licenseManager = new LicenseManager(TestProductCode, TestFolderName);
            var deviceId = DeviceIdentifier.GetDeviceId();

            var customExpiryStr = "20271231";
            var annualProductCode = TestProductCode + "-A-" + customExpiryStr;
            var key = LicenseManager.GenerateLicenseKey(annualProductCode, TestCustomerName, deviceId);
            
            Assert.Contains("-A-20271231-", key);
            var verifyResult = licenseManager.VerifyKey(TestCustomerName, deviceId, key);
            Assert.True(verifyResult);

            try
            {
                var activated = licenseManager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.True(activated);

                var validation = licenseManager.ValidateLicense();
                Assert.True(validation.IsValid);
                Assert.NotNull(validation.License);
                Assert.Equal("Annual", validation.License.LicenseType);
                Assert.NotNull(validation.License.ExpiryDate);
                Assert.Equal(new DateTime(2027, 12, 31), validation.License.ExpiryDate.Value.Date);
            }
            finally
            {
                var path = licenseManager.GetLicenseFilePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void LicenseManager_VerifyKey_Supports_Custom_Expiry_Dates_Demo()
        {
            var licenseManager = new LicenseManager(TestProductCode, TestFolderName);
            var deviceId = DeviceIdentifier.GetDeviceId();

            var customExpiryStr = "20261130";
            var demoProductCode = TestProductCode + "-D-" + customExpiryStr;
            var key = LicenseManager.GenerateLicenseKey(demoProductCode, TestCustomerName, deviceId);
            
            Assert.Contains("-D-20261130-", key);
            var verifyResult = licenseManager.VerifyKey(TestCustomerName, deviceId, key);
            Assert.True(verifyResult);

            try
            {
                var activated = licenseManager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.True(activated);

                var validation = licenseManager.ValidateLicense();
                Assert.True(validation.IsValid);
                Assert.NotNull(validation.License);
                Assert.Equal("Demo", validation.License.LicenseType);
                Assert.NotNull(validation.License.ExpiryDate);
                Assert.Equal(new DateTime(2026, 11, 30), validation.License.ExpiryDate.Value.Date);
            }
            finally
            {
                var path = licenseManager.GetLicenseFilePath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void LicenseManager_FiveDayDemo_ExactBoundaryTesting()
        {
            var testProduct = "PSC_DEMO_BOUNDARY";
            var simulatedTime = new DateTime(2026, 10, 13, 10, 0, 0); // Day 1
            var manager = new LicenseManager(testProduct, testProduct, () => simulatedTime);

            try
            {
                var deviceId = DeviceIdentifier.GetDeviceId();
                // 5-day demo issued on Day 1 (Oct 13), valid through Day 5 (Oct 17 end-of-day)
                var expiryDate = new DateTime(2026, 10, 17);
                var demoProductCode = $"{testProduct}-D-{expiryDate:yyyyMMdd}";
                var key = LicenseManager.GenerateLicenseKey(demoProductCode, TestCustomerName, deviceId);

                // Activation on Day 1
                var activated = manager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.True(activated);

                // Day 1: Valid
                simulatedTime = new DateTime(2026, 10, 13, 10, 0, 0);
                var resDay1 = manager.ValidateLicense();
                Assert.True(resDay1.IsValid);
                Assert.Equal("Demo", resDay1.License!.LicenseType);
                Assert.Equal(new DateTime(2026, 10, 17), resDay1.License.ExpiryDate!.Value.Date);

                // Day 3: Valid
                simulatedTime = new DateTime(2026, 10, 15, 14, 30, 0);
                var resDay3 = manager.ValidateLicense();
                Assert.True(resDay3.IsValid);

                // Day 5 evening (23:59:59): Still Valid (within defined end-of-day boundary)
                simulatedTime = new DateTime(2026, 10, 17, 23, 59, 59);
                var resDay5 = manager.ValidateLicense();
                Assert.True(resDay5.IsValid);

                // Day 6 00:00:00 (1 second past boundary): EXPIRED
                simulatedTime = new DateTime(2026, 10, 18, 0, 0, 0);
                var resDay6 = manager.ValidateLicense();
                Assert.False(resDay6.IsValid);
                Assert.Contains("License Expired", resDay6.ErrorMessage);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_LifetimeLicense_ValidatesSuccessfully_WithoutExpiry()
        {
            var testProduct = "PSC_LIFETIME_TEST";
            var manager = new LicenseManager(testProduct, testProduct);

            try
            {
                var deviceId = DeviceIdentifier.GetDeviceId();
                var key = LicenseManager.GenerateLicenseKey(testProduct, TestCustomerName, deviceId);

                var activated = manager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.True(activated);

                var validation = manager.ValidateLicense();
                Assert.True(validation.IsValid);
                Assert.NotNull(validation.License);
                Assert.Equal("Lifetime", validation.License.LicenseType);
                Assert.Null(validation.License.ExpiryDate);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_WrongMachineId_FailsActivation()
        {
            var testProduct = "PSC_WRONG_MACHINE";
            var manager = new LicenseManager(testProduct, testProduct,
                deviceIdProvider: () => "RT-ACTUAL-MACHINE-ID01");

            try
            {
                // Generate key for a different machine ID
                var otherMachineId = "RT-OTHER-MACHINE-ID99";
                var key = LicenseManager.GenerateLicenseKey(testProduct, TestCustomerName, otherMachineId);

                var activated = manager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.False(activated);
                Assert.False(File.Exists(manager.GetLicenseFilePath()));
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_MissingLicenseFile_ReturnsInvalid_PromptsActivation()
        {
            var testProduct = "PSC_MISSING_LIC";
            var manager = new LicenseManager(testProduct, testProduct);

            // Ensure file doesn't exist
            var path = manager.GetLicenseFilePath();
            if (File.Exists(path)) File.Delete(path);

            var validation = manager.ValidateLicense();
            Assert.False(validation.IsValid);
            Assert.Equal("License file not found. Activation Required.", validation.ErrorMessage);
        }

        [Fact]
        public void LicenseManager_TamperedPayload_ReturnsInvalid_Gracefully()
        {
            var testProduct = "PSC_TAMPERED";
            var manager = new LicenseManager(testProduct, testProduct);

            try
            {
                // Write corrupted ciphertext
                File.WriteAllText(manager.GetLicenseFilePath(), "CORRUPTED_NOT_VALID_CIPHERTEXT_BASE64!!!");

                var validation = manager.ValidateLicense();
                Assert.False(validation.IsValid);
                Assert.Contains("Decryption failed", validation.ErrorMessage);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_MalformedJson_ReturnsInvalid_Gracefully()
        {
            var testProduct = "PSC_MALFORMED";
            var manager = new LicenseManager(testProduct, testProduct);

            try
            {
                // Encrypt random JSON that doesn't match LicenseData
                var key = Encoding.UTF8.GetBytes("R4shtr4_T3chn0l0gi3s_K3y_3ncr1pt");
                var iv = Encoding.UTF8.GetBytes("R4shtr4_Iv_St4bl");
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write("null");
                }
                File.WriteAllText(manager.GetLicenseFilePath(), Convert.ToBase64String(ms.ToArray()));

                var validation = manager.ValidateLicense();
                Assert.False(validation.IsValid);
                Assert.Contains("Contact Rashtra Technologies", validation.ErrorMessage);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_HardwareTolerance_PassesAtThreeMatches_FailsBelow()
        {
            var testProduct = "PSC_HW_TOLERANCE";
            var currentHw = new HardwareComponents
            {
                ComputerName = "PUMP-PC-01",
                MoboSerial = "MOBO-1111",
                CpuId = "CPU-2222",
                DiskSerial = "DISK-3333",
                MachineGuid = "GUID-4444"
            };

            var manager = new LicenseManager(testProduct, testProduct,
                hardwareProvider: () => currentHw,
                deviceIdProvider: () => "RT-TEST-DEVICE-01");

            try
            {
                var key = LicenseManager.GenerateLicenseKey(testProduct, TestCustomerName, "RT-TEST-DEVICE-01");
                var activated = manager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.True(activated);

                // Scenario 1: Exact match (5/5) -> Valid
                var val1 = manager.ValidateLicense();
                Assert.True(val1.IsValid);

                // Scenario 2: Mobo and Cpu changed, but ComputerName, DiskSerial, MachineGuid match (3/5) -> Still Valid!
                currentHw.MoboSerial = "MOBO-NEW-REPLACED";
                currentHw.CpuId = "CPU-UPGRADED";
                var val2 = manager.ValidateLicense();
                Assert.True(val2.IsValid);

                // Scenario 3: Also Disk changed, leaving only 2 matches (ComputerName, MachineGuid) -> Invalid!
                currentHw.DiskSerial = "DISK-NEW-SSD";
                var val3 = manager.ValidateLicense();
                Assert.False(val3.IsValid);
                Assert.Contains("Hardware mismatch", val3.ErrorMessage);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_ClockRollback_DetectsRollback_And_AllowsNormalAdvancement()
        {
            var testProduct = "PSC_CLOCK_ROLLBACK";
            var simulatedTime = new DateTime(2026, 10, 13, 10, 0, 0);
            var manager = new LicenseManager(testProduct, testProduct, () => simulatedTime);

            try
            {
                var deviceId = DeviceIdentifier.GetDeviceId();
                var key = LicenseManager.GenerateLicenseKey(testProduct, TestCustomerName, deviceId);
                var activated = manager.ActivateLicense(TestCustomerName, TestBusinessName, TestMobileNumber, key);
                Assert.True(activated);

                // Normal progression (Day 2): succeeds
                simulatedTime = new DateTime(2026, 10, 14, 11, 0, 0);
                var valNormal = manager.ValidateLicense();
                Assert.True(valNormal.IsValid);

                // Clock rolled back by 4 days to Oct 10 (> 24 hours prior to LastRunDate): fails
                simulatedTime = new DateTime(2026, 10, 10, 10, 0, 0);
                var valRollback = manager.ValidateLicense();
                Assert.False(valRollback.IsValid);
                Assert.Contains("clock rollback", valRollback.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_LegacyLicenseFile_WithoutLastRunDate_DeserializesAndValidatesSafely()
        {
            var testProduct = "PSC_LEGACY_FORMAT";
            var simulatedTime = new DateTime(2026, 10, 13, 10, 0, 0);
            var manager = new LicenseManager(testProduct, testProduct, () => simulatedTime);

            try
            {
                var deviceId = DeviceIdentifier.GetDeviceId();
                var hw = DeviceIdentifier.GetComponents();
                var key = LicenseManager.GenerateLicenseKey(testProduct, TestCustomerName, deviceId);

                // Create legacy payload with NO LastRunDate
                var legacyLicense = new LicenseData
                {
                    ProductName = testProduct,
                    CustomerName = TestCustomerName,
                    BusinessName = TestBusinessName,
                    MobileNumber = TestMobileNumber,
                    DeviceId = deviceId,
                    LicenseKey = key,
                    LicenseType = "Lifetime",
                    ActivationDate = simulatedTime,
                    ExpiryDate = null,
                    LastRunDate = null, // Legacy license format
                    SavedComputerName = hw.ComputerName,
                    SavedMoboSerial = hw.MoboSerial,
                    SavedCpuId = hw.CpuId,
                    SavedDiskSerial = hw.DiskSerial,
                    SavedMachineGuid = hw.MachineGuid
                };

                // Encrypt directly without invoking ActivateLicense
                var aesKey = Encoding.UTF8.GetBytes("R4shtr4_T3chn0l0gi3s_K3y_3ncr1pt");
                var aesIv = Encoding.UTF8.GetBytes("R4shtr4_Iv_St4bl");
                using var aes = Aes.Create();
                aes.Key = aesKey;
                aes.IV = aesIv;
                using var enc = aes.CreateEncryptor(aes.Key, aes.IV);
                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs))
                {
                    sw.Write(JsonConvert.SerializeObject(legacyLicense));
                }
                File.WriteAllText(manager.GetLicenseFilePath(), Convert.ToBase64String(ms.ToArray()));

                // Validation must succeed without any false rollback lockout
                var validation = manager.ValidateLicense();
                Assert.True(validation.IsValid);
                Assert.Equal("Lifetime", validation.License!.LicenseType);
            }
            finally
            {
                var path = manager.GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void LicenseManager_BypassActivation_IsDisabledByDefault()
        {
            Assert.False(LicenseManager.BypassActivation);

            // An unlicensed LicenseManager instance MUST return IsValid == false
            var manager = new LicenseManager("PSC_UNLICENSED_CHECK", "PSC_UNLICENSED_CHECK");
            var path = manager.GetLicenseFilePath();
            if (File.Exists(path)) File.Delete(path);

            var result = manager.ValidateLicense();
            Assert.False(result.IsValid);
            Assert.Equal("License file not found. Activation Required.", result.ErrorMessage);
        }

        [Fact]
        public void LicenseManager_LicensingFailure_DoesNotModifyDatabaseOrUserData()
        {
            var tempDb = Path.Combine(Path.GetTempPath(), $"license_safety_test_{Guid.NewGuid():N}.db");
            try
            {
                // Create dummy test SQLite database with user data
                using (var conn = new SqliteConnection($"Data Source={tempDb}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "CREATE TABLE TestUsers (Id INTEGER PRIMARY KEY, Username TEXT); INSERT INTO TestUsers VALUES (1, 'StationOwner');";
                    cmd.ExecuteNonQuery();
                }

                var initialSize = new FileInfo(tempDb).Length;

                // Attempt validation on a failed license
                var manager = new LicenseManager("PSC_SAFETY", "PSC_SAFETY");
                var validation = manager.ValidateLicense();
                Assert.False(validation.IsValid);

                // Attempt failed activation
                var activated = manager.ActivateLicense("FakeUser", "FakeStation", "0000000000", "INVALID-KEY-1234");
                Assert.False(activated);

                // Verify database still exists and data is 100% intact
                Assert.True(File.Exists(tempDb));
                using (var conn = new SqliteConnection($"Data Source={tempDb}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM TestUsers;";
                    var count = Convert.ToInt32(cmd.ExecuteScalar());
                    Assert.Equal(1, count);
                }
            }
            finally
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            }
        }
    }
}
