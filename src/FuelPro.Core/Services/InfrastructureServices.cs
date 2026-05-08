using Serilog;

namespace FuelPro.Core.Services;

public class BackupService
{
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly ILogger _logger = Log.ForContext<BackupService>();

    public BackupService(string dbPath)
    {
        _dbPath = dbPath;
        _backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FuelPro", "backups");
    }

    public void PerformBackup()
    {
        try
        {
            if (!File.Exists(_dbPath) || new FileInfo(_dbPath).Length == 0) return;
            Directory.CreateDirectory(_backupDir);
            var backupPath = Path.Combine(_backupDir, $"fuelPro_{DateTime.Now:yyyyMMdd}.db");
            if (!File.Exists(backupPath))
            {
                File.Copy(_dbPath, backupPath, false);
                _logger.Information("Database backed up to {Path}", backupPath);
            }
            // Cleanup older than 30 days
            foreach (var f in Directory.GetFiles(_backupDir, "fuelPro_*.db"))
            {
                if (new FileInfo(f).CreationTime < DateTime.Now.AddDays(-30))
                    File.Delete(f);
            }
        }
        catch (Exception ex) { _logger.Error(ex, "Backup failed"); }
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
