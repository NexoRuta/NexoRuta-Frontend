namespace NexoRuta.Backoffice.Features.Messaging;
public sealed record FormularioReintento(Guid MensajeId, Guid SolicitudId, int Intentos, int Pagina, string Motivo, bool Seleccionado, bool Incierto);
