# Ask user for starting migration
$fromMigration = Read-Host "Enter the FROM migration (leave blank for initial)"
$newMigration = Read-Host "Enter the NEW migration name (leave blank for initial)"

# PostgreSQL
Write-Host "Generating Postgres migration script..."

$env:Db_Provider = "Postgres"


if ([string]::IsNullOrWhiteSpace($fromMigration)) {
    dotnet ef migrations add Initial --project MustMail.Migrations.Postgres --startup-project MustMail.App
    Start-Sleep -Seconds 5
    dotnet ef migrations script `
        -o "MustMail.Migrations.Postgres\Scripts\Initial.sql" --project MustMail.Migrations.Postgres --startup-project MustMail.App
} else {
    dotnet ef migrations add $newMigration --project MustMail.Migrations.Postgres --startup-project MustMail.App
    Start-Sleep -Seconds 5
    dotnet ef migrations script $fromMigration $newMigration `
        -o "MustMail.Migrations.Postgres\Scripts\$fromMigration.sql" --project MustMail.Migrations.Postgres --startup-project MustMail.App
}
# Sqlite
Write-Host "Generating Sqlite migration script..."

$env:Db_Provider = "Sqlite"

if ([string]::IsNullOrWhiteSpace($fromMigration)) {
    dotnet ef migrations add Initial --project MustMail.Migrations.Sqlite --startup-project MustMail.App
    Start-Sleep -Seconds 5
    dotnet ef migrations script `
        -o "MustMail.Migrations.Sqlite\Scripts\Initial.sql" --project MustMail.Migrations.Sqlite --startup-project MustMail.App
} else {
    dotnet ef migrations add $newMigration --project MustMail.Migrations.Sqlite --startup-project MustMail.App
    Start-Sleep -Seconds 5
    dotnet ef migrations script $fromMigration $newMigration `
        -o "MustMail.Migrations.Sqlite\Scripts\$fromMigration.sql" --project MustMail.Migrations.Sqlite --startup-project MustMail.App
}

Write-Host "Done!"