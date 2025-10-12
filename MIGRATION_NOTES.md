# Database Migration: SQLite to SQL Server

## Overview
This project has been migrated from SQLite to SQL Server to use a remote database hosted on somee.com.

## Changes Made

### 1. Connection String (appsettings.json)
- **Previous**: SQLite local file (`Data Source=eGestion360.db`)
- **Current**: SQL Server connection to `eBD_SPD.mssql.somee.com`
  - Server: `eBD_SPD.mssql.somee.com`
  - Database: `eBD_SPD`
  - User: `acc_datos`
  - TrustServerCertificate: `True` (required for Somee.com)

### 2. Program.cs
- Changed from `UseSqlite()` to `UseSqlServer()`
- Updated to read connection string from configuration
- Changed database initialization from `EnsureCreated()` to `Migrate()` for better migration management

### 3. Migrations
- **Removed**: SQLite-specific migrations (used INTEGER, TEXT types)
- **Added**: SQL Server migrations (uses int, nvarchar, datetime2, bit types)
- **Current Migration**: `20251012035426_InitialSqlServerMigration`

## Database Schema
The database includes a `Users` table with:
- Id (int, identity)
- Username (nvarchar(50), unique)
- Email (nvarchar(100), unique)
- Password (nvarchar(255))
- CreatedAt (datetime2)
- IsActive (bit)

## Seeded Data
- Default admin user:
  - Username: `admin`
  - Email: `admin@siptech.com`
  - Password: `admin123`

## Deployment Notes
When deploying this application:
1. The database migrations will run automatically on startup
2. Ensure the SQL Server database `eBD_SPD` exists on the server
3. The connection credentials must have permissions to create tables and indexes
4. The first run will create the schema and seed the admin user

## Security Considerations
⚠️ **Important**: The connection string contains credentials. In production:
- Consider using User Secrets for development
- Use environment variables or Azure Key Vault for production
- The default admin password should be changed immediately after first login
