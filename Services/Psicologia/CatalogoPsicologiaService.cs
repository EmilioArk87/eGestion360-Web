using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Psicologia;
using eGestion360Web.Services.Personas;

namespace eGestion360Web.Services.Psicologia
{
    public sealed class CatalogoPsicologiaService : ICatalogoPsicologiaService
    {
        private static readonly Regex CodigoValido = new("^[A-Z0-9][A-Z0-9_-]{0,29}$", RegexOptions.CultureInvariant);

        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _tiempo;
        private readonly ILogger<CatalogoPsicologiaService> _log;

        public CatalogoPsicologiaService(ApplicationDbContext db, TimeProvider tiempo, ILogger<CatalogoPsicologiaService> log)
        {
            _db = db;
            _tiempo = tiempo;
            _log = log;
        }

        private DateTime Ahora() => _tiempo.GetUtcNow().UtcDateTime;

        public async Task<ResultadoPsicologia<int>> GuardarPruebaAsync(ElementoCatalogoInput input, CancellationToken ct = default)
        {
            var (codigo, nombre, descripcion, errores) = Validar(input, conDescripcion: true);
            if (errores.Count > 0) return ResultadoPsicologia<int>.ConErrores(errores);

            var id = input.Id ?? 0;
            if (await _db.PruebasPsicologicas.AnyAsync(p => p.IdEmpresa == input.IdEmpresa && p.Codigo == codigo && p.IdPrueba != id, ct))
                return ResultadoPsicologia<int>.Error("Codigo", $"Ya hay una prueba con el código {codigo}.");

            Prueba prueba;
            if (id > 0)
            {
                var existente = await _db.PruebasPsicologicas.FirstOrDefaultAsync(p => p.IdPrueba == id && p.IdEmpresa == input.IdEmpresa, ct);
                if (existente == null) return ResultadoPsicologia<int>.Error(string.Empty, "La prueba no existe en esta empresa.");
                prueba = existente;
                prueba.ModificadoPor = input.Usuario;
                prueba.FechaModificacion = Ahora();
            }
            else
            {
                prueba = new Prueba { IdEmpresa = input.IdEmpresa, Activo = true, CreadoPor = input.Usuario, FechaCreacion = Ahora() };
                _db.PruebasPsicologicas.Add(prueba);
            }
            prueba.Codigo = codigo;
            prueba.Nombre = nombre;
            prueba.Descripcion = descripcion;

            return await GuardarAsync(() => prueba.IdPrueba, "la prueba", input.IdEmpresa, ct);
        }

        public async Task<ResultadoPsicologia<int>> GuardarTipoConstanciaAsync(ElementoCatalogoInput input, CancellationToken ct = default)
        {
            var (codigo, nombre, _, errores) = Validar(input, conDescripcion: false);
            if (codigo.Length > NumeradorDeConstancias.LargoMaximoCodigo && !errores.Any(e => e.Campo == "Codigo"))
                errores.Add(new ErrorValidacion("Codigo",
                    $"El código del tipo es el prefijo de la numeración de sus constancias: hasta {NumeradorDeConstancias.LargoMaximoCodigo} caracteres."));
            if (errores.Count > 0) return ResultadoPsicologia<int>.ConErrores(errores);

            var id = input.Id ?? 0;
            if (await _db.TiposConstancia.AnyAsync(t => t.IdEmpresa == input.IdEmpresa && t.Codigo == codigo && t.IdTipoConstancia != id, ct))
                return ResultadoPsicologia<int>.Error("Codigo", $"Ya hay un tipo de constancia con el código {codigo}.");

            TipoConstancia tipo;
            if (id > 0)
            {
                var existente = await _db.TiposConstancia.FirstOrDefaultAsync(t => t.IdTipoConstancia == id && t.IdEmpresa == input.IdEmpresa, ct);
                if (existente == null) return ResultadoPsicologia<int>.Error(string.Empty, "El tipo de constancia no existe en esta empresa.");
                if (existente.Codigo != codigo && await _db.Constancias.AnyAsync(c => c.IdTipoConstancia == id && c.IdEmpresa == input.IdEmpresa, ct))
                    return ResultadoPsicologia<int>.Error("Codigo",
                        $"El código no se puede cambiar: ya hay constancias numeradas con {existente.Codigo}. El nombre sí se puede cambiar.");
                tipo = existente;
                tipo.ModificadoPor = input.Usuario;
                tipo.FechaModificacion = Ahora();
            }
            else
            {
                tipo = new TipoConstancia { IdEmpresa = input.IdEmpresa, Activo = true, CreadoPor = input.Usuario, FechaCreacion = Ahora() };
                _db.TiposConstancia.Add(tipo);
            }
            tipo.Codigo = codigo;
            tipo.Nombre = nombre;

            return await GuardarAsync(() => tipo.IdTipoConstancia, "el tipo de constancia", input.IdEmpresa, ct);
        }

        public async Task<ResultadoPsicologia<bool>> CambiarEstadoPruebaAsync(int idEmpresa, int idPrueba, bool activo, string usuario, CancellationToken ct = default)
        {
            var prueba = await _db.PruebasPsicologicas.FirstOrDefaultAsync(p => p.IdPrueba == idPrueba && p.IdEmpresa == idEmpresa, ct);
            if (prueba == null) return ResultadoPsicologia<bool>.Error(string.Empty, "La prueba no existe en esta empresa.");
            if (prueba.Activo == activo) return ResultadoPsicologia<bool>.Exito(activo);

            prueba.Activo = activo;
            prueba.ModificadoPor = usuario;
            prueba.FechaModificacion = Ahora();
            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<bool>.Exito(activo);
        }

        public async Task<ResultadoPsicologia<bool>> CambiarEstadoTipoConstanciaAsync(int idEmpresa, int idTipo, bool activo, string usuario, CancellationToken ct = default)
        {
            var tipo = await _db.TiposConstancia.FirstOrDefaultAsync(t => t.IdTipoConstancia == idTipo && t.IdEmpresa == idEmpresa, ct);
            if (tipo == null) return ResultadoPsicologia<bool>.Error(string.Empty, "El tipo de constancia no existe en esta empresa.");
            if (tipo.Activo == activo) return ResultadoPsicologia<bool>.Exito(activo);

            tipo.Activo = activo;
            tipo.ModificadoPor = usuario;
            tipo.FechaModificacion = Ahora();
            await _db.SaveChangesAsync(ct);
            return ResultadoPsicologia<bool>.Exito(activo);
        }

        /// <summary>El código en mayúsculas, sin tildes ni espacios (los espacios pasan a guion): "mmpi 2" → "MMPI-2".</summary>
        public static string NormalizarCodigo(string? codigo)
        {
            var limpio = NombresPersona.Limpiar(codigo).ToUpperInvariant().Replace(' ', '-');
            var sinTildes = new StringBuilder(limpio.Length);
            foreach (var c in limpio.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sinTildes.Append(c);
            return sinTildes.ToString().Normalize(NormalizationForm.FormC);
        }

        private static (string Codigo, string Nombre, string? Descripcion, List<ErrorValidacion> Errores) Validar(ElementoCatalogoInput input, bool conDescripcion)
        {
            ArgumentNullException.ThrowIfNull(input);
            var errores = new List<ErrorValidacion>();
            if (input.IdEmpresa <= 0) errores.Add(new ErrorValidacion(string.Empty, "Entra a una empresa para editar los catálogos."));

            var codigo = NormalizarCodigo(input.Codigo);
            if (codigo.Length == 0) errores.Add(new ErrorValidacion("Codigo", "Escribe el código."));
            else if (!CodigoValido.IsMatch(codigo))
                errores.Add(new ErrorValidacion("Codigo", "El código admite hasta 30 letras, números, guion o guion bajo."));

            var nombre = NombresPersona.Limpiar(input.Nombre);
            if (nombre.Length == 0) errores.Add(new ErrorValidacion("Nombre", "Escribe el nombre."));
            else if (nombre.Length > 150) errores.Add(new ErrorValidacion("Nombre", "El nombre no puede pasar de 150 caracteres."));

            string? descripcion = null;
            if (conDescripcion)
            {
                descripcion = NombresPersona.Limpiar(input.Descripcion);
                if (descripcion.Length > 500) errores.Add(new ErrorValidacion("Descripcion", "La descripción no puede pasar de 500 caracteres."));
                if (descripcion.Length == 0) descripcion = null;
            }
            return (codigo, nombre, descripcion, errores);
        }

        private async Task<ResultadoPsicologia<int>> GuardarAsync(Func<int> id, string que, int idEmpresa, CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return ResultadoPsicologia<int>.Exito(id());
            }
            catch (DbUpdateException ex)
            {
                // Lo normal es el código repetido por otro usuario al mismo tiempo (UX_*_codigo).
                _log.LogWarning(ex, "No se pudo guardar {Que} en la empresa {IdEmpresa}", que, idEmpresa);
                _db.ChangeTracker.Clear();
                return ResultadoPsicologia<int>.Error("Codigo", $"No se pudo guardar {que}: revisa que el código no esté repetido e intenta de nuevo.");
            }
        }
    }
}
