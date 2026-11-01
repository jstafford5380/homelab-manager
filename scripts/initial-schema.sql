CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Activity" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Activity" PRIMARY KEY AUTOINCREMENT,
    "AtUtc" TEXT NOT NULL,
    "Actor" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "Detail" TEXT NOT NULL
);

CREATE TABLE "Connections" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Connections" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "ApiUri" TEXT NOT NULL,
    "ConnectHost" TEXT NULL,
    "ConnectPort" INTEGER NULL,
    "ProtectedApiKey" TEXT NOT NULL,
    "Version" TEXT NULL,
    "Status" TEXT NOT NULL,
    "LastSuccessUtc" TEXT NULL,
    "PollSeconds" INTEGER NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "TopologyJson" TEXT NULL
);

CREATE TABLE "Disks" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Disks" PRIMARY KEY,
    "ConnectionId" TEXT NOT NULL,
    "IdentityKey" TEXT NOT NULL,
    "Serial" TEXT NOT NULL,
    "Model" TEXT NOT NULL,
    "Wwn" TEXT NULL,
    "Alias" TEXT NOT NULL,
    "MappingConfidence" TEXT NOT NULL,
    "Bay" TEXT NULL,
    "BayVerified" INTEGER NOT NULL,
    "Present" INTEGER NOT NULL,
    "FirstSeenUtc" TEXT NOT NULL,
    "LastSeenUtc" TEXT NOT NULL,
    "PurchasedUtc" TEXT NULL,
    "WarrantyEndUtc" TEXT NULL,
    "Notes" TEXT NOT NULL
);

CREATE TABLE "Findings" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Findings" PRIMARY KEY,
    "ConnectionId" TEXT NOT NULL,
    "DiskId" TEXT NULL,
    "Rule" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Evidence" TEXT NOT NULL,
    "NextStep" TEXT NOT NULL,
    "State" TEXT NOT NULL,
    "FirstSeenUtc" TEXT NOT NULL,
    "LastSeenUtc" TEXT NOT NULL,
    "ReviewUtc" TEXT NULL
);

CREATE TABLE "Observations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Observations" PRIMARY KEY AUTOINCREMENT,
    "ConnectionId" TEXT NOT NULL,
    "DiskId" TEXT NULL,
    "SourceKey" TEXT NOT NULL,
    "Metric" TEXT NOT NULL,
    "Value" REAL NULL,
    "Unit" TEXT NOT NULL,
    "ObservedUtc" TEXT NULL,
    "ReceivedUtc" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "Quality" TEXT NOT NULL,
    "MissingReason" TEXT NULL
);

CREATE TABLE "Owners" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Owners" PRIMARY KEY AUTOINCREMENT,
    "Username" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL
);

CREATE TABLE "Plans" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Plans" PRIMARY KEY,
    "DiskId" TEXT NULL,
    "Title" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "Budget" TEXT NULL,
    "Currency" TEXT NOT NULL,
    "DueUtc" TEXT NULL
);

CREATE TABLE "Recovery" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Recovery" PRIMARY KEY,
    "Asset" TEXT NOT NULL,
    "Destination" TEXT NOT NULL,
    "Mechanism" TEXT NOT NULL,
    "RpoHours" INTEGER NOT NULL,
    "RecoveryPointUtc" TEXT NULL,
    "RestoreTestUtc" TEXT NULL,
    "RestoreResult" TEXT NOT NULL,
    "Evidence" TEXT NOT NULL
);

CREATE INDEX "IX_Disks_ConnectionId_IdentityKey" ON "Disks" ("ConnectionId", "IdentityKey");

CREATE UNIQUE INDEX "IX_Observations_SourceKey_ConnectionId" ON "Observations" ("SourceKey", "ConnectionId");

CREATE UNIQUE INDEX "IX_Owners_Username" ON "Owners" ("Username");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261101052807_InitialSchema', '10.0.0');

COMMIT;
