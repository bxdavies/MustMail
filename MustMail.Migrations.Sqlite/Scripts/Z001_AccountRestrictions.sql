BEGIN TRANSACTION;
CREATE TABLE "SMTPAccountAllowedRecipient" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SMTPAccountAllowedRecipient" PRIMARY KEY AUTOINCREMENT,
    "EmailAddress" TEXT NOT NULL,
    "SMTPAccountId" INTEGER NOT NULL,
    CONSTRAINT "FK_SMTPAccountAllowedRecipient_SMTPAccount_SMTPAccountId" FOREIGN KEY ("SMTPAccountId") REFERENCES "SMTPAccount" ("Id") ON DELETE CASCADE
);

CREATE TABLE "SMTPAccountAllowedSender" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SMTPAccountAllowedSender" PRIMARY KEY AUTOINCREMENT,
    "EmailAddress" TEXT NOT NULL,
    "SMTPAccountId" INTEGER NOT NULL,
    CONSTRAINT "FK_SMTPAccountAllowedSender_SMTPAccount_SMTPAccountId" FOREIGN KEY ("SMTPAccountId") REFERENCES "SMTPAccount" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_SMTPAccountAllowedRecipient_SMTPAccountId" ON "SMTPAccountAllowedRecipient" ("SMTPAccountId");

CREATE INDEX "IX_SMTPAccountAllowedSender_SMTPAccountId" ON "SMTPAccountAllowedSender" ("SMTPAccountId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260529222739_AccountRestrictions', '10.0.8');

COMMIT;

