CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "SMTPAccount" (
    "Id" INTEGER NOT NULL,
    "Username" TEXT NOT NULL,
    "Password" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    CONSTRAINT "PK_SMTPAccount" PRIMARY KEY ("Id")
);

CREATE TABLE "User" (
    "Id" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "Admin" INTEGER NOT NULL,
    CONSTRAINT "PK_User" PRIMARY KEY ("Id")
);

CREATE TABLE "Message" (
    "Id" TEXT NOT NULL,
    "Timestamp" TEXT NOT NULL,
    "SenderName" TEXT NOT NULL,
    "SenderEmail" TEXT NOT NULL,
    "Subject" TEXT NOT NULL,
    "AttachmentCount" INTEGER NOT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "PK_Message" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Message_User_UserId" FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Profile" (
    "Id" INTEGER NOT NULL,
    "TimeZone" TEXT NOT NULL,
    "DateFormat" TEXT NOT NULL,
    "TimeFormat" TEXT NOT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "PK_Profile" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Profile_User_UserId" FOREIGN KEY ("UserId") REFERENCES "User" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_Message_UserId" ON "Message" ("UserId");

CREATE UNIQUE INDEX "IX_Profile_UserId" ON "Profile" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260505190430_Initial', '10.0.7');

COMMIT;

