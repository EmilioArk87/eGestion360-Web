using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using eGestion360Web.Data;
using eGestion360Web.Models.Eventos;
using eGestion360Web.Services.Auditoria;
using eGestion360Web.Tests.Infra;
using DetallesDeLaCola = eGestion360Web.Pages.Admin.Outbox.DetailsModel;
using IndiceDeLaCola = eGestion360Web.Pages.Admin.Outbox.IndexModel;

namespace eGestion360Web.Tests.Paginas
{
    /// <summary>
    /// F1.8b: reintentar, marcar a mano o reactivar los eventos muertos de la cola es una acción del operador sobre la plataforma y deja su
    /// rastro (<c>cola.reintento</c> y <c>cola.procesado_manual</c>), de mejor esfuerzo y solo cuando la acción se hizo.
    /// </summary>
    public class ColaDeEventosAuditoriaTests : IDisposable
    {
        private readonly BaseDeDatosDePrueba _bd = new();
        private readonly AuditoriaEspia _auditoria = new();

        public void Dispose() => _bd.Dispose();

        private sealed class TempDataEnMemoria : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private long NuevoEvento(string estado = DomainEventStatus.Dead)
        {
            using var db = _bd.Crear();
            var evento = new DomainEvent
            {
                IdEmpresa = DatosBase.EmpresaA, IdTenant = DatosBase.TenantA, EventType = "prueba.evento", AggregateType = "prueba", AggregateId = "1",
                Status = estado, Intentos = 5, MaxIntentos = 5,
            };
            db.DomainEvents.Add(evento);
            db.SaveChanges();
            return evento.IdEvento;
        }

        private DetallesDeLaCola Detalles(ApplicationDbContext db, bool autenticado = true)
        {
            var http = SesionFalsa.Contexto("admin", autenticado: autenticado);
            return new DetallesDeLaCola(db, _auditoria)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
            };
        }

        private IndiceDeLaCola Indice(ApplicationDbContext db)
        {
            var http = SesionFalsa.Contexto("admin");
            return new IndiceDeLaCola(db, _auditoria)
            {
                PageContext = new PageContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
            };
        }

        [Fact]
        public async Task Reencolar_un_evento_deja_el_evento_de_cola_con_su_id()
        {
            var id = NuevoEvento();
            using var db = _bd.Crear();

            await Detalles(db).OnPostRetryAsync(id);

            var evento = Assert.Single(_auditoria.Eventos);
            Assert.Equal(TiposDeEvento.ColaReintento, evento.Tipo);
            Assert.Equal("evento_de_dominio", evento.ObjetoTipo);
            Assert.Equal(id.ToString(), evento.ObjetoId);
            using var lectura = _bd.Crear();
            Assert.Equal(DomainEventStatus.Pending, lectura.DomainEvents.Single(e => e.IdEvento == id).Status);
        }

        [Fact]
        public async Task Marcar_un_evento_como_procesado_a_mano_deja_su_propio_tipo()
        {
            var id = NuevoEvento();
            using var db = _bd.Crear();

            await Detalles(db).OnPostMarkProcessedAsync(id);

            var evento = Assert.Single(_auditoria.Eventos);
            Assert.Equal(TiposDeEvento.ColaProcesadoManual, evento.Tipo);
            Assert.Equal(id.ToString(), evento.ObjetoId);
        }

        [Fact]
        public async Task Reactivar_los_muertos_deja_un_solo_evento_con_cuantos_fueron()
        {
            NuevoEvento();
            NuevoEvento();
            NuevoEvento(DomainEventStatus.Processed);
            using var db = _bd.Crear();

            await Indice(db).OnPostResetDeadAsync();

            var evento = Assert.Single(_auditoria.Eventos);
            Assert.Equal(TiposDeEvento.ColaReintento, evento.Tipo);
            var detalle = System.Text.Json.JsonSerializer.SerializeToElement(evento.Detalle);
            Assert.Equal("reactivar_muertos", detalle.GetProperty("accion").GetString());
            Assert.Equal(2, detalle.GetProperty("cantidad").GetInt32());
        }

        [Fact]
        public async Task Un_evento_que_no_existe_o_sin_sesion_no_deja_rastro()
        {
            using var db = _bd.Crear();

            await Detalles(db).OnPostRetryAsync(987654);
            await Detalles(db).OnPostMarkProcessedAsync(987654);
            await Detalles(db, autenticado: false).OnPostRetryAsync(NuevoEvento());

            Assert.Empty(_auditoria.Eventos);
        }
    }
}
