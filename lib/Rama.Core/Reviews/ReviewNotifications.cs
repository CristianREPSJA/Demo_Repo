using Rama.Core.Notifications;

namespace Rama.Core.Reviews;

public static class ReviewNotifications
{
    public static NotificationMessage For(ReviewListItem item) => item.Kind == ReviewKind.Deployment
        ? new(item.Key, "Despliegue pendiente", $"{item.Title} espera tu aprobación")
        : new(item.Key, "Revisión pendiente", $"{item.Author} te pidió revisar el PR {item.Number}", item.Pr?.WebUrl);
}
