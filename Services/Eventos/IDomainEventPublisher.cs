namespace eGestion360Web.Services.Eventos
{
    /// <summary>
    /// Publica un evento de dominio al outbox.
    ///
    /// IMPORTANTE: el publisher comparte el ApplicationDbContext con el caller (DI Scoped).
    /// El método NO llama SaveChanges; agrega la entidad al change tracker.
    /// El módulo operativo es responsable de llamar SaveChangesAsync una sola vez,
    /// commiteando la operación de negocio y el evento ATÓMICAMENTE.
    ///
    /// Ejemplo:
    ///   _db.Facturas.Add(factura);
    ///   _events.Publish(idEmpresa, "factura.emitida.credito", "factura", factura.IdFactura.ToString(), new { ... });
    ///   await _db.SaveChangesAsync();   // commit atómico
    /// </summary>
    public interface IDomainEventPublisher
    {
        void Publish(
            int idEmpresa,
            string eventType,
            string aggregateType,
            string aggregateId,
            object payload,
            DateTime? occurredAt = null,
            int eventVersion = 1);
    }
}
