BEGIN TRANSACTION;
CREATE TABLE [SMTPAccountAllowedRecipient] (
    [Id] int NOT NULL IDENTITY,
    [EmailAddress] nvarchar(255) NOT NULL,
    [SMTPAccountId] int NOT NULL,
    CONSTRAINT [PK_SMTPAccountAllowedRecipient] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SMTPAccountAllowedRecipient_SMTPAccount_SMTPAccountId] FOREIGN KEY ([SMTPAccountId]) REFERENCES [SMTPAccount] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [SMTPAccountAllowedSender] (
    [Id] int NOT NULL IDENTITY,
    [EmailAddress] nvarchar(255) NOT NULL,
    [SMTPAccountId] int NOT NULL,
    CONSTRAINT [PK_SMTPAccountAllowedSender] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SMTPAccountAllowedSender_SMTPAccount_SMTPAccountId] FOREIGN KEY ([SMTPAccountId]) REFERENCES [SMTPAccount] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_SMTPAccountAllowedRecipient_SMTPAccountId] ON [SMTPAccountAllowedRecipient] ([SMTPAccountId]);

CREATE INDEX [IX_SMTPAccountAllowedSender_SMTPAccountId] ON [SMTPAccountAllowedSender] ([SMTPAccountId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260529222850_AccountRestrictions', N'10.0.8');

COMMIT;
GO

