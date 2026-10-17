using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HappyInventory.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3SessionSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecurityVersion",
                table: "RefreshSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Users_ProtectSystemAdminUpdate
                BEFORE UPDATE ON Users
                WHEN OLD.IsSystemAdmin = 1 AND
                    (NEW.IsSystemAdmin <> 1 OR NEW.IsActive <> 1 OR NEW.RoleId <> 1 OR NEW.DeletedAtUtc IS NOT NULL)
                BEGIN
                    SELECT RAISE(ABORT, 'protected_system_admin');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Users_ProtectSystemAdminDelete
                BEFORE DELETE ON Users
                WHEN OLD.IsSystemAdmin = 1
                BEGIN
                    SELECT RAISE(ABORT, 'protected_system_admin');
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Users_BumpSecurityVersion
                AFTER UPDATE OF PasswordHash, RoleId, IsActive, DeletedAtUtc ON Users
                WHEN NEW.PasswordHash <> OLD.PasswordHash OR NEW.RoleId <> OLD.RoleId OR
                    NEW.IsActive <> OLD.IsActive OR NEW.DeletedAtUtc IS NOT OLD.DeletedAtUtc
                BEGIN
                    UPDATE Users SET SecurityVersion = OLD.SecurityVersion + 1 WHERE Id = NEW.Id;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER TR_Users_BumpSecurityVersion;");
            migrationBuilder.Sql("DROP TRIGGER TR_Users_ProtectSystemAdminDelete;");
            migrationBuilder.Sql("DROP TRIGGER TR_Users_ProtectSystemAdminUpdate;");
            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "RefreshSessions");
        }
    }
}
