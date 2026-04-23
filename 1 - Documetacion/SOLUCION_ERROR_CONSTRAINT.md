# 🚨 Solución: Error de Constraint EmailConfiguration

## ❌ **Problema Identificado**
```
SQL Error [547] [23000]: The INSERT statement conflicted with the CHECK constraint "CK_EmailConfiguration_OnlyOneDefault"
```

**Causa:** El constraint requiere que **solo las configuraciones activas pueden ser por defecto** (`IsDefault = 1` requiere `IsActive = 1`).

---

## ✅ **Solución Inmediata**

### **Paso 1: Ejecutar Script de Corrección**
```sql
-- Ejecuta este script en tu base de datos eBD_SPD
-- Ubicación: sql_scripts/corregir_constraint_email_configuration.sql
```

**El script automáticamente:**
- ✅ Identifica configuraciones problemáticas
- ✅ Corrige configuraciones inactivas que están marcadas como por defecto
- ✅ Establece una nueva configuración por defecto si es necesario
- ✅ Verifica que todo esté correcto después del cambio

### **Paso 2: Verificar la Corrección**
```sql
SELECT 
    Id, ProfileName, IsActive, IsDefault,
    CASE 
        WHEN IsActive = 0 AND IsDefault = 1 THEN '❌ PROBLEMA'
        ELSE '✅ OK'
    END as Estado
FROM EmailConfiguration;
```

---

## 🔧 **Uso del Sistema Hostinger Después de la Corrección**

### **Opción A: Interfaz Web**
1. Ve a: `https://tu-app.com/ConfigurarHostinger`
2. Completa el formulario con tus datos de Hostinger
3. ✅ El sistema ahora manejará correctamente los constraints

### **Opción B: Procedimiento SQL**
```sql
-- Ahora funcionará sin errores
EXEC SP_ConfigurarHostingerEmail
    @EmailUsuario = 'miempresa@midominio.com',
    @ContraseñaPlana = 'mi_contraseña',
    @NombreRemitente = 'Mi Empresa - Sistema',
    @NombrePerfil = 'Hostinger Principal',
    @EstablecerPorDefecto = 1;  -- ✅ Funcionará porque IsActive = 1
```

---

## 🔒 **Cambios Realizados en el Sistema**

### **Script de Tabla Corregido**
- ❌ **Antes:** Insertaba configuraciones inactivas como por defecto
- ✅ **Ahora:** Solo inserta configuraciones activas como por defecto

### **Procedimiento Hostinger Mejorado**
- ✅ Siempre crea configuraciones **ACTIVAS** (`IsActive = 1`)
- ✅ Solo establece `IsDefault = 1` en configuraciones activas
- ✅ Respeta el constraint automáticamente

### **Nuevo Procedimiento Seguro**
- ✅ `sp_SetDefaultEmailConfigurationSafe`: Valida antes de establecer por defecto

---

## 💡 **Reglas para Evitar este Error en el Futuro**

### ✅ **Permitido:**
```sql
-- Configuración activa y por defecto
IsActive = 1, IsDefault = 1  ✅

-- Configuración activa pero no por defecto
IsActive = 1, IsDefault = 0  ✅

-- Configuración inactiva y no por defecto
IsActive = 0, IsDefault = 0  ✅
```

### ❌ **NO Permitido:**
```sql
-- Configuración inactiva como por defecto
IsActive = 0, IsDefault = 1  ❌ ← VIOLA CONSTRAINT
```

---

## 🚀 **Resumen de Pasos**

1. **🔧 Ejecuta:** `corregir_constraint_email_configuration.sql`
2. **✅ Verifica:** Que no hay violaciones del constraint
3. **📧 Configura:** Hostinger usando la interfaz web o SQL
4. **🔐 Encripta:** Las contraseñas en `/EncryptPasswords`
5. **🧪 Prueba:** El envío de correos

---

**¡Listo! Tu sistema de email estará funcionando correctamente después de estos pasos.** 🎉