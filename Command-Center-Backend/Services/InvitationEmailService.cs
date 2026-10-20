using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Dixon.CommandCenter.API.Services;

public sealed class InvitationEmailService(IConfiguration configuration)
{
    public async Task SendInvitationAsync(
        string recipientEmail,
        string recipientName,
        string setupUrl,
        CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(
                configuration["Email:From"]
                    ?? throw new InvalidOperationException("Email:From is missing."),
                configuration["Email:DisplayName"] ?? "Dixon Digital Command Center"),
            Subject = "Set up your Dixon Command Center account",
            Body = $"Hello {recipientName},\n\n" +
                   "Your Dixon Digital Command Center account has been created.\n\n" +
                   "Use this link to set your password:\n" + setupUrl + "\n\n" +
                   "If you were not expecting this invitation, contact your administrator.",
            IsBodyHtml = false
        };

        message.To.Add(new MailAddress(recipientEmail));
        using var smtp = CreateSmtpClient();

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message, cancellationToken);
    }

    public async Task SendPasswordResetAsync(
        string recipientEmail,
        string recipientName,
        string resetUrl,
        CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(
                configuration["Email:From"]
                    ?? throw new InvalidOperationException("Email:From is missing."),
                configuration["Email:DisplayName"] ?? "Dixon Digital Command Center"),
            Subject = "Reset Your Dixon Command Center Password",
            IsBodyHtml = true,
            Body = $"""
                <div style="font-family:Arial,sans-serif;max-width:600px;margin:auto">
                  <h2>Password Reset Request</h2>
                  <p>Hello {WebUtility.HtmlEncode(recipientName)},</p>
                  <p>We received a request to reset your Dixon Command Center password.</p>
                  <p>
                    <a href="{WebUtility.HtmlEncode(resetUrl)}"
                       style="background:#f97316;color:white;padding:12px 20px;
                       text-decoration:none;border-radius:6px;display:inline-block">
                      Reset Password
                    </a>
                  </p>
                  <p>This link expires in 30 minutes and can only be used once.</p>
                  <p>If you did not request this, you can ignore this email.</p>
                </div>
                """
        };

        message.To.Add(new MailAddress(recipientEmail));
        using var smtp = CreateSmtpClient();

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message, cancellationToken);
    }

    private SmtpClient CreateSmtpClient()
    {
        var host = configuration["Email:SmtpHost"]
            ?? throw new InvalidOperationException("Email:SmtpHost is missing.");
        var port = int.Parse(configuration["Email:SmtpPort"] ?? "587");
        var username = configuration["Email:Username"]
            ?? throw new InvalidOperationException("Email:Username is missing.");
        var password = configuration["Email:Password"]
            ?? throw new InvalidOperationException("Email:Password is missing.");

        return new SmtpClient(host, port)
        {
            EnableSsl = bool.TryParse(configuration["Email:EnableSsl"], out var ssl)
                ? ssl
                : true,
            Credentials = new NetworkCredential(username, password)
        };
    }
}
