using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenConquer.Infrastructure.Persistence.Accounts.Migrations;

/// <inheritdoc />
public partial class InitialAccountSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder
            .CreateTable(
                name: "accounts",
                columns: table => new
                {
                    account_id = table
                        .Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation(
                            "MySql:ValueGenerationStrategy",
                            MySqlValueGenerationStrategy.IdentityColumn
                        ),
                    username = table
                        .Column<string>(
                            type: "varchar(32)",
                            maxLength: 32,
                            nullable: false,
                            collation: "utf8mb4_0900_ai_ci"
                        )
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_status = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: false
                    ),
                    authority_role = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: false
                    ),
                    creation_operation_id = table.Column<Guid>(
                        type: "binary(16)",
                        nullable: false
                    ),
                    last_successful_login_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    state_revision = table.Column<ulong>(
                        type: "bigint unsigned",
                        nullable: false
                    ),
                    created_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    created_by_actor_kind = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: false
                    ),
                    created_by_account_id = table.Column<uint>(
                        type: "int unsigned",
                        nullable: true
                    ),
                    state_changed_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    state_changed_by_actor_kind = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: false
                    ),
                    state_changed_by_account_id = table.Column<uint>(
                        type: "int unsigned",
                        nullable: true
                    ),
                    deleted_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: true
                    ),
                    deleted_by_actor_kind = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: true
                    ),
                    deleted_by_account_id = table.Column<uint>(
                        type: "int unsigned",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.account_id);
                    table.CheckConstraint(
                        "CK_accounts_access_status",
                        "`access_status` IN (1, 2, 3)"
                    );
                    table.CheckConstraint(
                        "CK_accounts_authority_role",
                        "`authority_role` IN (1, 2, 3, 4, 5)"
                    );
                    table.CheckConstraint(
                        "CK_accounts_created_actor",
                        "((`created_by_actor_kind` = 2 AND `created_by_account_id` IS NOT NULL) OR (`created_by_actor_kind` IN (1, 3, 4) AND `created_by_account_id` IS NULL))"
                    );
                    table.CheckConstraint(
                        "CK_accounts_creation_operation_id",
                        "`creation_operation_id` <> 0x00000000000000000000000000000000"
                    );
                    table.CheckConstraint(
                        "CK_accounts_deleted_state",
                        "((`deleted_at_utc` IS NULL AND `deleted_by_actor_kind` IS NULL AND `deleted_by_account_id` IS NULL) OR (`deleted_at_utc` IS NOT NULL AND `deleted_at_utc` = `state_changed_at_utc` AND `deleted_by_actor_kind` IS NOT NULL AND `deleted_by_actor_kind` = `state_changed_by_actor_kind` AND (`deleted_by_account_id` <=> `state_changed_by_account_id`) AND ((`deleted_by_actor_kind` = 2 AND `deleted_by_account_id` IS NOT NULL) OR (`deleted_by_actor_kind` IN (3, 4) AND `deleted_by_account_id` IS NULL))))"
                    );
                    table.CheckConstraint(
                        "CK_accounts_last_successful_login_at",
                        "`last_successful_login_at_utc` IS NULL OR `last_successful_login_at_utc` >= `created_at_utc`"
                    );
                    table.CheckConstraint(
                        "CK_accounts_state_changed_actor",
                        "((`state_changed_by_actor_kind` = 2 AND `state_changed_by_account_id` IS NOT NULL) OR (`state_changed_by_actor_kind` IN (3, 4) AND `state_changed_by_account_id` IS NULL))"
                    );
                    table.CheckConstraint(
                        "CK_accounts_state_changed_at",
                        "`state_changed_at_utc` >= `created_at_utc`"
                    );
                    table.CheckConstraint("CK_accounts_state_revision", "`state_revision` > 0");
                    table.ForeignKey(
                        name: "FK_accounts_created_by_account",
                        column: x => x.created_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_accounts_deleted_by_account",
                        column: x => x.deleted_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_accounts_state_changed_by_account",
                        column: x => x.state_changed_by_account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4")
            .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

        migrationBuilder
            .CreateTable(
                name: "schema_compatibility",
                columns: table => new
                {
                    component_name = table
                        .Column<string>(
                            type: "varchar(64)",
                            maxLength: 64,
                            nullable: false,
                            collation: "ascii_bin"
                        )
                        .Annotation("MySql:CharSet", "ascii"),
                    schema_version = table.Column<uint>(type: "int unsigned", nullable: false),
                    migration_id = table
                        .Column<string>(
                            type: "varchar(128)",
                            maxLength: 128,
                            nullable: false,
                            collation: "ascii_bin"
                        )
                        .Annotation("MySql:CharSet", "ascii"),
                    applied_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_compatibility", x => x.component_name);
                    table.CheckConstraint(
                        "CK_schema_compatibility_component_name",
                        "CHAR_LENGTH(`component_name`) > 0"
                    );
                    table.CheckConstraint(
                        "CK_schema_compatibility_migration_id",
                        "CHAR_LENGTH(`migration_id`) > 0"
                    );
                    table.CheckConstraint(
                        "CK_schema_compatibility_schema_version",
                        "`schema_version` > 0"
                    );
                }
            )
            .Annotation("MySql:CharSet", "ascii")
            .Annotation("Relational:Collation", "ascii_bin");

        migrationBuilder
            .CreateTable(
                name: "account_audit_events",
                columns: table => new
                {
                    account_audit_event_id = table
                        .Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation(
                            "MySql:ValueGenerationStrategy",
                            MySqlValueGenerationStrategy.IdentityColumn
                        ),
                    account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    event_kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    actor_kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    actor_account_id = table.Column<uint>(type: "int unsigned", nullable: true),
                    correlation_id = table.Column<Guid>(type: "binary(16)", nullable: false),
                    reason_code = table
                        .Column<string>(
                            type: "varchar(64)",
                            maxLength: 64,
                            nullable: true,
                            collation: "ascii_bin"
                        )
                        .Annotation("MySql:CharSet", "ascii"),
                    previous_access_status = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: true
                    ),
                    new_access_status = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: true
                    ),
                    previous_authority_role = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: true
                    ),
                    new_authority_role = table.Column<byte>(
                        type: "tinyint unsigned",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_audit_events", x => x.account_audit_event_id);
                    table.CheckConstraint(
                        "CK_account_audit_events_actor",
                        "((`event_kind` = 1 AND `actor_kind` = 1 AND `actor_account_id` IS NULL) OR (`actor_kind` = 2 AND `actor_account_id` IS NOT NULL) OR (`actor_kind` IN (3, 4) AND `actor_account_id` IS NULL))"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_correlation_id",
                        "`correlation_id` <> 0x00000000000000000000000000000000"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_event_kind",
                        "`event_kind` IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_new_access_status",
                        "`new_access_status` IS NULL OR `new_access_status` IN (1, 2, 3)"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_new_authority_role",
                        "`new_authority_role` IS NULL OR `new_authority_role` IN (1, 2, 3, 4, 5)"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_payload",
                        "((`event_kind` = 1 AND `previous_access_status` IS NULL AND `new_access_status` IS NOT NULL AND `new_access_status` IN (1, 2, 3) AND `previous_authority_role` IS NULL AND `new_authority_role` IS NOT NULL AND `new_authority_role` IN (1, 2, 3, 4, 5)) OR (`event_kind` = 2 AND `previous_access_status` IS NOT NULL AND `previous_access_status` = 1 AND `new_access_status` IS NOT NULL AND `new_access_status` = 2 AND `previous_authority_role` IS NULL AND `new_authority_role` IS NULL) OR (`event_kind` = 3 AND `previous_access_status` IS NOT NULL AND `previous_access_status` = 2 AND `new_access_status` IS NOT NULL AND `new_access_status` = 1 AND `previous_authority_role` IS NULL AND `new_authority_role` IS NULL) OR (`event_kind` = 4 AND `previous_access_status` IS NOT NULL AND `previous_access_status` IN (1, 2) AND `new_access_status` IS NOT NULL AND `new_access_status` = 3 AND `previous_authority_role` IS NULL AND `new_authority_role` IS NULL) OR (`event_kind` = 5 AND `previous_access_status` IS NOT NULL AND `previous_access_status` = 3 AND `new_access_status` IS NOT NULL AND `new_access_status` IN (1, 2) AND `previous_authority_role` IS NULL AND `new_authority_role` IS NULL) OR (`event_kind` IN (6, 7, 8, 9) AND `previous_access_status` IS NULL AND `new_access_status` IS NULL AND `previous_authority_role` IS NULL AND `new_authority_role` IS NULL) OR (`event_kind` = 10 AND `previous_access_status` IS NULL AND `new_access_status` IS NULL AND `previous_authority_role` IS NOT NULL AND `previous_authority_role` IN (1, 2, 3, 4, 5) AND `new_authority_role` IS NOT NULL AND `new_authority_role` IN (1, 2, 3, 4, 5) AND `previous_authority_role` <> `new_authority_role`))"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_previous_access_status",
                        "`previous_access_status` IS NULL OR `previous_access_status` IN (1, 2, 3)"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_previous_authority_role",
                        "`previous_authority_role` IS NULL OR `previous_authority_role` IN (1, 2, 3, 4, 5)"
                    );
                    table.CheckConstraint(
                        "CK_account_audit_events_reason_code",
                        "`reason_code` IS NULL OR CHAR_LENGTH(`reason_code`) > 0"
                    );
                    table.ForeignKey(
                        name: "FK_account_audit_events_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_account_audit_events_actor_account",
                        column: x => x.actor_account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4")
            .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

        migrationBuilder
            .CreateTable(
                name: "account_password_credentials",
                columns: table => new
                {
                    account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    password_hash = table
                        .Column<string>(
                            type: "varchar(255)",
                            maxLength: 255,
                            nullable: false,
                            collation: "utf8mb4_0900_bin"
                        )
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password_changed_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    revision = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_password_credentials", x => x.account_id);
                    table.CheckConstraint(
                        "CK_account_password_credentials_password_hash",
                        "CHAR_LENGTH(`password_hash`) > 0"
                    );
                    table.CheckConstraint(
                        "CK_account_password_credentials_revision",
                        "`revision` > 0"
                    );
                    table.ForeignKey(
                        name: "FK_account_password_credentials_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4")
            .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

        migrationBuilder
            .CreateTable(
                name: "game_login_tickets",
                columns: table => new
                {
                    session_uid = table.Column<uint>(type: "int unsigned", nullable: false),
                    authentication_key = table.Column<uint>(
                        type: "int unsigned",
                        nullable: false
                    ),
                    account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    username = table
                        .Column<string>(
                            type: "varchar(32)",
                            maxLength: 32,
                            nullable: false,
                            collation: "utf8mb4_0900_ai_ci"
                        )
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issued_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                    expires_at_utc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_login_tickets", x => x.session_uid);
                    table.CheckConstraint(
                        "CK_game_login_tickets_expiration",
                        "`expires_at_utc` > `issued_at_utc`"
                    );
                    table.ForeignKey(
                        name: "FK_game_login_tickets_account",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            )
            .Annotation("MySql:CharSet", "utf8mb4")
            .Annotation("Relational:Collation", "utf8mb4_0900_as_cs");

        migrationBuilder.CreateIndex(
            name: "IX_account_audit_events_account_id_occurred_at_utc",
            table: "account_audit_events",
            columns: new[] { "account_id", "occurred_at_utc" }
        );

        migrationBuilder.CreateIndex(
            name: "IX_account_audit_events_actor_account_id",
            table: "account_audit_events",
            column: "actor_account_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_account_audit_events_correlation_id",
            table: "account_audit_events",
            column: "correlation_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_accounts_created_by_account_id",
            table: "accounts",
            column: "created_by_account_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_accounts_deleted_by_account_id",
            table: "accounts",
            column: "deleted_by_account_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_accounts_state_changed_by_account_id",
            table: "accounts",
            column: "state_changed_by_account_id"
        );

        migrationBuilder.CreateIndex(
            name: "UX_accounts_creation_operation_id",
            table: "accounts",
            column: "creation_operation_id",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "UX_accounts_username",
            table: "accounts",
            column: "username",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_game_login_tickets_account_id",
            table: "game_login_tickets",
            column: "account_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_game_login_tickets_expires_at_utc",
            table: "game_login_tickets",
            column: "expires_at_utc"
        );

        migrationBuilder.Sql(
            """
            INSERT INTO `schema_compatibility`
                (`component_name`, `schema_version`, `migration_id`, `applied_at_utc`)
            VALUES
                ('accounts', 1, '20260906043138_InitialAccountSchema', UTC_TIMESTAMP(6));
            """
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "account_audit_events");

        migrationBuilder.DropTable(name: "account_password_credentials");

        migrationBuilder.DropTable(name: "game_login_tickets");

        migrationBuilder.DropTable(name: "schema_compatibility");

        migrationBuilder.DropTable(name: "accounts");
    }
}
