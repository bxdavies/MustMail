CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
CREATE TABLE `SMTPAccount` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Username` varchar(255) NOT NULL,
    `Password` varchar(512) NOT NULL,
    `Description` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `User` (
    `Id` varchar(255) NOT NULL,
    `Name` varchar(255) NOT NULL,
    `Email` varchar(254) NOT NULL,
    `Admin` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Message` (
    `Id` varchar(255) NOT NULL,
    `Timestamp` datetime(6) NOT NULL,
    `SenderName` varchar(255) NOT NULL,
    `SenderEmail` varchar(254) NOT NULL,
    `Subject` varchar(255) NOT NULL,
    `AttachmentCount` int NOT NULL,
    `UserId` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Message_User_UserId` FOREIGN KEY (`UserId`) REFERENCES `User` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `Profile` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TimeZone` varchar(100) NOT NULL,
    `DateFormat` varchar(50) NOT NULL,
    `TimeFormat` varchar(50) NOT NULL,
    `UserId` varchar(255) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Profile_User_UserId` FOREIGN KEY (`UserId`) REFERENCES `User` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_Message_UserId` ON `Message` (`UserId`);

CREATE UNIQUE INDEX `IX_Profile_UserId` ON `Profile` (`UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260506094917_Initial', '10.0.7');

COMMIT;

