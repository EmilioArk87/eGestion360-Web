# 🛢️ Guía de Ejecución para DBeaver

## 🎯 **Diferencias Importantes: DBeaver vs SQL Server Management Studio**

### **Lo que DBeaver maneja diferente:**

❌ **No usa `GO` statements** - Se reemplaza por `;` y bloques separados  
❌ **PRINT no siempre visible** - Usar `SELECT` para ver resultados  
❌ **Procedimientos largos fallan** - Ejecutar por partes  
❌ **Transacciones manuales** - Mejor control de commit/rollback  

### **Ventajas de DBeaver:**
✅ **Mejor editor SQL** - Autocompletado y resaltado  
✅ **Multiplataforma** - Windows, Linux, Mac  
✅ **Conexiones múltiples** - Varios motores de BD  
✅ **Resultados tabulares** - Mejor visualización  

---

## 📋 **Orden de Ejecución Optimizado para DBeaver**

### **Script 1: `create_email_configuration_table.sql`** ⭐ PRINCIPAL

**🔥 EJECUTAR EN 6 BLOQUES SEPARADOS:**

#### **BLOQUE 1: Crear Tabla** 
```sql
-- Seleccionar desde "BLOQUE 1" hasta "FIN BLOQUE 1"
-- Líneas aproximadas: 15-55
```

#### **BLOQUE 2: Crear Índices**
```sql  
-- Seleccionar desde "BLOQUE 2" hasta "FIN BLOQUE 2"
-- Líneas aproximadas: 57-85
```

#### **BLOQUE 3A: Procedimiento 1**
```sql
-- Solo el primer procedimiento (sp_GetActiveEmailConfiguration)
-- EJECUTAR: Desde "PROCEDIMIENTO 1" hasta "FIN PROCEDIMIENTO 1"
```

#### **BLOQUE 3B: Procedimiento 2**
```sql 
-- Solo el segundo procedimiento (sp_SetDefaultEmailConfiguration)
-- EJECUTAR: Desde "PROCEDIMIENTO 2" hasta "FIN PROCEDIMIENTO 2"
```

#### **BLOQUE 3C: Procedimiento 3**
```sql
-- Solo el tercer procedimiento (sp_UpdateEmailTestStats)  
-- EJECUTAR: Desde "PROCEDIMIENTO 3" hasta "FIN BLOQUE 3"
```

#### **BLOQUE 4: Datos Iniciales**
```sql
-- Seleccionar desde "BLOQUE 4" hasta "FIN BLOQUE 4"
-- Líneas aproximadas: 187-225
```

#### **BLOQUE 5: Trigger** 
```sql
-- Seleccionar desde "BLOQUE 5" hasta "FIN BLOQUE 5"
-- Líneas aproximadas: 227-245
```

#### **BLOQUE 6: Verificación**
```sql  
-- Seleccionar desde "BLOQUE 6" hasta el final
-- Líneas aproximadas: 247-fin
```

---

### **Script 2: `corregir_constraint_email_configuration.sql`** (Solo si hay errores)

**🔥 EJECUTAR TODO DE UNA VEZ** - Es auto-contenido y seguro

---

### **Script 3: `setup_hostinger_email_config.sql`**

**🔥 EJECUTAR EN 2 BLOQUES:**

#### **BLOQUE A: Crear Procedimiento**
```sql
-- Todo excepto la última sección de ejemplos
```

#### **BLOQUE B: Ejemplos y Documentación**  
```sql
-- Solo los PRINT con ejemplos de uso
```

---

### **Script 4: `ejecutar_configuracion_hostinger.sql`** ⚠️ PERSONALIZAR

**🔥 PASOS OBLIGATORIOS:**

#### **PASO 1: Personalizar Datos**
```sql
-- CAMBIAR estas líneas con TUS datos reales:
DECLARE @MiEmail = 'TU-EMAIL@TU-DOMINIO.com';           -- 📧 TU EMAIL REAL
DECLARE @MiContraseña = 'TU-CONTRASEÑA-REAL';           -- 🔑 TU CONTRASEÑA REAL  
DECLARE @MiNombreEmpresa = 'TU EMPRESA - Notificaciones'; -- 🏢 TU NOMBRE
```

#### **PASO 2: Ejecutar en 3 Partes**

**Parte A: Diagnóstico**
```sql
-- Desde el inicio hasta "EJECUTANDO CONFIGURACIÓN"
-- Ver qué configuraciones ya existen
```

**Parte B: Configuración**
```sql  
-- Solo la línea EXEC SP_ConfigurarHostingerEmail
-- La parte más importante
```

**Parte C: Verificación**
```sql
-- Desde "CONFIGURACIÓN DESPUÉS" hasta el final
-- Ver que se creó correctamente
```

---

### **Script 5: `validacion_completa_emails.sql`**

**🔥 EJECUTAR TODO** - Es solo consultas de verificación

---

## 🔍 **Técnica de Ejecución en DBeaver**

### **1. Preparación:**
```
1️⃣ Abrir DBeaver
2️⃣ Conectar a SQL Server
3️⃣ Verificar base de datos correcta
4️⃣ Abrir script
```

### **2. Seleccionar Bloques:**
```
🖱️ MÉTODO 1: Seleccionar con mouse desde comentario inicio hasta comentario fin
⌨️ MÉTODO 2: Usar Ctrl+Shift+End para seleccionar hasta final de bloque  
🔍 MÉTODO 3: Buscar "-- FIN BLOQUE X" para encontrar límites
```

### **3. Ejecutar:**
```
🔥 OPCIÓN 1: Ctrl+Enter (ejecutar selección)
🔥 OPCIÓN 2: Botón ► en toolbar  
🔥 OPCIÓN 3: Click derecho → Execute Statement
```

### **4. Verificar Resultados:**
```sql
-- Después de cada bloque, ejecutar:
SELECT 
    OBJECT_NAME(object_id) as Objeto,
    type_desc as Tipo,
    create_date as Creado,
    modify_date as Modificado
FROM sys.objects 
WHERE name LIKE '%Email%' OR name LIKE '%sp_%'
ORDER BY create_date DESC;
```

---

## ⚠️ **Errores Comunes en DBeaver y Soluciones**

### **Error: "Incorrect syntax near 'GO'"**
```sql
-- ❌ INCORRECTO en DBeaver:
CREATE TABLE...
GO

-- ✅ CORRECTO en DBeaver: 
CREATE TABLE...;  -- Sin GO
```

### **Error: "PRINT statement not visible"**
```sql
-- ❌ No se ve resultado:
PRINT 'Creado exitosamente';

-- ✅ Visible en DBeaver:
SELECT 'Creado exitosamente' AS Resultado;
```

### **Error: "Cannot execute multiple batches"**
```sql
-- ❌ PROBLEMA: Ejecutar todo el script de una vez
-- ✅ SOLUCIÓN: Ejecutar bloque por bloque como se indica
```

### **Error: "Transaction not committed"**
```sql
-- En DBeaver, a veces hay que commit manual:
COMMIT;
-- O usar autocommit en configuración
```

---

## 📊 **Verification Queries for DBeaver**

### **Después de Cada Script:**
```sql
-- Ver tablas creadas
SELECT TABLE_NAME, TABLE_TYPE 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME LIKE '%Email%';

-- Ver procedimientos creados  
SELECT ROUTINE_NAME, ROUTINE_TYPE
FROM INFORMATION_SCHEMA.ROUTINES 
WHERE ROUTINE_NAME LIKE '%Email%';

-- Ver datos insertados
SELECT COUNT(*) AS Total, 
       SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END) AS Activas
FROM EmailConfiguration;
```

---

## 🚀 **Flujo Completo para DBeaver**

### **Sesión 1: Estructura Base (10 minutos)**
```
1️⃣ create_email_configuration_table.sql (6 bloques)
2️⃣ Verificar tabla creada  
3️⃣ Verificar procedimientos creados
4️⃣ commit;
```

### **Sesión 2: Procedimiento Hostinger (5 minutos)**  
```
1️⃣ setup_hostinger_email_config.sql (2 bloques)
2️⃣ Verificar procedimiento SP_ConfigurarHostingerEmail
3️⃣ commit;
```

### **Sesión 3: Configuración Real (5 minutos)**
```  
1️⃣ Personalizar ejecutar_configuracion_hostinger.sql con tus datos
2️⃣ Ejecutar en 3 partes
3️⃣ Verificar configuración creada
4️⃣ commit;
```

### **Sesión 4: Validación (2 minutos)**
```
1️⃣ validacion_completa_emails.sql (todo)  
2️⃣ Revisar resultados
3️⃣ Ir a /EncryptPasswords en la web
```

---

## 💡 **Tips Específicos para DBeaver**

### **Configuración Recomendada:**
```
🔧 Settings → Editors → SQL → Auto-commit: ON
🔧 Settings → Editors → SQL → Max result sets: 1000  
🔧 Settings → Connections → [Tu conexión] → Driver properties → selectMethod=cursor
```

### **Atajos Útiles:**
```
Ctrl+Enter: Ejecutar selección/línea actual
Ctrl+Shift+Enter: Ejecutar script completo 
Ctrl+/: Comentar/descomentar
F5: Refrescar metadatos
Ctrl+Space: Autocompletar
```

### **Ventana de Resultados:**
```  
📊 Results Tab: Ve los datos devueltos
📝 Output Tab: Ve mensajes PRINT y errores
🔍 Execution Log: Ve historial de ejecución
```

---

**¡Listo para DBeaver! 🚀** 

El script principal ya está optimizado con bloques claramente marcados. Solo sigue la secuencia de bloques y tendrás éxito.