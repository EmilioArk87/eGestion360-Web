# eGestion360-Web — Índice de Documentación

Proyecto web de gestión de flota de transporte (ASP.NET Core 8 / Razor Pages / SQL Server).

---

## Estándares y Arquitectura

| Documento | Descripción |
|-----------|-------------|
| [ESTANDARES_ERP.md](ESTANDARES_ERP.md) | Convenciones globales del ERP: stack real, arquitectura por capas, multitenant, nombres de BD, auditoría y control de cambios |
| [Arquitectura/README.md](Arquitectura/README.md) | Arquitectura SaaS multi-tenant aprobada (2026-10-10): resumen, roadmap F0–F9 y registros de decisión ADR-001 a ADR-014 |
| [PRUEBAS_AUTOMATIZADAS.md](PRUEBAS_AUTOMATIZADAS.md) | Proyecto de pruebas xUnit: cómo correrlas, qué cubren, la base SQLite de prueba y sus límites |

## Contabilidad

| Documento | Descripción |
|-----------|-------------|
| [PROMPT_MAESTRO_CONTABILIDAD.md](PROMPT_MAESTRO_CONTABILIDAD.md) | Prompt maestro adaptado + diseño concreto del núcleo contable (`ct_*`), integración con el outbox de eventos y fundamento legal (Honduras) |

## Flujos y Guías de Usuario

| Documento | Descripción |
|-----------|-------------|
| [FLUJO_AUTENTICACION.md](FLUJO_AUTENTICACION.md) | Login, recuperación y cambio de contraseña, gestión de usuarios |
| [FLUJO_OPERACION_DIARIA.md](FLUJO_OPERACION_DIARIA.md) | Odómetro, combustible, salarios, mantenimiento, repuestos y seguros |

## KPI — Costo por Kilómetro

| Documento | Descripción |
|-----------|-------------|
| [Módulo KPI de Costo por Kilómetro — Resumen Funcional.md](Módulo%20KPI%20de%20Costo%20por%20Kilómetro%20—%20Resumen%20Funcional.md) | Resumen ejecutivo del módulo KPI |
| [Kpi excel documentacion tecnica.md](Kpi%20excel%20documentacion%20tecnica.md) | Especificación técnica detallada del cálculo KPI |

## Configuración de Email

| Documento | Descripción |
|-----------|-------------|
| [CONFIGURACION_EMAIL.md](CONFIGURACION_EMAIL.md) | Guía general de configuración de email corporativo |
| [HOSTINGER_EMAIL_SETUP.md](HOSTINGER_EMAIL_SETUP.md) | Setup específico para Hostinger.es |
| [EMAIL_SETUP.md](EMAIL_SETUP.md) | Guía alternativa de configuración SMTP |
| [VALIDACION_EMAILS_GUIA.md](VALIDACION_EMAILS_GUIA.md) | Validación y prueba de envío de emails |

## Documentación de Vistas

| Documento | Descripción |
|-----------|-------------|
| [Vistas/Flota_ControlSalidas.md](Vistas/Flota_ControlSalidas.md) | Control de salidas y entradas en garita: vista en vivo e historial |
| [Vistas/Flota_Catalogos_Personas.md](Vistas/Flota_Catalogos_Personas.md) | Personal (personas maestras por empresa): listado, nuevo, editar e historial de cambios |
| [Vistas/Catalogos_Clientes.md](Vistas/Catalogos_Clientes.md) | Clientes: alta con ficha de persona (reutilizada entre empresas), edición, baja y eliminación |
| [Vistas/Admin_Personas.md](Vistas/Admin_Personas.md) | Personas del sistema (solo administrador general): todas las personas con sus empresas y roles, edición de datos personales e historial completo |
| [Vistas/Admin_Usuarios.md](Vistas/Admin_Usuarios.md) | Usuarios y su persona: crear y editar usuarios vinculados a la persona que los usa (script 021) |
| [Vistas/_PlantillaVista.md](Vistas/_PlantillaVista.md) | Plantilla para documentar una vista nueva |

## Base de Datos

| Documento | Descripción |
|-----------|-------------|
| [INDICE_SCRIPTS_SQL.md](INDICE_SCRIPTS_SQL.md) | Orden de ejecución de scripts SQL |
| [DBEAVER_EJECUCION_GUIA.md](DBEAVER_EJECUCION_GUIA.md) | Guía de ejecución de scripts en DBeaver |
| [SCRIPTS_ORDEN_DBEAVER.md](SCRIPTS_ORDEN_DBEAVER.md) | Orden detallado de scripts para DBeaver |
| [SOLUCION_ERROR_CONSTRAINT.md](SOLUCION_ERROR_CONSTRAINT.md) | Solución al error de constraint en EmailConfiguration |
| [USUARIOS_TABLE_MAPPING.md](USUARIOS_TABLE_MAPPING.md) | Mapeo de columnas de la tabla `usuarios` |

## Historial Técnico

| Documento | Descripción |
|-----------|-------------|
| [CONVERSION.md](CONVERSION.md) | Migración del proyecto de MVC a Razor Pages |
