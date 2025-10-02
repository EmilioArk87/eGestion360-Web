# Company Model - Multi-Tenant Support

## Overview
The Company model has been created to support multi-tenant functionality with international operations in mind. This table stores company/organization information that users can be associated with.

## Fields Description

### Core Business Information
- **Id**: Primary key (auto-increment)
- **Name**: Display name of the company (required, max 200 characters)
- **LegalName**: Official registered legal name (optional, max 100 characters)
- **TaxId**: Tax identification number - unique per company (required, max 50 characters)
  - This field has a unique index to prevent duplicate registrations
  - Supports various formats: EIN (US), VAT number (EU), RFC (Mexico), etc.

### Address Information
Designed to support international addresses:
- **Address**: Street address/location (optional, max 500 characters)
- **City**: City name (optional, max 100 characters)
- **State**: State/Province/Region (optional, max 100 characters)
- **PostalCode**: Postal/ZIP code (optional, max 20 characters)
- **Country**: ISO 3166-1 alpha-2 country code (required, 2 characters)
  - Examples: US, MX, ES, BR, AR, etc.

### Contact Information
- **Phone**: Contact phone number (optional, max 20 characters)
- **Email**: Contact email address (optional, max 100 characters)
- **Website**: Company website URL (optional, max 200 characters)

### International Settings
Essential for global operations:
- **Currency**: Default currency code (required, max 10 characters, default: "USD")
  - Examples: USD, EUR, MXN, BRL, ARS
- **TimeZone**: Default timezone identifier (required, max 10 characters, default: "UTC")
  - Examples: UTC, EST, PST, CET, GMT-5
- **Language**: Default language code (optional, max 10 characters)
  - Examples: en, es, pt, fr, de

### Status & Audit
- **CreatedAt**: Timestamp when the company was created (UTC)
- **UpdatedAt**: Timestamp of last update (nullable)
- **IsActive**: Boolean flag indicating if the company is active

## Relationships

### Users Relationship
- **One-to-Many**: One company can have many users
- **Foreign Key**: User.CompanyId references Company.Id
- **Delete Behavior**: SetNull - When a company is deleted, users' CompanyId is set to null
- **Navigation Property**: `ICollection<User> Users`

## Usage Examples

```csharp
// Creating a new company
var company = new Company
{
    Name = "Acme Corporation",
    LegalName = "Acme Corporation S.A. de C.V.",
    TaxId = "ABC123456789",
    Address = "Av. Reforma 123",
    City = "Ciudad de México",
    State = "CDMX",
    PostalCode = "01000",
    Country = "MX",
    Phone = "+52 55 1234 5678",
    Email = "contacto@acme.mx",
    Currency = "MXN",
    TimeZone = "GMT-6",
    Language = "es"
};

// Associating a user with a company
user.CompanyId = company.Id;
user.Company = company;
```

## Database Migration
The Company table was added via migration `20251002040801_AddCompanyTable`.

To apply the migration to your database:
```bash
dotnet ef database update
```

## Multi-Tenant Considerations
- Each company represents a separate tenant in the system
- Users are associated with companies via the CompanyId foreign key
- Company-specific settings (currency, timezone, language) can be used throughout the application
- The TaxId field ensures each company is uniquely identified
- The IsActive flag allows for soft deletion/deactivation of companies
