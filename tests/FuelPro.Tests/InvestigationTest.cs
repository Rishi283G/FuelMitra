using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Services;
using FuelPro.Sync;

namespace FuelPro.Tests;

public class InvestigationTest
{
    private readonly ITestOutputHelper _output;

    public InvestigationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Diagnostic forensic exploration only")]
    public async Task InspectMitaliDatabase()
    {
        var dbPath = @"D:\Projects\Pump_Automation\Clients\Mitali_Service_Station\forensic_investigation\DISPOSABLE_WORKING_CLONE\fuelPro.db";
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await conn.OpenAsync();

        // 1. Check CollectionTypes
        _output.WriteLine("=== COLLECTION TYPES ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM CollectionTypes;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = "";
                for (int i = 0; i < reader.FieldCount; i++) row += $"{reader.GetName(i)}: {reader.GetValue(i)} | ";
                _output.WriteLine(row);
            }
        }

        // 1b. Check PaymentCollectionItems & QrPayments
        _output.WriteLine("\n=== PAYMENT COLLECTION ITEMS PRAGMA ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('PaymentCollectionItems');";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _output.WriteLine($"Col: {reader["name"]} ({reader["type"]})");
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM PaymentCollectionItems LIMIT 5;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = "";
                for (int i = 0; i < reader.FieldCount; i++) row += $"{reader.GetName(i)}: {reader.GetValue(i)} | ";
                _output.WriteLine(row);
            }
        }

        _output.WriteLine("\n=== DSM QR PAYMENTS ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, DsmEntryId, DsmName, TargetDsmName, Date, Amount, Tid, Batch, Slot FROM DsmQrPayments ORDER BY Id DESC LIMIT 20;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _output.WriteLine($"Id: {reader["Id"]} | EntryId: {reader["DsmEntryId"]} | Dsm: '{reader["DsmName"]}' | Target: '{reader["TargetDsmName"]}' | Amt: {reader["Amount"]} | Tid: '{reader["Tid"]}' | Batch: '{reader["Batch"]}'");
            }
        }

        _output.WriteLine("\n=== RECENT PAYMENT COLLECTIONS ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT PaymentId, DsmEntryId, PhonePeMorning, CreditCardMorning, CashDeposit, Others, DynamicItemsJson FROM PaymentCollections ORDER BY PaymentId DESC LIMIT 10;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _output.WriteLine($"PaymentId: {reader["PaymentId"]} | EntryId: {reader["DsmEntryId"]} | PhM: {reader["PhonePeMorning"]} | CCM: {reader["CreditCardMorning"]} | Dep: {reader["CashDeposit"]} | Oth: {reader["Others"]} | DynJson: {reader["DynamicItemsJson"]}");
            }
        }

        // 2. Check AppSettings / Settings
        _output.WriteLine("\n=== SETTINGS ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT PumpStationName, PumpMappingsJson, PumpConnectionRulesJson, AppFeatureSettingsJson FROM Settings LIMIT 1;";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                _output.WriteLine($"Station: {reader["PumpStationName"]}");
                _output.WriteLine($"PumpMappingsJson: {reader["PumpMappingsJson"]}");
                _output.WriteLine($"PumpConnectionRulesJson: {reader["PumpConnectionRulesJson"]}");
                _output.WriteLine($"AppFeatureSettingsJson: {reader["AppFeatureSettingsJson"]}");
            }
        }

        // 3. Check DsmProfiles
        _output.WriteLine("\n=== DSM PROFILES PRAGMA ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('DsmProfiles');";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _output.WriteLine($"Col: {reader["name"]} ({reader["type"]})");
            }
        }
        _output.WriteLine("\n=== DSM PROFILES ROWS ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM DsmProfiles;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = "";
                for (int i = 0; i < reader.FieldCount; i++) row += $"{reader.GetName(i)}: {reader.GetValue(i)} | ";
                _output.WriteLine(row);
            }
        }

        // 4. Check DsmUsers
        _output.WriteLine("\n=== DSM USERS ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM DsmUsers;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = "";
                for (int i = 0; i < reader.FieldCount; i++) row += $"{reader.GetName(i)}: {reader.GetValue(i)} | ";
                _output.WriteLine(row);
            }
        }

        // 6. Check DsmEntries rows
        _output.WriteLine("\n=== RECENT DSM ENTRIES ===");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT e.DsmEntryId, s.ShiftDate, s.ShiftType, e.DsmName, e.PumpId, e.ConnectedPumpId, e.ConnectedPumpIdsJson, e.GrossSales, e.TotalCollection, e.Mismatch FROM DsmEntries e LEFT JOIN Shifts s ON e.ShiftId = s.ShiftId ORDER BY e.DsmEntryId DESC LIMIT 15;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _output.WriteLine($"Entry {reader["DsmEntryId"]} | Date: {reader["ShiftDate"]} | Shift: {reader["ShiftType"]} | DSM: {reader["DsmName"]} | Pump: {reader["PumpId"]} | ConnPump: {reader["ConnectedPumpId"]} | ConnJson: {reader["ConnectedPumpIdsJson"]} | Gross: {reader["GrossSales"]} | Coll: {reader["TotalCollection"]} | Mis: {reader["Mismatch"]}");
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT PumpMappingId, PumpId, NozzleNumber, FuelType, TankName, IsActive FROM PumpMappings;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _output.WriteLine($"Mapping {reader["PumpMappingId"]} -> Pump {reader["PumpId"]}, Nozzle {reader["NozzleNumber"]}, Fuel {reader["FuelType"]}, Tank {reader["TankName"]}, Active {reader["IsActive"]}");
            }
        }
    }

    [Fact(Skip = "Diagnostic investigation test only")]
    public async Task InspectDisposableDbAndSimulateSave()
    {
        var sourceDb = @"C:\Users\jadha\AppData\Local\FuelPro\disposable_investigation.db";
        var tempDb = Path.Combine(Path.GetTempPath(), $"investigation_test_{Guid.NewGuid():n}.db");
        File.Copy(sourceDb, tempDb, true);
        if (File.Exists(sourceDb + "-wal")) File.Copy(sourceDb + "-wal", tempDb + "-wal", true);
        if (File.Exists(sourceDb + "-shm")) File.Copy(sourceDb + "-shm", tempDb + "-shm", true);

        try
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                
                // Inspect Settings columns
                _output.WriteLine("--- SETTINGS COLUMNS ---");
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info('Settings');";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    _output.WriteLine($"Col: {reader["name"]} ({reader["type"]})");
                }
            }

            // Ensure schema compatibility
            var options = new DbContextOptionsBuilder<FuelProDbContext>()
                .UseSqlite($"Data Source={tempDb}")
                .Options;

            using (var db = new FuelProDbContext(options))
            {
                var rawSqls = new[]
                {
                    "ALTER TABLE Settings ADD COLUMN Shift1Manager TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN Shift2Manager TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN Shift3Manager TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN FuelRatesJson TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN TankDefinitionsJson TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN CollectionTypesJson TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN AppFeatureSettingsJson TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN PumpMappingsJson TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN PumpConnectionRulesJson TEXT NULL;",
                    "ALTER TABLE Settings ADD COLUMN CngRate REAL NOT NULL DEFAULT 85.0;"
                };
                foreach (var sql in rawSqls)
                {
                    try { await db.Database.ExecuteSqlRawAsync(sql); } catch { }
                }
            }

            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddTransient<FuelProDbContext>(sp => new FuelProDbContext(options));
            services.AddTransient<IStationConfigurationService, StationConfigurationService>();
            services.AddTransient<SyncConfigService>();
            services.AddTransient<SyncEngine>();
            var sp = services.BuildServiceProvider();

            using (var db = new FuelProDbContext(options))
            {
                var appMetas = await db.AppMeta.ToListAsync();
                _output.WriteLine($"--- APPMETA ({appMetas.Count}) ---");
                foreach (var m in appMetas)
                {
                    _output.WriteLine($"Meta: {m.Key} = {m.Value}");
                }

                var settings = await db.Settings.FirstOrDefaultAsync();
                _output.WriteLine($"--- SETTINGS ---");
                if (settings != null)
                {
                    _output.WriteLine($"StationName: {settings.PumpStationName}, LastUpdated: {settings.LastUpdated}");
                    _output.WriteLine($"PumpMappingsJson: {settings.PumpMappingsJson}");
                }

                var pumpMappings = await db.PumpMappings.ToListAsync();
                _output.WriteLine($"--- PUMP MAPPINGS ({pumpMappings.Count}) ---");
                foreach (var pm in pumpMappings)
                {
                    _output.WriteLine($"Id: {pm.PumpMappingId}, Pump: {pm.PumpId}, Nozzle: {pm.NozzleNumber}, Fuel: {pm.FuelType}, Tank: {pm.TankName}, Active: {pm.IsActive}");
                }

                var syncIdMaps = await db.SyncIdMappings.Where(m => m.TableName == "PumpMappings").ToListAsync();
                _output.WriteLine($"--- SYNC ID MAPPINGS FOR PUMPMAPPINGS ({syncIdMaps.Count}) ---");
                foreach (var m in syncIdMaps)
                {
                    _output.WriteLine($"Id: {m.Id}, Table: {m.TableName}, LocalId: {m.LocalId}, RemoteGuid: {m.RemoteGuid}");
                }

                var syncLogs = await db.SyncChangeLogs.Where(l => l.TableName == "PumpMappings").OrderByDescending(l => l.Id).Take(10).ToListAsync();
                _output.WriteLine($"--- SYNC CHANGE LOGS FOR PUMPMAPPINGS ({syncLogs.Count}) ---");
                foreach (var l in syncLogs)
                {
                    _output.WriteLine($"Id: {l.Id}, Table: {l.TableName}, RecordId: {l.RecordId}, Op: {l.Operation}, Guid: {l.RecordGuid}, StationId: {l.StationId}, IsSynced: {l.IsSynced}");
                }
            }

            // Now simulate what DeveloperMainWindowViewModel does when saving station layout
            var stationConfig = sp.GetRequiredService<IStationConfigurationService>();
            var mappings = await stationConfig.GetAllPumpMappingsAsync();

            _output.WriteLine("\n--- ATTEMPTING SavePumpMappingsAsync with current mappings ---");
            try
            {
                var ok = await stationConfig.SavePumpMappingsAsync(mappings);
                _output.WriteLine($"SavePumpMappingsAsync result: {ok}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"DIRECT EXCEPTION: {ex}");
            }

            // Now simulate changing Station ID and then saving
            _output.WriteLine("\n--- SIMULATING CHANGING STATION ID ---");
            var syncConfig = sp.GetRequiredService<SyncConfigService>();
            var currentSyncSettings = await syncConfig.GetSettingsAsync();
            currentSyncSettings.StationId = "TEST-STATION-NEW";
            currentSyncSettings.SyncEnabled = true;
            await syncConfig.SaveSettingsAsync(currentSyncSettings);

            // Queue records
            var syncEngine = sp.GetRequiredService<SyncEngine>();
            await syncEngine.QueueAllHistoricalRecordsForStationAsync("TEST-STATION-NEW");

            // Test 1: PushStationSnapshotToCloudAsync
            _output.WriteLine("\n--- TEST: PushStationSnapshotToCloudAsync ---");
            try
            {
                var snapshotResult = await syncEngine.PushStationSnapshotToCloudAsync("TEST-STATION-NEW");
                _output.WriteLine($"PushStationSnapshotToCloudAsync result: Success={snapshotResult.Success}, Message={snapshotResult.Message}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"PushStationSnapshotToCloudAsync EXCEPTION: {ex}");
            }

            // Test 2: SupabaseDsmService SavePumpNozzleConfigAsync
            _output.WriteLine("\n--- TEST: SupabaseDsmService.SavePumpNozzleConfigAsync ---");
            try
            {
                var dsmService = new SupabaseDsmService(syncConfig);
                var testNozzles = new List<(int NozzleId, string FuelType, int SortOrder)>
                {
                    (1, "MS-I", 1),
                    (3, "HSD", 2)
                };
                var dsmResult = await dsmService.SavePumpNozzleConfigAsync("TEST-STATION-NEW", 1, testNozzles);
                _output.WriteLine($"SavePumpNozzleConfigAsync result: Success={dsmResult.Success}, Error={dsmResult.Error}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"SavePumpNozzleConfigAsync EXCEPTION: {ex}");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempDb)) File.Delete(tempDb);
                if (File.Exists(tempDb + "-wal")) File.Delete(tempDb + "-wal");
                if (File.Exists(tempDb + "-shm")) File.Delete(tempDb + "-shm");
            }
            catch { }
        }
    }
}
