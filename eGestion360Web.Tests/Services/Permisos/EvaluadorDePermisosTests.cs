using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using eGestion360Web.Data;
using eGestion360Web.Models;
using eGestion360Web.Models.Plataforma;
using eGestion360Web.Services;
using eGestion360Web.Services.Identidad;
using eGestion360Web.Services.Permisos;
using eGestion360Web.Tests.Infra;

namespace eGestion360Web.Tests.Services.Permisos
{
    /// <summary>
    /// Permisos efectivos (paso F1.6a, script 041, ADR-029): las reglas del evaluador, la equivalencia con el sistema anterior en los tres
    /// roles de prueba, el aislamiento entre tenants y la memoria de 30 segundos. SQLite en memoria; las restricciones de la base las cubren
    /// el script 041 y su POSTCHECK.
    /// </summary>
    public class EvaluadorDePermisosTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _base = new();
        private readonly MemoryCache _memoria = new(new MemoryCacheOptions());

        public void Dispose()
        {
            _base.Dispose();
            _memoria.Dispose();
        }

        private static readonly DateTime Ahora = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        private EvaluadorDePermisos Evaluador(ApplicationDbContext db) => new(db, _memoria);

        private static IReadOnlySet<string> Habilitados(params string[] modulos) => new HashSet<string>(modulos, StringComparer.Ordinal);

        private static ContextoDePermisos ComoUsuario(int idTenant, int idRol, params string[] modulos)
            => new(EsOperador: false, idTenant, idRol, AuthHelper.EmpresaUserRole, Habilitados(modulos));

        /// <summary>El catálogo completo, tal como lo siembra el script 041.</summary>
        private void SembrarCatalogo()
        {
            using var db = _base.Crear();
            var orden = 0;
            foreach (var d in CatalogoDePermisos.Todos)
                db.Permisos.Add(new Permiso
                {
                    Codigo = d.Codigo, Modulo = d.Modulo, Recurso = d.Recurso, Accion = d.Accion, Ambito = d.Ambito, AccionLegada = d.CasillaLegada,
                    Descripcion = d.Descripcion, Orden = ++orden, CreadoPor = "pruebas", FechaCreacion = Ahora
                });
            db.SaveChanges();
        }

        /// <summary>Un rol de empresa con las casillas de antes en <c>empresa_rol_permisos</c> y, migradas con la regla del script 041, en <c>rol_permisos</c>.</summary>
        private int NuevoRol(int idEmpresa, string nombre, params (string Modulo, bool Ver, bool Crear, bool Editar, bool Eliminar)[] casillas)
        {
            using var db = _base.Crear();
            var idTenant = DatosBase.TenantDe(idEmpresa);
            var rol = new EmpresaRol { IdEmpresa = idEmpresa, IdTenant = idTenant, Nombre = nombre, Activo = true };
            db.EmpresaRoles.Add(rol);
            db.SaveChanges();

            foreach (var (modulo, ver, crear, editar, eliminar) in casillas)
            {
                db.EmpresaRolPermisos.Add(new EmpresaRolPermiso
                {
                    IdRol = rol.IdRol, IdTenant = idTenant, IdModulo = db.Modulos.Single(m => m.Codigo == modulo).IdModulo,
                    PuedeVer = ver, PuedeCrear = crear, PuedeEditar = editar, PuedeEliminar = eliminar
                });

                var concedidas = new List<string>();
                if (ver) concedidas.Add(CasillaLegada.Ver);
                if (crear) concedidas.Add(CasillaLegada.Crear);
                if (editar) concedidas.Add(CasillaLegada.Editar);
                if (eliminar) concedidas.Add(CasillaLegada.Eliminar);
                foreach (var casilla in concedidas)
                    foreach (var p in CatalogoDePermisos.DeCasillaLegada(modulo, casilla))
                        Conceder(db, rol.IdRol, idTenant, p.Codigo);
            }
            db.SaveChanges();
            return rol.IdRol;
        }

        private static void Conceder(ApplicationDbContext db, int idRol, int idTenant, string codigo)
        {
            var permiso = db.Permisos.Single(p => p.Codigo == codigo);
            db.RolPermisos.Add(new RolPermiso
            {
                IdTenant = idTenant, IdRol = idRol, IdPermiso = permiso.IdPermiso, AmbitoPermiso = permiso.Ambito, CreadoPor = "pruebas", FechaCreacion = Ahora
            });
        }

        // ── Las reglas ─────────────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task El_operador_tiene_todos_los_permisos_del_catalogo_aunque_no_haya_tenant()
        {
            SembrarCatalogo();
            await using var db = _base.CrearComo(idTenant: null, plataforma: true);

            var permisos = await Evaluador(db).EvaluarAsync(new ContextoDePermisos(true, null, null, AuthHelper.AdminRole, Habilitados()));

            Assert.Equal(CatalogoDePermisos.Todos.Count, permisos.Codigos.Count);
            Assert.True(permisos.Puede("plataforma.soporte.entrar"));
            Assert.True(permisos.Puede("flota.vehiculos.eliminar"));
            Assert.True(permisos.Puede("administracion.roles.editar"));
        }

        [Fact]
        public async Task El_administrador_de_empresa_tiene_la_administracion_y_los_modulos_habilitados_pero_nunca_la_plataforma()
        {
            SembrarCatalogo();
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var permisos = await Evaluador(db).EvaluarAsync(
                new ContextoDePermisos(false, DatosBase.TenantA, null, AuthHelper.EmpresaAdminRole, Habilitados("flota")));

            Assert.True(permisos.Puede("administracion.usuarios.crear"));
            Assert.True(permisos.Puede("flota.vehiculos.eliminar"));
            Assert.True(permisos.Puede("flota.salidas.anular"));
            Assert.False(permisos.Puede("facturacion.factura.anular"));   // su empresa no tiene facturación
            Assert.False(permisos.Puede("catalogos.tasas_cambio.configurar"));
            Assert.DoesNotContain(permisos.Codigos, c => c.StartsWith("plataforma.", StringComparison.Ordinal));
        }

        [Fact]
        public async Task El_rol_de_texto_se_compara_sin_distinguir_mayusculas()
        {
            SembrarCatalogo();
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var permisos = await Evaluador(db).EvaluarAsync(new ContextoDePermisos(false, DatosBase.TenantA, null, "EMPRESA_ADMIN", Habilitados("flota")));

            Assert.True(permisos.Puede("flota.vehiculos.crear"));
        }

        [Fact]
        public async Task Un_usuario_con_rol_solo_tiene_lo_que_su_rol_concede()
        {
            SembrarCatalogo();
            var consulta = NuevoRol(DatosBase.EmpresaA, "Consulta", ("flota", true, false, false, false));
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var permisos = await Evaluador(db).EvaluarAsync(ComoUsuario(DatosBase.TenantA, consulta, "flota"));

            Assert.True(permisos.Puede("flota.vehiculos.ver"));
            Assert.True(permisos.Puede("flota.salarios.ver"));
            Assert.False(permisos.Puede("flota.vehiculos.crear"));
            Assert.False(permisos.Puede("flota.vehiculos.eliminar"));
            Assert.False(permisos.Puede("administracion.usuarios.ver"));    // la administración no se concede por ser de flota
            Assert.False(permisos.Puede("plataforma.soporte.ver"));
        }

        [Fact]
        public async Task Un_usuario_sin_rol_o_sin_tenant_no_tiene_permisos()
        {
            SembrarCatalogo();
            var consulta = NuevoRol(DatosBase.EmpresaA, "Consulta", ("flota", true, false, false, false));
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var evaluador = Evaluador(db);

            Assert.Empty((await evaluador.EvaluarAsync(new ContextoDePermisos(false, DatosBase.TenantA, null, AuthHelper.EmpresaUserRole, Habilitados("flota")))).Codigos);
            Assert.Empty((await evaluador.EvaluarAsync(new ContextoDePermisos(false, null, consulta, AuthHelper.EmpresaUserRole, Habilitados("flota")))).Codigos);
            Assert.Empty((await evaluador.EvaluarAsync(new ContextoDePermisos(false, null, null, AuthHelper.EmpresaAdminRole, Habilitados("flota")))).Codigos);
        }

        [Fact]
        public async Task Los_permisos_de_un_modulo_solo_valen_si_la_empresa_puede_usar_el_modulo()
        {
            SembrarCatalogo();
            var rol = NuevoRol(DatosBase.EmpresaA, "Mixto", ("flota", true, true, true, false), ("catalogos", true, true, true, false));
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var evaluador = Evaluador(db);

            var conAmbos = await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota", "catalogos"));
            Assert.True(conAmbos.Puede("catalogos.clientes.crear"));
            Assert.True(conAmbos.Puede("flota.vehiculos.crear"));

            var soloFlota = await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"));
            Assert.False(soloFlota.Puede("catalogos.clientes.crear"));    // el plan o la activación no incluyen catálogos
            Assert.True(soloFlota.Puede("flota.vehiculos.crear"));
        }

        [Fact]
        public async Task Un_permiso_inactivo_del_catalogo_no_se_concede_a_nadie()
        {
            SembrarCatalogo();
            var rol = NuevoRol(DatosBase.EmpresaA, "Operador", ("flota", true, true, true, false));
            using (var edicion = _base.Crear())
            {
                edicion.Permisos.Single(p => p.Codigo == "flota.peajes.editar").Activo = false;
                edicion.SaveChanges();
            }
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var permisos = await Evaluador(db).EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"));

            Assert.False(permisos.Puede("flota.peajes.editar"));
            Assert.True(permisos.Puede("flota.peajes.crear"));
        }

        [Fact]
        public async Task Un_rol_nunca_recibe_permisos_de_plataforma_ni_aunque_una_fila_lo_diga()
        {
            SembrarCatalogo();
            var rol = NuevoRol(DatosBase.EmpresaA, "Consulta", ("flota", true, false, false, false));
            using (var edicion = _base.Crear())
            {
                // La base real lo impide con CK_rol_permisos_ambito; si algún día se saltara, el evaluador tampoco lo concede.
                Conceder(edicion, rol, DatosBase.TenantA, "plataforma.soporte.entrar");
                edicion.SaveChanges();
            }
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var permisos = await Evaluador(db).EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"));

            Assert.False(permisos.Puede("plataforma.soporte.entrar"));
            Assert.True(permisos.Puede("flota.vehiculos.ver"));
        }

        [Fact]
        public async Task Puede_alguno_acepta_si_tiene_cualquiera_de_los_permisos()
        {
            SembrarCatalogo();
            var rol = NuevoRol(DatosBase.EmpresaA, "Consulta", ("flota", true, false, false, false));
            await using var db = _base.CrearComo(DatosBase.TenantA);

            var permisos = await Evaluador(db).EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"));

            Assert.True(permisos.PuedeAlguno("flota.vehiculos.eliminar", "flota.vehiculos.ver"));
            Assert.False(permisos.PuedeAlguno("flota.vehiculos.eliminar", "flota.vehiculos.crear"));
            Assert.False(PermisosEfectivos.Ninguno.PuedeAlguno("flota.vehiculos.ver"));
        }

        // ── Equivalencia con el sistema anterior (los tres roles de prueba de Transgar) ──────────────────────────────────

        /// <summary>
        /// Lo que decidía el sistema anterior para este rol: se carga con el <see cref="CargadorDePermisos"/> de verdad, se copia a la sesión
        /// como lo hace <c>SesionDesdeIdentidadMiddleware</c> y se pregunta con <c>AuthHelper.PuedeVer</c> y compañía.
        /// </summary>
        private async Task<HttpContext> ContextoDeAntes(string rolDeTexto, int idEmpresa, int? idRol)
        {
            await using var db = _base.CrearComo(DatosBase.TenantDe(idEmpresa));
            var cargados = await new CargadorDePermisos(db).CargarAsync(idEmpresa, idRol, rolDeTexto);

            var http = SesionFalsa.Contexto(rol: rolDeTexto, empresaId: idEmpresa.ToString(), idTenant: DatosBase.TenantDe(idEmpresa));
            http.Session.SetString("Modulos", JsonSerializer.Serialize(cargados.Modulos.Select(m => m.ToLowerInvariant()).ToHashSet()));
            http.Session.SetString("Permisos", JsonSerializer.Serialize(cargados.Permisos));
            return http;
        }

        private void ActivarModulos(int idEmpresa, params string[] modulos)
        {
            using var db = _base.Crear();
            foreach (var codigo in modulos)
                db.EmpresaModulos.Add(new EmpresaModulo
                {
                    IdEmpresa = idEmpresa, IdModulo = db.Modulos.Single(m => m.Codigo == codigo).IdModulo, FechaActivacion = Ahora, Activo = true
                });
            db.SaveChanges();
        }

        [Theory]
        [InlineData("Administrador de flota", true, true, true, true)]
        [InlineData("Operador de flota", true, true, true, false)]
        [InlineData("Consulta", true, false, false, false)]
        [InlineData("Solo crear", false, true, false, false)]
        [InlineData("Sin casillas", false, false, false, false)]
        public async Task Cada_casilla_de_antes_da_exactamente_los_permisos_nuevos_que_le_corresponden(
            string nombre, bool ver, bool crear, bool editar, bool eliminar)
        {
            SembrarCatalogo();
            ActivarModulos(DatosBase.EmpresaA, "flota", "catalogos", "facturacion");
            var rol = NuevoRol(DatosBase.EmpresaA, nombre,
                ("flota", ver, crear, editar, eliminar), ("catalogos", ver, crear, editar, eliminar), ("facturacion", ver, crear, editar, eliminar));

            var antes = await ContextoDeAntes(AuthHelper.EmpresaUserRole, DatosBase.EmpresaA, rol);
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var ahora = await Evaluador(db).EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota", "catalogos", "facturacion"));

            var casillas = new (string Casilla, Func<HttpContext, string, bool> Antes)[]
            {
                (CasillaLegada.Ver, AuthHelper.PuedeVer), (CasillaLegada.Crear, AuthHelper.PuedeCrear),
                (CasillaLegada.Editar, AuthHelper.PuedeEditar), (CasillaLegada.Eliminar, AuthHelper.PuedeEliminar)
            };

            var comparados = 0;
            foreach (var modulo in CatalogoDePermisos.ModulosConPermisos)
                foreach (var (casilla, preguntar) in casillas)
                {
                    var antesDecia = preguntar(antes, modulo);
                    foreach (var p in CatalogoDePermisos.DeCasillaLegada(modulo, casilla))
                    {
                        Assert.True(antesDecia == ahora.Puede(p.Codigo),
                            $"{p.Codigo}: antes ({casilla} de {modulo}) = {antesDecia}, ahora = {ahora.Puede(p.Codigo)}");
                        comparados++;
                    }
                }
            Assert.True(comparados >= 40, "Debe comparar los permisos con casilla legada de los tres módulos.");

            // Los permisos sin casilla legada son nuevos (aprobar o configurar tasas…): un rol de empresa no los recibe por migración.
            foreach (var p in CatalogoDePermisos.Todos.Where(p => p.Ambito == AmbitoDePermiso.Modulo && p.CasillaLegada == null))
                Assert.False(ahora.Puede(p.Codigo), p.Codigo);
        }

        [Fact]
        public async Task El_administrador_de_empresa_y_el_operador_dan_lo_mismo_que_antes()
        {
            SembrarCatalogo();
            ActivarModulos(DatosBase.EmpresaA, "flota", "catalogos");
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var evaluador = Evaluador(db);

            // Administrador de empresa: antes, todo lo de los módulos que tiene.
            var antesAdmin = await ContextoDeAntes(AuthHelper.EmpresaAdminRole, DatosBase.EmpresaA, null);
            var adminAhora = await evaluador.EvaluarAsync(new ContextoDePermisos(false, DatosBase.TenantA, null, AuthHelper.EmpresaAdminRole, Habilitados("flota", "catalogos")));
            foreach (var p in CatalogoDePermisos.Todos.Where(p => p.Ambito == AmbitoDePermiso.Modulo && p.CasillaLegada != null))
            {
                var anterior = p.CasillaLegada switch
                {
                    CasillaLegada.Ver => AuthHelper.PuedeVer(antesAdmin, p.Modulo),
                    CasillaLegada.Crear => AuthHelper.PuedeCrear(antesAdmin, p.Modulo),
                    CasillaLegada.Editar => AuthHelper.PuedeEditar(antesAdmin, p.Modulo),
                    _ => AuthHelper.PuedeEliminar(antesAdmin, p.Modulo)
                };
                Assert.True(anterior == adminAhora.Puede(p.Codigo), p.Codigo);
            }

            // Operador: antes, cualquier guarda pasaba (IsAdmin); ahora, todo el catálogo.
            var antesOperador = SesionFalsa.Contexto(rol: AuthHelper.AdminRole);
            Assert.True(AuthHelper.PuedeEliminar(antesOperador, "facturacion"));
            var operadorAhora = await evaluador.EvaluarAsync(new ContextoDePermisos(true, null, null, AuthHelper.AdminRole, Habilitados()));
            Assert.All(CatalogoDePermisos.Todos, p => Assert.True(operadorAhora.Puede(p.Codigo), p.Codigo));
        }

        // ── Aislamiento entre tenants ──────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Los_permisos_de_un_rol_de_otro_tenant_no_se_ven()
        {
            SembrarCatalogo();
            var rolA = NuevoRol(DatosBase.EmpresaA, "Operador A", ("flota", true, true, true, false));
            var rolB = NuevoRol(DatosBase.EmpresaB, "Operador B", ("flota", true, false, false, false));

            // Con el contexto del tenant A: su rol responde; el de B no (el filtro de EF y el tenant de la pregunta lo impiden).
            await using (var dbA = _base.CrearComo(DatosBase.TenantA))
            {
                var evaluador = Evaluador(dbA);
                Assert.True((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rolA, "flota"))).Puede("flota.vehiculos.crear"));
                Assert.Empty((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rolB, "flota"))).Codigos);
                // La petición es de A: preguntar por B no lee nada ni deja un vacío en la memoria que luego le niegue sus permisos a B (ver abajo).
                Assert.Empty((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Codigos);
            }

            // Con el contexto del tenant B pasa lo simétrico.
            await using (var dbB = _base.CrearComo(DatosBase.TenantB))
            {
                var evaluador = Evaluador(dbB);
                Assert.True((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Puede("flota.vehiculos.ver"));
                Assert.False((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Puede("flota.vehiculos.crear"));
                Assert.Empty((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolA, "flota"))).Codigos);
            }
        }

        [Fact]
        public async Task Preguntar_por_un_tenant_que_no_es_el_de_la_peticion_no_deja_un_vacio_en_la_memoria()
        {
            SembrarCatalogo();
            var rolB = NuevoRol(DatosBase.EmpresaB, "Operador B", ("flota", true, true, true, false));

            // Una petición del tenant A pregunta (por error) por un rol de B: no recibe nada...
            await using (var dbA = _base.CrearComo(DatosBase.TenantA))
                Assert.Empty((await Evaluador(dbA).EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Codigos);

            // ...y esa respuesta vacía no se guardó: la gente de B, en su propia petición y con la misma memoria, conserva sus permisos.
            await using var dbB = _base.CrearComo(DatosBase.TenantB);
            var deB = await Evaluador(dbB).EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"));
            Assert.True(deB.Puede("flota.vehiculos.crear"));
        }

        [Fact]
        public async Task La_memoria_no_mezcla_tenants_aunque_dos_roles_se_parezcan()
        {
            SembrarCatalogo();
            var rolA = NuevoRol(DatosBase.EmpresaA, "Operador", ("flota", true, true, true, false));
            var rolB = NuevoRol(DatosBase.EmpresaB, "Operador", ("flota", true, false, false, false));

            await using var dbA = _base.CrearComo(DatosBase.TenantA);
            await using var dbB = _base.CrearComo(DatosBase.TenantB);
            var deA = await Evaluador(dbA).EvaluarAsync(ComoUsuario(DatosBase.TenantA, rolA, "flota"));
            var deB = await Evaluador(dbB).EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"));
            var deAOtraVez = await Evaluador(dbA).EvaluarAsync(ComoUsuario(DatosBase.TenantA, rolA, "flota"));   // ahora desde la memoria

            Assert.True(deA.Puede("flota.vehiculos.crear"));
            Assert.False(deB.Puede("flota.vehiculos.crear"));
            Assert.True(deAOtraVez.Puede("flota.vehiculos.crear"));
        }

        // ── La memoria de 30 segundos ──────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void La_memoria_dura_30_segundos_como_los_derechos_de_uso()
        {
            Assert.Equal(30, EvaluadorDePermisos.SegundosEnMemoria);
            Assert.Equal(eGestion360Web.Services.Derechos.DerechosDeUso.SegundosEnMemoria, EvaluadorDePermisos.SegundosEnMemoria);
        }

        [Fact]
        public async Task Un_cambio_de_rol_se_nota_al_invalidar_el_tenant_y_no_antes()
        {
            SembrarCatalogo();
            var rol = NuevoRol(DatosBase.EmpresaA, "Consulta", ("flota", true, false, false, false));
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var evaluador = Evaluador(db);

            Assert.False((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"))).Puede("flota.vehiculos.crear"));

            using (var edicion = _base.Crear())
            {
                Conceder(edicion, rol, DatosBase.TenantA, "flota.vehiculos.crear");
                edicion.SaveChanges();
            }

            // Sigue la copia de memoria hasta que alguien invalide.
            Assert.False((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"))).Puede("flota.vehiculos.crear"));

            evaluador.Invalidar(DatosBase.TenantA);
            Assert.True((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantA, rol, "flota"))).Puede("flota.vehiculos.crear"));
        }

        [Fact]
        public async Task Invalidar_un_tenant_no_borra_la_memoria_de_otro()
        {
            SembrarCatalogo();
            var rolB = NuevoRol(DatosBase.EmpresaB, "Consulta", ("flota", true, false, false, false));
            await using var dbB = _base.CrearComo(DatosBase.TenantB);
            var evaluador = Evaluador(dbB);

            Assert.False((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Puede("flota.vehiculos.crear"));
            using (var edicion = _base.Crear())
            {
                Conceder(edicion, rolB, DatosBase.TenantB, "flota.vehiculos.crear");
                edicion.SaveChanges();
            }

            evaluador.Invalidar(DatosBase.TenantA);   // el otro tenant
            Assert.False((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Puede("flota.vehiculos.crear"));

            evaluador.Invalidar(DatosBase.TenantB);
            Assert.True((await evaluador.EvaluarAsync(ComoUsuario(DatosBase.TenantB, rolB, "flota"))).Puede("flota.vehiculos.crear"));
        }

        [Fact]
        public async Task Un_permiso_nuevo_del_catalogo_se_nota_al_invalidar_el_catalogo()
        {
            SembrarCatalogo();
            await using var db = _base.CrearComo(DatosBase.TenantA);
            var evaluador = Evaluador(db);
            var contexto = new ContextoDePermisos(false, DatosBase.TenantA, null, AuthHelper.EmpresaAdminRole, Habilitados("flota"));

            Assert.False((await evaluador.EvaluarAsync(contexto)).Puede("flota.reportes.ver"));

            using (var edicion = _base.Crear())
            {
                edicion.Permisos.Add(new Permiso
                {
                    Codigo = "flota.reportes.ver", Modulo = "flota", Recurso = "reportes", Accion = "ver", Ambito = AmbitoDePermiso.Modulo,
                    AccionLegada = CasillaLegada.Ver, Descripcion = "Ver reportes", Orden = 999, CreadoPor = "pruebas", FechaCreacion = Ahora
                });
                edicion.SaveChanges();
            }

            Assert.False((await evaluador.EvaluarAsync(contexto)).Puede("flota.reportes.ver"));
            evaluador.InvalidarCatalogo();
            Assert.True((await evaluador.EvaluarAsync(contexto)).Puede("flota.reportes.ver"));
        }
    }
}
