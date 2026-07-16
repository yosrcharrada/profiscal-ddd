using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Profiscal.Application.Common.Interfaces;

namespace Profiscal.Infrastructure.Services;

/// <summary>
/// SMTP delivery for credential emails. Configure via .env / appsettings:
///   Smtp:Host, Smtp:Port (587), Smtp:User, Smtp:Password, Smtp:From, Smtp:FromName, Smtp:EnableSsl.
/// When Smtp:Host is empty the service reports IsConfigured=false and the API
/// returns the generated password to the admin instead of silently losing it.
///
/// Smtp:CopyCredentialsTo (optional) — one or more admin addresses (comma/semicolon
/// separated) that receive a BCC copy of EVERY credentials email, so an administrator
/// always gets the new account's email + temporary password regardless of who the new
/// user is. BCC (not To/CC) so the new user never sees the admin copy. Empty = no copy.
///
/// Smtp:OverrideTo (optional, TESTING) — when set, EVERY credentials email is delivered
/// to this address instead of the new user's real inbox (the body still shows the real
/// account email). Lets an admin verify the email flow without spamming real users.
/// </summary>
public class SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger) : IEmailService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Smtp:Host"]);

    public async Task<bool> SendCredentialsAsync(
        string toEmail, string fullName, string role, string temporaryPassword,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("SMTP not configured (Smtp:Host empty) — credentials email to {Email} skipped.", toEmail);
            return false;
        }

        var host       = config["Smtp:Host"]!;
        var port       = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
        var user       = config["Smtp:User"];
        var password   = config["Smtp:Password"];
        var from       = config["Smtp:From"] ?? user ?? "no-reply@tn.ey.com";
        var fromName   = config["Smtp:FromName"] ?? "EY Taxmind";
        var enableSsl  = !string.Equals(config["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
        var appUrl     = config["App:Url"] ?? "http://localhost:3000";

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = string.IsNullOrWhiteSpace(user)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(user, password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(from, fromName),
                Subject = "Your EY Taxmind access is ready",
                Body = BuildBody(fullName, toEmail, role, temporaryPassword, appUrl),
                IsBodyHtml = true
            };

            // Testing hook: redirect delivery while keeping the real account email in the body.
            var overrideTo = config["Smtp:OverrideTo"];
            var deliverTo  = string.IsNullOrWhiteSpace(overrideTo) ? toEmail : overrideTo.Trim();
            message.To.Add(deliverTo);
            if (deliverTo != toEmail)
                logger.LogInformation("Smtp:OverrideTo active — credentials email for {Email} delivered to {Override}.", toEmail, deliverTo);

            // Optional admin copy: BCC every credentials email to the configured address(es), so an
            // administrator always receives the new account's email + password. BCC keeps it invisible
            // to the new user. Accepts a comma/semicolon-separated list.
            var copyTo = config["Smtp:CopyCredentialsTo"];
            if (!string.IsNullOrWhiteSpace(copyTo))
                foreach (var addr in copyTo.Split(new[] { ',', ';' },
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    try { message.Bcc.Add(addr); }
                    catch (FormatException) { logger.LogWarning("Smtp:CopyCredentialsTo has an invalid address '{Addr}' — skipped.", addr); }

            await client.SendMailAsync(message, ct);
            logger.LogInformation("Credentials email sent to {Email}{Copy}.", toEmail,
                string.IsNullOrWhiteSpace(copyTo) ? "" : $" (admin copy → {copyTo})");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send credentials email to {Email}.", toEmail);
            return false;
        }
    }

    private static string BuildBody(string fullName, string email, string role, string password, string appUrl) => $$"""
        <div style="margin:0;padding:32px 16px;background:#f5f5f0;font-family:Segoe UI,Arial,sans-serif;">
          <div style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #e6e3de;border-radius:12px;overflow:hidden;">
            <div style="background:#2e2e38;padding:20px 28px;">
              <div style="border-left:4px solid #FFE600;padding-left:12px;">
                <span style="color:#ffffff;font-size:18px;font-weight:700;letter-spacing:.04em;">EY</span>
                <span style="color:#FFE600;font-size:12px;font-weight:600;letter-spacing:.22em;margin-left:8px;">TAXMIND</span>
              </div>
            </div>
            <div style="padding:28px;">
              <h2 style="margin:0 0 6px;color:#2e2e38;font-size:18px;">Welcome, {{fullName}}</h2>
              <p style="margin:0 0 18px;color:#5c5c6e;font-size:13px;line-height:1.6;">
                An administrator created your <strong>{{role}}</strong> account on the EY Taxmind
                fiscal intelligence platform. Sign in with the credentials below —
                you'll be asked to choose your own password on first login.
              </p>
              <div style="background:#fffdf0;border:1px solid #f0e8b0;border-radius:10px;padding:16px 18px;margin-bottom:18px;">
                <p style="margin:0 0 8px;font-size:12px;color:#5c5c6e;">Email</p>
                <p style="margin:0 0 14px;font-size:14px;color:#2e2e38;font-weight:600;">{{email}}</p>
                <p style="margin:0 0 8px;font-size:12px;color:#5c5c6e;">Temporary password</p>
                <p style="margin:0;font-size:16px;color:#2e2e38;font-weight:700;font-family:Consolas,monospace;letter-spacing:.06em;">{{password}}</p>
              </div>
              <a href="{{appUrl}}/login"
                 style="display:inline-block;background:#FFE600;color:#2e2e38;text-decoration:none;font-weight:700;font-size:13px;padding:11px 22px;border-radius:8px;">
                Sign in to Taxmind
              </a>
              <p style="margin:22px 0 0;color:#9c9cab;font-size:11px;line-height:1.6;">
                This message was sent automatically — please don't reply.
                If you weren't expecting this account, contact your administrator.
              </p>
            </div>
          </div>
        </div>
        """;
}
