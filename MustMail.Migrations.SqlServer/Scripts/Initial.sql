IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [SMTPAccount] (
    [Id] int NOT NULL IDENTITY,
    [Username] nvarchar(255) NOT NULL,
    [Password] nvarchar(512) NOT NULL,
    [Description] nvarchar(255) NOT NULL,
    CONSTRAINT [PK_SMTPAccount] PRIMARY KEY ([Id])
);

CREATE TABLE [User] (
    [Id] nvarchar(255) NOT NULL,
    [Name] nvarchar(255) NOT NULL,
    [Email] nvarchar(254) NOT NULL,
    [Admin] bit NOT NULL,
    CONSTRAINT [PK_User] PRIMARY KEY ([Id])
);

CREATE TABLE [Message] (
    [Id] nvarchar(255) NOT NULL,
    [Timestamp] datetime2 NOT NULL,
    [SenderName] nvarchar(255) NOT NULL,
    [SenderEmail] nvarchar(254) NOT NULL,
    [Subject] nvarchar(255) NOT NULL,
    [AttachmentCount] int NOT NULL,
    [UserId] nvarchar(255) NOT NULL,
    CONSTRAINT [PK_Message] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Message_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Profile] (
    [Id] int NOT NULL IDENTITY,
    [TimeZone] nvarchar(100) NOT NULL,
    [DateFormat] nvarchar(50) NOT NULL,
    [TimeFormat] nvarchar(50) NOT NULL,
    [UserId] nvarchar(255) NOT NULL,
    CONSTRAINT [PK_Profile] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Profile_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Message_UserId] ON [Message] ([UserId]);

CREATE UNIQUE INDEX [IX_Profile_UserId] ON [Profile] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260506094048_Initial', N'10.0.7');

COMMIT;
GO

