using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDsmPumpAssignmentCompletedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The CompletedDate column is managed by EnsureLegacyDatabaseCompatibility
            // (EnsureColumnExists in App.xaml.cs) which runs BEFORE MigrateAsync().
            //
            // On upgrade paths: EnsureColumnExists already added the column before this
            // migration ran, so a plain AddColumn would crash with "duplicate column name".
            //
            // On fresh-install paths: EnsureColumnExists adds the column only when the
            // DsmPumpAssignments table exists (guarded by TableExists check), which is
            // satisfied after the initial schema migration creates the table.
            //
            // Because both paths are covered by EnsureColumnExists, this migration
            // intentionally performs no DDL. It exists solely so the EF snapshot
            // and model remain in sync with the physical schema.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite does not reliably support DROP COLUMN; leave as no-op.
        }
    }
}
