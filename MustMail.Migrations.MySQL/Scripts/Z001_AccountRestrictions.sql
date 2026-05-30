START TRANSACTION;
CREATE TABLE `SMTPAccountAllowedRecipient` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmailAddress` varchar(255) NOT NULL,
    `SMTPAccountId` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SMTPAccountAllowedRecipient_SMTPAccount_SMTPAccountId` FOREIGN KEY (`SMTPAccountId`) REFERENCES `SMTPAccount` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `SMTPAccountAllowedSender` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmailAddress` varchar(255) NOT NULL,
    `SMTPAccountId` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SMTPAccountAllowedSender_SMTPAccount_SMTPAccountId` FOREIGN KEY (`SMTPAccountId`) REFERENCES `SMTPAccount` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_SMTPAccountAllowedRecipient_SMTPAccountId` ON `SMTPAccountAllowedRecipient` (`SMTPAccountId`);

CREATE INDEX `IX_SMTPAccountAllowedSender_SMTPAccountId` ON `SMTPAccountAllowedSender` (`SMTPAccountId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260529222803_AccountRestrictions', '10.0.8');

COMMIT;

