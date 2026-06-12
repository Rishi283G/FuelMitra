using Xunit;
using Rashtra.Licensing;
using System;
using System.IO;

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
    }
}
