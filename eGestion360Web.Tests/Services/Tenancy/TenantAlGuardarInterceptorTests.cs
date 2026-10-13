using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Catalogos;
using eGestion360Web.Models.Flota;
using eGestion360Web.Models.Personas;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Tenancy;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Tenancy
{
    /// <summary>
    /// Paso F1.2: <see cref="TenantAlGuardarInterceptor"/> pone el id_tenant a lo que se inserta (de su empresa, de su padre,
    /// de sus hijos o de la petición), nunca deja que cambie y nunca guarda una fila sin dueño. Todos los contextos de
    /// <see cref="BaseDeDatosDePrueba"/> lo llevan, igual que la aplicación.
    /// </summary>
    public class TenantAlGuardarInterceptorTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();

        public void Dispose() => _bd.Dispose();

        private static Cargo NuevoCargo(int idEmpresa, string codigo = "NUEVO") => new()
        {
            IdEmpresa = idEmpresa, Codigo = codigo, Nombre = codigo, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
        };

        private static CondicionPago NuevaCondicion(int idEmpresa, string codigo = "CP1") => new()
        {
            IdEmpresa = idEmpresa, Codigo = codigo, Nombre = codigo, Tipo = "cuotas", NumeroCuotas = 2,
            CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
        };

        // ── Altas: de dónde sale el tenant ──────────────────────────────────

        [Theory]
        [InlineData(DatosBase.EmpresaA, DatosBase.TenantA)]
        [InlineData(DatosBase.EmpresaB, DatosBase.TenantB)]
        public async Task Una_fila_nueva_toma_el_tenant_de_su_empresa(int idEmpresa, int tenantEsperado)
        {
            using (var db = _bd.Crear())
            {
                db.Cargos.Add(NuevoCargo(idEmpresa));
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(tenantEsperado, (await lectura.Cargos.AsNoTracking().SingleAsync(c => c.Codigo == "NUEVO" && c.IdEmpresa == idEmpresa)).IdTenant);
        }

        [Fact]
        public async Task La_empresa_manda_aunque_la_peticion_sea_de_otro_tenant()
        {
            _bd.Tenant.IdTenant = DatosBase.TenantA;   // sesión de la empresa A: el administrador general trabaja sobre la B

            using (var db = _bd.Crear())
            {
                db.Cargos.Add(NuevoCargo(DatosBase.EmpresaB));
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Cargos.AsNoTracking().SingleAsync(c => c.Codigo == "NUEVO")).IdTenant);
        }

        [Fact]
        public async Task El_tenant_que_ya_trae_la_fila_se_respeta()
        {
            using (var db = _bd.Crear())
            {
                var cargo = NuevoCargo(DatosBase.EmpresaA);
                cargo.IdTenant = DatosBase.TenantB;   // alta de empresa: el administrador eligió el tenant
                db.Cargos.Add(cargo);
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Cargos.AsNoTracking().SingleAsync(c => c.Codigo == "NUEVO")).IdTenant);
        }

        [Fact]
        public async Task Una_empresa_nueva_siembra_sus_filas_en_su_propio_tenant_aun_en_el_mismo_guardado()
        {
            using (var db = _bd.Crear())
            {
                var empresa = new eGestion360Web.Models.Empresa
                {
                    IdTenant = DatosBase.TenantB, Codigo = "E9", RazonSocial = "Empresa nueva", PaisIso = "HN", MonedaIso = "HNL",
                    ZonaHoraria = "America/Tegucigalpa", FechaActivacion = DateTime.UtcNow, CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                };
                db.Empresas.Add(empresa);
                await db.SaveChangesAsync();

                db.Cargos.Add(NuevoCargo(empresa.IdEmpresa, "SEMBRADO"));
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Cargos.AsNoTracking().SingleAsync(c => c.Codigo == "SEMBRADO")).IdTenant);
        }

        [Fact]
        public async Task Una_linea_hija_toma_el_tenant_de_su_padre_enlazado_en_el_mismo_guardado()
        {
            using (var db = _bd.Crear())
            {
                var condicion = NuevaCondicion(DatosBase.EmpresaB);
                condicion.Cuotas.Add(new CondicionPagoCuota { NumeroCuota = 1, DiasVencimiento = 30, Porcentaje = 50m });
                condicion.Cuotas.Add(new CondicionPagoCuota { NumeroCuota = 2, DiasVencimiento = 60, Porcentaje = 50m });
                db.CondicionesPago.Add(condicion);
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.CondicionesPago.AsNoTracking().SingleAsync()).IdTenant);
            var cuotas = await lectura.Set<CondicionPagoCuota>().AsNoTracking().ToListAsync();
            Assert.Equal(2, cuotas.Count);
            Assert.All(cuotas, c => Assert.Equal(DatosBase.TenantB, c.IdTenant));
        }

        [Fact]
        public async Task Una_linea_hija_toma_el_tenant_de_su_padre_rastreado_aunque_solo_traiga_su_clave()
        {
            int idCondicion;
            using (var db = _bd.Crear())
            {
                var condicion = NuevaCondicion(DatosBase.EmpresaB);
                db.CondicionesPago.Add(condicion);
                await db.SaveChangesAsync();
                idCondicion = condicion.IdCondicionPago;
            }

            using (var db = _bd.Crear())
            {
                _ = await db.CondicionesPago.FindAsync(idCondicion);   // el padre queda rastreado, como en Permisos.cshtml
                db.Set<CondicionPagoCuota>().Add(new CondicionPagoCuota { IdCondicionPago = idCondicion, NumeroCuota = 1, DiasVencimiento = 30, Porcentaje = 100m });
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Set<CondicionPagoCuota>().AsNoTracking().SingleAsync()).IdTenant);
        }

        [Fact]
        public async Task Una_persona_nueva_toma_el_tenant_de_su_vinculo_y_no_el_de_la_peticion()
        {
            _bd.Tenant.IdTenant = DatosBase.TenantA;

            int idPersona;
            using (var db = _bd.Crear())
            {
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez", dni: "0801199012345").IdPersona;
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == idPersona)).IdTenant);
            Assert.Equal(DatosBase.TenantB, (await lectura.Set<PersonaDocumento>().AsNoTracking().SingleAsync()).IdTenant);
            Assert.Equal(DatosBase.TenantB, (await lectura.PersonaEmpresas.AsNoTracking().SingleAsync()).IdTenant);
            Assert.Equal(DatosBase.TenantB, (await lectura.Empleados.AsNoTracking().SingleAsync()).IdTenant);
        }

        [Fact]
        public async Task Lo_que_no_cuelga_de_nada_toma_el_tenant_de_la_peticion()
        {
            _bd.Tenant.IdTenant = DatosBase.TenantB;

            using (var db = _bd.Crear())
            {
                db.Personas.Add(NuevaPersonaSuelta());
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.Personas.AsNoTracking().SingleAsync()).IdTenant);
        }

        private static Persona NuevaPersonaSuelta() => new()
        {
            Nombres = "Ana", Apellidos = "Mejía", PrimerNombre = "Ana", PrimerApellido = "Mejía",
            CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
        };

        [Fact]
        public void Sin_empresa_ni_padre_ni_peticion_no_se_guarda_y_dice_que_falta()
        {
            _bd.Tenant.IdTenant = null;   // el administrador general sin empresa en sesión

            using var db = _bd.Crear();
            db.Personas.Add(NuevaPersonaSuelta());

            var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            Assert.Contains("sin tenant", error.Message);
            Assert.Contains("Persona", error.Message);

            using var lectura = _bd.Crear();
            Assert.Empty(lectura.Personas.AsNoTracking().ToList());
        }

        [Fact]
        public async Task Una_empresa_que_no_existe_no_se_resuelve_con_el_tenant_de_la_peticion()
        {
            using var db = _bd.Crear();
            db.Cargos.Add(NuevoCargo(777));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("777", error.Message);
        }

        [Fact]
        public void El_guardado_sincrono_tambien_asigna_el_tenant()
        {
            using (var db = _bd.Crear())
            {
                db.Cargos.Add(NuevoCargo(DatosBase.EmpresaB));
                db.SaveChanges();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, lectura.Cargos.AsNoTracking().Single(c => c.Codigo == "NUEVO").IdTenant);
        }

        // ── Modificaciones: el tenant no cambia ─────────────────────────────

        [Fact]
        public async Task Cambiar_el_tenant_de_una_fila_guardada_falla_y_no_guarda_nada()
        {
            int id;
            using (var db = _bd.Crear())
            {
                var cargo = NuevoCargo(DatosBase.EmpresaA);
                db.Cargos.Add(cargo);
                await db.SaveChangesAsync();
                id = cargo.IdCargo;
            }

            using (var db = _bd.Crear())
            {
                var cargo = await db.Cargos.SingleAsync(c => c.IdCargo == id);
                cargo.IdTenant = DatosBase.TenantB;
                cargo.Nombre = "Otro nombre";

                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
                Assert.Contains("no se puede cambiar", error.Message);
            }

            using var lectura = _bd.Crear();
            var guardado = await lectura.Cargos.AsNoTracking().SingleAsync(c => c.IdCargo == id);
            Assert.Equal(DatosBase.TenantA, guardado.IdTenant);
            Assert.Equal("NUEVO", guardado.Nombre);
        }

        [Fact]
        public async Task Una_entidad_que_llega_de_un_formulario_sin_tenant_no_borra_el_de_la_fila()
        {
            int id;
            using (var db = _bd.Crear())
            {
                var cargo = NuevoCargo(DatosBase.EmpresaB);
                db.Cargos.Add(cargo);
                await db.SaveChangesAsync();
                id = cargo.IdCargo;
            }

            using (var db = _bd.Crear())
            {
                // Lo que arma el enlace del formulario: la clave y los campos que se editan, sin id_tenant.
                var delFormulario = NuevoCargo(DatosBase.EmpresaB);
                delFormulario.IdCargo = id;
                delFormulario.Nombre = "Editado";
                db.Entry(delFormulario).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            var guardado = await lectura.Cargos.AsNoTracking().SingleAsync(c => c.IdCargo == id);
            Assert.Equal("Editado", guardado.Nombre);
            Assert.Equal(DatosBase.TenantB, guardado.IdTenant);
        }

        [Fact]
        public async Task Una_modificacion_normal_de_una_fila_guardada_sigue_funcionando()
        {
            using (var db = _bd.Crear())
            {
                db.Cargos.Add(NuevoCargo(DatosBase.EmpresaB));
                await db.SaveChangesAsync();
            }

            using (var db = _bd.Crear())
            {
                (await db.Cargos.SingleAsync(c => c.Codigo == "NUEVO")).Nombre = "Renombrado";
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            var guardado = await lectura.Cargos.AsNoTracking().SingleAsync(c => c.Codigo == "NUEVO");
            Assert.Equal("Renombrado", guardado.Nombre);
            Assert.Equal(DatosBase.TenantB, guardado.IdTenant);
        }

        // ── Tabla mixta: la fila global queda sin tenant ────────────────────

        private static TasaCambio NuevaTasa(int? idEmpresa) => new()
        {
            IdEmpresa = idEmpresa, MonedaOrigen = "USD", MonedaDestino = "HNL", TipoTasa = TasasCambioCatalogo.TipoTasa.Venta,
            Tasa = 26.5m, FechaVigencia = new DateOnly(2026, 10, 1), FechaHoraObtencion = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Fuente = idEmpresa != null ? TasasCambioCatalogo.Fuente.Manual : TasasCambioCatalogo.Fuente.BchXlsx,
            CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
        };

        [Fact]
        public async Task Una_tasa_oficial_sin_empresa_es_global_y_se_queda_sin_tenant_aunque_haya_peticion()
        {
            using (var db = _bd.Crear())
            {
                db.TasasCambio.Add(NuevaTasa(null));
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Null((await lectura.TasasCambio.AsNoTracking().SingleAsync()).IdTenant);
        }

        [Fact]
        public async Task Una_tasa_propia_de_una_empresa_toma_el_tenant_de_esa_empresa()
        {
            using (var db = _bd.Crear())
            {
                db.TasasCambio.Add(NuevaTasa(DatosBase.EmpresaB));
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.TasasCambio.AsNoTracking().SingleAsync()).IdTenant);
        }

        [Fact]
        public async Task Una_tasa_global_no_puede_pasar_a_ser_de_un_tenant()
        {
            using (var db = _bd.Crear())
            {
                db.TasasCambio.Add(NuevaTasa(null));
                await db.SaveChangesAsync();
            }

            using var db2 = _bd.Crear();
            (await db2.TasasCambio.SingleAsync()).IdTenant = DatosBase.TenantA;

            await Assert.ThrowsAsync<InvalidOperationException>(() => db2.SaveChangesAsync());
        }

        // ── Bitácora ────────────────────────────────────────────────────────

        [Fact]
        public async Task La_bitacora_de_una_persona_lleva_el_tenant_de_la_persona_y_no_el_de_la_peticion()
        {
            _bd.Tenant.IdTenant = DatosBase.TenantA;   // el administrador de otra empresa edita a una persona de la B
            var auditoria = new ContextoAuditoriaFijo { Usuario = "ana.admin", IdEmpresa = DatosBase.EmpresaB };

            int idPersona;
            using (var db = _bd.Crear())
            {
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez").IdPersona;
            }

            using (var db = _bd.Crear(new AuditoriaCambiosInterceptor(auditoria)))
            {
                (await db.Personas.SingleAsync(p => p.IdPersona == idPersona)).PrimerNombre = "Luisa";
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            var filas = await lectura.BitacoraCambios.AsNoTracking().ToListAsync();
            Assert.NotEmpty(filas);
            Assert.All(filas, f => Assert.Equal(DatosBase.TenantB, f.IdTenant));
            Assert.DoesNotContain(filas, f => f.Campo == "id_tenant");
        }

        [Fact]
        public async Task Una_fila_de_bitacora_sin_empresa_toma_el_tenant_de_su_persona()
        {
            _bd.Tenant.IdTenant = null;

            int idPersona;
            using (var db = _bd.Crear())
            {
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaB, "Luis", "Pérez").IdPersona;
            }

            using (var db = _bd.Crear())
            {
                db.BitacoraCambios.Add(new BitacoraCambio
                {
                    IdTransaccion = Guid.NewGuid(), IdPersona = idPersona, Entidad = "Users", IdRegistro = 9, Operacion = OperacionesBitacora.Update,
                    Campo = "PersonaId", FechaHora = DateTime.UtcNow, Usuario = "admin", Origen = "app"
                });
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.Equal(DatosBase.TenantB, (await lectura.BitacoraCambios.AsNoTracking().SingleAsync()).IdTenant);
        }
        // ── Referencias entre tenants ───────────────────────────────────────

        [Fact]
        public async Task Un_vinculo_de_una_persona_con_una_empresa_de_otro_tenant_no_se_guarda()
        {
            int idPersona;
            using (var db = _bd.Crear())
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Luis", "Pérez").IdPersona;

            using (var db = _bd.Crear())
            {
                _ = await db.Personas.FindAsync(idPersona);   // la persona (tenant A) queda rastreada
                db.PersonaEmpresas.Add(new PersonaEmpresa
                {
                    IdPersona = idPersona, IdEmpresa = DatosBase.EmpresaB, TipoVinculo = TiposVinculo.Cliente,
                    FechaInicio = new DateOnly(2026, 10, 1), CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                });

                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
                Assert.Contains("de otro tenant", error.Message);
            }

            using var lectura = _bd.Crear();
            Assert.Equal(1, await lectura.PersonaEmpresas.CountAsync());
        }

        [Fact]
        public async Task Un_vinculo_de_una_persona_con_otra_empresa_del_mismo_tenant_si_se_guarda()
        {
            int idPersona;
            using (var db = _bd.Crear())
                idPersona = PersonasDePrueba.Insertar(db, DatosBase.EmpresaA, "Luis", "Pérez").IdPersona;

            using (var db = _bd.Crear())
            {
                _ = await db.Personas.FindAsync(idPersona);
                db.PersonaEmpresas.Add(new PersonaEmpresa
                {
                    IdPersona = idPersona, IdEmpresa = DatosBase.EmpresaA2, TipoVinculo = TiposVinculo.Cliente,
                    FechaInicio = new DateOnly(2026, 10, 1), CreadoPor = "pruebas", FechaCreacion = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            using var lectura = _bd.Crear();
            Assert.All(await lectura.PersonaEmpresas.AsNoTracking().ToListAsync(), v => Assert.Equal(DatosBase.TenantA, v.IdTenant));
        }

    }
}
