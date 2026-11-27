using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Services.Permisos;

namespace eGestion360Web.Tests.Arquitectura
{
    /// <summary>
    /// F1.8b, D76: toda pantalla de administración o de plataforma que escribe deja su rastro de auditoría de seguridad, o está en una lista
    /// revisada de excepciones con su motivo. Una pantalla nueva que escribe y no está en ninguna de las dos listas hace fallar esta prueba: hay
    /// que decidir, a propósito, si audita (y cómo) o por qué no hace falta.
    ///
    /// «Administración o plataforma» es: una pantalla cuyo permiso (en la clase o en un handler) empieza por <c>administracion.</c> o
    /// <c>plataforma.</c>; cualquiera de las carpetas <c>Pages/Admin</c> y <c>Pages/Empresas</c>; y las dos pantallas de las tasas oficiales
    /// donde actúa el operador (configurar y ejecutar el job, aprobar y rechazar). «Escribe» es tener un handler <c>OnPost*</c>, <c>OnPut*</c> o
    /// <c>OnDelete*</c>.
    ///
    /// Cómo se audita: casi todo lo hace <see cref="AuditoriaCambiosInterceptor"/> en el mismo guardado y la misma transacción, mirando las
    /// entidades de administración (usuarios, roles, permisos de un rol, módulos, accesos, empresas); el resto lo hace la pantalla o el servicio
    /// llamando a <see cref="IAuditoriaDeSeguridad"/>. La lista dice cuál es cada caso; la prueba comprueba que la pantalla o el servicio
    /// declarados de verdad mencionan el servicio de auditoría.
    /// </summary>
    public class CoberturaDeAuditoriaTests
    {
        private const string EspacioDePaginas = "eGestion360Web.Pages";

        private enum Como
        {
            /// <summary>Lo deja el interceptor al guardar las entidades de administración: no hace falta nada en la pantalla.</summary>
            Interceptor,

            /// <summary>La pantalla llama a <see cref="IAuditoriaDeSeguridad"/> (el archivo de la pantalla lo menciona).</summary>
            Pantalla,

            /// <summary>Un servicio que usa la pantalla llama a <see cref="IAuditoriaDeSeguridad"/> (el archivo del servicio lo menciona).</summary>
            Servicio,

            /// <summary>La bitácora de cambios por campo, que ya existía, cubre lo que la pantalla escribe.</summary>
            Bitacora,
        }

        private sealed record Cobertura(Como Como, string Detalle, string? ArchivoDelServicio = null);

        /// <summary>Pantallas que escriben y dejan su rastro, con el nombre completo de su clase.</summary>
        private static readonly IReadOnlyDictionary<string, Cobertura> Auditadas = new Dictionary<string, Cobertura>(StringComparer.Ordinal)
        {
            // Usuarios: User pasa por el interceptor (la clave solo se dice que cambió; la persona, además, por la bitácora de la persona).
            ["eGestion360Web.Pages.Admin.Usuarios.CreateModel"] = new(Como.Interceptor, "usuario.creado (y la invitación, si la hay)"),
            ["eGestion360Web.Pages.Admin.Usuarios.EditModel"] = new(Como.Interceptor, "usuario.editado con el antes y el después; vincular o quitar la persona queda también en su bitácora"),
            ["eGestion360Web.Pages.Admin.ResetPasswordModel"] = new(Como.Interceptor, "usuario.editado con claveCambiada (el administrador restablece la clave de otro usuario)"),
            ["eGestion360Web.Pages.UserManagementModel"] = new(Como.Interceptor, "usuario.editado con claveCambiada"),

            // Roles y permisos de la empresa
            ["eGestion360Web.Pages.Admin.RolesPermisos.IndexModel"] = new(Como.Interceptor, "rol.creado, rol.editado y rol.permisos"),
            ["eGestion360Web.Pages.Admin.EmpresaRoles.IndexModel"] = new(Como.Interceptor, "rol.creado y rol.editado (activar o desactivar)"),
            ["eGestion360Web.Pages.Admin.EmpresaRoles.PermisosModel"] = new(Como.Interceptor, "rol.permisos con lo concedido y lo retirado, por código"),

            // Módulos, empresas y accesos
            ["eGestion360Web.Pages.Admin.EmpresaModulos.IndexModel"] = new(Como.Interceptor, "modulo.cambiado (activar, desactivar o quitar un módulo de la empresa)"),
            ["eGestion360Web.Pages.Admin.Modulos.IndexModel"] = new(Como.Interceptor, "modulo.cambiado del catálogo global, solo en la auditoría de la plataforma"),
            ["eGestion360Web.Pages.Empresas.CreateModel"] = new(Como.Interceptor, "empresa.creada (y los roles y módulos que se siembran)"),
            ["eGestion360Web.Pages.Empresas.EditModel"] = new(Como.Interceptor, "empresa.editada con el antes y el después; el RTN solo dice que cambió"),
            ["eGestion360Web.Pages.Empresas.DeleteModel"] = new(Como.Interceptor, "empresa.eliminada"),

            // Consola del operador (F1.9a)
            ["eGestion360Web.Pages.Admin.Suscriptores.DetalleModel"] = new(Como.Interceptor, "tenant.estado y tenant.plan, con el motivo, en las dos tablas"),

            // Invitaciones: las manda y las anula el servicio
            ["eGestion360Web.Pages.Admin.Invitaciones.CreateModel"] = new(Como.Servicio, "invitacion.enviada (y usuario.creado por el interceptor)", "Services/Identidad/ServicioDeInvitaciones.cs"),
            ["eGestion360Web.Pages.Admin.Invitaciones.IndexModel"] = new(Como.Servicio, "invitacion.enviada (reenvío) e invitacion.anulada", "Services/Identidad/ServicioDeInvitaciones.cs"),

            // Soporte y acciones del operador sobre la plataforma: las pantallas llaman al servicio
            ["eGestion360Web.Pages.Admin.Soporte.IndexModel"] = new(Como.Pantalla, "soporte.entrada (estricto: sin rastro no se entra) y soporte.salida, en las dos tablas"),
            ["eGestion360Web.Pages.Admin.EmailConfigModel"] = new(Como.Pantalla, "correo.configurado (alta, edición, predeterminado, activar o desactivar y eliminar; nunca la contraseña)"),
            ["eGestion360Web.Pages.Admin.Outbox.DetailsModel"] = new(Como.Pantalla, "cola.reintento y cola.procesado_manual"),
            ["eGestion360Web.Pages.Admin.Outbox.IndexModel"] = new(Como.Pantalla, "cola.reintento (reactivar los muertos)"),
            ["eGestion360Web.Pages.Catalogos.TiposCambio.ConfiguracionModel"] = new(Como.Pantalla, "tasas.configurada"),
            ["eGestion360Web.Pages.Catalogos.TiposCambio.IndexModel"] = new(Como.Pantalla, "tasas.oficial_aprobada, tasas.oficial_rechazada y tasas.ejecutada; el registro manual de una tasa es un dato del catálogo, con su creado por"),

            // Datos de la persona: la bitácora por campo que ya existía
            ["eGestion360Web.Pages.Admin.Personas.EditModel"] = new(Como.Bitacora, "bitácora de cambios por campo de la persona (script 014), con la cuenta y la correlación desde el 045"),
        };

        /// <summary>Pantallas que escriben pero no cambian nada que haya que auditar, con el motivo.</summary>
        private static readonly IReadOnlyDictionary<string, string> Excepciones = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eGestion360Web.Pages.ValidarEmailsModel"] =
                "Pantalla de diagnóstico del operador: envía correos de prueba y valida un perfil, pero no guarda ni cambia nada en la base.",
        };

        private static string Raiz()
        {
            var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
            while (carpeta != null && !File.Exists(Path.Combine(carpeta.FullName, "eGestion360Web.csproj")))
                carpeta = carpeta.Parent;

            Assert.True(carpeta != null, "No se encontró eGestion360Web.csproj subiendo desde " + AppContext.BaseDirectory);
            return carpeta!.FullName;
        }

        private static readonly string[] PrefijosDeAdministracion = { "administracion.", "plataforma." };

        private static readonly string[] TasasDelOperador =
        {
            "eGestion360Web.Pages.Catalogos.TiposCambio.IndexModel",
            "eGestion360Web.Pages.Catalogos.TiposCambio.ConfiguracionModel",
        };

        private static IEnumerable<MethodInfo> Handlers(Type tipo) => tipo
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("OnPost", StringComparison.Ordinal)
                        || m.Name.StartsWith("OnPut", StringComparison.Ordinal)
                        || m.Name.StartsWith("OnDelete", StringComparison.Ordinal));

        private static bool EsDeAdministracionOPlataforma(Type tipo)
        {
            var codigos = new List<string>();
            if (tipo.GetCustomAttribute<RequierePermisoAttribute>(inherit: false) is { } deLaClase) codigos.Add(deLaClase.Codigo);
            codigos.AddRange(tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => m.GetCustomAttribute<RequierePermisoAttribute>(inherit: false)?.Codigo)
                .Where(c => c != null)!);

            var ns = tipo.Namespace ?? string.Empty;
            return codigos.Any(c => PrefijosDeAdministracion.Any(p => c.StartsWith(p, StringComparison.Ordinal)))
                   || ns == EspacioDePaginas + ".Admin" || ns.StartsWith(EspacioDePaginas + ".Admin.", StringComparison.Ordinal)
                   || ns == EspacioDePaginas + ".Empresas"
                   || TasasDelOperador.Contains(tipo.FullName);
        }

        /// <summary>Las pantallas de administración o plataforma que tienen al menos un handler que escribe.</summary>
        private static IReadOnlyList<Type> PantallasQueEscriben()
            => typeof(PermisosPageFilter).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(PageModel).IsAssignableFrom(t) && t.Namespace != null
                            && (t.Namespace == EspacioDePaginas || t.Namespace.StartsWith(EspacioDePaginas + ".", StringComparison.Ordinal))
                            && Handlers(t).Any() && EsDeAdministracionOPlataforma(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

        private static string ArchivoDe(Type pantalla)
        {
            var carpeta = pantalla.Namespace![EspacioDePaginas.Length..].TrimStart('.').Replace('.', Path.DirectorySeparatorChar);
            var nombre = pantalla.Name.EndsWith("Model", StringComparison.Ordinal) ? pantalla.Name[..^"Model".Length] : pantalla.Name;
            return Path.Combine(Raiz(), "Pages", carpeta, nombre + ".cshtml.cs");
        }

        [Fact]
        public void Se_encuentran_las_pantallas_de_administracion_y_plataforma_que_escriben()
        {
            // Si la reflexión dejara de verlas, las demás pruebas pasarían en vacío: este piso lo impide.
            Assert.True(PantallasQueEscriben().Count >= 20, $"Se esperaban al menos 20 pantallas y se encontraron {PantallasQueEscriben().Count}.");
        }

        [Fact]
        public void Toda_pantalla_de_administracion_o_plataforma_que_escribe_esta_auditada_o_es_una_excepcion_revisada()
        {
            var sinClasificar = PantallasQueEscriben()
                .Where(t => !Auditadas.ContainsKey(t.FullName!) && !Excepciones.ContainsKey(t.FullName!))
                .Select(t => t.FullName + " (" + string.Join(", ", Handlers(t).Select(h => h.Name)) + ")")
                .ToList();

            Assert.True(sinClasificar.Count == 0,
                "Estas pantallas escriben y no están ni en Auditadas ni en Excepciones (D76): decide si dejan su rastro de auditoría o por qué no hace falta.\n  "
                + string.Join("\n  ", sinClasificar));
        }

        [Fact]
        public void Las_listas_no_se_cruzan_no_traen_pantallas_que_ya_no_existen_y_las_excepciones_dicen_su_motivo()
        {
            var encontradas = PantallasQueEscriben().Select(t => t.FullName!).ToHashSet(StringComparer.Ordinal);

            Assert.Empty(Auditadas.Keys.Intersect(Excepciones.Keys));
            var obsoletas = Auditadas.Keys.Concat(Excepciones.Keys).Where(n => !encontradas.Contains(n)).ToList();
            Assert.True(obsoletas.Count == 0, "Entradas que ya no son una pantalla de administración o plataforma que escribe: " + string.Join(", ", obsoletas));
            Assert.All(Excepciones, e => Assert.True(e.Value.Trim().Length >= 20, $"La excepción {e.Key} necesita un motivo claro."));
            Assert.All(Auditadas, a => Assert.False(string.IsNullOrWhiteSpace(a.Value.Detalle), $"{a.Key} necesita decir qué deja."));
        }

        [Fact]
        public void Las_pantallas_y_servicios_que_dicen_llamar_al_servicio_de_auditoria_de_verdad_lo_mencionan()
        {
            foreach (var (nombre, cobertura) in Auditadas)
            {
                var tipo = PantallasQueEscriben().Single(t => t.FullName == nombre);
                string archivo;
                switch (cobertura.Como)
                {
                    case Como.Pantalla:
                        archivo = ArchivoDe(tipo);
                        break;
                    case Como.Servicio:
                        Assert.False(string.IsNullOrEmpty(cobertura.ArchivoDelServicio), $"{nombre} dice «Servicio» y no indica el archivo del servicio.");
                        archivo = Path.Combine(Raiz(), cobertura.ArchivoDelServicio!.Replace('/', Path.DirectorySeparatorChar));
                        break;
                    default:
                        continue;
                }

                Assert.True(File.Exists(archivo), $"No existe {archivo} ({nombre}).");
                var texto = File.ReadAllText(archivo);
                Assert.True(texto.Contains("IAuditoriaDeSeguridad", StringComparison.Ordinal) || texto.Contains("RegistrarAccionDeLaPlataformaAsync", StringComparison.Ordinal),
                    $"{nombre} dice que deja su rastro llamando a la auditoría, pero {Path.GetFileName(archivo)} no la menciona.");
            }
        }
    }
}
