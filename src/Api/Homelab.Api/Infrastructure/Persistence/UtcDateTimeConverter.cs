using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Homelab.Api;
// SQLite stores the timestamp value but does not restore DateTime.Kind. All
// persisted application timestamps use UTC; restore that contract on reads so
// JSON carries Z and browsers do not reinterpret the value as local time.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
