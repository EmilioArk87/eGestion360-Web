-- Agrega la columna RequirePasswordChange a la tabla Users.
-- Ejecutar una sola vez sobre la base de datos existente.

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'RequirePasswordChange'
)
BEGIN
    ALTER TABLE dbo.Users
    ADD RequirePasswordChange BIT NOT NULL
        CONSTRAINT DF_Users_RequirePasswordChange DEFAULT (0);
END;
