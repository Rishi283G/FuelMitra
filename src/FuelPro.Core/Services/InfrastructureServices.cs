using Microsoft.Data.Sqlite;
using Serilog;

namespace FuelPro.Core.Services;

public class BackupService
{
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly ILogger _logger = Log.ForContext<BackupService>();

    public BackupService(string dbPath, string? backupDir = null)
    {
        _dbPath = dbPath;
        _backupDir = backupDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FuelPro", "backups");
    }

    public bool PerformBackup()
    {
        string? temporaryBackupPath = null;
        try
        {
            if (!File.Exists(_dbPath) || new FileInfo(_dbPath).Length == 0) return true;
            Directory.CreateDirectory(_backupDir);
            var backupPath = Path.Combine(_backupDir, $"fuelPro_{DateTime.Now:yyyyMMdd}.db");
            temporaryBackupPath = Path.Combine(_backupDir, $"fuelPro_{Guid.NewGuid():N}.tmp");
            var sourceConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();
            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryBackupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            using (var source = new SqliteConnection(sourceConnectionString))
            using (var destination = new SqliteConnection(destinationConnectionString))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            File.Move(temporaryBackupPath, backupPath, true);
            temporaryBackupPath = null;
            _logger.Information("Database backed up to {Path}", backupPath);
            // Cleanup older than 30 days
            try
            {
                foreach (var f in Directory.GetFiles(_backupDir, "fuelPro_*.db"))
                {
                    if (new FileInfo(f).CreationTime < DateTime.Now.AddDays(-30))
                        File.Delete(f);
                }
            }
            catch (Exception cleanupEx) { _logger.Warning(cleanupEx, "Backup succeeded but old backup cleanup failed"); }
            return true;
        }
        catch (Exception ex)
        {
            if (temporaryBackupPath != null)
            {
                try { File.Delete(temporaryBackupPath); } catch { }
            }
            _logger.Error(ex, "Backup failed");
            return false;
        }
    }
}

public class DraftService
{
    private readonly string _draftsDir;
    private readonly ILogger _logger = Log.ForContext<DraftService>();

    public DraftService()
    {
        _draftsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FuelPro", "drafts");
        Directory.CreateDirectory(_draftsDir);
    }

    public string DraftFilePath => Path.Combine(_draftsDir, "dsm_draft.json");
    public bool HasDraft() => File.Exists(DraftFilePath);

    public void SaveDraft(string json)
    {
        try { File.WriteAllText(DraftFilePath, json); }
        catch (Exception ex) { _logger.Warning(ex, "Draft save failed"); }
    }

    public string? LoadDraft()
    {
        try { return File.Exists(DraftFilePath) ? File.ReadAllText(DraftFilePath) : null; }
        catch (Exception ex) { _logger.Warning(ex, "Draft load failed"); return null; }
    }

    public void ClearDraft()
    {
        try { if (File.Exists(DraftFilePath)) File.Delete(DraftFilePath); }
        catch (Exception ex) { _logger.Warning(ex, "Draft clear failed"); }
    }
}
