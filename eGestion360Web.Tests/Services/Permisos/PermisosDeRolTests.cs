using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Permisos
{
    /// <summary>
    /// La pantalla de roles guarda en el catálogo nuevo (paso F1.6e): solo ofrece lo que se puede dar (administración y módulos activos de la empresa,
    /// nunca plataforma), guarda exactamente lo marcado entre lo ofrecido y le avisa al evaluador para que el cambio rija enseguida. Antes guardaba las
    /// casillas viejas, que desde F1.6d ya no deciden nada. SQLite en memoria; las restricciones de la base las cubre el script 041.
    /// </summary>
    public class PermisosDeRolTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _base = new();
        private readonly MemoryCache _memoria = new(new MemoryCacheOptions());
        private static readonly DateTime Ahora = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

        public PermisosDeRolTests()
        {
            using var db = _base.Crear();
            var orden = 0;
            foreach (var d in CatalogoDePermisos.Todos)
                db.Permisos.Add(new Permiso
                {
                    Codigo = d.Codigo, Modulo = d.Modulo, Recurso = d.Recurso, Accion = d.Accion, Ambito = d.Ambito, AccionLegada = d.CasillaLegada,
                    Descripcion = d.Descripcion, Orden = ++orden, CreadoPor = "pruebas", FechaCreacion = Ahora
                });

            // La empresa A tiene activos flota y catálogos; facturación no.
            foreach (var codigo in new[] { "flota", "catalogos" })
                db.EmpresaModulos.Add(new EmpresaModulo { IdEmpresa = DatosBase.EmpresaA, IdModulo = db.Modulos.Single(m => m.Codigo == codigo).IdModulo, FechaActivacion = Ahora, Activo = true });
            db.SaveChanges();
        }

        public void Dispose()
        {
            _base.Dispose();
            _memoria.Dispose();
        }

        private int NuevoRol(int idEmpresa, string nombre, params string[] codigos)
        {
            using var db = _base.Crear();
            var idTenant = DatosBase.TenantDe(idEmpresa);
            var rol = new EmpresaRol { IdEmpresa = idEmpresa, IdTenant = idTenant, Nombre = nombre, Activo = true };
            db.EmpresaRoles.Add(rol);
            db.SaveChanges();
            foreach (var codigo in codigos)
            {
                var permiso = db.Permisos.Single(p => p.Codigo == codigo);
                db.RolPermisos.Add(new RolPermiso { IdTenant = idTenant, IdRol = rol.IdRol, IdPermiso = permiso.IdPermiso, AmbitoPermiso = permiso.Ambito, CreadoPor = "pruebas", FechaCreacion = Ahora });
            }
            db.SaveChanges();
            return rol.IdRol;
        }

        private HashSet<string> Guardados(int idRol)
        {
            using var db = _base.Crear();
            return db.RolPermisos.Where(rp => rp.IdRol == idRol).Join(db.Permisos, rp => rp.IdPermiso, p => p.IdPermiso, (rp, p) => p.Codigo).ToHashSet();
        }

        private PermisosDeRol Servicio(ApplicationDbContext db) => new(db, new EvaluadorDePermisos(db, _memoria));

        [Fact]
        public async Task Ofrece_la_administracion_y_solo_los_modulos_activos_de_la_empresa_y_nunca_la_plataforma()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var datos = await Servicio(db).ObtenerAsync(rol);

            Assert.NotNull(datos);
            var codigos = datos!.Grupos.SelectMany(g => g.Recursos).SelectMany(r => r.Permisos).Select(p => p.Codigo).ToList();
            Assert.Equal(new[] { "administracion", "catalogos", "flota" }, datos.Grupos.Select(g => g.Codigo).OrderBy(c => c, StringComparer.Ordinal));
            Assert.True(datos.Grupos.Single(g => g.Codigo == "administracion").EsAdministracion);
            Assert.DoesNotContain(codigos, c => c.StartsWith("plataforma."));
            Assert.DoesNotContain(codigos, c => c.StartsWith("facturacion."));
            Assert.Contains("flota.vehiculos.eliminar", codigos);
            Assert.Contains("administracion.roles.editar", codigos);
        }

        [Fact]
        public async Task La_auditoria_de_la_empresa_no_se_ofrece_ni_se_guarda_para_un_rol()
        {
            // F1.8c: la auditoría del tenant la ve solo su administrador (decisión del 2026-11-21).
            var rol = NuevoRol(DatosBase.EmpresaA, "Supervisor");
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var servicio = Servicio(db);

            var codigos = (await servicio.ObtenerAsync(rol))!.Grupos.SelectMany(g => g.Recursos).SelectMany(r => r.Permisos).Select(p => p.Codigo).ToList();
            Assert.DoesNotContain("administracion.auditoria.ver", codigos);
            Assert.Contains("administracion.usuarios.ver", codigos);

            var resultado = await servicio.GuardarAsync(rol, new[] { "administracion.auditoria.ver", "administracion.usuarios.ver" }, "emilio");

            Assert.Equal(new ResultadoDeGuardado(Agregados: 1, Quitados: 0, Ignorados: 1), resultado);
            Assert.Equal(new[] { "administracion.usuarios.ver" }, Guardados(rol));
        }

        [Fact]
        public async Task Marca_lo_que_el_rol_ya_tiene_y_nada_mas()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Consulta", "flota.vehiculos.ver", "catalogos.clientes.ver");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var datos = await Servicio(db).ObtenerAsync(rol);

            var marcados = datos!.Grupos.SelectMany(g => g.Recursos).SelectMany(r => r.Permisos).Where(p => p.Concedido).Select(p => p.Codigo).OrderBy(c => c, StringComparer.Ordinal);
            Assert.Equal(new[] { "catalogos.clientes.ver", "flota.vehiculos.ver" }, marcados);
        }

        [Fact]
        public async Task Las_columnas_de_un_grupo_van_en_orden_ver_crear_editar_eliminar_y_despues_las_propias()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var flota = (await Servicio(db).ObtenerAsync(rol))!.Grupos.Single(g => g.Codigo == "flota");

            Assert.Equal(new[] { "ver", "crear", "editar", "eliminar", "anular" }, flota.Acciones);
        }

        [Fact]
        public async Task Guardar_agrega_y_quita_lo_marcado_y_conserva_lo_que_no_se_ofrece()
        {
            // El rol conserva un permiso de facturación (módulo que la empresa no tiene activo): no se ofrece, no se toca.
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador", "flota.vehiculos.ver", "flota.vehiculos.eliminar", "facturacion.factura.ver");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var resultado = await Servicio(db).GuardarAsync(rol, new[] { "flota.vehiculos.ver", "flota.vehiculos.crear" }, "emilio");

            Assert.Equal(new ResultadoDeGuardado(Agregados: 1, Quitados: 1, Ignorados: 0), resultado);
            Assert.Equal(new[] { "facturacion.factura.ver", "flota.vehiculos.crear", "flota.vehiculos.ver" }, Guardados(rol).OrderBy(c => c, StringComparer.Ordinal));
        }

        [Fact]
        public async Task Guardar_ignora_los_permisos_de_plataforma_de_un_modulo_inactivo_y_los_que_no_existen()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var resultado = await Servicio(db).GuardarAsync(rol, new[] { "plataforma.empresas.eliminar", "facturacion.factura.anular", "no.existe.nada", "flota.kpi.ver" }, "emilio");

            Assert.Equal(new ResultadoDeGuardado(Agregados: 1, Quitados: 0, Ignorados: 3), resultado);
            Assert.Equal(new[] { "flota.kpi.ver" }, Guardados(rol));
        }

        [Fact]
        public async Task Guardar_dos_veces_lo_mismo_no_cambia_nada_la_segunda()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador");
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var servicio = Servicio(db);

            await servicio.GuardarAsync(rol, new[] { "flota.vehiculos.ver", "catalogos.clientes.ver", "administracion.invitaciones.ver" }, "emilio");
            var segunda = await servicio.GuardarAsync(rol, new[] { "flota.vehiculos.ver", "catalogos.clientes.ver", "administracion.invitaciones.ver" }, "emilio");

            Assert.False(segunda!.HuboCambios);
            Assert.Equal(3, Guardados(rol).Count);
        }

        [Fact]
        public async Task Guardar_sin_marcar_nada_quita_todo_lo_que_se_ofrece()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador", "flota.vehiculos.ver", "catalogos.clientes.editar", "administracion.roles.ver");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var resultado = await Servicio(db).GuardarAsync(rol, Array.Empty<string>(), "emilio");

            Assert.Equal(3, resultado!.Quitados);
            Assert.Empty(Guardados(rol));
        }

        [Fact]
        public async Task Guardar_deja_el_rastro_de_quien_concedio_cada_permiso_y_su_ambito()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador");
            await using var db = _base.CrearComo(DatosBase.TenantA);

            await Servicio(db).GuardarAsync(rol, new[] { "administracion.usuarios.ver", "flota.vehiculos.ver" }, "emilio");

            using var lectura = _base.Crear();
            var filas = lectura.RolPermisos.Where(rp => rp.IdRol == rol).Join(lectura.Permisos, rp => rp.IdPermiso, p => p.IdPermiso, (rp, p) => new { p.Codigo, rp.AmbitoPermiso, rp.CreadoPor, rp.IdTenant }).ToList();
            Assert.All(filas, f => Assert.Equal("emilio", f.CreadoPor));
            Assert.All(filas, f => Assert.Equal(DatosBase.TenantA, f.IdTenant));
            Assert.Equal(AmbitoDePermiso.Empresa, filas.Single(f => f.Codigo == "administracion.usuarios.ver").AmbitoPermiso);
            Assert.Equal(AmbitoDePermiso.Modulo, filas.Single(f => f.Codigo == "flota.vehiculos.ver").AmbitoPermiso);
        }

        [Fact]
        public async Task El_cambio_rige_enseguida_porque_se_avisa_al_evaluador()
        {
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador");
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var evaluador = new EvaluadorDePermisos(db, _memoria);
            var servicio = new PermisosDeRol(db, evaluador);
            var contexto = new ContextoDePermisos(false, DatosBase.TenantA, rol, AuthHelper.EmpresaUserRole, new HashSet<string> { "flota" });

            Assert.False((await evaluador.EvaluarAsync(contexto)).Puede("flota.vehiculos.eliminar"));   // y queda en la memoria de 30 s

            await servicio.GuardarAsync(rol, new[] { "flota.vehiculos.eliminar" }, "emilio");
            Assert.True((await evaluador.EvaluarAsync(contexto)).Puede("flota.vehiculos.eliminar"));

            await servicio.GuardarAsync(rol, Array.Empty<string>(), "emilio");
            Assert.False((await evaluador.EvaluarAsync(contexto)).Puede("flota.vehiculos.eliminar"));
        }

        [Fact]
        public async Task Un_rol_de_otro_tenant_no_existe_para_quien_pregunta()
        {
            var deOtroTenant = NuevoRol(DatosBase.EmpresaB, "Rol del tenant B", "flota.vehiculos.ver");
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var servicio = Servicio(db);

            Assert.Null(await servicio.ObtenerAsync(deOtroTenant));
            Assert.Null(await servicio.GuardarAsync(deOtroTenant, new[] { "flota.vehiculos.eliminar" }, "emilio"));
            Assert.Equal(new[] { "flota.vehiculos.ver" }, Guardados(deOtroTenant));
        }

        [Fact]
        public async Task Un_rol_que_no_existe_devuelve_nulo()
        {
            await using var db = _base.CrearComo(DatosBase.TenantA);

            Assert.Null(await Servicio(db).ObtenerAsync(987654));
            Assert.Null(await Servicio(db).GuardarAsync(987654, new[] { "flota.vehiculos.ver" }, "emilio"));
        }

        [Fact]
        public async Task La_marca_es_admin_del_rol_se_informa_pero_no_cambia_lo_que_se_ofrece()
        {
            int rol;
            using (var db = _base.Crear())
            {
                var nuevo = new EmpresaRol { IdEmpresa = DatosBase.EmpresaA, IdTenant = DatosBase.TenantA, Nombre = "Administrador", EsAdmin = true, Activo = true };
                db.EmpresaRoles.Add(nuevo);
                db.SaveChanges();
                rol = nuevo.IdRol;
            }
            await using var contexto = _base.CrearComo(DatosBase.TenantA);

            var datos = await Servicio(contexto).ObtenerAsync(rol);

            Assert.True(datos!.MarcaEsAdmin);
            Assert.Equal(3, datos.Grupos.Count);
        }
    }
}
