using API.Schema.NotificationsContext.NotificationConnectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Newtonsoft.Json;

namespace API.Schema.NotificationsContext;

public class NotificationsContext(DbContextOptions<NotificationsContext> options) : MangetteBaseContext<NotificationsContext>(options)
{
    public DbSet<NotificationConnector> NotificationConnectors { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ValueComparer<Dictionary<string, string>> headersComparer = new(
            (a, b) => JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b),
            v => JsonConvert.SerializeObject(v).GetHashCode(),
            v => JsonConvert.DeserializeObject<Dictionary<string, string>>(JsonConvert.SerializeObject(v)) ?? new());

        modelBuilder.Entity<NotificationConnector>()
            .Property(n => n.Headers)
            .HasConversion(
                v => JsonConvert.SerializeObject(v),
                v => JsonConvert.DeserializeObject<Dictionary<string, string>>(v) ?? new Dictionary<string, string>())
            .Metadata.SetValueComparer(headersComparer);
    }
}