using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Auditoria;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Auditoria
{
    /// <summary>
    /// F1.8b (ADR-030, D73): los cambios de usuarios, roles, permisos de un rol, módulos, accesos y empresas dejan su fila de auditoría de seguridad
    /// en el mismo guardado y la misma transacción. Con el interceptor real sobre SQLite: qué fila queda, en qué tabla, con qué datos y sin qué
    /// secretos; y que si la fila no se puede armar, el cambio tampoco se guarda.
    /// </summary>
    public class AuditoriaDeAdministracionTests : IDisposable
    {
        private static readonly DateTime Ahora = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
        private const string HashOriginal = "HASH-ORIGINAL-QUE-NUNCA-SE-GUARDA";

        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly RelojFijo _reloj = RelojFijo.PorDefecto();
        private readonly ContextoAuditoriaFijo _quien = new()
        {
            Usuario = "ana.admin", IdEmpresa = DatosBase.EmpresaA, IdCuenta = 10, IdCorrelacion = "corr-77",
            Ip = "203.0.113.7", Agente = "Navegador de pruebas", EsOperador = false,
        };

        public void Dispose() => _bd.Dispose();

        private ApplicationDbContext Auditado(IContextoAuditoria? quien = null)
        {
            var q = quien ?? _quien;
            return _bd.Crear(new AuditoriaCambiosInterceptor(q, new ConstructorDeEventosDeAdministracion(q, _bd.Resolvedor, _reloj)));
        }

        private List<AuditoriaSeguridad> DelTenant(string? tipo = null)
        {
            using var db = _bd.Crear();
            return db.AuditoriaSeguridad.AsNoTracking().Where(a => tipo == null || a.Tipo == tipo).OrderBy(a => a.IdEvento).ToList();
        }

        private List<AuditoriaPlataforma> DeLaPlataforma(string? tipo = null)
        {
            using var db = _bd.Crear();
            return db.AuditoriaPlataforma.AsNoTracking().Where(a => tipo == null || a.Tipo == tipo).OrderBy(a => a.IdEvento).ToList();
        }

        private static JsonElement Detalle(string? json) => JsonDocument.Parse(json!).RootElement;

        private static User NuevoUsuario(string nombre = "ana.perez", int? idEmpresa = DatosBase.EmpresaA) => new()
        {
            Username = nombre, Email = nombre + "@prueba.local", Password = HashOriginal, Role = "empresa_user", IsActive = true, EmpresaId = idEmpresa,
        };

        private async Task<int> CrearUsuarioAsync(IContextoAuditoria? quien = null, int? idEmpresa = DatosBase.EmpresaA)
        {
            using var db = Auditado(quien);
            var usuario = NuevoUsuario(idEmpresa: idEmpresa);
            db.Users.Add(usuario);
            await db.SaveChangesAsync();
            return usuario.Id;
        }

        // ── Usuarios ────────────────────────────────────────────────────────

        [Fact]
        public async Task Un_usuario_nuevo_deja_su_fila_en_el_tenant_con_quien_lo_hizo_y_sin_la_clave()
        {
            var id = await CrearUsuarioAsync();

            var fila = Assert.Single(DelTenant());
            Assert.Equal(TiposDeEvento.UsuarioCreado, fila.Tipo);
            Assert.Equal(ResultadosDeEvento.Ok, fila.Resultado);
            Assert.Equal(DatosBase.TenantA, fila.IdTenant);
            Assert.Equal(DatosBase.EmpresaA, fila.IdEmpresa);
            Assert.Equal("usuario", fila.ObjetoTipo);
            Assert.Equal(id.ToString(), fila.ObjetoId);
            Assert.Equal(10, fila.IdCuenta);
            Assert.Equal("ana.admin", fila.Usuario);
            Assert.False(fila.EsOperador);
            Assert.Equal("203.0.113.7", fila.Ip);
            Assert.Equal("Navegador de pruebas", fila.Agente);
            Assert.Equal("corr-77", fila.IdCorrelacion);
            Assert.Equal(_reloj.GetUtcNow().UtcDateTime, fila.FechaHora);

            var detalle = Detalle(fila.Detalle);
            Assert.Equal("ana.perez", detalle.GetProperty("usuario").GetString());
            Assert.Equal("empresa_user", detalle.GetProperty("rol").GetString());
            Assert.DoesNotContain(HashOriginal, fila.Detalle);
            Assert.DoesNotContain("ana.perez@prueba.local", fila.Detalle);   // el correo tampoco se guarda
            Assert.Empty(DeLaPlataforma());                                  // quien lo hizo no es del personal de la plataforma
        }

        [Fact]
        public async Task Lo_que_hace_el_personal_de_la_plataforma_en_un_tenant_queda_tambien_en_la_plataforma()
        {
            var operador = new ContextoAuditoriaFijo { Usuario = "soporte", IdCuenta = 1, IdCorrelacion = "corr-op", EsOperador = true, Ip = "198.51.100.9" };

            var id = await CrearUsuarioAsync(operador);

            var deTenant = Assert.Single(DelTenant());
            var dePlataforma = Assert.Single(DeLaPlataforma());
            Assert.True(deTenant.EsOperador);
            Assert.Equal(TiposDeEvento.UsuarioCreado, dePlataforma.Tipo);
            Assert.Equal(DatosBase.TenantA, dePlataforma.IdTenant);   // el tenant sobre el que se actuó
            Assert.Equal(id.ToString(), dePlataforma.ObjetoId);
            Assert.Equal(deTenant.IdCorrelacion, dePlataforma.IdCorrelacion);
        }

        [Fact]
        public async Task Un_usuario_sin_empresa_no_tiene_tenant_y_su_fila_va_solo_a_la_plataforma()
        {
            var operador = new ContextoAuditoriaFijo { Usuario = "soporte", IdCuenta = 1, EsOperador = true };

            await CrearUsuarioAsync(operador, idEmpresa: null);

            Assert.Empty(DelTenant());
            var fila = Assert.Single(DeLaPlataforma());
            Assert.Null(fila.IdTenant);
            Assert.Null(fila.IdEmpresa);
            Assert.Equal(TiposDeEvento.UsuarioCreado, fila.Tipo);
        }

        [Fact]
        public async Task Editar_un_usuario_deja_el_antes_y_el_despues_de_lo_que_cambio_y_solo_dice_que_la_clave_y_el_correo_cambiaron()
        {
            var id = await CrearUsuarioAsync();

            using (var db = Auditado())
            {
                var usuario = db.Users.Single(u => u.Id == id);
                usuario.Role = "empresa_admin";
                usuario.IsActive = false;
                usuario.Password = "OTRO-HASH-SECRETO";
                usuario.Email = "nuevo.correo@prueba.local";
                await db.SaveChangesAsync();
            }

            var fila = Assert.Single(DelTenant(TiposDeEvento.UsuarioEditado));
            var detalle = Detalle(fila.Detalle);
            Assert.Equal("empresa_user", detalle.GetProperty("rol")[0].GetString());
            Assert.Equal("empresa_admin", detalle.GetProperty("rol")[1].GetString());
            Assert.Equal("ana.perez", detalle.GetProperty("usuario").GetString());
            Assert.True(detalle.GetProperty("activo")[0].GetBoolean());
            Assert.False(detalle.GetProperty("activo")[1].GetBoolean());
            Assert.True(detalle.GetProperty("claveCambiada").GetBoolean());
            Assert.True(detalle.GetProperty("correoCambiado").GetBoolean());
            Assert.Equal(id.ToString(), fila.ObjetoId);
            foreach (var secreto in new[] { HashOriginal, "OTRO-HASH-SECRETO", "nuevo.correo@prueba.local", "ana.perez@prueba.local" })
                Assert.DoesNotContain(secreto, fila.Detalle);
        }

        [Fact]
        public async Task Un_cambio_que_no_es_de_seguridad_no_deja_fila()
        {
            var id = await CrearUsuarioAsync();

            using (var db = Auditado())
            {
                db.Users.Single(u => u.Id == id).CreatedAt = Ahora;
                await db.SaveChangesAsync();
            }

            Assert.Empty(DelTenant(TiposDeEvento.UsuarioEditado));
        }

        [Fact]
        public async Task Borrar_un_usuario_deja_su_baja_con_el_nombre_y_en_el_tenant_donde_estaba()
        {
            var id = await CrearUsuarioAsync();

            using (var db = Auditado())
            {
                db.Users.Remove(db.Users.Single(u => u.Id == id));
                await db.SaveChangesAsync();
            }

            var fila = Assert.Single(DelTenant(TiposDeEvento.UsuarioEliminado));
            Assert.Equal(DatosBase.TenantA, fila.IdTenant);
            Assert.Equal(id.ToString(), fila.ObjetoId);
            Assert.Equal("ana.perez", Detalle(fila.Detalle).GetProperty("usuario").GetString());
        }

        // ── Roles y sus permisos ────────────────────────────────────────────

        private static Permiso NuevoPermiso(string codigo, int orden) => new()
        {
            Codigo = codigo, Modulo = "flota", Recurso = "vehiculos", Accion = codigo[(codigo.LastIndexOf('.') + 1)..], Ambito = "modulo",
            Descripcion = codigo, Orden = orden, CreadoPor = "pruebas", FechaCreacion = Ahora,
        };

        [Fact]
        public async Task Un_rol_nuevo_editado_y_borrado_deja_una_fila_por_cada_cosa_sin_el_texto_de_la_descripcion()
        {
            int idRol;
            using (var db = Auditado())
            {
                var rol = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, Nombre = "Flota", Descripcion = "TEXTO LIBRE PRIVADO", Activo = true };
                db.EmpresaRoles.Add(rol);
                await db.SaveChangesAsync();
                idRol = rol.IdRol;
            }
            using (var db = Auditado())
            {
                var rol = db.EmpresaRoles.Single(r => r.IdRol == idRol);
                rol.Nombre = "Flota y taller";
                rol.Descripcion = "OTRO TEXTO LIBRE";
                await db.SaveChangesAsync();
            }
            using (var db = Auditado())
            {
                db.EmpresaRoles.Remove(db.EmpresaRoles.Single(r => r.IdRol == idRol));
                await db.SaveChangesAsync();
            }

            var tipos = DelTenant().Select(f => f.Tipo).ToList();
            Assert.Equal(new[] { TiposDeEvento.RolCreado, TiposDeEvento.RolEditado, TiposDeEvento.RolEliminado }, tipos);
            var editado = DelTenant(TiposDeEvento.RolEditado).Single();
            Assert.Equal("Flota", Detalle(editado.Detalle).GetProperty("nombreDelRol")[0].GetString());
            Assert.Equal("Flota y taller", Detalle(editado.Detalle).GetProperty("nombreDelRol")[1].GetString());
            Assert.Equal("Flota y taller", Detalle(editado.Detalle).GetProperty("rol").GetString());
            Assert.True(Detalle(editado.Detalle).GetProperty("descripcionCambiada").GetBoolean());
            Assert.All(DelTenant(), f => Assert.DoesNotContain("TEXTO LIBRE", f.Detalle));
            Assert.All(DelTenant(), f => Assert.Equal(idRol.ToString(), f.ObjetoId));
        }

        [Fact]
        public async Task Cambiar_los_permisos_de_un_rol_deja_una_sola_fila_con_lo_concedido_y_lo_retirado_por_codigo()
        {
            int idRol;
            using (var db = _bd.Crear())
            {
                db.Permisos.AddRange(NuevoPermiso("flota.vehiculos.ver", 1), NuevoPermiso("flota.vehiculos.crear", 2), NuevoPermiso("flota.vehiculos.editar", 3));
                var rol = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, IdTenant = DatosBase.TenantA, Nombre = "Operador", Activo = true };
                db.EmpresaRoles.Add(rol);
                db.SaveChanges();
                idRol = rol.IdRol;
                var ver = db.Permisos.Single(p => p.Codigo == "flota.vehiculos.ver");
                db.RolPermisos.Add(new RolPermiso { IdTenant = DatosBase.TenantA, IdRol = idRol, IdPermiso = ver.IdPermiso, AmbitoPermiso = "modulo", CreadoPor = "pruebas", FechaCreacion = Ahora });
                db.SaveChanges();
            }

            using (var db = Auditado())
            {
                var ver = db.RolPermisos.Single(rp => rp.IdRol == idRol);
                db.RolPermisos.Remove(ver);
                foreach (var codigo in new[] { "flota.vehiculos.editar", "flota.vehiculos.crear" })
                {
                    var permiso = db.Permisos.Single(p => p.Codigo == codigo);
                    db.RolPermisos.Add(new RolPermiso { IdTenant = DatosBase.TenantA, IdRol = idRol, IdPermiso = permiso.IdPermiso, AmbitoPermiso = "modulo", CreadoPor = "ana.admin", FechaCreacion = Ahora });
                }
                await db.SaveChangesAsync();
            }

            var fila = Assert.Single(DelTenant(TiposDeEvento.RolPermisos));
            var detalle = Detalle(fila.Detalle);
            Assert.Equal(new[] { "flota.vehiculos.crear", "flota.vehiculos.editar" }, detalle.GetProperty("concedidos").EnumerateArray().Select(x => x.GetString()).ToArray());
            Assert.Equal(new[] { "flota.vehiculos.ver" }, detalle.GetProperty("retirados").EnumerateArray().Select(x => x.GetString()).ToArray());
            Assert.Equal("Operador", detalle.GetProperty("rol").GetString());
            Assert.Equal(idRol.ToString(), fila.ObjetoId);
            Assert.Equal(DatosBase.TenantA, fila.IdTenant);
            Assert.Equal(DatosBase.EmpresaA, fila.IdEmpresa);
        }

        // ── Módulos, accesos y empresas ─────────────────────────────────────

        [Fact]
        public async Task Activar_desactivar_y_quitar_un_modulo_de_la_empresa_dejan_su_fila_con_el_codigo_del_modulo()
        {
            int id;
            using (var db = Auditado())
            {
                var flota = db.Modulos.Single(m => m.Codigo == "flota");
                var modulo = new EmpresaModulo { IdEmpresa = DatosBase.EmpresaA, IdModulo = flota.IdModulo, FechaActivacion = Ahora, Activo = true };
                db.EmpresaModulos.Add(modulo);
                await db.SaveChangesAsync();
                id = modulo.Id;
            }
            using (var db = Auditado())
            {
                db.EmpresaModulos.Single(m => m.Id == id).Activo = false;
                await db.SaveChangesAsync();
            }
            using (var db = Auditado())
            {
                db.EmpresaModulos.Remove(db.EmpresaModulos.Single(m => m.Id == id));
                await db.SaveChangesAsync();
            }

            var filas = DelTenant(TiposDeEvento.ModuloCambiado);
            Assert.Equal(new[] { "alta", "cambio", "baja" }, filas.Select(f => Detalle(f.Detalle).GetProperty("operacion").GetString()).ToArray());
            Assert.All(filas, f => Assert.Equal("flota", Detalle(f.Detalle).GetProperty("modulo").GetString()));
            Assert.False(Detalle(filas[1].Detalle).GetProperty("activo")[1].GetBoolean());
            Assert.All(filas, f => Assert.Equal(id.ToString(), f.ObjetoId));
        }

        [Fact]
        public async Task Dar_y_cambiar_un_acceso_a_una_empresa_deja_su_fila()
        {
            using (var db = Auditado())
            {
                var cuenta = IdentidadDePrueba.NuevaCuenta(db, "ana@prueba.local");
                var membresia = IdentidadDePrueba.NuevaMembresia(db, cuenta, DatosBase.TenantA, idPersona: null);
                var acceso = IdentidadDePrueba.NuevoAcceso(db, membresia, DatosBase.EmpresaA, rolLegado: "empresa_user");
                acceso.RolLegado = "empresa_admin";
                acceso.Activo = false;
                await db.SaveChangesAsync();
            }

            var filas = DelTenant(TiposDeEvento.AccesoCambiado);
            Assert.Equal(2, filas.Count);
            Assert.Equal("alta", Detalle(filas[0].Detalle).GetProperty("operacion").GetString());
            var cambio = Detalle(filas[1].Detalle);
            Assert.Equal("cambio", cambio.GetProperty("operacion").GetString());
            Assert.Equal("empresa_admin", cambio.GetProperty("rol")[1].GetString());
            Assert.All(filas, f => Assert.Equal(DatosBase.TenantA, f.IdTenant));
        }

        [Fact]
        public async Task Crear_editar_y_dar_de_baja_una_empresa_dejan_su_fila_y_el_RTN_solo_se_dice_que_cambio()
        {
            int idEmpresa;
            using (var db = Auditado())
            {
                var empresa = new Empresa
                {
                    Codigo = "NUEVA", RazonSocial = "Nueva SA", IdentificadorFiscal = "08019999999999", PaisIso = "HN", MonedaIso = "HNL",
                    ZonaHoraria = "America/Tegucigalpa", IdTenant = DatosBase.TenantA, FechaActivacion = Ahora, CreadoPor = "pruebas", FechaCreacion = Ahora,
                };
                db.Empresas.Add(empresa);
                await db.SaveChangesAsync();
                idEmpresa = empresa.IdEmpresa;
            }
            using (var db = Auditado())
            {
                var empresa = db.Empresas.Single(e => e.IdEmpresa == idEmpresa);
                empresa.RazonSocial = "Nueva SA de CV";
                empresa.IdentificadorFiscal = "08018888888888";
                await db.SaveChangesAsync();
            }
            using (var db = Auditado())
            {
                db.Empresas.Single(e => e.IdEmpresa == idEmpresa).Eliminado = true;
                await db.SaveChangesAsync();
            }

            Assert.Equal(new[] { TiposDeEvento.EmpresaCreada, TiposDeEvento.EmpresaEditada, TiposDeEvento.EmpresaEliminada }, DelTenant().Select(f => f.Tipo).ToArray());
            var editada = DelTenant(TiposDeEvento.EmpresaEditada).Single();
            Assert.Equal("Nueva SA de CV", Detalle(editada.Detalle).GetProperty("razonSocial")[1].GetString());
            Assert.True(Detalle(editada.Detalle).GetProperty("identificadorFiscalCambiado").GetBoolean());
            Assert.All(DelTenant(), f => Assert.DoesNotContain("0801", f.Detalle));
            Assert.All(DelTenant(), f => Assert.Equal(idEmpresa, f.IdEmpresa));
        }

        // ── Todo o nada (D73) ───────────────────────────────────────────────

        private sealed class ContextoQueFalla : IContextoAuditoria
        {
            public string Usuario => "ana.admin";
            public int? IdEmpresa => DatosBase.EmpresaA;
            public string Origen => "app";
            public string? Ip => throw new InvalidOperationException("La auditoría no está disponible.");
        }

        [Fact]
        public async Task Si_la_fila_de_auditoria_no_se_puede_armar_el_usuario_nuevo_tampoco_se_guarda()
        {
            using (var db = Auditado(new ContextoQueFalla()))
            {
                db.Users.Add(NuevoUsuario("no.debe.quedar"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            }

            using var lectura = _bd.Crear();
            Assert.DoesNotContain(lectura.Users, u => u.Username == "no.debe.quedar");
            Assert.Empty(DelTenant());
            Assert.Empty(DeLaPlataforma());
        }

        [Fact]
        public async Task Si_la_fila_de_auditoria_no_se_puede_armar_la_edicion_tampoco_se_guarda()
        {
            var id = await CrearUsuarioAsync();
            var antes = DelTenant().Count;

            using (var db = Auditado(new ContextoQueFalla()))
            {
                db.Users.Single(u => u.Id == id).IsActive = false;
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            }

            using var lectura = _bd.Crear();
            Assert.True(lectura.Users.Single(u => u.Id == id).IsActive);
            Assert.Equal(antes, DelTenant().Count);
        }

        [Fact]
        public async Task Sin_el_constructor_el_interceptor_sigue_igual_y_no_deja_filas_de_seguridad()
        {
            using (var db = _bd.Crear(new AuditoriaCambiosInterceptor(_quien)))
            {
                db.Users.Add(NuevoUsuario("sin.constructor"));
                await db.SaveChangesAsync();
            }

            Assert.Empty(DelTenant());
            Assert.Empty(DeLaPlataforma());
        }
    }
}
