CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "SMTPAccount" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SMTPAccount" PRIMARY KEY AUTOINCREMENT,
    "Username" TEXT NOT NULL,
    "Password" TEXT NOT NULL,
    "Description" TEXT NOT NULL
);

CREATE TABLE "User" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_User" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "Admin" INTEGER NOT NULL
);

CREATE TABLE "Message" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Message" PRIMARY KEY,
    "Timestamp" TEXT NOT NULL,
    "SenderName" TEXT NOT NULL,
    "SenderEmail" TEXT NOT NULL,
    "Subject" TEXT NOT NULL,
    "AttachmentCount" INTEGER NOT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "FK_Message_User_UserId" FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Profile" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Profile" PRIMARY KEY AUTOINCREMENT,
    "TimeZone" TEXT NOT NULL,
    "DateFormat" TEXT NOT NULL,
    "TimeFormat" TEXT NOT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "FK_Profile_User_UserId" FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_Message_UserId" ON "Message" ("UserId");

CREATE UNIQUE INDEX "IX_Profile_UserId" ON "Profile" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260304123706_v030Init', '10.0.3');

COMMIT;

