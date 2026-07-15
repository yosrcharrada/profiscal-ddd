namespace Profiscal.Domain.Entities;

/// <summary>
/// An in-app notification delivered to a single user. Generic on purpose so it
/// serves the JORT feed ("new document") today and the task workflow
/// ("your task was submitted / validated") tomorrow. The frontend bell polls
/// the unread count and rings a beep when it grows.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid    RecipientId { get; set; }
    public AppUser Recipient   { get; set; } = null!;

    /// <summary>Short machine tag used for grouping/icons: "jort", "task", "info".</summary>
    public string Type  { get; set; } = "info";

    public string  Title   { get; set; } = string.Empty;
    public string  Body    { get; set; } = string.Empty;

    /// <summary>Optional in-app route to open when the notification is clicked.</summary>
    public string? LinkUrl { get; set; }

    public bool     IsRead    { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
