using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Auditoria
{
    public class AuditoriaCambiosInterceptorTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly ContextoAuditoriaFijo _quien = new() { Usuario = "ana.admin", IdEmpresa = DatosBase.EmpresaB };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext ConAuditoria() => _bd.Crear(new AuditoriaCambiosInterceptor(_quien));
        private ApplicationDbContext SinAuditoria() => _bd.Crear();

        private static Persona NuevaPersona(string nombre = "Luis", string apellido = "Pérez", string? dni = "0801199012345")
        {
            var ahora = DateTime.UtcNow;
            var persona = new Persona
            {
                IdEmpresa = DatosBase.EmpresaB, Nombres = nombre, Apellidos = apellido,
                PrimerNombre = nombre, PrimerApellido = apellido, Cargo = "MECANICO",
                TipoDocumento = "DNI", Documento = dni ?? "X", CreadoPor = "pruebas", FechaCreacion = ahora
            };
            var vinculo = new PersonaEmpresa
            {
                IdEmpresa = DatosBase.EmpresaB, TipoVinculo = TiposVinculo.Empleado, FechaInicio = new DateOnly(2026, 1, 15),
                CreadoPor = "pruebas", FechaCreacion = ahora,
                Empleado = new Empleado { IdEmpresa = DatosBase.EmpresaB, CodigoInterno = "31234", Cargo = "MECANICO", TarifaDiaria = 350m, MonedaTarifa = "HNL", CreadoPor = "pruebas", FechaCreacion = ahora }
            };
            persona.Vinculos.Add(vinculo);
            if (dni != null)
                persona.Documentos.Add(new PersonaDocumento { TipoDocumento = "DNI", PaisEmisor = "HN", Numero = dni, EsPrincipal = true, CreadoPor = "pruebas", FechaCreacion = ahora });
            return persona;
        }

        private static Dictionary<string, JsonElement> Json(string? texto) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(texto!)!;

        // ── Altas ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Un_alta_registra_una_fila_por_cada_registro_creado_con_su_id_y_en_una_sola_transaccion()
        {
            int idPersona;
            using (var db = ConAuditoria())
            {
                var persona = NuevaPersona();
                db.Personas.Add(persona);
                await db.SaveChangesAsync();
                idPersona = persona.IdPersona;
            }

            using var lectura = SinAuditoria();
            var filas = await lectura.BitacoraCambios.AsNoTracking().OrderBy(b => b.IdBitacora).ToListAsync();

            Assert.Equal(4, filas.Count);
            Assert.All(filas, f => { Assert.Equal("INSERT", f.Operacion); Assert.Null(f.Campo); Assert.Equal("ana.admin", f.Usuario); Assert.Equal("app", f.Origen); Assert.Equal(idPersona, f.IdPersona); });
            Assert.Single(filas.Select(f => f.IdTransaccion).Distinct());
            Assert.Equal(new[] { "empleados", "persona_documentos", "persona_empresa", "personas" }, filas.Select(f => f.Entidad).Order().ToArray());

            var persona_ = filas.Single(f => f.Entidad == "personas");
            Assert.Equal(idPersona, persona_.IdRegistro);
            Assert.Equal(DatosBase.EmpresaB, persona_.IdEmpresa);

            var vinculo = await lectura.PersonaEmpresas.AsNoTracking().SingleAsync();
            Assert.Equal(vinculo.IdPersonaEmpresa, filas.Single(f => f.Entidad == "persona_empresa").IdRegistro);
            var empleado = await lectura.Empleados.AsNoTracking().SingleAsync();
            Assert.Equal(empleado.IdEmpleado, filas.Single(f => f.Entidad == "empleados").IdRegistro);
        }

        [Fact]
        public async Task La_foto_del_alta_lleva_las_columnas_con_valor_y_no_las_de_auditoria_ni_las_claves()
        {
            using (var db = ConAuditoria())
            {
                db.Personas.Add(NuevaPersona());
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var foto = Json((await lectura.BitacoraCambios.AsNoTracking().SingleAsync(b => b.Entidad == "personas")).ValorNuevo);

            Assert.Equal("Luis", foto["primer_nombre"].GetString());
            Assert.Equal("Pérez", foto["primer_apellido"].GetString());
            Assert.Equal("pendiente", foto["estado_identidad"].GetString());
            Assert.True(foto["activo"].GetBoolean());
            foreach (var ausente in new[] { "id_persona", "token_concurrencia", "creado_por", "fecha_creacion", "modificado_por", "fecha_modificacion", "segundo_nombre", "sexo" })
                Assert.False(foto.ContainsKey(ausente), $"No debería estar {ausente}");

            var empleado = Json((await lectura.BitacoraCambios.AsNoTracking().SingleAsync(b => b.Entidad == "empleados")).ValorNuevo);
            Assert.Equal("31234", empleado["codigo_interno"].GetString());
            Assert.Equal(350m, empleado["tarifa_diaria"].GetDecimal());
            Assert.Equal("HNL", empleado["moneda_tarifa"].GetString());
        }

        [Fact]
        public async Task El_numero_de_documento_se_guarda_enmascarado_y_no_aparece_completo_en_ninguna_fila()
        {
            using (var db = ConAuditoria())
            {
                db.Personas.Add(NuevaPersona(dni: "0801199012345"));
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var filas = await lectura.BitacoraCambios.AsNoTracking().ToListAsync();

            var documento = Json(filas.Single(f => f.Entidad == "persona_documentos").ValorNuevo);
            Assert.Equal("*********2345", documento["numero"].GetString());
            Assert.Equal("*********2345", Json(filas.Single(f => f.Entidad == "personas").ValorNuevo)["documento"].GetString());
            Assert.DoesNotContain(filas, f => (f.ValorNuevo ?? "").Contains("0801199012345") || (f.ValorAnterior ?? "").Contains("0801199012345"));
        }

        [Fact]
        public void Un_alta_con_SaveChanges_sincrono_tambien_se_registra()
        {
            using (var db = ConAuditoria())
            {
                db.Personas.Add(NuevaPersona());
                db.SaveChanges();
            }

            using var lectura = SinAuditoria();
            Assert.Equal(4, lectura.BitacoraCambios.Count());
        }

        // ── Modificaciones ──────────────────────────────────────────────────

        [Fact]
        public async Task Una_modificacion_registra_una_fila_por_campo_con_el_valor_anterior_y_el_nuevo()
        {
            int id;
            using (var db = SinAuditoria()) { var p = NuevaPersona(); db.Personas.Add(p); await db.SaveChangesAsync(); id = p.IdPersona; }

            using (var db = ConAuditoria())
            {
                var persona = await db.Personas.SingleAsync(p => p.IdPersona == id);
                persona.PrimerNombre = "Luis Alberto";
                persona.Telefono = "98765432";
                persona.ModificadoPor = "ana.admin";            // no se registra
                persona.FechaModificacion = DateTime.UtcNow;    // no se registra
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var filas = await lectura.BitacoraCambios.AsNoTracking().OrderBy(b => b.Campo).ToListAsync();

            Assert.Equal(new[] { "primer_nombre", "telefono" }, filas.Select(f => f.Campo).ToArray());
            Assert.All(filas, f => { Assert.Equal("UPDATE", f.Operacion); Assert.Equal("personas", f.Entidad); Assert.Equal(id, f.IdRegistro); Assert.Equal(id, f.IdPersona); });
            var nombre = filas.Single(f => f.Campo == "primer_nombre");
            Assert.Equal("Luis", nombre.ValorAnterior);
            Assert.Equal("Luis Alberto", nombre.ValorNuevo);
            var telefono = filas.Single(f => f.Campo == "telefono");
            Assert.Null(telefono.ValorAnterior);
            Assert.Equal("98765432", telefono.ValorNuevo);
        }

        [Fact]
        public async Task Asignar_el_mismo_valor_no_registra_nada()
        {
            int id;
            using (var db = SinAuditoria()) { var p = NuevaPersona(); db.Personas.Add(p); await db.SaveChangesAsync(); id = p.IdPersona; }

            using (var db = ConAuditoria())
            {
                var persona = await db.Personas.SingleAsync(p => p.IdPersona == id);
                persona.PrimerNombre = "Luis";
                db.Entry(persona).Property(p => p.PrimerNombre).IsModified = true;   // marcado, pero con el mismo valor
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            Assert.Equal(0, await lectura.BitacoraCambios.CountAsync());
        }

        [Fact]
        public async Task Los_cambios_de_fechas_y_booleanos_se_escriben_en_un_formato_estable()
        {
            int id;
            using (var db = SinAuditoria()) { var p = NuevaPersona(); db.Personas.Add(p); await db.SaveChangesAsync(); id = p.IdPersona; }

            using (var db = ConAuditoria())
            {
                var persona = await db.Personas.SingleAsync(p => p.IdPersona == id);
                persona.FechaNacimiento = new DateOnly(1990, 5, 10);
                persona.Activo = false;
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var filas = await lectura.BitacoraCambios.AsNoTracking().ToListAsync();

            Assert.Equal("1990-05-10", filas.Single(f => f.Campo == "fecha_nacimiento").ValorNuevo);
            var activo = filas.Single(f => f.Campo == "activo");
            Assert.Equal("true", activo.ValorAnterior);
            Assert.Equal("false", activo.ValorNuevo);
        }

        [Fact]
        public async Task Cambiar_el_documento_largo_de_la_persona_lo_registra_enmascarado()
        {
            int id;
            using (var db = SinAuditoria()) { var p = NuevaPersona(dni: "0801199012345"); db.Personas.Add(p); await db.SaveChangesAsync(); id = p.IdPersona; }

            using (var db = ConAuditoria())
            {
                var persona = await db.Personas.SingleAsync(p => p.IdPersona == id);
                persona.Documento = "0801199099999";
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var fila = await lectura.BitacoraCambios.AsNoTracking().SingleAsync(f => f.Campo == "documento");
            Assert.Equal("*********2345", fila.ValorAnterior);
            Assert.Equal("*********9999", fila.ValorNuevo);
        }

        [Fact]
        public async Task Un_cambio_en_el_empleado_registra_a_que_persona_pertenece_aunque_no_se_cargue_el_vinculo()
        {
            int idPersona;
            using (var db = SinAuditoria()) { var p = NuevaPersona(); db.Personas.Add(p); await db.SaveChangesAsync(); idPersona = p.IdPersona; }

            using (var db = ConAuditoria())
            {
                var empleado = await db.Empleados.SingleAsync();   // sin Include del vínculo
                empleado.Cargo = "SUPERVISOR";
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var fila = await lectura.BitacoraCambios.AsNoTracking().SingleAsync();
            Assert.Equal("empleados", fila.Entidad);
            Assert.Equal("cargo", fila.Campo);
            Assert.Equal("MECANICO", fila.ValorAnterior);
            Assert.Equal("SUPERVISOR", fila.ValorNuevo);
            Assert.Equal(idPersona, fila.IdPersona);
            Assert.Equal(DatosBase.EmpresaB, fila.IdEmpresa);
        }

        [Fact]
        public async Task Terminar_un_vinculo_registra_la_fecha_de_fin()
        {
            using (var db = SinAuditoria()) { db.Personas.Add(NuevaPersona()); await db.SaveChangesAsync(); }

            using (var db = ConAuditoria())
            {
                var vinculo = await db.PersonaEmpresas.SingleAsync();
                vinculo.FechaFin = new DateOnly(2026, 9, 30);
                vinculo.MotivoFin = "Renuncia";
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var filas = await lectura.BitacoraCambios.AsNoTracking().OrderBy(f => f.Campo).ToListAsync();
            Assert.Equal(new[] { "fecha_fin", "motivo_fin" }, filas.Select(f => f.Campo).ToArray());
            Assert.Equal("2026-09-30", filas[0].ValorNuevo);
        }

        // ── Bajas físicas ───────────────────────────────────────────────────

        [Fact]
        public async Task Una_baja_fisica_registra_la_foto_del_registro_borrado()
        {
            using (var db = SinAuditoria()) { db.Personas.Add(NuevaPersona()); await db.SaveChangesAsync(); }

            using (var db = ConAuditoria())
            {
                db.Empleados.Remove(await db.Empleados.SingleAsync());
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var fila = await lectura.BitacoraCambios.AsNoTracking().SingleAsync();
            Assert.Equal("DELETE", fila.Operacion);
            Assert.Equal("empleados", fila.Entidad);
            Assert.Null(fila.Campo);
            Assert.Null(fila.ValorNuevo);
            Assert.Equal("31234", Json(fila.ValorAnterior)["codigo_interno"].GetString());
        }

        // ── Lo que no se audita ─────────────────────────────────────────────

        [Fact]
        public async Task Los_cambios_en_otras_entidades_no_generan_filas()
        {
            using (var db = ConAuditoria())
            {
                var cargo = await db.Cargos.FirstAsync(c => c.IdEmpresa == DatosBase.EmpresaB);
                cargo.Nombre = "Otro nombre";
                db.Cargos.Add(new Cargo { IdEmpresa = DatosBase.EmpresaB, Codigo = "NUEVO", Nombre = "Nuevo", CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            Assert.Equal(0, await lectura.BitacoraCambios.CountAsync());
        }

        [Fact]
        public async Task La_propia_bitacora_no_se_audita()
        {
            using (var db = ConAuditoria())
            {
                db.Personas.Add(NuevaPersona());
                await db.SaveChangesAsync();
                await db.SaveChangesAsync();   // un segundo guardado sin cambios no agrega nada
            }

            using var lectura = SinAuditoria();
            Assert.Equal(4, await lectura.BitacoraCambios.CountAsync());
            Assert.DoesNotContain(await lectura.BitacoraCambios.ToListAsync(), f => f.Entidad == "bitacora_cambios");
        }

        // ── Atomicidad ──────────────────────────────────────────────────────

        [Fact]
        public async Task Si_el_guardado_falla_no_queda_ni_el_cambio_ni_su_registro()
        {
            using (var db = ConAuditoria())
            {
                var persona = NuevaPersona();
                // Nacionalidad inexistente (clave foránea hacia catalogo_paises): obliga a que falle el guardado.
                persona.PaisNacionalidad = "ZZ";
                db.Personas.Add(persona);

                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            using var lectura = SinAuditoria();
            Assert.Equal(0, await lectura.Personas.CountAsync());
            Assert.Equal(0, await lectura.PersonaEmpresas.CountAsync());
            Assert.Equal(0, await lectura.BitacoraCambios.CountAsync());
        }

        [Fact]
        public async Task Con_una_transaccion_abierta_por_quien_llama_todo_se_revierte_junto()
        {
            using (var db = ConAuditoria())
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                db.Personas.Add(NuevaPersona());
                await db.SaveChangesAsync();

                Assert.Equal(4, await db.BitacoraCambios.CountAsync());   // ya está dentro de la transacción de quien llama
                await tx.RollbackAsync();
            }

            using var lectura = SinAuditoria();
            Assert.Equal(0, await lectura.Personas.CountAsync());
            Assert.Equal(0, await lectura.BitacoraCambios.CountAsync());
        }

        [Fact]
        public async Task Con_una_transaccion_abierta_por_quien_llama_la_interceptora_no_la_cierra()
        {
            using (var db = ConAuditoria())
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                db.Personas.Add(NuevaPersona());
                await db.SaveChangesAsync();
                Assert.NotNull(db.Database.CurrentTransaction);   // sigue abierta
                await tx.CommitAsync();
            }

            using var lectura = SinAuditoria();
            Assert.Equal(1, await lectura.Personas.CountAsync());
            Assert.Equal(4, await lectura.BitacoraCambios.CountAsync());
        }

        [Fact]
        public async Task Despues_de_un_alta_el_contexto_queda_sin_transaccion_pendiente()
        {
            using var db = ConAuditoria();
            db.Personas.Add(NuevaPersona());
            await db.SaveChangesAsync();

            Assert.Null(db.Database.CurrentTransaction);
        }

        // ── Contexto de auditoría ───────────────────────────────────────────

        [Fact]
        public async Task El_usuario_y_el_origen_salen_del_contexto_de_auditoria()
        {
            _quien.Usuario = "script.migracion";
            _quien.Origen = "script:099";
            _quien.IdEmpresa = null;

            using (var db = ConAuditoria())
            {
                db.Personas.Add(NuevaPersona());
                await db.SaveChangesAsync();
            }

            using var lectura = SinAuditoria();
            var filas = await lectura.BitacoraCambios.AsNoTracking().ToListAsync();
            Assert.All(filas, f => { Assert.Equal("script.migracion", f.Usuario); Assert.Equal("script:099", f.Origen); });
            // Sin empresa en la sesión, la fila de la persona toma la empresa de su columna legada.
            Assert.Equal(DatosBase.EmpresaB, filas.Single(f => f.Entidad == "personas").IdEmpresa);
        }
    }
}
