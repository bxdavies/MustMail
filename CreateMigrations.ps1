# Ask user for starting migration
$fromMigration = Read-Host "Enter the FROM migration (leave blank for initial)"

# PostgreSQL
Write-Host "Generating Postgres migration script..."

$env:DB_PROVIDER = "Postgres"


if ([string]::IsNullOrWhiteSpace($fromMigration)) {
    dotnet ef migrations script `
        -o "Db\Scripts\Postgres\Initial.sql"
} else {
    dotnet ef migrations script $fromMigration `
        -o "Db\Scripts\Postgres\$fromMigration.sql"
}
# Sqlite
Write-Host "Generating Sqlite migration script..."

$env:DB_PROVIDER = "Sqlite"

if ([string]::IsNullOrWhiteSpace($fromMigration)) {
    dotnet ef migrations script `
        -o "Db\Scripts\Sqlite\Initial.sql"
} else {
    dotnet ef migrations script $fromMigration `
        -o "Db\Scripts\SSqlite\$fromMigration.sql"
}

Write-Host "Done!"